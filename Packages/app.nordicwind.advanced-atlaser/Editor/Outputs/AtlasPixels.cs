using System;
using System.Threading.Tasks;
using NordicWind.TextureWrapper;
using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Whole-map pixel functions, on every core. Each writes every pixel and marks it used. The
    // working arrays are kept between calls (the bake runs on one thread).
    public static class AtlasPixels
    {
        private static Color[] first, second;

        private static Color[] Buffer(ref Color[] buffer, NordicsTextureWrapper map)
        {
            int length = map.Width * map.Height;
            if (buffer == null || buffer.Length != length) buffer = new Color[length];
            map.GetPixels(buffer);
            return buffer;
        }

        // Frees the working arrays.
        public static void Release()
        {
            first = null;
            second = null;
        }

        public static void Map(NordicsTextureWrapper map, Func<Color, Color> function)
        {
            var pixels = Buffer(ref first, map);
            int width = map.Width;
            Parallel.For(0, map.Height, y =>
            {
                int row = y * width;
                for (int x = 0; x < width; x++) pixels[row + x] = function(pixels[row + x]);
            });
            map.SetPixels(pixels);
        }

        // With a second map of the same size.
        public static void Map(NordicsTextureWrapper map, NordicsTextureWrapper other, Func<Color, Color, Color> function)
        {
            if (other.Width != map.Width || other.Height != map.Height) throw new ArgumentException("The maps must have the same size.");
            var pixels = Buffer(ref first, map);
            var others = Buffer(ref second, other);
            int width = map.Width;
            Parallel.For(0, map.Height, y =>
            {
                int row = y * width;
                for (int x = 0; x < width; x++) pixels[row + x] = function(pixels[row + x], others[row + x]);
            });
            map.SetPixels(pixels);
        }

        // The largest value of a function over the map.
        public static float Max(NordicsTextureWrapper map, Func<Color, float> function)
        {
            var pixels = Buffer(ref first, map);
            int width = map.Width;
            var rows = new float[map.Height];
            Parallel.For(0, map.Height, y =>
            {
                int row = y * width;
                float max = float.MinValue;
                for (int x = 0; x < width; x++) max = Mathf.Max(max, function(pixels[row + x]));
                rows[y] = max;
            });
            float result = float.MinValue;
            foreach (float value in rows) result = Mathf.Max(result, value);
            return result;
        }
    }
}
