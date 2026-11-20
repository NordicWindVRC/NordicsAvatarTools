namespace NordicWind.AdvancedAtlaser.Editor
{
    public enum AtlasMaskBusStepKind
    {
        // value = the mask's value (1.4: a map's channel with its levels and vertex color)
        Texture,
        // value = saturate(blend(value, a vertex color channel)), sRGB decoded when VertexLinear
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
        Blacklight,
        // value = saturate(blend(value, the touch of VRChat contacts, see AtlasContact))
        Contact,
        // value = blend(value, 1 in the second pass (AtlasSecondPass) when Inside, in the first
        // when not; else 0)
        SecondPass
    }

    public enum AtlasContactPeople { Everyone, Others, Me }

    // A touch from VRChat's contacts sent to shaders (Poiyomi 11's global mask contacts): each
    // matching contact touches with 1 − smoothstep(InnerRadius, OuterRadius, distance), the
    // distance from the pixel (AroundTouch) or from the object's origin, the radii in meters at
    // the avatar's uploaded size (× the toucher's size with ScaleWithToucher); the touches add up,
    // faded in and out as contacts come and go; 1 − it when inverted.
    public sealed class AtlasContact
    {
        // Which contact tags (VRChat's 64, four groups of 16 bits).
        public readonly int[] Tags = new int[4];
        public AtlasContactPeople People = AtlasContactPeople.Everyone;
        public float InnerRadius = 0.05f;
        public float OuterRadius = 0.15f;
        public bool ScaleWithToucher;
        public bool AroundTouch = true;
        public bool Invert;
    }

    // One step of a channel's computation.
    public sealed class AtlasMaskBusStep
    {
        public int Channel;
        public AtlasMaskBusStepKind Kind;
        public AtlasMaskBlend Blend = AtlasMaskBlend.Multiply;
        // Texture.
        public AtlasMask Mask;
        // Contact.
        public AtlasContact Contact;
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
