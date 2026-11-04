using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace NordicWind.TextureWrapper.Tests
{
    // Loading textures without Read/Write, and decoding normal-map packings into vectors.
    public class TextureLoadingTests
    {
        private static void Near(double expected, float actual, double epsilon) => Assert.AreEqual(expected, actual, epsilon);
        // A tangent normal (0.6, -0.2, 0.775), stored as 0.8 and 0.4 in its X and Y channels.
        private const float X = 204 / 255f, Y = 102 / 255f;
        private static readonly Vector3 Expected = new Vector3(X * 2 - 1, Y * 2 - 1,
            Mathf.Sqrt(1 - (X * 2 - 1) * (X * 2 - 1) - (Y * 2 - 1) * (Y * 2 - 1)));

        private static Texture2D Texture(bool srgb, bool readable, params Color32[] pixels)
        {
            var texture = new Texture2D(pixels.Length, 1, TextureFormat.RGBA32, false, !srgb);
            texture.SetPixels32(pixels);
            texture.Apply(false, !readable);
            return texture;
        }
        private static void AssertNormal(Vector3 expected, Color actual, double epsilon)
        {
            Near(expected.x, actual.r, epsilon); Near(expected.y, actual.g, epsilon); Near(expected.z, actual.b, epsilon);
        }

        [Test] public void NonReadable_IsReadThroughTheGpuInTheSameOrientation()
        {
            var texture = new Texture2D(4, 2, TextureFormat.RGBA32, false, true);
            var pixels = new Color32[8];
            for (int y = 0; y < 2; y++) for (int x = 0; x < 4; x++) pixels[y * 4 + x] = new Color32((byte)(x * 60), (byte)(y * 200), 7, 128);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            try
            {
                Assert.IsFalse(texture.isReadable);
                using (var w = new NordicsTextureWrapper(texture))
                {
                    for (int y = 0; y < 2; y++) for (int x = 0; x < 4; x++)
                    {
                        Color c = w.GetPixel(x, y);
                        Near(x * 60 / 255.0, c.r, 1e-3); Near(y * 200 / 255.0, c.g, 1e-3);
                        Near(7 / 255.0, c.b, 1e-3); Near(128 / 255.0, c.a, 1e-3);
                    }
                    Assert.IsTrue(w.IsPixelUsed(3, 1));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test] public void NonReadable_SRGBIsDecodedToLinear()
        {
            var texture = Texture(true, false, new Color32(128, 128, 128, 255));
            try
            {
                using (var w = new NordicsTextureWrapper(texture))
                {
                    Assert.IsTrue(w.Settings.SRGB);
                    Near(0.21586, w.GetPixel(0, 0).r, 2e-3);
                    Near(1, w.GetPixel(0, 0).a, 1e-3);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void NormalTangent_XInAlphaLayout(bool readable)
        {
            // DXT5nm style: X in alpha, Y in green, red unused.
            var texture = Texture(false, readable, new Color32(255, 102, 255, 204));
            try
            {
                using (var w = new NordicsTextureWrapper(texture, TextureDataType.NormalTangent))
                {
                    AssertNormal(Expected, w.GetPixel(0, 0), 2e-3);
                    Near(1, w.GetPixel(0, 0).a, 1e-6);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test] public void NormalTangent_XInRedLayoutRebuildsZ()
        {
            // BC5 style: X in red, Y in green, no Z stored.
            var texture = Texture(false, true, new Color32(204, 102, 0, 255));
            try
            {
                using (var w = new NordicsTextureWrapper(texture, TextureDataType.NormalTangent))
                    AssertNormal(Expected, w.GetPixel(0, 0), 2e-3);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test] public void NormalObject_DecodesRgbAsXyz()
        {
            var texture = Texture(false, true, new Color32(128, 255, 128, 255));
            try
            {
                using (var w = new NordicsTextureWrapper(texture, TextureDataType.NormalObject))
                    AssertNormal(new Vector3(0, 1, 0), w.GetPixel(0, 0), 1e-2);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test] public void GetDataType_UsesTheFormatForTexturesThatAreNotAssets()
        {
            var srgb = Texture(true, true, new Color32(0, 0, 0, 255));
            var linear = Texture(false, true, new Color32(0, 0, 0, 255));
            try
            {
                Assert.AreEqual(TextureDataType.Color, TextureAssetUtility.GetDataType(srgb));
                Assert.AreEqual(TextureDataType.LinearData, TextureAssetUtility.GetDataType(linear));
            }
            finally { UnityEngine.Object.DestroyImmediate(srgb); UnityEngine.Object.DestroyImmediate(linear); }
        }

        [Test] public void ImportedNormalMap_IsDetectedAndDecoded()
        {
            // A real imported normal map: default compression, Read/Write off. Unity packs it
            // for the active build target, so this checks the packing that will be used.
            string path = "Assets/NordicWindTextureWrapperTest_" + Guid.NewGuid().ToString("N") + ".png";
            var source = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
            try
            {
                var pixels = new Color32[64];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(204, 102, 226, 255);
                source.SetPixels32(pixels);
                File.WriteAllBytes(Path.GetFullPath(path), source.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.NormalMap;
                importer.isReadable = false;
                importer.SaveAndReimport();

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.IsFalse(texture.isReadable);
                Assert.AreEqual(TextureDataType.NormalTangent, TextureAssetUtility.GetDataType(texture));
                using (var w = TextureAssetUtility.Load(texture))
                {
                    Assert.AreEqual(TextureDataType.NormalTangent, w.Settings.DataType);
                    // Block compression of a constant color is close, but not exact.
                    AssertNormal(Expected, w.GetPixel(4, 4), 3e-2);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
