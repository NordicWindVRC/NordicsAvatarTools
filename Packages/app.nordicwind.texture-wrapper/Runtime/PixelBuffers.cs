using System;
using System.Collections.Generic;
using UnityEngine;

namespace NordicWind.TextureWrapper
{
    // Pixel and usage arrays of disposed wrappers, kept for the next wrapper of the same size:
    // large textures otherwise allocate hundreds of megabytes per operation, which in Unity's
    // editor also means frequent garbage collections. Up to TextureProcessingSettings.PooledBytes.
    internal static class PixelBuffers
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<int, Stack<Color[]>> Colors = new Dictionary<int, Stack<Color[]>>();
        private static readonly Dictionary<int, Stack<byte[]>> Usage = new Dictionary<int, Stack<byte[]>>();
        private static long pooled;

        private static long Size(int length) => (long)length * 17;

        internal static void Rent(int length, bool clear, out Color[] colors, out byte[] usage)
        {
            colors = null;
            usage = null;
            lock (Gate)
            {
                if (Colors.TryGetValue(length, out var c) && c.Count > 0 && Usage.TryGetValue(length, out var u) && u.Count > 0)
                {
                    colors = c.Pop();
                    usage = u.Pop();
                    pooled -= Size(length);
                }
            }
            if (colors == null)
            {
                colors = new Color[length];
                usage = new byte[length];
            }
            else if (clear)
            {
                Array.Clear(colors, 0, length);
                Array.Clear(usage, 0, length);
            }
        }

        internal static void Return(Color[] colors, byte[] usage)
        {
            if (colors == null || usage == null || colors.Length != usage.Length) return;
            int length = colors.Length;
            lock (Gate)
            {
                if (pooled + Size(length) > TextureProcessingSettings.PooledBytes) return;
                if (!Colors.TryGetValue(length, out var c)) Colors[length] = c = new Stack<Color[]>();
                if (!Usage.TryGetValue(length, out var u)) Usage[length] = u = new Stack<byte[]>();
                c.Push(colors);
                u.Push(usage);
                pooled += Size(length);
            }
        }

        internal static void Clear()
        {
            lock (Gate)
            {
                Colors.Clear();
                Usage.Clear();
                pooled = 0;
            }
        }
    }
}
