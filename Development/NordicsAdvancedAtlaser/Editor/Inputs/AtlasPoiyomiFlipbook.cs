using System.Linq;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: Flipbooks (2), Mirror/Camera Visibility, UV Tile Discard.
    public abstract partial class AtlasPoiyomiParser
    {
        // Poiyomi's decal placement: the scale fitted to the texture's aspect (1 shrink, 2 grow),
        // then the side offsets (left, right, down, up) widen it and move its centre.
        private static AtlasDecal Placed(AtlasTextureReference map, Vector2 scale, int aspect, Vector4 side, Vector2 position)
        {
            if (aspect > 0 && map.Texture.width > 0 && map.Texture.height > 0)
            {
                float w = map.Texture.width, h = map.Texture.height;
                bool shrink = aspect == 1;
                if (w > h) scale = shrink ? new Vector2(scale.x, scale.y * h / w) : new Vector2(scale.x * w / h, scale.y);
                else scale = shrink ? new Vector2(scale.x * w / h, scale.y) : new Vector2(scale.x, scale.y * h / w);
            }
            return new AtlasDecal
            {
                Centre = position + new Vector2(side.y - side.x, side.w - side.z) * 0.5f,
                Scale = scale + new Vector2(side.x + side.y, side.z + side.w)
            };
        }

        private static void Flipbooks(Material m, AtlasUnifiedMaterial d)
        {
            Flipbook(m, d, "");
            Flipbook(m, d, "1");
        }

        // A placed texture array played as frames, blended into the base colour by its alpha.
        private static void Flipbook(Material m, AtlasUnifiedMaterial d, string s)
        {
            if (!On(m, "_EnableFlipbook" + s)) return;
            // Without an array nothing is drawn.
            var map = Texture(m, d, "_FlipbookTexArray" + s);
            if (map == null) return;
            var decal = Placed(map, AtlasMaterialReader.Vector(m, "_FlipbookScale" + s, Vector4.one), Int(m, "_FlipbookAspectRatioMode" + s, 0),
                AtlasMaterialReader.Vector(m, "_FlipbookSideOffset" + s, Vector4.zero),
                AtlasMaterialReader.Vector(m, "_FlipbookPosition" + s, new Vector4(0.5f, 0.5f, 0f, 0f)));
            decal.Rotation = Float(m, "_FlipbookRotation" + s, 0f);
            // Poiyomi turns by speed × 2t.
            decal.Spin = Float(m, "_FlipbookRotationSpeed" + s, 0f) * 2f;
            // Tiled seamless or as sprites repeats the placed area.
            decal.ClipOutside = Int(m, "_FlipbookTiled" + s, 0) == 0;
            decal.Symmetry = (AtlasDecalSymmetry)Mathf.Clamp(Int(m, "_FlipbookSymmetryMode" + s, 0), 0, 2);
            decal.Mirror = (AtlasDecalMirror)Mathf.Clamp(Int(m, "_FlipbookMirroredUVMode" + s, 0), 0, 4);
            decal.Depth = Float(m, "_FlipbookDepth" + s, 0f);
            decal.FlipInMirror = On(m, "_FlipbookFlipInMirror" + s);
            map.Decal = decal;
            map.Frames = FlipbookFrames(m, map, s);

            var layer = new AtlasSurfaceLayer
            {
                SourceAlphaIsOpacity = true,
                SourceIntensityIsAlpha = On(m, "_FlipbookIntensityControlsAlpha" + s),
                SourceMapIsMask = On(m, "_FlipbookColorReplaces" + s),
                EmissionAmount = Float(m, "_FlipbookEmissionStrength" + s, 0f)
            };
            layer.Source.Color = TintColor(m, d, "_FlipbookColor" + s, "_FlipbookColorThemeIndex" + s, layer.Source);
            layer.Source.Map = map;
            layer.Mask = BusMask(m, Mask(m, d, "_FlipbookMask" + s, Channels[Mathf.Clamp(Int(m, "_FlipbookMaskChannel" + s, 0), 0, 3)]),
                "_FlipbookMaskGlobalMask" + s, "_FlipbookMaskGlobalMaskBlendType" + s);
            layer.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.BaseColor, PoiyomiBlend(Int(m, "_FlipbookBlendType" + s, 0)), Float(m, "_FlipbookAlpha" + s, 1f)));
            if (On(m, "_FlipbookUseVideoEffects" + s))
                layer.Screen = d.Layers.OfType<AtlasPatternLayer>().Select(l => l.VideoScreen).FirstOrDefault(v => v != null);
            d.Layers.Add(layer);
            string path = "layers[" + (d.Layers.Count - 1) + "]";

            if (Int(m, "_FlipbookAlphaControlsFinalAlpha" + s, 0) > 0)
                d.Report(AtlasIssueKind.Unmapped, "flipbook.alpha-override", "A flipbook replacing the material's alpha (and colour) isn't mapped yet.");
            if (On(m, "_FlipbookHueShiftEnabled" + s))
                Hue(m, d, AtlasAdjustmentTarget.Layer, layer, "_FlipbookHueShift" + s, "_FlipbookHueShiftSpeed" + s,
                    "_FlipbookHueShiftColorSpace" + s, "_FlipbookHueSelectOrShift" + s);

            if (!AudioLinkOn(m)) return;
            var scale = AtlasMaterialReader.Vector(m, "_AudioLinkFlipbookScale" + s, Vector4.zero);
            int scaleBand = Int(m, "_AudioLinkFlipbookScaleBand" + s, 0);
            if (scale.x != 0f || scale.z != 0f) d.Modulators.Add(AudioModulator(m, path + ".source.map.decal.scale.x", AtlasModulatorMode.Add, scaleBand, scale.x, scale.z));
            if (scale.y != 0f || scale.w != 0f) d.Modulators.Add(AudioModulator(m, path + ".source.map.decal.scale.y", AtlasModulatorMode.Add, scaleBand, scale.y, scale.w));
            AudioRange(m, d, path + ".targets[0].opacity", AtlasModulatorMode.Add, "_AudioLinkFlipbookAlphaBand" + s, "_AudioLinkFlipbookAlpha" + s);
            AudioRange(m, d, path + ".emissionAmount", AtlasModulatorMode.Add, "_AudioLinkFlipbookEmissionBand" + s, "_AudioLinkFlipbookEmission" + s);
            AudioRange(m, d, path + ".source.map.frames.offset", AtlasModulatorMode.Add, "_AudioLinkFlipbookFrameBand" + s, "_AudioLinkFlipbookFrame" + s);
            if (On(m, "_FlipbookChronotensityEnabled" + s))
                AudioChrono(m, d, path + ".source.map.frames.offset", "_FlipbookChronotensityBand" + s, "_FlipbookChronoType" + s, Float(m, "_FlipbookChronotensitySpeed" + s, 0f));
            AudioColorOverride(m, d, path + ".source.color", "_FlipbookALColorOverride", s);
        }

        // frame = (t × FPS + offset) mod count (+ start), or the current frame mod count when manual;
        // with start and end frames, count = min(max(start, end), slices) − start.
        private static AtlasFrames FlipbookFrames(Material m, AtlasTextureReference map, string s)
        {
            int slices = map.Texture is Texture2DArray array ? array.depth : 0;
            var frames = new AtlasFrames
            {
                Source = AtlasFrameSource.TextureArray,
                Rate = Float(m, "_FlipbookFPS" + s, 30f),
                Offset = Float(m, "_FlipbookFrameOffset" + s, 0f),
                Crossfade = On(m, "_FlipbookCrossfadeEnabled" + s),
                CrossfadeRange = AtlasMaterialReader.Vector(m, "_FlipbookCrossfadeRange" + s, new Vector4(0.75f, 1f, 0f, 1f))
            };
            int count = slices;
            if (On(m, "_FlipbookStartAndEnd" + s))
            {
                int start = Mathf.RoundToInt(Float(m, "_FlipbookStartFrame" + s, 0f)), end = Mathf.RoundToInt(Float(m, "_FlipbookEndFrame" + s, 0f));
                int last = slices > 0 ? Mathf.Min(Mathf.Max(start, end), slices) : Mathf.Max(start, end);
                frames.First = Mathf.Max(0, start);
                count = Mathf.Max(0, last - frames.First);
                frames.Count = count;
            }
            if (On(m, "_FlipbookManualFrameControl" + s))
            {
                // The current frame isn't moved by the start frame.
                float current = Float(m, "_FlipbookCurrentFrame" + s, 0f);
                frames.Fixed = true;
                frames.FixedFrame = count > 0 ? current % count : current;
            }
            else if (frames.Rate == 0f)
            {
                frames.Fixed = true;
                frames.FixedFrame = 0f;
            }
            return frames;
        }

        // Mirror/Camera Visibility: where the material is drawn, and a texture and colour in mirrors.
        // UV Tile Discard: hidden tiles, everywhere or on one side.
        private static void Visibility(Material m, AtlasUnifiedMaterial d)
        {
            if (On(m, "_EnableMirrorOptions"))
            {
                ReadVisibility(m, d.Render.Visibility, "_VisibilityMode", "_Mirror", "_VisibilityVRC");
                if (On(m, "_MirrorTextureEnabled")) MirrorTexture(m, d);
            }

            if (On(m, "_EnableUDIMDiscardOptions"))
                TileDiscard(m, d, "_UDIMDiscard", AtlasFaces.Both);
            if (On(m, "_EnableUDIMFaceDiscardOptions"))
                // Face 0 discards back faces, 1 front faces.
                TileDiscard(m, d, "_UDIMFaceDiscard", Int(m, "_UDIMFaceDiscardFace", 0) == 1 ? AtlasFaces.Front : AtlasFaces.Back);
        }

        // The VRChat toggles (mode 1: normal, mirror VR and desktop, camera VR and desktop,
        // screenshot) or the generic mirror choice (both, mirror only, not in mirror).
        private static void ReadVisibility(Material m, AtlasVisibility visibility, string mode, string mirror, string vrc)
        {
            if (Int(m, mode, 1) == 1)
            {
                visibility.Normal = On(m, vrc + "Regular", 1f);
                visibility.MirrorVR = On(m, vrc + "MirrorVR", 1f);
                visibility.MirrorDesktop = On(m, vrc + "MirrorDesktop", 1f);
                visibility.CameraVR = On(m, vrc + "CameraVR", 1f);
                visibility.CameraDesktop = On(m, vrc + "CameraDesktop", 1f);
                visibility.Screenshot = On(m, vrc + "CameraScreenshot", 1f);
                return;
            }
            int choice = Int(m, mirror, 0);
            if (choice == 1) visibility.MirrorOnly();
            else if (choice == 2) visibility.NotInMirror();
        }

        // In mirrors: lerp(base, blend(base, texture), texture alpha × colour alpha), then
        // × lerp(1, colour, colour alpha): two layers shown only in mirrors (everywhere when forced).
        private static void MirrorTexture(Material m, AtlasUnifiedMaterial d)
        {
            var mirror = On(m, "_MirrorTextureForceEnabled") ? AtlasMirrorVisibility.Both : AtlasMirrorVisibility.MirrorOnly;
            var color = TintColor(m, d, "_MirrorColor", "_MirrorColorThemeIndex");
            var map = Texture(m, d, "_MirrorTexture");
            if (map != null)
            {
                var texture = new AtlasSurfaceLayer { Mirror = mirror, SourceAlphaIsOpacity = true };
                texture.Source.Map = map;
                texture.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.BaseColor, PoiyomiBlend(Int(m, "_MirrorTextureBlendType", 0)), color.a));
                d.Layers.Add(texture);
            }
            var tint = new AtlasSurfaceLayer { Mirror = mirror };
            tint.Source.Color = new Color(color.r, color.g, color.b, 1f);
            tint.Targets.Add(new AtlasLayerTarget(AtlasSurfaceTarget.BaseColor, AtlasBlend.Multiply, color.a));
            d.Layers.Add(tint);
        }

        // A tile is hidden when its value (rows 0–3, columns 0–3) is above 0.001; vertex mode hides
        // the triangles touching a hidden vertex, pixel mode the pixels: the same for UV islands
        // moved whole into a tile.
        private static void TileDiscard(Material m, AtlasUnifiedMaterial d, string prefix, AtlasFaces faces)
        {
            int tiles = 0;
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    if (Float(m, prefix + "Row" + row + "_" + column, 0f) > 0.001f) tiles |= 1 << (row * 4 + column);
            d.Render.HiddenTiles.Add(new AtlasTileDiscard
            {
                Space = AtlasTextureSpace.UV0 + Mathf.Clamp(Int(m, prefix + "UV", 0), 0, 3),
                Tiles = tiles,
                Faces = faces
            });
        }
    }
}
