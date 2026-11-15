namespace NordicWind.AdvancedAtlaser.Editor
{
    public enum AtlasOutlineShape
    {
        // An inverted hull pushed out along the normal.
        NormalExpansion
    }

    // 5.1: one outline layer. A material can have several.
    public sealed class AtlasOutline
    {
        public bool Enabled = true;
        public AtlasOutlineShape Shape = AtlasOutlineShape.NormalExpansion;
        // Metres.
        public float Width = 0.0005f;
        // Null: everywhere.
        public AtlasMask WidthMask;
        // The largest on-screen offset in normalised device coordinates; 0 = no limit.
        public float ScreenWidthLimit;
        // Width × vertex colour; black vertices have no outline.
        public bool WidthFromVertexColor;
        // No outline closer to the camera than this, in metres.
        public float HideWithin;
        public readonly AtlasColorInput Color = new AtlasColorInput { Color = UnityEngine.Color.black };
        // 0–1: blends toward the base colour (with vertex colour).
        public float BaseColorMix;
        // Colour × the base colour.
        public bool MultiplyByBaseColor;
        // Unlit: the colour as is.
        public bool Lit;
        // The material's emission also shows on the outline.
        public bool Emission;
        public readonly AtlasPassSettings Pass = new AtlasPassSettings { Cull = AtlasCull.Front };
    }
}
