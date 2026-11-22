using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Output shaders declare what they can express and their texture layouts; the shared
    // output features decide what to produce. Registrations are discovered automatically by
    // AtlasShaderRegistry.

    // Poiyomi's writer, one for its versions and variants: Poiyomi 11 keeps 10's property
    // names, and Pro has Toon's, so they share the texture layout, the capabilities and the
    // writer. Each registration says its major version and its variant, which add what that
    // version and Pro have. A property a version doesn't declare isn't set on its material.
    public abstract class PoiyomiOutputShader : IAtlasOutputShader
    {
        private readonly int major;
        private readonly bool pro;

        protected PoiyomiOutputShader(int major, AtlasPoiyomiVariant variant = AtlasPoiyomiVariant.Toon)
        {
            this.major = major;
            pro = variant == AtlasPoiyomiVariant.Pro;
            Capabilities = Build(major, pro);
        }

        public abstract string ShaderId { get; }
        public abstract string DisplayName { get; }
        public string Family => AtlasShaderFamilies.Poiyomi;
        // The major version × 10, plus 1 for Pro.
        public int Rank => major * 10 + (pro ? 1 : 0);

        // Every version names its shader the same; one installed next to a newer version lives
        // under Old Versions. The shader's master label says which version it is.
        public Shader FindShader()
        {
            string variant = pro ? "Poiyomi Pro" : "Poiyomi Toon";
            foreach (string name in new[] { ".poiyomi/" + variant, ".poiyomi/Old Versions/" + major + ".0/" + variant })
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;
                int found = AtlasPoiyomiVersionDetector.DetectFromMetadata(shader.name,
                    UnityEditor.AssetDatabase.GetAssetPath(shader), AtlasPoiyomiVersionDetector.GetShaderMasterLabel(shader));
                if (found == major) return shader;
            }
            return null;
        }

        public AtlasOutputCapabilities Capabilities { get; }

        private static AtlasOutputCapabilities Build(int major, bool pro)
        {
            var capabilities = new AtlasOutputCapabilities
            {
                Textures =
                {
                    new AtlasOutputTexture("BaseColor", "Base color") { SizeMode = AtlasTextureSizeMode.Main }
                        .With(AtlasVirtualMap.BaseColor, AtlasTextureChannels.RGB)
                        .With(AtlasVirtualMap.Alpha, AtlasTextureChannels.A),
                    NormalTexture(),
                    // Poiyomi's packed maps (_MochieMetallicMaps): R metallic, G smoothness, B reflection
                    // mask, A specular mask, read as they are (multipliers 1).
                    new AtlasOutputTexture("Metallic", "Metallic and smoothness") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false }
                        .With(AtlasVirtualMap.Metallic, AtlasTextureChannels.R, AtlasOutputTexture.Gray(0f))
                        .With(AtlasVirtualMap.Roughness, AtlasTextureChannels.G, AtlasOutputTexture.Gray(0f), invert: true)
                        .With(AtlasVirtualMap.ReflectionMask, AtlasTextureChannels.B, AtlasOutputTexture.Gray(0f))
                        .With(AtlasVirtualMap.SpecularMask, AtlasTextureChannels.A, AtlasOutputTexture.Gray(0f)),
                    // _LightingAOMaps: occlusion in R.
                    new AtlasOutputTexture("Occlusion", "Ambient occlusion") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false }
                        .With(AtlasVirtualMap.Occlusion, AtlasTextureChannels.R, AtlasOutputTexture.Gray(1f)),
                    EmissionTexture(),
                    // _EmissionMap2 and 3: emissions whose hue cycles.
                    new AtlasOutputTexture("EmissionCycle1", "Cycling emission 1") { SizeMode = AtlasTextureSizeMode.MainAspect, MainDivisor = 2 }
                        .With(AtlasVirtualMap.EmissionCycle1, AtlasTextureChannels.RGB, new Color(0f, 0f, 0f, 1f)),
                    new AtlasOutputTexture("EmissionCycle2", "Cycling emission 2") { SizeMode = AtlasTextureSizeMode.MainAspect, MainDivisor = 2 }
                        .With(AtlasVirtualMap.EmissionCycle2, AtlasTextureChannels.RGB, new Color(0f, 0f, 0f, 1f)),
                    // _MainColorAdjustTexture: R where the base color's hue cycles.
                    new AtlasOutputTexture("ColorAdjust", "Hue cycling mask") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false, MainDivisor = 2 }
                        .With(AtlasVirtualMap.HueCyclingMask, AtlasTextureChannels.R, AtlasOutputTexture.Gray(1f)),
                    // _EmissionMap1: emission shown through a live global mask channel.
                    new AtlasOutputTexture("EmissionLive", "Live emission") { SizeMode = AtlasTextureSizeMode.MainAspect, MainDivisor = 2 }
                        .With(AtlasVirtualMap.EmissionLive, AtlasTextureChannels.RGB, new Color(0f, 0f, 0f, 1f)),
                    // _LightingShadowMasks reads R, _LightingDetailShadowMaps G (by their channel strengths).
                    new AtlasOutputTexture("LightingMasks", "Shadow and detail masks") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false }
                        .With(AtlasVirtualMap.ShadowStrengthMask, AtlasTextureChannels.R, AtlasOutputTexture.Gray(1f))
                        .With(AtlasVirtualMap.DetailShadow, AtlasTextureChannels.G, AtlasOutputTexture.Gray(1f)),
                    // _ShadowColorTex / _1st_ShadeMap, _Shadow2ndColorTex / _2nd_ShadeMap, _Shadow3rdColorTex.
                    new AtlasOutputTexture("ShadowColor1", "Shadow color 1") { SizeMode = AtlasTextureSizeMode.MainAspect }
                        .With(AtlasVirtualMap.ShadowColor1, AtlasTextureChannels.RGBA),
                    new AtlasOutputTexture("ShadowColor2", "Shadow color 2") { SizeMode = AtlasTextureSizeMode.MainAspect }
                        .With(AtlasVirtualMap.ShadowColor2, AtlasTextureChannels.RGBA),
                    new AtlasOutputTexture("ShadowColor3", "Shadow color 3") { SizeMode = AtlasTextureSizeMode.MainAspect }
                        .With(AtlasVirtualMap.ShadowColor3, AtlasTextureChannels.RGBA),
                    // _SkinThicknessMap reads R.
                    new AtlasOutputTexture("SkinThickness", "Skin thickness") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false }
                        .With(AtlasVirtualMap.SkinThickness, AtlasTextureChannels.R, AtlasOutputTexture.Gray(0f)),
                    MaskTexture(1, AtlasVirtualMap.Mask1, AtlasVirtualMap.Mask2, AtlasVirtualMap.Mask3, AtlasVirtualMap.Mask4),
                    MaskTexture(2, AtlasVirtualMap.Mask5, AtlasVirtualMap.Mask6, AtlasVirtualMap.Mask7, AtlasVirtualMap.Mask8),
                    // _ClearCoatMaps: R strength, G smoothness, B reflection mask, A specular mask.
                    new AtlasOutputTexture("ClearCoat", "Clear coat maps") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false }
                        .With(AtlasVirtualMap.ClearCoatStrength, AtlasTextureChannels.R, AtlasOutputTexture.Gray(0f))
                        .With(AtlasVirtualMap.ClearCoatRoughness, AtlasTextureChannels.G, AtlasOutputTexture.Gray(0f), invert: true)
                        .With(AtlasVirtualMap.ClearCoatReflectionMask, AtlasTextureChannels.B, AtlasOutputTexture.Gray(0f))
                        .With(AtlasVirtualMap.ClearCoatSpecularMask, AtlasTextureChannels.A, AtlasOutputTexture.Gray(0f)),
                    // _DetailMask: R the detail texture's strength, G the detail normal's.
                    new AtlasOutputTexture("DetailMask", "Detail mask") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false, MainDivisor = 2 }
                        .With(AtlasVirtualMap.DetailTextureMask, AtlasTextureChannels.R, AtlasOutputTexture.Gray(1f))
                        .With(AtlasVirtualMap.DetailNormalMask, AtlasTextureChannels.G, AtlasOutputTexture.Gray(1f)),
                    // _SSSThicknessMap: RGB the subsurface color (black where a source has none), A the thickness.
                    new AtlasOutputTexture("Subsurface", "Subsurface color and thickness") { SizeMode = AtlasTextureSizeMode.MainAspect, MainDivisor = 2 }
                        .With(AtlasVirtualMap.SubsurfaceColor, AtlasTextureChannels.RGB, Color.black)
                        .With(AtlasVirtualMap.SubsurfaceThickness, AtlasTextureChannels.A, AtlasOutputTexture.Gray(1f)),
                    // _BacklightColorTex: the backlight's color, black where a source has none.
                    new AtlasOutputTexture("Backlight", "Backlight color") { SizeMode = AtlasTextureSizeMode.MainAspect, MainDivisor = 2 }
                        .With(AtlasVirtualMap.BacklightColor, AtlasTextureChannels.RGB, Color.black)
                },
                SurfaceModes = { AtlasSurfaceMode.Cutout, AtlasSurfaceMode.Transparent, AtlasSurfaceMode.Fade },
                VRChatFallback = true,
                BlendedDepthWrite = true,
                VertexColors = AtlasVertexColorSupport.Color | AtlasVertexColorSupport.Alpha | AtlasVertexColorSupport.Amount | AtlasVertexColorSupport.Srgb,
                // UV Tile Discard: on both sides (_UDIMDiscard*) and one side (_UDIMFaceDiscard*),
                // each reading UV0 to UV3.
                TileDiscard = true,
                FaceTileDiscard = true,
                // World AO Blocker on UV0 to UV3.
                AOBlocker = true,
                UVChannels = 3,
                // _SDFShadingTextureUV and _LightDataSDFMapUV pick the channel.
                SdfFace = true,
                // Matcap 0 to 3, every blend by weight; masks pick a channel and invert.
                MatcapSlots = 4,
                MatcapBlends = AtlasMatcapBlends.All,
                MatcapMaskInvert = true,
                MatcapColor = true,
                MatcapBaseColorMix = true,
                MatcapEmission = true,
                MatcapWeights = true,
                MatcapTextureTransform = true,
                MatcapLookup = true,
                // Rim Lighting 0 and 1, each with a mask (by channel, inverted); the environmental rim
                // with a mask (by channel); Backlight 0 with its color texture.
                RimSlots = 2,
                RimMask = true,
                EnvironmentalRim = true,
                Backlights = 1,
                // Global masks: Masks 1 and 2 as Global Mask Textures 1 and 2; the back face, mirror,
                // camera, distance and blacklight modifiers (Poiyomi 11's contacts are added below).
                // Detail texture (× 2 in gamma, tinted, with an intensity) and detail normal, on any
                // UV channel; the detail mask's R and G.
                Glitter = true,
                ProximityColor = true,
                HueCycling = true,
                EmissionSlots = 4,
                AudioLinkPerSection = true,
                DetailUVChannels = 3,
                DetailTextureBlends = { AtlasBlend.MultiplyDouble },
                DetailStrength = true,
                DetailNormal = true,
                GlobalMasks = true,
                LiveMaskSteps =
                {
                    AtlasMaskBusStepKind.Faces, AtlasMaskBusStepKind.Mirror, AtlasMaskBusStepKind.Camera,
                    AtlasMaskBusStepKind.Distance, AtlasMaskBusStepKind.Blacklight
                }
            };
            if (major >= 11)
            {
                // Poiyomi 11: Global Mask Contacts, a touch written into a channel.
                capabilities.LiveMaskSteps.Add(AtlasMaskBusStepKind.Contact);
                // And a second backlight: _BacklightColorTex__1.
                capabilities.Backlights = 2;
                capabilities.Textures.Add(new AtlasOutputTexture("Backlight2", "Backlight 2 color") { SizeMode = AtlasTextureSizeMode.MainAspect, MainDivisor = 2 }
                    .With(AtlasVirtualMap.BacklightColor2, AtlasTextureChannels.RGB, Color.black));
            }
            if (pro)
            {
                // Pro's own sections: GT7 tone mapping, screen-space ambient occlusion and contact
                // shadows; Pro 11 also has the neutral surface emission.
                capabilities.ToneMapping = true;
                capabilities.NeutralEmission = major >= 11;
                capabilities.ScreenOcclusion = true;
                capabilities.ContactShadows = true;
            }
            return capabilities;
        }

        // Poiyomi's rendering presets (_Mode), the same in 10 and 11, as its shader's on_value_actions set them, by
        // property: _Mode, queue, RenderType, _AlphaForceOpaque, _Cutoff, _SrcBlend, _DstBlend,
        // _AddSrcBlend, _ZWrite, _AlphaPremultiply, _OutlineSrcBlend, _OutlineDstBlend,
        // _OutlineDstBlendAlpha. The properties every preset sets alike follow in Write.
        private static readonly (AtlasSurfaceMode Mode, int Preset, int Queue, string Type, int ForceOpaque, float Cutoff,
            int Src, int Dst, int AddSrc, int ZWrite, int Premultiply, int OutlineSrc, int OutlineDst, int OutlineDstAlpha)[] Presets =
        {
            (AtlasSurfaceMode.Opaque, 0, 2000, "Opaque", 1, 0f, 1, 0, 1, 1, 0, 1, 0, 0),
            (AtlasSurfaceMode.Cutout, 1, 2450, "TransparentCutout", 0, 0.5f, 1, 0, 1, 1, 0, 1, 0, 1),
            (AtlasSurfaceMode.Fade, 2, 3000, "Transparent", 0, 0.002f, 5, 10, 5, 0, 0, 5, 10, 1),
            // TransClipping: a fade that writes depth.
            (AtlasSurfaceMode.Fade, 9, 2460, "TransparentCutout", 0, 0.01f, 5, 10, 5, 1, 0, 5, 10, 1),
            (AtlasSurfaceMode.Transparent, 3, 3000, "Transparent", 0, 0f, 1, 10, 1, 0, 1, 1, 10, 1)
        };

        // The main normal map: linear, a size keeping the main texture's aspect.
        public static AtlasOutputTexture NormalTexture() =>
            new AtlasOutputTexture("Normal", "Normal map") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false, Normal = true }
                .With(AtlasVirtualMap.Normal, AtlasTextureChannels.RGB, new Color(0f, 0f, 1f, 1f));

        // Poiyomi 10's main section: _Color (sRGB-authored, so set from the linear color's
        // gamma value) times _MainTex, whose alpha holds the baked alpha; the normal map at the
        // output's strength; then the rendering preset of the surface mode, and VRChat's fallback.
        public void Write(AtlasUnifiedMaterial output, Material material)
        {
            var baseColor = output.Surface.BaseColor;
            material.SetColor("_Color", baseColor.Color.gamma);
            material.SetTexture("_MainTex", baseColor.Map?.Texture);
            material.SetTextureScale("_MainTex", Vector2.one);
            material.SetTextureOffset("_MainTex", Vector2.zero);
            var normal = output.Surface.Normal;
            material.SetTexture("_BumpMap", normal.Map?.Texture);
            material.SetFloat("_BumpScale", normal.Map?.Texture != null ? normal.Strength : 1f);
            material.SetTextureScale("_BumpMap", Vector2.one);
            material.SetTextureOffset("_BumpMap", Vector2.zero);
            material.SetFloat("_BumpMapUV", 0);
            // Vertex colors: base × lerp(1, vertex RGB (sRGB decoded with Linear Colors), amount),
            // alpha × lerp(1, vertex alpha, alpha amount).
            var vertex = output.Surface.VertexColor;
            bool vertexColors = vertex.MultiplyColor || vertex.MultiplyAlpha;
            material.SetFloat("_MainVertexColoringEnabled", vertexColors ? 1 : 0);
            material.SetFloat("_MainVertexColoring", vertex.MultiplyColor ? Mathf.Clamp01(vertex.Amount) : 0f);
            material.SetFloat("_MainUseVertexColorAlpha", vertex.MultiplyAlpha ? Mathf.Clamp01(vertex.AlphaAmount) : 0f);
            material.SetFloat("_MainVertexColoringLinearSpace", vertex.SrgbData ? 1 : 0);
            WriteTileDiscard(output.Render.HiddenTiles, material);
            WriteAOBlocker(output.Render.AOBlocker, material);
            WriteLighting(output.Lighting, material);
            WriteMatcaps(output, material);
            WriteRims(output, material);
            WriteEnvironmentalRim(output, material);
            WriteBacklight(output, material);
            WriteSurfaceResponse(output.Surface.Response, material);
            WriteClearCoat(output.Surface.Response.ClearCoat, material);
            WriteGlobalMasks(output, material);
            WriteDetails(output, material);
            WriteGlitter(output, material);
            WriteSubsurface(output, material);
            WriteProximityColor(output, material);
            if (pro)
            {
                WriteToneMapping(output, material);
                WriteScreenOcclusion(output, material);
                WriteContactShadows(output, material);
            }
            WriteHueCycling(output, material);
            WriteCyclingEmission(output, material);
            WriteAudioLinkGlobals(output, material);
            WriteEmission(output, material);
            WriteLiveEmission(output, material);
            var render = output.Render;
            var preset = Array.Find(Presets, p => p.Mode == render.SurfaceMode &&
                (p.Mode != AtlasSurfaceMode.Fade || (p.ZWrite == 1) == render.Pass.DepthWrite));
            if (preset.Type == null) preset = Presets[0];
            material.SetFloat("_Mode", preset.Preset);
            material.SetOverrideTag("RenderType", preset.Type);
            material.SetFloat("_AlphaForceOpaque", preset.ForceOpaque);
            material.SetFloat("_Cutoff", preset.Mode == AtlasSurfaceMode.Cutout ? render.Cutoff : preset.Cutoff);
            material.SetFloat("_BlendOp", 0);
            material.SetFloat("_BlendOpAlpha", 4);
            material.SetFloat("_SrcBlend", preset.Src);
            material.SetFloat("_DstBlend", preset.Dst);
            material.SetFloat("_SrcBlendAlpha", 1);
            material.SetFloat("_DstBlendAlpha", 1);
            material.SetFloat("_AddSrcBlend", preset.AddSrc);
            material.SetFloat("_AddDstBlend", 1);
            material.SetFloat("_AddSrcBlendAlpha", 0);
            material.SetFloat("_AddDstBlendAlpha", 1);
            material.SetFloat("_AlphaToCoverage", preset.Mode == AtlasSurfaceMode.Cutout && render.AlphaToCoverage ? 1 : 0);
            // Poiyomi 11's presets also turn the sharpened coverage off; it follows the alpha's.
            material.SetFloat("_AlphaSharpenedA2C", output.Surface.Alpha.SharpenedCoverage ? 1 : 0);
            material.SetFloat("_ZWrite", preset.ZWrite);
            material.SetFloat("_ZTest", 4);
            material.SetFloat("_AlphaPremultiply", preset.Premultiply);
            material.SetFloat("_OutlineSrcBlend", preset.OutlineSrc);
            material.SetFloat("_OutlineDstBlend", preset.OutlineDst);
            material.SetFloat("_OutlineSrcBlendAlpha", 1);
            material.SetFloat("_OutlineDstBlendAlpha", preset.OutlineDstAlpha);
            material.SetFloat("_OutlineBlendOp", 0);
            material.SetFloat("_OutlineBlendOpAlpha", 4);
            material.renderQueue = render.Queue >= 0 ? render.Queue : preset.Queue;
            material.SetOverrideTag("VRCFallback", Fallback(render));
        }

        // The emission map, sRGB, a size keeping the main texture's aspect.
        // The mask allocator's places: four one-value masks in a linear texture.
        public static AtlasOutputTexture MaskTexture(int number, params AtlasVirtualMap[] maps) =>
            new AtlasOutputTexture("Masks" + number, "Masks " + number) { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false, MainDivisor = 2 }
                .With(maps[0], AtlasTextureChannels.R, AtlasOutputTexture.Gray(0f))
                .With(maps[1], AtlasTextureChannels.G, AtlasOutputTexture.Gray(0f))
                .With(maps[2], AtlasTextureChannels.B, AtlasOutputTexture.Gray(0f))
                .With(maps[3], AtlasTextureChannels.A, AtlasOutputTexture.Gray(0f));

        public static AtlasOutputTexture EmissionTexture() =>
            new AtlasOutputTexture("Emission", "Emission") { SizeMode = AtlasTextureSizeMode.MainAspect, MainDivisor = 2 }
                .With(AtlasVirtualMap.Emission, AtlasTextureChannels.RGB, new Color(0f, 0f, 0f, 1f));

        // Emission 0 (_EnableEmission with its _EMISSION keyword): _EmissionColor (HDR, set from
        // the linear color's gamma value) × _EmissionMap × _EmissionStrength (0–20; above that the
        // rest goes into the color).
        private static void WriteEmission(AtlasUnifiedMaterial output, Material material)
        {
            var emission = AtlasEmissionFeature.Of(output);
            var map = emission?.Source.Map?.Texture;
            material.SetFloat("_EnableEmission", map != null ? 1 : 0);
            if (map == null)
            {
                material.DisableKeyword("_EMISSION");
                return;
            }
            material.EnableKeyword("_EMISSION");
            float total = emission.Targets.First(t => t.Target == AtlasSurfaceTarget.Emission).Opacity;
            float strength = Mathf.Min(total, 20f);
            float rest = total / Mathf.Max(strength, 1e-4f);
            material.SetTexture("_EmissionMap", map);
            material.SetTextureScale("_EmissionMap", Vector2.one);
            material.SetTextureOffset("_EmissionMap", Vector2.zero);
            material.SetFloat("_EmissionMapUV", 0);
            material.SetColor("_EmissionColor", new Color(rest, rest, rest, 1f).gamma);
            material.SetFloat("_EmissionStrength", strength);
        }

        // Reflections & Specular (MOCHIE_PBR) from the packed map: metallic = R, smoothness = G,
        // the reflection and specular masks B and A × their strengths; and the AO maps
        // (PROP_LIGHTINGAOMAPS): occlusion = lerp(1, R, strength). Thry sets these keywords from
        // its inspector; a written material sets them itself.
        // Lighting: Light Data and the lighting type, the Poiyomi parser's reading written back
        // (AtlasPoiyomiLighting). Parts drawn on the texture are cleared by the Lighting feature.
        private static readonly string[] LightingKeywords =
        {
            "_LIGHTINGMODE_TEXTURERAMP", "_LIGHTINGMODE_MULTILAYER_MATH", "_LIGHTINGMODE_WRAPPED", "_LIGHTINGMODE_SKIN",
            "_LIGHTINGMODE_SHADEMAP", "_LIGHTINGMODE_FLAT", "_LIGHTINGMODE_REALISTIC", "_LIGHTINGMODE_CLOTH", "_LIGHTINGMODE_SDF"
        };

        private static void WriteLighting(AtlasLighting l, Material m)
        {
            m.SetFloat("_LightingColorMode", l.ColorModel == AtlasLightColorModel.Separate ? 1 : l.ColorModel == AtlasLightColorModel.Floored ? 2
                : l.ColorModel == AtlasLightColorModel.SplitProbes ? 3 : 0);
            m.SetFloat("_Unlit_Intensity", l.FloorIntensity);
            m.SetFloat("_LightingIndirectUsesNormals", l.Ambient == AtlasAmbientModel.Directional ? l.DirectionalAmount : 0f);
            int direction = 0;
            switch (l.Direction)
            {
                case AtlasLightDirectionModel.Fixed:
                    direction = l.DirectionLocal ? 1 : 2;
                    m.SetVector("_LightngForcedDirection", l.LightDirection);
                    break;
                case AtlasLightDirectionModel.ViewFallback: direction = 3; break;
                case AtlasLightDirectionModel.SplitProbes: direction = 4; break;
                case AtlasLightDirectionModel.View:
                    direction = 5;
                    m.SetFloat("_LightingViewDirOffsetYaw", l.ViewYaw);
                    m.SetFloat("_LightingViewDirOffsetPitch", l.ViewPitch);
                    break;
            }
            m.SetFloat("_LightingDirectionMode", direction);
            var ramp = l.Ramp;
            bool multilayer = l.Family == AtlasLightingFamily.RampToon && ramp.Term != AtlasShadingTerm.Wrapped && ramp.Bands.Count > 0 && !IsShadeMap(ramp);
            // Multilayer Math builds its own half-Lambert terms, and the light map (Light Data's
            // mode) is what specular and light masks read; Shade Map keeps the light map's.
            bool blendedSdf = ramp.Term == AtlasShadingTerm.SdfFace && ramp.Sdf?.Style == AtlasSdfStyle.Blended;
            if (blendedSdf)
            {
                // Light map mode SDF: a blended SDF texture's red or green by the light's side.
                TextureOn(m, "_LightDataSDFMap", ramp.Sdf.Map);
                m.SetFloat("_LightDataSDFBlendY", ramp.Sdf.VerticalBlend);
                m.SetFloat("_LightDataSDFMapLOD", ramp.Sdf.Softness);
                m.SetFloat("_LightDataSDFChannelsBA", ramp.Sdf.MaskChannels ? 1 : 0);
            }
            m.SetFloat("_LightingMapMode", blendedSdf ? 4 : ramp.Term == AtlasShadingTerm.HalfLambert ? 1 : ramp.Term == AtlasShadingTerm.Lambert ? 2
                : ramp.Term == AtlasShadingTerm.ShadowsOnly ? 3 : 0);
            // Shadow masks scale the shadow strength; detail shadows multiply the light map.
            PackedMask(m, "_LightingShadowMasks", "_LightingShadowMaskStrength", ramp.RampStrengthMask);
            PackedMask(m, "_LightingDetailShadowMaps", "_LightingDetailShadowStrength", ramp.TermMask);
            for (int c = 0; c < 4; c++)
                m.SetFloat("_LightingAddDetailShadowStrength" + "RGBA"[c], m.GetFloat("_LightingDetailShadowStrength" + "RGBA"[c]));
            m.SetFloat("_LightingCastedShadows", l.ReceiveShadows);
            m.SetFloat("_LightingShadowSoftness", l.ShadowSoftness);
            m.SetFloat("_LightingCleanCastedShadows", l.CleanShadows ? 1 : 0);
            m.SetFloat("_LightingMonochromatic", l.Monochrome);
            m.SetFloat("_LightingCapEnabled", l.LimitBrightness ? 1 : 0);
            m.SetFloat("_LightingCap", l.BrightnessCeiling);
            m.SetFloat("_LightingCapLuminance", l.LimitByLuminance);
            m.SetFloat("_LightingCapMode", l.LimitKeepsHue ? 1 : 0);
            m.SetFloat("_BackfaceForceShadow", ramp.BackFaceShadow);
            // Its one floor: the minimum light, or a brightness added like emission (Toon Standard's).
            m.SetFloat("_LightingMinLightBrightness", Mathf.Max(l.MinimumLight, l.MinimumBrightness));
            m.SetFloat("_LightingForceColorEnabled", l.FixedLightColor ? 1 : 0);
            m.SetColor("_LightingForcedColor", l.LightColor.gamma);
            m.SetFloat("_LightingForcedColorThemeIndex", 0);
            m.SetFloat("_LightingVertexLightingEnabled", l.Contributions.VertexLights > 0f ? 1 : 0);
            m.SetFloat("_LightingAdditiveEnable", l.Contributions.AdditionalLights > 0f ? 1 : 0);
            var additional = l.AdditionalLights;
            m.SetFloat("_LightingAdditiveType", additional.Shading == AtlasAdditionalLightShading.Lambert ? 0
                : additional.Shading == AtlasAdditionalLightShading.Toon ? 1 : 3);
            m.SetFloat("_LightingAdditiveGradientStart", additional.GradientStart);
            m.SetFloat("_LightingAdditiveGradientEnd", additional.GradientEnd);
            m.SetFloat("_LightingAdditiveLimited", additional.LimitBrightness ? 1 : 0);
            m.SetFloat("_LightingAdditiveLimit", additional.BrightnessCeiling);
            m.SetFloat("_LightingAdditiveLimitMode", additional.LimitKeepsHue ? 1 : 0);
            m.SetFloat("_LightingAdditiveCastedShadows", additional.ReceiveShadows);
            m.SetFloat("_LightingAdditiveMonochromatic", additional.Monochrome);
            m.SetFloat("_LightingAdditivePassthrough", additional.Passthrough);
            m.SetVector("_LightingAdditiveAttenCurve", additional.Falloff);
            m.SetFloat("_DisableDirectionalInAdd", additional.Directional ? 0 : 1);
            m.SetFloat("_LightingMirrorVertexLightingEnabled", additional.VertexLightsInMirrors ? 1 : 0);
            // The add pass: Max (4) with the alpha boost, or add.
            m.SetFloat("_AddBlendOp", additional.Combine == AtlasLightCombine.Max ? 4 : 0);
            m.SetFloat("_AlphaBoostFA", additional.MaxAlphaBoost);
            m.SetFloat("_PPLightingMultiplier", l.LightMultiplier);
            m.SetFloat("_PPLightingAddition", l.LightAddition);
            var volumes = l.LightVolumes;
            m.SetFloat("_LightingEnableLightVolumes", volumes.Enabled ? 1 : 0);
            m.SetFloat("_LightingLightVolumesSpecular", volumes.Specular ? volumes.SpecularStrength : 0f);
            m.SetFloat("_LightingLightVolumesNormalBias", volumes.Bias);
            m.SetFloat("_LightingLightVolumesSpecularMaxLights", volumes.SpecularMaxLights);
            // LTCGI (POI_LTCGI), which Poiyomi discourages on avatars now that worlds light them
            // through light volumes.
            var ltcgi = l.Ltcgi;
            m.SetFloat("_LTCGIEnabled", ltcgi.Enabled ? 1 : 0);
            if (ltcgi.Enabled) m.EnableKeyword("POI_LTCGI");
            else m.DisableKeyword("POI_LTCGI");
            m.SetFloat("_LTCGI_AnimToggle", 1);
            m.SetColor("_LTCGI_DiffuseColor", ltcgi.DiffuseTint.gamma);
            m.SetFloat("_LTCGI_DiffuseColorThemeIndex", 0);
            m.SetColor("_LTCGI_SpecularColor", ltcgi.SpecularTint.gamma);
            m.SetFloat("_LTCGI_SpecularColorThemeIndex", 0);
            m.SetFloat("_LTCGI_DiffuseMultiplier", ltcgi.DiffuseStrength);
            m.SetFloat("_LTCGI_SpecularMultiplier", ltcgi.SpecularStrength);
            m.SetFloat("_LTCGI_UsePBR", ltcgi.UseSurfaceResponse ? 1 : 0);
            m.SetFloat("_LTCGI_Metallic", ltcgi.Metallic);
            m.SetFloat("_LTCGI_Smoothness", 1f - ltcgi.Roughness);
            m.SetFloat("_LTCGI_UseEO", ltcgi.ExposureOcclusion ? 1 : 0);

            bool shading = l.Family != AtlasLightingFamily.Unlit;
            m.SetFloat("_ShadingEnabled", shading ? 1 : 0);
            if (shading) m.EnableKeyword("VIGNETTE_MASKED");
            else m.DisableKeyword("VIGNETTE_MASKED");
            int type = LightingType(l, m);
            m.SetFloat("_LightingMode", type);
            for (int k = 0; k < LightingKeywords.Length; k++)
                if (k == type) m.EnableKeyword(LightingKeywords[k]);
                else m.DisableKeyword(LightingKeywords[k]);
        }

        // Matcaps: the output's lookup layers by slot (consecutive layers sharing a mask object),
        // each written as the Poiyomi parser reads a matcap (AtlasPoiyomiLookups).
        private static readonly string[] MatcapKeywords = { "POI_MATCAP0", "COLOR_GRADING_HDR_3D", "POI_MATCAP2", "POI_MATCAP3" };
        private static readonly AtlasLookupProjection[] MatcapProjections =
        {
            AtlasLookupProjection.ViewCorrected, AtlasLookupProjection.ViewStabilized, AtlasLookupProjection.ReflectionPlanar,
            AtlasLookupProjection.Gradient, AtlasLookupProjection.LightHalfVector
        };
        private static readonly (string Property, AtlasBlend Blend, AtlasPlacement Placement)[] MatcapWeights =
        {
            ("Replace", AtlasBlend.Replace, AtlasPlacement.Surface), ("Multiply", AtlasBlend.Multiply, AtlasPlacement.Surface),
            ("Add", AtlasBlend.Add, AtlasPlacement.Surface), ("Screen", AtlasBlend.Screen, AtlasPlacement.Surface),
            ("AddToLight", AtlasBlend.Add, AtlasPlacement.Overlay), ("Mixed", AtlasBlend.Mixed, AtlasPlacement.Surface)
        };

        private static void WriteMatcaps(AtlasUnifiedMaterial output, Material m)
        {
            var slots = new List<List<AtlasLookupLayer>>();
            foreach (var layer in output.Layers.OfType<AtlasLookupLayer>())
            {
                if (slots.Count > 0 && ReferenceEquals(slots[slots.Count - 1][0].Mask, layer.Mask)) slots[slots.Count - 1].Add(layer);
                else slots.Add(new List<AtlasLookupLayer> { layer });
            }
            for (int i = 0; i < 4; i++)
            {
                string p = i == 0 ? "_Matcap" : "_Matcap" + (i + 1);
                bool on = i < slots.Count;
                m.SetFloat(p + "Enable", on ? 1 : 0);
                if (on) m.EnableKeyword(MatcapKeywords[i]);
                else m.DisableKeyword(MatcapKeywords[i]);
                if (on) WriteMatcap(m, i, p, slots[i]);
                WriteLayerHue(m, p + "HueShift", p + "HueSelectOrShift", output, on ? slots[i][0] : null);
                WriteMatcapAudio(m, output, i, p, on ? slots[i] : null);
            }
        }

        private static void WriteMatcap(Material m, int index, string p, List<AtlasLookupLayer> layers)
        {
            string ordinal = new[] { "", "2nd", "3rd", "4th" }[index];
            string circle = index == 0 ? "_Matcap0" : p;
            var first = layers[0];
            var map = first.Source.Map;
            m.SetTexture(p, map?.Texture);
            // The texture's scale and offset apply after the lookup (TRANSFORM_TEX); the pan
            // moves the lookup coordinate before the scale.
            var scale = map?.Tiling ?? Vector2.one;
            var scroll = map?.Scroll ?? Vector2.zero;
            m.SetTextureScale(p, scale);
            m.SetTextureOffset(p, map?.Offset ?? Vector2.zero);
            m.SetVector(p + "Pan", new Vector2(Mathf.Abs(scale.x) > 1e-6f ? scroll.x / scale.x : 0f,
                Mathf.Abs(scale.y) > 1e-6f ? scroll.y / scale.y : 0f) / 0.05f);
            // The lookup's tiling is 2 × Border.
            m.SetFloat(p + "Border", first.Tiling.x * 0.5f);
            var color = first.Source.Color;
            float intensity = Mathf.Max(1f, color.maxColorComponent);
            var shown = new Color(color.r / intensity, color.g / intensity, color.b / intensity, color.a);
            m.SetColor(p + "Color", shown.gamma);
            m.SetFloat(p + "ColorThemeIndex", 0);
            m.SetFloat(p + "Intensity", intensity);
            m.SetFloat(p + "UVMode", Mathf.Max(0, Array.IndexOf(MatcapProjections, first.Projection)));
            var mask = first.Mask;
            m.SetTexture(p + "Mask", mask?.Map?.Texture);
            m.SetTextureScale(p + "Mask", Vector2.one);
            m.SetTextureOffset(p + "Mask", Vector2.zero);
            m.SetFloat(p + "MaskUV", 0);
            m.SetFloat(p + "MaskChannel", mask?.Map?.Texture != null ? Mathf.Clamp((int)mask.Channel, 0, 3) : 0);
            m.SetFloat(p + "MaskInvert", mask?.Map?.Texture != null && mask.Invert ? 1 : 0);
            Consumer(m, p + "MaskGlobalMask", mask?.Bus ?? -1, mask?.BusBlend ?? AtlasMaskBlend.Add);
            if (mask == null || mask.Bus < 0) m.SetFloat(p + "MaskGlobalMask", 0);
            foreach (var weight in MatcapWeights)
            {
                var layer = layers.Find(l => l.Blend == weight.Blend && l.Placement == weight.Placement);
                m.SetFloat(p + weight.Property, layer != null ? layer.Opacity : 0f);
            }
            m.SetFloat(p + "EmissionStrength", first.EmissionAmount);
            m.SetFloat(p + "Rotation", first.LookupRotation / 180f);
            m.SetVector(index == 0 ? "_MatCapBlendUV1" : "_MatCap" + ordinal + "BlendUV1", first.UVBlend);
            m.SetFloat(p + "UVToBlend", Mathf.Clamp((int)(first.UVBlendSpace - AtlasTextureSpace.UV0), 0, 3));
            m.SetFloat(index == 0 ? "_MatCapVRParallaxStrength" : "_MatCap" + ordinal + "VRParallaxStrength", first.CenterEye ? 0 : 1);
            m.SetFloat(p + "Normal", first.SurfaceNormalAmount);
            m.SetFloat(p + "BaseColorMix", first.BaseColorMix);
            m.SetFloat(p + "LightColorMix", first.LightColorMix);
            m.SetFloat(p + "HideWithReflProbe", first.HideWithReflectionProbes ? 1 : 0);
            m.SetFloat(p + "ShowInOutline", first.OnOutlines ? 1 : 0);
            m.SetFloat(p + "LightMask", first.HideInShadow);
            m.SetFloat(circle + "CircleMaskEnabled", first.CircleMask ? 1 : 0);
            m.SetFloat(circle + "CircleMaskBorder", first.CircleRadius);
            m.SetFloat(circle + "CircleMaskBlur", first.CircleBlur);
            m.SetFloat(p + "Smoothness", 1f - first.Blur);
            m.SetFloat("_Matcap" + index + "CustomNormal", first.NormalMap != null ? 1 : 0);
            m.SetTexture("_Matcap" + index + "NormalMap", first.NormalMap?.Texture);
            m.SetFloat("_Matcap" + index + "NormalMapScale", first.NormalStrength);
        }

        // Rims: the output's view-angle rims by slot (a rim and the unlit-side rim after it share a
        // mask object), each in the style that holds it as the Poiyomi parser reads it
        // (AtlasPoiyomiRims); a rim no style holds is fitted to the Poiyomi style.
        private static bool IsRim(AtlasAngleLayer layer) =>
            layer.Source == AtlasAngleSource.ViewAngle && layer.ColorSource == AtlasAngleColorSource.Color;

        private static void WriteRims(AtlasUnifiedMaterial output, Material m)
        {
            var slots = new List<List<AtlasAngleLayer>>();
            foreach (var layer in output.Layers.OfType<AtlasAngleLayer>().Where(IsRim))
            {
                if (slots.Count > 0 && slots[slots.Count - 1].Count == 1 && ReferenceEquals(slots[slots.Count - 1][0].Mask, layer.Mask) &&
                    layer.LightSide.Invert && layer.LightSide.Mode != AtlasLightSideMode.None)
                    slots[slots.Count - 1].Add(layer);
                else slots.Add(new List<AtlasAngleLayer> { layer });
            }
            for (int i = 0; i < 2; i++)
            {
                int index = i;
                string N(string rim0, string rim1) => index == 0 ? rim0 : rim1;
                bool on = i < slots.Count;
                m.SetFloat(N("_EnableRimLighting", "_EnableRim2Lighting"), on ? 1 : 0);
                string keyword = N("_GLOSSYREFLECTIONS_OFF", "POI_RIM2");
                if (on) m.EnableKeyword(keyword);
                else m.DisableKeyword(keyword);
                if (on) WriteRim(m, slots[i], N);
                WriteLayerHue(m, N("_RimHueShift", "_Rim2HueShift"), N("_RimHueSelectOrShift", "_Rim2HueSelectOrShift"), output, on ? slots[i][0] : null);
                if (on) WriteRimAudio(m, output, slots[i][0], N);
            }
        }

        private delegate string RimName(string rim0, string rim1);

        private static void WriteRim(Material m, List<AtlasAngleLayer> slot, RimName n)
        {
            var rim = slot[0];
            var dark = slot.Count > 1 ? slot[1] : null;
            var maskTexture = rim.Mask?.Map?.Texture;
            bool step = rim.Profile == AtlasAngleProfile.Step && !rim.Smooth;
            bool edgeRising = step && rim.StepBase == AtlasAngleBase.Edge && !rim.Falling && !rim.LinearAngle;
            int style;
            if (step && rim.Falling && rim.Placement == AtlasPlacement.Surface && dark == null &&
                (rim.LightSide.Mode == AtlasLightSideMode.None || (rim.LightSide.Mode == AtlasLightSideMode.Mask && rim.LightSide.Smooth && !rim.LightSide.BeforeStep)))
                style = 0;
            else if (edgeRising && rim.Placement == AtlasPlacement.Surface && rim.Blend == AtlasBlend.Add && (rim.High <= rim.Low || rim.High >= 1f) &&
                     (rim.Mask == null || !rim.Mask.Invert) &&
                     (rim.LightSide.Mode == AtlasLightSideMode.None || rim.LightSide.Mode == AtlasLightSideMode.Subtract) &&
                     (dark == null || dark.LightSide.Mode == AtlasLightSideMode.Subtract))
                style = 1;
            else if (edgeRising && rim.Blend != AtlasBlend.Mixed &&
                     (maskTexture == null || (rim.Mask.Channel == AtlasChannel.Red && !rim.Mask.Invert)) &&
                     (rim.LightSide.Mode == AtlasLightSideMode.None || (rim.LightSide.Mode == AtlasLightSideMode.Mask && rim.LightSide.BeforeStep)) &&
                     (dark == null || dark.LightSide.Mode == AtlasLightSideMode.Scale))
                style = 2;
            // Another rim (a band, a smooth step, a lit rim without a lilToon place): fitted.
            else style = 3;

            string[] keywords = { n("_RIMSTYLE_POIYOMI", "_RIM2STYLE_POIYOMI"), n("_RIMSTYLE_UTS2", "_RIM2STYLE_UTS2"), n("_RIMSTYLE_LILTOON", "_RIM2STYLE_LILTOON") };
            int keywordIndex = style == 3 ? 0 : style;
            for (int k = 0; k < 3; k++)
                if (k == keywordIndex) m.EnableKeyword(keywords[k]);
                else m.DisableKeyword(keywords[k]);
            m.SetFloat(n("_RimStyle", "_Rim2Style"), style == 3 ? 0 : style);
            m.SetFloat(n("_RimHueShiftEnabled", "_Rim2HueShiftEnabled"), 0);
            m.SetFloat(n("_RimApplyAlpha", "_Rim2ApplyAlpha"), 0);
            Consumer(m, n("_RimGlobalMask", "_Rim2GlobalMask"), slot[0].Mask?.Bus ?? -1, slot[0].Mask?.BusBlend ?? AtlasMaskBlend.Add);
            m.SetFloat(n("_RimApplyGlobalMaskIndex", "_Rim2ApplyGlobalMaskIndex"), 0);
            // Poiyomi 11's application stage, which its lilToon style sets (WriteLilRim).
            m.SetFloat(n("_RimApplicationStage", "_Rim2ApplicationStage"), 0);
            switch (style)
            {
                case 1: WriteUts2Rim(m, rim, dark, n); break;
                case 2: WriteLilRim(m, rim, dark, n); break;
                default: WritePoiyomiRim(m, rim, style == 3, n); break;
            }
        }

        private static void WritePoiyomiRim(Material m, AtlasAngleLayer rim, bool fit, RimName n)
        {
            float low = rim.Low, high = rim.High, power = rim.Power, peak = 1f;
            bool edge = rim.StepBase == AtlasAngleBase.Edge, linear = rim.LinearAngle;
            if (fit)
            {
                // A band in 1 − facing: a falling step in facing around 1 − its midpoint.
                AtlasRimCurve.FitBand(rim, out float range, out float softness, out peak);
                low = Mathf.Clamp01(range - softness);
                high = Mathf.Clamp01(range + softness);
                power = 1f;
                edge = linear = false;
            }
            m.SetFloat(n("_RimWidth", "_Rim2Width"), Mathf.Clamp01((high + 0.05f) / 1.05f));
            m.SetFloat(n("_RimBlur", "_Rim2Blur"), Mathf.Clamp01(high - low));
            m.SetFloat(n("_RimLightingInvert", "_Rim2LightingInvert"), edge ? 1 : 0);
            m.SetFloat(n("_RimLinear", "_Rim2Linear"), linear ? 1 : 0);
            m.SetFloat(n("_RimPower", "_Rim2Power"), power);
            m.SetFloat(n("_Is_NormalMapToRimLight", "_Is_NormalMapToRim2Light"), rim.SurfaceNormalAmount);
            m.SetFloat(n("_RimBaseColorMix", "_Rim2BaseColorMix"), rim.BaseColorMix);
            m.SetFloat(n("_RimBlendStrength", "_Rim2BlendStrength"), rim.Opacity * peak);
            m.SetFloat(n("_RimStrength", "_Rim2Strength"), rim.EmissionAmount);
            int blend = rim.Blend == AtlasBlend.Replace ? 1 : rim.Blend == AtlasBlend.Multiply ? 2 : rim.Blend == AtlasBlend.Mixed ? 3
                : rim.Blend == AtlasBlend.Screen ? 4 : 0;
            m.SetFloat(n("_RimPoiBlendMode", "_RimPoi2BlendMode"), blend);
            var color = rim.Color.Color;
            float brightness = Mathf.Max(1f, color.maxColorComponent);
            m.SetColor(n("_RimLightColor", "_Rim2LightColor"), new Color(color.r / brightness, color.g / brightness, color.b / brightness, color.a).gamma);
            m.SetFloat(n("_RimLightColorThemeIndex", "_Rim2LightColorThemeIndex"), 0);
            m.SetFloat(n("_RimBrightness", "_Rim2Brightness"), brightness);
            m.SetTexture(n("_RimTex", "_Rim2Tex"), null);
            WriteMaskSlot(m, n("_RimMask", "_Rim2Mask"), rim.Mask);
            m.SetFloat(n("_RimMaskChannel", "_Rim2MaskChannel"), MaskChannel(rim.Mask));
            m.SetFloat(n("_RimMaskInvert", "_Rim2MaskInvert"), rim.Mask?.Map?.Texture != null && rim.Mask.Invert ? 1 : 0);
            m.SetFloat(n("_RimBiasIntensity", "_Rim2BiasIntensity"), 0);
            var side = rim.LightSide;
            bool shadow = rim.HideInShadow > 0f || side.Mode == AtlasLightSideMode.Mask || side.ShrinkInShadow > 0f;
            m.SetFloat(n("_RimShadowToggle", "_Rim2ShadowToggle"), shadow ? 1 : 0);
            if (side.Mode == AtlasLightSideMode.Mask)
            {
                m.SetFloat(n("_RimShadowMaskRampType", "_Rim2ShadowMaskRampType"), 1);
                m.SetVector(n("_RimShadowAlpha", "_Rim2ShadowAlpha"), new Vector4(side.Low, side.High, 0f, 1f));
                m.SetFloat(n("_RimShadowMaskInvert", "_Rim2ShadowMaskInvert"), side.Invert ? 1 : 0);
                m.SetFloat(n("_RimShadowMaskStrength", "_Rim2ShadowMaskStrength"), side.Strength);
            }
            else
            {
                m.SetFloat(n("_RimShadowMaskRampType", "_Rim2ShadowMaskRampType"), 0);
                m.SetFloat(n("_RimShadowMaskInvert", "_Rim2ShadowMaskInvert"), 0);
                m.SetFloat(n("_RimShadowMaskStrength", "_Rim2ShadowMaskStrength"), rim.HideInShadow);
            }
            m.SetFloat(n("_RimShadowWidth", "_Rim2ShadowWidth"), side.ShrinkInShadow);
        }

        private static void WriteUts2Rim(Material m, AtlasAngleLayer rim, AtlasAngleLayer dark, RimName n)
        {
            // Power = 2^(3 − 3 × p).
            float P(AtlasAngleLayer l) => (3f - Mathf.Log(Mathf.Max(1e-4f, l.Power), 2f)) / 3f;
            m.SetFloat(n("_RimLight_Power", "_Rim2Light_Power"), P(rim));
            m.SetFloat(n("_RimLight_InsideMask", "_Rim2Light_InsideMask"), rim.Low);
            m.SetFloat(n("_RimLight_FeatherOff", "_Rim2Light_FeatherOff"), rim.High <= rim.Low ? 1 : 0);
            m.SetFloat(n("_Is_NormalMapToRimLight", "_Is_NormalMapToRim2Light"), rim.SurfaceNormalAmount);
            m.SetFloat(n("_Is_LightColor_RimLight", "_Is_LightColor_Rim2Light"), rim.LightColorMix);
            var color = rim.Color.Color * rim.Opacity;
            m.SetColor(n("_RimLightColor", "_Rim2LightColor"), new Color(color.r, color.g, color.b, 1f).gamma);
            m.SetFloat(n("_RimLightColorThemeIndex", "_Rim2LightColorThemeIndex"), 0);
            WriteMaskSlot(m, n("_Set_RimLightMask", "_Set_Rim2LightMask"), rim.Mask);
            m.SetFloat(n("_Set_RimLightMaskChannel", "_Set_Rim2LightMaskChannel"), rim.Mask?.Map?.Texture != null ? MaskChannel(rim.Mask) : 1);
            m.SetFloat(n("_Tweak_RimLightMaskLevel", "_Tweak_Rim2LightMaskLevel"), 0);
            bool direction = rim.LightSide.Mode == AtlasLightSideMode.Subtract;
            m.SetFloat(n("_LightDirection_MaskOn", "_LightDirection_MaskOn2"), direction ? 1 : 0);
            m.SetFloat(n("_Tweak_LightDirection_MaskLevel", "_Tweak_LightDirection_MaskLevel2"),
                direction ? rim.LightSide.Level : dark != null ? dark.LightSide.Level : 0f);
            m.SetFloat(n("_Add_Antipodean_RimLight", "_Add_Antipodean_Rim2Light"), dark != null ? 1 : 0);
            if (dark == null) return;
            m.SetFloat(n("_Ap_RimLight_Power", "_Ap_Rim2Light_Power"), P(dark));
            m.SetFloat(n("_Ap_RimLight_FeatherOff", "_Ap_Rim2Light_FeatherOff"), dark.High <= dark.Low ? 1 : 0);
            m.SetFloat(n("_Is_LightColor_Ap_RimLight", "_Is_LightColor_Ap_Rim2Light"), dark.LightColorMix);
            var apColor = dark.Color.Color * dark.Opacity;
            m.SetColor(n("_Ap_RimLightColor", "_Ap_Rim2LightColor"), new Color(apColor.r, apColor.g, apColor.b, 1f).gamma);
            m.SetFloat(n("_RimApColorThemeIndex", "_Rim2ApColorThemeIndex"), 0);
        }

        private static void WriteLilRim(Material m, AtlasAngleLayer rim, AtlasAngleLayer dark, RimName n)
        {
            m.SetFloat(n("_RimBorder", "_Rim2Border"), (rim.Low + rim.High) * 0.5f);
            m.SetFloat(n("_RimBlur", "_Rim2Blur"), rim.High - rim.Low);
            m.SetFloat(n("_RimFresnelPower", "_Rim2FresnelPower"), rim.Power);
            int blend = rim.Blend == AtlasBlend.Replace ? 0 : rim.Blend == AtlasBlend.Screen ? 2 : rim.Blend == AtlasBlend.Multiply ? 3 : 1;
            m.SetFloat(n("_RimBlendMode", "_Rim2BlendMode"), blend);
            m.SetFloat(n("_RimEnableLighting", "_Rim2EnableLighting"), rim.LightResponse == AtlasLightResponse.IgnoreLight ? 0 : 1);
            // Poiyomi 11: on the base color, lit with the surface (1); after lighting, lit by
            // the direct light alone (2) or by all of it (0), as the parser reads the stages.
            // Poiyomi 10 has the last one only.
            m.SetFloat(n("_RimApplicationStage", "_Rim2ApplicationStage"),
                rim.Placement == AtlasPlacement.Surface ? 1 : rim.LightResponse == AtlasLightResponse.ScaleByDirectLight ? 2 : 0);
            m.SetFloat(n("_RimVRParallaxStrength", "_Rim2VRParallaxStrength"), rim.PerEye ? 1 : 0);
            m.SetFloat(n("_RimAAStrength", "_Rim2AAStrength"), rim.AntiAliasing);
            m.SetFloat(n("_RimApplyTransparency", "_Rim2ApplyTransparency"), rim.TimesAlpha ? 1 : 0);
            m.SetFloat(n("_RimNormalStrength", "_Rim2NormalStrength"), rim.SurfaceNormalAmount);
            m.SetFloat(n("_RimMainStrength", "_Rim2MainStrength"), rim.BaseColorMix);
            m.SetFloat(n("_RimShadowMask", "_Rim2ShadowMask"), rim.HideInShadow);
            m.SetFloat(n("_RimBackfaceMask", "_Rim2BackfaceMask"), rim.Faces == AtlasFaces.Front ? 1 : 0);
            var color = rim.Color.Color;
            m.SetColor(n("_RimColor", "_Rim2Color"), new Color(color.r, color.g, color.b, Mathf.Clamp01(color.a * rim.Opacity)).gamma);
            m.SetFloat(n("_RimLightColorThemeIndex", "_Rim2LightColorThemeIndex"), 0);
            // A mask goes in the color texture as a mask (its red).
            var mask = rim.Mask?.Map?.Texture;
            m.SetTexture(n("_RimColorTex", "_Rim2ColorTex"), mask);
            m.SetTextureScale(n("_RimColorTex", "_Rim2ColorTex"), Vector2.one);
            m.SetTextureOffset(n("_RimColorTex", "_Rim2ColorTex"), Vector2.zero);
            m.SetFloat(n("_RimMaskOnlyMask", "_Rim2MaskOnlyMask"), mask != null ? 1 : 0);
            m.SetFloat(n("_RimUseMainTexUV", "_Rim2UseMainTexUV"), 0);
            bool direction = rim.LightSide.Mode == AtlasLightSideMode.Mask && dark != null;
            m.SetFloat(n("_RimDirStrength", "_Rim2DirStrength"), direction ? rim.LightSide.Strength : 0f);
            if (!direction) return;
            m.SetFloat(n("_RimDirRange", "_Rim2DirRange"), -rim.LightSide.Low);
            m.SetFloat(n("_RimIndirRange", "_Rim2IndirRange"), -dark.LightSide.Low);
            m.SetFloat(n("_RimIndirBorder", "_Rim2IndirBorder"), (dark.Low + dark.High) * 0.5f);
            m.SetFloat(n("_RimIndirBlur", "_Rim2IndirBlur"), dark.High - dark.Low);
            m.SetColor(n("_RimIndirColor", "_Rim2IndirColor"), dark.Color.Color.gamma);
        }

        // A mask texture slot read on UV0 as it is.
        private static void WriteMaskSlot(Material m, string property, AtlasMask mask)
        {
            m.SetTexture(property, mask?.Map?.Texture);
            m.SetTextureScale(property, Vector2.one);
            m.SetTextureOffset(property, Vector2.zero);
            m.SetFloat(property + "UV", 0);
        }

        private static int MaskChannel(AtlasMask mask) => mask?.Map?.Texture != null ? Mathf.Clamp((int)mask.Channel, 0, 3) : 0;

        // Emission 1 (POI_EMISSION_1): the live emission, × its global mask channel by multiply.
        private static void WriteLiveEmission(AtlasUnifiedMaterial output, Material m)
        {
            var emission = AtlasEmissionFeature.LiveOf(output);
            var map = emission?.Source.Map?.Texture;
            m.SetFloat("_EnableEmission1", map != null ? 1 : 0);
            if (map != null) m.EnableKeyword("POI_EMISSION_1");
            else m.DisableKeyword("POI_EMISSION_1");
            if (map == null) return;
            float total = emission.Targets.First(t => t.Target == AtlasSurfaceTarget.Emission).Opacity;
            float strength = Mathf.Min(total, 20f);
            float rest = total / Mathf.Max(strength, 1e-4f);
            m.SetTexture("_EmissionMap1", map);
            m.SetTextureScale("_EmissionMap1", Vector2.one);
            m.SetTextureOffset("_EmissionMap1", Vector2.zero);
            m.SetFloat("_EmissionMap1UV", 0);
            m.SetColor("_EmissionColor1", new Color(rest, rest, rest, 1f).gamma);
            m.SetFloat("_EmissionColor1ThemeIndex", 0);
            m.SetFloat("_EmissionStrength1", strength);
            m.SetTexture("_EmissionMask1", null);
            m.SetFloat("_EmissionReplace1", 0);
            m.SetFloat("_EmissionBaseColorAsMap1", 0);
            Consumer(m, "_EmissionMask1GlobalMask", emission.Mask.Bus, emission.Mask.BusBlend);
        }

        // A hue shift as Poiyomi's sections have it: enabled, shift, speed (turns per 20 seconds),
        // color space (1 HSV, 0 Oklab), select (0) or shift (1). A numbered section (emission 2)
        // puts its number after each name: `suffix`.
        private static void WriteHue(Material m, string prefix, string select, AtlasColorAdjustment adjustment, AtlasModulator turn,
            string enabled = null, string suffix = "")
        {
            m.SetFloat(enabled ?? prefix + "Enabled" + suffix, adjustment != null ? 1 : 0);
            if (adjustment == null) return;
            m.SetFloat(prefix + suffix, adjustment.Amount);
            m.SetFloat(prefix + "Speed" + suffix, turn != null ? AtlasHueCycling.Rate(turn) / 0.05f : 0f);
            m.SetFloat(prefix + "ColorSpace" + suffix, adjustment.HueMethod == AtlasHueMethod.HSV ? 1 : 0);
            m.SetFloat(select, adjustment.HueSet ? 0 : 1);
        }

        private static void WriteLayerHue(Material m, string prefix, string select, AtlasUnifiedMaterial output, AtlasLayer layer, string suffix = "")
        {
            var (adjustment, turn) = layer != null ? AtlasHueCycling.LayerHue(output, layer) : (null, null);
            WriteHue(m, prefix, select, adjustment, turn, suffix: suffix);
        }

        // The base color's hue cycling (Color Adjust, COLOR_GRADING_HDR: the hue shift masked by
        // _MainColorAdjustTexture's R) and the final color's (Post Processing, POSTPROCESS).
        private static void WriteHueCycling(AtlasUnifiedMaterial output, Material m)
        {
            var color = AtlasHueCycling.Of(output, AtlasAdjustmentTarget.BaseColor).Select(c => ((int, AtlasColorAdjustment, AtlasModulator)?)c).FirstOrDefault();
            m.SetFloat("_MainColorAdjustToggle", color.HasValue ? 1 : 0);
            if (color.HasValue) m.EnableKeyword("COLOR_GRADING_HDR");
            else m.DisableKeyword("COLOR_GRADING_HDR");
            if (color.HasValue)
            {
                var (_, adjustment, turn) = color.Value;
                m.SetFloat("_MainHueShiftReplace", 1);
                WriteHue(m, "_MainHueShift", "_MainHueShiftSelectOrShift", adjustment, turn, "_MainHueShiftToggle");
                m.SetTexture("_MainColorAdjustTexture", adjustment.Mask?.Map?.Texture);
                m.SetTextureScale("_MainColorAdjustTexture", Vector2.one);
                m.SetTextureOffset("_MainColorAdjustTexture", Vector2.zero);
                m.SetFloat("_MainColorAdjustTextureUV", 0);
                m.SetFloat("_MainHueGlobalMask", 0);
            }
            var final = AtlasHueCycling.Of(output, AtlasAdjustmentTarget.FinalColor).Select(c => ((int, AtlasColorAdjustment, AtlasModulator)?)c).FirstOrDefault();
            m.SetFloat("_PostProcess", final.HasValue ? 1 : 0);
            if (final.HasValue) m.EnableKeyword("POSTPROCESS");
            else m.DisableKeyword("POSTPROCESS");
            if (!final.HasValue) return;
            var (__, finalAdjustment, finalTurn) = final.Value;
            m.SetFloat("_PPHue", finalAdjustment.Amount);
            m.SetFloat("_PPHueSpeed", AtlasHueCycling.Rate(finalTurn) / 0.05f);
            m.SetFloat("_PPHueShiftColorSpace", finalAdjustment.HueMethod == AtlasHueMethod.HSV ? 1 : 0);
            m.SetFloat("_ppHueSelectOrShift", finalAdjustment.HueSet ? 0 : 1);
            WriteMaskSlot(m, "_PPMask", finalAdjustment.Mask);
            m.SetFloat("_PPMaskChannel", MaskChannel(finalAdjustment.Mask));
            m.SetFloat("_PPMaskInvert", finalAdjustment.Mask?.Map?.Texture != null && finalAdjustment.Mask.Invert ? 1 : 0);
            m.SetFloat("_PPMaskGlobalMask", 0);
        }

        // Emissions 2 and 3 (POI_EMISSION_2, POI_EMISSION_3): emissions whose hue cycles.
        private static void WriteCyclingEmission(AtlasUnifiedMaterial output, Material m)
        {
            var slots = AtlasEmissionFeature.CyclingOf(output);
            for (int k = 0; k < 2; k++)
            {
                string s = (k + 2).ToString();
                var emission = k < slots.Count ? slots[k] : null;
                var map = emission?.Source.Map?.Texture;
                m.SetFloat("_EnableEmission" + s, map != null ? 1 : 0);
                if (map != null) m.EnableKeyword("POI_EMISSION_" + s);
                else m.DisableKeyword("POI_EMISSION_" + s);
                if (map == null) continue;
                float total = emission.Targets.First(t => t.Target == AtlasSurfaceTarget.Emission).Opacity;
                float strength = Mathf.Min(total, 20f);
                // What's above 20 goes into the color (none at strength 0: AudioLink adds it).
                float rest = total > 20f ? total / 20f : 1f;
                m.SetTexture("_EmissionMap" + s, map);
                m.SetTextureScale("_EmissionMap" + s, Vector2.one);
                m.SetTextureOffset("_EmissionMap" + s, Vector2.zero);
                m.SetFloat("_EmissionMap" + s + "UV", 0);
                // The slot's color (an AudioLink emission's brightest color) × what's above 20.
                var tint = emission.Source.Color * rest;
                m.SetColor("_EmissionColor" + s, new Color(tint.r, tint.g, tint.b, 1f).gamma);
                m.SetFloat("_EmissionColor" + s + "ThemeIndex", 0);
                m.SetFloat("_EmissionStrength" + s, strength);
                m.SetTexture("_EmissionMask" + s, null);
                m.SetFloat("_EmissionReplace" + s, 0);
                m.SetFloat("_EmissionBaseColorAsMap" + s, 0);
                WriteLayerHue(m, "_EmissionHueShift", "_EmissionHueSelectOrShift" + s, output, emission, s);
                WriteEmissionAudio(m, output, emission, s);
            }
        }

        // ---- AudioLink, as the Poiyomi parser reads it (AtlasPoiyomiAudioLink) ----

        private static readonly string[] AudioSmoothing =
            { "_AudioLinkSmoothingBass", "_AudioLinkSmoothingLowMid", "_AudioLinkSmoothingHighMid", "_AudioLinkSmoothingTreble" };

        // The section (POI_AUDIOLINK) and each band's smoothing, from the modulators.
        private static void WriteAudioLinkGlobals(AtlasUnifiedMaterial output, Material m)
        {
            var audio = output.Modulators.Where(AtlasAudio.IsAudio).ToList();
            m.SetFloat("_EnableAudioLink", audio.Count > 0 ? 1 : 0);
            if (audio.Count > 0) m.EnableKeyword("POI_AUDIOLINK");
            else m.DisableKeyword("POI_AUDIOLINK");
            if (audio.Count == 0) return;
            m.SetFloat("_AudioLinkAnimToggle", 1);
            for (int band = 0; band < 4; band++)
            {
                var first = audio.FirstOrDefault(a => (int)a.Audio.Band == band && a.Audio.Spread == AtlasAudioSpread.None);
                if (first != null) m.SetFloat(AudioSmoothing[band], first.Audio.Smoothing);
            }
        }

        // target mode lerp(range.x, range.y, band): the range (neutral without) and the band.
        private static void WriteAudioRange(Material m, AtlasUnifiedMaterial output, AtlasLayer layer, string part, AtlasModulatorMode mode,
            string band, string range)
        {
            var modulator = layer != null ? AtlasAudio.Find(output, layer, part, mode) : null;
            float neutral = mode == AtlasModulatorMode.Multiply ? 1f : 0f;
            m.SetVector(range, modulator != null ? new Vector4(modulator.Min, modulator.Max, 0f, 0f) : new Vector4(neutral, neutral, 0f, 0f));
            if (modulator != null) m.SetFloat(band, (int)modulator.Audio.Band);
        }

        // The color override: prefix + Enabled, Band, ThemeIndex and Ramp, each + suffix; the color.
        private static void WriteAudioColor(Material m, AtlasUnifiedMaterial output, AtlasLayer layer, string part, string prefix, string suffix = "")
        {
            var modulator = layer != null ? AtlasAudio.Find(output, layer, part, AtlasModulatorMode.BlendToColor) : null;
            m.SetFloat(prefix + "Enabled" + suffix, modulator != null ? 1 : 0);
            if (modulator == null) return;
            m.SetFloat(prefix + "Band" + suffix, (int)modulator.Audio.Band);
            m.SetColor(prefix + suffix, modulator.Color.gamma);
            m.SetFloat(prefix + "ThemeIndex" + suffix, 0);
            m.SetVector(prefix + "Ramp" + suffix, modulator.Curve);
        }

        // Chronotensity × speed added to the part.
        private static void WriteAudioChrono(Material m, AtlasUnifiedMaterial output, AtlasLayer layer, string part, string band, string motion, string speed)
        {
            var modulator = layer != null ? AtlasAudio.Find(output, layer, part, AtlasModulatorMode.Add, AtlasAudioSpread.Chrono) : null;
            m.SetFloat(speed, modulator != null ? modulator.Max : 0f);
            if (modulator == null) return;
            m.SetFloat(band, (int)modulator.Audio.Band);
            m.SetFloat(motion, modulator.Audio.ChronoMotion);
        }

        // A × (1 + add / base) modulator written back as Poiyomi's add to a base value.
        private static void WriteAudioAdd(Material m, AtlasUnifiedMaterial output, AtlasLayer layer, string part, string band, string range, float baseValue)
        {
            var modulator = layer != null ? AtlasAudio.Find(output, layer, part, AtlasModulatorMode.Multiply) : null;
            m.SetVector(range, modulator != null ? new Vector4((modulator.Min - 1f) * baseValue, (modulator.Max - 1f) * baseValue, 0f, 0f) : Vector4.zero);
            if (modulator != null) m.SetFloat(band, (int)modulator.Audio.Band);
        }

        private static void WriteEmissionAudio(Material m, AtlasUnifiedMaterial output, AtlasSurfaceLayer emission, string s)
        {
            string a = "_EmissionAL" + s;
            bool any = AtlasAudio.HasAudio(output, emission);
            m.SetFloat(a + "Enabled", any ? 1 : 0);
            if (!any) return;
            WriteAudioRange(m, output, emission, ".targets[0].opacity", AtlasModulatorMode.Add, a + "StrengthBand", a + "StrengthMod");
            WriteAudioRange(m, output, emission, ".targets[0].opacity", AtlasModulatorMode.Multiply, a + "MultipliersBand", a + "Multipliers");
            var centerOut = AtlasAudio.Find(output, emission, ".targets[0].opacity", AtlasModulatorMode.Add, AtlasAudioSpread.History);
            string c = "_AudioLinkEmission" + s + "CenterOut";
            m.SetVector(c, centerOut != null ? new Vector4(centerOut.Min, centerOut.Max, 0f, 0f) : Vector4.zero);
            if (centerOut != null)
            {
                m.SetFloat(c + "Band", (int)centerOut.Audio.Band);
                m.SetFloat(c + "Duration", centerOut.Audio.HistoryLength / 128f);
                m.SetFloat(c + "Size", centerOut.Audio.Threshold);
            }
            WriteAudioColor(m, output, emission, ".source.color", a + "ColorOverride");
        }

        private static void WriteMatcapAudio(Material m, AtlasUnifiedMaterial output, int index, string p, List<AtlasLookupLayer> layers)
        {
            string a = "_Matcap" + index + "AL";
            var first = layers?.FirstOrDefault();
            bool any = layers != null && layers.Any(l => AtlasAudio.HasAudio(output, l));
            m.SetFloat(a + "Enabled", any ? 1 : 0);
            if (!any) return;
            WriteAudioRange(m, output, first, ".source.color.a", AtlasModulatorMode.Add, a + "AlphaAddBand", a + "AlphaAdd");
            WriteAudioRange(m, output, first, ".emissionAmount", AtlasModulatorMode.Add, a + "EmissionAddBand", a + "EmissionAdd");
            WriteAudioAdd(m, output, first, ".source.color", a + "IntensityAddBand", a + "IntensityAdd", m.GetFloat(p + "Intensity"));
            WriteAudioColor(m, output, first, ".source.color", a + "ColorOverride");
        }

        private static void WriteRimAudio(Material m, AtlasUnifiedMaterial output, AtlasAngleLayer rim, RimName n)
        {
            string a = n("_AudioLinkRim", "_AudioLinkRim2");
            WriteAudioRange(m, output, rim, ".high", AtlasModulatorMode.Add, a + "WidthBand", a + "WidthAdd");
            WriteAudioRange(m, output, rim, ".emissionAmount", AtlasModulatorMode.Add, a + "EmissionBand", a + "EmissionAdd");
            WriteAudioAdd(m, output, rim, ".color.color", a + "BrightnessBand", a + "BrightnessAdd", m.GetFloat(n("_RimBrightness", "_Rim2Brightness")));
            WriteAudioColor(m, output, rim, ".color.color", a + "ColorOverride");
        }

        private static void WriteGlitterAudio(Material m, AtlasUnifiedMaterial output, AtlasPatternLayer layer)
        {
            bool any = AtlasAudio.HasAudio(output, layer);
            m.SetFloat("_GlitterALEnabled", any ? 1 : 0);
            if (!any) return;
            WriteAudioRange(m, output, layer, ".opacity", AtlasModulatorMode.Add, "_GlitterALAlphaAddBand", "_GlitterALAlphaAdd");
            WriteAudioRange(m, output, layer, ".glitter.maxBrightness", AtlasModulatorMode.Add, "_GlitterALMaxBrightnessBand", "_GlitterALMaxBrightnessAdd");
            WriteAudioRange(m, output, layer, ".glitter.size", AtlasModulatorMode.Add, "_GlitterALSizeAddBand", "_GlitterALSizeAdd");
            WriteAudioChrono(m, output, layer, ".glitter.sparkleOffset", "_GlitterALChronoSparkleSpeedBand", "_GlitterALChronoSparkleSpeedType", "_GlitterALChronoSparkleSpeed");
            WriteAudioChrono(m, output, layer, ".glitter.rotationOffset", "_GlitterALChronoRotationSpeedBand", "_GlitterALChronoRotationSpeedType", "_GlitterALChronoRotationSpeed");
            WriteAudioColor(m, output, layer, ".color", "_GlitterALColorOverride");
        }

        // Subsurface scattering (POI_SUBSURFACESCATTERING), as the Poiyomi parser reads it
        // (AtlasPoiyomiEdgeLight): the color and thickness from one texture (its channel enum: R 1,
        // G 2, B 3, A 0), the strength × π in the layer.
        private static void WriteSubsurface(AtlasUnifiedMaterial output, Material m)
        {
            var sss = output.Layers.OfType<AtlasSubsurfaceLayer>().FirstOrDefault(l => l.Enabled);
            m.SetFloat("_SubsurfaceScattering", sss != null ? 1 : 0);
            if (sss != null) m.EnableKeyword("POI_SUBSURFACESCATTERING");
            else m.DisableKeyword("POI_SUBSURFACESCATTERING");
            if (sss == null) return;
            var map = sss.Thickness?.Texture ?? sss.Color.Map?.Texture;
            m.SetTexture("_SSSThicknessMap", map);
            m.SetTextureScale("_SSSThicknessMap", Vector2.one);
            m.SetTextureOffset("_SSSThicknessMap", Vector2.zero);
            m.SetFloat("_SSSThicknessMapUV", 0);
            m.SetFloat("_SSSThicknessMapChannel", ((int)sss.ThicknessChannel + 1) % 4);
            // Without a map the shader's thickness is 1: the offset is read one lower.
            m.SetFloat("_SSSThicknessMod", map != null ? sss.ThicknessOffset : sss.ThicknessOffset - 1f);
            m.SetFloat("_SSSStrength", sss.Intensity / Mathf.PI);
            m.SetFloat("_SSSSpread", sss.Power);
            m.SetFloat("_SSSDistortion", sss.Distortion);
            m.SetColor("_SSSColor", sss.Color.Color.gamma);
            m.SetFloat("_SSSLightColorMix", sss.LightColorMix);
            m.SetFloat("_SSSShadowStrength", sss.ShadowStrength);
            m.SetFloat("_SSSBaseColorMix", sss.BaseColorMix);
            m.SetFloat("_SSSGlobalMaskIndex", 0);
        }

        // Proximity color, as the Poiyomi parser reads it (AtlasPoiyomiColorAdjust): one near and
        // far color for the final color and or the emission.
        private static void WriteProximityColor(AtlasUnifiedMaterial output, Material m)
        {
            var tints = AtlasProximityColorFeature.Of(output);
            m.SetFloat("_FXProximityColor", tints.Count > 0 ? 1 : 0);
            if (tints.Count == 0) return;
            var (adjustment, modulator) = tints[0];
            m.SetColor("_FXProximityColorMinColor", adjustment.Color.gamma);
            m.SetFloat("_FXProximityColorMinColorThemeIndex", 0);
            m.SetColor("_FXProximityColorMaxColor", modulator.Color.gamma);
            m.SetFloat("_FXProximityColorMaxColorThemeIndex", 0);
            var distance = modulator.CameraDistance;
            m.SetFloat("_FXProximityColorType", distance.PixelPosition ? 1 : 0);
            m.SetFloat("_FXProximityColorMinDistance", distance.Near);
            m.SetFloat("_FXProximityColorMaxDistance", distance.Far);
            m.SetVector("_FXProximityColorCurve", distance.Curve);
            m.SetFloat("_FXProximityColorSolid", distance.Step ? 1 : 0);
            m.SetFloat("_FXProximityColorOverrideColor", tints.Any(t => t.Adjustment.Target == AtlasAdjustmentTarget.FinalColor) ? 1 : 0);
            m.SetFloat("_FXProximityColorOverrideEmission", tints.Any(t => t.Adjustment.Target == AtlasAdjustmentTarget.Emission) ? 1 : 0);
            m.SetFloat("_FXProximityColorOverrideAlpha", 0);
            m.SetFloat("_FXProximityColorBackFace", 0);
        }

        // Pro's own sections, as the Poiyomi parser reads them (AtlasPoiyomiPro).

        // GT7 tone mapping of the final color (GT7_TONEMAPPING), and Pro 11's neutral surface
        // emission.
        private static void WriteToneMapping(AtlasUnifiedMaterial output, Material m)
        {
            var tone = AtlasToneMappingFeature.Of(output, AtlasColorOperation.ToneMapGt7);
            m.SetFloat("_GT7ToneMapping", tone != null ? 1 : 0);
            if (tone != null) m.EnableKeyword("GT7_TONEMAPPING");
            else m.DisableKeyword("GT7_TONEMAPPING");
            if (tone != null)
            {
                m.SetFloat("_GT7TM_ColorAccuracy", tone.Settings.x);
                m.SetFloat("_GT7TM_ChromaFadeStart", tone.Settings.y);
                m.SetFloat("_GT7TM_ChromaFadeEnd", tone.Settings.z);
            }
            var neutral = AtlasToneMappingFeature.Of(output, AtlasColorOperation.NeutralEmission);
            m.SetFloat("_NeutralEmission", neutral != null ? 1 : 0);
            if (neutral == null) return;
            m.SetFloat("_NeutralEmissionHDRRetention", neutral.Settings.x);
            m.SetFloat("_NeutralEmissionHueCorrection", neutral.Settings.y);
        }

        // Screen-space ambient occlusion (POI_SSAO): its color by the layer's blend, its mask by
        // channel.
        private static void WriteScreenOcclusion(AtlasUnifiedMaterial output, Material m)
        {
            var layer = AtlasScreenOcclusionFeature.Of(output);
            var ao = layer?.AmbientOcclusion;
            m.SetFloat("_SSAOEnabled", ao != null ? 1 : 0);
            if (ao != null) m.EnableKeyword("POI_SSAO");
            else m.DisableKeyword("POI_SSAO");
            if (ao == null) return;
            m.SetFloat("_SSAOAnimationToggle", 1);
            m.SetFloat("_SSAOIntensity", ao.Intensity);
            m.SetFloat("_SSAORadius", ao.Radius);
            m.SetFloat("_SSAOQuality", ao.Quality);
            m.SetFloat("_SSAOCenterImportance", ao.CenterImportance);
            m.SetFloat("_SSAOBias", ao.DepthBias);
            m.SetFloat("_SSAOCone", ao.ConeBias);
            m.SetFloat("_SSAORandomScale", ao.Jitter);
            m.SetFloat("_SSAOUseNormals", ao.NormalAmount);
            m.SetFloat("_SSAONormalBias", ao.NormalRejection);
            m.SetTexture("_SSAOColorMap", ao.ColorMap?.Texture);
            m.SetTextureScale("_SSAOColorMap", Vector2.one);
            m.SetTextureOffset("_SSAOColorMap", Vector2.zero);
            m.SetVector("_SSAOColorMapPan", Vector4.zero);
            m.SetFloat("_SSAOColorMapUV", 0);
            m.SetFloat("_SSAOAsRamp", ao.ColorMap != null && ao.ColorMapAsRamp ? 1 : 0);
            m.SetVector("_SSAOHSV", ao.Hsvg);
            m.SetFloat("_SSAOUseSurfaceColor", ao.SurfaceColorMix);
            m.SetFloat("_SSAOCameraClip", ao.CameraClip);
            m.SetFloat("_SSAOHideByRampedLightMap", ao.HideInLight);
            m.SetFloat("_SSAOAlphaFade", ao.AlphaFade);
            m.SetFloat("_SSAOFalloffStart", ao.QualityFalloff.x);
            m.SetFloat("_SSAOFalloffEnd", ao.QualityFalloff.y);
            m.SetVector("_SSAOMaxDistanceFalloff", ao.MaxDistanceFalloff);
            var color = layer.Color;
            m.SetColor("_SSAOColor", new Color(color.r, color.g, color.b, 1f).gamma);
            m.SetFloat("_SSAOColorThemeIndex", 0);
            m.SetFloat("_SSAOBlendType", BlendType(layer.Blend));
            WriteMaskSlot(m, "_SSAOMask", layer.Mask);
            m.SetVector("_SSAOMaskPan", Vector4.zero);
            m.SetFloat("_SSAOMaskChannel", MaskChannel(layer.Mask));
            m.SetFloat("_SSAOMaskInvert", layer.Mask?.Map?.Texture != null && layer.Mask.Invert ? 1 : 0);
            m.SetFloat("_SSAOGlobalMaskIndex", 0);
            m.SetFloat("_SSAOApplyGlobalMaskIndex", 0);
        }

        // Contact shadows (POI_CONTACT_SHADOWS): blended into the base color (apply mode 0),
        // multiplied into the light map (1) or both (2); the mask by channel.
        private static void WriteContactShadows(AtlasUnifiedMaterial output, Material m)
        {
            var layer = AtlasContactShadowFeature.Of(output);
            var shadow = layer?.ContactShadow;
            m.SetFloat("_ContactShadowsEnabled", shadow != null ? 1 : 0);
            if (shadow != null) m.EnableKeyword("POI_CONTACT_SHADOWS");
            else m.DisableKeyword("POI_CONTACT_SHADOWS");
            if (shadow == null) return;
            m.SetFloat("_ContactShadowsAnimationToggle", 1);
            m.SetFloat("_ContactShadowsApplyMode", shadow.ApplyToColor && shadow.ApplyToLightMap ? 2 : shadow.ApplyToLightMap ? 1 : 0);
            m.SetFloat("_ContactShadowsIntensity", shadow.Intensity);
            m.SetFloat("_ContactShadowsMaxDistance", shadow.MaxDistance);
            m.SetFloat("_ContactShadowsQuality", shadow.Quality);
            m.SetFloat("_ContactShadowsThickness", shadow.Thickness);
            m.SetFloat("_ContactShadowsBias", shadow.DepthBias);
            m.SetFloat("_ContactShadowsLightSource", shadow.CustomDirection ? 1 : 0);
            m.SetVector("_ContactShadowsCustomLightDir", new Vector4(shadow.Direction.x, shadow.Direction.y, shadow.Direction.z, 0f));
            m.SetFloat("_ContactShadowsCustomLightSpace", (int)shadow.DirectionSpace);
            m.SetVector("_ContactShadowsCameraFalloff", new Vector4(shadow.CameraFalloff.x, shadow.CameraFalloff.y, 0f, 0f));
            m.SetFloat("_ContactShadowsNormalBias", shadow.NormalRejection);
            m.SetFloat("_ContactShadowsHideInLight", shadow.HideInLight);
            m.SetFloat("_ContactShadowsNoiseScale", shadow.Dither);
            m.SetFloat("_ContactShadowsRejectionDepth", shadow.RejectionDepth);
            m.SetColor("_ContactShadowsColor", layer.Color.gamma);
            m.SetFloat("_ContactShadowsColorThemeIndex", 0);
            m.SetFloat("_ContactShadowsBlendType", BlendType(layer.Blend));
            WriteMaskSlot(m, "_ContactShadowsMask", layer.Mask);
            m.SetVector("_ContactShadowsMaskPan", Vector4.zero);
            m.SetFloat("_ContactShadowsMaskChannel", MaskChannel(layer.Mask));
            m.SetFloat("_ContactShadowsMaskInvert", layer.Mask?.Map?.Texture != null && layer.Mask.Invert ? 1 : 0);
            m.SetFloat("_ContactShadowsGlobalMaskIndex", 0);
            m.SetFloat("_ContactShadowsApplyGlobalMaskIndex", 0);
            m.SetFloat("_ContactShadowsDebug", 0);
        }

        // A layer's blend as Poiyomi's blend type, the reverse of the parser's reading.
        private static int BlendType(AtlasBlend blend)
        {
            switch (blend)
            {
                case AtlasBlend.Darken: return 1;
                case AtlasBlend.Multiply: return 2;
                case AtlasBlend.Lighten: return 5;
                case AtlasBlend.Screen: return 6;
                case AtlasBlend.Subtract: return 7;
                case AtlasBlend.Add: return 8;
                case AtlasBlend.Overlay: return 9;
                case AtlasBlend.Mixed: return 20;
                default: return 0;
            }
        }

        // Glitter (_SUNDISK_SIMPLE), as the Poiyomi parser reads it (AtlasPoiyomiPatterns).
        private static void WriteGlitter(AtlasUnifiedMaterial output, Material m)
        {
            var layer = output.Layers.OfType<AtlasPatternLayer>().FirstOrDefault(l => l.Kind == AtlasPatternKind.Glitter && l.Glitter != null);
            m.SetFloat("_GlitterEnable", layer != null ? 1 : 0);
            if (layer != null) m.EnableKeyword("_SUNDISK_SIMPLE");
            else m.DisableKeyword("_SUNDISK_SIMPLE");
            if (layer == null) return;
            var g = layer.Glitter;
            bool linear = g.Mode == AtlasGlitterMode.Linear;
            m.SetFloat("_GlitterMode", (int)g.Mode);
            m.SetFloat("_GlitterShape", g.Shape == AtlasGlitterShape.Square ? 1 : 0);
            m.SetFloat("_GlitterUV", Mathf.Clamp(g.Space - AtlasTextureSpace.UV0, 0, 3));
            m.SetVector("_GlitterUVPanning", new Vector4(g.Scroll.x / 0.05f, g.Scroll.y / 0.05f, 0f, 0f));
            m.SetFloat("_GlitterFrequency", g.Density);
            m.SetFloat("_GlitterLayers", g.Layers);
            m.SetFloat("_GlitterRandomLocation", g.RandomPosition);
            m.SetFloat("_GlitterFullCoverage", g.Overlap ? 1 : 0);
            m.SetFloat("_GlitterSize", g.Size);
            m.SetFloat("_GlitterRandomSize", g.RandomSize ? 1 : 0);
            m.SetVector("_GlitterMinMaxSize", new Vector4(g.SizeRange.x, g.SizeRange.y, 0f, 1f));
            m.SetFloat("_GlitterUseNormals", g.NormalAmount);
            m.SetFloat(linear ? "_GlitterLinearMinBrightness" : "_GlitterMinBrightness", g.MinBrightness);
            m.SetFloat(linear ? "_GlitterLinearBrightness" : "_GlitterBrightness", g.MaxBrightness);
            m.SetFloat(linear ? "_GlitterLinearSpeed" : "_GlitterSpeed", g.Speed);
            m.SetFloat(linear ? "_GlitterLinearDistanceLOD" : "_GlitterDistanceLOD", g.DistanceLOD ? 1 : 0);
            m.SetFloat("_GlitterAngleRange", g.AngleRange);
            m.SetFloat("_GlitterBias", g.Bias);
            m.SetFloat("_GlitterAngleOffset", g.AngleOffset);
            m.SetFloat("_GlitterContrast", g.Contrast);
            m.SetFloat("_GlitterJaggyFix", g.JaggyFix);
            m.SetFloat("_GlitterLinearSensitivity", g.LinearSensitivity);
            m.SetFloat("_GlitterLinearContrast", g.LinearContrast);
            m.SetFloat("_GlitterLinearPostContrast", g.LinearPostContrast);
            m.SetFloat("_GlitterLinearAngle", g.LinearAngleLimit);
            m.SetFloat("_GlitterLinearLightDirection", g.LinearLightDirection);
            m.SetFloat("_GlitterLinearVRParallax", g.LinearVRParallax);
            m.SetTexture("_GlitterColorMap", g.ColorMap?.Texture);
            m.SetFloat("_GlitterColorMapUV", 0);
            m.SetFloat("_GlitterUseSurfaceColor", g.BaseColorMix);
            m.SetFloat("_GlitterRandomColors", g.RandomColors ? 1 : 0);
            m.SetVector("_GlitterMinMaxSaturation", new Vector4(g.SaturationRange.x, g.SaturationRange.y, 0f, 1f));
            m.SetVector("_GlitterMinMaxBrightness", new Vector4(g.BrightnessRange.x, g.BrightnessRange.y, 0f, 1f));
            m.SetFloat("_GlitterRandomRotation", g.RandomRotation ? 1 : 0);
            m.SetFloat("_GlitterRandomRotationDirection", g.RandomDirection ? 1 : 0);
            m.SetFloat("_GlitterTextureRotation", g.RotationSpeed);
            m.SetVector("_GlitterRandomRotationSpeed", new Vector4(g.RandomRotationSpeed.x, g.RandomRotationSpeed.y, 0f, 0f));
            m.SetFloat("_GlitterScaleWithLighting", g.LightScale);
            m.SetTexture("_GlitterTexture", g.Texture?.Texture);
            m.SetFloat("_GlitterTexturePixelMode", g.Texture != null && g.Texture.Sampling == AtlasTextureSampling.Pixel ? 1 : 0);
            var frames = g.Texture?.Frames;
            m.SetFloat("_GlitterSpriteSheetEnabled", frames != null ? 1 : 0);
            if (frames != null)
            {
                m.SetFloat("_GlitterSpriteSheetColumns", frames.Columns);
                m.SetFloat("_GlitterSpriteSheetRows", frames.Rows);
                m.SetFloat("_GlitterSpriteSheetFPS", frames.Rate);
                m.SetFloat("_GlitterSpriteSheetTotalFrames", frames.Count);
                m.SetFloat("_GlitterSpriteSheetFrameSelection", g.RandomFrame ? 0 : 1);
                m.SetFloat("_GlitterSpriteSheetRandomStart", g.RandomStartFrame ? 1 : 0);
            }
            m.SetFloat("_GlitterBlendType", layer.Blend == AtlasBlend.Replace ? 1 : 0);
            var color = layer.Color;
            m.SetColor("_GlitterColor", new Color(color.r, color.g, color.b, Mathf.Clamp01(layer.Opacity)).gamma);
            m.SetFloat("_GlitterColorThemeIndex", 0);
            WriteMaskSlot(m, "_GlitterMask", layer.Mask);
            m.SetFloat("_GlitterMaskChannel", MaskChannel(layer.Mask));
            m.SetFloat("_GlitterMaskInvert", layer.Mask?.Map?.Texture != null && layer.Mask.Invert ? 1 : 0);
            Consumer(m, "_GlitterMaskGlobalMask", layer.Mask?.Bus ?? -1, layer.Mask?.BusBlend ?? AtlasMaskBlend.Add);
            m.SetFloat("_GlitterHideInShadow", layer.HideInShadow);
            WriteLayerHue(m, "_GlitterHueShift", "_GlitterHueSelectOrShift", output, layer);
            WriteGlitterAudio(m, output, layer);
        }

        // The detail texture and detail normal kept live (FINALPASS), as the Poiyomi parser reads
        // them: on their preserved UV channel at tiling 1, the texture × tint × brightness at its
        // intensity, the normal at its scale, both masked by _DetailMask's R and G.
        private static void WriteDetails(AtlasUnifiedMaterial output, Material m)
        {
            var texture = AtlasDetailFeatures.Of(output, AtlasSurfaceTarget.BaseColor);
            var normal = AtlasDetailFeatures.Of(output, AtlasSurfaceTarget.Normal);
            bool on = texture != null || normal != null;
            m.SetFloat("_DetailEnabled", on ? 1 : 0);
            if (on) m.EnableKeyword("FINALPASS");
            else m.DisableKeyword("FINALPASS");
            if (!on) return;
            m.SetTexture("_DetailMask", texture?.Mask?.Map?.Texture ?? normal?.Mask?.Map?.Texture);
            m.SetTextureScale("_DetailMask", Vector2.one);
            m.SetTextureOffset("_DetailMask", Vector2.zero);
            m.SetFloat("_DetailMaskUV", 0);
            m.SetFloat("_DetailTexGlobalMask", 0);
            m.SetFloat("_DetailNormalGlobalMask", 0);
            m.SetTexture("_DetailTex", texture?.Source.Map.Texture);
            m.SetTextureScale("_DetailTex", Vector2.one);
            m.SetTextureOffset("_DetailTex", Vector2.zero);
            if (texture != null)
            {
                m.SetFloat("_DetailTexUV", AtlasDetailFeatures.UV(texture));
                var color = texture.Source.Color;
                float brightness = Mathf.Max(1f, color.maxColorComponent);
                m.SetColor("_DetailTint", new Color(color.r / brightness, color.g / brightness, color.b / brightness, 1f).gamma);
                m.SetFloat("_DetailTintThemeIndex", 0);
                m.SetFloat("_DetailBrightness", brightness);
                m.SetFloat("_DetailTexIntensity", texture.Targets[0].Opacity);
            }
            else m.SetFloat("_DetailTexIntensity", 0);
            m.SetTexture("_DetailNormalMap", normal?.Source.Map.Texture);
            m.SetTextureScale("_DetailNormalMap", Vector2.one);
            m.SetTextureOffset("_DetailNormalMap", Vector2.zero);
            if (normal != null)
            {
                m.SetFloat("_DetailNormalMapUV", AtlasDetailFeatures.UV(normal));
                m.SetFloat("_DetailNormalMapScale", normal.Targets[0].Opacity);
            }
        }

        // Global masks (1.5), as the Poiyomi parser reads them: the texture steps' maps as Global
        // Mask Textures (channel c in texture c / 4), the back face, mirror, camera and distance
        // modifiers per channel, blacklight masking; and every consumer of the output's channels.
        private static void WriteGlobalMasks(AtlasUnifiedMaterial output, Material m)
        {
            var steps = output.MaskBus.Steps;
            var textures = new Texture[4];
            foreach (var step in steps.Where(s => s.Kind == AtlasMaskBusStepKind.Texture && s.Mask?.Map?.Texture != null))
                textures[Mathf.Clamp(step.Channel / 4, 0, 3)] = step.Mask.Map.Texture;
            bool anyTexture = textures.Any(t => t != null);
            m.SetFloat("_GlobalMaskTexturesEnable", anyTexture ? 1 : 0);
            if (anyTexture) m.EnableKeyword("POI_GLOBALMASK_TEXTURES");
            else m.DisableKeyword("POI_GLOBALMASK_TEXTURES");
            for (int t = 0; t < 4; t++)
            {
                string p = "_GlobalMaskTexture" + t;
                m.SetTexture(p, textures[t]);
                m.SetTextureScale(p, Vector2.one);
                m.SetTextureOffset(p, Vector2.zero);
                m.SetFloat(p + "UV", 0);
                m.SetFloat(p + "Split", 0);
            }
            bool faces = false, mirror = false, camera = false, distance = false;
            for (int c = 0; c < 16; c++)
            {
                var channel = steps.Where(s => s.Channel == c).ToList();
                var face = channel.FirstOrDefault(s => s.Kind == AtlasMaskBusStepKind.Faces);
                m.SetFloat("_GlobalMaskBackface_" + c, face == null ? 0 : face.Faces == AtlasFaces.Back ? 1 : 2);
                faces |= face != null;
                var inMirror = channel.FirstOrDefault(s => s.Kind == AtlasMaskBusStepKind.Mirror);
                m.SetFloat("_GlobalMaskMirror_" + c, inMirror == null ? 0 : inMirror.Inside ? 2 : 1);
                mirror |= inMirror != null;
                var inCamera = channel.FirstOrDefault(s => s.Kind == AtlasMaskBusStepKind.Camera);
                m.SetFloat("_GlobalMaskCamera_" + c, inCamera == null ? 0 : inCamera.Inside ? 2 : 1);
                camera |= inCamera != null;
                var far = channel.FirstOrDefault(s => s.Kind == AtlasMaskBusStepKind.Distance);
                m.SetFloat("_GlobalMaskDistanceEnable_" + c, far != null ? 1 : 0);
                distance |= far != null;
                if (far != null)
                {
                    m.SetFloat("_GlobalMaskDistanceType_" + c, far.PixelPosition ? 1 : 0);
                    m.SetFloat("_GlobalMaskDistanceMin_" + c, far.Min);
                    m.SetFloat("_GlobalMaskDistanceMax_" + c, far.Max);
                    m.SetFloat("_GlobalMaskDistanceMinAlpha_" + c, far.MinValue);
                    m.SetFloat("_GlobalMaskDistanceMaxAlpha_" + c, far.MaxValue);
                    m.SetFloat("_GlobalMaskDistanceBlendType_" + c, far.Blend == AtlasMaskBlend.Multiply ? 2 : 0);
                }
            }
            m.SetFloat("_GlobalMaskModifiersBackfaceEnable", faces ? 1 : 0);
            m.SetFloat("_GlobalMaskModifiersMirrorEnable", mirror ? 1 : 0);
            m.SetFloat("_GlobalMaskMirrorVisibilityMode", 1);
            m.SetFloat("_GlobalMaskModifiersCameraEnable", camera ? 1 : 0);
            m.SetFloat("_GlobalMaskModifiersDistanceEnable", distance ? 1 : 0);
            var blacklights = steps.Where(s => s.Kind == AtlasMaskBusStepKind.Blacklight).Take(4).ToList();
            m.SetFloat("_BlackLightMaskingEnabled", blacklights.Count > 0 ? 1 : 0);
            if (blacklights.Count > 0) m.EnableKeyword("POI_BLACKLIGHTMASKING");
            else m.DisableKeyword("POI_BLACKLIGHTMASKING");
            for (int b = 0; b < 4; b++)
            {
                string p = "_BlackLightMasking" + b;
                var light = b < blacklights.Count ? blacklights[b] : null;
                m.SetFloat(p + "GlobalMaskIndex", light != null ? light.Channel + 1 : 0);
                if (light == null) continue;
                m.SetFloat(p + "Key", light.Amount);
                m.SetVector(p + "Range", new Vector4(light.Min, light.Max, 0f, 0f));
                m.SetFloat(p + "GlobalMaskBlendType", (int)light.Blend);
            }
            m.SetFloat("_GlobalMaskOptionsEnable", 0);
            foreach (var color in new[] { "Red", "Green", "Blue", "Alpha" }) m.SetFloat("_GlobalMaskVertexColor" + color, 0);
            WriteContacts(steps, m);

            // Consumers: the channel (1R = 1) and blend (maskBlend's order; the Mochie strengths
            // use customBlend, whose 8 adds).
            var response = output.Surface.Response;
            Consumer(m, "_MochieMetallicGlobalMask", response.Metallic.Bus, response.Metallic.BusBlend);
            Consumer(m, "_MochieSmoothnessGlobalMask", response.Roughness.Bus, response.Roughness.BusBlend);
            Consumer(m, "_MochieReflectionStrengthGlobalMask", response.Reflections.Mask?.Bus ?? -1, response.Reflections.Mask?.BusBlend ?? AtlasMaskBlend.Add, custom: true);
            Consumer(m, "_MochieSpecularStrengthGlobalMask", response.Highlights.Mask?.Bus ?? -1, response.Highlights.Mask?.BusBlend ?? AtlasMaskBlend.Add, custom: true);
            Consumer(m, "_LightDataAOGlobalMaskR", response.Occlusion.Bus, response.Occlusion.BusBlend, "_LightDataAOGlobalMaskBlendTypeR");
            var coat = response.ClearCoat;
            Consumer(m, "_ClearCoatGlobalMask", coat?.Strength.Bus ?? -1, coat?.Strength.BusBlend ?? AtlasMaskBlend.Add);
            Consumer(m, "_ClearCoatSmoothnessGlobalMask", coat?.Roughness.Bus ?? -1, coat?.Roughness.BusBlend ?? AtlasMaskBlend.Add);
            Consumer(m, "_ClearCoatReflectionStrengthGlobalMask", coat?.Reflections.Mask?.Bus ?? -1, coat?.Reflections.Mask?.BusBlend ?? AtlasMaskBlend.Add);
            Consumer(m, "_ClearCoatSpecularStrengthGlobalMask", coat?.Highlights.Mask?.Bus ?? -1, coat?.Highlights.Mask?.BusBlend ?? AtlasMaskBlend.Add);
            // Alpha: the channel combined with the alpha after Alpha Mod.
            var alpha = output.Surface.Alpha;
            Consumer(m, "_AlphaGlobalMask", alpha.Bus, alpha.BusBlend);
        }

        // Global Mask Contacts (Poiyomi 11), as the Poiyomi parser reads them: up to 8, each
        // writing its touch into a channel by its blend.
        private static void WriteContacts(List<AtlasMaskBusStep> steps, Material m)
        {
            var contacts = steps.Where(s => s.Kind == AtlasMaskBusStepKind.Contact && s.Contact != null).Take(8).ToList();
            m.SetFloat("_GlobalMaskContactsEnable", contacts.Count > 0 ? 1 : 0);
            if (contacts.Count > 0) m.EnableKeyword("POI_GLOBALMASK_CONTACTS");
            else m.DisableKeyword("POI_GLOBALMASK_CONTACTS");
            for (int i = 0; i < 8; i++)
            {
                string s = "_" + i;
                var step = i < contacts.Count ? contacts[i] : null;
                m.SetFloat("_GlobalMaskContactEnable" + s, step != null ? 1 : 0);
                m.SetFloat("_GlobalMaskContactTarget" + s, step != null ? step.Channel + 1 : 0);
                if (step == null) continue;
                var contact = step.Contact;
                m.SetVector("_GlobalMaskContactTag" + s, new Vector4(contact.Tags[0], contact.Tags[1], contact.Tags[2], contact.Tags[3]));
                m.SetFloat("_GlobalMaskContactPeople" + s, (int)contact.People);
                m.SetFloat("_GlobalMaskContactThemeColor" + s, 0);
                m.SetFloat("_GlobalMaskContactRadius" + s, contact.InnerRadius);
                m.SetFloat("_GlobalMaskContactFalloff" + s, contact.OuterRadius);
                m.SetFloat("_GlobalMaskContactToucherScale" + s, contact.ScaleWithToucher ? 1 : 0);
                m.SetFloat("_GlobalMaskContactPosition" + s, contact.AroundTouch ? 1 : 0);
                m.SetFloat("_GlobalMaskContactInvert" + s, contact.Invert ? 1 : 0);
                m.SetFloat("_GlobalMaskContactBlendType" + s, (int)step.Blend);
            }
        }

        private static void Consumer(Material m, string property, int bus, AtlasMaskBlend blend, string blendProperty = null, bool custom = false)
        {
            m.SetFloat(property, bus >= 0 ? bus + 1 : 0);
            if (bus < 0) return;
            m.SetFloat(blendProperty ?? property + "BlendType", custom && blend == AtlasMaskBlend.Add ? 8 : (int)blend);
        }

        // The clear coat, as the Poiyomi parser reads it (AtlasPoiyomiSpecular): strength and
        // smoothness from the packed map (or their constants), the masks in B and A, or none
        // (channel 4: white), the highlight mask sharing B when it reads the reflection mask's map.
        // The installed Poiyomi's highlight scale against the unified one: π from 10.0.26 to
        // 10.0.29 (AtlasPoiyomiVersionDetector.HighlightsTimesPi), 1 otherwise.
        private static float HighlightScale(Material m) =>
            AtlasPoiyomiVersionDetector.HighlightsTimesPi(AtlasPoiyomiVersionDetector.DetectFull(m)) ? Mathf.PI : 1f;

        private static void WriteClearCoat(AtlasClearCoat coat, Material m)
        {
            m.SetFloat("_ClearCoatBRDF", coat != null ? 1 : 0);
            if (coat != null) m.EnableKeyword("POI_CLEARCOAT");
            else m.DisableKeyword("POI_CLEARCOAT");
            if (coat == null) return;
            var packed = coat.Strength.Map?.Texture ?? coat.Roughness.Map?.Texture ?? coat.Reflections.Mask?.Map?.Texture ?? coat.Highlights.Mask?.Map?.Texture;
            m.SetTexture("_ClearCoatMaps", packed);
            m.SetTextureScale("_ClearCoatMaps", Vector2.one);
            m.SetTextureOffset("_ClearCoatMaps", Vector2.zero);
            m.SetFloat("_ClearCoatMapsUV", 0);
            bool strengthMap = coat.Strength.Map != null, roughnessMap = coat.Roughness.Map != null;
            m.SetFloat("_ClearCoatMapsClearCoatMaskChannel", strengthMap ? 0 : 4);
            m.SetFloat("_ClearCoatStrength", coat.Strength.Constant);
            m.SetFloat("_ClearCoatMaskInvert", 0);
            m.SetFloat("_ClearCoatMapsRoughnessChannel", roughnessMap ? 1 : 4);
            // Stored as smoothness × 1; without a map, the smoothness constant.
            m.SetFloat("_ClearCoatSmoothness", roughnessMap ? 1f : 1f - coat.Roughness.Constant);
            m.SetFloat("_ClearCoatSmoothnessMapInvert", 0);
            var reflections = coat.Reflections;
            var highlights = coat.Highlights;
            m.SetFloat("_ClearCoatMapsReflectionMaskChannel", reflections.Mask != null ? 2 : 4);
            m.SetFloat("_ClearCoatMapsSpecularMaskChannel", highlights.Mask == null ? 4
                : reflections.Mask != null && highlights.Mask.Map == reflections.Mask.Map ? 2 : 3);
            m.SetFloat("_ClearCoatReflectionMaskInvert", 0);
            m.SetFloat("_ClearCoatSpecularMaskInvert", 0);
            m.SetFloat("_ClearCoatReflectionStrength", reflections.Enabled ? Mathf.Clamp01(reflections.Strength) : 0f);
            m.SetFloat("_ClearCoatSpecularStrength", highlights.Enabled ? highlights.Strength / HighlightScale(m) : 0f);
            m.SetColor("_ClearCoatReflectionTint", reflections.Tint.gamma);
            m.SetFloat("_ClearCoatReflectionTintThemeIndex", 0);
            m.SetColor("_ClearCoatSpecularTint", highlights.Tint.gamma);
            m.SetFloat("_ClearCoatSpecularTintThemeIndex", 0);
            m.SetFloat("_ClearcoatFresnelStrength", AtlasPoiyomiFresnel.Slider(reflections.FresnelStrength));
            m.SetFloat("_ClearcoatExposureOcclusion", reflections.ExposureOcclusion);
            bool forced = reflections.Source == AtlasReflectionSource.Cubemap && reflections.Cubemap != null;
            m.SetTexture("_ClearCoatFallback", forced ? reflections.Cubemap.Texture : reflections.FallbackCubemap?.Texture);
            m.SetFloat("_ClearCoatForceFallback", forced ? 1 : 0);
            m.SetFloat("_ClearCoatLitFallback", reflections.CubemapLit ? 1 : 0);
            m.SetFloat("_CCIgnoreCastedShadows", highlights.IgnoreShadows ? 1 : 0);
            m.SetFloat("_ClearCoatNormalSelect", coat.NormalMapAmount);
            m.SetFloat("_ClearCoatGSAAEnabled", coat.SpecularAntiAliasing.Enabled ? 1 : 0);
            m.SetFloat("_ClearCoatGSAAVariance", coat.SpecularAntiAliasing.Variance);
            m.SetFloat("_ClearCoatGSAAThreshold", coat.SpecularAntiAliasing.Threshold);
            m.SetFloat("_ClearCoatTPSDepthMaskEnabled", 0);
        }

        // The environmental rim, as the Poiyomi parser reads it (AtlasPoiyomiEdgeLight).
        private static void WriteEnvironmentalRim(AtlasUnifiedMaterial output, Material m)
        {
            var rim = AtlasEnvironmentalRimFeature.Layer(output);
            m.SetFloat("_EnableEnvironmentalRim", rim != null ? 1 : 0);
            if (rim != null) m.EnableKeyword("POI_ENVIRORIM");
            else m.DisableKeyword("POI_ENVIRORIM");
            if (rim == null) return;
            m.SetFloat("_RimEnviroWidth", rim.High);
            m.SetFloat("_RimEnviroSharpness", rim.Low);
            m.SetFloat("_RimEnviroBlur", rim.EnvironmentRoughness);
            m.SetFloat("_RimEnviroMinBrightness", rim.EnvironmentThreshold);
            m.SetFloat("_RimEnviroExposureOcclusion", rim.ExposureOcclusion);
            m.SetFloat("_RimEnviroIntensity", Mathf.Clamp01(rim.Opacity));
            WriteMaskSlot(m, "_RimEnviroMask", rim.Mask);
            m.SetFloat("_RimEnviroChannel", MaskChannel(rim.Mask));
        }

        // The backlights, as the Poiyomi parser reads them (AtlasPoiyomiEdgeLight): the first,
        // and Poiyomi 11's second with the same properties + "__1".
        private static void WriteBacklight(AtlasUnifiedMaterial output, Material m)
        {
            var lights = AtlasBacklightFeature.Layers(output);
            string[] suffixes = { "", "__1" };
            for (int k = 0; k < suffixes.Length; k++)
            {
                string s = suffixes[k];
                var light = k < lights.Count ? lights[k] : null;
                m.SetFloat("_BacklightEnabled" + s, light != null ? 1 : 0);
                if (light != null) m.EnableKeyword("POI_BACKLIGHT" + s);
                else m.DisableKeyword("POI_BACKLIGHT" + s);
                if (light == null) continue;
                m.SetFloat("_BacklightBorder" + s, (light.Low + light.High) * 0.5f);
                m.SetFloat("_BacklightBlur" + s, light.High - light.Low);
                m.SetFloat("_BacklightDirectivity" + s, light.Directivity);
                m.SetFloat("_BacklightViewStrength" + s, light.ViewStrength);
                m.SetFloat("_BacklightReceiveShadow" + s, light.ReceiveShadows ? 1 : 0);
                m.SetFloat("_BacklightNormalStrength" + s, light.SurfaceNormalAmount);
                m.SetFloat("_BacklightMainStrength" + s, light.BaseColorMix);
                m.SetFloat("_BacklightBackfaceMask" + s, light.Faces == AtlasFaces.Front ? 1 : 0);
                m.SetFloat("_BacklightApplyToOutlines" + s, light.OnOutlines ? 1 : 0);
                m.SetColor("_BacklightColor" + s, light.Color.Color.gamma);
                m.SetTexture("_BacklightColorTex" + s, light.Color.Map?.Texture);
                m.SetTextureScale("_BacklightColorTex" + s, Vector2.one);
                m.SetTextureOffset("_BacklightColorTex" + s, Vector2.zero);
                m.SetFloat("_BacklightColorTexUV" + s, 0);
                m.SetFloat("_BacklightGlobalMask" + s, 0);
                m.SetFloat("_BacklightApplyGlobalMaskIndex" + s, 0);
            }
        }

        // A texture slot set from a reference: its texture, tiling and offset, and its UV channel
        // (UV0 to UV3); no texture without one.
        private static void TextureOn(Material m, string property, AtlasTextureReference map)
        {
            m.SetTexture(property, map?.Texture);
            m.SetTextureScale(property, map?.Tiling ?? Vector2.one);
            m.SetTextureOffset(property, map?.Offset ?? Vector2.zero);
            int uv = map == null ? 0 : (int)(map.Space - AtlasTextureSpace.UV0);
            m.SetFloat(property + "UV", uv >= 0 && uv <= 3 ? uv : 0);
        }

        // A mask in one channel of a packed texture slot: the channel's strength set, the others 0.
        // Without a mask: no texture, the default strengths (R at 1).
        private static void PackedMask(Material m, string property, string strengths, AtlasMask mask)
        {
            var texture = mask?.Map?.Texture;
            m.SetTexture(property, texture);
            m.SetTextureScale(property, Vector2.one);
            m.SetTextureOffset(property, Vector2.zero);
            m.SetFloat(property + "UV", 0);
            int channel = texture == null ? 0 : Mathf.Clamp((int)mask.Channel, 0, 3);
            for (int c = 0; c < 4; c++)
                m.SetFloat(strengths + "RGBA"[c], c == channel ? (texture == null ? 1f : Mathf.Clamp01(mask.Strength)) : 0f);
        }

        // Shade Map's reading: two steps, both sides lit by the direct light, the shades occluded.
        private static bool IsShadeMap(AtlasRampToon ramp) =>
            ramp.Bands.Count == 2 && ramp.DirectOccluded && Mathf.Approximately(ramp.ShadowDirect, 1f) && ramp.ShadowAmbient <= 0f && !ramp.RampScaledShadowDirect;

        // The lighting type and its settings.
        private static int LightingType(AtlasLighting l, Material m)
        {
            var ramp = l.Ramp;
            switch (l.Family)
            {
                case AtlasLightingFamily.Unlit:
                    return 5;
                case AtlasLightingFamily.Flat:
                    m.SetFloat("_ForceFlatRampedLightmap", l.FlatLightMap ? 1 : 0);
                    return 5;
                case AtlasLightingFamily.PhysicallyBased:
                    if (l.Diffuse == AtlasDiffuseModel.PreintegratedSkin)
                    {
                        var skin = l.Skin;
                        m.SetColor("_LightingShadowColor", l.ShadowTint.gamma);
                        m.SetTexture("_SkinLUT", skin.Lut?.Texture);
                        m.SetFloat("_SssScale", skin.Scattering);
                        m.SetTexture("_SkinThicknessMap", skin.Thickness?.Map?.Texture);
                        m.SetTextureScale("_SkinThicknessMap", Vector2.one);
                        m.SetTextureOffset("_SkinThicknessMap", Vector2.zero);
                        m.SetFloat("_SkinThicknessMapUV", 0);
                        m.SetFloat("_SkinThicknessMapInvert", 0);
                        m.SetFloat("_SkinThicknessPower", skin.ThicknessPower);
                        m.SetVector("_SssTransmissionAbsorption", skin.Absorption);
                        m.SetFloat("_SkinScatteringStability", skin.Stability);
                        m.SetFloat("_SkinDiffuseSoftnessEnabled", skin.NormalSoftness > 0f ? 1 : 0);
                        m.SetFloat("_SssBumpBlur", skin.NormalSoftness > 0f ? skin.NormalSoftness : 0.7f);
                        m.SetFloat("_LightingIgnoreAmbientColor", skin.ConstantShadow);
                        m.SetFloat("_ShadowStrength", skin.Strength);
                        return 3;
                    }
                    // Realistic: Lambert, the light × lerp(1, light map, strength), tinted in shadow.
                    m.SetFloat("_ShadowStrength", l.ShadowStrength);
                    m.SetColor("_LightingShadowColor", l.ShadowTint.gamma);
                    return 6;
            }
            if (ramp.Term == AtlasShadingTerm.SdfFace && ramp.Sdf?.Style == AtlasSdfStyle.Threshold)
            {
                // SDF: the face's texture by threshold, mirrored by the light's side; the shadow
                // as most types share it.
                TextureOn(m, "_SDFShadingTexture", ramp.Sdf.Map);
                m.SetVector("_SDFForward", ramp.Sdf.Forward);
                m.SetVector("_SDFLeft", ramp.Sdf.Left);
                m.SetFloat("_SDFBlur", ramp.Sdf.Blur);
                m.SetColor("_LightingShadowColor", ramp.ShadowTint.Color.gamma);
                m.SetFloat("_LightingIgnoreAmbientColor", ramp.ShadowDirect);
                m.SetFloat("_ShadowStrength", ramp.RampStrength);
                return 8;
            }
            if (ramp.Term == AtlasShadingTerm.Wrapped)
            {
                m.SetFloat("_LightingWrappedWrap", ramp.Wrap);
                m.SetFloat("_LightingWrappedNormalization", ramp.WrapNormalization);
                m.SetColor("_LightingShadowColor", ramp.ShadowTint.Color.gamma);
                m.SetColor("_LightingWrappedColor", ramp.LitTint.gamma);
                m.SetFloat("_ShadowStrength", ramp.RampStrength);
                // The keys from black to white: r = saturate((t − (1 − end)) / (end − start + 0.001)).
                float low = 0f, high = 1f;
                foreach (var key in ramp.Keys)
                {
                    if (key.Color.r <= 0f) low = Mathf.Max(low, key.Position);
                    if (key.Color.r >= 1f) high = Mathf.Min(high, key.Position);
                }
                float end = 1f - low;
                m.SetFloat("_LightingGradientEnd", end);
                m.SetFloat("_LightingGradientStart", end - (high - low) + 0.001f);
                return 2;
            }
            float ignore = ramp.ShadowDirect;
            if (ramp.Bands.Count > 0)
            {
                m.SetFloat("_ShadowStrength", ramp.RampStrength);
                // Shade Map: two steps, both sides lit by the direct light, the shades occluded.
                if (IsShadeMap(ramp))
                {
                    var first = ramp.Bands[0];
                    var second = ramp.Bands[1];
                    m.SetColor("_1st_ShadeColor", first.Color.Color.gamma);
                    m.SetFloat("_BaseShade_Feather", first.Blur);
                    m.SetFloat("_BaseColor_Step", first.Border + first.Blur * 0.5f);
                    m.SetFloat("_Use_BaseAs1st", first.ColorFromBase ? 1 : 0);
                    TextureOn(m, "_1st_ShadeMap", first.Color.Map);
                    m.SetColor("_2nd_ShadeColor", second.Color.Color.gamma);
                    m.SetFloat("_1st2nd_Shades_Feather", second.Blur);
                    m.SetFloat("_ShadeColor_Step", second.Border + second.Blur * 0.5f);
                    m.SetFloat("_Use_1stAs2nd", 0);
                    TextureOn(m, "_2nd_ShadeMap", second.Color.Map);
                    m.SetFloat("_ShadingShadeMapBlendType", ramp.BandsReplaceBase ? 0 : 1);
                    return 4;
                }
                // Multilayer Math: up to three bands.
                string[] prefixes = { "_Shadow", "_Shadow2nd", "_Shadow3rd" };
                for (int k = 0; k < prefixes.Length; k++)
                {
                    var band = k < ramp.Bands.Count ? ramp.Bands[k] : null;
                    var color = band != null ? band.Color.Color : Color.clear;
                    m.SetColor(prefixes[k] + "Color", new Color(color.r, color.g, color.b, band != null ? band.Alpha : 0f).gamma);
                    TextureOn(m, prefixes[k] + "ColorTex", band?.Color.Map);
                    if (band == null) continue;
                    m.SetFloat(prefixes[k] + "ColorTexAddition", band.ColorAddition);
                    m.SetFloat(prefixes[k] + "Border", band.Border);
                    m.SetFloat(prefixes[k] + "Blur", band.Blur);
                    m.SetFloat(prefixes[k] + "NormalStrength", band.NormalStrength);
                    m.SetFloat(prefixes[k] + "Receive", band.ReceiveShadows);
                }
                m.SetFloat("_LightingMulitlayerNonLinear", ramp.SmoothEdges ? 1 : 0);
                m.SetColor("_ShadowBorderColor", new Color(ramp.BorderColor.r, ramp.BorderColor.g, ramp.BorderColor.b, 1f).gamma);
                m.SetFloat("_ShadowBorderRange", ramp.BorderRange);
                m.SetFloat("_ShadowEnvStrength", ramp.EnvironmentStrength);
                m.SetFloat("_ShadowMainStrength", ramp.ShadowColorFromBase);
                m.SetFloat("_LightingIgnoreAmbientColor", ignore);
                m.SetFloat("_ShadowMaskType", 0);
                m.SetTexture("_ShadowStrengthMask", null);
                return 1;
            }
            // Texture Ramp: the ramp by the light map, Shadow Tint × lerp(ambient, r × direct, ignore).
            m.SetTexture("_ToonRamp", ramp.Ramp?.Texture);
            m.SetFloat("_ShadowOffset", ramp.Offset);
            m.SetFloat("_ShadowStrength", ramp.RampStrength);
            m.SetColor("_LightingShadowColor", ramp.ShadowTint.Color.gamma);
            m.SetFloat("_LightingIgnoreAmbientColor", ignore);
            return 0;
        }

        // UV Tile Discard (vertex mode: a triangle with a vertex in a hidden tile is hidden; never
        // outside the 4 × 4 grid) for both sides, and Face Discard for one side, which needs the
        // first one's keyword.
        // World AO Blocker: depth only for triangles in the tile left of the default one, on its
        // UV channel.
        private static void WriteAOBlocker(AtlasAOBlocker blocker, Material material)
        {
            material.SetFloat("_RenderingAOBlockerEnabled", blocker != null ? 1 : 0);
            material.SetFloat("_RenderingAOBlockerUVChannel", blocker != null ? Mathf.Clamp((int)(blocker.Space - AtlasTextureSpace.UV0), 0, 3) : 0);
            material.SetFloat("_RenderingAOBlockerFlipNormal", blocker != null && blocker.FlipNormal ? 1 : 0);
        }

        private static void WriteTileDiscard(List<AtlasTileDiscard> hidden, Material material)
        {
            var both = hidden.FirstOrDefault(t => t.Faces == AtlasFaces.Both);
            var face = hidden.FirstOrDefault(t => t.Faces != AtlasFaces.Both);
            bool on = both != null || face != null;
            material.SetFloat("_EnableUDIMDiscardOptions", on ? 1 : 0);
            if (on) material.EnableKeyword("POI_UDIMDISCARD");
            else material.DisableKeyword("POI_UDIMDISCARD");
            material.SetFloat("_UDIMDiscardUV", on ? Mathf.Clamp((int)((both ?? face).Space - AtlasTextureSpace.UV0), 0, 3) : 0);
            material.SetFloat("_UDIMDiscardMode", 0);
            Rows(material, "_UDIMDiscardRow", both?.Tiles ?? 0);
            material.SetFloat("_EnableUDIMFaceDiscardOptions", face != null ? 1 : 0);
            if (face == null) return;
            material.SetFloat("_UDIMFaceDiscardUV", Mathf.Clamp((int)(face.Space - AtlasTextureSpace.UV0), 0, 3));
            // 0 discards back faces, 1 front faces.
            material.SetFloat("_UDIMFaceDiscardFace", face.Faces == AtlasFaces.Front ? 1 : 0);
            Rows(material, "_UDIMFaceDiscardRow", face.Tiles);
        }

        // Bit row × 4 + column into _…Row{row}_{column}.
        private static void Rows(Material material, string prefix, int tiles)
        {
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    material.SetFloat(prefix + row + "_" + column, ((tiles >> (row * 4 + column)) & 1) != 0 ? 1 : 0);
        }

        private static void WriteSurfaceResponse(AtlasSurfaceResponse response, Material material)
        {
            var packed = response.Metallic.Map?.Texture;
            material.SetFloat("_MochieBRDF", packed != null ? 1 : 0);
            if (packed != null)
            {
                material.EnableKeyword("MOCHIE_PBR");
                material.SetTexture("_MochieMetallicMaps", packed);
                material.SetTextureScale("_MochieMetallicMaps", Vector2.one);
                material.SetTextureOffset("_MochieMetallicMaps", Vector2.zero);
                material.SetFloat("_MochieMetallicMapsUV", 0);
                material.SetFloat("_MochieMetallicMultiplier", 1);
                material.SetFloat("_MochieRoughnessMultiplier", 1);
                material.SetFloat("_MochieMetallicMapsMetallicChannel", 0);
                material.SetFloat("_MochieMetallicMapsRoughnessChannel", 1);
                material.SetFloat("_MochieMetallicMapsReflectionMaskChannel", response.Reflections.Mask != null ? 2 : 4);
                // A highlight mask that reads the reflection mask's map shares its channel.
                material.SetFloat("_MochieMetallicMapsSpecularMaskChannel", response.Highlights.Mask == null ? 4
                    : response.Reflections.Mask != null && response.Highlights.Mask.Map == response.Reflections.Mask.Map ? 2 : 3);
                material.SetFloat("_MochieMetallicMapInvert", 0);
                material.SetFloat("_MochieRoughnessMapInvert", 0);
                material.SetFloat("_MochieReflectionMaskInvert", 0);
                material.SetFloat("_MochieSpecularMaskInvert", 0);
                material.SetFloat("_MochieReflectionStrength", response.Reflections.Enabled ? Mathf.Clamp01(response.Reflections.Strength) : 0f);
                material.SetFloat("_MochieSpecularStrength", response.Highlights.Enabled ? Mathf.Clamp(response.Highlights.Strength / HighlightScale(material), 0f, 5f) : 0f);
            }
            else material.DisableKeyword("MOCHIE_PBR");

            var occlusion = response.Occlusion.Map?.Texture;
            material.SetTexture("_LightingAOMaps", occlusion);
            if (occlusion != null)
            {
                material.EnableKeyword("PROP_LIGHTINGAOMAPS");
                material.SetTextureScale("_LightingAOMaps", Vector2.one);
                material.SetTextureOffset("_LightingAOMaps", Vector2.zero);
                material.SetFloat("_LightingAOMapsUV", 0);
                material.SetFloat("_LightDataAOStrengthR", Mathf.Clamp01(response.Occlusion.Constant));
                material.SetFloat("_LightDataAOStrengthG", 0);
                material.SetFloat("_LightDataAOStrengthB", 0);
                material.SetFloat("_LightDataAOStrengthA", 0);
            }
            else material.DisableKeyword("PROP_LIGHTINGAOMAPS");
        }

        // VRChat's fallback tag, as Thry's editor writes it: a shader type, the render type
        // (none for opaque) and DoubleSided when nothing is culled; or Hidden. A fade that writes
        // depth falls back to cutout, which keeps its solid parts solid.
        public static string Fallback(AtlasRender render)
        {
            if (render.Fallback == AtlasFallbackShader.Hidden) return "Hidden";
            string type = render.Fallback == AtlasFallbackShader.Unlit ? "Unlit"
                : render.Fallback == AtlasFallbackShader.Standard ? "Standard" : "Toon";
            string mode = render.SurfaceMode == AtlasSurfaceMode.Cutout || render.SurfaceMode == AtlasSurfaceMode.Dithered ? "Cutout"
                : render.SurfaceMode == AtlasSurfaceMode.Fade ? (render.Pass.DepthWrite ? "Cutout" : "Fade")
                : render.SurfaceMode == AtlasSurfaceMode.Transparent ? "Transparent" : "";
            return type + mode + (render.Pass.Cull == AtlasCull.Off ? "DoubleSided" : "");
        }
    }

    public sealed class PoiyomiToon10OutputShader : PoiyomiOutputShader
    {
        public PoiyomiToon10OutputShader() : base(10) { }
        public override string ShaderId => "poiyomi10";
        public override string DisplayName => "Poiyomi Toon 10";
    }

    // Toon 11 adds global mask contacts kept live, a rim's application stage and a second
    // backlight.
    public sealed class PoiyomiToon11OutputShader : PoiyomiOutputShader
    {
        public PoiyomiToon11OutputShader() : base(11) { }
        public override string ShaderId => "poiyomi11";
        public override string DisplayName => "Poiyomi Toon 11";
    }

    // Pro has Toon's sections and its own: GT7 tone mapping, screen-space ambient occlusion and
    // contact shadows.
    public sealed class PoiyomiPro10OutputShader : PoiyomiOutputShader
    {
        public PoiyomiPro10OutputShader() : base(10, AtlasPoiyomiVariant.Pro) { }
        public override string ShaderId => "poiyomipro10";
        public override string DisplayName => "Poiyomi Pro 10";
    }

    // Pro 11 adds what Toon 11 does, and the neutral surface emission.
    public sealed class PoiyomiPro11OutputShader : PoiyomiOutputShader
    {
        public PoiyomiPro11OutputShader() : base(11, AtlasPoiyomiVariant.Pro) { }
        public override string ShaderId => "poiyomipro11";
        public override string DisplayName => "Poiyomi Pro 11";
    }

    public sealed class VRChatMobileToonOutputShader : IAtlasOutputShader
    {
        public string ShaderId => "mobiletoon";
        public string DisplayName => "VRChat Mobile Toon Standard";
        public string Family => AtlasShaderFamilies.VRChat;
        public int Rank => 10;
        public Shader FindShader() => Shader.Find("VRChat/Mobile/Toon Standard");
        public AtlasOutputCapabilities Capabilities { get; } = new AtlasOutputCapabilities
        {
            Textures =
            {
                new AtlasOutputTexture("BaseColor", "Base color") { SizeMode = AtlasTextureSizeMode.Main }
                    .With(AtlasVirtualMap.BaseColor, AtlasTextureChannels.RGB),
                PoiyomiOutputShader.NormalTexture(),
                // One texture for _MetallicMap (R), _GlossMap (G, smoothness) and _OcclusionMap (B).
                new AtlasOutputTexture("Surface", "Metallic, gloss and occlusion") { SizeMode = AtlasTextureSizeMode.MainAspect, Srgb = false }
                    .With(AtlasVirtualMap.Metallic, AtlasTextureChannels.R, AtlasOutputTexture.Gray(0f))
                    .With(AtlasVirtualMap.Roughness, AtlasTextureChannels.G, AtlasOutputTexture.Gray(0f), invert: true)
                    .With(AtlasVirtualMap.Occlusion, AtlasTextureChannels.B, AtlasOutputTexture.Gray(1f)),
                PoiyomiOutputShader.EmissionTexture(),
                PoiyomiOutputShader.MaskTexture(1, AtlasVirtualMap.Mask1, AtlasVirtualMap.Mask2, AtlasVirtualMap.Mask3, AtlasVirtualMap.Mask4),
                // _AudioLinkMask: the AudioLink emission's color per part, black without.
                new AtlasOutputTexture("AudioLink", "AudioLink color") { SizeMode = AtlasTextureSizeMode.MainAspect, MainDivisor = 2 }
                    .With(AtlasVirtualMap.AudioLinkColor, AtlasTextureChannels.RGB, Color.black)
            },
            Reflectance = AtlasReflectanceSupport.FollowsSmoothness,
            // _VertexColor: the base color × the vertex RGB as stored.
            VertexColors = AtlasVertexColorSupport.Color,
            // _Ramp is its only shading control.
            GeneratedRamp = true,
            // One matcap, added or multiplied; its mask picks a channel.
            MatcapSlots = 1,
            MatcapBlends = AtlasMatcapBlends.Add | AtlasMatcapBlends.Multiply,
            // One rim without a mask, which can follow the scene's light.
            RimSlots = 1,
            RimFollowsSceneLight = true,
            // Detail albedo by mode and detail normal on UV0 or UV1, one mask by channel.
            DetailUVChannels = 1,
            DetailTextureBlends = { AtlasBlend.Replace, AtlasBlend.Add, AtlasBlend.Multiply, AtlasBlend.MultiplyDouble },
            DetailNormal = true,
            DetailMaskByChannel = true,
            // One AudioLink emission (USE_AUDIOLINK) with a mask texture.
        };

        // AudioLink (USE_AUDIOLINK), as the Toon Standard parser reads it: one emission, its color
        // and mask in _AudioLinkMask (RGB, not masked by the emission), the tint white × the
        // intensity, the band, mode (pulse, scroll, bar) and blend.
        private static readonly AtlasBlend[] AudioBlends = { AtlasBlend.Replace, AtlasBlend.Add, AtlasBlend.Multiply };

        private static void WriteAudioLink(AtlasUnifiedMaterial output, Material material)
        {
            var layer = AtlasAudioLinkFeature.Of(output);
            if (layer == null)
            {
                material.DisableKeyword("USE_AUDIOLINK");
                return;
            }
            material.EnableKeyword("USE_AUDIOLINK");
            var modulator = AtlasAudio.Of(output, layer).First();
            var audio = modulator.Audio;
            material.SetFloat("_AudioLinkMode", audio.Spread == AtlasAudioSpread.History ? 1 : audio.Spread == AtlasAudioSpread.Fill ? 2 : 0);
            material.SetFloat("_ALBlendMode", Mathf.Max(0, Array.IndexOf(AudioBlends, layer.Targets[0].Blend)));
            material.SetTexture("_AudioLinkMask", layer.Source.Map?.Texture);
            material.SetFloat("_ALMaskUVChannel", 0);
            material.SetFloat("_ALMaskByEmission", 0);
            material.SetFloat("_ALMaskChannel", 0);
            // The level: above 1 in the intensity (up to 12), below 1 in the tint, since an
            // intensity below 1 also lowers the tint's alpha and would count twice.
            float level = Mathf.Max(0f, layer.Source.Color.maxColorComponent);
            material.SetColor("_ALTint", level < 1f ? new Color(level, level, level, 1f).gamma : Color.white);
            material.SetFloat("_ALIntensity", Mathf.Clamp(Mathf.Max(level, 1f), 1f, 12f));
            material.SetFloat("_ALBand", Mathf.Clamp((int)audio.Band, 0, 3));
            material.SetFloat("_ALSmoothing", Mathf.Clamp(Mathf.Round(audio.Smoothing * 15f), 0f, 15f));
            material.SetFloat("_ALEnableFallback", audio.FallbackBeat ? 1 : 0);
            material.SetFloat("_ALFallbackSpeed", audio.FallbackSpeed);
            material.SetFloat("_ALEffectUVChannel", 0);
            material.SetFloat("_ALEffectUseMask", 0);
            material.SetFloat("_ALRimMaskStrength", 0);
            bool centerOut = audio.Coordinate == AtlasSpreadCoordinate.ViewAngle;
            material.SetFloat("_ALScrollCenterOut", centerOut ? 1 : 0);
            material.SetFloat("_ALScrollScale", audio.HistoryLength / (centerOut ? 64f : 128f));
            material.SetFloat("_ALBarSmoothing", audio.FillSoftness);
        }

        // The detail maps kept live (USE_DETAIL_MAPS) on UV1: the albedo by mode, the normal at its
        // scale (normal maps on), one mask by channel for both.
        private static readonly AtlasBlend[] DetailModes = { AtlasBlend.Replace, AtlasBlend.Add, AtlasBlend.Multiply, AtlasBlend.MultiplyDouble };

        private static void WriteDetails(AtlasUnifiedMaterial output, Material material)
        {
            var texture = AtlasDetailFeatures.Of(output, AtlasSurfaceTarget.BaseColor);
            var normal = AtlasDetailFeatures.Of(output, AtlasSurfaceTarget.Normal);
            bool on = texture != null || normal != null;
            if (on) material.EnableKeyword("USE_DETAIL_MAPS");
            else material.DisableKeyword("USE_DETAIL_MAPS");
            if (!on) return;
            material.SetFloat("_DetailUV", AtlasDetailFeatures.UV(texture ?? normal) >= 1 ? 1 : 0);
            var mask = texture != null ? texture.Mask : normal.Mask;
            material.SetTexture("_DetailMask", mask?.Map?.Texture);
            material.SetFloat("_DetailMaskChannel", mask?.Map?.Texture != null ? Mathf.Clamp((int)mask.Channel, 0, 3) : 3);
            material.SetTexture("_DetailAlbedoMap", texture?.Source.Map.Texture);
            if (texture != null) material.SetFloat("_DetailMode", Mathf.Max(0, Array.IndexOf(DetailModes, texture.Targets[0].Blend)));
            material.SetTexture("_DetailNormalMap", normal?.Source.Map.Texture);
            if (normal != null)
            {
                material.SetFloat("_DetailNormalMapScale", normal.Targets[0].Opacity);
                material.EnableKeyword("USE_NORMAL_MAPS");
            }
        }

        // Its one rim: a band, added after lighting (× the scene's light when Environmental). A
        // rim of another shape is fitted to the band (AtlasRimCurve.FitBand); its strength and
        // a color above 1 go into the intensity.
        private static void WriteRim(AtlasUnifiedMaterial output, Material material)
        {
            var rim = output.Layers.OfType<AtlasAngleLayer>()
                .FirstOrDefault(l => l.Source == AtlasAngleSource.ViewAngle && l.ColorSource == AtlasAngleColorSource.Color);
            float range = 0f, softness = 0f, peak = 0f;
            bool on = rim != null && AtlasRimCurve.FitBand(rim, out range, out softness, out peak);
            if (on) material.EnableKeyword("USE_RIMLIGHT");
            else material.DisableKeyword("USE_RIMLIGHT");
            if (!on) return;
            var color = rim.Color.Color;
            float brightness = Mathf.Max(1f, color.maxColorComponent);
            material.SetColor("_RimColor", new Color(color.r / brightness, color.g / brightness, color.b / brightness, 1f).gamma);
            material.SetFloat("_RimIntensity", Mathf.Clamp01(rim.Opacity * peak * brightness * color.a));
            material.SetFloat("_RimRange", range);
            material.SetFloat("_RimSharpness", softness);
            material.SetFloat("_RimAlbedoTint", rim.BaseColorMix);
            material.SetFloat("_RimEnvironmental", rim.LightResponse == AtlasLightResponse.IgnoreLight ? 0 : 1);
        }

        // Lighting: Toon Standard's one model, base × (lerp(base × Shadow Tint, 1, min(1, ramp(N·L
        // × 0.5 + 0.5) + Shadow Boost)) × direct + ambient). The ramp is the source's texture, the
        // one generated from keys, or the SDK ramp nearest to its family.
        private static readonly (AtlasRampPreset Preset, string Texture)[] RampTextures =
        {
            (AtlasRampPreset.Flat, "ShadowRampFlat"), (AtlasRampPreset.Realistic, "ShadowRampRealistic"),
            (AtlasRampPreset.RealisticSoft, "ShadowRampRealisticSoft"), (AtlasRampPreset.RealisticVerySoft, "ShadowRampRealisticVerySoft"),
            (AtlasRampPreset.TwoBands, "ShadowRampToon2Band"), (AtlasRampPreset.ThreeBands, "ShadowRampToon3Band"),
            (AtlasRampPreset.FourBands, "ShadowRampToon4Band")
        };

        private static void WriteLighting(AtlasLighting l, Material m)
        {
            var ramp = l.Ramp;
            var texture = ramp.Ramp?.Texture;
            // A ramp described by keys is generated by the build, which sets it as the texture.
            if (texture == null && ramp.Keys.Count == 0)
            {
                var preset = ramp.Preset != AtlasRampPreset.None ? ramp.Preset
                    : l.Family == AtlasLightingFamily.PhysicallyBased ? AtlasRampPreset.Realistic
                    : l.Family == AtlasLightingFamily.RampToon ? AtlasRampPreset.None : AtlasRampPreset.Flat;
                texture = PresetTexture(preset);
            }
            m.SetTexture("_Ramp", texture);
            m.SetFloat("_ShadowBoost", Mathf.Clamp01(ramp.ShadowLift));
            // Banded shading keeps its shadow color in the first band.
            var tint = ramp.Bands.Count > 0 ? ramp.Bands[0].Color.Color : ramp.ShadowTint.Color;
            m.SetFloat("_ShadowAlbedo", Mathf.Clamp01(tint.r * 0.2126f + tint.g * 0.7152f + tint.b * 0.0722f));
            // Its one floor (base × it, added like emission): the unified floors' largest.
            m.SetFloat("_MinBrightness", Mathf.Clamp(AtlasLightingFeature.Floor(l), 0f, 0.1f));
            m.SetFloat("_LimitBrightness", l.LimitBrightness ? 1 : 0);
        }

        // The VRChat SDK's ramp texture for a preset, found once; null: none (white, fully lit).
        private static readonly Dictionary<AtlasRampPreset, Texture2D> PresetTextures = new Dictionary<AtlasRampPreset, Texture2D>();

        private static Texture PresetTexture(AtlasRampPreset preset)
        {
            if (PresetTextures.TryGetValue(preset, out var found) && found != null) return found;
            foreach (var (candidate, name) in RampTextures)
            {
                if (candidate != preset) continue;
                foreach (string guid in UnityEditor.AssetDatabase.FindAssets(name + " t:Texture2D"))
                {
                    var texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (texture != null && texture.name == name) return PresetTextures[preset] = texture;
                }
            }
            return null;
        }

        // Toon Standard: _Color (sRGB-authored) times _MainTex; the normal map, behind its
        // USE_NORMAL_MAPS keyword. It's opaque only, and a VRChat shader, so it needs no fallback.
        public void Write(AtlasUnifiedMaterial output, Material material)
        {
            var baseColor = output.Surface.BaseColor;
            material.SetColor("_Color", baseColor.Color.gamma);
            material.SetTexture("_MainTex", baseColor.Map?.Texture);
            material.SetTextureScale("_MainTex", Vector2.one);
            material.SetTextureOffset("_MainTex", Vector2.zero);
            material.SetFloat("_VertexColor", output.Surface.VertexColor.MultiplyColor ? 1 : 0);
            WriteLighting(output.Lighting, material);
            // Its one matcap: added or multiplied into the albedo, a mask by channel.
            var matcap = output.Layers.OfType<AtlasLookupLayer>().FirstOrDefault();
            if (matcap != null) material.EnableKeyword("USE_MATCAP");
            else material.DisableKeyword("USE_MATCAP");
            material.SetTexture("_Matcap", matcap?.Source.Map?.Texture);
            material.SetTexture("_MatcapMask", matcap?.Mask?.Map?.Texture);
            material.SetFloat("_MatcapMaskChannel", matcap?.Mask?.Map?.Texture != null ? Mathf.Clamp((int)matcap.Mask.Channel, 0, 3) : 0);
            material.SetFloat("_MatcapType", matcap != null && matcap.Blend == AtlasBlend.Multiply ? 1 : 0);
            material.SetFloat("_MatcapStrength", matcap != null ? Mathf.Clamp01(matcap.Opacity) : 1f);
            WriteRim(output, material);
            var normal = output.Surface.Normal;
            if (normal.Map?.Texture != null)
            {
                material.EnableKeyword("USE_NORMAL_MAPS");
                material.SetTexture("_BumpMap", normal.Map.Texture);
                material.SetFloat("_BumpScale", normal.Strength);
                material.SetTextureScale("_BumpMap", Vector2.one);
                material.SetTextureOffset("_BumpMap", Vector2.zero);
            }
            else material.DisableKeyword("USE_NORMAL_MAPS");
            // After the normal map: a live detail normal turns normal maps on.
            WriteDetails(output, material);
            WriteAudioLink(output, material);

            // USE_SPECULAR: metallic = _MetallicMap's channel × strength, smoothness = _GlossMap's ×
            // strength, reflectance = gloss × _Reflectance. USE_OCCLUSION_MAP: lerp(1, channel, strength).
            var response = output.Surface.Response;
            var surface = response.Metallic.Map?.Texture;
            if (surface != null)
            {
                material.EnableKeyword("USE_SPECULAR");
                material.SetTexture("_MetallicMap", surface);
                material.SetFloat("_MetallicMapChannel", 0);
                material.SetFloat("_MetallicStrength", 1);
                material.SetTexture("_GlossMap", response.Roughness.Map?.Texture ?? surface);
                material.SetFloat("_GlossMapChannel", 1);
                material.SetFloat("_GlossStrength", 1);
                material.SetFloat("_Reflectance", Mathf.Clamp01(response.Reflectance.Constant));
            }
            else material.DisableKeyword("USE_SPECULAR");
            var occlusion = response.Occlusion.Map?.Texture;
            if (occlusion != null)
            {
                material.EnableKeyword("USE_OCCLUSION_MAP");
                material.SetTexture("_OcclusionMap", occlusion);
                material.SetFloat("_OcclusionMapChannel", 2);
                material.SetFloat("_OcclusionStrength", Mathf.Clamp01(response.Occlusion.Constant));
            }
            else material.DisableKeyword("USE_OCCLUSION_MAP");

            // Emission: _EmissionColor (set from the linear color's gamma value) × _EmissionStrength
            // (0–2; above that the rest goes into the color) × _EmissionMap on UV0.
            var emission = AtlasEmissionFeature.Of(output);
            var emissionMap = emission?.Source.Map?.Texture;
            material.SetTexture("_EmissionMap", emissionMap);
            if (emissionMap != null)
            {
                float total = emission.Targets.First(t => t.Target == AtlasSurfaceTarget.Emission).Opacity;
                float strength = Mathf.Min(total, 2f);
                float rest = total / Mathf.Max(strength, 1e-4f);
                material.SetColor("_EmissionColor", new Color(rest, rest, rest, 1f).gamma);
                material.SetFloat("_EmissionStrength", strength);
                material.SetFloat("_EmissionUV", 0);
                material.SetTextureScale("_EmissionMap", Vector2.one);
                material.SetTextureOffset("_EmissionMap", Vector2.zero);
            }
            else material.SetColor("_EmissionColor", Color.black);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }
    }
}
