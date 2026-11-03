using NUnit.Framework;
using UnityEngine;

namespace NordicWind.TextureWrapper.Tests
{
    public class DilateRegressionTests
    {
        private static TextureSettings Settings(TextureDataType type) => new TextureSettings { DataType=type, SRGB=false };
        private static void Pixel(NordicsTextureWrapper t,int x,int y,Color expected,float tolerance=0.00002f)
        {
            Color c=t.GetPixel(x,y);
            Assert.AreEqual(expected.r,c.r,tolerance,"red");
            Assert.AreEqual(expected.g,c.g,tolerance,"green");
            Assert.AreEqual(expected.b,c.b,tolerance,"blue");
            Assert.AreEqual(expected.a,c.a,tolerance,"alpha");
        }
        [TestCase(TextureDataType.NormalTangent)]
        [TestCase(TextureDataType.NormalObject)]
        [TestCase(TextureDataType.NormalWorld)]
        public void Normal_QuarterAndOverOneAlpha_AreOpaqueAndKeepRGB(TextureDataType type)
        {
            using(var t=new NordicsTextureWrapper(2,1,Settings(type)))
            {
                t.SetPixel(0,0,new Color(1,0,0,.25f));
                t.SetPixel(1,0,new Color(0,1,0,1.25f));
                t.Dilate();
                Pixel(t,0,0,new Color(1,0,0,1));Pixel(t,1,0,new Color(0,1,0,1));
                Assert.IsTrue(t.IsPixelUsed(0,0));Assert.IsTrue(t.IsPixelUsed(1,0));
            }
        }
        [Test] public void Normal_Uncovered_UsesNearestOriginalSiteAndOpaqueAlpha()
        {
            using(var t=new NordicsTextureWrapper(7,1,Settings(TextureDataType.NormalTangent)))
            {
                t.SetPixel(0,0,new Color(1,0,0,.25f));
                t.SetPixel(6,0,new Color(0,1,0,.4f));
                t.Dilate();
                // x=3 is equidistant from both sites; exact ties resolve to the lower coordinate.
                for(int x=0;x<7;x++)
                {
                    Pixel(t,x,0,x<=3?new Color(1,0,0,1):new Color(0,1,0,1));
                    Assert.IsTrue(t.IsPixelUsed(x,0));
                }
            }
        }
        [Test] public void EquidistantPixel_TakesLowerCoordinateSiteInBothAxes()
        {
            using(var t=new NordicsTextureWrapper(3,3,Settings(TextureDataType.Color)))
            {
                Color low=new Color(.2f,.2f,.2f,1),high=new Color(.8f,.8f,.8f,1);
                t.SetPixel(0,1,low);t.SetPixel(2,1,high);t.Dilate();
                Pixel(t,1,1,low);
                t.Clear();
                t.SetPixel(1,0,low);t.SetPixel(1,2,high);t.Dilate();
                Pixel(t,1,1,low);
            }
        }
        [TestCase(TextureDataType.Color)]
        [TestCase(TextureDataType.LinearData)]
        [TestCase(TextureDataType.Mask)]
        public void RGBA_PreservesQuarterAndTransparentAlpha(TextureDataType type)
        {
            using(var t=new NordicsTextureWrapper(4,1,Settings(type)))
            {
                Color a=new Color(.1f,.2f,.3f,.25f),b=new Color(.8f,.6f,.4f,0f);
                t.SetPixel(0,0,a);t.SetPixel(3,0,b);t.Dilate();
                Pixel(t,0,0,a);Pixel(t,1,0,a);Pixel(t,2,0,b);Pixel(t,3,0,b);
                for(int x=0;x<4;x++)Assert.IsTrue(t.IsPixelUsed(x,0));
            }
        }
        [Test] public void FullyUsedNormal_CorrectsAlphaWithoutChangingUnitNormals()
        {
            using(var t=new NordicsTextureWrapper(3,2,Settings(TextureDataType.NormalWorld)))
            {
                for(int y=0;y<2;y++)for(int x=0;x<3;x++)t.SetPixel(x,y,new Color(0,0,1,.25f));
                t.Dilate();
                for(int y=0;y<2;y++)for(int x=0;x<3;x++)
                {Pixel(t,x,y,new Color(0,0,1,1));Assert.IsTrue(t.IsPixelUsed(x,y));}
            }
        }
        [Test] public void NoUsedPixels_KeepOriginalUnusedPixelsAndAlpha()
        {
            using(var t=new NordicsTextureWrapper(3,3,Settings(TextureDataType.NormalWorld)))
            {
                t.FillUnused(new Color(.1f,.2f,.3f,.4f));t.Dilate();
                for(int y=0;y<3;y++)for(int x=0;x<3;x++)
                {Assert.IsFalse(t.IsPixelUsed(x,y)); /* GetPixel normalizes only used pixels */ Pixel(t,x,y,new Color(.1f,.2f,.3f,.4f));}
            }
        }
        [Test] public void PartialRegion_OutsidePixelsAndUsageAreUnchanged()
        {
            using(var t=new NordicsTextureWrapper(4,4,Settings(TextureDataType.NormalTangent)))
            {
                t.SetPixel(1,1,new Color(0,0,1,.25f));
                t.SetPixel(3,3,new Color(1,0,0,.5f));
                t.Dilate(new Rect(0,0,.5f,.5f));
                Pixel(t,0,0,new Color(0,0,1,1));Assert.IsTrue(t.IsPixelUsed(0,0));
                Pixel(t,3,3,new Color(1,0,0,.5f));Assert.IsTrue(t.IsPixelUsed(3,3));
                Assert.IsFalse(t.IsPixelUsed(2,0));Pixel(t,2,0,Color.clear);
            }
        }
        [Test] public void SequentialParallel_AgreeOnAllPixelValuesAndUsage()
        {
            int oldThreads=TextureProcessingSettings.MaxThreads,oldMinimum=TextureProcessingSettings.MinParallelPixels;
            try
            {
                using(var first=new NordicsTextureWrapper(37,29,Settings(TextureDataType.NormalObject)))
                {
                    first.SetPixel(2,3,new Color(1,0,0,.4f));
                    first.SetPixel(30,19,new Color(0,1,0,.7f));
                    first.SetPixel(9,24,new Color(0,0,1,1.4f));
                    using(var second=first.Clone())
                    {
                        TextureProcessingSettings.MinParallelPixels=0;
                        TextureProcessingSettings.MaxThreads=1;first.Dilate();
                        TextureProcessingSettings.MaxThreads=4;second.Dilate();
                        for(int y=0;y<29;y++)for(int x=0;x<37;x++)
                        {Pixel(second,x,y,first.GetPixel(x,y));Assert.AreEqual(first.IsPixelUsed(x,y),second.IsPixelUsed(x,y));}
                    }
                }
            }
            finally {TextureProcessingSettings.MaxThreads=oldThreads;TextureProcessingSettings.MinParallelPixels=oldMinimum;}
        }
        [Test] public void PaintTriangle_FractionalAlphaRemainsRawUntilDilate()
        {
            using(var t=new NordicsTextureWrapper(2,2,Settings(TextureDataType.NormalTangent)))
            {
                t.PaintTriangle(new Vector2(0,0),new Vector2(.5f,0),new Vector2(0,.5f),
                    new Color(0,0,1,1),new Color(0,0,1,1),new Color(0,0,1,1));
                Assert.IsTrue(t.IsPixelUsed(0,0));
                Assert.AreEqual(.625f,t.GetPixel(0,0).a,.00001f);
                t.Dilate();
                for(int y=0;y<2;y++)for(int x=0;x<2;x++)
                {Assert.IsTrue(t.IsPixelUsed(x,y));Assert.AreEqual(1,t.GetPixel(x,y).a,.00001f);}
            }
        }
    }
}
