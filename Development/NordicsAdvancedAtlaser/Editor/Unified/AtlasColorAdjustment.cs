namespace NordicWind.AdvancedAtlaser.Editor
{
    public enum AtlasAdjustmentTarget
    {
        // The base colour input (2.1), before layers.
        BaseColor,
        // One layer's source. Emission is a layer.
        Layer,
        // The finished colour (S6).
        FinalColor
    }

    public enum AtlasColorOperation { Hue }

    public enum AtlasHueMethod
    {
        // Rotation of the linear colour around the grey axis (1,1,1)/√3; keeps brightness roughly constant.
        GreyAxisRotation
    }

    // 1.10: one colour operation on a target, with a mask. The material keeps them in order.
    public sealed class AtlasColorAdjustment
    {
        public AtlasAdjustmentTarget Target = AtlasAdjustmentTarget.BaseColor;
        // For Target Layer.
        public AtlasLayer Layer;
        // Null: everywhere.
        public AtlasMask Mask;
        public AtlasColorOperation Operation = AtlasColorOperation.Hue;
        // Hue: turns (1 = a full turn).
        public float Amount;
        public AtlasHueMethod HueMethod = AtlasHueMethod.GreyAxisRotation;
    }
}
