// Triangle painting (GPU_ACCELERATION.md, G3): the triangles drawn with their UVs as positions,
// into a texture the sampling factor times larger, adding up. The values are interpolated
// linearly (w = 1), like the CPU's barycentric weights.
Shader "Hidden/NordicWind/TextureWrapper/PaintTriangles"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend One One

        CGINCLUDE
        #include "UnityCG.cginc"
        // world: a corner's position (xyz) and its triangle's UV winding (w: 1 counter-clockwise,
        // -1 clockwise); normal: the corner's normal (xyz) and how far the triangle's surface
        // moves over one pixel of the texture (w). Only hidden surfaces use them.
        struct appdata { float4 vertex : POSITION; float4 color : COLOR; float4 world : TEXCOORD1; float4 normal : TEXCOORD2; };
        struct v2f { float4 position : SV_POSITION; float4 value : TEXCOORD0; float4 world : TEXCOORD1; float4 normal : TEXCOORD2; };
        // 1 or -1: which way clip space's y runs through a render texture on this graphics API,
        // found by a test draw (TextureGpu.PaintFlip).
        float _FlipY;
        v2f vert(appdata v)
        {
            v2f o;
            // Straight to clip space, without matrices: the UV's 0–1 becomes -1–1.
            o.position = float4(v.vertex.x * 2 - 1, (v.vertex.y * 2 - 1) * _FlipY, 0.5, 1);
            o.value = v.color;
            o.world = v.world;
            o.normal = v.normal;
            return o;
        }

        // Hidden surfaces (HiddenSurfaceOptions), per pixel of the texture's own size: the
        // normals of each UV winding added up (_NormalsCCW, _NormalsCW), and how far out the
        // outermost surface lies along their direction (_Outer: R counter-clockwise, G
        // clockwise; _Offset keeps it above zero, so zero means none).
        Texture2D<float4> _NormalsCCW;
        Texture2D<float4> _NormalsCW;
        Texture2D<float4> _Outer;
        float _Winding;
        float _Offset;
        float _Samples;
        float _Margin;
        float3 PixelNormal(int2 pixel, bool ccw)
        {
            float3 n = ccw ? _NormalsCCW.Load(int3(pixel, 0)).xyz : _NormalsCW.Load(int3(pixel, 0)).xyz;
            float l = length(n);
            return l > 1e-10 ? n / l : float3(0, 0, 0);
        }
        bool Hidden(v2f i)
        {
            int2 pixel = int2(i.position.xy) / (int)_Samples;
            bool ccw = i.world.w > 0;
            float3 n = PixelNormal(pixel, ccw);
            if (dot(n, n) == 0) return false;
            float2 outer = _Outer.Load(int3(pixel, 0)).xy;
            float o = ccw ? outer.x : outer.y;
            // A sample lies within its triangle's footprint of the surface at the pixel's center.
            return o > 0 && o - (dot(i.world.xyz, n) + _Offset) > _Margin + i.normal.w;
        }
        ENDCG

        // 0: the values.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            float4 frag(v2f i) : SV_Target { return i.value; }
            ENDCG
        }

        // 1: one hit per covered sample.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            float4 frag(v2f i) : SV_Target { return float4(1, 0, 0, 0); }
            ENDCG
        }

        // 2: hidden surfaces, first: the normals of one UV winding (_Winding) at the pixel
        // centers, added up.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            float4 frag(v2f i) : SV_Target
            {
                if (i.world.w * _Winding <= 0) discard;
                return float4(i.normal.xyz, 1);
            }
            ENDCG
        }

        // 3: hidden surfaces, second: the outermost surface at each pixel center, along the
        // average normal there.
        Pass
        {
            BlendOp Max
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            float4 frag(v2f i) : SV_Target
            {
                bool ccw = i.world.w > 0;
                float3 n = PixelNormal(int2(i.position.xy), ccw);
                if (dot(n, n) == 0) discard;
                float d = dot(i.world.xyz, n) + _Offset;
                return ccw ? float4(d, 0, 0, 0) : float4(0, d, 0, 0);
            }
            ENDCG
        }

        // 4: the values, without hidden samples.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            float4 frag(v2f i) : SV_Target
            {
                if (Hidden(i)) discard;
                return i.value;
            }
            ENDCG
        }

        // 5: the hits, without hidden samples.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            float4 frag(v2f i) : SV_Target
            {
                if (Hidden(i)) discard;
                return float4(1, 0, 0, 0);
            }
            ENDCG
        }
    }
}
