using System.Collections.Generic;
using System.Linq;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Shadow masks and detail shadows (3.2): the ramp strength mask (how strongly the shadow
    // shows) and the term mask (shadows drawn in), each source's baked into the output's
    // lighting masks texture; 1 (no change) where a source has none. They apply to the output's
    // ramp toon lighting, the lighting feature's setup.
    public sealed class AtlasLightingMasksFeature : IAtlasOutputFeature
    {
        public const string FeatureId = "lighting-masks";

        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Lighting masks texture", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.ShadowStrengthMask, AtlasVirtualMap.DetailShadow }
            },
            AtlasFeatureMode.Off
        };

        public string Id => FeatureId;
        public string DisplayName => "Shadow Masks";
        public AtlasFeatureSection Section => AtlasFeatureSection.Lighting;
        public int Order => 1;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        public bool IsPresent(AtlasUnifiedMaterial source) =>
            source != null && (source.Lighting.Ramp.RampStrengthMask != null || source.Lighting.Ramp.TermMask != null);

        public void Resolve(AtlasFeatureContext context)
        {
            var lighting = context.Output.Lighting;
            if (lighting.Family != AtlasLightingFamily.RampToon)
            {
                context.Notes.Add("Shadow masks and detail shadows apply to ramp toon lighting; the atlas's lighting isn't, so they're left out.");
                return;
            }
            var job = context.Job;
            var active = new HashSet<int>(context.Sources);
            var texture = context.Capabilities.TextureFor(AtlasVirtualMap.ShadowStrengthMask);
            if (active.Any(i => job.Sources[i].Material.Lighting.Ramp.RampStrengthMask != null))
                lighting.Ramp.RampStrengthMask = new AtlasMask
                {
                    Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.ShadowStrengthMask, active, 1f,
                        (m, w, h) => AtlasMapEvaluator.ScaledMask(m.Lighting.Ramp.RampStrengthMask, 1f, w, h, m)),
                    Channel = Channel(texture, AtlasVirtualMap.ShadowStrengthMask)
                };
            if (active.Any(i => job.Sources[i].Material.Lighting.Ramp.TermMask != null))
                lighting.Ramp.TermMask = new AtlasMask
                {
                    Map = AtlasSpecularFeature.Request(context, AtlasVirtualMap.DetailShadow, active, 1f,
                        (m, w, h) => AtlasMapEvaluator.ScaledMask(m.Lighting.Ramp.TermMask, 1f, w, h, m)),
                    Channel = Channel(texture, AtlasVirtualMap.DetailShadow)
                };
            var materials = active.Select(i => job.Sources[i].Material).ToList();
            if (materials.Any(m => (m.Lighting.Ramp.RampStrengthMask != null && !AtlasMapEvaluator.CanEvaluate(m.Lighting.Ramp.RampStrengthMask, m)) ||
                                   (m.Lighting.Ramp.TermMask != null && !AtlasMapEvaluator.CanEvaluate(m.Lighting.Ramp.TermMask, m))))
                context.Notes.Add("A shadow mask or detail shadow on another UV or a global mask written by layers or the second pass isn't baked yet; it counts as 1.");
        }

        // The channel the texture holds a one-value map in.
        private static AtlasChannel Channel(AtlasOutputTexture texture, AtlasVirtualMap map)
        {
            var channels = texture?.Channels.Find(c => c.Map == map)?.Channels ?? AtlasTextureChannels.R;
            switch (channels)
            {
                case AtlasTextureChannels.G: return AtlasChannel.Green;
                case AtlasTextureChannels.B: return AtlasChannel.Blue;
                case AtlasTextureChannels.A: return AtlasChannel.Alpha;
                default: return AtlasChannel.Red;
            }
        }
    }
}
