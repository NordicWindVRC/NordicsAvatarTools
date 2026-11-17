using System;
using System.Collections.Generic;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The UV0 triangles of one submesh that uses a source material: plain arrays, so no mesh
    // crosses the generator job's boundary.
    public sealed class AtlasUVTriangles
    {
        public Vector2[] UV;
        public int[] Triangles;
    }

    // Where a source's meshes use its texture. The map bake grows the UV islands over the rest,
    // so filtering and mipmaps at an island's edge read the island's own colors, not whatever
    // the unused parts of the texture hold.
    public static class AtlasCoverage
    {
        // The UV triangles of every submesh of these renderers that uses the material.
        public static List<AtlasUVTriangles> Triangles(Material material, IEnumerable<Renderer> renderers)
        {
            var result = new List<AtlasUVTriangles>();
            if (material == null || renderers == null) return result;
            foreach (var renderer in renderers)
            {
                var mesh = MeshOf(renderer);
                if (mesh == null) continue;
                var materials = renderer.sharedMaterials;
                Vector2[] uv = null;
                for (int i = 0; i < Math.Min(mesh.subMeshCount, materials.Length); i++)
                {
                    if (materials[i] != material) continue;
                    uv = uv ?? mesh.uv;
                    result.Add(new AtlasUVTriangles { UV = uv, Triangles = mesh.GetTriangles(i) });
                }
            }
            return result;
        }

        // The coverage at a size, or null without triangles or when none lands in the texture.
        // A triangle in another UV tile is moved into 0–1 by whole tiles: the texture repeats.
        public static NordicsTextureWrapper Paint(IReadOnlyList<AtlasUVTriangles> triangles, int width, int height)
        {
            if (triangles == null || triangles.Count == 0) return null;
            var mask = new NordicsTextureWrapper(width, height, new TextureSettings
            {
                DataType = TextureDataType.Mask, SRGB = false, Mipmaps = false, WrapMode = TextureWrapMode.Clamp,
                Compression = TextureCompression.None, OutputChannels = TextureOutputChannels.RGB
            });
            // A pixel counts when any part of a triangle covers it, so thin strips keep their texels.
            var paint = new TrianglePaintOptions { Mode = TriangleRasterizationMode.Quality, SamplesPerAxis = 2 };
            var corners = new List<Vector2>();
            foreach (var part in triangles)
            {
                if (part?.UV == null || part.Triangles == null) continue;
                for (int i = 0; i + 2 < part.Triangles.Length; i += 3)
                {
                    int a = part.Triangles[i], b = part.Triangles[i + 1], c = part.Triangles[i + 2];
                    if (a < 0 || b < 0 || c < 0 || a >= part.UV.Length || b >= part.UV.Length || c >= part.UV.Length) continue;
                    Vector2 uv0 = part.UV[a], uv1 = part.UV[b], uv2 = part.UV[c];
                    if (!Finite(uv0) || !Finite(uv1) || !Finite(uv2)) continue;
                    var center = (uv0 + uv1 + uv2) / 3f;
                    var tile = new Vector2(Mathf.Floor(center.x), Mathf.Floor(center.y));
                    corners.Add(uv0 - tile);
                    corners.Add(uv1 - tile);
                    corners.Add(uv2 - tile);
                }
            }
            if (corners.Count > 0)
            {
                // One batch: painted on every core, or on the GPU.
                var white = new Color[corners.Count];
                for (int i = 0; i < white.Length; i++) white[i] = Color.white;
                mask.PaintTriangles(corners, white, paint);
                if (HasUsed(mask)) return mask;
            }
            mask.Dispose();
            return null;
        }

        // Pixels the coverage doesn't reach become unused; the dilation then fills them from the
        // nearest island. A coverage of another size is resampled.
        public static void GrowIslands(NordicsTextureWrapper content, NordicsTextureWrapper coverage)
        {
            content.Blit(coverage, sourceChannel: TextureChannel.R, destinationChannel: TextureChannel.Usage);
            content.Dilate();
        }

        private static bool HasUsed(NordicsTextureWrapper mask)
        {
            for (int y = 0; y < mask.Height; y++)
                for (int x = 0; x < mask.Width; x++)
                    if (mask.IsPixelUsed(x, y)) return true;
            return false;
        }

        private static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            return filter != null ? filter.sharedMesh : null;
        }

        private static bool Finite(Vector2 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y);
    }
}
