// Dilation by jump flooding (GPU_ACCELERATION.md, G2). Seeds hold the pixel coordinates of the
// nearest used pixel found so far, or -1. Every texture here has the same size.
Shader "Hidden/NordicWind/TextureWrapper/Dilate"
{
    Properties { _MainTex ("", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        // x, y: the size in pixels of the texture being written.
        float4 _Size;
        float2 PixelOf(float2 uv) { return floor(uv * _Size.xy); }
        // A texel of a texture of `size` pixels, read without filtering.
        float4 Texel(sampler2D t, float2 pixel, float2 size) { return tex2Dlod(t, float4((pixel + 0.5) / size, 0, 0)); }
        ENDCG

        // 0: a used pixel seeds itself (usage in _MainTex).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            float4 frag(v2f_img i) : SV_Target
            {
                float2 p = PixelOf(i.uv);
                return Texel(_MainTex, p, _Size.xy).r > 0.5 ? float4(p, 0, 0) : float4(-1, -1, 0, 0);
            }
            ENDCG
        }

        // 1: one jump: the nearest of the seeds `_Step` pixels away in eight directions and here.
        // Equally near: the lower x, then the lower y, as on the CPU.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            float _Step;
            float4 frag(v2f_img i) : SV_Target
            {
                float2 p = PixelOf(i.uv);
                float2 best = float2(-1, -1);
                float bestDistance = 3.0e38;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 q = p + float2(x, y) * _Step;
                    if (q.x < 0 || q.y < 0 || q.x >= _Size.x || q.y >= _Size.y) continue;
                    float2 seed = Texel(_MainTex, q, _Size.xy).xy;
                    if (seed.x < 0) continue;
                    float2 d = seed - p;
                    float distance = dot(d, d);
                    if (distance < bestDistance || (distance == bestDistance && (seed.x < best.x || (seed.x == best.x && seed.y < best.y))))
                    {
                        best = seed;
                        bestDistance = distance;
                    }
                }
                return float4(best, 0, 0);
            }
            ENDCG
        }

        // 2: colors: a used pixel keeps its own; an unused one takes its seed's. Normal maps
        // (_Normal) get alpha 1 wherever there's a value.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            sampler2D _Pixels;
            sampler2D _Usage;
            float _Normal;
            float4 frag(v2f_img i) : SV_Target
            {
                float2 p = PixelOf(i.uv);
                bool used = Texel(_Usage, p, _Size.xy).r > 0.5;
                float2 seed = Texel(_MainTex, p, _Size.xy).xy;
                float4 c = Texel(_Pixels, used || seed.x < 0 ? p : seed, _Size.xy);
                if (_Normal > 0.5 && (used || seed.x >= 0)) c.a = 1;
                return c;
            }
            ENDCG
        }

        // 3: usage: used, or filled from a seed.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            sampler2D _Usage;
            float4 frag(v2f_img i) : SV_Target
            {
                float2 p = PixelOf(i.uv);
                bool used = Texel(_Usage, p, _Size.xy).r > 0.5 || Texel(_MainTex, p, _Size.xy).x >= 0;
                return used ? 1 : 0;
            }
            ENDCG
        }
    }
}
