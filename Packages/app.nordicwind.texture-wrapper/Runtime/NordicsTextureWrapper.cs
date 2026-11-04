using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace NordicWind.TextureWrapper
{
    public sealed class NordicsTextureWrapper : IDisposable
    {
        private Color[] _pixels;
        private byte[] _usage;
        private bool _normalized;
        private bool _disposed;
        private bool _pixelsDirty = true;
        private Texture2D _cached;
        private bool _ownsCache;
        private string _settingsFingerprint;
        private INordicsTextureExporter _exporter;
        private string _exportName;
        private TextureFormat _cachedFormat;
        private TextureFormat? _lastExportFormat;
        public int Width { get; }
        public int Height { get; }
        public bool IsReadOnly { get; }
        public TextureSettings Settings { get; }
        public bool IsDirty { get { Check(); return _pixelsDirty || _settingsFingerprint != Settings.Fingerprint(); } }
        private bool Normal => Settings.IsNormal;
        public NordicsTextureWrapper(int width,int height,TextureSettings settings=null) : this(width,height,settings,false) { }
        private NordicsTextureWrapper(int width,int height,TextureSettings settings,bool readOnly)
        {
            if(width<=0 || height<=0 || (long)width*height>int.MaxValue) throw new ArgumentOutOfRangeException(nameof(width),"Dimensions must be positive and fit in an array.");
            Settings=(settings??new TextureSettings()).Clone(); Settings.Validate();
            Width=width;Height=height;IsReadOnly=readOnly;
            _pixels=new Color[checked(width*height)];_usage=new byte[_pixels.Length];
            _normalized=true;
        }
        public NordicsTextureWrapper(Texture2D source,bool readOnly=true)
            : this(source!=null?source.width:throw new ArgumentNullException(nameof(source)),source.height,InferSettings(source),readOnly)
        {
            if(!source.isReadable) throw new ArgumentException("Source texture is not readable. Enable Read/Write or provide a GPU-readback texture.",nameof(source));
            if(Settings.SRGB && !Normal)
            {
                // The buffer holds what a shader reads: the GPU decodes sRGB textures to
                // linear when sampling, so decode here too. sRGB formats are 8-bit, so a
                // lookup table is exact. Alpha is never sRGB-encoded.
                Color32[] bytes=source.GetPixels32();
                float[] lut=MathUtil.SRGBByteToLinear;
                for(int i=0;i<bytes.Length;i++)
                {
                    Color32 b=bytes[i];
                    _pixels[i]=new Color(lut[b.r],lut[b.g],lut[b.b],b.a/255f);_usage[i]=1;
                }
            }
            else
            {
                Color[] colors=source.GetPixels();
                for(int i=0;i<colors.Length;i++)
                {
                    Color c=colors[i];
                    if(Normal) c=new Color(c.r*2f-1f,c.g*2f-1f,c.b*2f-1f,c.a);
                    _pixels[i]=c;_usage[i]=1;
                }
            }
            _normalized=!Normal;
        }
        private static TextureSettings InferSettings(Texture2D tex)
        {
            return new TextureSettings { WrapMode=tex.wrapMode,FilterMode=tex.filterMode,AnisoLevel=tex.anisoLevel, Mipmaps=tex.mipmapCount>1,
                SRGB=GraphicsFormatUtility.IsSRGBFormat(tex.graphicsFormat) };
        }
        private void Check() {if(_disposed)throw new ObjectDisposedException(nameof(NordicsTextureWrapper));}
        private void Writable() {Check();if(IsReadOnly)throw new InvalidOperationException("Wrapper is read-only. Clone it before editing.");}
        private static void ValidateUV(Vector2 uv) {if(!MathUtil.Finite(uv))throw new ArgumentException("UV must be finite.",nameof(uv));}
        private static void ValidateColor(Color c) {if(!MathUtil.Finite(c))throw new ArgumentException("Color components must be finite.");}
        private static void ValidateRect(Rect? rect)
        {
            if(!rect.HasValue)return;
            Rect r=rect.Value;
            if(!MathUtil.Finite(r.xMin)||!MathUtil.Finite(r.xMax)||!MathUtil.Finite(r.yMin)||!MathUtil.Finite(r.yMax)||r.width<0||r.height<0)throw new ArgumentException("Region must be finite and nonnegative.");
        }
        private int Index(int x,int y)
        {
            if(x<0||y<0||x>=Width||y>=Height)throw new ArgumentOutOfRangeException(nameof(x),"Pixel coordinate outside texture.");
            return y*Width+x;
        }
        private static void ForRows(int width,int height,Action<int> action)
        {
            if((long)width*height<TextureProcessingSettings.MinParallelPixels||TextureProcessingSettings.MaxThreads==1)
            {for(int y=0;y<height;y++)action(y);return;}
            int max=TextureProcessingSettings.MaxThreads==0?Environment.ProcessorCount:TextureProcessingSettings.MaxThreads;
            Parallel.For(0,height,new ParallelOptions{MaxDegreeOfParallelism=max},action);
        }
        private void Changed(bool invalidateNormals=true)
        {
            _pixelsDirty=true;
            if(invalidateNormals&&Normal)_normalized=false;
        }
        public NordicsTextureWrapper Clone()
        {
            Check();var v=new NordicsTextureWrapper(Width,Height,Settings);
            Array.Copy(_pixels,v._pixels,_pixels.Length);Array.Copy(_usage,v._usage,_usage.Length);v._normalized=_normalized;
            return v;
        }
        public NordicsTextureWrapper CloneEmpty(int? width=null,int? height=null)
        {Check();return new NordicsTextureWrapper(width??Width,height??Height,Settings);}
        public Color GetPixel(int x,int y) {Check();if(Normal)EnsureNormalized();return _pixels[Index(x,y)];}
        public Color GetPixel(Vector2 uv) {Check();ValidateUV(uv);return Sample(uv,TextureSamplingMode.Nearest);}
        public void SetPixel(int x,int y,Color color)
        {Writable();ValidateColor(color);int i=Index(x,y);_pixels[i]=color;_usage[i]=1;Changed();}
        public void SetPixel(Vector2 uv,Color color)
        {
            Writable();ValidateUV(uv);int x=Address(Mathf.FloorToInt(uv.x*Width),Width,Settings.WrapMode);int y=Address(Mathf.FloorToInt(uv.y*Height),Height,Settings.WrapMode);SetPixel(x,y,color);
        }
        public bool IsPixelUsed(int x,int y){Check();return _usage[Index(x,y)]!=0;}
        public bool IsPixelUsed(Vector2 uv){Check();ValidateUV(uv);return IsPixelUsed(Address(Mathf.FloorToInt(uv.x*Width),Width,Settings.WrapMode),Address(Mathf.FloorToInt(uv.y*Height),Height,Settings.WrapMode));}
        public void SetPixelUsed(int x,int y,bool used){Writable();_usage[Index(x,y)]=(byte)(used?1:0);}
        private static int Address(int x,int size,TextureWrapMode wrap)
        {
            if(wrap==TextureWrapMode.Repeat) return ((x%size)+size)%size;
            if(wrap==TextureWrapMode.Mirror||wrap==TextureWrapMode.MirrorOnce)
            {
                if(wrap==TextureWrapMode.MirrorOnce)x=Mathf.Clamp(x,-size,size*2-1);
                int p=((x%(2*size))+2*size)%(2*size);return p<size?p:2*size-1-p;
            }
            return Mathf.Clamp(x,0,size-1);
        }
        private Color Tap(int x,int y,TextureWrapMode wrap) => _pixels[Address(y,Height,wrap)*Width+Address(x,Width,wrap)];
        private bool UsedTap(int x,int y,TextureWrapMode wrap)=>_usage[Address(y,Height,wrap)*Width+Address(x,Width,wrap)]!=0;
        private static float Cubic(float x)
        {
            x=Mathf.Abs(x);if(x<1f)return (1.5f*x-2.5f)*x*x+1f;
            if(x<2f)return ((-.5f*x+2.5f)*x-4f)*x+2f;
            return 0f;
        }
        public Color Sample(Vector2 uv,TextureSamplingMode sampling=TextureSamplingMode.Bilinear,TextureWrapMode? wrap=null)
        {
            Check();ValidateUV(uv);if(Normal)EnsureNormalized();return SampleRaw(uv,sampling,wrap??Settings.WrapMode);
        }
        private Color SampleRaw(Vector2 uv,TextureSamplingMode sampling,TextureWrapMode wrap)
        {
            float fx=uv.x*Width-.5f,fy=uv.y*Height-.5f;
            if(sampling==TextureSamplingMode.Nearest)return Tap(Mathf.FloorToInt(fx+.5f),Mathf.FloorToInt(fy+.5f),wrap);
            int x=Mathf.FloorToInt(fx),y=Mathf.FloorToInt(fy);float u=fx-x,v=fy-y;
            if(sampling==TextureSamplingMode.Bilinear||sampling==TextureSamplingMode.Area)
            {
                return MathUtil.Lerp(MathUtil.Lerp(Tap(x,y,wrap),Tap(x+1,y,wrap),u),MathUtil.Lerp(Tap(x,y+1,wrap),Tap(x+1,y+1,wrap),u),v);
            }
            if(sampling!=TextureSamplingMode.Bicubic)throw new ArgumentOutOfRangeException(nameof(sampling));
            Color sum=Color.clear;float weight=0f;
            for(int j=-1;j<=2;j++)for(int i=-1;i<=2;i++)
            {float w=Cubic(i-u)*Cubic(j-v);sum+=Tap(x+i,y+j,wrap)*w;weight+=w;}
            return Mathf.Abs(weight)>1e-8f?sum*(1f/weight):Color.clear;
        }
        private bool SampleUsage(Vector2 uv,TextureSamplingMode sampling,TextureWrapMode wrap)
        {
            float fx=uv.x*Width-.5f,fy=uv.y*Height-.5f;
            int x=Mathf.FloorToInt(fx),y=Mathf.FloorToInt(fy);
            if(sampling==TextureSamplingMode.Nearest)return UsedTap(Mathf.FloorToInt(fx+.5f),Mathf.FloorToInt(fy+.5f),wrap);
            int r=sampling==TextureSamplingMode.Bicubic?2:1;
            for(int j=1-r;j<=r;j++)for(int i=1-r;i<=r;i++)if(UsedTap(x+i,y+j,wrap))return true;
            return false;
        }
        private static Rect Bounds(Rect? region)=>region??new Rect(0,0,1,1);
        private void PixelBounds(Rect? region,out int x0,out int y0,out int x1,out int y1)
        {
            ValidateRect(region);Rect r=Bounds(region);
            x0=Mathf.Clamp(Mathf.CeilToInt(r.xMin*Width-.00001f),0,Width);
            y0=Mathf.Clamp(Mathf.CeilToInt(r.yMin*Height-.00001f),0,Height);
            x1=Mathf.Clamp(Mathf.FloorToInt(r.xMax*Width+.00001f),0,Width);
            y1=Mathf.Clamp(Mathf.FloorToInt(r.yMax*Height+.00001f),0,Height);
            if(x1<x0||y1<y0)throw new ArgumentException("Invalid pixel region.");
        }
        private static Vector2 MapUV(Vector2 local,Rect sourceRegion,Vector2 scale,Vector2 offset,float degrees)
        {
            Vector2 p=local-new Vector2(.5f,.5f);
            float radians=degrees*Mathf.Deg2Rad;float cs=Mathf.Cos(radians),sn=Mathf.Sin(radians);
            p=new Vector2(cs*p.x-sn*p.y,sn*p.x+cs*p.y);
            p=Vector2.Scale(p,scale)+new Vector2(.5f,.5f)+offset;
            return new Vector2(sourceRegion.xMin+p.x*sourceRegion.width,sourceRegion.yMin+p.y*sourceRegion.height);
        }
        private static Vector3 RotateNormal(Vector3 n,float degrees)
        {
            float a=-degrees*Mathf.Deg2Rad,cs=Mathf.Cos(a),sn=Mathf.Sin(a);
            return new Vector3(n.x*cs-n.y*sn,n.x*sn+n.y*cs,n.z);
        }
        public NordicsTextureWrapper Resize(int width,int height,Rect? sourceRegion=null,Vector2? scale=null,Vector2? offset=null,float rotation=0f,TextureSamplingMode sampling=TextureSamplingMode.Bilinear,TextureWrapMode? wrap=null,IReadOnlyList<ColorOperation> operations=null)
        {
            Check();ValidateRect(sourceRegion);
            if(!MathUtil.Finite(rotation)||!MathUtil.Finite(scale??Vector2.one)||!MathUtil.Finite(offset??Vector2.zero))throw new ArgumentException("Transform must be finite.");
            var output=new NordicsTextureWrapper(width,height,Settings);Rect r=Bounds(sourceRegion);
            Vector2 s=scale??Vector2.one,o=offset??Vector2.zero;TextureWrapMode w=wrap??Settings.WrapMode;
            ValidateOperations(operations);if(Normal)EnsureNormalized();PrepareOperationMasks(operations);
            ForRows(width,height,y=>
            {
                for(int x=0;x<width;x++)
                {
                    Vector2 local=new Vector2((x+.5f)/width,(y+.5f)/height);
                    Vector2 uv=MapUV(local,r,s,o,rotation);
                    Color c;
                    if(sampling==TextureSamplingMode.Area&&(Mathf.Abs(s.x*r.width)*Width>width || Mathf.Abs(s.y*r.height)*Height>height))
                    {
                        // Deterministic supersampling approximation to area integration.
                        int nx=Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(s.x*r.width)*Width/width),1,16);
                        int ny=Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(s.y*r.height)*Height/height),1,16);
                        c=Color.clear;
                        for(int j=0;j<ny;j++)for(int i=0;i<nx;i++)
                        {
                            Vector2 sub=new Vector2((x+(i+.5f)/nx)/width,(y+(j+.5f)/ny)/height);
                            c+=SampleRaw(MapUV(sub,r,s,o,rotation),TextureSamplingMode.Bilinear,w);
                        }
                        c*=1f/(nx*ny);
                    }
                    else c=SampleRaw(uv,sampling==TextureSamplingMode.Area?TextureSamplingMode.Bilinear:sampling,w);
                    if(Normal)
                    {
                        Vector3 n=MathUtil.Normal(c);
                        if(Settings.DataType==TextureDataType.NormalTangent && rotation!=0f)n=RotateNormal(n,rotation);
                        c=MathUtil.Pack(MathUtil.SafeNormal(n),c.a);
                    }
                    c=ApplyOperations(c,local,operations);
                    int index=y*width+x;output._pixels[index]=c;output._usage[index]=(byte)(SampleUsage(uv,sampling,w)?1:0);
                }
            });
            output._normalized=!output.Normal || operations==null || operations.Count==0;
            return output;
        }
        private static void ValidateOperations(IReadOnlyList<ColorOperation> operations)
        {
            if(operations==null)return;
            foreach(var op in operations)
            {
                if(op==null||op.Parameters==null)throw new ArgumentException("Color operations and parameters cannot be null.");
                if(!MathUtil.Finite(op.Parameters.Amount)||!MathUtil.Finite(op.Parameters.Amount2)||!MathUtil.Finite(op.Parameters.Color))throw new ArgumentException("Color operation values must be finite.");
                op.MaskOptions?.Validate();
            }
        }
        // Prepares every texture an operation reads before the parallel loop, so worker
        // threads only read them.
        private static void PrepareOperationMasks(IReadOnlyList<ColorOperation> operations)
        {
            if(operations==null)return;
            foreach(var op in operations)
            {
                if(op.Mask!=null){op.Mask.Check();if(op.Mask.Normal)op.Mask.EnsureNormalized();}
                var map=op.Parameters.ColorMap;
                if(map!=null){map.Check();if(map.Normal)map.EnsureNormalized();}
            }
        }
        private static Color ApplyOperations(Color original,Vector2 local,IReadOnlyList<ColorOperation> operations)
        {
            if(operations==null)return original;
            Color c=original;
            foreach(var op in operations)
            {
                float influence=1f;
                if(op.Mask!=null)
                {
                    Color sample=op.Mask.SampleRaw(local,TextureSamplingMode.Bilinear,op.Mask.Settings.WrapMode);
                    bool used=op.Mask.SampleUsage(local,TextureSamplingMode.Nearest,op.Mask.Settings.WrapMode);
                    influence=TextureMath.EvaluateMask(sample,used,op.MaskOptions);
                }
                if(influence<=0f)continue;
                Color color=op.Parameters.Color;
                var map=op.Parameters.ColorMap;
                if(map!=null)color*=map.SampleRaw(local,TextureSamplingMode.Bilinear,map.Settings.WrapMode);
                c=MathUtil.Lerp(c,TextureMath.Apply(c,op,color),influence);
            }
            return c;
        }
        public void ApplyColor(ColorOperation operation,Rect? region=null) => ApplyColor(new[]{operation},region);
        public void ApplyColor(IReadOnlyList<ColorOperation> operations,Rect? region=null)
        {
            Writable();ValidateOperations(operations);ValidateRect(region);if(operations==null||operations.Count==0)return;
            foreach(var operation in operations)
                if(ReferenceEquals(operation.Mask,this)||ReferenceEquals(operation.Parameters.ColorMap,this))
                    throw new ArgumentException("An operation mask or color map cannot be the mutable destination. Clone it first.");
            PrepareOperationMasks(operations);if(Normal)EnsureNormalized();
            PixelBounds(region,out int x0,out int y0,out int x1,out int y1);
            ForRows(x1-x0,y1-y0,dy=>
            {
                int y=y0+dy;
                for(int x=x0;x<x1;x++)
                {int i=y*Width+x;_pixels[i]=ApplyOperations(_pixels[i],new Vector2((x+.5f)/Width,(y+.5f)/Height),operations);}
            });
            Changed();
        }
        private float Influence(NordicsTextureWrapper mask,TextureMaskOptions options,Vector2 local)
        {
            if(mask==null)return 1f;
            Color c=mask.SampleRaw(local,TextureSamplingMode.Bilinear,mask.Settings.WrapMode);
            bool used=mask.SampleUsage(local,TextureSamplingMode.Nearest,mask.Settings.WrapMode);
            return TextureMath.EvaluateMask(c,used,options);
        }
        public void Fill(Color color,Rect? region=null,NordicsTextureWrapper mask=null,TextureMaskOptions maskOptions=null)
        {
            Writable();ValidateColor(color);ValidateRect(region);maskOptions?.Validate();
            if(mask!=null){mask.Check();if(ReferenceEquals(mask,this))throw new ArgumentException("A destination cannot be its own mask during mutation. Clone the mask first.");if(mask.Normal)mask.EnsureNormalized();}
            PixelBounds(region,out int x0,out int y0,out int x1,out int y1);
            ForRows(x1-x0,y1-y0,dy=>
            {
                int y=y0+dy;
                for(int x=x0;x<x1;x++)
                {
                    Vector2 uv=new Vector2((x+.5f)/Width,(y+.5f)/Height);
                    float t=Influence(mask,maskOptions,uv);if(t<=0f)continue;
                    int i=y*Width+x;_pixels[i]=MathUtil.Lerp(_pixels[i],color,t);_usage[i]=1;
                }
            });
            Changed();
        }
        public void Clear(Rect? region=null)
        {
            Writable();PixelBounds(region,out int x0,out int y0,out int x1,out int y1);
            ForRows(x1-x0,y1-y0,dy=>{int y=y0+dy;for(int x=x0;x<x1;x++){int i=y*Width+x;_pixels[i]=Color.clear;_usage[i]=0;}});
            Changed();
        }
        private static bool NormalMode(TextureBlendMode b)=>b==TextureBlendMode.NormalLerp||b==TextureBlendMode.NormalRNM||b==TextureBlendMode.NormalWhiteout;
        private static Color Compose(Color dst,Color src,float influence,TextureBlendMode mode,float strength,TextureChannel? from,TextureChannel? to,bool srcUsed,ref byte dstUsage)
        {
            if(NormalMode(mode))
            {
                if(from.HasValue||to.HasValue)throw new ArgumentException("Normal blending requires full-vector channels.");
                Vector3 n=MathUtil.Normal(src);n=MathUtil.SafeNormal(new Vector3(n.x*strength,n.y*strength,n.z));
                Vector3 outN=TextureMath.BlendNormals(MathUtil.Normal(dst),n,mode);
                Vector3 mixed=MathUtil.SafeNormal(Vector3.LerpUnclamped(MathUtil.Normal(dst),outN,influence));
                return MathUtil.Pack(mixed,Mathf.LerpUnclamped(dst.a,src.a,influence));
            }
            if(from.HasValue || to.HasValue)
            {
                TextureChannel source=from??TextureChannel.AverageRGB;
                float sourceValue=TextureMath.Select(src,source,srcUsed);
                if(!to.HasValue)
                {
                    for(int k=0;k<4;k++)dst[k]=Mathf.LerpUnclamped(dst[k],TextureMath.Blend(dst[k],sourceValue,mode),influence);
                    return dst;
                }
                if(to.Value==TextureChannel.AverageRGB)throw new ArgumentException("AverageRGB cannot be a destination channel.");
                float old=to.Value==TextureChannel.Usage?(dstUsage!=0?1f:0f):dst[(int)to.Value];
                float result=Mathf.LerpUnclamped(old,TextureMath.Blend(old,sourceValue,mode),influence);
                if(to.Value==TextureChannel.Usage)dstUsage=(byte)(result>0f?1:0);
                else dst[(int)to.Value]=result;
                return dst;
            }
            for(int k=0;k<4;k++)dst[k]=Mathf.LerpUnclamped(dst[k],TextureMath.Blend(dst[k],src[k],mode),influence);
            return dst;
        }
        public Rect Blit(NordicsTextureWrapper source,Rect? region=null,Rect? sourceRegion=null,Vector2? scale=null,Vector2? offset=null,float rotation=0f,NordicsTextureWrapper mask=null,TextureMaskOptions maskOptions=null,TextureChannel? sourceChannel=null,TextureChannel? destinationChannel=null,TextureBlendMode blend=TextureBlendMode.Replace,float strength=1f,int? padding=null,TextureSamplingMode sampling=TextureSamplingMode.Bilinear,TextureWrapMode? wrap=null,IReadOnlyList<ColorOperation> operations=null)
        {
            Writable();if(source==null)throw new ArgumentNullException(nameof(source));source.Check();ValidateRect(region);ValidateRect(sourceRegion);maskOptions?.Validate();
            if(!MathUtil.Finite(strength)||strength<0)throw new ArgumentOutOfRangeException(nameof(strength));
            if(padding.HasValue&&padding.Value<0)throw new ArgumentOutOfRangeException(nameof(padding));
            if(!Enum.IsDefined(typeof(TextureBlendMode),blend))throw new ArgumentOutOfRangeException(nameof(blend));
            if(sourceChannel.HasValue&&!Enum.IsDefined(typeof(TextureChannel),sourceChannel.Value))throw new ArgumentOutOfRangeException(nameof(sourceChannel));
            if(destinationChannel.HasValue&&!Enum.IsDefined(typeof(TextureChannel),destinationChannel.Value))throw new ArgumentOutOfRangeException(nameof(destinationChannel));
            PixelBounds(region,out int x0,out int y0,out int x1,out int y1);
            int p=padding??0;int innerW=x1-x0-2*p,innerH=y1-y0-2*p;
            if(innerW<=0||innerH<=0)throw new ArgumentException("Region must contain at least one content pixel after padding.");
            Rect inner=new Rect((x0+p)/(float)Width,(y0+p)/(float)Height,innerW/(float)Width,innerH/(float)Height);
            using(var transformed=source.Resize(innerW,innerH,sourceRegion,scale,offset,rotation,sampling,wrap,operations))
            {
                if(mask!=null){mask.Check();if(ReferenceEquals(mask,this))throw new ArgumentException("A destination cannot be its own mask during mutation. Clone the mask first.");if(mask.Normal)mask.EnsureNormalized();}
                if(NormalMode(blend))EnsureNormalized();
                // Snapshotting source through Resize makes self-blits well defined.
                int rw=x1-x0,rh=y1-y0;
                ForRows(rw,rh,dy=>
                {
                    int y=y0+dy;
                    for(int x=x0;x<x1;x++)
                    {
                        int sx=Mathf.Clamp(x-x0-p,0,innerW-1),sy=Mathf.Clamp(y-y0-p,0,innerH-1);
                        bool inContent=x>=x0+p&&x<x1-p&&y>=y0+p&&y<y1-p;
                        int si=sy*innerW+sx,di=y*Width+x;
                        Vector2 local=new Vector2((sx+.5f)/innerW,(sy+.5f)/innerH);
                        float influence=Influence(mask,maskOptions,local);
                        if(influence<=0f)continue;
                        byte used=_usage[di];
                        _pixels[di]=Compose(_pixels[di],transformed._pixels[si],influence,blend,strength,sourceChannel,destinationChannel,transformed._usage[si]!=0,ref used);
                        if(padding.HasValue && !destinationChannel.HasValue)
                            used=(byte)(used!=0||transformed._usage[si]!=0?1:0);
                        if(destinationChannel==TextureChannel.Usage)_usage[di]=used;
                        else if(padding.HasValue)_usage[di]=used;
                        // Padding is filled by edge extension; it is never sampled from the destination.
                    }
                });
            }
            Changed(!NormalMode(blend));if(NormalMode(blend))_normalized=true;
            return inner;
        }
        /// <summary>Finalizes uncovered pixels using the exact nearest originally-used site
        /// (squared Euclidean distance). Normal data receives opaque alpha; other data
        /// types preserve the source RGBA including fractional or zero alpha.</summary>
        private sealed class DilateScratch
        {
            internal readonly int[] V, Sites;
            internal readonly double[] Z, Costs;
            internal DilateScratch(int extent)
            {
                V=new int[extent]; Sites=new int[extent];
                Z=new double[extent+1]; Costs=new double[extent];
            }
        }
        public void Dilate(Rect? region=null)
        {
            Writable();PixelBounds(region,out int x0,out int y0,out int x1,out int y1);
            int w=x1-x0,h=y1-y0;
            if(w==0||h==0)return;
            bool normal=Normal,any=false,all=true;
            for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)
            {
                bool used=_usage[y*Width+x]!=0;
                any|=used;all&=used;
            }
            // An empty coverage domain is not geometry. In particular, do not make
            // an empty normal map opaque or mark its pixels used.
            if(!any)return;
            if(all)
            {
                if(normal)
                {
                    ForRows(w,h,y=>
                    {
                        int row=(y+y0)*Width+x0;
                        for(int x=0;x<w;x++)
                        {
                            int i=row+x;Color c=_pixels[i];c.a=1f;_pixels[i]=c;
                        }
                    });
                    Changed(false);
                }
                return;
            }

            // Only two full-resolution scratch arrays: source indices (4 bytes/pixel)
            // and squared distances (8 bytes/pixel). Each vertical worker snapshots
            // one column before overwriting it, so its output cannot affect input.
            int count=checked(w*h);
            int[] nearest=new int[count];
            double[] distance=new double[count];
            using(var scratch=new ThreadLocal<DilateScratch>(()=>new DilateScratch(Math.Max(w,h))))
            {
            ForRows(w,h,y=>
            {
                DilateScratch work=scratch.Value;
                int[] v=work.V;double[] z=work.Z;int k=-1;
                int row=(y+y0)*Width+x0,local=y*w;
                for(int q=0;q<w;q++)
                {
                    if(_usage[row+q]==0)continue;
                    double s=0;
                    while(k>=0)
                    {
                        int prev=v[k];
                        s=(q*(double)q-prev*(double)prev)/(2d*(q-prev));
                        if(s>z[k])break;
                        k--;
                    }
                    k++;v[k]=q;z[k]=k==0?double.NegativeInfinity:s;
                    z[k+1]=double.PositiveInfinity;
                }
                if(k<0)
                {
                    for(int q=0;q<w;q++){nearest[local+q]=-1;distance[local+q]=double.PositiveInfinity;}
                    return;
                }
                int current=0;
                for(int q=0;q<w;q++)
                {
                    while(current<k&&z[current+1]<q)current++;
                    int p=v[current];nearest[local+q]=local+p;
                    distance[local+q]=(q-p)*(double)(q-p);
                }
            });
            ForRows(h,w,x=>
            {
                // Per-column input snapshots protect exact source selection from
                // in-place results, regardless of the number of parallel workers.
                DilateScratch work=scratch.Value;
                int[] sites=work.Sites;double[] costs=work.Costs;
                int[] v=work.V;double[] z=work.Z;
                int k=-1;
                for(int q=0;q<h;q++)
                {
                    int i=q*w+x;sites[q]=nearest[i];costs[q]=distance[i];
                }
                for(int q=0;q<h;q++)
                {
                    if(sites[q]<0)continue;
                    double f=costs[q],s=0;
                    while(k>=0)
                    {
                        int prev=v[k];
                        s=((f+q*(double)q)-(costs[prev]+prev*(double)prev))/(2d*(q-prev));
                        if(s>z[k])break;
                        k--;
                    }
                    k++;v[k]=q;z[k]=k==0?double.NegativeInfinity:s;
                    z[k+1]=double.PositiveInfinity;
                }
                if(k<0)return; // Impossible if any site exists in the region.
                int current=0;
                for(int q=0;q<h;q++)
                {
                    while(current<k&&z[current+1]<q)current++;
                    nearest[q*w+x]=sites[v[current]];
                }
            });
            } // Release per-worker scratch references after both distance passes.
            ForRows(w,h,y=>
            {
                int local=y*w,global=(y+y0)*Width+x0;
                for(int x=0;x<w;x++)
                {
                    int di=global+x,site=nearest[local+x];
                    if(_usage[di]!=0)
                    {
                        if(normal){Color c=_pixels[di];c.a=1f;_pixels[di]=c;}
                        continue;
                    }
                    if(site<0)continue;
                    int src=(site/w+y0)*Width+site%w+x0;
                    Color value=_pixels[src];
                    if(normal)value.a=1f;
                    _pixels[di]=value;_usage[di]=1;
                }
            });
            // Do not reset the lazy normal-normalization flag: copying a source
            // direction cannot change its length or require per-copy normalization.
            Changed(false);
        }
        public void PaintTriangle(Vector2 uv0,Vector2 uv1,Vector2 uv2,Color color0,Color color1,Color color2,TrianglePaintOptions options=null)
        {
            Writable();ValidateUV(uv0);ValidateUV(uv1);ValidateUV(uv2);ValidateColor(color0);ValidateColor(color1);ValidateColor(color2);
            options=options??new TrianglePaintOptions();if(options.SamplesPerAxis<1||options.SamplesPerAxis>32)throw new ArgumentOutOfRangeException(nameof(options.SamplesPerAxis));
            Vector2 a=new Vector2(uv0.x*Width,uv0.y*Height),b=new Vector2(uv1.x*Width,uv1.y*Height),c=new Vector2(uv2.x*Width,uv2.y*Height);
            float cross=(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);if(Mathf.Abs(cross)<1e-10f)return;
            int x0=Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x,Mathf.Min(b.x,c.x))),0,Width),x1=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x,Mathf.Max(b.x,c.x))),0,Width);
            int y0=Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y,Mathf.Min(b.y,c.y))),0,Height),y1=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y,Mathf.Max(b.y,c.y))),0,Height);
            int samples=options.Mode==TriangleRasterizationMode.Fast?1:options.SamplesPerAxis;
            ForRows(x1-x0,y1-y0,dy=>
            {
                int y=y0+dy;
                for(int x=x0;x<x1;x++)
                {
                    Color sum=Color.clear;int hits=0;
                    for(int j=0;j<samples;j++)for(int i=0;i<samples;i++)
                    {
                        Vector2 p=new Vector2(x+(i+.5f)/samples,y+(j+.5f)/samples);
                        float wb=((p.x-a.x)*(c.y-a.y)-(p.y-a.y)*(c.x-a.x))/cross;
                        float wc=((b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x))/cross;
                        float wa=1f-wb-wc;
                        if(wa < -1e-6f||wb < -1e-6f||wc < -1e-6f)continue;
                        sum+=color0*wa+color1*wb+color2*wc;hits++;
                    }
                    if(hits==0)continue;
                    int index=y*Width+x;Color existing=_pixels[index];Color add=sum*(1f/(samples*samples));
                    // Preserve the complete floating-point accumulation. Clamping here
                    // would lose information if later triangles/operations add to it.
                    // The Unity output boundary clamps values for bounded formats.
                    _pixels[index]=existing+add;
                    _usage[index]=1;
                }
            });
            Changed();
        }
        /// <summary>Sets RGBA only on currently unused pixels. By default, preserves
        /// the usage mask, allowing coverage to be exported separately from a fully
        /// initialized correction texture.</summary>
        public void FillUnused(Color color, bool markUsed = false, Rect? region = null)
        {
            Writable(); ValidateColor(color); ValidateRect(region);
            PixelBounds(region, out int x0, out int y0, out int x1, out int y1);
            ForRows(x1-x0, y1-y0, dy =>
            {
                int y = y0 + dy;
                for (int x = x0; x < x1; x++)
                {
                    int i = y * Width + x;
                    if (_usage[i] != 0) continue;
                    _pixels[i] = color;
                    if (markUsed) _usage[i] = 1;
                }
            });
            // Filled pixels in normal buffers may not be unit vectors.
            Changed();
        }
        private void EnsureNormalized()
        {
            if(!Normal||_normalized)return;
            ForRows(Width,Height,y=>
            {
                for(int x=0;x<Width;x++)
                {
                    int i=y*Width+x;if(_usage[i]==0)continue;
                    Color c=_pixels[i];_pixels[i]=MathUtil.Pack(MathUtil.SafeNormal(MathUtil.Normal(c)),c.a);
                }
            });
            _normalized=true;_pixelsDirty=true;
        }
        /// <summary>Returns the reference world normal (this wrapper) expressed in
        /// the supplied destination/target mesh tangent basis. The targetNormals map
        /// is retained for API compatibility and used as the destination geometry
        /// coverage domain; its normal direction is not the value being reconstructed.
        /// B = cross(N,T) * handedness must be supplied by the caller.</summary>
        public NordicsTextureWrapper BakeNormalDifference(NordicsTextureWrapper targetNormals, TangentBasisData basis)
        {
            Check();
            if (targetNormals == null) throw new ArgumentNullException(nameof(targetNormals));
            if (basis == null) throw new ArgumentNullException(nameof(basis));
            if (basis.Tangent == null || basis.Bitangent == null || basis.Normal == null)
                throw new ArgumentException("All three destination tangent basis maps are required.", nameof(basis));
            targetNormals.Check();
            basis.Tangent.Check(); basis.Bitangent.Check(); basis.Normal.Check();
            if (!Normal || !targetNormals.Normal)
                throw new ArgumentException("Reference and target maps must be normal maps.");
            if (targetNormals.Width != Width || targetNormals.Height != Height ||
                basis.Tangent.Width != Width || basis.Tangent.Height != Height ||
                basis.Bitangent.Width != Width || basis.Bitangent.Height != Height ||
                basis.Normal.Width != Width || basis.Normal.Height != Height)
                throw new ArgumentException("All maps must have matching dimensions.");
            if (Settings.DataType != TextureDataType.NormalWorld ||
                targetNormals.Settings.DataType != TextureDataType.NormalWorld)
                throw new ArgumentException("Reference and target normals must be world-space normals.");
            EnsureNormalized();
            var output = new NordicsTextureWrapper(Width, Height,
                new TextureSettings { DataType=TextureDataType.NormalTangent, SRGB=false });
            ForRows(Width, Height, y =>
            {
                for (int x=0; x<Width; x++)
                {
                    int i=y*Width+x;
                    if (_usage[i]==0 || targetNormals._usage[i]==0 ||
                        basis.Tangent._usage[i]==0 || basis.Bitangent._usage[i]==0 ||
                        basis.Normal._usage[i]==0) continue;
                    Vector3 reference=MathUtil.SafeNormal(MathUtil.Normal(_pixels[i]));
                    Vector3 n=MathUtil.Normal(basis.Normal._pixels[i]);
                    Vector3 t=MathUtil.Normal(basis.Tangent._pixels[i]);
                    Vector3 b=MathUtil.Normal(basis.Bitangent._pixels[i]);
                    if (!MathUtil.Finite(n.x) || !MathUtil.Finite(n.y) || !MathUtil.Finite(n.z) ||
                        !MathUtil.Finite(t.x) || !MathUtil.Finite(t.y) || !MathUtil.Finite(t.z) ||
                        !MathUtil.Finite(b.x) || !MathUtil.Finite(b.y) || !MathUtil.Finite(b.z))
                        continue;
                    if (n.sqrMagnitude<1e-12f || t.sqrMagnitude<1e-12f || b.sqrMagnitude<1e-12f)
                        continue;
                    n.Normalize();
                    // Orthogonalize the tangent to obtain a valid basis for projection.
                    t -= n*Vector3.Dot(t,n);
                    if (t.sqrMagnitude<1e-12f) continue;
                    t.Normalize();
                    // Preserve the supplied bitangent sign (mirrored UV handedness).
                    float handedness=Vector3.Dot(Vector3.Cross(n,t),b)<0f ? -1f : 1f;
                    b=Vector3.Cross(n,t)*handedness;
                    Vector3 tangentSpace=MathUtil.SafeNormal(new Vector3(
                        Vector3.Dot(reference,t), Vector3.Dot(reference,b), Vector3.Dot(reference,n)));
                    output._pixels[i]=MathUtil.Pack(tangentSpace,Mathf.Clamp01(_pixels[i].a));
                    output._usage[i]=1;
                }
            });
            output._normalized=true;
            return output;
        }
        public Texture2D GetTexture2D()
        {
            Check();Settings.Validate();EnsureNormalized();
            if(_exporter!=null)return SyncExport();
            bool settingsChanged=_settingsFingerprint!=Settings.Fingerprint();
            TextureFormat desiredFormat = GetOutputTextureFormat();
            if (_cached != null && (_cachedFormat != desiredFormat || _cached.mipmapCount > 1 != Settings.Mipmaps))
            {
                if (_ownsCache) UnityEngine.Object.DestroyImmediate(_cached);
                _cached = null;
            }
            if(_cached==null)
            {
                _cached=new Texture2D(Width,Height,desiredFormat,Settings.Mipmaps,!Settings.SRGB || Settings.IsNormal);
                _cachedFormat=desiredFormat;
                _ownsCache=true;_pixelsDirty=true;
            }
            if(_pixelsDirty)
            {
                Color[] data=new Color[_pixels.Length];
                ForRows(Width,Height,y=>{for(int x=0;x<Width;x++){int i=y*Width+x;data[i]=Encode(_pixels[i]);}});
                _cached.SetPixels(data);_cached.Apply(Settings.Mipmaps,false);
            }
            if(_pixelsDirty||settingsChanged)
            {
                _cached.wrapMode=Settings.WrapMode;_cached.filterMode=Settings.FilterMode;_cached.anisoLevel=Settings.AnisoLevel;
            }
            _pixelsDirty=false;_settingsFingerprint=Settings.Fingerprint();return _cached;
        }
        private Color Encode(Color c)
        {
            // Only the output representation is bounded. The authoritative float
            // buffer retains unsaturated RGBA/coverage and signed normals.
            if (!Normal)
            {
                float r=Mathf.Clamp01(c.r), g=Mathf.Clamp01(c.g), b=Mathf.Clamp01(c.b);
                // The buffer is linear. sRGB output stores encoded values, which the GPU
                // decodes back to the buffer's values when a shader samples it.
                if (Settings.SRGB) { r=MathUtil.LinearToSRGB(r); g=MathUtil.LinearToSRGB(g); b=MathUtil.LinearToSRGB(b); }
                return new Color(r, g, b, Mathf.Clamp01(c.a));
            }
            Vector3 n=MathUtil.SafeNormal(MathUtil.Normal(c));
            return new Color(Mathf.Clamp01((n.x+1f)*.5f),
                Mathf.Clamp01((n.y+1f)*.5f),
                Mathf.Clamp01((n.z+1f)*.5f), Mathf.Clamp01(c.a));
        }
        /// <summary>Returns the output texture format after inspecting encoded 8-bit alpha for Auto.</summary>
        public TextureFormat GetOutputTextureFormat()
        {
            Check(); Settings.Validate();
            if (Settings.OutputChannels == TextureOutputChannels.RGB) return TextureFormat.RGB24;
            if (Settings.OutputChannels == TextureOutputChannels.RGBA) return TextureFormat.RGBA32;
            // Alpha is independent of normal normalization; no need to mutate the buffer.
            // Quantization uses Unity's nearest-byte conversion semantics.
            for (int i = 0; i < _pixels.Length; i++)
                if (Mathf.RoundToInt(Mathf.Clamp01(_pixels[i].a) * 255f) != 255)
                    return TextureFormat.RGBA32;
            return TextureFormat.RGB24;
        }
        public Color[] GetEncodedPixels()
        {
            Check();EnsureNormalized();Color[] output=new Color[_pixels.Length];
            ForRows(Width,Height,y=>{for(int x=0;x<Width;x++){int i=y*Width+x;output[i]=Encode(_pixels[i]);}});return output;
        }
        public Texture2D Export(INordicsTextureExporter exporter,string name)
        {
            Check();if(exporter==null)throw new ArgumentNullException(nameof(exporter));if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Export name must not be blank.",nameof(name));
            _exporter=exporter;_exportName=name;_pixelsDirty=true;_settingsFingerprint=null;
            _lastExportFormat=null;
            return SyncExport();
        }
        private Texture2D SyncExport()
        {
            bool pixels=_pixelsDirty;bool settings=_settingsFingerprint!=Settings.Fingerprint();
            TextureFormat outputFormat = GetOutputTextureFormat();
            if (!_lastExportFormat.HasValue || _lastExportFormat.Value != outputFormat) pixels = true;
            if(_cached==null)pixels=settings=true;
            if(!pixels&&!settings)return _cached;
            Settings.Validate();
            _cached=_exporter.Synchronize(this,_exportName,pixels,settings);
            if(_cached==null)throw new InvalidOperationException("Exporter returned a null texture.");
            _ownsCache=false;_pixelsDirty=false;_settingsFingerprint=Settings.Fingerprint();
            _lastExportFormat=outputFormat;return _cached;
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;_pixels=null;_usage=null;
            if(_ownsCache&&_cached!=null)
            {
#if UNITY_EDITOR
                if(!Application.isPlaying)UnityEngine.Object.DestroyImmediate(_cached);
                else
#endif
                    UnityEngine.Object.Destroy(_cached);
            }
            _cached=null;_exporter=null;
        }
    }
    public interface INordicsTextureExporter
    {
        Texture2D Synchronize(NordicsTextureWrapper wrapper,string name,bool pixelsChanged,bool settingsChanged);
    }
}
