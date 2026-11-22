using System;
using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // The kept material (ATLAS_OUTPUT.md, "The look model"): the output unified material as
    // the output shader shows it, in unified terms. The writer maps the output unified material
    // into its shader's properties, and the shader's own parser reads them back, so what a writer
    // approximates or drops shows without rules of its own, and an output shader is described
    // once, by its parser: also in how it computes what it has no settings for (Described).
    public static class AtlasKeptMaterial
    {
        /// <summary>The written material read back by its shader's parser; null for a shader
        /// without one.</summary>
        public static AtlasUnifiedMaterial Read(Material written) =>
            written == null ? null : AtlasShaderRegistry.Detect(written)?.Parse(written);

        // What each output shader's parser reads of a material of the shader with nothing set,
        // by shader ID.
        private static readonly Dictionary<string, AtlasUnifiedMaterial> Shaders = new Dictionary<string, AtlasUnifiedMaterial>();

        /// <summary>The output shader as its own parser describes it: what it reads of a
        /// material of the shader with nothing set. How the shader computes what it has no
        /// settings for (its fresnel) is there, in unified terms. Null for capabilities that
        /// aren't a registered output shader's, or whose shader isn't installed or has no
        /// parser: the unified defaults apply.</summary>
        public static AtlasUnifiedMaterial Described(AtlasOutputCapabilities capabilities)
        {
            string id = capabilities?.ShaderId;
            if (string.IsNullOrEmpty(id)) return null;
            if (Shaders.TryGetValue(id, out var known)) return known;
            var shader = AtlasShaderRegistry.Output(id)?.FindShader();
            if (shader == null) return null;
            var scratch = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            AtlasUnifiedMaterial described;
            try
            {
                described = Read(scratch);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scratch);
            }
            if (described != null) Shaders[id] = described;
            return described;
        }

        /// <summary>A part of an output unified material as the output shader shows it: written
        /// alone to a scratch material of the shader and read back by its parser. Null where
        /// the shader isn't known (as for Described).</summary>
        public static AtlasUnifiedMaterial Shown(AtlasOutputCapabilities capabilities, AtlasUnifiedMaterial part)
        {
            var output = string.IsNullOrEmpty(capabilities?.ShaderId) ? null : AtlasShaderRegistry.Output(capabilities.ShaderId);
            var shader = output?.FindShader();
            if (shader == null) return null;
            var scratch = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                output.Write(part, scratch);
                return Read(scratch);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scratch);
            }
        }

        /// <summary>What the written material shows differently from what the plan asked for
        /// and from the sources' own looks, one line each: the lighting settings the shader can't
        /// set the same way, and the sources whose own lighting looks different, with the base
        /// color scales the bake applied to them.</summary>
        public static IEnumerable<string> Notes(AtlasGeneratorJob job, AtlasOutputPlan plan, Material written)
        {
            var kept = Read(written);
            if (kept == null) yield break;
            // A minimum light kept through the emission is a minimum brightness.
            if (plan.LightFloor > 0f) kept.Lighting.MinimumBrightness = Mathf.Max(kept.Lighting.MinimumBrightness, plan.LightFloor);
            foreach (var note in AtlasLightingFeature.Unkept(plan.Output, kept)) yield return note;
            foreach (var note in AtlasLook.LightingNotes(job, kept, plan.LevelApplied)) yield return note;
        }
    }

    // The lighting an output shader keeps, read before an atlas's material exists: the
    // lighting alone is written to a scratch material and read back by the shader's parser.
    // Tuning asks for many candidates, so the scratch material is reused.
    public sealed class AtlasKeptLighting : IDisposable
    {
        /// <summary>The output shader the lighting is read for.</summary>
        public IAtlasOutputShader Shader => shader;

        private readonly IAtlasOutputShader shader;
        private readonly IAtlasSourceShaderParser parser;
        private readonly Material scratch;
        private readonly AtlasUnifiedMaterial holder = new AtlasUnifiedMaterial();

        private AtlasKeptLighting(IAtlasOutputShader shader, IAtlasSourceShaderParser parser, Material scratch)
        {
            this.shader = shader;
            this.parser = parser;
            this.scratch = scratch;
        }

        /// <summary>A reader for the output shader; null where the shader isn't installed or
        /// has no parser.</summary>
        public static AtlasKeptLighting For(IAtlasOutputShader shader)
        {
            var unity = shader?.FindShader();
            if (unity == null) return null;
            var scratch = new Material(unity) { hideFlags = HideFlags.HideAndDontSave };
            var parser = AtlasShaderRegistry.Detect(scratch);
            if (parser != null) return new AtlasKeptLighting(shader, parser, scratch);
            UnityEngine.Object.DestroyImmediate(scratch);
            return null;
        }

        /// <summary>The lighting as the output shader shows it.</summary>
        public AtlasLighting Of(AtlasLighting lighting)
        {
            AtlasFieldwise.Copy(lighting, holder.Lighting);
            shader.Write(holder, scratch);
            var kept = parser.Parse(scratch).Lighting;
            // A ramp the build generates from keys isn't a texture yet: the written material
            // will read it back as the keys describe it.
            if (AtlasLightingFeature.NeedsGeneratedRamp(holder, shader.Capabilities)) kept.Ramp.Keys.AddRange(lighting.Ramp.Keys);
            return kept;
        }

        public void Dispose() => UnityEngine.Object.DestroyImmediate(scratch);
    }
}
