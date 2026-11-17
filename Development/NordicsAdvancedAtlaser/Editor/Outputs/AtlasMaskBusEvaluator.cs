using System;
using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // A source's mask bus (UNIFIED_MATERIAL_FEATURES.md, 1.5) baked over its UV 0–1: each channel
    // starts at 1 and runs its steps in order. Steps that change live are baked as the avatar is
    // normally seen (ATLAS_OUTPUT.md, "Masks"): front faces, outside mirrors and VRChat cameras,
    // the camera far away, no blacklight near and no contact touching. Vertex colors come from
    // AtlasVertexColors. Layer coverage and the second pass aren't baked yet; a channel with one
    // of them isn't baked.
    public static class AtlasMaskBusEvaluator
    {
        // Steps whose value changes while the avatar is seen.
        public static bool IsLive(AtlasMaskBusStepKind kind) =>
            kind == AtlasMaskBusStepKind.Faces || kind == AtlasMaskBusStepKind.Mirror || kind == AtlasMaskBusStepKind.Camera ||
            kind == AtlasMaskBusStepKind.Distance || kind == AtlasMaskBusStepKind.Blacklight || kind == AtlasMaskBusStepKind.Contact;

        public static bool CanBake(AtlasMaskBusStep step)
        {
            switch (step.Kind)
            {
                case AtlasMaskBusStepKind.LayerCoverage:
                case AtlasMaskBusStepKind.SecondPass:
                    return false;
                case AtlasMaskBusStepKind.Texture:
                    return step.Mask == null || (step.Mask.Bus < 0 && AtlasMapEvaluator.CanEvaluate(step.Mask));
                default:
                    return true;
            }
        }

        public static bool CanBake(AtlasUnifiedMaterial source, int channel) =>
            source != null && channel >= 0 && Steps(source, channel).All(CanBake);

        // Whether the source's bus has a step that changes live.
        public static bool HasLiveSteps(AtlasUnifiedMaterial source) =>
            source != null && source.MaskBus.Steps.Any(s => IsLive(s.Kind));

        private static IEnumerable<AtlasMaskBusStep> Steps(AtlasUnifiedMaterial source, int channel) =>
            source.MaskBus.Steps.Where(s => s.Channel == channel);

        /// <summary>The channel's value in RGB, over the source's UV 0–1. Only for channels that
        /// CanBake.</summary>
        public static NordicsTextureWrapper Channel(AtlasUnifiedMaterial source, int channel, int width, int height) =>
            AtlasEvaluationCache.Copy(("bus", source, channel, width, height), () => Compute(source, channel, width, height));

        private static NordicsTextureWrapper Compute(AtlasUnifiedMaterial source, int channel, int width, int height)
        {
            var value = AtlasMapEvaluator.Constant(AtlasVirtualMap.Alpha, 1f, width, height);
            try
            {
                foreach (var step in Steps(source, channel))
                {
                    if (step.Kind == AtlasMaskBusStepKind.Texture)
                    {
                        // value = the mask's value
                        var mask = AtlasMapEvaluator.ScaledMask(step.Mask, 1f, width, height);
                        value.Dispose();
                        value = mask;
                    }
                    else if (step.Kind == AtlasMaskBusStepKind.VertexColor)
                    {
                        // value = saturate(blend(value, vertex color channel)), sRGB decoded when linear
                        using (var vertex = AtlasVertexColors.Channel(source, step.VertexChannel, step.VertexLinear, width, height))
                            if (vertex != null) Blend(value, vertex, step.Blend);
                            else Each(value, v => Blend(v, 1f, step.Blend));
                    }
                    else Apply(value, step);
                }
                return value;
            }
            catch
            {
                value.Dispose();
                throw;
            }
        }

        private static void Apply(NordicsTextureWrapper value, AtlasMaskBusStep step)
        {
            switch (step.Kind)
            {
                case AtlasMaskBusStepKind.Faces:
                    // Front faces: 0 where only the back face counts.
                    if (step.Faces == AtlasFaces.Back) Each(value, v => 0f);
                    return;
                case AtlasMaskBusStepKind.Mirror:
                case AtlasMaskBusStepKind.Camera:
                    // Outside mirrors and cameras: 0 where only inside counts.
                    if (step.Inside) Each(value, v => 0f);
                    return;
                case AtlasMaskBusStepKind.Distance:
                    // smoothstep(min, max, distance) with the camera far away.
                    float far = step.Max >= step.Min ? 1f : 0f;
                    float distance = Mathf.Lerp(step.MinValue, step.MaxValue, far);
                    Each(value, v => Blend(v, distance, step.Blend));
                    return;
                case AtlasMaskBusStepKind.Offset:
                    Each(value, v => Mathf.Clamp01(v + step.Amount));
                    return;
                case AtlasMaskBusStepKind.Remap:
                    Each(value, v => step.Min + (step.Max - step.Min) * v);
                    return;
                case AtlasMaskBusStepKind.ForceOn:
                    Each(value, v => 1f);
                    return;
                case AtlasMaskBusStepKind.ForceOff:
                    Each(value, v => 0f);
                    return;
                case AtlasMaskBusStepKind.Blacklight:
                    // Poiyomi applies it only while a matching blacklight is near.
                    return;
                case AtlasMaskBusStepKind.Contact:
                    // No touch: 0, or 1 inverted.
                    float touch = step.Contact != null && step.Contact.Invert ? 1f : 0f;
                    Each(value, v => Blend(v, touch, step.Blend));
                    return;
            }
        }

        /// <summary>Poiyomi's maskBlend: the base combined with the blend value, then saturated.</summary>
        public static float Blend(float value, float blend, AtlasMaskBlend mode)
        {
            float result;
            switch (mode)
            {
                case AtlasMaskBlend.Replace: result = blend; break;
                case AtlasMaskBlend.Subtract: result = value - blend; break;
                case AtlasMaskBlend.Multiply: result = value * blend; break;
                case AtlasMaskBlend.Divide: result = value / Mathf.Max(blend, 0.0001f); break;
                case AtlasMaskBlend.Min: result = Mathf.Min(value, blend); break;
                case AtlasMaskBlend.Max: result = Mathf.Max(value, blend); break;
                case AtlasMaskBlend.Average: result = (value + blend) * 0.5f; break;
                default: result = value + blend; break;
            }
            return Mathf.Clamp01(result);
        }

        /// <summary>Combines a value map (in RGB) with a bus channel of the same size by the blend.</summary>
        public static void Blend(NordicsTextureWrapper value, NordicsTextureWrapper channel, AtlasMaskBlend mode) =>
            AtlasPixels.Map(value, channel, (c, b) =>
            {
                float v = Blend(c.r, b.r, mode);
                return new Color(v, v, v, c.a);
            });

        /// <summary>A function on every pixel's value, in RGB.</summary>
        public static void Each(NordicsTextureWrapper value, Func<float, float> function) =>
            AtlasPixels.Map(value, c =>
            {
                float v = function(c.r);
                return new Color(v, v, v, c.a);
            });
    }
}
