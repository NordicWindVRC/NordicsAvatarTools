using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>Reads the fur pass of Poiyomi's Lil Fur variants into the unified fur definition.</summary>
    internal static class AtlasPoiyomiFurParser
    {
        // The fur variants always draw fur; their fur properties mark them.
        // Lil Fur (strands) has _FurLayerNum; Poiyomi Pro Fur (shells) has _FurLayerCount.
        public static bool HasFur(Material m) =>
            AtlasMaterialReader.Has(m, "_FurLayerNum") || AtlasMaterialReader.Has(m, "_FurLayerCount");

        public static void Populate(Material m, AtlasUnifiedMaterial d)
        {
            if (AtlasMaterialReader.Has(m, "_FurLayerCount")) { PopulateShells(m, d); return; }
            if (!HasFur(m)) return;
            var fur = d.Fur;
            fur.Enabled = true;

            Vector4 vector = Vector(m, "_FurVector", new Vector4(0f, 0f, 1f, 0.02f));
            fur.Direction = new Vector3(vector.x, vector.y, vector.z);
            fur.Length = vector.w;
            fur.DirectionMap = AtlasMaterialReader.Sample(m, "_FurVectorTex", AtlasColorInterpretation.TangentNormal, d,
                "_FurVectorTexUV", true);
            fur.DirectionMapStrength = AtlasMaterialReader.Float(m, "_FurVectorScale", 1f);
            fur.DirectionFromVertexColors = AtlasSemanticReader.Toggle(m, "_VertexColor2FurVector");
            fur.Gravity = AtlasMaterialReader.Float(m, "_FurGravity", 0.25f);
            fur.Randomness = AtlasMaterialReader.Float(m, "_FurRandomize", 0f);
            // 11.x: fur bends away from a VRChat contact.
            fur.ContactIndex = Mathf.RoundToInt(AtlasMaterialReader.Float(m, "_FurContact", 0f));
            fur.ContactPush = AtlasMaterialReader.Float(m, "_FurContactPush", 1f);

            fur.LengthMask = FurMask(m, d, "_FurLengthChannel");
            fur.AlphaMask = FurMask(m, d, "_FurAlphaChannel");
            fur.StrandNoise = AtlasMaterialReader.Sample(m, "_FurNoiseMask", AtlasColorInterpretation.LinearData, d,
                "_FurNoiseMaskUV", true);
            fur.RootOcclusion = AtlasMaterialReader.Float(m, "_FurAO", 0f);

            fur.LayerCount = Mathf.Clamp(Mathf.RoundToInt(AtlasMaterialReader.Float(m, "_FurLayerNum", 2f)), 1, 3);
            fur.RootWidth = AtlasMaterialReader.Float(m, "_FurRootOffset", 0f);
            fur.Width = AtlasMaterialReader.Float(m, "_FurWidth", 0.15f);

            var wind = fur.Wind;
            wind.Enabled = AtlasSemanticReader.Toggle(m, "_FurWindEnabled");
            wind.PrimaryDirection = Vector(m, "_FurWindPrimaryDirection", new Vector4(1f, 0f, 0f, 0f));
            wind.PrimaryAmplitude = AtlasMaterialReader.Float(m, "_FurWindPrimaryAmplitude", 0.2f);
            wind.PrimarySpeed = AtlasMaterialReader.Float(m, "_FurWindPrimarySpeed", 1f);
            wind.PrimaryFrequency = AtlasMaterialReader.Float(m, "_FurWindPrimaryFrequency", 1f);
            wind.DetailDirection = Vector(m, "_FurWindDetailDirection", new Vector4(0f, 0f, 1f, 0f));
            wind.DetailAmplitude = AtlasMaterialReader.Float(m, "_FurWindDetailAmplitude", 0.1f);
            wind.DetailSpeed = AtlasMaterialReader.Float(m, "_FurWindDetailSpeed", 2.5f);
            wind.DetailFrequency = AtlasMaterialReader.Float(m, "_FurWindDetailFrequency", 5f);

            fur.RimColor = AtlasMaterialReader.Color(m, "_FurRimColor", Color.black);
            fur.RimPower = AtlasMaterialReader.Float(m, "_FurRimFresnelPower", 3f);
            fur.RimLitSideSuppression = AtlasMaterialReader.Float(m, "_FurRimAntiLight", 0.5f);

            var render = fur.Render;
            int cull = Mathf.RoundToInt(AtlasMaterialReader.Float(m, "_FurCull", 0f));
            render.Cull = cull == 1 ? AtlasCullMode.Front : cull == 2 ? AtlasCullMode.Back : AtlasCullMode.Off;
            render.DoubleSided = render.Cull == AtlasCullMode.Off;
            render.ZWrite = AtlasSemanticReader.Toggle(m, "_FurZWrite", true);
            render.ZClip = AtlasSemanticReader.Toggle(m, "_FurZClip", true);
            render.DepthOffsetFactor = AtlasMaterialReader.Float(m, "_FurOffsetFactor", 0f);
            render.DepthOffsetUnits = AtlasMaterialReader.Float(m, "_FurOffsetUnits", 0f);
            render.AlphaToCoverage = AtlasSemanticReader.Toggle(m, "_FurAlphaToMask");
        }

        // Poiyomi Pro Fur: the mesh redrawn in stacked shells.
        private static void PopulateShells(Material m, AtlasUnifiedMaterial d)
        {
            var fur = d.Fur;
            fur.Enabled = true;
            fur.Method = AtlasFurMethod.Shells;
            fur.LayerCount = Mathf.Max(1, Mathf.RoundToInt(AtlasMaterialReader.Float(m, "_FurLayerCount", 15f)));
            fur.Length = AtlasMaterialReader.Float(m, "_FurLength", 0.005f);
            fur.Gravity = AtlasMaterialReader.Float(m, "_FurGravityStrength", 0.5f);
            fur.TipTaper = AtlasMaterialReader.Float(m, "_FurTipTaper", 0f);
            fur.ContactIndex = Mathf.RoundToInt(AtlasMaterialReader.Float(m, "_FurContact", 0f));
            fur.ContactPush = AtlasMaterialReader.Float(m, "_FurContactPush", 1f);
            fur.Color.Color = AtlasMaterialReader.Color(m, "_FurColor", Color.white);
            fur.Color.Map = AtlasMaterialReader.Sample(m, "_FurTexture", AtlasColorInterpretation.ColorSRGB, d);
            fur.ColorAppliesToSurface = AtlasSemanticReader.Toggle(m, "_FurTextureApplyToSkin", true);
            var mask = AtlasMaterialReader.Sample(m, "_FurMask", AtlasColorInterpretation.LinearData, d, "_FurMaskUV", true);
            fur.AlphaMask = mask == null ? null : new AtlasMaskDefinition { Sample = mask, Channel = AtlasChannel.Red };
            // The height map shapes the strands within each shell.
            fur.StrandNoise = AtlasMaterialReader.Sample(m, "_FurHeightMap", AtlasColorInterpretation.LinearData, d);
            fur.RootOcclusion = AtlasMaterialReader.Float(m, "_FurAO", 0.5f);
            fur.DirectionFromVertexColors = AtlasSemanticReader.Toggle(m, "_FurUseVertexColorNormals");
            fur.LevelOfDetailStart = AtlasMaterialReader.Float(m, "_FurMinDistance", 0f);
            fur.LevelOfDetailEnd = AtlasMaterialReader.Float(m, "_FurMaxDistance", 0f);
            var shape = AtlasSemanticReader.Feature(d, "geometry.fur");
            AtlasSemanticReader.Vector(m, shape, "strandHeightRange", "_FurHeightMapMinMax");
            AtlasSemanticReader.Vector(m, shape, "occlusionRange", "_FurAOMinMax");
            AtlasSemanticReader.NumberSet(m, shape, "hueShift", "_FurHueShift", "hueShiftSpeed", "_FurHueShiftSpeed");
        }

        // The fur mask texture's channel is chosen per use; 4 means the use is off.
        // Each channel also has its own strength.
        private static AtlasMaskDefinition FurMask(Material m, AtlasUnifiedMaterial d, string channelProperty)
        {
            int channel = Mathf.RoundToInt(AtlasMaterialReader.Float(m, channelProperty, 4f));
            if (channel < 0 || channel > 3) return null;
            var sample = AtlasMaterialReader.Sample(m, "_FurMask", AtlasColorInterpretation.LinearData, d, "_FurMaskUV", true);
            if (sample == null) return null;
            string[] strengths = { "_FurMaskStrengthR", "_FurMaskStrengthG", "_FurMaskStrengthB", "_FurMaskStrengthA" };
            return new AtlasMaskDefinition {
                Sample = sample, Channel = (AtlasChannel)channel,
                Strength = AtlasMaterialReader.Float(m, strengths[channel], 1f)
            };
        }

        private static Vector4 Vector(Material m, string property, Vector4 fallback)
            => AtlasMaterialReader.Vector(m, property, fallback);
    }
}
