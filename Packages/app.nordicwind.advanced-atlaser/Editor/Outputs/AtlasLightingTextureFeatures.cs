using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Shadow colors (3.2): the band and shade textures of banded lighting (Poiyomi's multilayer
    // math and shade map), each source's band baked into the output's shadow color texture for
    // that band; white (the band's color alone) where a source has none.
    public sealed class AtlasShadowColorsFeature : IAtlasOutputFeature
    {
        public const string FeatureId = "shadow-colors";
        private static readonly AtlasVirtualMap[] Maps = { AtlasVirtualMap.ShadowColor1, AtlasVirtualMap.ShadowColor2, AtlasVirtualMap.ShadowColor3 };

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Shadow color textures", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.ShadowColor1 },
                OptionalMaps = new[] { AtlasVirtualMap.ShadowColor2, AtlasVirtualMap.ShadowColor3 }
            },
            AtlasFeatureMode.Off
        };

        public string Id => FeatureId;
        public string DisplayName => "Shadow Colors";
        public AtlasFeatureSection Section => AtlasFeatureSection.Lighting;
        public int Order => 2;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        public bool IsPresent(AtlasUnifiedMaterial source) =>
            source != null && source.Lighting.Ramp.Bands.Any(b => b.Color.Map?.Texture != null);

        private static AtlasShadeBand Band(AtlasUnifiedMaterial source, int index) =>
            index < source.Lighting.Ramp.Bands.Count ? source.Lighting.Ramp.Bands[index] : null;

        public void Resolve(AtlasFeatureContext context)
        {
            var bands = context.Output.Lighting.Ramp.Bands;
            if (context.Output.Lighting.Family != AtlasLightingFamily.RampToon || bands.Count == 0)
            {
                context.Notes.Add("Shadow color textures apply to banded lighting (multilayer math, shade map); the atlas's lighting isn't, so they're left out.");
                return;
            }
            var job = context.Job;
            var active = new HashSet<int>(context.Sources);
            bool elsewhere = false;
            for (int k = 0; k < Mathf.Min(Maps.Length, bands.Count); k++)
            {
                int index = k;
                if (!context.Capabilities.Provides(Maps[k]) || !active.Any(i => Band(job.Sources[i].Material, index)?.Color.Map?.Texture != null)) continue;
                elsewhere |= active.Any(i => Band(job.Sources[i].Material, index)?.Color.Map is AtlasTextureReference map && !Bakeable(map));
                var reference = AtlasSpecularFeature.Request(context, Maps[k], active, 1f, (m, w, h) => Evaluate(m, index, w, h));
                reference.Interpretation = AtlasTextureInterpretation.Color;
                bands[k].Color.Map = reference;
            }
            if (elsewhere)
                context.Notes.Add("A band or shade texture on another UV, scrolling or animated isn't baked yet; the band's color is used there.");
        }

        private static bool Bakeable(AtlasTextureReference map) =>
            map.Texture is Texture2D && map.Space == AtlasTextureSpace.UV0 && map.Decal == null && map.Scroll == Vector2.zero && map.Frames == null;

        private static NordicsTextureWrapper Evaluate(AtlasUnifiedMaterial source, int index, int width, int height)
        {
            var map = Band(source, index)?.Color.Map;
            return map != null && Bakeable(map)
                ? AtlasMapEvaluator.SampleColor(map, (Texture2D)map.Texture, width, height)
                : AtlasMapEvaluator.Constant(AtlasVirtualMap.ShadowColor1, 1f, width, height);
        }
    }

    // Skin thickness (3.3): the pre-integrated skin's thickness mask, each source's baked into
    // the output's skin thickness texture; 0 where a source has none.
    public sealed class AtlasSkinThicknessFeature : IAtlasOutputFeature
    {
        public const string FeatureId = "skin-thickness";

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Skin thickness texture", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.SkinThickness }
            },
            AtlasFeatureMode.Off
        };

        public string Id => FeatureId;
        public string DisplayName => "Skin Thickness";
        public AtlasFeatureSection Section => AtlasFeatureSection.Lighting;
        public int Order => 3;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        private static AtlasMask Thickness(AtlasUnifiedMaterial source) =>
            source.Lighting.Diffuse == AtlasDiffuseModel.PreintegratedSkin ? source.Lighting.Skin.Thickness : null;

        public bool IsPresent(AtlasUnifiedMaterial source) => source != null && Thickness(source) != null;

        public void Resolve(AtlasFeatureContext context)
        {
            if (context.Output.Lighting.Diffuse != AtlasDiffuseModel.PreintegratedSkin)
            {
                context.Notes.Add("Skin thickness applies to skin lighting; the atlas's lighting isn't, so it's left out.");
                return;
            }
            var active = new HashSet<int>(context.Sources);
            context.Output.Lighting.Skin.Thickness = new AtlasMask
            {
                Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.SkinThickness, active, 0f,
                    (m, w, h) => Thickness(m) is AtlasMask mask ? AtlasMapEvaluator.ScaledMask(mask, 1f, w, h, m)
                        : AtlasMapEvaluator.Constant(AtlasVirtualMap.SkinThickness, 0f, w, h)),
                Channel = AtlasChannel.Red
            };
        }
    }
}
