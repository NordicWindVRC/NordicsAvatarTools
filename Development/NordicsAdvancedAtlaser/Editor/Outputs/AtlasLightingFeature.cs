using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Shading, lighting and shadows (ATLAS_OUTPUT.md, "Feature inventory"): one lighting setup,
    // the reference source's, which the writer maps to its shader's own modes. Where the sources
    // light differently, the user picks the source (Lighting from); automatic: the largest region.
    // Parts drawn on the texture are baked by their own features (shadow masks, shadow colors,
    // skin thickness) or, for an SDF face, kept live on preserved UVs; cloth maps aren't merged yet.
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
            sources.Skip(1).Any(s => Differ(sources[0].Lighting, s.Lighting));

        // Whether two sources are lit differently, the parts baked per source (shadow masks,
        // detail shadows, shadow color textures, skin thickness) aside.
        private static bool Differ(AtlasLighting a, AtlasLighting b) =>
            AtlasFieldwise.Differences(WithoutBaked(a), WithoutBaked(b)).Count > 0;

        private static AtlasLighting WithoutBaked(AtlasLighting lighting)
        {
            var copy = new AtlasLighting();
            AtlasFieldwise.Copy(lighting, copy);
            copy.Ramp.TermMask = null;
            copy.Ramp.RampStrengthMask = null;
            copy.Skin.Thickness = null;
            CopyBands(copy.Ramp);
            foreach (var band in copy.Ramp.Bands) band.Color.Map = null;
            return copy;
        }

        // The ramp's own band copies, so changing them leaves the source's alone.
        private static void CopyBands(AtlasRampToon ramp)
        {
            var bands = ramp.Bands.ToList();
            ramp.Bands.Clear();
            foreach (var band in bands)
            {
                var copy = new AtlasShadeBand();
                AtlasFieldwise.Copy(band, copy);
                ramp.Bands.Add(copy);
            }
        }

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var capabilities = context.Capabilities;
            int reference = Reference(context);
            var lighting = context.Output.Lighting;
            AtlasFieldwise.Copy(job.Sources[reference].Material.Lighting, lighting);
            var sources = context.Sources.Select(i => job.Sources[i].Material.Lighting).ToList();
            var leftOut = new List<string>();
            var masked = context.Sources.Where(i => job.Sources[i].Material.Lighting.Ramp.RampStrengthMask != null ||
                                                    job.Sources[i].Material.Lighting.Ramp.TermMask != null).ToList();
            if (masked.Count > 0 && !capabilities.Provides(AtlasVirtualMap.ShadowStrengthMask))
                context.Notes.Add($"The output shader has no shadow masks or detail shadows; those of {Numbers(masked)} are left out.");

            // What is drawn in the sources' UV space is baked by its own feature (shadow masks,
            // shadow colors, skin thickness) or kept on preserved UVs (an SDF face); the copy
            // starts without it.
            var ramp = lighting.Ramp;
            ramp.TermMask = null;
            ramp.RampStrengthMask = null;
            CopyBands(ramp);
            foreach (var band in ramp.Bands) band.Color.Map = null;
            if (sources.Any(l => l.Ramp.Bands.Any(b => b.Color.Map != null)) && !capabilities.Provides(AtlasVirtualMap.ShadowColor1))
                leftOut.Add("band and shade textures");
            lighting.Skin.Thickness = null;
            if (sources.Any(l => l.Diffuse == AtlasDiffuseModel.PreintegratedSkin && l.Skin.Thickness != null) && !capabilities.Provides(AtlasVirtualMap.SkinThickness))
                leftOut.Add("skin thickness");
            if (lighting.Cloth != null)
            {
                lighting.Cloth = null;
                lighting.Diffuse = AtlasDiffuseModel.Lambert;
                leftOut.Add("cloth maps");
            }
            if (ramp.Term == AtlasShadingTerm.SdfFace && !KeepSdf(context, lighting))
            {
                ramp.Term = AtlasShadingTerm.HalfLambert;
                ramp.Sdf = null;
                leftOut.Add("SDF face maps");
            }

            // An output that shades only through a ramp texture gets the bands' edge as keys,
            // which the build turns into one.
            if (capabilities.GeneratedRamp && lighting.Family == AtlasLightingFamily.RampToon && ramp.Ramp == null &&
                ramp.Preset == AtlasRampPreset.None && ramp.Keys.Count == 0 && ramp.Bands.Count > 0)
                ramp.Keys.AddRange(AtlasRampTexture.FromBands(ramp));

            var differ = context.Sources.Where(i => i != reference &&
                Differ(job.Sources[reference].Material.Lighting, job.Sources[i].Material.Lighting)).ToList();
            if (differ.Count > 0)
                context.Notes.Add($"Lighting is taken from source {reference + 1}; {Numbers(differ)} {(differ.Count == 1 ? "is" : "are")} lit differently.");
            if (leftOut.Count > 0)
                context.Notes.Add($"Lighting drawn on the texture isn't merged here ({string.Join(", ", leftOut)}); the atlas is lit without it.");
        }

        // The reference source's SDF face texture, kept as it is and read through every source's
        // own UVs in a free channel: the SDF is sampled mirrored by the light's side, which only
        // works in the texture's own UV space. False where the output can't.
        private static bool KeepSdf(AtlasFeatureContext context, AtlasLighting lighting)
        {
            var sdf = lighting.Ramp.Sdf;
            var map = sdf?.Map;
            if (map?.Texture == null || !context.Capabilities.SdfFace || map.Space < AtlasTextureSpace.UV0 || map.Space > AtlasTextureSpace.UV3) return false;
            int channel = context.Plan.AllocateUVChannel(context.Capabilities.UVChannels);
            if (channel < 0) return false;
            for (int i = 0; i < context.Job.Sources.Count; i++)
                context.Plan.Remap(i).PreservedUVs.Add(new AtlasPreservedUV { Channel = channel, SourceChannel = map.Space - AtlasTextureSpace.UV0 });
            var copy = new AtlasSdfFace();
            AtlasFieldwise.Copy(sdf, copy);
            copy.Map = new AtlasTextureReference();
            AtlasFieldwise.Copy(map, copy.Map);
            copy.Map.Space = AtlasTextureSpace.UV0 + channel;
            lighting.Ramp.Sdf = copy;
            return true;
        }

        // The chosen source when it's one of the output's, otherwise the one with the largest region.
        private static int Reference(AtlasFeatureContext context)
        {
            var choice = context.Job.Features.Find(f => f.FeatureId == FeatureId);
            if (choice != null && context.Sources.Contains(choice.ReferenceSource)) return choice.ReferenceSource;
            return context.Sources.OrderByDescending(i => context.Job.Sources[i].UVRegion.width * context.Job.Sources[i].UVRegion.height)
                .ThenBy(i => i).First();
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

        /// <summary>The darkest the lighting lets the surface get, as a share of its base color: the
        /// largest of the unified floors (the minimum brightness added like emission, the minimum
        /// light, and the floored light color's ambient floor of 0.05 × its intensity). For writers
        /// with one control for it.</summary>
        public static float Floor(AtlasLighting lighting) =>
            Mathf.Max(lighting.MinimumBrightness, Mathf.Max(lighting.MinimumLight,
                lighting.ColorModel == AtlasLightColorModel.Floored ? 0.05f * lighting.FloorIntensity : 0f));

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
