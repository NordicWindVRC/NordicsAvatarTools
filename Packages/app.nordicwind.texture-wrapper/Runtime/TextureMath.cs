using System;
using UnityEngine;
namespace NordicWind.TextureWrapper
{
    internal static class TextureMath
    {
        internal static float Select(Color c, TextureChannel channel, bool usage = false)
        {
            switch (channel)
            {
                case TextureChannel.R: return c.r;
                case TextureChannel.G: return c.g;
                case TextureChannel.B: return c.b;
                case TextureChannel.A: return c.a;
                case TextureChannel.AverageRGB: return (c.r + c.g + c.b) / 3f;
                case TextureChannel.Usage: return usage ? 1f : 0f;
                default: throw new ArgumentOutOfRangeException(nameof(channel));
            }
        }
        internal static float MaskBlend(float a, float b, TextureMaskBlendMode mode)
        {
            float v;
            switch (mode)
            {
                case TextureMaskBlendMode.Replace: v = b; break;
                case TextureMaskBlendMode.Subtract: v = a-b; break;
                case TextureMaskBlendMode.Multiply: v = a*b; break;
                case TextureMaskBlendMode.Divide: v = a / Mathf.Max(b, .0001f); break;
                case TextureMaskBlendMode.Min: v = Mathf.Min(a,b); break;
                case TextureMaskBlendMode.Max: v = Mathf.Max(a,b); break;
                case TextureMaskBlendMode.Average: v = (a+b)*.5f; break;
                case TextureMaskBlendMode.Add: v = a+b; break;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
            return Mathf.Clamp01(v);
        }
        internal static float EvaluateMask(Color sample, bool used, TextureMaskOptions options)
        {
            if (options == null) options = new TextureMaskOptions();
            float v;
            if (options.UseChannelBlending)
            {
                Vector4 w=options.ChannelStrengths;
                v=sample.r*w.x;
                v=Mathf.LerpUnclamped(v, MaskBlend(v,sample.g,options.ChannelBlendMode),w.y);
                v=Mathf.LerpUnclamped(v, MaskBlend(v,sample.b,options.ChannelBlendMode),w.z);
                v=Mathf.LerpUnclamped(v, MaskBlend(v,sample.a,options.ChannelBlendMode),w.w);
            }
            else v=Select(sample,options.Channel,used);
            v=Mathf.Clamp01((v-options.Min)/Mathf.Max(options.Max-options.Min,.0001f));
            v=Mathf.Pow(v,options.Gamma);
            v=Mathf.Clamp01(v*options.Strength+options.Offset);
            return options.Invert ? 1f-v : v;
        }
        internal static float Blend(float a,float b,TextureBlendMode mode)
        {
            switch(mode)
            {
                case TextureBlendMode.Replace: return b;
                case TextureBlendMode.Darken: return Mathf.Min(a,b);
                case TextureBlendMode.Multiply: return a*b;
                case TextureBlendMode.Lighten: return Mathf.Max(a,b);
                case TextureBlendMode.Screen: return 1f-(1f-a)*(1f-b);
                case TextureBlendMode.Subtract: return a-b;
                case TextureBlendMode.Add: return a+b;
                case TextureBlendMode.Overlay: return a<=.5f ? 2f*a*b : 1f-2f*(1f-a)*(1f-b);
                case TextureBlendMode.Mixed: return a+a*b; // brightens by the blend value, scaled by the base
                case TextureBlendMode.Divide: return a/Mathf.Max(Mathf.Abs(b),.0001f);
                case TextureBlendMode.Average: return (a+b)*.5f;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
        internal static Vector3 BlendNormals(Vector3 dst, Vector3 src, TextureBlendMode mode)
        {
            dst=MathUtil.SafeNormal(dst); src=MathUtil.SafeNormal(src);
            if(mode==TextureBlendMode.NormalWhiteout) return MathUtil.SafeNormal(new Vector3(dst.x+src.x,dst.y+src.y,dst.z*src.z));
            if(mode==TextureBlendMode.NormalLerp) return MathUtil.SafeNormal((dst+src)*.5f);
            // RNM: Mikkelsen / Hill packed-vector formulation for Z-positive tangent normals.
            if(mode==TextureBlendMode.NormalRNM)
            {
                Vector3 t=dst+Vector3.forward;
                Vector3 u=new Vector3(-src.x,-src.y,src.z);
                return MathUtil.SafeNormal(t*Vector3.Dot(t,u)/Mathf.Max(t.z,1e-6f)-u);
            }
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        /// <summary>Applies one operation. <paramref name="color"/> is the operation's color
        /// for this pixel: <c>Parameters.Color</c>, multiplied by <c>Parameters.ColorMap</c> when set.</summary>
        internal static Color Apply(Color input,ColorOperation op,Color color)
        {
            var p=op.Parameters ?? throw new ArgumentException("Color operation parameters are null.");
            float a=p.Amount;
            Color o=input;
            switch(op.Opcode)
            {
                case ColorOperationCode.Multiply: o = new Color(input.r*color.r,input.g*color.g,input.b*color.b,input.a*color.a); break;
                case ColorOperationCode.Tint: o = new Color(input.r*color.r,input.g*color.g,input.b*color.b,input.a); break;
                case ColorOperationCode.Colorize: {
                    // Perceptual tint in OKLab: keeps the pixel's lightness, takes the hue from
                    // the color, and keeps as much of the pixel's chroma as the color is
                    // saturated (a grey color makes the pixel grey). Mixed in by color.a.
                    Vector3 lab=ToOKLab(new Vector3(input.r,input.g,input.b));
                    Vector3 tintLab=ToOKLab(new Vector3(color.r,color.g,color.b));
                    float hue=Mathf.Atan2(tintLab.z,tintLab.y);
                    float tintMin=Mathf.Min(color.r,Mathf.Min(color.g,color.b)),tintMax=Mathf.Max(color.r,Mathf.Max(color.g,color.b));
                    float tintSaturation=tintMax>.0001f?1f-tintMin/tintMax:0f;
                    float chroma=new Vector2(lab.y,lab.z).magnitude*tintSaturation;
                    lab.y=Mathf.Cos(hue)*chroma;lab.z=Mathf.Sin(hue)*chroma;
                    Vector3 rgb=NonNegative(FromOKLab(lab));
                    o=MathUtil.Lerp(input,new Color(rgb.x,rgb.y,rgb.z,input.a),color.a);
                    break;
                }
                case ColorOperationCode.Add: o=input+color; break;
                case ColorOperationCode.Invert: o=new Color(1-input.r,1-input.g,1-input.b,input.a); break;
                case ColorOperationCode.Clamp: o=new Color(Mathf.Clamp(input.r,a,p.Amount2),Mathf.Clamp(input.g,a,p.Amount2),Mathf.Clamp(input.b,a,p.Amount2),input.a);break;
                case ColorOperationCode.Gamma: o=new Color(PowSigned(input.r,a),PowSigned(input.g,a),PowSigned(input.b,a),input.a);break;
                case ColorOperationCode.Exposure: {float m=Mathf.Pow(2,a);o=new Color(input.r*m,input.g*m,input.b*m,input.a);break;}
                case ColorOperationCode.Brightness: o=new Color(input.r*a,input.g*a,input.b*a,input.a);break;
                case ColorOperationCode.Contrast: o=new Color((input.r-.5f)*a+.5f,(input.g-.5f)*a+.5f,(input.b-.5f)*a+.5f,input.a);break;
                case ColorOperationCode.Saturation: {float l=input.r*.30f+input.g*.59f+input.b*.11f;float k=1f-a;o=new Color(Mathf.LerpUnclamped(input.r,l,k),Mathf.LerpUnclamped(input.g,l,k),Mathf.LerpUnclamped(input.b,l,k),input.a);break;}
                case ColorOperationCode.HueShift:
                case ColorOperationCode.HueReplace: {
                    if(p.ColorSpace==TextureColorSpace.OKLab)
                    {
                        Vector3 lab=ToOKLab(new Vector3(input.r,input.g,input.b));
                        float chroma=new Vector2(lab.y,lab.z).magnitude;
                        float angle=op.Opcode==ColorOperationCode.HueShift ? Mathf.Atan2(lab.z,lab.y)+a*Mathf.PI*2f : a*Mathf.PI*2f;
                        lab.y=Mathf.Cos(angle)*chroma;lab.z=Mathf.Sin(angle)*chroma;
                        Vector3 rgb=FromOKLab(lab);o=new Color(rgb.x,rgb.y,rgb.z,input.a);
                    }
                    else
                    {
                        Color.RGBToHSV(input,out float h,out float s,out float v);
                        h = op.Opcode == ColorOperationCode.HueShift ? Mathf.Repeat(h+a,1f) : Mathf.Repeat(a,1f);
                        Color rgb=Color.HSVToRGB(h,s,v,true);o=new Color(rgb.r,rgb.g,rgb.b,input.a);
                    }
                    break;
                }
                case ColorOperationCode.Posterize: {float count=Mathf.Max(1,Mathf.Round(a));o=new Color(Mathf.Floor(input.r*count)/count,Mathf.Floor(input.g*count)/count,Mathf.Floor(input.b*count)/count,input.a);break;}
                case ColorOperationCode.Levels: {float range=Mathf.Max(1e-6f,p.Amount2-a);o=new Color((input.r-a)/range,(input.g-a)/range,(input.b-a)/range,input.a);break;}
                case ColorOperationCode.Chromatize: {
                    // Raises OKLab chroma by (1 + Amount), limited to the chroma of the fully
                    // saturated color with the same hue, so colors never pass full saturation.
                    Vector3 linear=new Vector3(input.r,input.g,input.b);
                    float min=Mathf.Min(linear.x,Mathf.Min(linear.y,linear.z));
                    float max=Mathf.Max(linear.x,Mathf.Max(linear.y,linear.z));
                    Vector3 reference=max-min>.0001f?(linear-Vector3.one*min)/(max-min):Vector3.right;
                    Vector3 maxLab=ToOKLab(reference);
                    float maxChroma=new Vector2(maxLab.y,maxLab.z).magnitude;
                    Vector3 lab=ToOKLab(linear);
                    float current=new Vector2(lab.y,lab.z).magnitude;
                    float angle=Mathf.Atan2(lab.z,lab.y);
                    float chroma=Mathf.Min(current*(1f+a),maxChroma);
                    lab.y=chroma*Mathf.Cos(angle);lab.z=chroma*Mathf.Sin(angle);
                    Vector3 rgb=NonNegative(FromOKLab(lab));o=new Color(rgb.x,rgb.y,rgb.z,input.a);
                    break;
                }
                case ColorOperationCode.GradientMap: {
                    if(p.Gradient==null) throw new ArgumentException("GradientMap requires a gradient wrapper.");
                    float l=input.r*.3f+input.g*.59f+input.b*.11f;
                    Color sample=p.Gradient.Sample(new Vector2(l,.5f));o=new Color(sample.r,sample.g,sample.b,input.a);break;
                }
                default: throw new ArgumentOutOfRangeException(nameof(op.Opcode));
            }
            return o;
        }
        // OKLab results can fall slightly outside the RGB gamut; negative light isn't meaningful.
        private static Vector3 NonNegative(Vector3 c) => new Vector3(Mathf.Max(0f,c.x),Mathf.Max(0f,c.y),Mathf.Max(0f,c.z));
        private static float Cbrt(float x) => Mathf.Sign(x)*Mathf.Pow(Mathf.Abs(x),1f/3f);
        private static Vector3 ToOKLab(Vector3 c)
        {
            float l=Cbrt(.4122214708f*c.x+.5363325363f*c.y+.0514459929f*c.z);
            float m=Cbrt(.2119034982f*c.x+.6806995451f*c.y+.1073969566f*c.z);
            float s=Cbrt(.0883024619f*c.x+.2817188376f*c.y+.6299787005f*c.z);
            return new Vector3(.2104542553f*l+.7936177850f*m-.0040720468f*s,
                1.9779984951f*l-2.4285922050f*m+.4505937099f*s,
                .0259040371f*l+.7827717662f*m-.8086757660f*s);
        }
        private static Vector3 FromOKLab(Vector3 c)
        {
            float l=c.x+.3963377774f*c.y+.2158037573f*c.z;
            float m=c.x-.1055613458f*c.y-.0638541728f*c.z;
            float s=c.x-.0894841775f*c.y-1.2914855480f*c.z;
            l=l*l*l;m=m*m*m;s=s*s*s;
            return new Vector3(4.0767416621f*l-3.3077115913f*m+.2309699292f*s,
               -1.2684380046f*l+2.6097574011f*m-.3413193965f*s,
               -.0041960863f*l-.7034186147f*m+1.7076147010f*s);
        }
        private static float PowSigned(float x,float exponent) => Mathf.Sign(x)*Mathf.Pow(Mathf.Abs(x),exponent);
    }
}
