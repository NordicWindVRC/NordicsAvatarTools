using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Shading, lighting and shadows (ATLAS_OUTPUT.md, "Feature inventory"): one lighting setup,
    // the reference source's, which the writer maps to its shader's own modes. Where the sources
    // light differently, the user picks the source (Lighting from); automatic: the largest region.
    // Parts drawn on the texture (shadow masks, detail shadows, band and shade textures, SDF face
    // maps, skin thickness, cloth maps) aren't merged yet: they'd need maps in the atlas.
    public sealed class AtlasLightingFeature : IAtlasAlwaysOnFeature, IAtlasSharedValueFeature
    {
        public const string FeatureId = "lighting";

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode { Id = "native", Label = "One lighting setup", Kind = AtlasFeatureModeKind.Single }
        };

        public string Id => FeatureId;
        public string DisplayName => "Lighting";
        public AtlasFeatureSection Section => AtlasFeatureSection.Lighting;
        public int Order => 0;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;
        public string SharedValueName => "Lighting";

        public bool IsPresent(AtlasUnifiedMaterial source) => source != null;

        public bool SharedValuesDiffer(IReadOnlyList<AtlasUnifiedMaterial> sources, AtlasOutputCapabilities capabilities) =>
            sources.Skip(1).Any(s => AtlasFieldwise.Differences(sources[0].Lighting, s.Lighting).Count > 0);

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            int reference = Reference(context);
            var lighting = context.Output.Lighting;
            AtlasFieldwise.Copy(job.Sources[reference].Material.Lighting, lighting);
            bool painted = LeaveOutPainted(lighting);
            // An output that shades only through a ramp texture gets the bands' edge as keys,
            // which the build turns into one.
            var ramp = lighting.Ramp;
            if (context.Capabilities.GeneratedRamp && lighting.Family == AtlasLightingFamily.RampToon && ramp.Ramp == null &&
                ramp.Preset == AtlasRampPreset.None && ramp.Keys.Count == 0 && ramp.Bands.Count > 0)
                ramp.Keys.AddRange(AtlasRampTexture.FromBands(ramp));

            var differ = context.Sources.Where(i => i != reference &&
                AtlasFieldwise.Differences(job.Sources[reference].Material.Lighting, job.Sources[i].Material.Lighting).Count > 0).ToList();
            if (differ.Count > 0)
                context.Notes.Add($"Lighting is taken from source {reference + 1}; {Numbers(differ)} {(differ.Count == 1 ? "is" : "are")} lit differently.");
            if (painted)
                context.Notes.Add("Lighting parts drawn on the texture (shadow masks, detail shadows, band and shade textures, SDF face maps, skin thickness, cloth maps) aren't merged yet; the atlas is lit without them.");
        }

        // The chosen source when it's one of the output's, otherwise the one with the largest region.
        private static int Reference(AtlasFeatureContext context)
        {
            var choice = context.Job.Features.Find(f => f.FeatureId == FeatureId);
            if (choice != null && context.Sources.Contains(choice.ReferenceSource)) return choice.ReferenceSource;
            return context.Sources.OrderByDescending(i => context.Job.Sources[i].UVRegion.width * context.Job.Sources[i].UVRegion.height)
                .ThenBy(i => i).First();
        }

        // Clears what is drawn in the source's UV space: on the atlas it would land elsewhere.
        // The bands are copied first, since the source's own are shared. True when anything was.
        private static bool LeaveOutPainted(AtlasLighting lighting)
        {
            var ramp = lighting.Ramp;
            bool painted = ramp.TermMask != null || ramp.RampStrengthMask != null || lighting.Skin.Thickness != null || lighting.Cloth != null;
            ramp.TermMask = null;
            ramp.RampStrengthMask = null;
            lighting.Skin.Thickness = null;
            if (lighting.Cloth != null)
            {
                lighting.Cloth = null;
                lighting.Diffuse = AtlasDiffuseModel.Lambert;
            }
            if (ramp.Term == AtlasShadingTerm.SdfFace)
            {
                painted = true;
                ramp.Term = AtlasShadingTerm.HalfLambert;
                ramp.Sdf = null;
            }
            var bands = ramp.Bands.ToList();
            ramp.Bands.Clear();
            foreach (var band in bands)
            {
                var copy = new AtlasShadeBand();
                AtlasFieldwise.Copy(band, copy);
                if (copy.Color.Map != null)
                {
                    painted = true;
                    copy.Color.Map = null;
                }
                ramp.Bands.Add(copy);
            }
            return painted;
        }

        private static string Numbers(IEnumerable<int> indices)
        {
            var list = indices.Select(i => (i + 1).ToString()).ToList();
            return (list.Count == 1 ? "source " : "sources ") + string.Join(", ", list);
        }

        /// <summary>A copy with the settings that have no effect in its setup at their defaults,
        /// so two setups that light alike compare equal.</summary>
        public static AtlasLighting Canonical(AtlasLighting lighting)
        {
            var c = new AtlasLighting();
            AtlasFieldwise.Copy(lighting, c);
            var defaults = new AtlasLighting();
            if (c.Family == AtlasLightingFamily.Unlit)
            {
                var unlit = new AtlasLighting { Family = AtlasLightingFamily.Unlit };
                return unlit;
            }
            if (c.Family != AtlasLightingFamily.Flat) c.FlatLightMap = false;
            if (c.Family != AtlasLightingFamily.PhysicallyBased)
            {
                c.PhysicalModel = defaults.PhysicalModel;
                c.Diffuse = defaults.Diffuse;
                c.DisneyAmount = defaults.DisneyAmount;
            }
            if (c.Diffuse != AtlasDiffuseModel.Disney) c.DisneyAmount = defaults.DisneyAmount;
            if (c.Diffuse != AtlasDiffuseModel.PreintegratedSkin) AtlasFieldwise.Copy(defaults.Skin, c.Skin);
            // Skin uses the shadow tint its own way; the strength is the realistic type's.
            if (c.Family != AtlasLightingFamily.PhysicallyBased || c.Diffuse == AtlasDiffuseModel.PreintegratedSkin)
                c.ShadowStrength = defaults.ShadowStrength;
            if (c.Family != AtlasLightingFamily.PhysicallyBased) c.ShadowTint = defaults.ShadowTint;
            if (c.Family != AtlasLightingFamily.RampToon)
            {
                // The light map's term and the back-face shadow apply to every lit family.
                var term = c.Ramp.Term;
                float backFaces = c.Ramp.BackFaceShadow;
                AtlasFieldwise.Copy(defaults.Ramp, c.Ramp);
                c.Ramp.Term = term;
                c.Ramp.BackFaceShadow = backFaces;
            }
            if (c.Ambient != AtlasAmbientModel.Directional) c.DirectionalAmount = defaults.DirectionalAmount;
            if (c.ColorModel != AtlasLightColorModel.Floored) c.FloorIntensity = defaults.FloorIntensity;
            if (c.Direction != AtlasLightDirectionModel.Fixed)
            {
                c.LightDirection = defaults.LightDirection;
                c.DirectionLocal = defaults.DirectionLocal;
            }
            if (c.Direction != AtlasLightDirectionModel.View) c.ViewYaw = c.ViewPitch = 0f;
            if (!c.FixedLightColor) c.LightColor = defaults.LightColor;
            if (!c.LimitBrightness)
            {
                c.BrightnessCeiling = defaults.BrightnessCeiling;
                c.LimitByLuminance = defaults.LimitByLuminance;
                c.LimitKeepsHue = defaults.LimitKeepsHue;
            }
            var additional = c.AdditionalLights;
            if (!additional.LimitBrightness)
            {
                additional.BrightnessCeiling = defaults.AdditionalLights.BrightnessCeiling;
                additional.LimitKeepsHue = defaults.AdditionalLights.LimitKeepsHue;
            }
            if (additional.Shading != AtlasAdditionalLightShading.Toon)
            {
                additional.GradientStart = defaults.AdditionalLights.GradientStart;
                additional.GradientEnd = defaults.AdditionalLights.GradientEnd;
            }
            if (additional.Combine != AtlasLightCombine.Max) additional.MaxAlphaBoost = defaults.AdditionalLights.MaxAlphaBoost;
            if (!c.LightVolumes.Enabled) AtlasFieldwise.Copy(defaults.LightVolumes, c.LightVolumes);
            else if (!c.LightVolumes.Specular)
            {
                c.LightVolumes.SpecularStrength = defaults.LightVolumes.SpecularStrength;
                c.LightVolumes.SpecularMaxLights = defaults.LightVolumes.SpecularMaxLights;
            }
            if (!c.Ltcgi.Enabled) AtlasFieldwise.Copy(defaults.Ltcgi, c.Ltcgi);
            return c;
        }

        /// <summary>Whether the build generates a ramp texture for the output: one that shades
        /// only through a ramp, whose ramp is described by keys.</summary>
        public static bool NeedsGeneratedRamp(AtlasUnifiedMaterial output, AtlasOutputCapabilities capabilities) =>
            capabilities.GeneratedRamp && output.Lighting.Family == AtlasLightingFamily.RampToon &&
            output.Lighting.Ramp.Ramp == null && output.Lighting.Ramp.Keys.Count > 0;

        /// <summary>The lighting settings the written material doesn't hold the same way, read
        /// back by its parser: one line, or none.</summary>
        public static IEnumerable<string> Unkept(AtlasUnifiedMaterial output, Material material)
        {
            var parsed = AtlasShaderRegistry.Detect(material)?.Parse(material);
            if (parsed == null) yield break;
            var paths = AtlasFieldwise.Differences(Canonical(output.Lighting), Canonical(parsed.Lighting));
            if (paths.Count == 0) yield break;
            var names = paths.Select(p => string.Join(" ", p.Split('.').Select(ObjectNames.NicifyVariableName))).Distinct().ToList();
            yield return "The output shader can't set these lighting settings the same way: " + string.Join(", ", names) + ".";
        }
    }
}
