using System;
using NUnit.Framework;
using UnityEngine;
namespace NordicWind.TextureWrapper.Tests
{
    public class WrapperTests
    {
        private static TextureSettings ColorSettings()=>new TextureSettings{DataType=TextureDataType.Color,SRGB=false,WrapMode=TextureWrapMode.Clamp};
        [Test] public void Clone_IndependentAndEmpty()
        {
            using(var a=new NordicsTextureWrapper(4,4,ColorSettings()))
            {
                a.SetPixel(0,0,Color.red);
                using(var b=a.Clone())using(var c=a.CloneEmpty(8,8))
                {
                    b.SetPixel(0,0,Color.blue);
                    b.Settings.Crunch=true;
                    Assert.AreEqual(Color.red,a.GetPixel(0,0));Assert.IsFalse(a.Settings.Crunch);
                    Assert.IsFalse(c.IsPixelUsed(0,0));Assert.AreEqual(8,c.Width);
                }
            }
        }
        [Test] public void Usage_AnyPositiveChannelResultIsTrue()
        {
            using(var src=new NordicsTextureWrapper(2,2,ColorSettings()))using(var dst=new NordicsTextureWrapper(2,2,ColorSettings()))
            {
                src.Fill(new Color(.00001f,0,0,1));
                dst.Blit(src,sourceChannel:TextureChannel.R,destinationChannel:TextureChannel.Usage);
                Assert.IsTrue(dst.IsPixelUsed(0,0));
                src.Fill(Color.clear);
                dst.Blit(src,sourceChannel:TextureChannel.R,destinationChannel:TextureChannel.Usage);
                Assert.IsFalse(dst.IsPixelUsed(0,0));
            }
        }
        [Test] public void MaskRemap_MapsMinMaxToZeroOne()
        {
            var mask=new TextureMaskOptions{Channel=TextureChannel.R,Min=.25f,Max=.75f};
            Assert.AreEqual(.5f,TextureMaskCheck(new Color(.5f,0,0,1),mask),.0001f);
            Assert.AreEqual(0f,TextureMaskCheck(new Color(.1f,0,0,1),mask),.0001f);
            Assert.AreEqual(1f,TextureMaskCheck(new Color(.9f,0,0,1),mask),.0001f);
        }
        private static float TextureMaskCheck(Color c,TextureMaskOptions op)
        {
            using(var src=new NordicsTextureWrapper(1,1,ColorSettings()))using(var mask=new NordicsTextureWrapper(1,1,ColorSettings()))using(var dst=new NordicsTextureWrapper(1,1,ColorSettings()))
            {
                src.Fill(Color.white);mask.Fill(c);dst.Blit(src,mask:mask,maskOptions:op);
                return dst.GetPixel(0,0).r;
            }
        }
        [Test] public void NullablePadding_DoesNotChangeUsage()
        {
            using(var src=new NordicsTextureWrapper(4,4,ColorSettings()))using(var dst=new NordicsTextureWrapper(8,8,ColorSettings()))
            {
                src.Fill(Color.white);
                dst.Blit(src,padding:null);
                Assert.IsFalse(dst.IsPixelUsed(3,3));
                dst.Blit(src,padding:0);
                Assert.IsTrue(dst.IsPixelUsed(3,3));
            }
        }
        [Test] public void Dilation_UsesNearestOriginalValidPixel()
        {
            using(var tex=new NordicsTextureWrapper(5,1,ColorSettings()))
            {
                tex.SetPixel(0,0,Color.red);tex.SetPixel(4,0,Color.blue);
                tex.Dilate();Assert.AreEqual(Color.red,tex.GetPixel(1,0));Assert.AreEqual(Color.blue,tex.GetPixel(3,0));
                Assert.IsTrue(tex.IsPixelUsed(2,0));
            }
        }
        [Test] public void Normal_StorageIsSignedAndNormalizesLazily()
        {
            using(var tex=new NordicsTextureWrapper(2,2,new TextureSettings{DataType=TextureDataType.NormalTangent,SRGB=false}))
            {
                tex.SetPixel(0,0,new Color(-3,0,4,1));
                Color result=tex.GetPixel(0,0);
                Assert.AreEqual(-.6f,result.r,.00001f);Assert.AreEqual(.8f,result.b,.00001f);
            }
        }
        [Test] public void Triangle_AccumulationSetsUsage()
        {
            using(var tex=new NordicsTextureWrapper(8,8,ColorSettings()))
            {
                tex.PaintTriangle(new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),Color.white,Color.white,Color.white);
                Assert.IsTrue(tex.IsPixelUsed(0,0));Assert.Greater(tex.GetPixel(0,0).r,0f);
            }
        }
        [Test] public void InvalidPaddingThrows()
        {
            using(var src=new NordicsTextureWrapper(2,2))using(var dst=new NordicsTextureWrapper(4,4))
                Assert.Throws<ArgumentException>(()=>dst.Blit(src,padding:3));
        }
        [Test] public void DisposeThrows()
        {
            var tex=new NordicsTextureWrapper(2,2);tex.Dispose();
            Assert.Throws<ObjectDisposedException>(()=>tex.GetPixel(0,0));
        }
        [Test] public void OutputChannels_AutoDetectsEncodedAlpha()
        {
            using(var tex=new NordicsTextureWrapper(2,2,ColorSettings()))
            {
                tex.Fill(new Color(0.2f,0.4f,0.6f,1f));
                Assert.AreEqual(TextureFormat.RGB24,tex.GetOutputTextureFormat());
                tex.SetPixel(0,0,new Color(1f,0f,0f,0.999999f));
                Assert.AreEqual(TextureFormat.RGB24,tex.GetOutputTextureFormat());
                tex.SetPixel(0,0,new Color(1f,0f,0f,0.5f));
                Assert.AreEqual(TextureFormat.RGBA32,tex.GetOutputTextureFormat());
                tex.Settings.OutputChannels=TextureOutputChannels.RGB;
                Assert.AreEqual(TextureFormat.RGB24,tex.GetOutputTextureFormat());
                tex.Settings.OutputChannels=TextureOutputChannels.RGBA;
                Assert.AreEqual(TextureFormat.RGBA32,tex.GetOutputTextureFormat());
                tex.Settings.OutputChannels=TextureOutputChannels.Auto;
                Assert.AreEqual(TextureFormat.RGBA32,tex.GetOutputTextureFormat());
            }
        }
        [Test] public void OutputChannels_InMemoryTextureChangesFormatWhenNeeded()
        {
            using(var tex=new NordicsTextureWrapper(2,2,ColorSettings()))
            {
                tex.Fill(Color.white);
                Assert.AreEqual(TextureFormat.RGB24,tex.GetTexture2D().format);
                tex.SetPixel(0,0,new Color(1f,1f,1f,.2f));
                Assert.IsTrue(tex.IsDirty);
                Assert.AreEqual(TextureFormat.RGBA32,tex.GetTexture2D().format);
                tex.Settings.OutputChannels=TextureOutputChannels.RGB;
                Assert.AreEqual(TextureFormat.RGB24,tex.GetTexture2D().format);
            }
        }
        [Test] public void OutputChannels_InvalidEnumRejected()
        {
            var settings=ColorSettings();
            settings.OutputChannels=(TextureOutputChannels)999;
            Assert.Throws<ArgumentOutOfRangeException>(()=>settings.Validate());
        }
    }
}
