using System.Collections.Generic;
using System.Linq;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Emission (ATLAS_OUTPUT.md, "Feature inventory"): each source's static emission layers
    // added up into the output's emission map, relative to the strongest source's emission, which
    // the output glows at. Sources without emission are dark. Only the native mode: emission
    // baked into the color wouldn't glow.
    public sealed class AtlasEmissionFeature : IAtlasOutputFeature
    {
        private static readonly AtlasFeatureMode[] ModeList =
        {
            new AtlasFeatureMode
            {
                Id = "native", Label = "Emission map", Kind = AtlasFeatureModeKind.Native,
                RequiredMaps = new[] { AtlasVirtualMap.Emission }
            },
            AtlasFeatureMode.Off
        };

        public string Id => "emission";
        public string DisplayName => "Emission";
        public AtlasFeatureSection Section => AtlasFeatureSection.Layers;
        public int Order => 0;
        public IReadOnlyList<AtlasFeatureMode> Modes => ModeList;

        public bool IsPresent(AtlasUnifiedMaterial source) =>
            source != null && source.Layers.OfType<AtlasSurfaceLayer>().Any(l => l.Enabled &&
                l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission && t.Opacity > 0f) && l.Source.Color.maxColorComponent > 0f);

        public void Resolve(AtlasFeatureContext context)
        {
            var job = context.Job;
            var active = new HashSet<int>(context.Sources);
            float strength = active.Max(i => AtlasColorStack.EmissionStrength(job.Sources[i].Material));
            Notes(context, active);
            if (strength <= 0f) return;
            var request = new AtlasMapRequest
            {
                Map = AtlasVirtualMap.Emission,
                Reference = new AtlasTextureReference { Interpretation = AtlasTextureInterpretation.Color },
                Evaluate = (index, material, width, height) => active.Contains(index)
                    ? AtlasColorStack.EvaluateEmission(material, width, height, strength)
                    : AtlasMapEvaluator.Constant(AtlasVirtualMap.Emission, 0f, width, height)
            };
            for (int i = 0; i < job.Sources.Count; i++) request.Sources.Add(i);
            context.Maps.Add(request);
            // The output's emission: one layer, the map at the strongest source's strength.
            var layer = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
            layer.Source.Map = request.Reference;
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add, strength));
            context.Output.Layers.Add(layer);
        }

        // The output's emission layer, if any.
        public static AtlasSurfaceLayer Of(AtlasUnifiedMaterial output) =>
            output.Layers.OfType<AtlasSurfaceLayer>().FirstOrDefault(l => l.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission));

        private static void Notes(AtlasFeatureContext context, IEnumerable<int> active)
        {
            foreach (int i in active)
            {
                var material = context.Job.Sources[i].Material;
                for (int k = 0; k < material.Layers.Count; k++)
                {
                    if (!(material.Layers[k] is AtlasSurfaceLayer layer) || !layer.Enabled) continue;
                    if (layer.Targets.Any(t => t.Target == AtlasSurfaceTarget.Emission) && !AtlasColorStack.CanBakeEmission(material, k, layer))
                    {
                        context.Notes.Add("An emission that is live (blinking, scrolling, audio, light based), replaces the base color, follows transparency, or is masked by the mask bus or vertex colors isn't baked yet; it's left out.");
                        return;
                    }
                }
            }
        }
    }
}
