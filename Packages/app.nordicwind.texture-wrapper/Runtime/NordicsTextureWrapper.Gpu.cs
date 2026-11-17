using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NordicWind.TextureWrapper
{
    public sealed partial class NordicsTextureWrapper
    {
        /// <summary>Copies the pixels (normalized first for normal maps) and the usage flags to
        /// render textures of this size, made with <see cref="TextureGpu.CreatePixels"/> and
        /// <see cref="TextureGpu.CreateUsage"/>. <paramref name="usage"/> may be null.</summary>
        internal void UploadTo(RenderTexture pixels, RenderTexture usage)
        {
            Check();
            if (Normal) EnsureNormalized();
            TextureGpu.UploadPixels(_pixels, Width, Height, pixels);
            if (usage != null) TextureGpu.UploadUsage(_usage, Width, Height, usage);
        }

        /// <summary>Replaces the pixels, and the usage flags unless <paramref name="usage"/> is
        /// null, with render textures of this size. <paramref name="normalized"/>: the GPU
        /// operation left normal vectors unit length.</summary>
        internal void DownloadFrom(RenderTexture pixels, RenderTexture usage, bool normalized)
        {
            Writable();
            TextureGpu.DownloadPixels(pixels, _pixels);
            if (usage != null) TextureGpu.DownloadUsage(usage, _usage);
            Changed(!normalized);
        }

        /// <summary>Dilate by jump flooding: one pass at step 1 first, then about log2 of the
        /// size in passes, then two more (at steps 2 and 1); the extra passes catch most of its
        /// misses, but rarely a pixel still takes a slightly farther one. See
        /// GPU_ACCELERATION.md.</summary>
        private void DilateGpu()
        {
            var material = TextureGpu.Material("Dilate");
            var active = RenderTexture.active;
            RenderTexture pixels = null, usage = null, seeds = null, next = null, outPixels = null, outUsage = null;
            try
            {
                pixels = TextureGpu.CreatePixels(Width, Height);
                usage = TextureGpu.CreateUsage(Width, Height);
                UploadTo(pixels, usage);
                seeds = TextureGpu.Create(Width, Height, RenderTextureFormat.RGFloat);
                next = TextureGpu.Create(Width, Height, RenderTextureFormat.RGFloat);
                material.SetVector("_Size", new Vector4(Width, Height, 0, 0));
                Graphics.Blit(usage, seeds, material, 0);
                var steps = new List<int> { 1 };
                int step = 1;
                while (step * 2 < Math.Max(Width, Height)) step *= 2;
                for (; step >= 1; step /= 2) steps.Add(step);
                steps.Add(2);
                steps.Add(1);
                foreach (int s in steps)
                {
                    material.SetFloat("_Step", s);
                    Graphics.Blit(seeds, next, material, 1);
                    (seeds, next) = (next, seeds);
                }
                outPixels = TextureGpu.CreatePixels(Width, Height);
                outUsage = TextureGpu.CreateUsage(Width, Height);
                material.SetTexture("_Pixels", pixels);
                material.SetTexture("_Usage", usage);
                material.SetFloat("_Normal", Normal ? 1f : 0f);
                Graphics.Blit(seeds, outPixels, material, 2);
                Graphics.Blit(seeds, outUsage, material, 3);
                // Copying a direction doesn't change its length: normals stay normalized.
                DownloadFrom(outPixels, outUsage, true);
            }
            finally
            {
                RenderTexture.active = active;
                foreach (var texture in new[] { pixels, usage, seeds, next, outPixels, outUsage }) TextureGpu.Release(texture);
                TextureGpu.DestroyObject(material);
            }
        }

        /// <summary>The batch's triangles drawn into a texture `samples` times larger, adding
        /// up, then each pixel adds its samples' average, like the CPU's sub-samples. With
        /// hidden surfaces, two draws at the texture's own size first find each pixel's average
        /// normal per UV winding and the outermost surface along it, as the CPU does per tile.</summary>
        private void PaintTrianglesGpu(IReadOnlyList<Vector2> uv, IReadOnlyList<Color> color, int samples, HiddenSurfaceOptions hidden)
        {
            var paint = TextureGpu.Material("PaintTriangles");
            var resolve = TextureGpu.Material("ResolvePaint");
            var active = RenderTexture.active;
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = uv.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            RenderTexture painted = null, hits = null, pixels = null, usage = null, outPixels = null, outUsage = null;
            RenderTexture normalsCcw = null, normalsCw = null, outer = null;
            try
            {
                var vertices = new Vector3[uv.Count];
                var colors = new Color[uv.Count];
                var indices = new int[uv.Count];
                for (int i = 0; i < uv.Count; i++)
                {
                    vertices[i] = new Vector3(uv[i].x, uv[i].y, 0f);
                    colors[i] = color[i];
                    indices[i] = i;
                }
                mesh.vertices = vertices;
                mesh.colors = colors;
                mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
                float offset = 1f;
                if (hidden != null)
                {
                    // Each corner's position with its triangle's UV winding (as the CPU finds it,
                    // in pixel space), and its normal with the triangle's footprint.
                    var world = new List<Vector4>(uv.Count);
                    var normals = new List<Vector4>(uv.Count);
                    for (int i = 0; i < uv.Count; i += 3)
                    {
                        Vector2 a = new Vector2(uv[i].x * Width, uv[i].y * Height);
                        Vector2 b = new Vector2(uv[i + 1].x * Width, uv[i + 1].y * Height);
                        Vector2 c = new Vector2(uv[i + 2].x * Width, uv[i + 2].y * Height);
                        float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                        float winding = cross > 0f ? 1f : -1f;
                        // Degenerate triangles draw nothing; their footprint is never used.
                        float footprint = Mathf.Abs(cross) < 1e-10f ? 0f
                            : Footprint(a, b, c, cross, hidden.Positions[i], hidden.Positions[i + 1], hidden.Positions[i + 2]);
                        for (int k = i; k < i + 3; k++)
                        {
                            var p = hidden.Positions[k];
                            var n = hidden.Normals[k];
                            world.Add(new Vector4(p.x, p.y, p.z, winding));
                            normals.Add(new Vector4(n.x, n.y, n.z, footprint));
                            offset = Math.Max(offset, p.magnitude + 1f);
                        }
                    }
                    mesh.SetUVs(1, world);
                    mesh.SetUVs(2, normals);
                }
                int width = Width * samples, height = Height * samples;
                painted = TextureGpu.Create(width, height, RenderTextureFormat.ARGBFloat);
                hits = TextureGpu.Create(width, height, RenderTextureFormat.RFloat);
                TextureGpu.Clear(painted);
                TextureGpu.Clear(hits);
                paint.SetFloat("_FlipY", TextureGpu.PaintFlip);
                int valuePass = 0, hitPass = 1;
                if (hidden != null)
                {
                    normalsCcw = TextureGpu.Create(Width, Height, RenderTextureFormat.ARGBHalf);
                    normalsCw = TextureGpu.Create(Width, Height, RenderTextureFormat.ARGBHalf);
                    outer = TextureGpu.Create(Width, Height, RenderTextureFormat.RGFloat);
                    TextureGpu.Clear(normalsCcw);
                    TextureGpu.Clear(normalsCw);
                    TextureGpu.Clear(outer);
                    paint.SetFloat("_Winding", 1f);
                    TextureGpu.DrawInTextureSpace(mesh, paint, 2, normalsCcw);
                    paint.SetFloat("_Winding", -1f);
                    TextureGpu.DrawInTextureSpace(mesh, paint, 2, normalsCw);
                    paint.SetTexture("_NormalsCCW", normalsCcw);
                    paint.SetTexture("_NormalsCW", normalsCw);
                    // Keeps every distance along a normal above zero, the outer map's "none".
                    paint.SetFloat("_Offset", offset);
                    TextureGpu.DrawInTextureSpace(mesh, paint, 3, outer);
                    paint.SetTexture("_Outer", outer);
                    paint.SetFloat("_Samples", samples);
                    paint.SetFloat("_Margin", hidden.Margin);
                    valuePass = 4;
                    hitPass = 5;
                }
                TextureGpu.DrawInTextureSpace(mesh, paint, valuePass, painted);
                TextureGpu.DrawInTextureSpace(mesh, paint, hitPass, hits);

                pixels = TextureGpu.CreatePixels(Width, Height);
                usage = TextureGpu.CreateUsage(Width, Height);
                UploadTo(pixels, usage);
                outPixels = TextureGpu.CreatePixels(Width, Height);
                outUsage = TextureGpu.CreateUsage(Width, Height);
                resolve.SetVector("_Size", new Vector4(Width, Height, 0, 0));
                resolve.SetFloat("_Samples", samples);
                resolve.SetTexture("_Pixels", pixels);
                resolve.SetTexture("_Usage", usage);
                Graphics.Blit(painted, outPixels, resolve, 0);
                Graphics.Blit(hits, outUsage, resolve, 1);
                // Painted sums aren't unit vectors: normal maps normalize again on their next read.
                DownloadFrom(outPixels, outUsage, false);
            }
            finally
            {
                RenderTexture.active = active;
                foreach (var texture in new[] { painted, hits, pixels, usage, outPixels, outUsage, normalsCcw, normalsCw, outer }) TextureGpu.Release(texture);
                TextureGpu.DestroyObject(mesh);
                TextureGpu.DestroyObject(paint);
                TextureGpu.DestroyObject(resolve);
            }
        }
    }
}
