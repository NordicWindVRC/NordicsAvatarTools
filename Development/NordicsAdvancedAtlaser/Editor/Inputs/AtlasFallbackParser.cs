using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Any material no other parser recognises. It reads only the property names most shaders
    /// share, with Unity's conventions, so its mapping is a guess and is reported as one.
    /// </summary>
    public sealed class AtlasFallbackParser : AtlasParserBase
    {
        public override string ParserId => "fallback";
        public override string DisplayName => "Fallback";
        public override int Priority => -1000;
        public override bool CanParse(Material material) => material != null;

        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            d.Report(AtlasIssueKind.Unmapped, "material",
                "Unrecognised shader: only common property names were read, so the result is a guess.");

            // Base colour: texture × colour.
            if (Has(m, "_MainTex"))
                AtlasSourceMapping.Color(m, d.Surface.BaseColor, "_MainTex", Has(m, "_Color") ? "_Color" : "_BaseColor");
            else if (Has(m, "_BaseMap"))
                AtlasSourceMapping.Color(m, d.Surface.BaseColor, "_BaseMap", "_BaseColor");
            else
                AtlasSourceMapping.Color(m, d.Surface.BaseColor, "_BaseColorMap", "_BaseColor");

            // Alpha from a separate mask, when there is one.
            var alphaMask = AtlasSourceMapping.Texture(m, First(m, "_AlphaMask", "_OpacityMask"), AtlasTextureInterpretation.Data);
            if (alphaMask != null)
            {
                d.Surface.Alpha.Source = AtlasAlphaSource.Mask;
                d.Surface.Alpha.Mask = new AtlasMask { Map = alphaMask };
            }

            // Normal map.
            d.Surface.Normal.Map = AtlasSourceMapping.Texture(m, First(m, "_BumpMap", "_NormalMap", "_NormalTex", "_NormalTexture"),
                AtlasTextureInterpretation.TangentNormal);
            d.Surface.Normal.Strength = Float(m, 1f, "_BumpScale", "_NormalStrength", "_NormalScale");

            // Metallic workflow when the shader has metallic; specular colour when it has only that.
            var response = d.Surface.Response;
            string metallicMap = First(m, "_MetallicGlossMap", "_MetallicMap");
            if (Has(m, "_Metallic") || metallicMap != null)
            {
                response.Metallic.Constant = Float(m, 1f, "_Metallic");
                response.Metallic.Map = AtlasSourceMapping.Texture(m, metallicMap, AtlasTextureInterpretation.Data);
            }
            else
            {
                string specularColor = First(m, "_SpecColor", "_SpecularColor");
                string specularMap = First(m, "_SpecGlossMap", "_SpecularMap");
                if (specularColor != null || specularMap != null)
                {
                    response.SpecularColor = new AtlasColorInput();
                    AtlasSourceMapping.Color(m, response.SpecularColor, specularMap, specularColor);
                }
            }

            // Roughness, or smoothness turned into roughness.
            string roughnessMap = First(m, "_RoughnessMap");
            string smoothnessMap = First(m, "_SmoothnessMap");
            if (Has(m, "_Roughness") || roughnessMap != null)
            {
                response.Roughness.Constant = Float(m, 1f, "_Roughness");
                response.Roughness.Map = AtlasSourceMapping.Texture(m, roughnessMap, AtlasTextureInterpretation.Data);
            }
            else if (Has(m, "_Smoothness") || Has(m, "_Glossiness") || smoothnessMap != null)
            {
                response.Roughness.InvertResult = true;
                response.Roughness.Constant = Float(m, smoothnessMap != null || metallicMap != null ? 1f : 0.5f, "_Smoothness", "_Glossiness");
                if (smoothnessMap != null)
                    response.Roughness.Map = AtlasSourceMapping.Texture(m, smoothnessMap, AtlasTextureInterpretation.Data);
                else if (metallicMap != null)
                {
                    // Unity's packing: smoothness in the metallic map's alpha.
                    response.Roughness.Map = AtlasSourceMapping.Texture(m, metallicMap, AtlasTextureInterpretation.Data);
                    response.Roughness.Channel = AtlasChannel.Alpha;
                }
            }

            // Occlusion: Unity's green channel, strength around one.
            response.Occlusion.Map = AtlasSourceMapping.Texture(m, First(m, "_OcclusionMap", "_AOMap"), AtlasTextureInterpretation.Data);
            response.Occlusion.Channel = AtlasChannel.Green;
            response.Occlusion.Constant = Float(m, 1f, "_OcclusionStrength", "_AOIntensity");

            // Emission: texture × colour × strength, when there's a texture or a colour that glows.
            var emissionMap = AtlasSourceMapping.Texture(m, First(m, "_EmissionMap", "_EmissionTex", "_EmissiveMap"));
            Color emission = AtlasMaterialReader.ColorLinear(m, First(m, "_EmissionColor", "_EmissionTint"), emissionMap != null ? Color.white : Color.black)
                * Float(m, 1f, "_EmissionStrength", "_EmissionIntensity");
            if (emission.maxColorComponent > 0f)
            {
                var layer = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
                layer.Source.Color = new Color(emission.r, emission.g, emission.b, 1f);
                layer.Source.Map = emissionMap;
                layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add));
                d.Layers.Add(layer);
            }

            // A matcap's blend can't be guessed.
            if (AtlasMaterialReader.Texture(m, First(m, "_Matcap", "_MatCap")) != null)
                d.Report(AtlasIssueKind.Unmapped, "matcap", "A matcap texture was found, but its blend can't be guessed.");

            Render(m, d);
        }

        // Unity's conventional render properties: culling, depth write, cutout and blend factors.
        private static void Render(Material m, AtlasUnifiedMaterial d)
        {
            int cull = Mathf.RoundToInt(Float(m, 2f, "_Cull", "_CullMode"));
            d.Render.Pass.Cull = cull == 0 ? AtlasCull.Off : cull == 1 ? AtlasCull.Front : AtlasCull.Back;
            d.Render.Pass.DepthWrite = Float(m, 1f, "_ZWrite") > 0.5f;
            d.Render.Cutoff = Float(m, 0.5f, "_Cutoff");

            int source = Mathf.RoundToInt(Float(m, 1f, "_SrcBlend"));
            int destination = Mathf.RoundToInt(Float(m, 0f, "_DstBlend"));
            const int one = (int)AtlasBlendFactor.One, zero = (int)AtlasBlendFactor.Zero;
            const int sourceAlpha = (int)AtlasBlendFactor.SourceAlpha, oneMinusSourceAlpha = (int)AtlasBlendFactor.OneMinusSourceAlpha;
            if (source == sourceAlpha && destination == oneMinusSourceAlpha) d.Render.SurfaceMode = AtlasSurfaceMode.Fade;
            else if (source == one && destination == oneMinusSourceAlpha) d.Render.SurfaceMode = AtlasSurfaceMode.Transparent;
            else if (source == one && destination == zero)
                d.Render.SurfaceMode = AtlasMaterialReader.Keyword(m, "_ALPHATEST_ON") ? AtlasSurfaceMode.Cutout : AtlasSurfaceMode.Opaque;
            else if (source >= 0 && source <= 10 && destination >= 0 && destination <= 10)
            {
                d.Render.SurfaceMode = AtlasSurfaceMode.Custom;
                d.Render.SourceBlend = (AtlasBlendFactor)source;
                d.Render.DestinationBlend = (AtlasBlendFactor)destination;
            }
        }

        private static bool Has(Material m, string name) => AtlasMaterialReader.Has(m, name);

        // The first of the names the material has, or null.
        private static string First(Material m, params string[] names)
        {
            foreach (var name in names) if (Has(m, name)) return name;
            return null;
        }

        // The first of the names' values, or the fallback.
        private static float Float(Material m, float fallback, params string[] names)
        {
            string name = First(m, names);
            return name == null ? fallback : AtlasMaterialReader.Float(m, name, fallback);
        }
    }
}
