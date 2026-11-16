namespace NordicWind.AdvancedAtlaser.Editor
{
    public enum AtlasMaskBusStepKind
    {
        // value = the map's channel
        Texture,
        // value = saturate(blend(value, a vertex colour channel)), sRGB decoded when VertexLinear
        VertexColor,
        // value × (1 on Faces, 0 on the other side)
        Faces,
        // value × (in a mirror ? Inside : !Inside), 1 or 0
        Mirror,
        // value × (in a VRChat camera ? Inside : !Inside), 1 or 0
        Camera,
        // value = saturate(blend(value, lerp(MinValue, MaxValue, smoothstep(Min, Max, distance to
        // the camera)))), the distance from the pixel or the object's origin
        Distance,
        // value = saturate(value + Amount)
        Offset,
        // value = lerp(Min, Max, value)
        Remap,
        // value = 1
        ForceOn,
        // value = 0
        ForceOff,
        // value = saturate(blend(value, the layer's coverage)), or of 1 − it when InvertCoverage
        // (a transition's undissolved part, Poiyomi's Output Undissolved)
        LayerCoverage,
        // value = saturate(blend(value, smoothstep(Max, Min, distance to a blacklight))): a
        // VRChat blacklight is a black vertex light whose alpha is the key (Amount)
        Blacklight
    }

    // One step of a channel's computation.
    public sealed class AtlasMaskBusStep
    {
        public int Channel;
        public AtlasMaskBusStepKind Kind;
        public AtlasMaskBlend Blend = AtlasMaskBlend.Multiply;
        public AtlasTextureReference Map;
        public AtlasChannel MapChannel = AtlasChannel.Red;
        public AtlasChannel VertexChannel = AtlasChannel.Red;
        public bool VertexLinear = true;
        public AtlasFaces Faces = AtlasFaces.Both;
        public bool Inside;
        public bool PixelPosition = true;
        public float Min, Max = 1f, MinValue, MaxValue = 1f;
        public float Amount;
        public AtlasLayer Layer;
        public bool InvertCoverage;
    }

    // 1.5: numbered channels shared by the whole material (Poiyomi: 16, 1R … 4A). Each starts at 1
    // and runs its steps in order; masks and scalar inputs read the result.
    public sealed class AtlasMaskBus
    {
        public readonly System.Collections.Generic.List<AtlasMaskBusStep> Steps = new System.Collections.Generic.List<AtlasMaskBusStep>();
    }
}
