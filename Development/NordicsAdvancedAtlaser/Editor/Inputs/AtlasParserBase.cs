using System;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    public abstract class AtlasParserBase : IAtlasSourceShaderParser
    {
        public abstract string ParserId { get; }
        public abstract string DisplayName { get; }
        public virtual string Family => null;
        public virtual int Priority => 50;
        public abstract bool CanParse(Material material);
        protected static string Name(Material m) => AtlasMaterialReader.ShaderName(m);

        public AtlasUnifiedMaterial Parse(Material material)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            AtlasMaterialReader.ClearCache();
            try
            {
                var output = new AtlasUnifiedMaterial();
                if (AtlasMaterialReader.Saved(material) != null)
                    output.Report(AtlasIssueKind.Note, "shader.not-installed",
                        "The shader isn't installed, so the material's saved values were read as " + DisplayName + ".");
                Populate(material, output);
                return output;
            }
            finally { AtlasMaterialReader.ClearCache(); }
        }

        protected abstract void Populate(Material m, AtlasUnifiedMaterial output);
    }
}
