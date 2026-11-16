using UnityEngine;

namespace NordicWind.TextureWrapper
{
    public sealed partial class NordicsTextureWrapper
    {
        /// <summary>Copies the pixels (normalized first for normal maps) and the usage flags to
        /// render textures of this size, made with <see cref="TextureGpu.CreatePixels"/> and
        /// <see cref="TextureGpu.CreateUsage"/>. <paramref name="usage"/> may be null.</summary>
        internal void UploadTo(RenderTexture pixels, RenderTexture usage)
        {
            Check();
            if (Normal) EnsureNormalized();
            TextureGpu.UploadPixels(_pixels, Width, Height, pixels);
            if (usage != null) TextureGpu.UploadUsage(_usage, Width, Height, usage);
        }

        /// <summary>Replaces the pixels, and the usage flags unless <paramref name="usage"/> is
        /// null, with render textures of this size. <paramref name="normalized"/>: the GPU
        /// operation left normal vectors unit length.</summary>
        internal void DownloadFrom(RenderTexture pixels, RenderTexture usage, bool normalized)
        {
            Writable();
            TextureGpu.DownloadPixels(pixels, _pixels);
            if (usage != null) TextureGpu.DownloadUsage(usage, _usage);
            Changed(!normalized);
        }
    }
}
