using System;
using NUnit.Framework;
using UnityEngine;

namespace NordicWind.TextureWrapper.Tests
{
    public class GeometryRegressionTests
    {
        private static TextureSettings NormalSettings(TextureDataType type=TextureDataType.NormalWorld)
            => new TextureSettings { DataType=type, SRGB=false, Mipmaps=false };
        private static readonly TrianglePaintOptions Quality = new TrianglePaintOptions
            { Mode=TriangleRasterizationMode.Quality, SamplesPerAxis=4 };
        private static Color V(Vector3 v,float a=1f)=>new Color(v.x,v.y,v.z,a);
        private static void Paint(NordicsTextureWrapper w, Color v)
            =>w.PaintTriangle(Vector2.zero,Vector2.right,Vector2.up,v,v,v,Quality);
        private static void Near(float expected,float actual,float epsilon=1e-4f)
            => Assert.That(actual,Is.EqualTo(expected).Within(epsilon));
        [Test] public void Triangle_InterpolatesVertexNormalsUsingBarycentricCoordinates()
        {
            using(var w=new NordicsTextureWrapper(4,4,NormalSettings()))
            {
                w.PaintTriangle(Vector2.zero,Vector2.right,Vector2.up,
                    V(Vector3.right),V(Vector3.up),V(Vector3.forward),Quality);
                // Pixel (0,0) is fully inside; at UV (0.125,0.125),
                // barycentric weights are (0.75,0.125,0.125).
                Vector3 expected=new Vector3(.75f,.125f,.125f).normalized;
                Color actual=w.GetPixel(0,0);
                Near(expected.x,actual.r,.04f);Near(expected.y,actual.g,.04f);
                Near(expected.z,actual.b,.04f);
            }
        }
        [Test] public void RepeatedNormalTriangles_PreserveRawAlphaAndDirection()
        {
            using(var once=new NordicsTextureWrapper(8,8,NormalSettings()))
            using(var twice=new NordicsTextureWrapper(8,8,NormalSettings()))
            {
                var n=V(new Vector3(.6f,0f,.8f));
                Paint(once,n); Paint(twice,n);Paint(twice,n);
                Color[] encodedTwice=twice.GetEncodedPixels();
                for(int y=0;y<8;y++)for(int x=0;x<8;x++)
                {
                    Color a=once.GetPixel(x,y),b=twice.GetPixel(x,y);
                    Assert.That(b.a,Is.GreaterThanOrEqualTo(a.a));
                    Near(Mathf.Clamp01(b.a), encodedTwice[y*8+x].a);
                    if (!once.IsPixelUsed(x,y)) continue;
                    Near(a.r,b.r);Near(a.g,b.g);Near(a.b,b.b);
                }
                Near(2f,twice.GetPixel(0,0).a);
                Near(1f,twice.GetEncodedPixels()[0].a);
            }
        }
        [Test] public void SharedEdgeAndPartialCoverage_ClampOnlyAtOutput()
        {
            using(var w=new NordicsTextureWrapper(8,8,NormalSettings()))
            {
                var n=V(Vector3.forward);
                w.PaintTriangle(Vector2.zero,Vector2.right,Vector2.up,n,n,n,Quality);
                w.PaintTriangle(Vector2.right,Vector2.one,Vector2.up,n,n,n,Quality);
                Color[] encoded=w.GetEncodedPixels();
                for(int y=0;y<8;y++)for(int x=0;x<8;x++)
                    Assert.That(encoded[y*8+x].a,Is.InRange(0f,1f));
                Assert.That(w.GetPixel(3,3).a,Is.GreaterThan(0f));
            }
        }
        [Test] public void OpposingAndPerpendicularNormals_AreFinite()
        {
            using(var w=new NordicsTextureWrapper(8,8,NormalSettings()))
            {
                Paint(w,V(Vector3.forward));Paint(w,V(Vector3.back));
                Color n=w.GetPixel(0,0);
                Assert.IsFalse(float.IsNaN(n.r)||float.IsNaN(n.g)||float.IsNaN(n.b));
                Near(2f,n.a);
                Near(1f,w.GetEncodedPixels()[0].a);
            }
            using(var w=new NordicsTextureWrapper(8,8,NormalSettings()))
            {
                Paint(w,V(Vector3.forward));Paint(w,V(Vector3.right));
                Color n=w.GetPixel(0,0);Near(Mathf.Sqrt(.5f),n.r);Near(Mathf.Sqrt(.5f),n.b);
            }
        }
        [Test] public void OverlapCoverageAccumulatesAndClampsAtOutput()
        {
            using(var w=new NordicsTextureWrapper(8,8,NormalSettings()))
            {
                for(int i=0;i<4;i++)Paint(w,V(Vector3.up));
                Near(4f,w.GetPixel(0,0).a);
                Near(1f,w.GetEncodedPixels()[0].a);
                Near(1f,w.GetPixel(0,0).g);
            }
        }
        [Test] public void TriangleColorAccumulation_PreservesOverbrightRgbAndCoverageUntilEncoding()
        {
            // Linear output, so the encoded values show clamping only, without sRGB encoding.
            using(var w=new NordicsTextureWrapper(8,8,new TextureSettings { DataType=TextureDataType.Color, SRGB=false, Mipmaps=false }))
            {
                var color=new Color(.7f,.4f,.2f,.75f);
                for(int i=0;i<3;i++) Paint(w,color);
                Color raw=w.GetPixel(0,0);
                Near(2.1f,raw.r); Near(1.2f,raw.g); Near(.6f,raw.b); Near(2.25f,raw.a);
                Color encoded=w.GetEncodedPixels()[0];
                Near(1f,encoded.r); Near(1f,encoded.g); Near(.6f,encoded.b); Near(1f,encoded.a);
                // Export must not modify the authoritative accumulation.
                Near(2.25f,w.GetPixel(0,0).a);
            }
        }
        [Test] public void PartialCoverage_RemainsFractionalUntilFinalOutput()
        {
            using(var w=new NordicsTextureWrapper(4,4,new TextureSettings { DataType=TextureDataType.Color, Mipmaps=false }))
            {
                var c=new Color(1f,1f,1f,1f);
                w.PaintTriangle(Vector2.zero,new Vector2(.125f,0),new Vector2(0,.125f),c,c,c,Quality);
                Color raw=w.GetPixel(0,0);
                Assert.That(raw.a,Is.GreaterThan(0f).And.LessThan(1f));
                Near(raw.a,w.GetEncodedPixels()[0].a);
                Assert.IsTrue(w.IsPixelUsed(0,0));
            }
        }
        [Test] public void FillUnused_ProducesOpaqueFlatNormalWithoutChangingUsage()
        {
            using(var w=new NordicsTextureWrapper(4,4,NormalSettings(TextureDataType.NormalTangent)))
            {
                w.SetPixel(0,0,V(Vector3.right,0.25f));
                w.FillUnused(V(Vector3.forward),false);
                Assert.IsFalse(w.IsPixelUsed(3,3));
                Color c=w.GetPixel(3,3);
                Near(0,c.r);Near(0,c.g);Near(1,c.b);Near(1,c.a);
                Near(.25f,w.GetPixel(0,0).a);
            }
        }
        [Test] public void WorldReferenceToTargetBasis_PreservesDirectionAndMirroredHandedness()
        {
            using(var original=new NordicsTextureWrapper(1,1,NormalSettings()))
            using(var target=new NordicsTextureWrapper(1,1,NormalSettings()))
            using(var t=new NordicsTextureWrapper(1,1,NormalSettings()))
            using(var b=new NordicsTextureWrapper(1,1,NormalSettings()))
            using(var n=new NordicsTextureWrapper(1,1,NormalSettings()))
            {
                original.SetPixel(0,0,V(Vector3.up));target.SetPixel(0,0,V(Vector3.forward));
                t.SetPixel(0,0,V(Vector3.right));b.SetPixel(0,0,V(Vector3.down));n.SetPixel(0,0,V(Vector3.forward));
                using(var correction=original.BakeNormalDifference(target,new TangentBasisData{Tangent=t,Bitangent=b,Normal=n}))
                {
                    Color v=correction.GetPixel(0,0);
                    Near(-1f,v.g);Near(0f,v.r);Near(0f,v.b);
                }
                b.SetPixel(0,0,V(Vector3.up));
                using(var correction=original.BakeNormalDifference(target,new TangentBasisData{Tangent=t,Bitangent=b,Normal=n}))
                    Near(1f,correction.GetPixel(0,0).g);
            }
        }
        [Test] public void NormalDifference_DoesNotReverseReferenceTarget()
        {
            using(var reference=new NordicsTextureWrapper(1,1,NormalSettings()))
            using(var target=new NordicsTextureWrapper(1,1,NormalSettings()))
            using(var t=new NordicsTextureWrapper(1,1,NormalSettings()))
            using(var b=new NordicsTextureWrapper(1,1,NormalSettings()))
            using(var n=new NordicsTextureWrapper(1,1,NormalSettings()))
            {
                reference.SetPixel(0,0,V(Vector3.right));target.SetPixel(0,0,V(Vector3.forward));
                t.SetPixel(0,0,V(Vector3.right));b.SetPixel(0,0,V(Vector3.up));n.SetPixel(0,0,V(Vector3.forward));
                using(var correction=reference.BakeNormalDifference(target,new TangentBasisData{Tangent=t,Bitangent=b,Normal=n}))
                { Near(1f,correction.GetPixel(0,0).r);Near(0f,correction.GetPixel(0,0).b); }
            }
        }
        [Test] public void ReversedUVWinding_ProducesEquivalentRasterValues()
        {
            using(var a=new NordicsTextureWrapper(8,8,NormalSettings()))
            using(var b=new NordicsTextureWrapper(8,8,NormalSettings()))
            {
                Color c=V(new Vector3(.3f,.4f,.8660254f));
                a.PaintTriangle(Vector2.zero,Vector2.right,Vector2.up,c,c,c,Quality);
                b.PaintTriangle(Vector2.zero,Vector2.up,Vector2.right,c,c,c,Quality);
                for(int y=0;y<8;y++)for(int x=0;x<8;x++)
                {
                    Assert.AreEqual(a.IsPixelUsed(x,y),b.IsPixelUsed(x,y));
                    var x1=a.GetPixel(x,y);var x2=b.GetPixel(x,y);
                    Near(x1.a,x2.a); Near(x1.r,x2.r);Near(x1.g,x2.g);Near(x1.b,x2.b);
                }
            }
        }
        [Test] public void Triangle_InvalidParametersAreRejected()
        {
            using(var w=new NordicsTextureWrapper(4,4,NormalSettings()))
            {
                Assert.Throws<ArgumentException>(()=>w.PaintTriangle(new Vector2(float.NaN,0),Vector2.right,Vector2.up,Color.white,Color.white,Color.white));
                Assert.Throws<ArgumentOutOfRangeException>(()=>w.PaintTriangle(Vector2.zero,Vector2.right,Vector2.up,Color.white,Color.white,Color.white,new TrianglePaintOptions{SamplesPerAxis=0}));
                Assert.Throws<ArgumentException>(()=>w.FillUnused(new Color(float.NaN,0,1,1)));
            }
        }
    }
}
