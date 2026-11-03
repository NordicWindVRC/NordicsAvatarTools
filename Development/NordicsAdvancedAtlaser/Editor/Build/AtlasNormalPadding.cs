using System;
using NordicWind.TextureWrapper;

namespace NordicWind.AdvancedAtlaser.Editor
{
    /// <summary>
    /// Delegates full normal-correction dilation to Nordic's Texture Wrapper.
    /// The wrapper owns normal-vector normalization and its parallel pixel processing.
    /// Atlaser must not interleave GetPixel/SetPixel over an entire normal buffer,
    /// because doing so can repeatedly trigger expensive whole-texture normalization.
    /// UV coverage is retained separately and is not modified here.
    /// </summary>
    public static class AtlasNormalPadding
    {
        public static void DilateAndFinalize(NordicsTextureWrapper correction)
        {
            if (correction == null) throw new ArgumentNullException(nameof(correction));
            correction.Dilate();
        }
    }
}
