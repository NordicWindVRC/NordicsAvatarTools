using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace NordicWind.TextureWrapper
{
    /// <summary>The GPU side of operations that have a GPU version: whether to use it, and
    /// moving pixels and usage flags to render textures and back without changing them.
    /// Pixels are float RGBA (<see cref="RenderTextureFormat.ARGBFloat"/>, linear); usage flags
    /// are <see cref="RenderTextureFormat.R8"/>, 1 for used.</summary>
    internal static class TextureGpu
    {
        internal static bool Available =>
            SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null
            && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)
            && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8);

        /// <summary>Whether an operation on this many pixels should run on the GPU.</summary>
        internal static bool ShouldUse(long pixels) =>
            TextureProcessingSettings.UseGpu && pixels >= TextureProcessingSettings.MinGpuPixels && Available;

        internal static RenderTexture CreatePixels(int width, int height) => Create(width, height, RenderTextureFormat.ARGBFloat);

        internal static RenderTexture CreateUsage(int width, int height) => Create(width, height, RenderTextureFormat.R8);

        internal static RenderTexture Create(int width, int height, RenderTextureFormat format)
        {
            var texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false
            };
            if (!texture.Create())
                throw new InvalidOperationException($"Couldn't create a {width}×{height} {format} render texture.");
            return texture;
        }

        internal static void Release(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            Destroy(texture);
        }

        internal static void UploadPixels(Color[] pixels, int width, int height, RenderTexture target)
        {
            var staging = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            try
            {
                staging.SetPixelData(pixels, 0);
                staging.Apply(false, false);
                Copy(staging, target);
            }
            finally
            {
                Destroy(staging);
            }
        }

        internal static void UploadUsage(byte[] usage, int width, int height, RenderTexture target)
        {
            var bytes = new byte[usage.Length];
            for (int i = 0; i < usage.Length; i++) bytes[i] = usage[i] != 0 ? (byte)255 : (byte)0;
            var staging = new Texture2D(width, height, TextureFormat.R8, false, true) { filterMode = FilterMode.Point };
            try
            {
                staging.SetPixelData(bytes, 0);
                staging.Apply(false, false);
                Copy(staging, target);
            }
            finally
            {
                Destroy(staging);
            }
        }

        internal static void DownloadPixels(RenderTexture source, Color[] target)
        {
            if (SystemInfo.supportsAsyncGPUReadback)
            {
                var request = AsyncGPUReadback.Request(source, 0, TextureFormat.RGBAFloat);
                request.WaitForCompletion();
                if (request.hasError) throw new InvalidOperationException("Reading a render texture back from the GPU failed.");
                request.GetData<Color>().CopyTo(target);
                return;
            }
            var staging = ReadPixels(source, TextureFormat.RGBAFloat);
            try { Array.Copy(staging.GetPixels(), target, target.Length); }
            finally { Destroy(staging); }
        }

        internal static void DownloadUsage(RenderTexture source, byte[] target)
        {
            byte[] bytes;
            if (SystemInfo.supportsAsyncGPUReadback)
            {
                var request = AsyncGPUReadback.Request(source, 0, TextureFormat.R8);
                request.WaitForCompletion();
                if (request.hasError) throw new InvalidOperationException("Reading a render texture back from the GPU failed.");
                bytes = request.GetData<byte>().ToArray();
            }
            else
            {
                var staging = ReadPixels(source, TextureFormat.R8);
                try { bytes = staging.GetRawTextureData(); }
                finally { Destroy(staging); }
            }
            for (int i = 0; i < target.Length; i++) target[i] = bytes[i] != 0 ? (byte)1 : (byte)0;
        }

        // A copy that keeps the values: CopyTexture where the platform has it, otherwise a
        // same-size blit with point sampling in a linear render texture.
        private static void Copy(Texture source, RenderTexture target)
        {
            if (SystemInfo.copyTextureSupport != CopyTextureSupport.None) Graphics.CopyTexture(source, target);
            else Graphics.Blit(source, target);
        }

        private static Texture2D ReadPixels(RenderTexture source, TextureFormat format)
        {
            var previous = RenderTexture.active;
            var staging = new Texture2D(source.width, source.height, format, false, true);
            try
            {
                RenderTexture.active = source;
                staging.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                staging.Apply(false, false);
                return staging;
            }
            catch
            {
                Destroy(staging);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static void Destroy(UnityEngine.Object o)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(o); return; }
#endif
            UnityEngine.Object.Destroy(o);
        }
    }
}
