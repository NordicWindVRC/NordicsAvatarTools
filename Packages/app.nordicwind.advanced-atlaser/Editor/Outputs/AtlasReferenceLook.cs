using System;
using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The light at a sample point: how it shades a surface color (S2), and the direct and
    // ambient light that a layer following the light takes.
    public sealed class AtlasLookLight
    {
        public Func<Color, Color> Lit;
        public float Direct, Ambient;

        /// <summary>A lighting in a scene, at an angle to the light.</summary>
        public static AtlasLookLight At(AtlasLighting lighting, Color[] ramp, AtlasLookScene scene, float nDotL)
        {
            AtlasLightingResponse.Light(lighting, scene, nDotL, out float direct, out float ambient);
            return new AtlasLookLight { Lit = c => AtlasLightingResponse.Lit(lighting, c, nDotL, scene, ramp), Direct = direct, Ambient = ambient };
        }

        /// <summary>A lighting in a scene on average over the angles to the light: the light
        /// it gives a white surface, for layers looked up by the view alone, whose place on
        /// the surface doesn't follow the light.</summary>
        public static AtlasLookLight Average(AtlasLighting lighting, AtlasLookScene scene)
        {
            var ramp = AtlasLightingResponse.RampOf(lighting);
            float sum = 0f;
            for (int k = 0; k < AtlasLook.Angles; k++)
                sum += AtlasSourceSamples.Luminance(AtlasLightingResponse.Lit(lighting, Color.white, -1f + 2f * k / (AtlasLook.Angles - 1), scene, ramp));
            return Even(sum / AtlasLook.Angles);
        }

        /// <summary>The same light on every surface: the color × <paramref name="level"/>.</summary>
        public static AtlasLookLight Even(float level) =>
            new AtlasLookLight { Lit = c => new Color(c.r * level, c.g * level, c.b * level, 1f), Direct = level };
    }

    // A view layer (a matcap, a rim) at a sample point: what it shows there, and where in the
    // evaluation it acts. A layer can act at several stages (a matcap that blends and glows).
    public struct AtlasLookLayer
    {
        // Its color × its texture there, before its base color mix.
        public Color Color;
        public float BaseColorMix;
        // The blend of its surface and overlay parts.
        public AtlasBlend Blend;
        // S1: the opacity of its blend into the surface color, before the light.
        public float Surface;
        // S3–S4: the opacity of its blend with the lit color, its color × the light it follows.
        public float Overlay;
        public AtlasLightResponse Response;
        // S4: the amount of its color added × the direct and ambient light.
        public float Lit;
        // S5: the amount of its color added without light.
        public float Unlit;
    }

    // A sample point of a source: one of its base colors, in a scene, at an angle to the light.
    public struct AtlasLookPoint
    {
        public Color Base;
        public AtlasLookScene Scene;
        public float NDotL;
        public AtlasLookLight Light;
    }

    // The reference look (ATLAS_OUTPUT.md, "The reference look"): the displayed color of a
    // sample point of a surface with its view layers, computed in the unified evaluation
    // stages. The fits and the measures that compare a layer in a source with the layer in the
    // output describe the layer (AtlasLookLayer) and take the stages and the light from here.
    // - S1: the layers on the surface blend into the base color, in order.
    // - S2: the light shades the result (AtlasLookLight, from AtlasLightingResponse).
    // - S3–S4: overlays blend with the lit color, by the light they follow; lit adds take the
    //   direct and ambient light.
    // - S5: unlit adds (emission placements, glows).
    // Not part of it: highlights and reflections (AtlasSpecularResponse has its own model),
    // received shadows, occlusion, masks, and what the lighting's look leaves out
    // (AtlasLightingResponse).
    public static class AtlasReferenceLook
    {
        // The angles to the light a view layer's effect is taken at, N·L from −1 to 1.
        public const int LightAngles = 5;

        /// <summary>The linear color of a base color with its layers under a light.</summary>
        public static Color Linear(Color baseColor, IReadOnlyList<AtlasLookLayer> layers, AtlasLookLight light)
        {
            var c = baseColor;
            var added = Color.black;
            bool overlays = false;
            float total = light.Direct + light.Ambient;
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                // A layer mixes in the surface color as it is before its own blend.
                var tinted = Tinted(layer, c);
                if (layer.Surface > 0f) c = AtlasBlendMath.Apply(layer.Blend, c, tinted, layer.Surface);
                float amount = layer.Lit * total + layer.Unlit;
                if (amount > 0f) added += tinted * amount;
                overlays |= layer.Overlay > 0f;
            }
            var result = light.Lit(c);
            if (overlays)
            {
                var surface = baseColor;
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    var tinted = Tinted(layer, surface);
                    if (layer.Surface > 0f) surface = AtlasBlendMath.Apply(layer.Blend, surface, tinted, layer.Surface);
                    if (layer.Overlay <= 0f) continue;
                    float response = layer.Response == AtlasLightResponse.ScaleByLight ? total
                        : layer.Response == AtlasLightResponse.ScaleByDirectLight ? light.Direct : 1f;
                    result = AtlasBlendMath.Apply(layer.Blend, result, tinted * response, layer.Overlay);
                }
            }
            return new Color(result.r + added.r, result.g + added.g, result.b + added.b, 1f);
        }

        private static Color Tinted(AtlasLookLayer layer, Color surface) =>
            new Color(layer.Color.r * Mathf.Lerp(1f, surface.r, layer.BaseColorMix), layer.Color.g * Mathf.Lerp(1f, surface.g, layer.BaseColorMix),
                layer.Color.b * Mathf.Lerp(1f, surface.b, layer.BaseColorMix), 1f);

        /// <summary>S2, a highlight from a direct light of 1, without its fresnel: GGX's
        /// distribution × the Smith-GGX correlated visibility × N·L, at a perceptual
        /// roughness.</summary>
        public static float Highlight(float roughness, float nDotL, float nDotV, float nDotH)
        {
            float alpha = Mathf.Max(roughness * roughness, 0.0045f);
            float a2 = alpha * alpha;
            float denominator = nDotH * nDotH * (a2 - 1f) + 1f;
            float distribution = a2 / (Mathf.PI * denominator * denominator + 1e-6f);
            float visibility = Mathf.Clamp01(0.5f / Mathf.Max(Mathf.Lerp(2f * nDotL * nDotV, nDotL + nDotV, alpha), 1e-5f));
            return distribution * visibility * nDotL;
        }

        /// <summary>S2, how far the fresnel of highlights and of reflections rises from F0
        /// toward 1 at the angles (0–1), by the response's fresnel; the rise of reflections ×
        /// their fresnel strength.</summary>
        public static void FresnelRise(AtlasSurfaceResponse response, float vDotH, float nDotV, out float highlights, out float reflections)
        {
            float strength = Mathf.Max(0f, response.Reflections.FresnelStrength);
            if (response.Fresnel == AtlasFresnel.ViewHalfTimesViewNormal)
            {
                highlights = Mathf.Pow((1f - vDotH) * (1f - nDotV), 5f);
                reflections = highlights * strength;
                return;
            }
            highlights = Mathf.Pow(1f - vDotH, 5f);
            reflections = Mathf.Pow(1f - nDotV, 5f) * strength;
        }

        /// <summary>S2, the highlights and reflections added to the diffuse light: each × its
        /// fresnel, F0 + (1 − F0) × its rise, both × the response's reflectance fade.
        /// <paramref name="highlight"/> and <paramref name="reflection"/> are what shows
        /// without fresnel: the highlight × the direct light, and the environment, each × its
        /// strength.</summary>
        public static float Specular(AtlasSurfaceResponse response, float f0, float reflectance, float metallic, float highlight, float highlightRise,
            float reflection, float reflectionRise)
        {
            float fade = response.ReflectanceFade > 0f ? Mathf.Clamp01((reflectance + metallic) / response.ReflectanceFade) : 1f;
            return (highlight * (f0 + (1f - f0) * highlightRise) + reflection * (f0 + (1f - f0) * reflectionRise)) * fade;
        }

        /// <summary>The look as displayed (sRGB).</summary>
        public static Color Shown(Color baseColor, IReadOnlyList<AtlasLookLayer> layers, AtlasLookLight light) =>
            AtlasLightingResponse.Display(Linear(baseColor, layers, light));

        /// <summary>The layers' effect: the displayed look with them minus the look without
        /// them, per channel.</summary>
        public static Color Effect(Color baseColor, IReadOnlyList<AtlasLookLayer> layers, AtlasLookLight light)
        {
            var with = Shown(baseColor, layers, light);
            var without = AtlasLightingResponse.Display(light.Lit(baseColor));
            return new Color(with.r - without.r, with.g - without.g, with.b - without.b, 0f);
        }

        /// <summary>The sample points of a set of base colors under a lighting: each color in
        /// each scene, over the angles to the light.</summary>
        public static List<AtlasLookPoint> Points(IReadOnlyList<Color> bases, AtlasLighting lighting)
        {
            lighting = lighting ?? new AtlasLighting();
            var ramp = AtlasLightingResponse.RampOf(lighting);
            var points = new List<AtlasLookPoint>(bases.Count * AtlasLook.Scenes.Length * LightAngles);
            foreach (var color in bases)
                foreach (var scene in AtlasLook.Scenes)
                    for (int k = 0; k < LightAngles; k++)
                    {
                        float nDotL = -1f + 2f * k / (LightAngles - 1);
                        points.Add(new AtlasLookPoint { Base = color, Scene = scene, NDotL = nDotL, Light = AtlasLookLight.At(lighting, ramp, scene, nDotL) });
                    }
            return points;
        }
    }
}
