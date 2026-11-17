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
        // Found once per domain load: the device and the package's shaders don't change meanwhile.
        private static bool? available;
        internal static bool Available => available ?? (available = Find()).Value;

        private static bool Find() =>
            SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null
            && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)
            && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
            && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGFloat)
            && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
            && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8)
            && Supported("Dilate") && Supported("PaintTriangles") && Supported("ResolvePaint");

        // Found in the package and compiled for this GPU.
        private static bool Supported(string name)
        {
            var shader = Shader(name);
            return shader != null && shader.isSupported;
        }

        /// <summary>Whether an operation on this many pixels should run on the GPU.</summary>
        internal static bool ShouldUse(long pixels) =>
            TextureProcessingSettings.UseGpu && pixels >= TextureProcessingSettings.MinGpuPixels && Available;

        /// <summary>Batches with fewer triangles than this paint on the CPU: the GPU's setup costs
        /// more than painting them. Tests lower it.</summary>
        internal static int MinTriangles = 1024;

        /// <summary>One of the package's shaders, from its Resources (null when missing).</summary>
        internal static Shader Shader(string name) => Resources.Load<Shader>("NordicWind.TextureWrapper/" + name);

        internal static Material Material(string name)
        {
            var shader = Shader(name);
            if (shader == null) throw new InvalidOperationException($"The Texture Wrapper's {name} shader is missing from its Resources.");
            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        /// <summary>Clears a render texture to transparent black.</summary>
        internal static void Clear(RenderTexture target)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
        }

        /// <summary>Draws a mesh whose vertices are positions 0–1 on the texture into it, with a
        /// shader that turns them into clip space itself (PaintTriangles, with _FlipY set).</summary>
        internal static void DrawInTextureSpace(Mesh mesh, Material material, int pass, RenderTexture target)
        {
            using (var commands = new CommandBuffer { name = "NordicWind Texture Wrapper" })
            {
                commands.SetRenderTarget(target);
                commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, pass);
                Graphics.ExecuteCommandBuffer(commands);
            }
        }

        // Found once by a test draw: 1 when clip space's y = -1 lands on texture row 0, -1 when
        // the graphics API runs it the other way, 0 when the drawing didn't land at all
        // (triangle painting then stays on the CPU).
        private static float? paintFlip;
        internal static float PaintFlip => paintFlip ?? (paintFlip = FindPaintFlip()).Value;

        private static float FindPaintFlip()
        {
            if (!Available) return 0f;
            var material = Material("PaintTriangles");
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture target = null;
            try
            {
                // The lower half of a 4×4 texture (rows 0 and 1), drawn unflipped.
                mesh.vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 0.5f, 0f), new Vector3(0f, 0.5f, 0f) };
                mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                mesh.SetIndices(new[] { 0, 1, 2, 0, 2, 3 }, MeshTopology.Triangles, 0, false);
                target = CreatePixels(4, 4);
                Clear(target);
                material.SetFloat("_FlipY", 1f);
                DrawInTextureSpace(mesh, material, 0, target);
                var pixels = new Color[16];
                DownloadPixels(target, pixels);
                bool bottom = pixels[0].r > 0.5f, top = pixels[12].r > 0.5f;
                return bottom && !top ? 1f : top && !bottom ? -1f : 0f;
            }
            finally
            {
                Release(target);
                Destroy(mesh);
                Destroy(material);
            }
        }

        internal static void DestroyObject(UnityEngine.Object o) => Destroy(o);

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
            // Blit leaves its target active; Unity warns about releasing the active one.
            if (RenderTexture.active == texture) RenderTexture.active = null;
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
