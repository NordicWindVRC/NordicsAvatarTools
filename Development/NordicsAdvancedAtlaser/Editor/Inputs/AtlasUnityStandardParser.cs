using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Unity's built-in Standard shader, metallic and specular setup. Behaviour follows the
    /// shader's keywords, as the shader does, and every main map uses the albedo's tiling and
    /// offset. See UNIFIED_MATERIAL_UNITY_STANDARD.md.
    /// </summary>
    public abstract class AtlasUnityStandardParserBase : AtlasParserBase
    {
        protected abstract bool SpecularSetup { get; }

        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            bool smoothnessFromAlbedo = Keyword(m, "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");

            // Rendering Mode
            if (Keyword(m, "_ALPHATEST_ON"))
            {
                d.Render.SurfaceMode = AtlasSurfaceMode.Cutout;
                d.Render.Cutoff = AtlasMaterialReader.Float(m, "_Cutoff", 0.5f);
            }
            else if (Keyword(m, "_ALPHABLEND_ON")) d.Render.SurfaceMode = AtlasSurfaceMode.Fade;
            else if (Keyword(m, "_ALPHAPREMULTIPLY_ON")) d.Render.SurfaceMode = AtlasSurfaceMode.Transparent;
            d.Render.Pass.DepthWrite = AtlasMaterialReader.Float(m, "_ZWrite", 1f) > 0.5f;

            // Albedo. With smoothness in the albedo's alpha, alpha is the colour's alone.
            d.Surface.BaseColor.Color = AtlasMaterialReader.ColorLinear(m, "_Color", Color.white);
            d.Surface.BaseColor.Map = Main(m, "_MainTex");
            if (smoothnessFromAlbedo) d.Surface.Alpha.Source = AtlasAlphaSource.BaseColorConstantAlpha;

            // Metallic or Specular: a map replaces the slider or colour.
            var response = d.Surface.Response;
            string glossMap = SpecularSetup ? "_SpecGlossMap" : "_MetallicGlossMap";
            bool hasGlossMap = Keyword(m, SpecularSetup ? "_SPECGLOSSMAP" : "_METALLICGLOSSMAP");
            if (SpecularSetup)
            {
                response.SpecularColor = hasGlossMap
                    ? new AtlasColorInput { Map = Main(m, glossMap) }
                    : new AtlasColorInput { Color = AtlasMaterialReader.ColorLinear(m, "_SpecColor", new Color(0.2f, 0.2f, 0.2f).linear) };
            }
            else if (hasGlossMap)
            {
                response.Metallic.Constant = 1f;
                response.Metallic.Map = Main(m, glossMap, AtlasTextureInterpretation.Data);
            }
            else
            {
                // The slider is gamma-encoded ([Gamma]); the shading sees it in linear.
                response.Metallic.Constant = Mathf.GammaToLinearSpace(AtlasMaterialReader.Float(m, "_Metallic", 0f));
            }

            // Smoothness: a map's alpha times the scale, or the slider alone.
            response.Roughness.InvertResult = true;
            if (smoothnessFromAlbedo || hasGlossMap)
            {
                response.Roughness.Constant = AtlasMaterialReader.Float(m, "_GlossMapScale", 1f);
                response.Roughness.Map = Main(m, smoothnessFromAlbedo ? "_MainTex" : glossMap, AtlasTextureInterpretation.Data);
                response.Roughness.Channel = AtlasChannel.Alpha;
            }
            else response.Roughness.Constant = AtlasMaterialReader.Float(m, "_Glossiness", 0.5f);

            // Forward Rendering Options
            response.Highlights.Enabled = !Keyword(m, "_SPECULARHIGHLIGHTS_OFF");
            if (Keyword(m, "_GLOSSYREFLECTIONS_OFF")) response.Reflections.Source = AtlasReflectionSource.AmbientColor;

            // Normal Map
            bool normalMaps = Keyword(m, "_NORMALMAP");
            if (normalMaps)
            {
                d.Surface.Normal.Map = Main(m, "_BumpMap", AtlasTextureInterpretation.TangentNormal);
                d.Surface.Normal.Strength = AtlasMaterialReader.Float(m, "_BumpScale", 1f);
            }

            // Height Map: green channel, single-step parallax. Without a map there is no height.
            var height = Main(m, "_ParallaxMap", AtlasTextureInterpretation.Data);
            if (Keyword(m, "_PARALLAXMAP") && height != null)
            {
                var parallax = d.Surface.Parallax;
                parallax.Enabled = true;
                parallax.Height.Map = height;
                parallax.Height.Channel = AtlasChannel.Green;
                parallax.Depth = AtlasMaterialReader.Float(m, "_Parallax", 0.02f);
            }

            // Occlusion: green channel, strength around one.
            response.Occlusion.Map = Main(m, "_OcclusionMap", AtlasTextureInterpretation.Data);
            response.Occlusion.Channel = AtlasChannel.Green;
            response.Occlusion.Constant = AtlasMaterialReader.Float(m, "_OcclusionStrength", 1f);

            // Emission: texture × HDR colour, added after lighting.
            if (Keyword(m, "_EMISSION"))
            {
                var emission = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission };
                emission.Source.Color = AtlasMaterialReader.ColorLinear(m, "_EmissionColor", Color.black);
                emission.Source.Map = Main(m, "_EmissionMap");
                emission.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add));
                d.Layers.Add(emission);
            }

            // Secondary Maps: detail albedo ×2 and detail normal, faded in by the detail mask's
            // alpha (on the main tiling). The detail normal needs the normal-map keyword.
            if (Keyword(m, "_DETAIL_MULX2")) AtlasSourceMapping.UnityDetail(m, d, normalMaps);

            // Unity's physically based lighting: Disney diffuse, GGX, full light probes, shadows.
            d.Lighting.Family = AtlasLightingFamily.PhysicallyBased;
            d.Lighting.Diffuse = AtlasDiffuseModel.Disney;
            d.Lighting.Ambient = AtlasAmbientModel.Directional;
        }

        // Main maps use the albedo's tiling and offset.
        private static AtlasTextureReference Main(Material m, string property,
            AtlasTextureInterpretation interpretation = AtlasTextureInterpretation.Color) =>
            AtlasSourceMapping.Texture(m, property, interpretation, "_MainTex");

        private static bool Keyword(Material m, string keyword) => AtlasMaterialReader.Keyword(m, keyword);
    }

    public sealed class AtlasUnityStandardParser : AtlasUnityStandardParserBase
    {
        public override string ParserId => "standard";
        public override string DisplayName => "Unity Standard";
        protected override bool SpecularSetup => false;
        public override bool CanParse(Material m) => Name(m) == "Standard";
    }

    public sealed class AtlasUnityStandardSpecularParser : AtlasUnityStandardParserBase
    {
        public override string ParserId => "standard-specular";
        public override string DisplayName => "Unity Standard (Specular setup)";
        protected override bool SpecularSetup => true;
        public override bool CanParse(Material m) => Name(m) == "Standard (Specular setup)";
    }
}
