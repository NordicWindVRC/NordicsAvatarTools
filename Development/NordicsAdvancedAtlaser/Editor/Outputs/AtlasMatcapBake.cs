using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Matcaps baked into the base color and the emission (ATLAS_OUTPUT.md, "Virtual view"): each
    // texel looks its matcaps up as the virtual view sees it, by the layer's projection as its
    // shader computes it.
    // - Surface matcaps blend into the base color by their blend, in layer order; overlay
    //   matcaps that don't add do too (lit instead of after lighting).
    // - Overlay matcaps that add (added after lighting) and matcap glows go into the emission,
    //   which is added after lighting as they are.
    // - The surface is painted per UV winding (AtlasSubmeshBaker.PaintFrame), so mirrored halves
    //   are looked up apart and averaged, as in normal-map shading.
    public static class AtlasMatcapBake
    {
        // The view is frontal over the whole front half (N·Z ≥ 0), so the sides reach the edge
        // of the matcap's disc as they do seen from the front, where shading matcaps put their
        // darkening. It turns toward the normal only on the back, along it from N·Z = −0.5, so
        // the back reads as seen from behind rather than as a silhouette, without a seam.
        public const float Frontal = 0f, AlongNormal = -0.5f;

        /// <summary>The direction toward the camera baked matcaps are seen from: the settings'
        /// side (toward +X, the viewer's left) and height.</summary>
        public static Vector3 Camera(AtlasBakeSettings settings) =>
            settings == null ? Vector3.forward : AtlasLightRig.Direction(settings.matcapViewSide, settings.matcapViewHeight);

        // A layer ready to evaluate on every core.
        private sealed class Prepared
        {
            public AtlasLookupLayer Layer;
            public Color[] Pixels;
            public int Width, Height;
            public Vector2 Tiling = Vector2.one, Offset;
            public float[] Mask;
        }

        /// <summary>The share of a matcap's glow (and of an overlay add) that doesn't follow the
        /// light, which goes into the emission: Poiyomi multiplies the matcap by lerp(1, the
        /// light's color, light color mix) and its mask by lerp(1, lit, light mask), so
        /// (1 − light color mix) × (1 − light mask) of it shows without light. The rest is added
        /// to the base color, where the world's light shades it.</summary>
        public static float GlowShare(AtlasLookupLayer layer) =>
            (1f - Mathf.Clamp01(layer.LightColorMix)) * (1f - Mathf.Clamp01(layer.HideInShadow));

        /// <summary>The light mask at its average under the rig: Poiyomi multiplies a matcap's
        /// mask by lerp(1, lit, light mask), lit 1 where the main light reaches and 0 in shadow,
        /// which a texture can't know. A toon ramp lights the directions with N·L above −wrap,
        /// (1 + wrap) / 2 of every direction a surface can face, so the mask is
        /// lerp(1, (1 + wrap) / 2, light mask), with the rig's key light's wrap.</summary>
        public static float LightMask(AtlasLookupLayer layer, AtlasLightRig rig) =>
            Mathf.Lerp(1f, (1f + rig.Key.Wrap) * 0.5f, Mathf.Clamp01(layer.HideInShadow));

        // How much a layer adds after lighting: an overlay add's opacity and its glow.
        private static float Added(AtlasLookupLayer layer) =>
            (AddsAfterLighting(layer) ? layer.Opacity : 0f) + Mathf.Max(0f, layer.EmissionAmount);

        // Overlay blends that brighten go into the emission as an add.
        public static bool AddsAfterLighting(AtlasLookupLayer layer) =>
            layer.Placement != AtlasPlacement.Surface &&
            (layer.Blend == AtlasBlend.Add || layer.Blend == AtlasBlend.Screen || layer.Blend == AtlasBlend.Lighten || layer.Blend == AtlasBlend.Mixed);

        // Settings the bake leaves as they are: one line per kind, by the caller.
        public static IEnumerable<string> LeftOut(AtlasLookupLayer layer)
        {
            if (layer.UVBlend != Vector2.zero) yield return "blending the lookup toward a mesh UV";
            if (layer.Rotation != Vector3.zero || layer.Spin != Vector3.zero) yield return "turning the view direction";
            if (layer.Blur > 0f || layer.BlurFromRoughness) yield return "blur";
            if (layer.NormalMap != null) yield return "a normal map of its own";
            if (layer.Source.Map != null && layer.Source.Map.Scroll != Vector2.zero) yield return "panning";
        }

        /// <summary>Applies the surface matcaps (and overlay matcaps that don't add) of
        /// <paramref name="layers"/> to <paramref name="color"/>, and adds the part of the glows
        /// and overlay adds that follows the light (all of it without <paramref name="emission"/>),
        /// lerped by <paramref name="strength"/>. <paramref name="normals"/>: the source's
        /// tangent-space normal map at the same size, or null for the mesh normals.</summary>
        public static void ApplyColor(NordicsTextureWrapper color, AtlasUnifiedMaterial material, IReadOnlyList<AtlasLookupLayer> layers,
            IReadOnlyList<AtlasMeshSection> sections, NordicsTextureWrapper normals, AtlasLightRig rig, float strength, bool emission = true,
            Vector3? camera = null)
        {
            int width = color.Width, height = color.Height;
            var prepared = Prepare(material, layers, width, height);
            if (prepared.Count == 0 || strength <= 0f) return;
            var original = color.GetPixels();
            var sum = new Color[original.Length];
            Surface(width, height, sections, normals, rig, camera ?? Vector3.forward, (i, look, cover) =>
            {
                var c = original[i];
                foreach (var p in prepared)
                {
                    var m = Look(p, i, look, out float opacity);
                    float mix = Mathf.Clamp01(p.Layer.BaseColorMix);
                    var tinted = new Color(m.r * Mathf.Lerp(1f, c.r, mix), m.g * Mathf.Lerp(1f, c.g, mix), m.b * Mathf.Lerp(1f, c.b, mix), 1f);
                    float lit = Added(p.Layer) * (1f - (emission ? GlowShare(p.Layer) : 0f));
                    if (!AddsAfterLighting(p.Layer)) c = AtlasBlendMath.Apply(p.Layer.Blend, c, tinted, opacity * p.Layer.Opacity * LightMask(p.Layer, rig));
                    if (lit > 0f) c = new Color(c.r + tinted.r * opacity * lit, c.g + tinted.g * opacity * lit, c.b + tinted.b * opacity * lit, c.a);
                }
                sum[i] += new Color(c.r * cover, c.g * cover, c.b * cover, cover);
            });
            var pixels = original;
            Parallel.For(0, height, y =>
            {
                for (int x = 0, i = y * width; x < width; x++, i++)
                {
                    if (sum[i].a <= 1e-4f) continue;
                    var baked = sum[i] / sum[i].a;
                    var c = original[i];
                    pixels[i] = new Color(Mathf.Lerp(c.r, baked.r, strength), Mathf.Lerp(c.g, baked.g, strength), Mathf.Lerp(c.b, baked.b, strength), c.a);
                }
            });
            color.SetPixels(pixels);
        }

        /// <summary>Adds the part of the overlay adds and glows of <paramref name="layers"/> that
        /// doesn't follow the light (GlowShare) to <paramref name="emission"/>, × <paramref name="scale"/>
        /// (the map is relative to its layer's strength). <paramref name="baseColor"/>: the base
        /// color at the same size, for base color mixes; null: white.</summary>
        public static void ApplyEmission(NordicsTextureWrapper emission, NordicsTextureWrapper baseColor, AtlasUnifiedMaterial material,
            IReadOnlyList<AtlasLookupLayer> layers, IReadOnlyList<AtlasMeshSection> sections, NordicsTextureWrapper normals,
            AtlasLightRig rig, float scale, Vector3? camera = null)
        {
            int width = emission.Width, height = emission.Height;
            var prepared = Prepare(material, layers.Where(l => Added(l) * GlowShare(l) > 0f), width, height);
            if (prepared.Count == 0 || scale <= 0f) return;
            var bases = baseColor?.GetPixels();
            var sum = new Color[width * height];
            Surface(width, height, sections, normals, rig, camera ?? Vector3.forward, (i, look, cover) =>
            {
                var b = bases != null ? bases[i] : Color.white;
                var glow = Color.black;
                foreach (var p in prepared)
                {
                    var m = Look(p, i, look, out float mask);
                    float mix = Mathf.Clamp01(p.Layer.BaseColorMix);
                    var tinted = new Color(m.r * Mathf.Lerp(1f, b.r, mix), m.g * Mathf.Lerp(1f, b.g, mix), m.b * Mathf.Lerp(1f, b.b, mix), 0f);
                    float amount = Added(p.Layer) * GlowShare(p.Layer);
                    glow += tinted * (mask * amount);
                }
                sum[i] += new Color(glow.r * cover, glow.g * cover, glow.b * cover, cover);
            });
            var pixels = emission.GetPixels();
            Parallel.For(0, height, y =>
            {
                for (int x = 0, i = y * width; x < width; x++, i++)
                {
                    if (sum[i].a <= 1e-4f) continue;
                    float k = scale / sum[i].a;
                    var c = pixels[i];
                    pixels[i] = new Color(c.r + sum[i].r * k, c.g + sum[i].g * k, c.b + sum[i].b * k, c.a);
                }
            });
            emission.SetPixels(pixels);
        }

        private static List<Prepared> Prepare(AtlasUnifiedMaterial material, IEnumerable<AtlasLookupLayer> layers, int width, int height)
        {
            var result = new List<Prepared>();
            foreach (var layer in layers)
            {
                var p = new Prepared { Layer = layer };
                if (layer.Source.Map?.Texture is Texture2D texture)
                    using (var wrapper = new NordicsTextureWrapper(texture, TextureDataType.Color))
                    {
                        p.Pixels = wrapper.GetPixels();
                        p.Width = wrapper.Width;
                        p.Height = wrapper.Height;
                        p.Tiling = layer.Source.Map.Tiling;
                        p.Offset = layer.Source.Map.Offset;
                    }
                if (layer.Mask != null && AtlasMapEvaluator.CanEvaluate(layer.Mask, material))
                    using (var mask = AtlasMapEvaluator.ScaledMask(layer.Mask, 1f, width, height, material))
                        p.Mask = mask.GetPixels().Select(c => c.r).ToArray();
                result.Add(p);
            }
            return result;
        }

        // The layer's color at a texel (source color × texture), and its mask (× the texture's
        // alpha where it is the opacity, × the circle mask).
        private static Color Look(Prepared p, int i, (Vector3 Mesh, Vector3 Mapped, Vector3 View, Vector3 Light) look, out float mask)
        {
            var layer = p.Layer;
            var n = Vector3.Lerp(look.Mesh, look.Mapped, Mathf.Clamp01(layer.SurfaceNormalAmount));
            n = n.sqrMagnitude > 1e-8f ? n.normalized : look.Mesh;
            var uv = Lookup(layer, n, look.View, look.Light);
            mask = p.Mask != null ? Mathf.Clamp01(p.Mask[i]) : 1f;
            if (layer.CircleMask)
            {
                float d = Vector2.Distance(uv, new Vector2(0.5f, 0.5f));
                float low = Mathf.Clamp01(layer.CircleRadius - layer.CircleBlur * 0.5f), high = Mathf.Clamp01(layer.CircleRadius + layer.CircleBlur * 0.5f);
                mask *= 1f - SmoothStep(low, high, d);
            }
            uv = Rotate(uv, layer.LookupRotation);
            var sample = p.Pixels != null ? Sample(p, Vector2.Scale(uv, p.Tiling) + p.Offset) : Color.white;
            if (layer.SourceAlphaIsOpacity) mask *= sample.a;
            var color = layer.Source.Color;
            return new Color(color.r * sample.r, color.g * sample.g, color.b * sample.b, 1f);
        }

        /// <summary>The lookup coordinate of a projection (AtlasLookupProjection), for a surface
        /// facing <paramref name="n"/> seen along <paramref name="v"/> (toward the viewer), lit
        /// from <paramref name="light"/>: projection × 0.5 × tiling + 0.5, or u = v = 1 − |N·V|
        /// for the gradient.</summary>
        public static Vector2 Lookup(AtlasLookupLayer layer, Vector3 n, Vector3 v, Vector3 light)
        {
            Vector2 p;
            switch (layer.Projection)
            {
                case AtlasLookupProjection.Gradient:
                {
                    float t = 1f - Mathf.Abs(Vector3.Dot(n, v));
                    return new Vector2(t, t);
                }
                case AtlasLookupProjection.ReflectionPlanar:
                {
                    var r = -v + 2f * Vector3.Dot(n, v) * n;
                    p = new Vector2(r.x, r.y);
                    break;
                }
                case AtlasLookupProjection.LightHalfVector:
                {
                    var tangent = Vector3.Cross(n, Vector3.up);
                    if (tangent.sqrMagnitude < 1e-12f) tangent = Vector3.Cross(n, Vector3.forward);
                    tangent.Normalize();
                    var bitangent = Vector3.Cross(n, tangent).normalized;
                    var half = (light + v).normalized;
                    p = new Vector2(Vector3.Dot(half, tangent), Vector3.Dot(half, bitangent));
                    break;
                }
                default:
                {
                    // The view-space normal with world up kept up. For the virtual view, a camera
                    // along the texel's view direction, the corrected projection is the same.
                    var up = (Vector3.up - v * Vector3.Dot(v, Vector3.up)).normalized;
                    var right = Vector3.Cross(v, up).normalized;
                    p = new Vector2(Vector3.Dot(right, n), Vector3.Dot(up, n));
                    break;
                }
            }
            return new Vector2(p.x * 0.5f * layer.Tiling.x + 0.5f, p.y * 0.5f * layer.Tiling.y + 0.5f);
        }

        // Turns the lookup around its center as Poiyomi's RotateUV does: (x, y) × [[cos, −sin],
        // [sin, cos]], by degrees.
        private static Vector2 Rotate(Vector2 uv, float degrees)
        {
            if (degrees == 0f) return uv;
            float a = degrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            float x = uv.x - 0.5f, y = uv.y - 0.5f;
            return new Vector2(x * c + y * s + 0.5f, -x * s + y * c + 0.5f);
        }

        private static float SmoothStep(float low, float high, float x)
        {
            if (high <= low) return x < low ? 0f : 1f;
            float t = Mathf.Clamp01((x - low) / (high - low));
            return t * t * (3f - 2f * t);
        }

        // Bilinear, repeating.
        private static Color Sample(Prepared p, Vector2 uv)
        {
            float x = uv.x * p.Width - 0.5f, y = uv.y * p.Height - 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            Color At(int px, int py)
            {
                px %= p.Width; if (px < 0) px += p.Width;
                py %= p.Height; if (py < 0) py += p.Height;
                return p.Pixels[py * p.Width + px];
            }
            return Color.Lerp(Color.Lerp(At(x0, y0), At(x0 + 1, y0), fx), Color.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), fx), fy);
        }

        private static TextureSettings Frames() => new TextureSettings
        {
            DataType = TextureDataType.NormalWorld, SRGB = false, Mipmaps = false, Compression = TextureCompression.None,
            WrapMode = TextureWrapMode.Clamp, OutputChannels = TextureOutputChannels.RGBA
        };

        // Calls `texel` for every covered texel of each UV winding (on every core, one winding
        // after the other) with the mesh normal, the mapped normal, the virtual view and the
        // rig's key light as seen from it, and the coverage.
        private static void Surface(int width, int height, IReadOnlyList<AtlasMeshSection> sections, NordicsTextureWrapper normals,
            AtlasLightRig rig, Vector3 camera, Action<int, (Vector3 Mesh, Vector3 Mapped, Vector3 View, Vector3 Light), float> texel)
        {
            int count = width * height;
            var mapped = normals?.GetPixels();
            var parts = new[] { mapped != null ? new Color[count] : null, mapped != null ? new Color[count] : null, new Color[count] };
            var key = rig.Key.Direction;
            foreach (bool counterClockwise in new[] { true, false })
            {
                bool painted = true;
                for (int part = 0; part < 3 && painted; part++)
                {
                    if (parts[part] == null) continue;
                    using (var map = new NordicsTextureWrapper(width, height, Frames()))
                    {
                        painted = AtlasSubmeshBaker.PaintFrame(sections, map, part, counterClockwise);
                        if (painted) map.GetPixels(parts[part]);
                    }
                }
                if (!painted) continue;
                Parallel.For(0, height, y =>
                {
                    for (int x = 0, i = y * width; x < width; x++, i++)
                    {
                        var f = parts[2][i];
                        if (f.a <= 1e-4f) continue;
                        var n = new Vector3(f.r, f.g, f.b);
                        if (n.sqrMagnitude < 0.5f) continue;
                        var bent = n;
                        if (mapped != null)
                        {
                            var m = mapped[i];
                            var t = parts[0][i];
                            var b = parts[1][i];
                            bent = new Vector3(t.r, t.g, t.b) * m.r + new Vector3(b.r, b.g, b.b) * m.g + n * m.b;
                            bent = bent.sqrMagnitude > 1e-8f ? bent.normalized : n;
                        }
                        var view = AtlasVirtualView.Direction(n, camera, Frontal, AlongNormal);
                        texel(i, (n, bent, view, AtlasVirtualView.Turn(key, view)), f.a);
                    }
                });
            }
        }
    }
}
