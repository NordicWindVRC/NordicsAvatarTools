using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The current meshes' surface for bakes that light it (ATLAS_OUTPUT.md, "Geometry buffers").
    public static class AtlasSurfaceGeometry
    {
        /// <summary>A source's surface, made when the first bake asks for it and shared by
        /// the bakes after it: the matcaps in the color and in the emission, the normal-map
        /// shading. It lasts as long as the source of the job that holds it.</summary>
        public static System.Func<IReadOnlyList<AtlasMeshSection>> Once(System.Func<IReadOnlyList<AtlasMeshSection>> make)
        {
            IReadOnlyList<AtlasMeshSection> sections = null;
            bool made = false;
            return () =>
            {
                if (!made)
                {
                    sections = make();
                    made = true;
                }
                return sections;
            };
        }

        // The sections of every submesh of these renderers that uses the material, in the space
        // of `root` (skinned meshes in their current pose).
        public static List<AtlasMeshSection> Sections(Material material, IEnumerable<Renderer> renderers, Transform root)
        {
            var result = new List<AtlasMeshSection>();
            if (material == null || renderers == null) return result;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials;
                Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || !(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) continue;
                bool uses = false;
                for (int i = 0; i < Math.Min(mesh.subMeshCount, materials.Length); i++) uses |= materials[i] == material;
                if (!uses) continue;
                Vector3[] vertices, normals;
                Vector4[] tangents;
                Matrix4x4 toRoot;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    // Posed in root space already.
                    AtlasGeometryPose.Capture(skin, root, out vertices, out normals, out tangents, out _);
                    toRoot = Matrix4x4.identity;
                }
                else
                {
                    vertices = mesh.vertices;
                    normals = mesh.normals;
                    tangents = mesh.tangents;
                    toRoot = (root != null ? root.worldToLocalMatrix : Matrix4x4.identity) * renderer.transform.localToWorldMatrix;
                }
                var uv = mesh.uv;
                for (int i = 0; i < Math.Min(mesh.subMeshCount, materials.Length); i++)
                {
                    if (materials[i] != material) continue;
                    result.Add(new AtlasMeshSection
                    {
                        Material = material, SubmeshIndex = i, RendererPath = renderer.name,
                        Vertices = vertices, Normals = normals, Tangents = tangents, UV = uv,
                        Triangles = mesh.GetTriangles(i), LocalToWorld = toRoot
                    });
                }
            }
            return result;
        }

        // The avatar root of an object in the editor: its topmost ancestor with an Animator (an
        // avatar's root has one), otherwise the top of its hierarchy.
        public static Transform AvatarRoot(Transform transform)
        {
            if (transform == null) return null;
            Transform found = null;
            for (var t = transform; t != null; t = t.parent)
                if (t.GetComponent<Animator>() != null) found = t;
            return found != null ? found : transform.root;
        }

        // The size the overlap is checked at, and the share of the covered texels that must be
        // covered more than once for the UVs to count as overlapping: a few stray triangles
        // don't change how a source looks, mirrored halves or stacked copies do.
        private const int OverlapSize = 256;
        private const float OverlapShare = 0.01f;

        // Whether the UVs overlap: mirrored halves or stacked copies share texels, which can't
        // hold two different light-dependent results. Triangles in other UV tiles count where
        // the texture repeats them.
        public static bool Overlaps(IReadOnlyList<AtlasUVTriangles> triangles)
        {
            if (triangles == null || triangles.Count == 0) return false;
            var uvs = new List<Vector2>();
            var counts = new List<Color>();
            foreach (var set in triangles)
            {
                if (set?.UV == null || set.Triangles == null) continue;
                for (int i = 0; i + 2 < set.Triangles.Length; i += 3)
                {
                    int a = set.Triangles[i], b = set.Triangles[i + 1], c = set.Triangles[i + 2];
                    if (a < 0 || b < 0 || c < 0 || a >= set.UV.Length || b >= set.UV.Length || c >= set.UV.Length) continue;
                    Vector2 ua = set.UV[a], ub = set.UV[b], uc = set.UV[c];
                    if (!Finite(ua) || !Finite(ub) || !Finite(uc)) continue;
                    var tile = new Vector2(Mathf.Floor((ua.x + ub.x + uc.x) / 3f), Mathf.Floor((ua.y + ub.y + uc.y) / 3f));
                    uvs.Add(ua - tile); uvs.Add(ub - tile); uvs.Add(uc - tile);
                    counts.Add(Color.white); counts.Add(Color.white); counts.Add(Color.white);
                }
            }
            if (uvs.Count == 0) return false;
            using (var map = new NordicsTextureWrapper(OverlapSize, OverlapSize, new TextureSettings
            {
                DataType = TextureDataType.LinearData, SRGB = false, Mipmaps = false, Compression = TextureCompression.None,
                WrapMode = TextureWrapMode.Clamp, OutputChannels = TextureOutputChannels.RGB
            }))
            {
                // Fast painting counts the triangles whose center sample covers each texel.
                map.PaintTriangles(uvs, counts, new TrianglePaintOptions { Mode = TriangleRasterizationMode.Fast });
                int covered = 0, shared = 0;
                foreach (var count in map.GetPixels())
                {
                    if (count.r > 0.5f) covered++;
                    if (count.r > 1.5f) shared++;
                }
                return covered > 0 && shared > covered * OverlapShare;
            }
        }

        private static bool Finite(Vector2 uv) => !float.IsNaN(uv.x) && !float.IsNaN(uv.y) && !float.IsInfinity(uv.x) && !float.IsInfinity(uv.y);
    }

    // Normal-map shading baked into the base color (ATLAS_OUTPUT.md, "Virtual light rigs"): the
    // ratio of the shading with and without the normal map, shade(N mapped) / shade(N mesh),
    // under the key and fill lights' diffuse terms. It adds the normal map's detail without
    // shading the mesh's form twice.
    public static class AtlasNormalShading
    {
        // Beyond twice as bright, a texel's shading is unstable (nearly at a grazing angle to
        // every light), and one such texel would lower the precision of the whole texture.
        public const float MaxGain = 2f;
        // Added to both sides of the ratio, as a share of the lights' full diffuse light, so
        // texels the lights barely reach keep their color instead of a noisy ratio.
        public const float Floor = 0.05f;

        private static TextureSettings Frames() => new TextureSettings
        {
            DataType = TextureDataType.NormalWorld, SRGB = false, Mipmaps = false, Compression = TextureCompression.None,
            WrapMode = TextureWrapMode.Clamp, OutputChannels = TextureOutputChannels.RGBA
        };

        // Multiplies `color` by the normal-map shading of `normals` (the source's tangent-space
        // normal map, the same size) on the meshes' surface, lerped from 1 by `strength`. The
        // rig turns with each texel's virtual view and is then tilted onto its surface, so every
        // surface gets the lights at the same angles and the detail reads evenly over the front,
        // the sides, the back and surfaces facing up or down. Texels covered by more than one surface get the average of
        // their surfaces' results; the surfaces are painted per UV winding, so mirrored halves
        // never mix their frames. Returns the texels changed.
        public static int Apply(NordicsTextureWrapper color, NordicsTextureWrapper normals,
            IReadOnlyList<AtlasMeshSection> sections, AtlasLightRig rig, float strength)
        {
            if (color == null || normals == null || sections == null || sections.Count == 0 || rig == null || strength <= 0f) return 0;
            int width = color.Width, height = color.Height, count = width * height;
            if (normals.Width != width || normals.Height != height) throw new ArgumentException("The normal map must have the color map's size.");
            var mapped = normals.GetPixels();
            // The ratios × coverage, with the coverage summed in alpha.
            var sum = new Color[count];
            var tangents = new Color[count];
            var bitangents = new Color[count];
            var frameNormals = new Color[count];
            var parts = new[] { tangents, bitangents, frameNormals };
            var key = rig.Key;
            var fill = rig.Fill;
            // The floor, per channel.
            var floor = (key.Color * key.Diffuse + fill.Color * fill.Diffuse) * Floor;
            foreach (bool counterClockwise in new[] { true, false })
            {
                bool painted = true;
                for (int part = 0; part < 3 && painted; part++)
                    using (var map = new NordicsTextureWrapper(width, height, Frames()))
                    {
                        painted = AtlasSubmeshBaker.PaintFrame(sections, map, part, counterClockwise);
                        // Read normalized; alpha holds the coverage.
                        if (painted) map.GetPixels(parts[part]);
                    }
                if (!painted) continue;
                Parallel.For(0, height, y =>
                {
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        int i = row + x;
                        float cover = frameNormals[i].a;
                        if (cover <= 1e-4f) continue;
                        var n = Vector(frameNormals[i]);
                        if (n.sqrMagnitude < 0.5f) continue;
                        var m = mapped[i];
                        var bent = Vector(tangents[i]) * m.r + Vector(bitangents[i]) * m.g + n * m.b;
                        bent = bent.sqrMagnitude > 1e-8f ? bent.normalized : n;
                        // The rig as seen from this texel's view, then tilted from the view onto
                        // the surface, so every surface gets the lights at the angles a surface
                        // facing the view does.
                        var view = AtlasVirtualView.Direction(n);
                        var keyDirection = Tilt(AtlasVirtualView.Turn(key.Direction, view), view, n);
                        var fillDirection = Tilt(AtlasVirtualView.Turn(fill.Direction, view), view, n);
                        var lit = key.Color * (key.Diffuse * key.Lambert(bent, keyDirection)) +
                                  fill.Color * (fill.Diffuse * fill.Lambert(bent, fillDirection)) + floor;
                        var flat = key.Color * (key.Diffuse * key.Lambert(n, keyDirection)) +
                                   fill.Color * (fill.Diffuse * fill.Lambert(n, fillDirection)) + floor;
                        var ratio = new Color(Gain(lit.r, flat.r), Gain(lit.g, flat.g), Gain(lit.b, flat.b), 1f);
                        sum[i] += ratio * cover;
                    }
                });
            }
            mapped = tangents = bitangents = frameNormals = null;
            var pixels = color.GetPixels();
            int changed = 0;
            var rows = new int[height];
            Parallel.For(0, height, y =>
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;
                    if (sum[i].a <= 1e-4f) continue;
                    var ratio = sum[i] / sum[i].a;
                    var c = pixels[i];
                    pixels[i] = new Color(c.r * Mathf.LerpUnclamped(1f, ratio.r, strength),
                        c.g * Mathf.LerpUnclamped(1f, ratio.g, strength), c.b * Mathf.LerpUnclamped(1f, ratio.b, strength), c.a);
                    rows[y]++;
                }
            });
            foreach (int r in rows) changed += r;
            color.SetPixels(pixels);
            return changed;
        }

        // `direction` turned by the shortest rotation from `from` to `to` (unit vectors).
        // The virtual view always faces the surface's own side (from · to > 0), so the rotation
        // is defined everywhere and changes smoothly over the surface.
        private static Vector3 Tilt(Vector3 direction, Vector3 from, Vector3 to)
        {
            float cos = Vector3.Dot(from, to);
            if (cos <= -0.999f) return direction;
            var axis = Vector3.Cross(from, to);
            return direction * cos + Vector3.Cross(axis, direction) + axis * (Vector3.Dot(axis, direction) / (1f + cos));
        }

        private static float Gain(float lit, float flat) => flat > 1e-6f ? Mathf.Clamp(lit / flat, 0f, MaxGain) : 1f;

        private static Vector3 Vector(Color c) => new Vector3(c.r, c.g, c.b);
    }

    // Ambient occlusion baked into the base color (ATLAS_OUTPUT.md, "Virtual light rigs"):
    // the base color × 1 − (1 − occlusion) × the share of the light the source's occlusion
    // darkens. The share is averaged over every direction a surface can face under the rig, so
    // the shadow sides, lit by the ambient alone, count fully, and no light direction is baked.
    public static class AtlasOcclusionShading
    {
        // The directions the shares are averaged over, evenly spread on the sphere.
        private const int Directions = 1024;

        // The shares of the rig's diffuse light that occlusion darkens, averaged over every
        // direction a surface can face: x, the direct light's × `direct`; y, the ambient's ×
        // `indirect` (the source's occlusion of direct and ambient light, 0–1).
        public static Vector2 Shares(AtlasLightRig rig, float direct, float indirect)
        {
            direct = Mathf.Clamp01(direct);
            indirect = Mathf.Clamp01(indirect);
            double directShare = 0, ambientShare = 0;
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < Directions; i++)
            {
                float y = 1f - (i + 0.5f) * 2f / Directions, r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)), phi = golden * i;
                var n = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
                float lit = rig.Key.Diffuse * rig.Key.Lambert(n, rig.Key.Direction) + rig.Fill.Diffuse * rig.Fill.Lambert(n, rig.Fill.Direction) +
                            rig.Back.Diffuse * rig.Back.Lambert(n, rig.Back.Direction);
                var ambientColor = rig.Ambient(n);
                float ambient = 0.2126f * ambientColor.r + 0.7152f * ambientColor.g + 0.0722f * ambientColor.b;
                float total = lit + ambient;
                if (total <= 1e-6f) continue;
                directShare += lit * direct / total;
                ambientShare += ambient * indirect / total;
            }
            return new Vector2((float)(directShare / Directions), (float)(ambientShare / Directions));
        }

        // Multiplies `color` by lerp(1, 1 − (1 − tint) × darkening, strength), where darkening is
        // shares.x × (1 − o) + shares.y × (1 − o ambient) and o is `occlusion`'s R (the same
        // size). With `multiBounce`, the ambient's occlusion is raised on light colors by
        // Jimenez et al.'s multi-bounce fit (2016), over the base color × (1 − metallic), with
        // `metallic`'s R; o ambient = o × max(1, (a o + b) o + c).
        public static void Apply(NordicsTextureWrapper color, NordicsTextureWrapper occlusion, NordicsTextureWrapper metallic,
            Vector2 shares, Color tint, bool multiBounce, float strength)
        {
            if (color == null || occlusion == null || strength <= 0f) return;
            int width = color.Width, height = color.Height;
            if (occlusion.Width != width || occlusion.Height != height) throw new ArgumentException("The occlusion map must have the color map's size.");
            var pixels = color.GetPixels();
            var occlusions = occlusion.GetPixels();
            var metals = multiBounce && metallic != null ? metallic.GetPixels() : null;
            Parallel.For(0, height, y =>
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;
                    float o = Mathf.Clamp01(occlusions[i].r);
                    var c = pixels[i];
                    float diffuse = metals != null ? 1f - Mathf.Clamp01(metals[i].r) : 1f;
                    pixels[i] = new Color(
                        c.r * Factor(o, Mathf.Clamp01(c.r) * diffuse, tint.r, shares, multiBounce, strength),
                        c.g * Factor(o, Mathf.Clamp01(c.g) * diffuse, tint.g, shares, multiBounce, strength),
                        c.b * Factor(o, Mathf.Clamp01(c.b) * diffuse, tint.b, shares, multiBounce, strength), c.a);
                }
            });
            color.SetPixels(pixels);
        }

        private static float Factor(float o, float albedo, float tint, Vector2 shares, bool multiBounce, float strength)
        {
            float ambient = multiBounce ? Mathf.Min(1f, o * Mathf.Max(1f, MultiBounce(albedo, o))) : o;
            float darkening = shares.x * (1f - o) + shares.y * (1f - ambient);
            return Mathf.Lerp(1f, 1f - (1f - Mathf.Clamp01(tint)) * darkening, strength);
        }

        // Jimenez, Wu, Pesce and Jarabo, "Practical Real-Time Strategies for Accurate Indirect
        // Occlusion" (2016): the visibility after interreflections, divided by the occlusion.
        public static float MultiBounce(float albedo, float occlusion)
        {
            float a = 2.0404f * albedo - 0.3324f, b = -4.7951f * albedo + 0.6417f, c = 2.7552f * albedo + 0.6903f;
            return (a * occlusion + b) * occlusion + c;
        }
    }
}
