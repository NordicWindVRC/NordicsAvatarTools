using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Global Mask (the mask bus: textures, vertex colors, modifiers, options) and Global
    // Themes (theme colors, resolved to colors here).
    internal sealed partial class AtlasPoiyomiReader
    {
        private const int BusChannels = 16;

        // The bus's producers, in the shader's order: textures, vertex colors, back face, mirror,
        // camera and distance modifiers, then the options.
        private void MaskBus(Material m, AtlasUnifiedMaterial d)
        {
            var steps = d.MaskBus.Steps;
            if (On(m, "_GlobalMaskTexturesEnable"))
                for (int t = 0; t < 4; t++)
                {
                    var map = Texture(m, d, "_GlobalMaskTexture" + t, AtlasTextureInterpretation.Data);
                    if (map == null) continue;
                    if (On(m, "_GlobalMaskTexture" + t + "Split"))
                        d.Report(AtlasIssueKind.Unmapped, "mask.bus-split", "A global mask texture's split sampling isn't mapped yet; its own tiling is used.");
                    for (int c = 0; c < 4; c++)
                        steps.Add(new AtlasMaskBusStep
                        {
                            Channel = t * 4 + c, Kind = AtlasMaskBusStepKind.Texture,
                            Mask = MaskOptions(m, new AtlasMask { Map = map, Channel = Channels[c] }, "_GlobalMaskTexture" + t, c)
                        });
                }
            Contacts(m, d);

            bool linear = On(m, "_GlobalMaskVertexColorLinearSpace", 1f);
            string[] colors = { "Red", "Green", "Blue", "Alpha" };
            for (int c = 0; c < 4; c++)
            {
                int target = Int(m, "_GlobalMaskVertexColor" + colors[c], 0);
                if (target <= 0) continue;
                steps.Add(new AtlasMaskBusStep
                {
                    Channel = target - 1, Kind = AtlasMaskBusStepKind.VertexColor, VertexChannel = Channels[c], VertexLinear = linear,
                    Blend = BusBlend(Int(m, "_GlobalMaskVertexColor" + colors[c] + "BlendType", 2))
                });
            }

            for (int i = 0; i < BusChannels; i++)
            {
                if (On(m, "_GlobalMaskModifiersBackfaceEnable"))
                {
                    int faces = Int(m, "_GlobalMaskBackface_" + i, 0);
                    if (faces > 0) steps.Add(new AtlasMaskBusStep { Channel = i, Kind = AtlasMaskBusStepKind.Faces, Faces = faces == 1 ? AtlasFaces.Back : AtlasFaces.Front });
                }
                if (On(m, "_GlobalMaskModifiersMirrorEnable"))
                {
                    int mirror = Int(m, "_GlobalMaskMirror_" + i, 0);
                    if (mirror > 0) steps.Add(new AtlasMaskBusStep { Channel = i, Kind = AtlasMaskBusStepKind.Mirror, Inside = mirror == 2 });
                }
                if (On(m, "_GlobalMaskModifiersCameraEnable"))
                {
                    int camera = Int(m, "_GlobalMaskCamera_" + i, 0);
                    if (camera > 0) steps.Add(new AtlasMaskBusStep { Channel = i, Kind = AtlasMaskBusStepKind.Camera, Inside = camera == 2 });
                }
            }
            if (On(m, "_GlobalMaskModifiersMirrorEnable") && Int(m, "_GlobalMaskMirrorVisibilityMode", 1) == 0)
                d.Report(AtlasIssueKind.Note, "mask.bus-mirror", "Generic mirror detection is read as VRChat's.");
            if (On(m, "_GlobalMaskModifiersDistanceEnable"))
                for (int i = 0; i < BusChannels; i++)
                {
                    if (!On(m, "_GlobalMaskDistanceEnable_" + i)) continue;
                    steps.Add(new AtlasMaskBusStep
                    {
                        Channel = i, Kind = AtlasMaskBusStepKind.Distance,
                        PixelPosition = Int(m, "_GlobalMaskDistanceType_" + i, 1) == 1,
                        Min = Float(m, "_GlobalMaskDistanceMin_" + i, 1f), Max = Float(m, "_GlobalMaskDistanceMax_" + i, 2f),
                        MinValue = Float(m, "_GlobalMaskDistanceMinAlpha_" + i, 0f), MaxValue = Float(m, "_GlobalMaskDistanceMaxAlpha_" + i, 1f),
                        Blend = Int(m, "_GlobalMaskDistanceBlendType_" + i, 0) == 2 ? AtlasMaskBlend.Multiply : AtlasMaskBlend.Replace
                    });
                }

            // Blacklights: smoothstep(far, near, distance to a black vertex light whose alpha is the key).
            if (On(m, "_BlackLightMaskingEnabled"))
                for (int b = 0; b < 4; b++)
                {
                    string p = "_BlackLightMasking" + b;
                    int target = Int(m, p + "GlobalMaskIndex", 0);
                    if (target <= 0) continue;
                    var range = AtlasMaterialReader.Vector(m, p + "Range", new Vector4(0.1f, 0.5f, 0f, 0f));
                    steps.Add(new AtlasMaskBusStep
                    {
                        Channel = Mathf.Min(target, BusChannels) - 1, Kind = AtlasMaskBusStepKind.Blacklight,
                        Amount = Float(m, p + "Key", b + 1), Min = range.x, Max = range.y, Blend = BusBlend(Int(m, p + "GlobalMaskBlendType", 2))
                    });
                }

            if (Int(m, "_ShadingRampedLightMapApplyGlobalMaskIndex", 0) > 0 || Int(m, "_ShadingRampedLightMapInverseApplyGlobalMaskIndex", 0) > 0)
                d.Report(AtlasIssueKind.Unmapped, "mask.bus-shading", "Writing the shading into a global mask isn't mapped yet.");

            if (!On(m, "_GlobalMaskOptionsEnable")) return;
            int type = Int(m, "_GlobalMaskOptionsType", 0);
            for (int i = 0; i < BusChannels; i++)
            {
                if (type == 0)
                {
                    float amount = Float(m, "_GlobalMaskSlider_" + i, 0f);
                    if (amount != 0f) steps.Add(new AtlasMaskBusStep { Channel = i, Kind = AtlasMaskBusStepKind.Offset, Amount = amount });
                }
                else if (type == 1)
                {
                    var range = AtlasMaterialReader.Vector(m, "_GlobalMaskMinMaxSlider_" + i, new Vector4(0f, 1f, 0f, 1f));
                    if (range.x != 0f || range.y != 1f) steps.Add(new AtlasMaskBusStep { Channel = i, Kind = AtlasMaskBusStepKind.Remap, Min = range.x, Max = range.y });
                }
                else if (type == 2)
                {
                    if (On(m, "_GlobalMaskToggleOn_" + i)) steps.Add(new AtlasMaskBusStep { Channel = i, Kind = AtlasMaskBusStepKind.ForceOn });
                    if (On(m, "_GlobalMaskToggleOff_" + i)) steps.Add(new AtlasMaskBusStep { Channel = i, Kind = AtlasMaskBusStepKind.ForceOff });
                }
            }
        }

        // Poiyomi's blend selectors use the bus blends' order.
        // 8.1's inspector listed color blends (screen, add, overlay, mixed) the mask blend doesn't
        // have: past 7 the shader gave 0. Read as replace and reported.
        private AtlasMaskBlend BusBlend(int type)
        {
            if (major <= 8 && type > 7) legacyMaskBlend = true;
            return (AtlasMaskBlend)Mathf.Clamp(type, 0, 7);
        }

        // A consumer: the mask reads the channel the selector names (Off, 1R … 4A), combined by its
        // blend. Creates a mask when there's none.
        // Global Mask Contacts (Poiyomi 11): up to 8 VRChat contacts, each writing its touch into a channel.
        private void Contacts(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_GlobalMaskContactsEnable")) return;
            bool themed = false;
            for (int i = 0; i < 8; i++)
            {
                string s = "_" + i;
                if (!On(m, "_GlobalMaskContactEnable" + s)) continue;
                themed |= Int(m, "_GlobalMaskContactThemeColor" + s, 0) > 0;
                int target = Int(m, "_GlobalMaskContactTarget" + s, 0);
                if (target <= 0) continue;
                var tags = AtlasMaterialReader.Vector(m, "_GlobalMaskContactTag" + s, Vector4.zero);
                var contact = new AtlasContact
                {
                    People = (AtlasContactPeople)Mathf.Clamp(Int(m, "_GlobalMaskContactPeople" + s, 0), 0, 2),
                    InnerRadius = Float(m, "_GlobalMaskContactRadius" + s, 0.05f),
                    OuterRadius = Float(m, "_GlobalMaskContactFalloff" + s, 0.15f),
                    ScaleWithToucher = On(m, "_GlobalMaskContactToucherScale" + s),
                    AroundTouch = Int(m, "_GlobalMaskContactPosition" + s, 1) == 1,
                    Invert = On(m, "_GlobalMaskContactInvert" + s)
                };
                for (int g = 0; g < 4; g++) contact.Tags[g] = Mathf.RoundToInt(tags[g]);
                d.MaskBus.Steps.Add(new AtlasMaskBusStep
                {
                    Channel = Mathf.Min(target, BusChannels) - 1, Kind = AtlasMaskBusStepKind.Contact, Contact = contact,
                    Blend = BusBlend(Int(m, "_GlobalMaskContactBlendType" + s, 2))
                });
            }
            if (themed)
                d.Report(AtlasIssueKind.Unmapped, "contact.person-color", "A contact tinting a theme color by who touches isn't mapped yet.");
        }

        private AtlasMask BusMask(Material m, AtlasMask mask, string selector, string blend = null)
        {
            int channel = Int(m, selector, 0);
            if (channel <= 0) return mask;
            mask = mask ?? new AtlasMask();
            mask.Bus = Mathf.Min(channel, BusChannels) - 1;
            mask.BusBlend = BusBlend(Int(m, blend ?? selector + "BlendType", 2));
            return mask;
        }

        private void BusScalar(Material m, AtlasScalarInput input, string selector, string blend = null)
        {
            int channel = Int(m, selector, 0);
            if (channel <= 0) return;
            input.Bus = Mathf.Min(channel, BusChannels) - 1;
            input.BusBlend = BusBlend(Int(m, blend ?? selector + "BlendType", 2));
        }

        // Every layer added since `from` reads the channel.
        private void BusLayers(Material m, AtlasUnifiedMaterial d, int from, string selector, string blend = null)
        {
            for (int i = from; i < d.Layers.Count; i++) d.Layers[i].Mask = BusMask(m, d.Layers[i].Mask, selector, blend);
        }

        // A producer: the layer's coverage written into the channel the selector names.
        private void BusWrite(Material m, AtlasUnifiedMaterial d, AtlasLayer layer, string selector, string blend, bool invert = false)
        {
            int channel = Int(m, selector, 0);
            if (channel <= 0 || layer == null) return;
            d.MaskBus.Steps.Add(new AtlasMaskBusStep
            {
                Channel = Mathf.Min(channel, BusChannels) - 1, Kind = AtlasMaskBusStepKind.LayerCoverage, Layer = layer, Blend = BusBlend(Int(m, blend, 2)), InvertCoverage = invert
            });
        }

        // A color, or the theme color its selector names: Theme Color 0–3 are resolved here;
        // ColorChord 0–3 and AudioLink themes 0–3 go on the color input when there is one.
        private Color TintColor(Material m, AtlasUnifiedMaterial d, string property, string themeIndex, AtlasColorInput input = null)
        {
            var color = LinearColor(m, property, Color.white);
            int theme = Int(m, themeIndex, 0);
            if (theme <= 0) return color;
            if (theme > 4)
            {
                // Without AudioLink the shader uses the color as is.
                if (!AudioLinkOn(m)) return color;
                if (input != null) input.AudioTheme = Mathf.Min(theme - 5, 7);
                else
                    d.Report(AtlasIssueKind.Unmapped, "palette.audiolink", "A ColorChord or AudioLink theme color here isn't mapped yet.");
                return color;
            }
            var resolved = ThemeColor(m, d, theme - 1);
            return new Color(resolved.r, resolved.g, resolved.b, color.a);
        }

        // A theme color as the shader computes it: the HDR color split into color and exposure,
        // hue shifted (Oklab or HSV) and saturated (old: HSV saturation and value offsets; new:
        // saturation and brightness as exposure), normalized to 1, the exposure applied.
        private Color ThemeColor(Material m, AtlasUnifiedMaterial d, int index)
        {
            string i = index.ToString();
            var hdr = LinearColor(m, "_GlobalThemeColor" + i, Color.white);
            var c = new Vector3(hdr.r, hdr.g, hdr.b);
            float max = Mathf.Max(c.x, Mathf.Max(c.y, c.z));
            float exposure = 0f;
            if (max > 1f) { exposure = Mathf.Log(max, 2f); c /= max; }
            if (Float(m, "_GlobalThemeHueSpeed" + i, 0f) != 0f)
                d.Report(AtlasIssueKind.Unmapped, "palette.hue-speed", "A theme color's hue speed isn't mapped yet; its color at time 0 is used.");
            float hue = Float(m, "_GlobalThemeHue" + i, 0f);
            bool hsv = Int(m, "_GlobalThemeHueShiftColorSpace" + i, 0) == 1;
            if (Int(m, "_GlobalThemeSaturationMethod" + i, 1) == 0)
            {
                c = AtlasColorMath.ModifyHsv(c, Float(m, "_GlobalThemeSaturation" + i, 0f), Float(m, "_GlobalThemeValue" + i, 0f));
                c = AtlasColorMath.HueShift(c, hue, hsv);
            }
            else
            {
                c = Vector3.Max(Vector3.zero, AtlasColorMath.HueShift(c, hue, hsv));
                c = AtlasColorMath.ThemeSaturation(c, Float(m, "_GlobalThemeSaturation" + i, 0f));
                exposure += Mathf.Log(Mathf.Max(Float(m, "_GlobalThemeBrightness" + i, 0f) + 1f, 0.0001f), 2f);
            }
            c = Vector3.Max(Vector3.zero, c);
            c /= Mathf.Max(1f, Mathf.Max(c.x, Mathf.Max(c.y, c.z)));
            c *= Mathf.Pow(2f, exposure);
            return new Color(c.x, c.y, c.z, hdr.a);
        }
    }
}
