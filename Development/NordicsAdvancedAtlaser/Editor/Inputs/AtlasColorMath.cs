using UnityEngine;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // Color math for parsers that resolve a source's color settings to colors: linear RGB,
    // Oklab and HSV, as shaders compute them.
    internal static class AtlasColorMath
    {
        public static Vector3 ToOklab(Vector3 c)
        {
            float l = Cbrt(0.4122214708f * c.x + 0.5363325363f * c.y + 0.0514459929f * c.z);
            float m = Cbrt(0.2119034982f * c.x + 0.6806995451f * c.y + 0.1073969566f * c.z);
            float s = Cbrt(0.0883024619f * c.x + 0.2817188376f * c.y + 0.6299787005f * c.z);
            return new Vector3(
                0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
        }

        public static Vector3 FromOklab(Vector3 c)
        {
            float l = c.x + 0.3963377774f * c.y + 0.2158037573f * c.z;
            float m = c.x - 0.1055613458f * c.y - 0.0638541728f * c.z;
            float s = c.x - 0.0894841775f * c.y - 1.2914855480f * c.z;
            l = l * l * l; m = m * m * m; s = s * s * s;
            return new Vector3(
                4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
                -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
                -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
        }

        public static Vector3 ToHsv(Vector3 c)
        {
            Vector4 p = c.z <= c.y ? new Vector4(c.y, c.z, 0f, -1f / 3f) : new Vector4(c.z, c.y, -1f, 2f / 3f);
            Vector4 q = p.x <= c.x ? new Vector4(c.x, p.y, p.z, p.x) : new Vector4(p.x, p.y, p.w, c.x);
            float d = q.x - Mathf.Min(q.w, q.y);
            const float e = 1e-4f;
            return new Vector3(Mathf.Abs(q.z + (q.w - q.y) / (6f * d + e)), d / (q.x + e), q.x);
        }

        public static Vector3 FromHsv(Vector3 c)
        {
            float Channel(float k)
            {
                float p = Mathf.Abs(Frac(c.x + k) * 6f - 3f);
                return c.z * Mathf.Lerp(1f, Mathf.Clamp01(p - 1f), c.y);
            }
            return new Vector3(Channel(1f), Channel(2f / 3f), Channel(1f / 3f));
        }

        // A hue shift by turns: the Oklab hue rotated, or the HSV hue offset.
        public static Vector3 HueShift(Vector3 c, float turns, bool hsv)
        {
            if (hsv)
            {
                var h = ToHsv(c);
                h.x += turns;
                return FromHsv(h);
            }
            var lab = ToOklab(c);
            float angle = turns * 2f * Mathf.PI, sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
            return FromOklab(new Vector3(lab.x, cos * lab.y - sin * lab.z, sin * lab.y + cos * lab.z));
        }

        // HSV saturation and value offsets, saturated.
        public static Vector3 ModifyHsv(Vector3 c, float saturation, float value)
        {
            var h = ToHsv(c);
            h.x = Frac(h.x);
            h.y = Mathf.Clamp01(h.y + saturation);
            h.z = Mathf.Clamp01(h.z + value);
            return FromHsv(h);
        }

        // Oklab chroma × (1 + amount), at most that of the most saturated color of the hue.
        public static Vector3 Chromatize(Vector3 c, float amount)
        {
            float min = Mathf.Min(c.x, Mathf.Min(c.y, c.z)), max = Mathf.Max(c.x, Mathf.Max(c.y, c.z)), range = max - min;
            var pure = range > 1e-4f ? (c - Vector3.one * min) / range : new Vector3(1f, 0f, 0f);
            var pureLab = ToOklab(pure);
            float limit = new Vector2(pureLab.y, pureLab.z).magnitude;
            var lab = ToOklab(c);
            float chroma = Mathf.Min(new Vector2(lab.y, lab.z).magnitude * (1f + amount), limit);
            float hue = Mathf.Atan2(lab.z, lab.y);
            return FromOklab(new Vector3(lab.x, chroma * Mathf.Cos(hue), chroma * Mathf.Sin(hue)));
        }

        // Poiyomi's theme saturation: chromatize below 0; above, toward the pure hue, darkened past 1.
        public static Vector3 ThemeSaturation(Vector3 c, float amount)
        {
            if (amount <= 0f) return Chromatize(c, amount);
            float min = Mathf.Min(c.x, Mathf.Min(c.y, c.z)), max = Mathf.Max(c.x, Mathf.Max(c.y, c.z)), range = max - min;
            var pure = range > 1e-4f ? (c - Vector3.one * min) / range * max : c;
            return Vector3.Lerp(c, pure, Mathf.Clamp01(amount)) / (1f + 0.35f * Mathf.Max(amount - 1f, 0f));
        }

        private static float Cbrt(float x) => Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), 1f / 3f);
        private static float Frac(float x) => x - Mathf.Floor(x);
    }
}
