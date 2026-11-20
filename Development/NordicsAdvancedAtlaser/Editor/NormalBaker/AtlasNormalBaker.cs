using System;
using System.Collections.Generic;
using System.IO;
using NordicWind.TextureWrapper;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// The Normal Baker's logic, separate from its window: bakes the shading of an original mesh
    /// onto a current (for example lower-detail) mesh as a tangent-space normal map, using the
    /// build's own bake. See NORMAL_BAKER.md.
    /// </summary>
    public static class AtlasNormalBaker
    {
        public const int MaxSize = 4096;
        public const float DefaultMaxAngle = 60f;
        // A bake is refused when the meshes' UV layouts evidently don't match.
        public const float MaxUnmatchedShare = .10f, MaxLimitedShare = .25f;

        /// <summary>What a slot resolves to: a mesh, and the scene object it's on, if any.</summary>
        public sealed class Source
        {
            public Mesh Mesh;
            public Renderer Renderer;   // set only for scene objects
            public string Error;
            public bool InScene => Renderer != null;
            public bool Valid => Mesh != null && Error == null;
        }

        /// <summary>Accepts a mesh asset, or an object (in the scene or a model/prefab asset) with
        /// one Skinned Mesh Renderer or Mesh Renderer, directly or as its only one below it.</summary>
        public static Source Resolve(Object slot)
        {
            if (slot == null) return new Source();
            if (slot is Mesh mesh) return new Source { Mesh = mesh };
            GameObject go = slot as GameObject ?? (slot as Component)?.gameObject;
            if (go == null) return new Source { Error = "Drag in a mesh, or an object with a Skinned Mesh Renderer or Mesh Renderer." };
            var renderer = MeshRendererOf(go);
            if (renderer == null)
            {
                var found = new List<Renderer>();
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    if (MeshOf(r) != null) found.Add(r);
                if (found.Count == 0) return new Source { Error = "'" + go.name + "' has no Skinned Mesh Renderer or Mesh Renderer with a mesh." };
                if (found.Count > 1) return new Source { Error = "'" + go.name + "' has several meshes. Drag in the object with the mesh you want, or the mesh itself." };
                renderer = found[0];
            }
            var resolvedMesh = MeshOf(renderer);
            if (resolvedMesh == null) return new Source { Error = "'" + renderer.name + "' has no mesh." };
            // Objects in a scene are captured with their transform and pose; assets as they are.
            bool inScene = renderer.gameObject.scene.IsValid();
            return new Source { Mesh = resolvedMesh, Renderer = inScene ? renderer : null };
        }
        private static Renderer MeshRendererOf(GameObject go)
        {
            var skinned = go.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null && skinned.sharedMesh != null) return skinned;
            var meshRenderer = go.GetComponent<MeshRenderer>();
            return meshRenderer != null && MeshOf(meshRenderer) != null ? meshRenderer : null;
        }
        private static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            if (renderer is MeshRenderer) return renderer.GetComponent<MeshFilter>()?.sharedMesh;
            return null;
        }

        /// <summary>Submesh names for a dropdown: index, then the material (scene objects) or the triangle count.</summary>
        public static string[] SubmeshLabels(Source source)
        {
            if (source == null || !source.Valid) return Array.Empty<string>();
            var mesh = source.Mesh;
            var materials = source.Renderer != null ? source.Renderer.sharedMaterials : null;
            var labels = new string[mesh.subMeshCount];
            for (int i = 0; i < labels.Length; i++)
            {
                string material = materials != null && i < materials.Length && materials[i] != null ? materials[i].name : null;
                labels[i] = i + ": " + (material ?? (mesh.GetSubMesh(i).indexCount / 3) + " triangles");
            }
            return labels;
        }

        /// <summary>Captures a submesh as a mesh section, in the space the bake uses.</summary>
        public static bool TryCapture(Source source, int submesh, out AtlasMeshSection section, out string error)
        {
            section = null; error = null;
            if (source == null || !source.Valid) { error = source?.Error ?? "Nothing is selected."; return false; }
            var mesh = source.Mesh;
            if (submesh < 0 || submesh >= mesh.subMeshCount) { error = "'" + mesh.name + "' has no submesh " + submesh + "."; return false; }
            int count = mesh.vertexCount;
            var uv = mesh.uv;
            if (uv == null || uv.Length != count) { error = "'" + mesh.name + "' has no UVs."; return false; }
            if (mesh.normals.Length != count) { error = "'" + mesh.name + "' has no normals."; return false; }
            if (mesh.tangents.Length != count) { error = "'" + mesh.name + "' has no tangents. Set Tangents to Import or Calculate in its import settings."; return false; }

            Vector3[] vertices, normals; Vector4[] tangents; Matrix4x4 localToWorld;
            if (source.Renderer is SkinnedMeshRenderer skinned)
            {
                // World space with the current pose, the same way the build captures it.
                AtlasGeometryPose.Capture(skinned, null, out vertices, out normals, out tangents, out _);
                localToWorld = Matrix4x4.identity;
            }
            else
            {
                vertices = mesh.vertices; normals = mesh.normals; tangents = mesh.tangents;
                localToWorld = source.Renderer != null ? source.Renderer.transform.localToWorldMatrix : Matrix4x4.identity;
            }
            section = new AtlasMeshSection {
                RendererPath = source.Renderer != null ? source.Renderer.name : mesh.name, SubmeshIndex = submesh,
                Vertices = vertices, Normals = normals, Tangents = tangents, UV = uv,
                Triangles = mesh.GetTriangles(submesh), LocalToWorld = localToWorld,
                RendererToAvatarRoot = localToWorld, PoseDescription = source.InScene ? "scene object" : "mesh asset" };
            return true;
        }

        /// <summary>A warning when the two slots are oriented differently: a mesh asset counts as
        /// unrotated and unscaled; a scene object uses its transform. Null when they match.</summary>
        public static string OrientationWarning(Source original, Source current)
        {
            if (original == null || current == null || !original.Valid || !current.Valid) return null;
            Quaternion ro = Rotation(original), rc = Rotation(current);
            Vector3 so = Scale(original), sc = Scale(current);
            bool rotated = Quaternion.Angle(ro, rc) > 1f;
            bool scaled = Mathf.Abs(so.x - sc.x) > .01f * Mathf.Abs(so.x) || Mathf.Abs(so.y - sc.y) > .01f * Mathf.Abs(so.y) ||
                          Mathf.Abs(so.z - sc.z) > .01f * Mathf.Abs(so.z);
            if (!rotated && !scaled) return null;
            return "The two meshes are " + (rotated && scaled ? "rotated and scaled" : rotated ? "rotated" : "scaled") +
                   " differently. Shading is compared by direction, so the normal map will be wrong unless they face the same way." +
                   " A mesh asset counts as unrotated and unscaled.";
        }
        private static Quaternion Rotation(Source s) => s.Renderer != null ? s.Renderer.transform.rotation : Quaternion.identity;
        private static Vector3 Scale(Source s) => s.Renderer != null ? s.Renderer.transform.lossyScale : Vector3.one;

        public sealed class Result : IDisposable
        {
            public NordicsTextureWrapper Map;
            public NormalDifferenceStatistics Statistics;
            // Share of the current mesh's texels with no original geometry at the same UV.
            public float UnmatchedShare;
            public bool Identical;
            public bool UVsMatch;
            public string Summary;
            public void Dispose() { Map?.Dispose(); Map = null; }
        }

        /// <summary>Bakes the normal map. The result's map is null when the UVs don't match.</summary>
        public static Result Bake(AtlasMeshSection original, AtlasMeshSection current, int width, int height, float maxAngle = DefaultMaxAngle)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (width < 1 || height < 1 || width > MaxSize || height > MaxSize)
                throw new ArgumentOutOfRangeException(nameof(width), "The size must be between 1 and " + MaxSize + ".");
            var originals = new List<AtlasMeshSection> { original };
            if (AtlasGeometryComparison.SurvivingTrianglesUnchanged(originals, current))
            {
                var flat = new NordicsTextureWrapper(width, height, MapSettings());
                flat.Fill(new Color(0, 0, 1, 1));
                return new Result { Map = flat, Identical = true, UVsMatch = true,
                    Summary = "The current mesh's triangles are unchanged from the original, so the normal map is flat." };
            }
            float unmatched = 0f;
            var map = AtlasSubmeshBaker.BakeCorrection(originals, current, width, height, maxAngle, out var stats,
                maps => unmatched = UnmatchedShare(maps.Original, maps.FrameNormal));
            float limited = stats.Baked > 0 ? stats.Limited / (float)stats.Baked : 0f;
            bool match = unmatched <= MaxUnmatchedShare && limited <= MaxLimitedShare;
            string Share(float v) => (100f * v).ToString("F1") + "%";
            var result = new Result { Statistics = stats, UnmatchedShare = unmatched, UVsMatch = match };
            if (!match)
            {
                map.Dispose();
                result.Summary = "The two meshes' UV layouts don't match (" + Share(unmatched) + " of the current mesh has no " +
                    "original geometry at the same UV, and " + Share(limited) + " of the correction is above " + maxAngle +
                    "°). The baker compares the meshes by UV, so the current mesh must keep the original's UV layout. Nothing was saved.";
                return result;
            }
            result.Map = map;
            result.Summary = "Baked " + stats.Baked + " texels. No correction where UVs overlap (" +
                Share(stats.Baked > 0 ? stats.Overlapping / (float)stats.Baked : 0f) + "); faded where the surfaces don't line up (" +
                Share(stats.Baked > 0 ? stats.Kept / (float)stats.Baked : 0f) + "); compressed beyond " + maxAngle + "° (" +
                Share(limited) + "); largest correction kept " + stats.LargestAngle.ToString("F1") + "°. Shading off from the original by " +
                stats.MeanAngleBefore.ToString("F1") + "° on average without the map, " + stats.MeanAngleAfter.ToString("F1") +
                "° with it, at this size; the atlas fades the map out at its UV seams.";
            return result;
        }
        private static TextureSettings MapSettings() => new TextureSettings {
            DataType = TextureDataType.NormalTangent, SRGB = false, Mipmaps = false,
            Compression = TextureCompression.None, OutputChannels = TextureOutputChannels.RGB };

        // Sampled on a grid of at most 512 × 512 texels, so it stays fast at 4096².
        private static float UnmatchedShare(NordicsTextureWrapper original, NordicsTextureWrapper current)
        {
            int step = Math.Max(1, Math.Max(current.Width, current.Height) / 512);
            int covered = 0, unmatched = 0;
            for (int y = step / 2; y < current.Height; y += step)
                for (int x = step / 2; x < current.Width; x += step)
                {
                    if (!current.IsPixelUsed(x, y)) continue;
                    covered++;
                    if (!original.IsPixelUsed(x, y)) unmatched++;
                }
            return covered > 0 ? unmatched / (float)covered : 0f;
        }

        /// <summary>Writes the map as a PNG. Inside the project's Assets folder it's imported as a normal map.</summary>
        public static void SavePng(NordicsTextureWrapper map, string path)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("No path.", nameof(path));
            var staging = new Texture2D(map.Width, map.Height, map.GetOutputTextureFormat(), false, true);
            try
            {
                staging.SetPixels(map.GetEncodedPixels());
                staging.Apply(false, false);
                File.WriteAllBytes(path, staging.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(staging); }
            string assetPath = AssetPath(path);
            if (assetPath == null) return;
            // Imported as a normal map in the first import, so it's compressed only once.
            AtlasNormalBakerImport.Pending.Add(assetPath);
            try { AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate); }
            finally { AtlasNormalBakerImport.Pending.Remove(assetPath); }
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }
        /// <summary>The project-relative path ("Assets/…") of a file inside Assets, or null.</summary>
        public static string AssetPath(string path)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            return full.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase) ? "Assets" + full.Substring(assets.Length) : null;
        }

        /// <summary>Where the save dialog starts: the Atlaser's folder in the shared NordicWind
        /// generated-assets folder (ProjectSettings/NordicWindSettings.json), if it exists.</summary>
        public static string StartFolder()
        {
            string generated = "Assets/NordicWind/Generated";
            try
            {
                const string settings = "ProjectSettings/NordicWindSettings.json";
                if (File.Exists(settings))
                {
                    var data = JsonUtility.FromJson<SharedSettings>(File.ReadAllText(settings));
                    if (data != null && !string.IsNullOrWhiteSpace(data.generatedAssetsFolder) &&
                        (data.generatedAssetsFolder == "Assets" || data.generatedAssetsFolder.StartsWith("Assets/", StringComparison.Ordinal)))
                        generated = data.generatedAssetsFolder.TrimEnd('/');
                }
            }
            catch (Exception) { /* unreadable settings: use the default */ }
            foreach (var folder in new[] { generated + "/AdvancedAtlaser", generated, "Assets" })
                if (Directory.Exists(folder)) return Path.GetFullPath(folder);
            return Path.GetFullPath("Assets");
        }
        [Serializable] private sealed class SharedSettings { public string generatedAssetsFolder; }
    }

    /// <summary>Marks the Normal Baker's PNGs as normal maps while they're first imported.</summary>
    internal sealed class AtlasNormalBakerImport : AssetPostprocessor
    {
        internal static readonly HashSet<string> Pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private void OnPreprocessTexture()
        {
            if (Pending.Contains(assetPath) && assetImporter is TextureImporter importer)
                importer.textureType = TextureImporterType.NormalMap;
        }
    }
}
