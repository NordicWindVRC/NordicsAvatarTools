using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NordicWind.AvatarStatistics.Editor
{
    public static class AvatarStatisticsCollector
    {
        public static bool NativeFaceTrackingEnabled(GameObject root) => OfficialSdkPerformanceCollector.IsFaceTrackingEnabled(root);

        // Tools such as VRCFury (Visemes, BlendShape Link) can switch native face tracking on
        // in the build copy. The costs are real in-game, so the rows stay; this explains them.
        public static void NoteFaceTrackingEnabledDuringBuild(AvatarStatisticsReport report)
        {
            const string note = "Native face tracking is off on your avatar descriptor, but another tool turned it on " +
                                "during the build (for example VRCFury Visemes or BlendShape Link), so it counts in-game.";
            foreach (var metric in report.Performance)
                if (metric.Name == "Face Tracking Operations" || metric.Name == "Forwarded Parameters")
                    metric.Note = string.IsNullOrEmpty(metric.Note) ? note : metric.Note + " " + note;
        }

        public static AvatarStatisticsReport Collect(GameObject root)
        {
            var result = new AvatarStatisticsReport();
            var materials = new Dictionary<Material, MaterialEntry>();
            var textures = new Dictionary<Texture, TextureEntry>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                // VRChat counts at least one slot for an otherwise empty renderer.
                // Particle systems use one base slot plus one when particle trails are enabled.
                var slots = renderer.sharedMaterials;
                var particleRenderer = renderer as ParticleSystemRenderer;
                var particle = particleRenderer != null ? particleRenderer.GetComponent<ParticleSystem>() : null;
                int countedSlots;
                if (particleRenderer != null)
                    countedSlots = 1 + (particle != null && particle.trails.enabled ? 1 : 0);
                else
                    countedSlots = Math.Max(1, slots.Length);
                result.MaterialSlots += countedSlots;
                for (int i = 0; i < slots.Length; i++)
                {
                    var mat = slots[i];
                    if (mat == null) continue;
                    MaterialEntry me;
                    if (!materials.TryGetValue(mat, out me))
                    {
                        me = new MaterialEntry { Name = mat.name, Shader = mat.shader != null ? mat.shader.name : "<missing shader>", AssetPath = AssetDatabase.GetAssetPath(mat) };
                        materials.Add(mat, me);
                    }
                    me.Slots++;
                }
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skin) { result.SkinnedCount++; mesh = skin.sharedMesh; if (mesh != null) result.BlendShapeCount += mesh.blendShapeCount; }
                else if (renderer is MeshRenderer mr)
                {
                    result.RegularCount++;
                    var filter = mr.GetComponent<MeshFilter>();
                    if (filter != null) mesh = filter.sharedMesh;
                }
                else continue;
                var entry = new MeshEntry { Path = HierarchyPath(renderer.transform, root.transform), Name = mesh != null ? mesh.name : "<missing mesh>", Type = renderer is SkinnedMeshRenderer ? "Skinned" : "Regular", Slots = countedSlots, AssetPath = mesh != null ? AssetDatabase.GetAssetPath(mesh) : "" };
                if (mesh != null)
                {
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        var sub = mesh.GetSubMesh(s);
                        var triangleCount = mesh.GetTopology(s) == MeshTopology.Triangles ? (int)(sub.indexCount / 3) : 0;
                        entry.Triangles += triangleCount;
                        string materialName = s < slots.Length && slots[s] != null ? slots[s].name : "<none>";
                        entry.Submeshes.Add("#" + s + "  " + mesh.GetTopology(s) + "  " + triangleCount.ToString("N0") + " triangles  |  " + materialName);
                    }

                }
                result.TriangleCount += entry.Triangles;
                result.Meshes.Add(entry);
            }
            foreach (var pair in materials)
            {
                var mat = pair.Key;
                var info = pair.Value;
                int count = 0;
                try { count = mat.shader != null ? mat.shader.GetPropertyCount() : 0; }
                catch { /* Invalid shader: keep material, no texture properties. */ }
                for (int i = 0; i < count; i++)
                {
                    if (mat.shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                    var property = mat.shader.GetPropertyName(i);
                    Texture tex;
                    try { tex = mat.GetTexture(property); } catch { continue; }
                    if (tex == null) continue;
                    TextureEntry te;
                    if (!textures.TryGetValue(tex, out te))
                    {
                        var path = AssetDatabase.GetAssetPath(tex);
                        te = new TextureEntry {
                            Name = string.IsNullOrEmpty(path) ? tex.name + " [Generated / Embedded]" : Path.GetFileName(path),
                            AssetPath = path,
                            Width = tex.width, Height = tex.height,
                            Format = tex.graphicsFormat.ToString(),
                            Dimension = tex.dimension.ToString(),
                            Mips = Math.Max(1, tex.mipmapCount),
                            Bytes = EstimateBytes(tex),
                            GpuBytes = EstimateGpuBytes(tex)
                        };
                        PopulateTextureImportData(tex, path, te);
                        te.PcGpuBytes = PlatformTextureMemoryCalculator.Calculate(tex, path, "Standalone", te.GpuBytes, out te.PcFallback);
                        te.QuestGpuBytes = PlatformTextureMemoryCalculator.Calculate(tex, path, "Android", te.GpuBytes, out te.QuestFallback);
                        textures.Add(tex, te);
                    }
                    var display = info.Name + "  →  " + property;
                    if (!te.References.Contains(display)) te.References.Add(display);
                    te.MaterialKeys.Add(info.Name + "|" + info.AssetPath);
                    var description = te.Name + " (" + property + ")";
                    if (!info.Textures.Contains(description)) info.Textures.Add(description);
                }
                result.Materials.Add(info);
            }
            result.Textures.AddRange(textures.Values);
            result.TextureBytes = result.Textures.Sum(x => x.Bytes);
            result.GpuTextureBytes = result.Textures.Sum(x => x.GpuBytes);
            result.PcGpuTextureBytes = result.Textures.Sum(x => x.PcGpuBytes);
            result.QuestGpuTextureBytes = result.Textures.Sum(x => x.QuestGpuBytes);
            result.PcTextureFallbacks = result.Textures.Count(x => x.PcFallback);
            result.QuestTextureFallbacks = result.Textures.Count(x => x.QuestFallback);
            result.Meshes.Sort((a,b) => b.Triangles.CompareTo(a.Triangles));
            result.Materials.Sort((a,b) => b.Slots.CompareTo(a.Slots));
            result.Textures.Sort((a,b) => b.Bytes.CompareTo(a.Bytes));
            AvatarParameterCollector.Collect(root, result);
            AvatarPerformanceCollector.Collect(root, result);
            return result;
        }
        // The importer describes the actual asset settings; generated/embedded textures may not have one.
        // TextureUtil is an internal Unity Editor API, so the optional stored-size lookup must fail safely.
        private static void PopulateTextureImportData(Texture texture, string path, TextureEntry entry)
        {
            if (string.IsNullOrEmpty(path)) return;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            entry.HasImporter = true;
            // Classify using importer settings, not inferred channel contents or GPU format.
            if (importer.textureType == TextureImporterType.NormalMap)
                entry.DisplayType = "Normal";
            else
            {
                bool hasAlpha = importer.alphaSource != TextureImporterAlphaSource.None;
                entry.DisplayType = importer.sRGBTexture
                    ? (hasAlpha ? "sRGBA" : "sRGB")
                    : (hasAlpha ? "RGBA" : "RGB");
            }
            entry.StreamingMipmaps = importer.streamingMipmaps;
            entry.StreamingPriority = importer.streamingMipmapsPriority;
            entry.MipmapFilter = importer.mipmapFilter.ToString();
            entry.CrunchCompressed = importer.crunchedCompression;
            entry.CrunchQuality = importer.compressionQuality;
            if (!entry.CrunchCompressed) return;
            try
            {
                var utilType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.TextureUtil");
                var method = utilType != null ? utilType.GetMethod("GetStorageMemorySizeLong", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Texture) }, null) : null;
                if (method != null)
                {
                    var size = method.Invoke(null, new object[] { texture });
                    if (size != null) entry.CrunchStorageBytes = Convert.ToInt64(size);
                }
            }
            catch { /* Optional diagnostic; do not fail avatar statistics collection. */ }
        }

        private static string HierarchyPath(Transform tr, Transform root)
        {
            var parts = new List<string>();
            while (tr != null) { parts.Add(tr.name); if (tr == root) break; tr = tr.parent; }
            parts.Reverse(); return string.Join("/", parts);
        }
        // Format-aware allocation estimate. GraphicsFormatUtility handles BC/ETC/ASTC block sizes,
        // mip rounding and ordinary uncompressed formats; each unique Texture is counted once.
        private static long EstimateGpuBytes(Texture t)
        {
            try
            {
                int mips = Math.Max(1, t.mipmapCount);
                var format = t.graphicsFormat;
                if (t is Texture3D volume)
                    return UnityEngine.Experimental.Rendering.GraphicsFormatUtility.ComputeMipChainSize(
                        t.width, t.height, Math.Max(1, volume.depth), format, mips);

                long sliceBytes = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.ComputeMipChainSize(
                    t.width, t.height, format, mips);
                if (t is Cubemap) return sliceBytes * 6L;
                if (t is CubemapArray cubes) return sliceBytes * 6L * Math.Max(1, cubes.cubemapCount);
                if (t is Texture2DArray array) return sliceBytes * Math.Max(1, array.depth);
                return sliceBytes;
            }
            catch
            {
                // Unusual or unsupported formats fall back to an uncompressed estimate.
                return EstimateBytes(t);
            }
        }
        // Uncompressed-equivalent estimate, not measured runtime GPU allocation.
        // Uses component/storage precision of the format; GPU block compression is ignored.
        private static long EstimateBytes(Texture t)
        {
            var fmt = t.graphicsFormat.ToString().ToUpperInvariant();
            int channels = 4;
            if (fmt.StartsWith("R8_") || fmt.StartsWith("R16_") || fmt.StartsWith("R32_") || fmt.StartsWith("R64_")) channels = 1;
            else if (fmt.StartsWith("R8G8_") || fmt.StartsWith("R16G16_") || fmt.StartsWith("R32G32_")) channels = 2;
            else if (fmt.StartsWith("R8G8B8_") || fmt.StartsWith("R16G16B16_") || fmt.StartsWith("R32G32B32_")) channels = 3;
            int bits = fmt.StartsWith("R32") ? 32 : fmt.StartsWith("R16") ? 16 : 8;
            if (fmt.Contains("BC") || fmt.Contains("ETC") || fmt.Contains("ASTC") || fmt.Contains("PVRTC")) { channels = 4; bits = 8; }
            long perPixel = Math.Max(1, (channels * bits + 7) / 8);
            long layers = t.dimension == TextureDimension.Cube ? 6L : t.dimension == TextureDimension.CubeArray ? 6L * Math.Max(1, t is CubemapArray ca ? ca.cubemapCount : 1) : t is Texture2DArray a ? Math.Max(1, a.depth) : t is Texture3D d ? Math.Max(1, d.depth) : 1L;
            long sum = 0;
            for (int m = 0; m < Math.Max(1, t.mipmapCount); m++)
            {
                long w = Math.Max(1, t.width >> m), h = Math.Max(1, t.height >> m);
                long depth = t is Texture3D ? Math.Max(1, layers >> m) : layers;
                sum += w * h * depth * perPixel;
            }
            return sum;
        }
    }
}
