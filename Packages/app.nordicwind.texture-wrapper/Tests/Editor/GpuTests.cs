using System;
using NUnit.Framework;
using UnityEngine;

namespace NordicWind.TextureWrapper.Tests
{
    /// <summary>Runs a check on the CPU and, where there is a GPU, on the GPU too, with the
    /// settings restored afterward. Operations with a GPU version use it to compare both.</summary>
    internal static class BothProcessors
    {
        internal static void Run(Action<string> check)
        {
            bool useGpu = TextureProcessingSettings.UseGpu;
            int minimum = TextureProcessingSettings.MinGpuPixels;
            int triangles = TextureGpu.MinTriangles;
            try
            {
                TextureProcessingSettings.UseGpu = false;
                check("CPU");
                if (!TextureProcessingSettings.GpuAvailable) return;
                TextureProcessingSettings.UseGpu = true;
                TextureProcessingSettings.MinGpuPixels = 0;
                TextureGpu.MinTriangles = 0;
                check("GPU");
            }
            finally
            {
                TextureProcessingSettings.UseGpu = useGpu;
                TextureProcessingSettings.MinGpuPixels = minimum;
                TextureGpu.MinTriangles = triangles;
            }
        }
    }

    public class GpuTests
    {
        [Test]
        public void PixelsAndUsageRoundTripThroughTheGpuUnchanged()
        {
            if (!TextureGpu.Available) Assert.Ignore("No GPU (batch mode without graphics).");
            using (var source = new NordicsTextureWrapper(37, 21, new TextureSettings { DataType = TextureDataType.LinearData, SRGB = false }))
            using (var copy = source.CloneEmpty())
            {
                for (int y = 0; y < 21; y++)
                    for (int x = 0; x < 37; x++)
                        if ((x + y) % 3 != 0)
                            source.SetPixel(x, y, new Color(x * 0.1f - 1f, y * 1.5f, -0.25f, x * 0.001f));
                var pixels = TextureGpu.CreatePixels(37, 21);
                var usage = TextureGpu.CreateUsage(37, 21);
                try
                {
                    source.UploadTo(pixels, usage);
                    copy.DownloadFrom(pixels, usage, true);
                }
                finally
                {
                    TextureGpu.Release(pixels);
                    TextureGpu.Release(usage);
                }
                for (int y = 0; y < 21; y++)
                    for (int x = 0; x < 37; x++)
                    {
                        Assert.AreEqual(source.IsPixelUsed(x, y), copy.IsPixelUsed(x, y), $"usage at ({x}, {y})");
                        Assert.AreEqual(source.GetPixel(x, y), copy.GetPixel(x, y), $"pixel at ({x}, {y})");
                    }
            }
        }

        [Test]
        public void SmallJobsAndATurnedOffGpuStayOnTheCpu()
        {
            bool useGpu = TextureProcessingSettings.UseGpu;
            int minimum = TextureProcessingSettings.MinGpuPixels;
            try
            {
                TextureProcessingSettings.UseGpu = true;
                TextureProcessingSettings.MinGpuPixels = 1000;
                Assert.IsFalse(TextureGpu.ShouldUse(999));
                Assert.AreEqual(TextureGpu.Available, TextureGpu.ShouldUse(1000));
                TextureProcessingSettings.UseGpu = false;
                Assert.IsFalse(TextureGpu.ShouldUse(1 << 24));
                Assert.Throws<ArgumentOutOfRangeException>(() => TextureProcessingSettings.MinGpuPixels = -1);
            }
            finally
            {
                TextureProcessingSettings.UseGpu = useGpu;
                TextureProcessingSettings.MinGpuPixels = minimum;
            }
        }

        [Test]
        public void BothProcessorsRestoreTheSettings()
        {
            bool useGpu = TextureProcessingSettings.UseGpu;
            int minimum = TextureProcessingSettings.MinGpuPixels;
            int runs = 0;
            BothProcessors.Run(_ => runs++);
            Assert.AreEqual(TextureProcessingSettings.GpuAvailable ? 2 : 1, runs);
            Assert.AreEqual(useGpu, TextureProcessingSettings.UseGpu);
            Assert.AreEqual(minimum, TextureProcessingSettings.MinGpuPixels);
        }
    }
}
