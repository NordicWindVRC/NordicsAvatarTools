using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Vertex Options (Basics & Fun, LookAt, Glitching), Depth Bulge and View Clip
    // Prevention as deformations (catalogue 5.3), in the order Poiyomi applies them.
    internal sealed partial class AtlasPoiyomiReader
    {
        private void Vertex(Material m, AtlasUnifiedMaterial d)
        {
            VertexBasics(m, d);
            VertexLookAt(m, d);
            VertexGlitch(m, d);
            DepthBulge(m, d);
            CameraPush(m, d);
            ProDeformations(m, d);
        }

        private static readonly string[] LookAtRigs = { "Red", "Green", "Blue", "Alpha" };

        private void VertexLookAt(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_VertexLookAtEnabled")) return;
            var masks = Texture(m, d, "_LookAtMask", AtlasTextureInterpretation.Data);
            var lookAt = new AtlasDeformation { Kind = AtlasDeformationKind.LookAt };
            for (int i = 0; i < 4; i++)
            {
                string r = "_LookAt" + LookAtRigs[i];
                // Red is on by default.
                if (!On(m, r + "MaskEnabled", i == 0 ? 1f : 0f)) continue;
                lookAt.Rigs.Add(new AtlasLookAtRig
                {
                    Mask = MaskOptions(m, masks == null ? null : new AtlasMask { Map = masks, Channel = Channels[i] }, "_LookAtMask", i),
                    Strength = Float(m, r + "Alpha", 1f),
                    TargetUp = (AtlasLookAtUp)Mathf.Clamp(Int(m, r + "TargetUp", 0), 0, 3),
                    Forward = Vector3Of(m, r + "ForwardDirection", Vector3.forward),
                    Up = Vector3Of(m, r + "UpDirection", Vector3.up),
                    Pivot = Vector3Of(m, r + "PivotOffset", Vector3.zero),
                    Origin = Vector3Of(m, r + "OriginOffset", Vector3.zero),
                    DistanceFalloff = AtlasMaterialReader.Vector(m, r + "DistanceFalloff", Vector4.zero),
                    FovFalloff = AtlasMaterialReader.Vector(m, r + "FoV", Vector4.zero),
                    Pitch = AtlasMaterialReader.Vector(m, r + "MaxPitchAngle", new Vector4(-180f, 180f, -180f, 180f)),
                    Yaw = AtlasMaterialReader.Vector(m, r + "MaxYawAngle", new Vector4(-180f, 180f, -180f, 180f)),
                    Roll = AtlasMaterialReader.Vector(m, r + "MaxRollAngle", new Vector4(-180f, 180f, -180f, 180f))
                });
            }
            if (lookAt.Rigs.Count == 0) return;
            if (On(m, "_LookAtVisibilityEnabled"))
            {
                lookAt.Visibility = new AtlasVisibility();
                ReadVisibility(m, lookAt.Visibility, "_LookAtVisibilityMode", "_LookAtVisibilityMirror", "_LookAtVisibilityVRC");
            }
            int k = AddDeformation(d, lookAt);
            if (AudioLinkOn(m) && On(m, "_VertexLookAtAudioLinkEnabled"))
                AudioRange(m, d, "deformations[" + k + "].strength", AtlasModulatorMode.Add, "_VertexLookAtAudioLinkBand", "_VertexLookAtAlphaAudiolink");
        }

        private void VertexGlitch(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_VertexGlitchingEnabled")) return;
            bool useMap = On(m, "_VertexGlitchUseMap");
            var map = useMap ? AtlasSourceMapping.Texture(m, "_VertexGlitchMap", AtlasTextureInterpretation.Data) : null;
            // A white map cancels itself out: nothing moves.
            if (useMap && map == null) return;
            var glitch = new AtlasDeformation
            {
                Kind = AtlasDeformationKind.Glitch,
                Amount = new Vector3(Float(m, "_VertexGlitchStrength", 1f), 0f, 0f),
                Frequency = Float(m, "_VertexGlitchFrequency", 1f),
                Threshold = Float(m, "_VertexGlitchThreshold", 1f),
                Map = map,
                Density = Float(m, "_VertexGlitchDensity", 10f),
                Speed = Float(m, "_VertexGlitchMapPanSpeed", 10f),
                Mirror = On(m, "_VertexGlitchMirrorEnable") ? (AtlasMirrorVisibility)Mathf.Clamp(Int(m, "_VertexGlitchMirror", 0), 0, 2) : AtlasMirrorVisibility.Both,
                Mask = Mask(m, d, "_VertexGlitchingMask")
            };
            if (AudioLinkOn(m) && On(m, "_VertexGlitchingAudioLinkEnabled"))
            {
                glitch.AudioBand = Mathf.Clamp(Int(m, "_VertexGlitchingAudioLinkBand", 0), 0, 4);
                glitch.AudioOverride = On(m, "_VertexGlitchingAudiolinkOverride", 1f);
            }
            AddDeformation(d, glitch);
        }

        private void DepthBulge(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_EnableDepthBulge")) return;
            AddDeformation(d, new AtlasDeformation
            {
                Kind = AtlasDeformationKind.DepthBulge,
                Amount = new Vector3(Float(m, "_DepthBulgeHeight", 0.02f), 0f, 0f),
                FadeLength = Float(m, "_DepthBulgeFadeLength", 0.02f),
                Mask = Mask(m, d, "_DepthBulgeMask", Channels[Mathf.Clamp(Int(m, "_DepthBulgeMaskChannel", 0), 0, 3)])
            });
        }

        // View Clip Prevention (Uzumore); its animation toggle off is off.
        private void CameraPush(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_UzumoreCategoryToggle") || !On(m, "_UzumoreEnabled", 1f)) return;
            AddDeformation(d, new AtlasDeformation
            {
                Kind = AtlasDeformationKind.CameraPush,
                Amount = new Vector3(Float(m, "_UzumoreAmount", 0.1f), 0f, 0f),
                Bias = Float(m, "_UzumoreBias", 0.001f),
                ObjectScale = Float(m, "_UzumoreObjectScale", 1f),
                Mask = Mask(m, d, "_UzumoreMask", Channels[Mathf.Clamp(Int(m, "_UzumoreMaskChannel", 3), 0, 3)])
            });
        }
        private void VertexBasics(Material m, AtlasUnifiedMaterial d)
        {
            if (!On(m, "_VertexManipulationsEnabled")) return;
            // The effects mask; its UV property has no leading underscore.
            var masks = AtlasSourceMapping.Texture(m, "_VertexBasicsMask", AtlasTextureInterpretation.Data);
            if (masks != null)
            {
                masks.Space = UVSpace(m, d, "VertexBasicsMaskUV");
                var pan = AtlasMaterialReader.Vector(m, "_VertexBasicsMaskUVPan", Vector4.zero);
                masks.Scroll = new Vector2(pan.x, pan.y) * 0.05f;
            }
            bool audio = AudioLinkOn(m) && On(m, "_VertexAudioLinkEnabled");
            var basics = VertexMask(m, masks, "_VertexBasicsMaskChannel");

            // Scale, rotation and translation, each value × its w.
            var scale = AtlasMaterialReader.Vector(m, "_VertexManipulationLocalScale", Vector4.one);
            var scaleMin = AtlasMaterialReader.Vector(m, "_VertexLocalScaleALMin", Vector4.zero);
            var scaleMax = AtlasMaterialReader.Vector(m, "_VertexLocalScaleALMax", Vector4.zero);
            bool scaleAudio = audio && ((Vector3)scaleMin != Vector3.zero || (Vector3)scaleMax != Vector3.zero);
            if ((Vector3)scale * scale.w != Vector3.one || scaleAudio)
            {
                int k = AddDeformation(d, new AtlasDeformation { Kind = AtlasDeformationKind.Scale, Amount = (Vector3)scale * scale.w, Mask = basics });
                if (scaleAudio)
                    AudioVector(m, d, k, Int(m, "_VertexLocalScaleALBand", 0),
                        ((Vector3)scaleMin + Vector3.one * scaleMin.w) * scale.w, ((Vector3)scaleMax + Vector3.one * scaleMax.w) * scale.w);
            }

            var rotation = AtlasMaterialReader.Vector(m, "_VertexManipulationLocalRotation", new Vector4(0f, 0f, 0f, 1f));
            var spin = (Vector3)AtlasMaterialReader.Vector(m, "_VertexManipulationLocalRotationSpeed", Vector4.zero);
            var rotationAudio = (Vector3)AtlasMaterialReader.Vector(m, "_VertexLocalRotationAL", Vector4.zero);
            var chrono = (Vector3)AtlasMaterialReader.Vector(m, "_VertexLocalRotationCTALSpeed", Vector4.zero);
            bool rotates = audio && (rotationAudio != Vector3.zero || chrono != Vector3.zero);
            if ((Vector3)rotation != Vector3.zero || spin != Vector3.zero || rotates)
            {
                // Poiyomi turns by speed × t / 20.
                int k = AddDeformation(d, new AtlasDeformation
                {
                    Kind = AtlasDeformationKind.RotateAxes, Amount = (Vector3)rotation * rotation.w, Spin = spin * 0.05f * rotation.w, Mask = basics
                });
                if (rotates)
                {
                    string path = "deformations[" + k + "].amount.";
                    string[] axes = { "x", "y", "z" }, names = { "X", "Y", "Z" };
                    for (int i = 0; i < 3; i++)
                    {
                        if (rotationAudio[i] != 0f)
                            d.Modulators.Add(AudioModulator(m, path + axes[i], AtlasModulatorMode.Add, Int(m, "_VertexLocalRotationALBand" + names[i], 0), 0f, rotationAudio[i] * rotation.w));
                        AudioChrono(m, d, path + axes[i], "_VertexLocalRotationCTALBand" + names[i], "_VertexLocalRotationCTALType" + names[i], chrono[i] * 360f * rotation.w);
                    }
                }
            }

            var translation = AtlasMaterialReader.Vector(m, "_VertexManipulationLocalTranslation", new Vector4(0f, 0f, 0f, 1f));
            var translationMin = (Vector3)AtlasMaterialReader.Vector(m, "_VertexLocalTranslationALMin", Vector4.zero);
            var translationMax = (Vector3)AtlasMaterialReader.Vector(m, "_VertexLocalTranslationALMax", Vector4.zero);
            bool translationAudio = audio && (translationMin != Vector3.zero || translationMax != Vector3.zero);
            if ((Vector3)translation != Vector3.zero || translationAudio)
            {
                int k = AddDeformation(d, new AtlasDeformation { Kind = AtlasDeformationKind.Translate, Amount = (Vector3)translation * translation.w, Mask = basics });
                if (translationAudio)
                    AudioVector(m, d, k, Int(m, "_VertexLocalTranslationALBand", 0), translationMin * translation.w, translationMax * translation.w);
            }

            if (On(m, "_VertexBarrelMode"))
                AddDeformation(d, new AtlasDeformation
                {
                    Kind = AtlasDeformationKind.Barrel, Radius = Float(m, "_VertexBarrelWidth", 0.2f), Height = Float(m, "_VertexBarrelHeight", 0f),
                    Strength = Float(m, "_VertexBarrelAlpha", 0f), Mask = VertexMask(m, masks, "_VertexBarrelMaskChannel")
                });
            if (On(m, "_VertexSphereMode"))
                AddDeformation(d, new AtlasDeformation
                {
                    Kind = AtlasDeformationKind.Sphere, Radius = Float(m, "_VertexSphereRadius", 1f), Height = Float(m, "_VertexSphereHeight", 1f),
                    Centre = Vector3Of(m, "_VertexSphereCenter", Vector3.zero), Strength = Float(m, "_VertexSphereAlpha", 0f),
                    Mask = VertexMask(m, masks, "_VertexSphereMaskChannel")
                });
            if (On(m, "_VertexTornadoMode"))
                AddDeformation(d, new AtlasDeformation
                {
                    Kind = AtlasDeformationKind.Tornado, Radius = Float(m, "_VertexTornadoRadius", 1f), Speed = Float(m, "_VertexTornadoSpeed", 5f),
                    Twist = Float(m, "_VertexTornadoIntensity", 100f), Funnel = Float(m, "_VertexTornadoFunnelShape", 1f),
                    Pull = Float(m, "_VertexTornadoVerticalPull", 0f),
                    HeightRange = new Vector2(Float(m, "_VertexTornadoBaseHeight", 0f), Float(m, "_VertexTornadoTopHeight", 1f)),
                    Mask = VertexMask(m, masks, "_VertexTornadoMaskChannel")
                });
            if (On(m, "_VertexWindEnabled"))
                AddDeformation(d, new AtlasDeformation
                {
                    Kind = AtlasDeformationKind.Wind,
                    Wind = new AtlasWind
                    {
                        Enabled = true,
                        PrimaryDirection = Vector3Of(m, "_VertexWindPrimaryDirection", Vector3.right),
                        PrimaryAmplitude = Float(m, "_VertexWindPrimaryAmplitude", 0.1f),
                        PrimarySpeed = Float(m, "_VertexWindPrimarySpeed", 1f),
                        PrimaryFrequency = Float(m, "_VertexWindPrimaryFrequency", 1f),
                        DetailDirection = Vector3Of(m, "_VertexWindDetailDirection", Vector3.up),
                        DetailAmplitude = Float(m, "_VertexWindDetailAmplitude", 0.05f),
                        DetailSpeed = Float(m, "_VertexWindDetailSpeed", 2.5f),
                        DetailFrequency = Float(m, "_VertexWindDetailFrequency", 5f),
                        // The effects mask, sampled at the world position.
                        Noise = masks,
                        NoiseChannel = Channels[Mathf.Clamp(Int(m, "_VertexWindNoiseChannel", 0), 0, 3)],
                        NoiseStrength = masks == null ? 0f : Float(m, "_VertexWindNoiseStrength", 0.5f),
                        NoiseScale = Float(m, "_VertexWindNoiseScale", 10f),
                        NoiseSpeed = Float(m, "_VertexWindNoiseSpeed", 0.5f)
                    },
                    Mask = VertexMask(m, masks, "_VertexWindMaskChannel")
                });

            var world = (Vector3)AtlasMaterialReader.Vector(m, "_VertexManipulationWorldTranslation", Vector4.zero);
            var worldMin = (Vector3)AtlasMaterialReader.Vector(m, "_VertexWorldTranslationALMin", Vector4.zero);
            var worldMax = (Vector3)AtlasMaterialReader.Vector(m, "_VertexWorldTranslationALMax", Vector4.zero);
            bool worldAudio = audio && (worldMin != Vector3.zero || worldMax != Vector3.zero);
            if (world != Vector3.zero || worldAudio)
            {
                int k = AddDeformation(d, new AtlasDeformation { Kind = AtlasDeformationKind.TranslateWorld, Amount = world, Mask = basics });
                if (worldAudio) AudioVector(m, d, k, Int(m, "_VertexWorldTranslationALBand", 0), worldMin, worldMax);
            }

            float height = Float(m, "_VertexManipulationHeight", 0f);
            var heightAudio = AtlasMaterialReader.Vector(m, "_VertexManipulationHeightAL", Vector4.zero);
            bool heightModulated = audio && (heightAudio.x != 0f || heightAudio.y != 0f);
            if (height != 0f || heightModulated)
            {
                int k = AddDeformation(d, new AtlasDeformation
                {
                    Kind = AtlasDeformationKind.Displace, Amount = new Vector3(height, 0f, 0f),
                    Map = Texture(m, d, "_VertexManipulationHeightMask", AtlasTextureInterpretation.Data),
                    MapChannel = Channels[Mathf.Clamp(Int(m, "_VertexManipulationHeightMapChannel", 0), 0, 3)],
                    Bias = Float(m, "_VertexManipulationHeightBias", 0f),
                    Mask = VertexMask(m, masks, "_VertexManipulationHeightMaskChannel")
                });
                if (heightModulated)
                    d.Modulators.Add(AudioModulator(m, "deformations[" + k + "].amount.x", AtlasModulatorMode.Add, Int(m, "_VertexManipulationHeightBand", 0), heightAudio.x, heightAudio.y));
            }

            if (On(m, "_VertexRoundingEnabled"))
            {
                float interval = Mathf.Max(Float(m, "_VertexRoundingDivision", 0.02f), 0.0000001f);
                AddDeformation(d, new AtlasDeformation
                {
                    Kind = AtlasDeformationKind.Snap, Precision = 1f / interval, Centred = true,
                    Local = Int(m, "_VertexRoundingSpace", 0) == 1, Mask = VertexMask(m, masks, "_VertexRoundingMaskChannel")
                });
                var range = AtlasMaterialReader.Vector(m, "_VertexRoundingRangeAL", Vector4.zero);
                if (audio && (range.x != 0f || range.y != 0f))
                    d.Report(AtlasIssueKind.Unmapped, "audiolink.vertex-rounding", "Vertex rounding's interval by AudioLink isn't mapped yet.");
            }
            if (audio && On(m, "_VertexSpectrumMotion"))
                d.Report(AtlasIssueKind.Unmapped, "audiolink.vertex-spectrum", "Vertices moved by AudioLink's spectrum aren't mapped yet.");
        }

        private int AddDeformation(AtlasUnifiedMaterial d, AtlasDeformation deformation)
        {
            d.Deformations.Add(deformation);
            return d.Deformations.Count - 1;
        }

        // amount.x, .y and .z + lerp(min, max, band), for the axes whose range moves.
        private void AudioVector(Material m, AtlasUnifiedMaterial d, int deformation, int band, Vector3 min, Vector3 max)
        {
            string[] axes = { "x", "y", "z" };
            for (int i = 0; i < 3; i++)
                if (min[i] != 0f || max[i] != 0f)
                    d.Modulators.Add(AudioModulator(m, "deformations[" + deformation + "].amount." + axes[i], AtlasModulatorMode.Add, band, min[i], max[i]));
        }

        // A channel of the effects mask (0–3) or of the vertex colour (4–7); null when the map's
        // channel is chosen without a map.
        private AtlasMask VertexMask(Material m, AtlasTextureReference masks, string channel)
        {
            int c = Mathf.Clamp(Int(m, channel, 0), 0, 7);
            if (c >= 4) return new AtlasMask { VertexColor = true, Channel = Channels[c - 4] };
            return MaskOptions(m, masks == null ? null : new AtlasMask { Map = masks, Channel = Channels[c] }, "_VertexBasicsMask", c);
        }
    }
}
