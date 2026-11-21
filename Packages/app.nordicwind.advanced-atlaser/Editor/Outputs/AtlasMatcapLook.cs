using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // What a matcap is for (ATLAS_OUTPUT.md, "What a matcap is for").
    public enum AtlasMatcapRole
    {
        // Colors the surface: the same effect all over.
        Tint,
        // Stands in for the world's light: soft shading, sky and ground.
        Lighting,
        // Lights or darkens the edges: its effect follows how far the surface faces the
        // viewer, and lies toward the edge.
        Rim,
        // Gloss, shine, glints: most of its effect in a small part.
        Highlight,
        // Shows surroundings: looked up by the reflection, or detail that moves with the view,
        // so that freezing it costs more than leaving it out.
        Reflection
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
        // The share of its variation over the disc that follows the facing (the distance from
        // the disc's center) alone: 1 for a matcap that is alike all around.
        public float FollowsFacing;
        public AtlasMatcapRole Role;

        public string RoleName => Role.ToString().ToLowerInvariant();

        /// <summary>The role and what it does, in a sentence.</summary>
        public string Describe()
        {
            string does = Role == AtlasMatcapRole.Tint ? "colors the surface"
                : Role == AtlasMatcapRole.Lighting ? "stands in for the world's light"
                : Role == AtlasMatcapRole.Rim ? "lights or darkens the edges"
                : Role == AtlasMatcapRole.Highlight ? "gloss, shine or glints"
                : "shows surroundings";
            string direction = Brightens >= 0.9f ? "brightening" : Brightens <= 0.1f ? "darkening" : "brightening and darkening";
            return $"A {RoleName} matcap ({does}), {direction}; it changes the displayed brightness by " +
                   Impact.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " on average where it shows.";
        }
    }

    public static class AtlasMatcapLook
    {
        // The limits between the roles, each on a measure of the matcap's own effect.
        // - A highlight carries at least half its effect in its strongest tenth.
        public const float HighlightSpread = 0.5f;
        // - A rim's variation follows the facing for three quarters or more, and at least three
        //   fifths of its effect is on the outer ring, which holds 0.44 of an even matcap's.
        public const float RimFollowsFacing = 0.75f, RimEdgeWeight = 0.6f;
        // - A tint varies by at most a tenth of its effect.
        public const float TintEvenness = 0.9f;
        // - Frozen at one view, a reflection costs at least what leaving it out does, and its
        //   variation doesn't follow the facing.
        public const float ReflectionBakeLoss = 1f;

        // Where the outer ring starts, as the facing N·V: three quarters of the disc's radius.
        private static readonly float RingFacing = Mathf.Sqrt(1f - 0.75f * 0.75f);
        private const int Normals = 400, Views = 8, Rings = 12, TextureSize = 32;
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
            // Seen from the front: each point's weight (its facing), effect size and signed effect.
            var front = new List<(float Weight, float Size, float Signed)>();
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
                    float size = 0f, signed = 0f;
                    for (int c = 0; c < combos; c++)
                    {
                        var e = seen[c];
                        size += (Mathf.Abs(e.r) + Mathf.Abs(e.g) + Mathf.Abs(e.b)) / 3f;
                        signed += (e.r + e.g + e.b) / 3f;
                        brightens += weight * (Mathf.Max(0f, e.r) + Mathf.Max(0f, e.g) + Mathf.Max(0f, e.b)) / 3f;
                        cost.Add(e, baked[i][c], weight);
                    }
                    total += weight * size;
                    weights += weight * combos;
                    if (k == 0) front.Add((weight, size / combos, signed / combos));
                }
            }

            var measure = new AtlasMatcapMeasure
            {
                Impact = weights > 0.0 ? (float)(total / weights) : 0f,
                Brightens = total > 1e-9 ? (float)(brightens / total) : 0f,
                BakeLoss = cost.Impact > 1e-9f ? cost.Cost / cost.Impact : 0f
            };
            Shape(front, measure);
            measure.Role = Role(first, measure);
            if (Measures.Count >= 256) Measures.Clear();
            Measures[key] = measure;
            return measure;
        }

        // How the effect is spread over the disc, seen from the front.
        private static void Shape(List<(float Weight, float Size, float Signed)> front, AtlasMatcapMeasure measure)
        {
            float weight = front.Sum(p => p.Weight), effect = front.Sum(p => p.Weight * p.Size);
            measure.Evenness = measure.FollowsFacing = 1f;
            if (effect <= 1e-9f) return;
            // The strongest tenth of the disc.
            float tenth = weight * 0.1f, taken = 0f, strongest = 0f;
            foreach (var p in front.OrderByDescending(p => p.Size))
            {
                float part = Mathf.Min(p.Weight, tenth - taken);
                if (part <= 0f) break;
                strongest += part * p.Size;
                taken += part;
            }
            measure.Spread = strongest / effect;
            float mean = front.Sum(p => p.Weight * p.Signed) / weight;
            float signed = front.Sum(p => p.Weight * Mathf.Abs(p.Signed));
            float variation = front.Sum(p => p.Weight * Mathf.Abs(p.Signed - mean));
            measure.Evenness = signed > 1e-9f ? Mathf.Clamp01(1f - variation / signed) : 1f;
            measure.EdgeWeight = front.Where(p => p.Weight <= RingFacing).Sum(p => p.Weight * p.Size) / effect;
            // What is left of the variation within rings of the same facing.
            int Ring(float facing) => Mathf.Min(Rings - 1, Mathf.FloorToInt(Mathf.Sqrt(Mathf.Max(0f, 1f - facing * facing)) * Rings));
            var weights = new float[Rings];
            var sums = new float[Rings];
            foreach (var p in front)
            {
                weights[Ring(p.Weight)] += p.Weight;
                sums[Ring(p.Weight)] += p.Weight * p.Signed;
            }
            float within = front.Sum(p => p.Weight * Mathf.Abs(p.Signed - sums[Ring(p.Weight)] / weights[Ring(p.Weight)]));
            measure.FollowsFacing = variation > 1e-9f ? Mathf.Clamp01(1f - within / variation) : 1f;
        }

        // The role, by the first limit that holds: how it is looked up, whether it follows the
        // facing toward the edge, how much of it is in one place, how even it is, and what
        // freezing it would cost.
        private static AtlasMatcapRole Role(AtlasLookupLayer layer, AtlasMatcapMeasure measure)
        {
            if (layer.Projection == AtlasLookupProjection.ReflectionPlanar) return AtlasMatcapRole.Reflection;
            bool followsFacing = measure.FollowsFacing >= RimFollowsFacing;
            if (followsFacing && measure.EdgeWeight >= RimEdgeWeight) return AtlasMatcapRole.Rim;
            if (measure.Spread >= HighlightSpread) return AtlasMatcapRole.Highlight;
            if (measure.Evenness >= TintEvenness) return AtlasMatcapRole.Tint;
            if (!followsFacing && measure.BakeLoss >= ReflectionBakeLoss) return AtlasMatcapRole.Reflection;
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
