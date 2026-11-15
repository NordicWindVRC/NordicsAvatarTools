using UnityEngine;
using UnityEngine.Rendering;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Poiyomi 11 masks have "levels" (input and output range, midpoint, invert, normalize) and can
    /// add vertex-colour channels. The unified mask has no remap yet, so a material that changes
    /// any of them gets one report line instead of silently wrong masks.
    /// </summary>
    internal static class AtlasPoiyomiMaskLevels
    {
        private static readonly (string Suffix, Vector4 Default)[] Levels =
        {
            ("MLInputMin", Vector4.zero), ("MLInputMax", Vector4.one), ("MLMidpoint", new Vector4(.5f, .5f, .5f, .5f)),
            ("MLOutputMin", Vector4.zero), ("MLOutputMax", Vector4.one), ("MLInvert", Vector4.zero), ("MLNormalize", Vector4.zero),
            ("VertexMask", Vector4.zero)
        };

        public static void Report(Material m, AtlasUnifiedMaterial d)
        {
            foreach (string name in VectorNames(m))
            {
                foreach (var (suffix, value) in Levels)
                {
                    if (!name.EndsWith(suffix)) continue;
                    if (Vector4.Distance(AtlasMaterialReader.Vector(m, name, value), value) < 1e-4f) break;
                    AtlasMaterialReader.MissingFeature(d, "mask.levels",
                        "Mask levels or vertex-colour mask channels are used; masks are read without them.");
                    return;
                }
            }
        }

        private static System.Collections.Generic.IEnumerable<string> VectorNames(Material m)
        {
            var saved = AtlasMaterialReader.Saved(m);
            if (saved != null)
            {
                foreach (var name in saved.Vectors.Keys) yield return name;
                yield break;
            }
            var shader = m.shader;
            if (shader == null) yield break;
            for (int i = 0; i < shader.GetPropertyCount(); ++i)
                if (shader.GetPropertyType(i) == ShaderPropertyType.Vector) yield return shader.GetPropertyName(i);
        }
    }
}
