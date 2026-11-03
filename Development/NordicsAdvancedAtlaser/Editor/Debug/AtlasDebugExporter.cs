using System;
using System.IO;
using System.Text;
using UnityEngine;
using NordicWind.TextureWrapper;
using Object = UnityEngine.Object;

namespace NordicWind.AdvancedAtlaser.Editor
{
    // This class is Editor-only. It never stores textures and never imports into Assets.
    public static class AtlasDebugExporter
    {
        internal static string SafeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Unnamed";
            var builder = new StringBuilder();
            foreach (char c in value)
                builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c == '/' || c == '\\' ? '_' : c);
            return builder.ToString().Trim().Length == 0 ? "Unnamed" : builder.ToString().Trim();
        }
        public static string DirectoryFor(string atlaser, string material, string source)
        {
            var path = Path.Combine(AtlasDebugSettings.OutputDirectory, SafeName(atlaser),
                SafeName(material) + "_" + SafeName(source));
            Directory.CreateDirectory(path);
            return path;
        }
        private static void WriteTexture(string folder, string name, int width, int height, Color[] pixels, bool exr)
        {
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(width, height, exr ? TextureFormat.RGBAFloat : TextureFormat.RGBA32,
                    false, true);
                texture.SetPixels(pixels);
                texture.Apply(false, false);
                byte[] bytes = exr ? texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat) : texture.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(folder, name + (exr ? ".exr" : ".png")), bytes);
            }
            finally { if (texture != null) Object.DestroyImmediate(texture); }
        }
        internal static void Normal(string folder, string name, NordicsTextureWrapper wrapper)
        {
            if (wrapper == null) return;
            // Wrapper encodes signed normal vectors into [0,1] for readable previews.
            var encoded = wrapper.GetEncodedPixels();
            WriteTexture(folder, name + "_Preview", wrapper.Width, wrapper.Height, encoded, false);
            for (int i = 0; i < encoded.Length; i++)
                encoded[i] = new Color(encoded[i].r * 2f - 1f, encoded[i].g * 2f - 1f,
                    encoded[i].b * 2f - 1f, encoded[i].a);
            WriteTexture(folder, name + "_Raw", wrapper.Width, wrapper.Height, encoded, true);
        }
        internal static void Coverage(string folder, string name, NordicsTextureWrapper wrapper)
        {
            if (wrapper == null) return;
            var pixels = new Color[wrapper.Width * wrapper.Height];
            for (int y=0; y<wrapper.Height; y++)
                for (int x=0; x<wrapper.Width; x++)
                    pixels[y*wrapper.Width+x] = wrapper.IsPixelUsed(x,y) ? Color.white : Color.black;
            WriteTexture(folder, name, wrapper.Width, wrapper.Height, pixels, false);
        }
        internal static void Heatmap(string folder, NordicsTextureWrapper original, NordicsTextureWrapper current)
        {
            if (original == null || current == null) return;
            var pixels = new Color[original.Width*original.Height];
            for(int y=0;y<original.Height;y++) for(int x=0;x<original.Width;x++)
            {
                int i=y*original.Width+x;
                bool o=original.IsPixelUsed(x,y), c=current.IsPixelUsed(x,y);
                if (!o && !c) { pixels[i]=Color.black; continue; }
                if (!o || !c) { pixels[i]=Color.magenta; continue; }
                var a=original.GetPixel(x,y);var b=current.GetPixel(x,y);
                var an=new Vector3(a.r,a.g,a.b).normalized; var bn=new Vector3(b.r,b.g,b.b).normalized;
                float angle=Vector3.Angle(an,bn)/180f;
                // Blue -> cyan -> yellow -> red. Full red corresponds to 180 degrees.
                pixels[i]=Color.Lerp(Color.blue, Color.red, Mathf.Clamp01(angle*2f));
            }
            WriteTexture(folder,"NormalDifferenceHeatmap",original.Width,original.Height,pixels,false);
        }
        internal static void Info(string folder, string material, string sourceKey, int originalCount,
            int currentCount, int width, int height)
        {
            File.WriteAllText(Path.Combine(folder,"BakeInfo.txt"),
                "Nordic's Advanced Atlaser - intermediate geometry maps\n"+
                "Material: "+material+"\nSource: "+sourceKey+"\nOriginal sections: "+originalCount+
                "\nCurrent submeshes: "+currentCount+"\nResolution: "+width+" x "+height+
                "\nWorld normal EXR: signed XYZ in RGB; A is raster coverage weight.\n"+
                "Normal preview PNG: XYZ packed to [0,1].\n"+
                "Coverage PNG: white = rasterizer wrote pixel, black = no coverage.\n"+
                "Heatmap: magenta = unmatched UV coverage; blue-to-red = angular difference.\n"+
                "Raw tangent basis EXR: signed XYZ in avatar-root space, from the surviving mesh.\n");
        }
    }
}
