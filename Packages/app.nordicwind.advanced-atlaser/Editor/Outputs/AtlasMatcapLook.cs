using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // What a matcap is for (ATLAS_OUTPUT.md, "What a matcap is for").
    public enum AtlasMatcapRole
    {
        // Stands in for the world's light: a lit sphere, sky and ground, a soft glow, or one
        // color or shade all over.
        Lighting,
        // Lights or darkens the edges: its effect lies toward the edge, all the way around.
        Rim,
        // Gloss, shine, glints and reflections: most of its effect in a small part, or fine
        // detail all over (facets, a mirrored room).
        Highlight
    }

    // A matcap's effect, measured on a sphere of normals seen from around the avatar: the look
    // with it minus the look without it, as displayed, on its source's base colors in the
    // standard scenes.
    public sealed class AtlasMatcapMeasure
    {
        // The mean size of its effect on the parts it shows on.
        public float Impact;
        // The share of its effect that brightens, 0–1; the rest darkens.
        public float Brightens;
        // What freezing it at the virtual view costs seen from around the avatar, over its
        // impact (AtlasLayerCost): from 1, leaving it out costs less.
        public float BakeLoss;
        // The share of its effect in its strongest tenth, seen from the front.
        public float Spread;
        // 1: the same effect all over; 0: as much variation as effect.
        public float Evenness;
        // The share of its effect on the outer ring (beyond three quarters of the disc's radius).
        public float EdgeWeight;
        // How much of the way around the outer ring shows the effect: the share of directions
        // with a quarter of the ring's average or more.
        public float Around;
        // The share of its variation that is fine: what is left after each point is averaged
        // with its neighbors (a twelfth of the radius, 15° around).
        public float Detail;
        // How colored the matcap is where it shows: its saturation, weighted by its effect.
        public float Tint;
        public AtlasMatcapRole Role;

        // Whether it colors the surface besides what its role does.
        public bool Tints => Tint >= AtlasMatcapLook.TintSaturation;

        public string RoleName => Role.ToString().ToLowerInvariant();

        // Whether it changes the look at all: half a displayed level of 256 on average.
        public bool HasEffect => Impact >= 0.002f;

        /// <summary>The role, whether it brightens or darkens, and whether it tints, in a few
        /// words: "Rim, darkens", "Highlight, brightens, tints".</summary>
        public string Summary() => !HasEffect ? "No visible effect"
            : Role + ", " + (Brightens >= 0.9f ? "brightens" : Brightens <= 0.1f ? "darkens" : "brightens and darkens") +
              (Tints ? ", tints" : "");
    }

    public static class AtlasMatcapLook
    {
        // The limits between the roles, each on a measure of the matcap's own effect, checked
        // against the matcaps in use on an avatar and two collections of 184 (an artist's,
        // sorted into gems, glass, metal, pearl, rubber, shine and silk, and Poiyomi's samples).
        // How a matcap is looked up doesn't say what it is for: Poiyomi's double-sided lookup,
        // by the reflection, is used for every kind.
        // - A rim has at least three fifths of its effect on the outer ring, which holds 0.44 of
        //   an even matcap's, around at least 0.65 of the ring (a ring thinner at the top and
        //   bottom is one; a crescent on one side isn't).
        public const float RimEdgeWeight = 0.6f, RimAround = 0.65f;
        // - A highlight carries at least half its effect in its strongest tenth, or at least half
        //   its variation is fine detail (gems and facets measure 0.53 and up, soft lit spheres
        //   0.35 and below), where it varies at all (speckle on an even color isn't one).
        public const float HighlightSpread = 0.5f, HighlightDetail = 0.5f, DetailEvenness = 0.75f;
        // - It tints from a quarter saturation: a warm white (0.2) doesn't, a silk blue (0.3) does.
        public const float TintSaturation = 0.25f;
        // - Everything else is lighting, an even color or shade included: how even a matcap is
        //   depends on the base colors it is on (a multiply mixed with a dark base color darkens
        //   almost evenly), so evenness is a measure and not a role.

        // Where the outer ring starts: three quarters of the disc's radius.
        private const float RingRadius = 0.75f;
        private const int Normals = 400, Views = 8, TextureSize = 32;
        // The disc seen from the front, for how the effect is spread over it: rings by radius,
        // steps around.
        private const int Rings = 24, Steps = 48;
        // The world's light for lookups by the light's half vector: above and in front.
        private static readonly Vector3 Light = new Vector3(0.3f, 0.6f, 0.74f).normalized;

        private static readonly Vector3[] Sphere = MakeSphere();
        private static readonly Dictionary<string, AtlasMatcapMeasure> Measures = new Dictionary<string, AtlasMatcapMeasure>();

        // Points spread evenly over the unit sphere (a Fibonacci spiral).
        private static Vector3[] MakeSphere()
        {
            var points = new Vector3[Normals];
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < Normals; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / Normals, r = Mathf.Sqrt(1f - y * y), a = golden * i;
                points[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
            return points;
        }

        /// <summary>Measures a source's matcap: its lookup layers (one per blend weight, sharing
        /// their texture and lookup) on the source's base colors under its own lighting.</summary>
        public static AtlasMatcapMeasure Measure(IReadOnlyList<AtlasLookupLayer> layers, AtlasUnifiedMaterial material)
        {
            var first = layers[0];
            var map = first.Source.Map;
            var texture = map?.Texture as Texture2D;
            var bases = AtlasLook.Representative(AtlasSourceSamples.Bases(material));
            string key = string.Join("|", layers.Select(l => AtlasFieldwise.Signature(l, "Mask"))) + "|" +
                         (texture != null ? texture.imageContentsHash.ToString() : "") + "|" +
                         AtlasFieldwise.Signature(material.Lighting) + "|" + string.Join(",", bases.Select(b => b.ToString("0.###")));
            if (Measures.TryGetValue(key, out var known)) return known;

            var pixels = texture != null ? AtlasSourceSamples.Small(texture, TextureSize, TextureSize) : null;
            // The matcap's color and opacity where a surface facing `n` is seen from `v`.
            (Color Color, float Opacity) Look(Vector3 n, Vector3 v)
            {
                var uv = AtlasMatcapBake.Lookup(first, n, v, Light);
                float opacity = 1f;
                if (first.CircleMask)
                {
                    float d = Vector2.Distance(uv, new Vector2(0.5f, 0.5f));
                    float low = Mathf.Clamp01(first.CircleRadius - first.CircleBlur * 0.5f), high = Mathf.Clamp01(first.CircleRadius + first.CircleBlur * 0.5f);
                    opacity *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(low, high, d));
                }
                uv = Rotate(uv, first.LookupRotation);
                var sample = pixels != null ? Sample(pixels, Vector2.Scale(uv, map.Tiling) + map.Offset) : Color.white;
                if (first.SourceAlphaIsOpacity) opacity *= sample.a;
                var color = first.Source.Color;
                return (new Color(color.r * sample.r, color.g * sample.g, color.b * sample.b, 1f), opacity);
            }

            // The light each scene gives the surface on average, by the source's own lighting.
            var lights = AtlasLook.Scenes.Select(scene => LightLevel(material.Lighting, scene)).ToArray();
            int combos = bases.Count * lights.Length;
            // The effect on each base color in each scene: the displayed look with the matcap
            // minus the look without it.
            void Effects((Color Color, float Opacity) look, Color[] into)
            {
                int n = 0;
                foreach (var b in bases)
                    foreach (float light in lights)
                    {
                        var with = AtlasLightingResponse.Display(With(layers, b, look.Color, look.Opacity, light));
                        var without = AtlasLightingResponse.Display(new Color(b.r * light, b.g * light, b.b * light, 1f));
                        into[n++] = new Color(with.r - without.r, with.g - without.g, with.b - without.b, 0f);
                    }
            }

            var seen = new Color[combos];
            var baked = new Color[Normals][];
            double total = 0.0, brightens = 0.0, weights = 0.0;
            var cost = new AtlasLayerCost();
            for (int k = 0; k < Views; k++)
            {
                float yaw = k * 2f * Mathf.PI / Views;
                var view = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                for (int i = 0; i < Normals; i++)
                {
                    var normal = Sphere[i];
                    // A sphere shows each normal by how much it faces the viewer.
                    float weight = Vector3.Dot(normal, view);
                    if (weight <= 0f) continue;
                    Effects(Look(normal, view), seen);
                    if (baked[i] == null)
                    {
                        // The bake looks each normal up from the virtual view, once for every viewer.
                        baked[i] = new Color[combos];
                        Effects(Look(normal, AtlasVirtualView.Direction(normal, Vector3.forward, AtlasMatcapBake.Frontal, AtlasMatcapBake.AlongNormal)), baked[i]);
                    }
                    float size = 0f;
                    for (int c = 0; c < combos; c++)
                    {
                        var e = seen[c];
                        size += (Mathf.Abs(e.r) + Mathf.Abs(e.g) + Mathf.Abs(e.b)) / 3f;
                        brightens += weight * (Mathf.Max(0f, e.r) + Mathf.Max(0f, e.g) + Mathf.Max(0f, e.b)) / 3f;
                        cost.Add(e, baked[i][c], weight);
                    }
                    total += weight * size;
                    weights += weight * combos;
                }
            }

            var measure = new AtlasMatcapMeasure
            {
                Impact = weights > 0.0 ? (float)(total / weights) : 0f,
                Brightens = total > 1e-9 ? (float)(brightens / total) : 0f,
                BakeLoss = cost.Impact > 1e-9f ? cost.Cost / cost.Impact : 0f
            };
            // The disc seen from the front, on a polar grid: each point's effect size, signed
            // effect and the matcap's saturation there. A ring's points weigh by its radius.
            var sizes = new float[Rings, Steps];
            var signs = new float[Rings, Steps];
            double tinted = 0.0, tintWeights = 0.0;
            for (int j = 0; j < Rings; j++)
            {
                float radius = (j + 0.5f) / Rings;
                for (int k = 0; k < Steps; k++)
                {
                    float angle = 2f * Mathf.PI * (k + 0.5f) / Steps;
                    var normal = new Vector3(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle), Mathf.Sqrt(1f - radius * radius));
                    var look = Look(normal, Vector3.forward);
                    Effects(look, seen);
                    float size = 0f, signed = 0f;
                    for (int c = 0; c < combos; c++)
                    {
                        var e = seen[c];
                        size += (Mathf.Abs(e.r) + Mathf.Abs(e.g) + Mathf.Abs(e.b)) / 3f;
                        signed += (e.r + e.g + e.b) / 3f;
                    }
                    sizes[j, k] = size / combos;
                    signs[j, k] = signed / combos;
                    float brightest = Mathf.Max(look.Color.r, Mathf.Max(look.Color.g, look.Color.b));
                    float darkest = Mathf.Min(look.Color.r, Mathf.Min(look.Color.g, look.Color.b));
                    tintWeights += radius * sizes[j, k];
                    if (brightest > 1e-4f) tinted += radius * sizes[j, k] * (brightest - darkest) / brightest;
                }
            }
            measure.Tint = tintWeights > 1e-9 ? (float)(tinted / tintWeights) : 0f;
            Shape(sizes, signs, measure);
            measure.Role = Role(measure);
            if (Measures.Count >= 256) Measures.Clear();
            Measures[key] = measure;
            return measure;
        }

        // How the effect is spread over the disc, seen from the front.
        private static void Shape(float[,] sizes, float[,] signs, AtlasMatcapMeasure measure)
        {
            float Radius(int ring) => (ring + 0.5f) / Rings;
            double weight = 0.0, effect = 0.0, signedSum = 0.0, absolute = 0.0, edge = 0.0;
            var points = new List<(float Weight, float Size)>(Rings * Steps);
            for (int j = 0; j < Rings; j++)
                for (int k = 0; k < Steps; k++)
                {
                    float r = Radius(j);
                    weight += r;
                    effect += r * sizes[j, k];
                    signedSum += r * signs[j, k];
                    absolute += r * Mathf.Abs(signs[j, k]);
                    if (r >= RingRadius) edge += r * sizes[j, k];
                    points.Add((r, sizes[j, k]));
                }
            measure.Evenness = 1f;
            if (effect <= 1e-9) return;
            // The strongest tenth of the disc.
            double tenth = weight * 0.1, taken = 0.0, strongest = 0.0;
            foreach (var p in points.OrderByDescending(p => p.Size))
            {
                double part = System.Math.Min(p.Weight, tenth - taken);
                if (part <= 0.0) break;
                strongest += part * p.Size;
                taken += part;
            }
            measure.Spread = (float)(strongest / effect);
            measure.EdgeWeight = (float)(edge / effect);

            float mean = (float)(signedSum / weight);
            double variation = 0.0, fine = 0.0;
            for (int j = 0; j < Rings; j++)
                for (int k = 0; k < Steps; k++)
                {
                    variation += Radius(j) * Mathf.Abs(signs[j, k] - mean);
                    // Each point against the average of its neighbors: two rings, two steps.
                    float sum = 0f;
                    int count = 0;
                    for (int dj = -2; dj <= 2; dj++)
                    {
                        if (j + dj < 0 || j + dj >= Rings) continue;
                        for (int dk = -2; dk <= 2; dk++)
                        {
                            sum += signs[j + dj, (k + dk + Steps) % Steps];
                            count++;
                        }
                    }
                    fine += Radius(j) * Mathf.Abs(signs[j, k] - sum / count);
                }
            measure.Evenness = absolute > 1e-9 ? Mathf.Clamp01((float)(1.0 - variation / absolute)) : 1f;
            measure.Detail = variation > 1e-9 ? (float)(fine / variation) : 0f;

            // The directions around the outer ring that show the effect.
            var directions = new float[Steps];
            float average = 0f;
            for (int k = 0; k < Steps; k++)
            {
                for (int j = 0; j < Rings; j++)
                    if (Radius(j) >= RingRadius) directions[k] += Radius(j) * sizes[j, k];
                average += directions[k] / Steps;
            }
            measure.Around = average > 1e-9f ? directions.Count(d => d >= 0.25f * average) / (float)Steps : 0f;
        }

        // The role, by the first limit that holds: toward the edge all around, then most of it in
        // one place or fine detail all over.
        private static AtlasMatcapRole Role(AtlasMatcapMeasure measure)
        {
            if (measure.EdgeWeight >= RimEdgeWeight && measure.Around >= RimAround) return AtlasMatcapRole.Rim;
            if (measure.Spread >= HighlightSpread) return AtlasMatcapRole.Highlight;
            if (measure.Detail >= HighlightDetail && measure.Evenness < DetailEvenness) return AtlasMatcapRole.Highlight;
            return AtlasMatcapRole.Lighting;
        }

        /// <summary>The surface's color with the matcap's layers on base color
        /// <paramref name="b"/>, under <paramref name="light"/>: surface blends before the light,
        /// overlay adds and glows after it, the part that follows the light lit.</summary>
        public static Color With(IReadOnlyList<AtlasLookupLayer> layers, Color b, Color matcap, float opacity, float light)
        {
            var c = b;
            var lit = Color.black;
            var glow = Color.black;
            foreach (var layer in layers)
            {
                float mix = Mathf.Clamp01(layer.BaseColorMix);
                var tinted = new Color(matcap.r * Mathf.Lerp(1f, c.r, mix), matcap.g * Mathf.Lerp(1f, c.g, mix), matcap.b * Mathf.Lerp(1f, c.b, mix), 1f);
                bool after = AtlasMatcapBake.AddsAfterLighting(layer);
                if (!after) c = AtlasBlendMath.Apply(layer.Blend, c, tinted, opacity * layer.Opacity);
                float added = (after ? layer.Opacity : 0f) + Mathf.Max(0f, layer.EmissionAmount);
                if (added <= 0f) continue;
                float unlit = AtlasMatcapBake.GlowShare(layer);
                glow += tinted * (opacity * added * unlit);
                lit += tinted * (opacity * added * (1f - unlit));
            }
            return new Color((c.r + lit.r) * light + glow.r, (c.g + lit.g) * light + glow.g, (c.b + lit.b) * light + glow.b, 1f);
        }

        /// <summary>The light a lighting gives a white surface in a scene, on average over the
        /// angles to the light.</summary>
        public static float LightLevel(AtlasLighting lighting, AtlasLookScene scene)
        {
            var ramp = AtlasLightingResponse.RampOf(lighting);
            float sum = 0f;
            for (int k = 0; k < AtlasLook.Angles; k++)
                sum += AtlasSourceSamples.Luminance(AtlasLightingResponse.Lit(lighting, Color.white, -1f + 2f * k / (AtlasLook.Angles - 1), scene, ramp));
            return sum / AtlasLook.Angles;
        }

        // Turns the lookup around its center, by degrees, as the bake does.
        private static Vector2 Rotate(Vector2 uv, float degrees)
        {
            if (degrees == 0f) return uv;
            float a = degrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            float x = uv.x - 0.5f, y = uv.y - 0.5f;
            return new Vector2(x * c + y * s + 0.5f, -x * s + y * c + 0.5f);
        }

        // Bilinear, repeating.
        private static Color Sample(Color[] pixels, Vector2 uv)
        {
            float x = (uv.x - Mathf.Floor(uv.x)) * TextureSize - 0.5f, y = (uv.y - Mathf.Floor(uv.y)) * TextureSize - 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            Color At(int i, int j) => pixels[((j % TextureSize + TextureSize) % TextureSize) * TextureSize + ((i % TextureSize + TextureSize) % TextureSize)];
            return Color.Lerp(Color.Lerp(At(x0, y0), At(x0 + 1, y0), fx), Color.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), fx), fy);
        }
    }
}
