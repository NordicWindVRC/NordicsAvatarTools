using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The kept material (ATLAS_OUTPUT.md, "The look model"): the output unified material as
    // the output shader shows it, in unified terms. The writer maps the output unified material
    // into its shader's properties, and the shader's own parser reads them back, so what a writer
    // approximates or drops shows without rules of its own, and an output shader is described
    // once, by its parser.
    public static class AtlasKeptMaterial
    {
        /// <summary>The written material read back by its shader's parser; null for a shader
        /// without one.</summary>
        public static AtlasUnifiedMaterial Read(Material written) =>
            written == null ? null : AtlasShaderRegistry.Detect(written)?.Parse(written);

        /// <summary>What the written material shows differently from what the plan asked for
        /// and from the sources' own looks, one line each: the lighting settings the shader can't
        /// set the same way, and the sources whose own lighting looks different.</summary>
        public static IEnumerable<string> Notes(AtlasGeneratorJob job, AtlasUnifiedMaterial output, Material written)
        {
            var kept = Read(written);
            if (kept == null) yield break;
            foreach (var note in AtlasLightingFeature.Unkept(output, kept)) yield return note;
            foreach (var note in AtlasLook.LightingNotes(job, kept)) yield return note;
        }
    }
}
