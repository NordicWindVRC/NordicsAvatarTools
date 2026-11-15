using System.Collections.Generic;
using UnityEngine;
namespace NordicWind.AdvancedAtlaser.Editor
{
    internal static class AtlasConfigurationValidator
    {
        internal static List<string> Validate(GameObject owner, AtlasDiscoveryResult discovery)
        {
            var warnings = new List<string>();
            if (!string.IsNullOrEmpty(discovery.Warning)) warnings.Add(discovery.Warning);
            var known = new HashSet<Material>(discovery.Materials);
            var assignments = new Dictionary<Material, NordicsAtlasOutput>();
            foreach (var output in owner.GetComponents<NordicsAtlasOutput>())
            {
                if (output.configuration.sources == null) continue;
                var local = new HashSet<Material>();
                foreach (var entry in output.configuration.sources)
                {
                    if (entry == null || entry.material == null) { warnings.Add(output.configuration.outputName + ": null source material."); continue; }
                    if (!local.Add(entry.material)) warnings.Add(output.configuration.outputName + ": duplicate material " + entry.material.name + ".");
                    NordicsAtlasOutput previous;
                    if (assignments.TryGetValue(entry.material, out previous) && previous != output)
                        warnings.Add(entry.material.name + " assigned to both " + previous.configuration.outputName + " and " + output.configuration.outputName + ".");
                    else assignments[entry.material] = output;
                    if (!known.Contains(entry.material)) warnings.Add(output.configuration.outputName + ": " + entry.material.name + " is not in the currently detected source materials (it may have been removed or the sources changed).");
                    var r = entry.uvRegion;
                    if (r.width <= 0f || r.height <= 0f || r.x < 0f || r.y < 0f || r.xMax > 1f || r.yMax > 1f)
                        warnings.Add(output.configuration.outputName + ": invalid UV rectangle for " + entry.material.name + ". Rectangles must be inside 0..1 and have positive size.");
                }
                if (output.configuration.regionPadding < 0) warnings.Add(output.configuration.outputName + ": padding must not be negative.");
                var outputShader = AtlasShaderRegistry.Output(output.configuration.shaderId);
                if (outputShader == null)
                    warnings.Add(output.configuration.outputName + ": output shader ID is not registered: " + output.configuration.shaderId);
                else if (!AtlasShaderRegistry.IsInstalled(outputShader))
                    warnings.Add(output.configuration.outputName + ": " + outputShader.DisplayName +
                        " isn't installed in this project, so this output won't be built.");
            }
            return warnings;
        }
    }
}
