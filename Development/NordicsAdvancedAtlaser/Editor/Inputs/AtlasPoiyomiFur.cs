using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Poiyomi: the Lil Fur variants' fur pass (catalogue 5.2): the LilFur category and the LilFur
    // Rendering section.
    public abstract partial class AtlasPoiyomiParser
    {
        private static void Fur(Material m, AtlasUnifiedMaterial d)
        {
            // Only the Lil Fur variants have the fur properties.
            if (!AtlasMaterialReader.Has(m, "_FurLayerNum")) return;
            var fur = d.Fur;
            fur.Enabled = true;
            fur.Method = AtlasFurMethod.Strands;
            fur.Transparent = Int(m, "_FurRenderingMode", 0) == 1;
            fur.AlphaToCoverage = On(m, "_FurAlphaToMask", 1f);

            // Shape: the vector's xyz is the direction, its w the length in metres.
            var vector = AtlasMaterialReader.Vector(m, "_FurVector", new Vector4(0f, 0f, 1f, 0.02f));
            fur.Direction = new Vector3(vector.x, vector.y, vector.z);
            fur.Length = vector.w;
            fur.DirectionMap = Texture(m, d, "_FurVectorTex", AtlasTextureInterpretation.Direction);
            fur.DirectionMapStrength = Float(m, "_FurVectorScale", 1f);
            fur.DirectionFromVertexColors = On(m, "_VertexColor2FurVector");
            fur.Gravity = Float(m, "_FurGravity", 0.25f);
            fur.Randomness = Float(m, "_FurRandomize", 0f);
            var masks = Texture(m, d, "_FurMask", AtlasTextureInterpretation.Data);
            fur.LengthMask = FurMask(m, masks, "_FurLengthChannel");

            // Appearance and layers.
            fur.StrandNoise = Texture(m, d, "_FurNoiseMask", AtlasTextureInterpretation.Data);
            fur.AlphaMask = FurMask(m, masks, "_FurAlphaChannel");
            fur.RootOcclusion = Float(m, "_FurAO", 0f);
            fur.LayerCount = Mathf.Clamp(Int(m, "_FurLayerNum", 2), 1, 3);
            fur.RootWidth = Float(m, "_FurRootOffset", 0f);
            fur.Width = Float(m, "_FurWidth", 0.15f);

            var wind = fur.Wind;
            wind.Enabled = On(m, "_FurWindEnabled");
            wind.PrimaryDirection = Vector3Of(m, "_FurWindPrimaryDirection", Vector3.right);
            wind.PrimaryAmplitude = Float(m, "_FurWindPrimaryAmplitude", 0.2f);
            wind.PrimarySpeed = Float(m, "_FurWindPrimarySpeed", 1f);
            wind.PrimaryFrequency = Float(m, "_FurWindPrimaryFrequency", 1f);
            wind.DetailDirection = Vector3Of(m, "_FurWindDetailDirection", Vector3.forward);
            wind.DetailAmplitude = Float(m, "_FurWindDetailAmplitude", 0.1f);
            wind.DetailSpeed = Float(m, "_FurWindDetailSpeed", 2.5f);
            wind.DetailFrequency = Float(m, "_FurWindDetailFrequency", 5f);

            fur.RimColor = LinearColor(m, "_FurRimColor", Color.black);
            fur.RimPower = Float(m, "_FurRimFresnelPower", 3f);
            fur.RimLitSideSuppression = Float(m, "_FurRimAntiLight", 0.5f);

            // LilFur Rendering; the blend factors follow the fur mode.
            var pass = fur.Pass;
            int cull = Int(m, "_FurCull", 0);
            pass.Cull = cull == 0 ? AtlasCull.Off : cull == 1 ? AtlasCull.Front : AtlasCull.Back;
            pass.DepthTest = (AtlasDepthTest)Mathf.Clamp(Int(m, "_FurZTest", 4), 0, 8);
            pass.DepthWrite = On(m, "_FurZWrite", 1f);
            pass.DepthClip = On(m, "_FurZClip", 1f);
            pass.DepthOffsetFactor = Float(m, "_FurOffsetFactor", 0f);
            pass.DepthOffsetUnits = Float(m, "_FurOffsetUnits", 0f);
            pass.ColorMask = Int(m, "_FurColorMask", 15);
            var stencil = pass.Stencil;
            stencil.Reference = Int(m, "_FurStencilRef", 0);
            stencil.ReadMask = Int(m, "_FurStencilReadMask", 255);
            stencil.WriteMask = Int(m, "_FurStencilWriteMask", 255);
            stencil.Compare = (AtlasDepthTest)Mathf.Clamp(Int(m, "_FurStencilComp", 8), 0, 8);
            stencil.Pass = (AtlasStencilOperation)Mathf.Clamp(Int(m, "_FurStencilPass", 0), 0, 7);
            stencil.Fail = (AtlasStencilOperation)Mathf.Clamp(Int(m, "_FurStencilFail", 0), 0, 7);
            stencil.DepthFail = (AtlasStencilOperation)Mathf.Clamp(Int(m, "_FurStencilZFail", 0), 0, 7);
            stencil.Enabled = stencil.Reference != 0 || stencil.Compare != AtlasDepthTest.Always || stencil.Pass != AtlasStencilOperation.Keep;
            if (Int(m, "_FurStencilType", 0) == 1)
                d.Report(AtlasIssueKind.Unmapped, "render.stencil-faces", "Separate front- and back-face stencils aren't mapped yet.");
        }

        // A fur mask channel (0–3, 4 off) with its strength: lerp(1, channel, strength).
        private static AtlasMask FurMask(Material m, AtlasTextureReference masks, string channel)
        {
            int c = Int(m, channel, 4);
            if (c > 3 || masks == null) return null;
            return new AtlasMask { Map = masks, Channel = Channels[c], Strength = Float(m, "_FurMaskStrength" + "RGBA"[c], 1f) };
        }
    }
}
