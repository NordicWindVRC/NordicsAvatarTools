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
                if (output.Configuration.Sources == null) continue;
                var local = new HashSet<Material>();
                foreach (var entry in output.Configuration.Sources)
                {
                    if (entry == null || entry.Material == null) { warnings.Add(output.Configuration.OutputName + ": null source material."); continue; }
                    if (!local.Add(entry.Material)) warnings.Add(output.Configuration.OutputName + ": duplicate material " + entry.Material.name + ".");
                    NordicsAtlasOutput previous;
                    if (assignments.TryGetValue(entry.Material, out previous) && previous != output)
                        warnings.Add(entry.Material.name + " assigned to both " + previous.Configuration.OutputName + " and " + output.Configuration.OutputName + ".");
                    else assignments[entry.Material] = output;
                    if (!known.Contains(entry.Material)) warnings.Add(output.Configuration.OutputName + ": " + entry.Material.name + " is not in the currently detected source materials (it may have been removed or the sources changed).");
                    var r = entry.UVRegion;
                    if (r.width <= 0f || r.height <= 0f || r.x < 0f || r.y < 0f || r.xMax > 1f || r.yMax > 1f)
                        warnings.Add(output.Configuration.OutputName + ": invalid UV rectangle for " + entry.Material.name + ". Rectangles must be inside 0..1 and have positive size.");
                }
                if (output.Configuration.RegionPadding < 0) warnings.Add(output.Configuration.OutputName + ": padding must not be negative.");
                bool supported = false;
                foreach (var shader in AtlasShaderRegistry.OutputShaders) if (shader.ShaderId == output.Configuration.ShaderId) supported = true;
                if (!supported) warnings.Add(output.Configuration.OutputName + ": output shader ID is not registered: " + output.Configuration.ShaderId);
            }
            return warnings;
        }
    }
}
