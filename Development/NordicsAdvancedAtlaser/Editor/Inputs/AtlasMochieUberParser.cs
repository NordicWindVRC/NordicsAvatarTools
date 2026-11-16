using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Mochie Uber and Uber (Outline), Mochie's avatar shader (v1.35.2). Sections follow their
    /// keywords, as the shader does. See UNIFIED_MATERIAL_MOCHIE_UBER.md.
    /// </summary>
    public sealed class AtlasMochieUberParser : AtlasParserBase
    {
        public override string ParserId => "mochie-uber";
        public override string DisplayName => "Mochie Uber";
        public override bool CanParse(Material m)
        {
            if (Name(m) == "Mochie/Uber" || Name(m) == "Mochie/Uber (Outline)") return true;
            // Without the shader: Uber's own combination of controls.
            return AtlasMaterialReader.Saved(m) != null && Has(m, "_MirrorBehavior") && Has(m, "_CubeMode") &&
                   Has(m, "_PBRWorkflow") && Has(m, "_RenderMode") && Has(m, "_ColorPreservation");
        }

        private static readonly AtlasChannel[] Channels = { AtlasChannel.Red, AtlasChannel.Green, AtlasChannel.Blue, AtlasChannel.Alpha };

        // Feature masks, in packed order: four textures, a channel each.
        private enum UberMask
        {
            Reflection, Specular, PrimaryMatcap, SecondaryMatcap,
            Shadow, Diffuse, Subsurface, Detail,
            Rim, EnvironmentRim, Refraction, SpecularBlend,
            Emission, EmissionPulse, Filter, OutlineThickness
        }
        private static readonly string[] SeparateMasks = {
            "_ReflectionMask", "_SpecularMask", "_MatcapMask", "_MatcapBlendMask",
            "_ShadowMask", "_DiffuseMask", "_SubsurfaceMask", "_DetailMask",
            "_RimMask", "_ERimMask", "_RefractionMask", "_InterpMask",
            "_EmissMask", "_PulseMask", "_FilterMask", "_OutlineMask"
        };

        // UV distortion shared by the references it shifts, for the duration of one parse.
        [System.ThreadStatic] private static AtlasUVDistortion mainDistortion, emissionDistortion, detailDistortion,
            rimDistortion, matcapDistortion0, matcapDistortion1;

        protected override void Populate(Material m, AtlasUnifiedMaterial d)
        {
            try
            {
                Distortions(m);
                PopulateSections(m, d);
            }
            finally
            {
                mainDistortion = emissionDistortion = detailDistortion = rimDistortion = matcapDistortion0 = matcapDistortion1 = null;
            }
        }

        // UV Distortion: a normal map's xy or simplex noise, × strength × 0.1 in UV units, × the mask;
        // matcap lookups take it × 20 more.
        private static void Distortions(Material m)
        {
            if (!Keyword(m, "_UV_DISTORTION_ON")) return;
            var distortion = new AtlasUVDistortion { Strength = Float(m, "_DistortUVStr", 1f) * 0.1f };
            var mask = Main(m, "_DistortUVMask", AtlasTextureInterpretation.Data);
            if (mask != null) distortion.Mask = new AtlasMask { Map = mask };
            if (Keyword(m, "_UV_DISTORTION_NORMALMAP_ON"))
            {
                distortion.Source = AtlasDistortionSource.NormalMap;
                distortion.NormalMap = AtlasSourceMapping.Texture(m, "_DistortUVMap", AtlasTextureInterpretation.TangentNormal);
                if (distortion.NormalMap != null) distortion.NormalMap.Scroll = Vector2Of(m, "_DistortUVScroll");
            }
            else
            {
                distortion.NoiseScale = Vector2Of(m, "_NoiseScale");
                distortion.NoiseSpeed = Float(m, "_NoiseSpeed", 0.5f);
                distortion.NoiseOctaves = Int(m, "_NoiseOctaves", 1);
            }
            var matcap = new AtlasUVDistortion { Source = distortion.Source, NoiseScale = distortion.NoiseScale,
                NoiseSpeed = distortion.NoiseSpeed, NoiseOctaves = distortion.NoiseOctaves, NormalMap = distortion.NormalMap,
                Strength = distortion.Strength * 20f, Mask = distortion.Mask };
            if (Int(m, "_DistortMainUV", 0) == 1) mainDistortion = distortion;
            if (Int(m, "_DistortEmissUV", 0) == 1) emissionDistortion = distortion;
            if (Int(m, "_DistortDetailUV", 0) == 1) detailDistortion = distortion;
            if (Int(m, "_DistortRimUV", 0) == 1) rimDistortion = distortion;
            if (Int(m, "_DistortMatcap0", 0) == 1) matcapDistortion0 = matcap;
            if (Int(m, "_DistortMatcap1", 0) == 1) matcapDistortion1 = matcap;
        }

        private static void PopulateSections(Material m, AtlasUnifiedMaterial d)
        {
            bool shading = Keyword(m, "_SHADING_ON");
            Render(m, d);
            BaseColor(m, d);
            if (shading)
            {
                Surface(m, d);
                Detail(m, d);
                if (Keyword(m, "_FLIPBOOK_ON")) { Flipbook(m, d, ""); Flipbook(m, d, "1"); }
                Parallax(m, d);
                Lighting(m, d);
                Reflections(m, d);
                Highlights(m, d);
                Subsurface(m, d);
                if (Keyword(m, "_MATCAP_ON")) Matcaps(m, d);
                if (Keyword(m, "_RIM_ON")) Rim(m, d);
                if (Keyword(m, "_ENVIRONMENT_RIM_ON")) EnvironmentRim(m, d);
                if (Int(m, "_Iridescence", 0) == 1) Iridescence(m, d);
            }
            else
            {
                // Shading off: uniform light clamped to the base colour, the flat family.
                d.Lighting.Family = AtlasLightingFamily.Flat;
                d.Lighting.Ambient = AtlasAmbientModel.Average;
                d.Lighting.LimitBrightness = true;
                d.Surface.Response.Highlights.Enabled = false;
                d.Surface.Response.Reflections.Enabled = false;
            }
            if (Keyword(m, "_EMISSION_ON")) Emission(m, d);
            if (Keyword(m, "_FILTERING_ON")) Filtering(m, d);
            if (Keyword(m, "_VERTEX_MANIP_ON")) VertexManipulation(m, d);
            if (Keyword(m, "_REFRACTION_ON")) Refraction(m, d);
            if (!shading && Keyword(m, "_FLIPBOOK_ON")) { Flipbook(m, d, ""); Flipbook(m, d, "1"); }
            if (Name(m) == "Mochie/Uber (Outline)") Outline(m, d);
            // Uber and its Outline variant declare the same properties, so saved values can't tell them apart.
            else if (AtlasMaterialReader.Saved(m) != null)
                d.Report(AtlasIssueKind.Note, "shader.variant",
                    "The shader isn't installed, so Uber (Outline) can't be told from Uber; read without an outline.");
            var volumes = d.Lighting.LightVolumes;
            volumes.Enabled = Float(m, "_LightVolumesToggle", 1f) > 0.5f;
            volumes.Strength = Float(m, "_LightVolumeStrength", 1f);
            volumes.Specular = Float(m, "_LightVolumeSpecularity", 0f) > 0.5f;
            volumes.SpecularStrength = Float(m, "_LightVolumeSpecularityStrength", 1f);
            volumes.Bias = Float(m, "_LightVolumeBias", 0f);
            if (Keyword(m, "_AUDIOLINK_ON")) AudioLink(m, d);
            ReportUberX(m, d);
        }

        // Blending Mode, alpha, culling and depth.
        private static void Render(Material m, AtlasUnifiedMaterial d)
        {
            var render = d.Render;
            bool nonOpaque = false;
            if (Keyword(m, "_ALPHATEST_ON"))
            {
                // Cutout sharpens the alpha edge for alpha-to-coverage; dithered is screen-door.
                render.SurfaceMode = Int(m, "_BlendMode", 1) == 2 ? AtlasSurfaceMode.Dithered : AtlasSurfaceMode.Cutout;
                nonOpaque = true;
            }
            else if (Keyword(m, "_ALPHABLEND_ON")) { render.SurfaceMode = AtlasSurfaceMode.Fade; nonOpaque = true; }
            else if (Keyword(m, "_ALPHAPREMULTIPLY_ON")) { render.SurfaceMode = AtlasSurfaceMode.Transparent; nonOpaque = true; }
            render.Cutoff = Float(m, "_Cutoff", 0.5f);
            render.AlphaToCoverage = Int(m, "_ATM", 0) == 1;
            render.Pass.DepthWrite = Int(m, "_ZWrite", 1) == 1;
            render.Pass.DepthTest = (AtlasDepthTest)Mathf.Clamp(Int(m, "_ZTest", 4), 0, 8);
            int cull = Int(m, "_CullingMode", 2);
            render.Pass.Cull = cull == 0 ? AtlasCull.Off : cull == 1 ? AtlasCull.Front : AtlasCull.Back;
            render.TwoSidedLighting = true; // Back faces flip their normal.
            Stencil(m, render.Pass.Stencil, "_StencilPass", "_StencilCompare");
            render.Invisible = Int(m, "_Hide", 0) == 1;
            switch (Int(m, "_MirrorBehavior", 0))
            {
                case 1: render.Visibility.MirrorOnly(); break;
                case 2: render.MirrorBaseColor = Main(m, "_MirrorTex"); break;
                case 3: render.Visibility.NotInMirror(); break;
            }
            if (Int(m, "_NearClipToggle", 0) == 1)
            {
                render.NearClip.Enabled = true;
                render.NearClip.Distance = Float(m, "_NearClip", 0f);
                var clipMask = Raw(m, "_NearClipMask");
                if (clipMask != null) render.NearClip.Mask = new AtlasMask { Map = clipMask };
            }

            if (!nonOpaque) return;
            d.Surface.Alpha.Strength = Float(m, "_AlphaStrength", 1f);
            if (Int(m, "_UseAlphaMask", 0) != 1) return;
            var map = AtlasSourceMapping.Texture(m, "_AlphaMask", AtlasTextureInterpretation.Data);
            d.Surface.Alpha.Source = AtlasAlphaSource.Mask;
            d.Surface.Alpha.Mask = map == null ? null : new AtlasMask { Map = map, Channel = Channel(m, "_AlphaMaskChannel", 0) };
            d.Surface.Alpha.MaskMultiplier = AtlasMaterialReader.Color(m, "_Color", Color.white).a;
        }

        // Base colour: texture × colour on the main transform (tiling, scroll, rotation); vertex colour by amount.
        private static void BaseColor(Material m, AtlasUnifiedMaterial d)
        {
            d.Surface.BaseColor.Color = AtlasMaterialReader.ColorLinear(m, "_Color", Color.white);
            d.Surface.BaseColor.Map = Main(m, "_MainTex");
            float vertex = Float(m, "_VertexColor", 0f);
            d.Surface.VertexColor.MultiplyColor = vertex > 0f;
            d.Surface.VertexColor.MultiplyAlpha = vertex > 0f;
            d.Surface.VertexColor.Amount = vertex;

            CubeMode(m, d);
            if (Keyword(m, "_BASECOLOR_DISSOLVE_ON")) BaseColorDissolve(m, d);
        }

        // Cube mode: a cubemap along the view direction replaces or blends onto the base colour.
        private static void CubeMode(Material m, AtlasUnifiedMaterial d)
        {
            bool cubeOnly = Keyword(m, "_CUBEMAP_ON");
            if (!cubeOnly && !Keyword(m, "_CUBEMAP_COMBINED_ON")) return;
            var cube = new AtlasLookupLayer { Projection = AtlasLookupProjection.ViewDirection, Placement = AtlasPlacement.Surface };
            cube.Source.Map = AtlasSourceMapping.Texture(m, "_MainTexCube0");
            if (cube.Source.Map != null) { cube.Source.Map.Space = AtlasTextureSpace.View; cube.Source.Map.Tiling = Vector2.one; cube.Source.Map.Offset = Vector2.zero; }
            cube.Source.Color = AtlasMaterialReader.ColorLinear(m, "_CubeColor0", Color.white);
            // Uber's rotation turns each plane by the angle + 90° once any angle is set.
            var rotation = (Vector3)AtlasMaterialReader.Vector(m, "_CubeRotate0", new Vector4(180f, 0f, 0f, 0f));
            if (rotation != Vector3.zero)
            {
                bool auto = Int(m, "_AutoRotate0", 0) == 1;
                cube.Rotation = new Vector3(90f, 90f, 90f) + (auto ? Vector3.zero : rotation);
                if (auto) cube.Spin = rotation;
            }
            if (cubeOnly)
            {
                cube.Blend = AtlasBlend.Replace;
                cube.Opacity = 1f;
            }
            else
            {
                var (blend, scale) = Blend(Int(m, "_CubeBlendMode", 6));
                cube.Blend = blend;
                bool useMask = Int(m, "_IsCubeBlendMask", 0) == 1;
                var mask = useMask ? Raw(m, "_CubeBlendMask") : null;
                cube.Mask = mask == null ? null : new AtlasMask { Map = mask };
                cube.Opacity = (useMask ? 1f : Float(m, "_CubeBlend", 0f)) * scale;
            }
            d.Layers.Add(cube);
        }

        // Base Color Dissolve: a second base colour where the noise is below the amount, with a
        // glowing band just above it.
        private static void BaseColorDissolve(Material m, AtlasUnifiedMaterial d)
        {
            var layer = new AtlasTransitionLayer {
                Threshold = AtlasSourceMapping.Texture(m, "_BCNoiseTex", AtlasTextureInterpretation.Data),
                Amount = Float(m, "_BCDissolveStr", 0f),
                EdgeWidth = Float(m, "_BCRimWidth", 0.5f) * 0.035f
            };
            layer.Revealed.Color = AtlasMaterialReader.ColorLinear(m, "_BCColor", Color.white);
            layer.Revealed.Map = AtlasSourceMapping.Texture(m, "_MainTex2");
            var rim = AtlasMaterialReader.Color(m, "_BCRimCol", Color.white);
            var linear = rim.linear * rim.a;
            layer.EdgeColor = new Color(linear.r, linear.g, linear.b, 1f);
            d.Layers.Add(layer);
        }

        // Primary maps: the workflow, normal map, occlusion, and the map filters.
        private static void Surface(Material m, AtlasUnifiedMaterial d)
        {
            var response = d.Surface.Response;
            if (Keyword(m, "_NORMALMAP_ON"))
            {
                d.Surface.Normal.Map = Main(m, "_BumpMap", AtlasTextureInterpretation.TangentNormal);
                d.Surface.Normal.Strength = Float(m, "_BumpScale", 1f);
            }
            if (Keyword(m, "_PACKED_WORKFLOW_ON"))
            {
                // One packed texture, a channel per property.
                var packed = Main(m, "_PackedMap", AtlasTextureInterpretation.Data);
                response.Metallic.Constant = 1f;
                response.Metallic.Map = packed;
                response.Metallic.Channel = Channel(m, "_MetallicChannel", 0);
                response.Roughness.Constant = 1f;
                response.Roughness.Map = packed;
                response.Roughness.Channel = Channel(m, "_RoughnessChannel", 1);
                response.Occlusion.Map = packed;
                response.Occlusion.Channel = Channel(m, "_OcclusionChannel", 2);
            }
            else if (Keyword(m, "_SPECULAR_WORKFLOW_ON"))
            {
                // Specular colour; smoothness from its own map (1), the specular map's alpha (2), or the scale.
                bool useMap = Int(m, "_UseSpecMap", 0) == 1;
                var specular = useMap ? Main(m, "_SpecGlossMap") : null;
                response.SpecularColor = new AtlasColorInput {
                    Color = useMap ? Color.white : AtlasMaterialReader.ColorLinear(m, "_SpecCol", Color.white), Map = specular };
                response.Roughness.InvertResult = true;
                response.Roughness.Constant = Float(m, "_GlossMapScale", 1f);
                if (useMap && Int(m, "_PBRWorkflow", 1) == 2)
                {
                    response.Roughness.Map = Main(m, "_SpecGlossMap", AtlasTextureInterpretation.Data);
                    response.Roughness.Channel = AtlasChannel.Alpha;
                }
                else if (useMap && Int(m, "_UseSmoothMap", 0) == 1)
                    response.Roughness.Map = Main(m, "_SmoothnessMap", AtlasTextureInterpretation.Data);
                response.Occlusion.Map = Main(m, "_OcclusionMap", AtlasTextureInterpretation.Data);
                response.Occlusion.Channel = AtlasChannel.Green;
            }
            else
            {
                // Metallic: separate maps, each replacing its slider. Roughness, not smoothness.
                MapOrSlider(m, response.Metallic, "_MetallicGlossMap", "_UseMetallicMap", "_Metallic", 0f);
                MapOrSlider(m, response.Roughness, "_SpecGlossMap", "_UseSpecMap", "_Glossiness", 0.5f);
                response.Occlusion.Map = Main(m, "_OcclusionMap", AtlasTextureInterpretation.Data);
                response.Occlusion.Channel = AtlasChannel.Green;
            }
            response.Occlusion.Constant = Float(m, "_OcclusionStrength", 1f);
            // Map filters: remaps on the maps' values, and a tint for occlusion.
            bool specularWorkflow = Keyword(m, "_SPECULAR_WORKFLOW_ON");
            if (!specularWorkflow) response.Metallic.Remap = Filter(m, "_MetallicFiltering", "_Metallic");
            response.Roughness.Remap = specularWorkflow ? Filter(m, "_SmoothnessFiltering", "_Smooth") : Filter(m, "_RoughnessFiltering", "_Rough");
            response.Occlusion.Remap = Filter(m, "_AOFiltering", "_AO");
            if (response.Occlusion.Remap != null)
                response.OcclusionTint = new AtlasColorInput { Color = AtlasMaterialReader.ColorLinear(m, "_AOTint", Color.black),
                    Map = Main(m, "_AOTintTex") };
            response.OcclusionDirect = Int(m, "_DirectAO", 1);
            response.OcclusionIndirect = Int(m, "_IndirectAO", 0);
            response.OcclusionReflections = 1f;
            response.SpecularAntiAliasing.Enabled = Int(m, "_GSAA", 0) == 1;
            d.Surface.Normal.FlatShading = Int(m, "_HardenNormals", 0) == 1;
            // Uber's "clear coat": specular from the mesh normal, diffuse from the normal map.
            response.SpecularFromMeshNormal = Int(m, "_ClearCoat", 0) == 1;
        }

        private static AtlasScalarRemap Filter(Material m, string toggle, string prefix)
        {
            if (Int(m, toggle, 0) != 1) return null;
            return new AtlasScalarRemap {
                OutputMin = Float(m, prefix + "RemapMin", 0f), OutputMax = Float(m, prefix + "RemapMax", 1f),
                Contrast = Float(m, prefix + "Contrast", 1f), Intensity = Float(m, prefix + "Intensity", 0f),
                Lightness = Float(m, prefix + "Lightness", 0f) };
        }

        // Height: multi-step parallax on the main transform, from the height map or the packed map.
        private static void Parallax(Material m, AtlasUnifiedMaterial d)
        {
            if (!Keyword(m, "_PARALLAXMAP_ON")) return;
            var parallax = d.Surface.Parallax;
            bool packed = Keyword(m, "_PACKED_WORKFLOW_ON") && Int(m, "_EnablePackedHeight", 0) == 1;
            parallax.Height.Map = Main(m, packed ? "_PackedMap" : "_ParallaxMap", AtlasTextureInterpretation.Data);
            if (parallax.Height.Map == null) return;
            parallax.Enabled = true;
            parallax.Height.Channel = packed ? Channel(m, "_HeightChannel", 3) : AtlasChannel.Red;
            parallax.Height.Remap = Filter(m, "_HeightFiltering", "_Height");
            parallax.Method = AtlasParallaxMethod.MultiStep;
            parallax.Steps = Mathf.Clamp(Int(m, "_ParallaxSteps", 25), 1, 50);
            parallax.Depth = Float(m, "_Parallax", 0.01f);
            parallax.HeightOffset = Float(m, "_ParallaxOffset", 0f);
        }

        // Flipbook (suffix "" or "1"): a placed, animated decal blended by its alpha; unlit ones
        // apply after lighting.
        private static void Flipbook(Material m, AtlasUnifiedMaterial d, string suffix)
        {
            if (Int(m, "_EnableSpritesheet" + suffix, 0) != 1) return;
            string index = suffix == "" ? "0" : "1";
            bool sheet = Int(m, "_SpritesheetMode" + index, 0) == 1;
            var map = AtlasSourceMapping.Texture(m, sheet ? "_Spritesheet" + suffix : "_Flipbook" + index);
            if (map == null) return;
            map.Tiling = Vector2.one;
            map.Offset = Vector2.zero;
            map.Scroll = Vector2Of(m, "_Flipbook" + index + "Scroll");
            map.Decal = new AtlasDecal {
                Centre = Vector2Of(m, "_SpritesheetPos" + suffix) + new Vector2(0.5f, 0.5f),
                Scale = (Vector2)AtlasMaterialReader.Vector(m, "_SpritesheetScale" + suffix, Vector4.one),
                Rotation = Float(m, "_SpritesheetRot" + suffix, 0f),
                ClipOutside = sheet || Int(m, "_Flipbook" + index + "ClipEdge", 0) == 1
            };
            var grid = AtlasMaterialReader.Vector(m, "_RowsColumns" + suffix, new Vector4(8f, 8f, 0f, 0f));
            map.Frames = new AtlasFrames {
                Source = sheet ? AtlasFrameSource.SpriteSheet : AtlasFrameSource.TextureArray,
                Columns = Mathf.Max(1, Mathf.RoundToInt(grid.x)), Rows = Mathf.Max(1, Mathf.RoundToInt(grid.y)),
                Rate = Float(m, "_FPS" + suffix, 30f),
                Fixed = Int(m, "_ManualScrub" + suffix, 0) == 1,
                FixedFrame = Int(m, "_ScrubPos" + suffix, 1)
            };
            var (blend, scale) = Blend(Int(m, "_SpritesheetBlending" + suffix, 6));
            var layer = new AtlasSurfaceLayer { SourceAlphaIsOpacity = true,
                Placement = Int(m, "_UnlitSpritesheet" + suffix, 0) == 1 ? AtlasPlacement.Overlay : AtlasPlacement.Surface };
            var color = AtlasMaterialReader.ColorLinear(m, "_SpritesheetCol" + suffix, Color.white) * Float(m, "_SpritesheetBrightness" + suffix, 1f);
            color.a = AtlasMaterialReader.Color(m, "_SpritesheetCol" + suffix, Color.white).a;
            layer.Source.Color = color;
            layer.Source.Map = map;
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.BaseColor, blend, scale));
            d.Layers.Add(layer);
            if (Int(m, "_UseSpritesheetAlpha", 0) == 1)
                d.Report(AtlasIssueKind.Unmapped, "flipbook.alpha", "A flipbook replacing the material's alpha isn't mapped yet.");
        }

        // Refraction: the grabbed screen, offset by the normal; lit or unlit.
        private static void Refraction(Material m, AtlasUnifiedMaterial d)
        {
            var layer = new AtlasRefractionLayer {
                Mask = Mask(m, UberMask.Refraction),
                Placement = Int(m, "_UnlitRefraction", 0) == 1 ? AtlasPlacement.Overlay : AtlasPlacement.Lit,
                IOR = Float(m, "_RefractionIOR", 1.3f),
                Tint = AtlasMaterialReader.ColorLinear(m, "_RefractionTint", Color.white),
                // lerp(refraction, surface, opacity).
                Amount = 1f - Float(m, "_RefractionOpac", 0f),
                ChromaticAberration = Keyword(m, "_CHROMATIC_ABBERATION_ON"),
                ChromaticAberrationStrength = Float(m, "_RefractionCAStr", 0.1f),
                CutThreshold = Float(m, "_RefractionDissolveMaskStr", 1f)
            };
            if (Int(m, "_RefractionBlur", 0) == 1)
                layer.Blur = new AtlasScreenBlur
                {
                    Strength = Float(m, "_RefractionBlurStrength", 0f), ByRoughness = Int(m, "_RefractionBlurRough", 0) == 1 ? 1f : 0f
                };
            layer.CutMask = AtlasSourceMapping.Texture(m, "_RefractionDissolveMask", AtlasTextureInterpretation.Data);
            if (layer.CutMask != null) layer.CutMask.Scroll = Vector2Of(m, "_RefractionDissolveMaskScroll");
            d.Layers.Add(layer);
        }

        // Iridescence: the highlights and reflections hue-rotated by a power band of the view angle,
        // or multiplied by a ramp; scaled by metallic.
        private static void Iridescence(Material m, AtlasUnifiedMaterial d)
        {
            bool ramp = Int(m, "_IridescenceMode", 0) == 1;
            var mask = Raw(m, "_IridescenceMask");
            d.Layers.Add(new AtlasAngleLayer {
                Profile = ramp ? AtlasAngleProfile.Ramp : AtlasAngleProfile.HueRotation,
                Ramp = ramp ? AtlasSourceMapping.Texture(m, "_IridescenceRamp") : null,
                Power = (1f - Float(m, "_IridescenceWidth", 0.7f)) * 10f,
                Edge = Float(m, "_IridescenceEdge", 0f),
                SpecularOnly = true, ScaleByMetallic = true,
                Blend = AtlasBlend.Replace,
                Opacity = Float(m, "_IridescenceStrength", 1f),
                Mask = mask == null ? null : new AtlasMask { Map = mask }
            });
        }

        private static void MapOrSlider(Material m, AtlasScalarInput input, string texture, string useToggle, string slider, float fallback)
        {
            var map = Int(m, useToggle, 0) == 1 ? Main(m, texture, AtlasTextureInterpretation.Data) : null;
            input.Map = map;
            input.Constant = map != null ? 1f : Float(m, slider, fallback);
        }

        // Detail maps on their own transform and UV set, each blended in by strength × the detail mask.
        private static void Detail(Material m, AtlasUnifiedMaterial d)
        {
            var mask = Mask(m, UberMask.Detail);
            if (Int(m, "_UsingDetailAlbedo", 0) == 1)
                DetailLayer(m, d, "_DetailAlbedoMap", AtlasTextureInterpretation.Color, AtlasSurfaceTarget.BaseColor,
                    Int(m, "_DetailAlbedoBlending", 3), Float(m, "_DetailAlbedoStrength", 1f), mask);
            if (Int(m, "_UsingDetailMetallic", 0) == 1 && !Keyword(m, "_SPECULAR_WORKFLOW_ON"))
                DetailLayer(m, d, "_DetailMetallic", AtlasTextureInterpretation.Data, AtlasSurfaceTarget.Metallic,
                    Int(m, "_DetailMetallicBlending", 0), Float(m, "_DetailMetallicStrength", 1f), mask);
            if (Int(m, "_UsingDetailRough", 0) == 1 && !Keyword(m, "_SPECULAR_WORKFLOW_ON"))
                DetailLayer(m, d, "_DetailRoughnessMap", AtlasTextureInterpretation.Data, AtlasSurfaceTarget.Roughness,
                    Int(m, "_DetailRoughBlending", 6), Float(m, "_DetailRoughStrength", 1f), mask);
            if (Int(m, "_UsingDetailOcclusion", 0) == 1)
                DetailLayer(m, d, "_DetailOcclusionMap", AtlasTextureInterpretation.Data, AtlasSurfaceTarget.Occlusion,
                    Int(m, "_DetailOcclusionBlending", 2), Float(m, "_DetailOcclusionStrength", 1f), mask);
            if (Keyword(m, "_DETAIL_NORMALMAP_ON"))
            {
                var normal = DetailMap(m, "_DetailNormalMap", AtlasTextureInterpretation.TangentNormal);
                if (normal != null)
                {
                    var layer = new AtlasSurfaceLayer { Mask = mask };
                    layer.Source.Map = normal;
                    layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Normal, AtlasBlend.Replace, Float(m, "_DetailNormalMapScale", 1f)));
                    d.Layers.Add(layer);
                }
            }
        }

        private static void DetailLayer(Material m, AtlasUnifiedMaterial d, string texture, AtlasTextureInterpretation interpretation,
            AtlasSurfaceTarget target, int blend, float strength, AtlasMask mask)
        {
            var map = DetailMap(m, texture, interpretation);
            if (map == null) return;
            var (unifiedBlend, opacityScale) = Blend(blend);
            var layer = new AtlasSurfaceLayer { Mask = mask, SourceAlphaIsOpacity = blend == 1 };
            layer.Source.Map = map;
            layer.Targets.Add(new AtlasLayerTarget(target, blend == 1 ? AtlasBlend.Replace : unifiedBlend, strength * opacityScale));
            d.Layers.Add(layer);
        }

        // The detail transform: its own tiling, scroll and rotation, on UV0–2.
        private static AtlasTextureReference DetailMap(Material m, string texture, AtlasTextureInterpretation interpretation)
        {
            var map = AtlasSourceMapping.Texture(m, texture, interpretation, "_DetailAlbedoMap");
            if (map == null) return null;
            map.Space = AtlasTextureSpace.UV0 + Mathf.Clamp(Int(m, "_UVSec", 0), 0, 2);
            map.Distortion = detailDistortion;
            map.Scroll = Vector2Of(m, "_DetailScroll");
            UberRotation(map, Float(m, "_DetailRot", 0f));
            return map;
        }

        // Uber turns the UV clockwise around its origin, after the tiling, offset and scroll; the
        // unified rotation turns it counterclockwise around (0.5, 0.5), before them. Turning the
        // offset and scroll and moving the pivot gives the same UV when the tiling is the same on
        // both axes; with different tilings per axis it's approximate.
        private static void UberRotation(AtlasTextureReference map, float degrees)
        {
            if (degrees == 0f) return;
            float radians = -degrees * Mathf.Deg2Rad, cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
            Vector2 Turn(Vector2 v) => new Vector2(cos * v.x - sin * v.y, sin * v.x + cos * v.y);
            var centre = new Vector2(0.5f, 0.5f);
            map.Rotation = -degrees;
            map.Offset = Turn(map.Offset) + Vector2.Scale(map.Tiling, Turn(centre) - centre);
            map.Scroll = Turn(map.Scroll);
        }

        // Shading: physically based, or ramp toon with a ramp described by numbers or a texture.
        private static void Lighting(Material m, AtlasUnifiedMaterial d)
        {
            var lighting = d.Lighting;
            int shadows = Int(m, "_ShadowMode", 0);
            lighting.Family = shadows == 0 ? AtlasLightingFamily.PhysicallyBased : AtlasLightingFamily.RampToon;
            float disney = Float(m, "_DisneyDiffuse", 0f);
            lighting.Diffuse = disney > 0f ? AtlasDiffuseModel.Disney : AtlasDiffuseModel.Lambert;
            lighting.DisneyAmount = disney > 0f ? disney : 1f;
            float probes = Float(m, "_SHStr", 0f);
            lighting.Ambient = probes > 0f ? AtlasAmbientModel.Directional : AtlasAmbientModel.Average;
            lighting.DirectionalAmount = probes > 0f ? probes : 1f;
            lighting.Contributions.MainLight = Float(m, "_RTDirectCont", 1f);
            lighting.Contributions.Ambient = Float(m, "_RTIndirectCont", 1f);
            // Baked lighting: without a real-time light the probes stand in for it.
            lighting.Contributions.AmbientAsMainLight = Float(m, "_DirectCont", 0.6f);
            lighting.Contributions.AmbientWithoutMainLight = Float(m, "_IndirectCont", 0.5f);
            // Clamp Additive saturates each additional light's colour: a limit of 1.
            lighting.AdditionalLights.LimitBrightness = Int(m, "_ClampAdditive", 1) == 1;
            lighting.Contributions.VertexLights = Float(m, "_VLightCont", 1f);
            lighting.Contributions.AdditionalLights = Float(m, "_AddCont", 1f);
            lighting.LimitBrightness = Int(m, "_ColorPreservation", 1) == 1;
            if (Int(m, "_StaticLightDirToggle", 0) == 1) lighting.Direction = AtlasLightDirectionModel.Fixed;
            lighting.LightDirection = (Vector3)AtlasMaterialReader.Vector(m, "_StaticLightDir", new Vector4(0f, 0.75f, 1f, 0f));
            if (shadows == 0) return;

            var ramp = lighting.Ramp;
            if (shadows == 1)
            {
                // Manual Blend: two linear widths from an offset, blended; shadow tint below.
                float position = Mathf.Clamp01(Float(m, "_RampPos", 0f));
                float width0 = Mathf.Max(Float(m, "_RampWidth0", 0.005f), 1e-4f);
                float width1 = Mathf.Max(Float(m, "_RampWidth1", 0.5f), 1e-4f);
                float weight = Float(m, "_RampWeight", 0f);
                var tint = AtlasMaterialReader.ColorLinear(m, "_ShadowTint", Color.black);
                foreach (float nl in new[] { -1f, position, position + width0, position + width1, 1f })
                {
                    float n = Mathf.Clamp(nl, -1f, 1f);
                    float value = Mathf.Lerp(Mathf.Clamp01((n - position) / width0), Mathf.Clamp01((n - position) / width1), weight);
                    var color = Color.Lerp(tint, Color.white, value);
                    color.a = 1f;
                    float u = n * 0.5f + 0.5f;
                    if (ramp.Keys.Exists(k => Mathf.Approximately(k.Position, u))) continue;
                    ramp.Keys.Add(new AtlasRampKey(u, color));
                }
                ramp.Keys.Sort((a, b) => a.Position.CompareTo(b.Position));
            }
            else ramp.Ramp = AtlasSourceMapping.Texture(m, "_ShadowRamp");
            ramp.RampStrength = Float(m, "_ShadowStr", 0.5f);
            ramp.RampStrengthMask = Mask(m, UberMask.Shadow);
            var painted = AtlasSourceMapping.Texture(m, "_DetailShadowMap", AtlasTextureInterpretation.Data);
            if (painted != null) { painted.Tiling = Vector2.one; painted.Offset = Vector2.zero; ramp.TermMask = new AtlasMask { Map = painted }; }
            lighting.ReceiveShadows = Int(m, "_RTSelfShadow", 1) == 1 ? 1f : 0f;
            ramp.Conditions = (AtlasRampConditions)Mathf.Clamp(Int(m, "_ShadowConditions", 0), 0, 2);
            ramp.Dithered = Int(m, "_DitheredShadows", 0) == 1;
        }

        private static void Reflections(Material m, AtlasUnifiedMaterial d)
        {
            var reflections = d.Surface.Response.Reflections;
            reflections.Enabled = Keyword(m, "_REFLECTIONS_ON");
            if (!reflections.Enabled) return;
            reflections.Strength = Float(m, "_ReflectionStr", 1f);
            reflections.Tint = AtlasMaterialReader.ColorLinear(m, "_ReflCol", Color.white);
            reflections.Mask = Mask(m, UberMask.Reflection);
            reflections.FresnelStrength = Int(m, "_FresnelToggle", 1) == 1 ? Float(m, "_FresnelStrength", 1f) : 0f;
            reflections.RoughnessOverride = Int(m, "_ReflUseRough", 0) == 1;
            reflections.Roughness = Float(m, "_ReflRough", 0.5f);
            var cube = AtlasSourceMapping.Texture(m, "_ReflCube");
            reflections.CubemapLit = Int(m, "_LitCubemap", 0) == 1;
            if (Keyword(m, "_CUBEMAP_REFLECTIONS_ON")) { reflections.Source = AtlasReflectionSource.Cubemap; reflections.Cubemap = cube; }
            else if (Keyword(m, "_REFLECTION_FALLBACK_ON")) reflections.FallbackCubemap = cube;
            reflections.Steps = Int(m, "_ReflStepping", 0) == 1 ? Int(m, "_ReflSteps", 7) : 0;
            reflections.LightBased = Int(m, "_LightingBasedIOR", 0);
        }

        private static void Highlights(Material m, AtlasUnifiedMaterial d)
        {
            var highlights = d.Surface.Response.Highlights;
            highlights.Enabled = Keyword(m, "_SPECULAR_ON");
            if (!highlights.Enabled) return;
            highlights.Strength = Float(m, "_SpecStr", 1f);
            highlights.Tint = AtlasMaterialReader.ColorLinear(m, "_SpecCol", Color.white);
            highlights.Mask = Mask(m, UberMask.Specular);
            highlights.RoughnessOverride = Int(m, "_SpecUseRough", 0) == 1;
            highlights.Roughness = Float(m, "_SpecRough", 0.5f);
            bool stepped = Int(m, "_SharpSpecular", 0) == 1;
            highlights.Steps = stepped ? Int(m, "_SharpSpecStr", 1) : 0;
            highlights.RealtimeLightOnly = Int(m, "_RealtimeSpec", 0) == 1;
            highlights.BaseColorMix = Int(m, "_SpecBiasOverrideToggle", 0) == 1 ? Float(m, "_SpecBiasOverride", 0.5f) : 0f;
            highlights.IgnoreLightColor = Int(m, "_ManualSpecBright", 0) == 1;
            bool aniso = Keyword(m, "_SPECULAR_ANISO_ON"), combined = Keyword(m, "_SPECULAR_COMBINED_ON");
            if (!aniso && !combined) return;
            highlights.Shape = aniso ? AtlasHighlightShape.Stretched : AtlasHighlightShape.Both;
            var stretched = highlights.Stretched;
            stretched.Width = Float(m, "_AnisoAngleY", 1f);
            stretched.SecondWidth = Float(m, "_AnisoLayerY", 1f);
            stretched.SecondStrength = Float(m, "_AnisoLayerStr", 0.1f);
            stretched.SecondOver = Int(m, "_AnisoLerp", 0) == 1;
            stretched.Strength = Float(m, "_AnisoStr", 1f);
            stretched.Steps = stepped ? Int(m, "_AnisoSteps", 2) : 0;
            stretched.RippleFrequency = Float(m, "_RippleFrequency", 1f);
            stretched.RippleAmplitude = Float(m, "_RippleAmplitude", 0.5f);
            stretched.RippleStrength = Float(m, "_RippleStrength", 0f);
            stretched.RippleContinuity = Float(m, "_RippleContinuity", 1f);
            if (combined)
            {
                // The anisotropic blend mask: where it's white the highlight is round.
                highlights.ShapeMask = Mask(m, UberMask.SpecularBlend);
                if (highlights.ShapeMask != null) highlights.ShapeMask.Invert = false;
            }
        }

        private static void Subsurface(Material m, AtlasUnifiedMaterial d)
        {
            if (!Keyword(m, "_SUBSURFACE_ON")) return;
            var layer = new AtlasSubsurfaceLayer {
                Mask = Mask(m, UberMask.Subsurface),
                BaseColorMix = Int(m, "_ScatterBaseColorTint", 0),
                Thickness = Main(m, "_ThicknessMap", AtlasTextureInterpretation.Data),
                ThicknessPower = Float(m, "_ThicknessMapPower", 1f),
                Intensity = Float(m, "_ScatterIntensity", 1f),
                Power = Float(m, "_ScatterPow", 1f),
                Distortion = Float(m, "_ScatterDist", 1f),
                Ambient = Float(m, "_ScatterAmbient", 0f)
            };
            layer.Color.Color = AtlasMaterialReader.ColorLinear(m, "_ScatterCol", Color.white);
            layer.Color.Map = Main(m, "_ScatterTex");
            d.Layers.Add(layer);
        }

        // Two matcaps, added after lighting; scaled by the surroundings' brightness unless unlit.
        private static void Matcaps(Material m, AtlasUnifiedMaterial d)
        {
            Matcap(m, d, 0, "_Matcap", "_MatcapColor", "_MatcapStr", "_MatcapBlending", "_UnlitMatcap", "_MatcapUseRough", "_MatcapRough",
                "_MatcapCenter", Mask(m, UberMask.PrimaryMatcap));
            if (Int(m, "_UseMatcap1", 0) == 1)
                Matcap(m, d, 1, "_Matcap1", "_MatcapColor1", "_MatcapStr1", "_MatcapBlending1", "_UnlitMatcap1", "_MatcapUseRough1", "_MatcapRough1",
                    "_MatcapCenter1", Mask(m, UberMask.SecondaryMatcap));
        }

        private static void Matcap(Material m, AtlasUnifiedMaterial d, int index, string texture, string color, string strength, string blend,
            string unlit, string useRough, string rough, string centre, AtlasMask mask)
        {
            var map = AtlasSourceMapping.Texture(m, texture);
            var layer = new AtlasLookupLayer {
                Mask = mask, Placement = AtlasPlacement.Overlay, Blend = AtlasBlend.Add,
                Opacity = Float(m, strength, 1f),
                SourceAlphaIsOpacity = Int(m, blend, 0) == 1,
                LightResponse = Int(m, unlit, 0) == 1 ? AtlasLightResponse.IgnoreLight : AtlasLightResponse.ScaleByLight,
                BlurFromRoughness = Int(m, useRough, 0) == 0,
                Blur = Mathf.Clamp01(Float(m, rough, 0.5f)),
                CentreEye = Int(m, centre, 0) == 1
            };
            if (map != null)
            {
                layer.Tiling = map.Tiling;
                map.Space = AtlasTextureSpace.View;
                map.Tiling = Vector2.one;
                map.Offset = Vector2.zero;
                map.Distortion = index == 0 ? matcapDistortion0 : matcapDistortion1;
            }
            layer.Source.Map = map;
            layer.Source.Color = AtlasMaterialReader.ColorLinear(m, color, Color.white);
            string normal = "_MatcapNormal" + index;
            if (Int(m, normal + "Toggle", 0) == 1)
            {
                layer.NormalMap = AtlasSourceMapping.Texture(m, normal, AtlasTextureInterpretation.TangentNormal);
                if (layer.NormalMap != null) layer.NormalMap.Scroll = Vector2Of(m, normal + "Scroll");
                layer.NormalStrength = Float(m, normal + "Str", 1f);
                layer.NormalWithSurface = Int(m, normal + "Mix", 0) == 1;
            }
            d.Layers.Add(layer);
        }

        // Rim: a power band of the view angle, blended onto the lit colour.
        private static void Rim(Material m, AtlasUnifiedMaterial d)
        {
            var (blend, scale) = Blend(Int(m, "_RimBlending", 0));
            var rim = new AtlasAngleLayer {
                Profile = AtlasAngleProfile.PowerBand,
                Power = (1f - Float(m, "_RimWidth", 0.5f)) * 10f,
                Edge = Float(m, "_RimEdge", 0f),
                Opacity = Float(m, "_RimStr", 1f) * scale,
                Blend = blend,
                Mask = Mask(m, UberMask.Rim),
                LightResponse = Int(m, "_UnlitRim", 1) == 1 ? AtlasLightResponse.IgnoreLight : AtlasLightResponse.ScaleByLight
            };
            rim.Color.Color = AtlasMaterialReader.ColorLinear(m, "_RimCol", Color.white);
            var map = AtlasSourceMapping.Texture(m, "_RimTex");
            if (map != null) { map.Scroll = Vector2Of(m, "_RimScroll"); map.Distortion = rimDistortion; }
            rim.Color.Map = map;
            d.Layers.Add(rim);
        }

        // Environment rim: the reflection probe at the edge. The shader applies its tint twice.
        private static void EnvironmentRim(Material m, AtlasUnifiedMaterial d)
        {
            var (blend, scale) = Blend(Int(m, "_ERimBlending", 0));
            var tint = AtlasMaterialReader.ColorLinear(m, "_ERimTint", Color.white);
            var rim = new AtlasAngleLayer {
                Profile = AtlasAngleProfile.PowerBand,
                Power = (1f - Float(m, "_ERimWidth", 0.7f)) * 10f,
                Edge = Float(m, "_ERimEdge", 0f),
                Opacity = Float(m, "_ERimStr", 1f) * scale,
                Blend = blend,
                Mask = Mask(m, UberMask.EnvironmentRim),
                ColorSource = AtlasAngleColorSource.Environment,
                EnvironmentRoughnessOverride = Int(m, "_ERimUseRough", 0) == 1,
                EnvironmentRoughness = Float(m, "_ERimRoughness", 0.5f)
            };
            rim.Color.Color = new Color(tint.r * tint.r, tint.g * tint.g, tint.b * tint.b, 1f);
            d.Layers.Add(rim);
        }

        // Two emission layers, masked; pulse (time) and light reactivity (light level) scale both.
        private static void Emission(Material m, AtlasUnifiedMaterial d)
        {
            var mask = Mask(m, UberMask.Emission);
            var first = EmissionLayer(m, d, "_EmissionMap", "_EmissionColor", "_EmissIntensity", "_EmissScroll", mask);
            var second = EmissionLayer(m, d, "_EmissionMap2", "_EmissionColor2", "_EmissIntensity2", "_EmissScroll2", mask);
            foreach (int index in new[] { first, second })
            {
                if (index < 0) continue;
                string target = "layers[" + index + "].source.color";
                if (Keyword(m, "_PULSE_ON"))
                {
                    float strength = Float(m, "_PulseStr", 0.5f);
                    var pulse = new AtlasModulator { Target = target, Source = AtlasModulatorSource.Time,
                        Min = 1f - strength, Max = 1f, Mask = Mask(m, UberMask.EmissionPulse) };
                    float speed = Float(m, "_PulseSpeed", 1f);
                    switch (Int(m, "_PulseWaveform", 0))
                    {
                        case 1: pulse.Time.Waveform = AtlasWaveform.Square; pulse.Time.Frequency = speed / (2f * Mathf.PI); break;
                        case 2: pulse.Time.Waveform = AtlasWaveform.Triangle; pulse.Time.Frequency = speed / 6f; break;
                        case 3: pulse.Time.Waveform = AtlasWaveform.Saw; pulse.Time.Frequency = speed / 5f; break;
                        case 4: pulse.Time.Waveform = AtlasWaveform.ReverseSaw; pulse.Time.Frequency = speed / 5f; break;
                        default: pulse.Time.Waveform = AtlasWaveform.Sine; pulse.Time.Frequency = speed / (2f * Mathf.PI); break;
                    }
                    d.Modulators.Add(pulse);
                }
                if (Int(m, "_ReactToggle", 0) == 1)
                {
                    var react = new AtlasModulator { Target = target, Source = AtlasModulatorSource.LightLevel, Min = 1f, Max = 0f };
                    react.LightLevel.Smooth = Int(m, "_CrossMode", 0) == 1;
                    react.LightLevel.Threshold = Float(m, "_ReactThresh", 0.5f);
                    react.LightLevel.Crossfade = Float(m, "_Crossfade", 0.1f);
                    react.LightLevel.IncludeShadows = Int(m, "_UseShadowsForLREmiss", 0) == 1;
                    d.Modulators.Add(react);
                }
            }
        }

        // Returns the layer's index, or −1 when nothing glows.
        private static int EmissionLayer(Material m, AtlasUnifiedMaterial d, string texture, string color, string intensity,
            string scroll, AtlasMask mask)
        {
            var linear = AtlasMaterialReader.ColorLinear(m, color, Color.black) * Float(m, intensity, 1f);
            if (linear.maxColorComponent <= 0f) return -1;
            var layer = new AtlasSurfaceLayer { Placement = AtlasPlacement.Emission, Mask = mask };
            layer.Source.Color = new Color(linear.r, linear.g, linear.b, 1f);
            var map = AtlasSourceMapping.Texture(m, texture);
            if (map != null)
            {
                map.Scroll = Vector2Of(m, scroll);
                // Only the first emission map follows the distortion.
                if (texture == "_EmissionMap") map.Distortion = emissionDistortion;
            }
            layer.Source.Map = map;
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.Emission, AtlasBlend.Add));
            d.Layers.Add(layer);
            return d.Layers.Count - 1;
        }

        // Vertex manipulation: expansion, rotation and position in object space, then rounding of the
        // world position. Masks are sampled per vertex on raw UV0.
        private static void VertexManipulation(Material m, AtlasUnifiedMaterial d)
        {
            var expansion = (Vector3)AtlasMaterialReader.Vector(m, "_VertexExpansion", Vector4.zero);
            if (expansion != Vector3.zero)
                d.Deformations.Add(new AtlasDeformation { Kind = AtlasDeformationKind.Inflate, Amount = expansion * 0.001f,
                    AbsoluteNormal = Int(m, "_VertexExpansionClamp", 0) == 1, Mask = RawMask(m, "_VertexExpansionMask") });
            var rotation = (Vector3)AtlasMaterialReader.Vector(m, "_VertexRotation", Vector4.zero);
            if (rotation != Vector3.zero)
                // Uber's rotation turns each plane by the angle + 90° once any angle is set.
                d.Deformations.Add(new AtlasDeformation { Kind = AtlasDeformationKind.Rotate, Amount = rotation + new Vector3(90f, 90f, 90f) });
            var position = (Vector3)AtlasMaterialReader.Vector(m, "_VertexPosition", Vector4.zero);
            if (position != Vector3.zero)
                d.Deformations.Add(new AtlasDeformation { Kind = AtlasDeformationKind.Translate, Amount = position });
            float rounding = Float(m, "_VertexRounding", 0f);
            if (rounding > 0f)
                d.Deformations.Add(new AtlasDeformation { Kind = AtlasDeformationKind.Snap, Strength = rounding,
                    Precision = Float(m, "_VertexRoundingPrecision", 100f), Mask = RawMask(m, "_VertexRoundingMask") });
        }

        // The Outline variant: an inverted hull, thickness × 0.01 × multiplier along the normal.
        private static void Outline(Material m, AtlasUnifiedMaterial d)
        {
            var outline = new AtlasOutline {
                Width = Float(m, "_OutlineThicc", 0.1f) * 0.01f * Float(m, "_OutlineMult", 1f),
                WidthFromVertexColor = Int(m, "_UseVertexColor", 0) == 1,
                HideWithin = Float(m, "_OutlineRange", 0f),
                MultiplyByBaseColor = Int(m, "_ApplyAlbedoTint", 0) == 1,
                Lit = Int(m, "_ApplyOutlineLighting", 0) == 1,
                Emission = Int(m, "_ApplyOutlineEmiss", 0) == 1
            };
            outline.Color.Color = AtlasMaterialReader.ColorLinear(m, "_OutlineCol", new Color(0.75f, 0.75f, 0.75f, 1f));
            var texture = AtlasSourceMapping.Texture(m, "_OutlineTex");
            if (texture != null) texture.Scroll = Vector2Of(m, "_OutlineScroll");
            outline.Color.Map = texture;
            // The thickness mask: separate (raw UV0, or its own transform), or packed (mask 3, alpha).
            if (Keyword(m, "_SEPARATE_MASKING_ON"))
            {
                AtlasTextureReference map;
                if (Keyword(m, "_MASK_TRANSFORMS_ON"))
                {
                    map = AtlasSourceMapping.Texture(m, "_OutlineMask", AtlasTextureInterpretation.Data);
                    if (map != null) map.Scroll = Vector2Of(m, "_OutlineMaskScroll");
                }
                else map = Raw(m, "_OutlineMask");
                if (map != null) outline.WidthMask = new AtlasMask { Map = map };
            }
            else if (Keyword(m, "_PACKED_MASKING_ON"))
            {
                var packed = Raw(m, "_PackedMask3");
                if (packed != null) outline.WidthMask = new AtlasMask { Map = packed, Channel = AtlasChannel.Alpha };
            }
            Stencil(m, outline.Pass.Stencil, "_OutlineStencilPass", "_OutlineStencilCompare");
            int cull = Int(m, "_OutlineCulling", 1);
            outline.Pass.Cull = cull == 0 ? AtlasCull.Off : cull == 1 ? AtlasCull.Front : AtlasCull.Back;
            d.Outlines.Add(outline);
        }

        // Audio Link: each target is scaled by lerp(1, remapped band, multiplier × strength), where the
        // band is remapped from the global range to the target's. Without AudioLink the band reads 1.
        private static void AudioLink(Material m, AtlasUnifiedMaterial d)
        {
            float strength = Float(m, "_AudioLinkStrength", 1f);
            for (int i = 0; i < d.Layers.Count; ++i)
            {
                string layer = "layers[" + i + "]";
                switch (d.Layers[i])
                {
                    case AtlasSurfaceLayer emission when emission.Placement == AtlasPlacement.Emission:
                        Audio(m, d, layer + ".source.color", "Emission", Float(m, "_AudioLinkEmissionMultiplier", 0f) * strength);
                        break;
                    case AtlasAngleLayer rim when rim.Profile == AtlasAngleProfile.PowerBand && rim.ColorSource == AtlasAngleColorSource.Color:
                        Audio(m, d, layer + ".opacity", "Rim", Float(m, "_AudioLinkRimMultiplier", 0f) * strength);
                        // The width is scaled (without the global strength); the power band's power is
                        // (1 − width) × 10, linear in the band.
                        float k = Float(m, "_AudioLinkRimWidth", 0f);
                        if (k > 0f)
                        {
                            float width = Float(m, "_RimWidth", 0.5f);
                            var (low, high) = Factor(m, "Rim", k);
                            d.Modulators.Add(AudioModulator(m, layer + ".power", AtlasModulatorMode.Replace,
                                (1f - width * low) * 10f, (1f - width * high) * 10f, "_AudioLinkRimBand"));
                        }
                        if (Float(m, "_AudioLinkRimPulse", 0f) > 0f)
                            d.Report(AtlasIssueKind.Unmapped, "audiolink.rim-pulse", "AudioLink's rim pulse isn't mapped yet.");
                        break;
                    case AtlasTransitionLayer _:
                        Audio(m, d, layer + ".amount", "BCDissolve", Float(m, "_AudioLinkBCDissolveMultiplier", 0f) * strength);
                        break;
                }
            }
            for (int i = 0; i < d.Outlines.Count; ++i)
                Audio(m, d, "outlines[" + i + "].width", "Outline", Float(m, "_AudioLinkOutlineMultiplier", 0f) * strength);
            float vertex = Float(m, "_AudioLinkVertManipMultiplier", 0f);
            for (int i = 0; i < d.Deformations.Count; ++i)
            {
                string deformation = "deformations[" + i + "]";
                switch (d.Deformations[i].Kind)
                {
                    case AtlasDeformationKind.Inflate: Audio(m, d, deformation + ".amount", "VertManip", vertex * strength); break;
                    case AtlasDeformationKind.Snap: Audio(m, d, deformation + ".strength", "VertManip", vertex); break;
                    case AtlasDeformationKind.Rotate:
                        if (vertex > 0f)
                            d.Report(AtlasIssueKind.Unmapped, "audiolink.vertex-rotation", "AudioLink on the vertex rotation isn't mapped yet.");
                        break;
                }
            }
            if (Float(m, "_AudioLinkUVDistortionMultiplier", 0f) > 0f && Keyword(m, "_UV_DISTORTION_ON"))
                d.Report(AtlasIssueKind.Unmapped, "audiolink.distortion", "AudioLink on the UV distortion isn't mapped yet.");

            // The oscilloscope: the waveform drawn on raw UV0, added after lighting.
            float scope = Float(m, "_OscilloscopeStrength", 0f);
            if (scope <= 0f) return;
            var color = AtlasMaterialReader.Color(m, "_OscilloscopeCol", Color.white);
            var linear = color.linear * color.a;
            var margins = AtlasMaterialReader.Vector(m, "_OscilloscopeMarginTB", new Vector4(1f, 0f, 0f, 0f));
            d.Layers.Add(new AtlasPatternLayer {
                Kind = AtlasPatternKind.Oscilloscope,
                Color = new Color(linear.r, linear.g, linear.b, 1f),
                Opacity = scope * strength,
                Scale = Vector2Of(m, "_OscilloscopeScale"),
                Offset = Vector2Of(m, "_OscilloscopeOffset"),
                Rotation = Float(m, "_OscilloscopeRot", 0f),
                URange = Vector2Of(m, "_OscilloscopeMarginLR"),
                VRange = new Vector2(margins.y, margins.x)
            });
        }

        // Adds a multiplying audio modulator when the target's multiplier is above 0.
        private static void Audio(Material m, AtlasUnifiedMaterial d, string target, string name, float multiplier)
        {
            if (multiplier <= 0f) return;
            var (low, high) = Factor(m, name, multiplier);
            d.Modulators.Add(AudioModulator(m, target, AtlasModulatorMode.Multiply, low, high, "_AudioLink" + name + "Band"));
        }

        // The factor lerp(1, remap(level), k) at band levels 0 and 1; it's linear in the level.
        private static (float Low, float High) Factor(Material m, string name, float k)
        {
            float globalMin = Float(m, "_AudioLinkRemapMin", 0f), globalMax = Float(m, "_AudioLinkRemapMax", 1f);
            float min = Float(m, "_AudioLinkRemap" + name + "Min", 0f), max = Float(m, "_AudioLinkRemap" + name + "Max", 1f);
            float span = Mathf.Abs(globalMax - globalMin) < 1e-6f ? 1e-6f : globalMax - globalMin;
            float Remap(float level) => min + (level - globalMin) * (max - min) / span;
            return (Mathf.Lerp(1f, Remap(0f), k), Mathf.Lerp(1f, Remap(1f), k));
        }

        private static AtlasModulator AudioModulator(Material m, string target, AtlasModulatorMode mode, float min, float max, string band)
        {
            var modulator = new AtlasModulator { Target = target, Mode = mode, Min = min, Max = max, Source = AtlasModulatorSource.Audio };
            modulator.Audio.Band = (AtlasAudioBand)Mathf.Clamp(Int(m, band, 0), 0, 3);
            // Uber reads the raw band, and 1 when the world has no AudioLink.
            modulator.Audio.Smoothing = 0f;
            modulator.Audio.ValueWithoutAudioLink = 1f;
            return modulator;
        }

        // The stencil, when the inspector's stencil toggle is on.
        private static void Stencil(Material m, AtlasStencil stencil, string passOperation, string compare)
        {
            if (Int(m, "_StencilToggle", 0) != 1) return;
            stencil.Enabled = true;
            stencil.Reference = Int(m, "_StencilRef", 1);
            stencil.Compare = (AtlasDepthTest)Mathf.Clamp(Int(m, compare, 8), 0, 8);
            stencil.Pass = (AtlasStencilOperation)Mathf.Clamp(Int(m, passOperation, 0), 0, 7);
            stencil.Fail = (AtlasStencilOperation)Mathf.Clamp(Int(m, "_StencilFail", 0), 0, 7);
            stencil.DepthFail = (AtlasStencilOperation)Mathf.Clamp(Int(m, "_StencilZFail", 0), 0, 7);
        }

        // Filtering: colour adjustments on the surface colour, or with Post Filtering on the lit colour.
        private static void Filtering(Material m, AtlasUnifiedMaterial d)
        {
            var target = Keyword(m, "_POST_FILTERING_ON") ? AtlasAdjustmentTarget.FinalColor : AtlasAdjustmentTarget.SurfaceColor;
            var mask = Mask(m, UberMask.Filter);
            // Invert applies to the surface colour, unmasked, before the other filters.
            float invert = Float(m, "_Invert", 0f);
            if (invert > 0f)
                d.ColorAdjustments.Add(new AtlasColorAdjustment { Target = AtlasAdjustmentTarget.SurfaceColor,
                    Operation = AtlasColorOperation.SmoothInvert, Amount = invert });
            if (Int(m, "_TeamFiltering", 0) == 1)
            {
                var team = Adjust(d, target, mask, AtlasColorOperation.ChannelTint, 1f);
                for (int i = 0; i < 4; ++i)
                    team.ChannelColors[i] = AtlasMaterialReader.ColorLinear(m, "_TeamColor" + i, Color.white);
                if (Keyword(m, "_SEPARATE_MASKING_ON") && Keyword(m, "_MASK_TRANSFORMS_ON"))
                {
                    team.ChannelMask = AtlasSourceMapping.Texture(m, "_TeamColorMask", AtlasTextureInterpretation.Data);
                    if (team.ChannelMask != null) team.ChannelMask.Scroll = Vector2Of(m, "_TeamColorMaskScroll");
                }
                else team.ChannelMask = Main(m, "_TeamColorMask", AtlasTextureInterpretation.Data);
            }
            float hue = Float(m, "_Hue", 0f);
            bool autoShift = Int(m, "_AutoShift", 0) == 1;
            bool mono = Int(m, "_MonoTint", 0) == 1;
            if (hue % 1f != 0f || autoShift || mono)
            {
                var adjustment = Adjust(d, target, mask, AtlasColorOperation.Hue, hue);
                adjustment.HueSet = mono;
                adjustment.HueMethod = Int(m, "_HueMode", 0) == 1 ? AtlasHueMethod.Oklab : AtlasHueMethod.HSV;
                if (autoShift)
                {
                    var cycle = new AtlasModulator { Target = "colorAdjustments[" + (d.ColorAdjustments.Count - 1) + "].amount",
                        Source = AtlasModulatorSource.Time, Mode = AtlasModulatorMode.Add };
                    cycle.Time.Waveform = AtlasWaveform.Saw;
                    cycle.Time.Frequency = Float(m, "_AutoShiftSpeed", 0.25f);
                    d.Modulators.Add(cycle);
                }
            }
            Vector4 rgb = AtlasMaterialReader.Vector(m, "_RGB", Vector4.one);
            if (rgb.x != 1f || rgb.y != 1f || rgb.z != 1f)
                Adjust(d, target, mask, AtlasColorOperation.Tint, 1f).Color = new Color(rgb.x, rgb.y, rgb.z, 1f);
            float saturation = Float(m, "_Saturation", 1f);
            if (saturation != 1f) Adjust(d, target, mask, AtlasColorOperation.Saturation, saturation);
            float hdr = Float(m, "_HDR", 0f);
            if (hdr > 0f) Adjust(d, target, mask, AtlasColorOperation.SrgbDecode, hdr);
            float aces = Float(m, "_ACES", 0f);
            if (aces > 0f) Adjust(d, target, mask, AtlasColorOperation.ToneMapAces, aces);
            float contrast = Float(m, "_Contrast", 1f);
            if (contrast != 1f) Adjust(d, target, mask, AtlasColorOperation.Contrast, contrast);
            float brightness = Float(m, "_Brightness", 1f);
            if (brightness != 1f) Adjust(d, target, mask, AtlasColorOperation.Brightness, brightness);
        }

        private static AtlasColorAdjustment Adjust(AtlasUnifiedMaterial d, AtlasAdjustmentTarget target, AtlasMask mask,
            AtlasColorOperation operation, float amount)
        {
            var adjustment = new AtlasColorAdjustment { Target = target, Mask = mask, Operation = operation, Amount = amount };
            d.ColorAdjustments.Add(adjustment);
            return adjustment;
        }

        // The paid Uber X adds these; the free version's code is a stub, so they're unsupported.
        private static void ReportUberX(Material m, AtlasUnifiedMaterial d)
        {
            foreach (string toggle in new[] { "_DistanceFadeToggle", "_DissolveStyle", "_Screenspace", "_CloneToggle",
                         "_GlitchToggle", "_ShatterToggle", "_WireframeToggle" })
            {
                if (Float(m, toggle, 0f) <= 0f) continue;
                d.Report(AtlasIssueKind.Unsupported, "material.uber-x", "Uber X special features aren't supported; their source isn't available.");
                break;
            }
        }

        // Mochie's blends: 0 Add, 1 Sub (or Alpha for details), 2 Mul, 3 Mul×2, 4 Overlay, 5 Screen,
        // 6 Lerp (half way, so the opacity halves).
        private static (AtlasBlend Blend, float OpacityScale) Blend(int value)
        {
            switch (value)
            {
                case 1: return (AtlasBlend.Subtract, 1f);
                case 2: return (AtlasBlend.Multiply, 1f);
                case 3: return (AtlasBlend.MultiplyDouble, 1f);
                case 4: return (AtlasBlend.Overlay, 1f);
                case 5: return (AtlasBlend.Screen, 1f);
                case 6: return (AtlasBlend.Replace, 0.5f);
                default: return (AtlasBlend.Add, 1f);
            }
        }

        // A feature mask: one texture per feature (red), four packed textures, or none. Masks use the
        // main transform unless mask transforms are on, which give separate masks their own.
        private static AtlasMask Mask(Material m, UberMask mask)
        {
            if (Keyword(m, "_SEPARATE_MASKING_ON"))
            {
                string name = SeparateMasks[(int)mask];
                AtlasTextureReference map;
                if (Keyword(m, "_MASK_TRANSFORMS_ON"))
                {
                    map = AtlasSourceMapping.Texture(m, name, AtlasTextureInterpretation.Data);
                    if (map != null) map.Scroll = Vector2Of(m, name + "Scroll");
                }
                else map = Main(m, name, AtlasTextureInterpretation.Data);
                return map == null ? null : new AtlasMask { Map = map, Invert = mask == UberMask.SpecularBlend };
            }
            if (!Keyword(m, "_PACKED_MASKING_ON")) return null;
            var packed = Main(m, "_PackedMask" + ((int)mask / 4), AtlasTextureInterpretation.Data);
            return packed == null ? null : new AtlasMask { Map = packed, Channel = Channels[(int)mask % 4], Invert = mask == UberMask.SpecularBlend };
        }

        // The main transform: the base colour's tiling and offset, scroll and rotation.
        private static AtlasTextureReference Main(Material m, string property,
            AtlasTextureInterpretation interpretation = AtlasTextureInterpretation.Color)
        {
            var map = AtlasSourceMapping.Texture(m, property, interpretation, "_MainTex");
            if (map == null) return null;
            map.Scroll = Vector2Of(m, "_MainTexScroll");
            UberRotation(map, Float(m, "_MainTexRot", 0f));
            map.Distortion = mainDistortion;
            return map;
        }

        // A texture sampled on raw UV0: no tiling, offset or scroll.
        private static AtlasTextureReference Raw(Material m, string property)
        {
            var map = AtlasSourceMapping.Texture(m, property, AtlasTextureInterpretation.Data);
            if (map != null) { map.Tiling = Vector2.one; map.Offset = Vector2.zero; }
            return map;
        }

        private static AtlasMask RawMask(Material m, string property)
        {
            var map = Raw(m, property);
            return map == null ? null : new AtlasMask { Map = map };
        }

        private static Vector2 Vector2Of(Material m, string property)
        {
            var value = AtlasMaterialReader.Vector(m, property, Vector4.zero);
            return new Vector2(value.x, value.y);
        }

        private static AtlasChannel Channel(Material m, string property, int fallback) =>
            Channels[Mathf.Clamp(Int(m, property, fallback), 0, 3)];
        private static bool Has(Material m, string property) => AtlasMaterialReader.Has(m, property);
        private static bool Keyword(Material m, string keyword) => AtlasMaterialReader.Keyword(m, keyword);
        private static float Float(Material m, string property, float fallback) => AtlasMaterialReader.Float(m, property, fallback);
        private static int Int(Material m, string property, int fallback) => Mathf.RoundToInt(AtlasMaterialReader.Float(m, property, fallback));
    }
}
