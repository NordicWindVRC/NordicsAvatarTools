// The painted samples back to the texture's size: each pixel adds the average of its
// _Samples × _Samples samples to its value, and is used where it was or any sample was hit.
Shader "Hidden/NordicWind/TextureWrapper/ResolvePaint"
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

        // 0: values: the existing value (_Pixels) plus the samples' average (_MainTex).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            sampler2D _Pixels;
            float _Samples;
            float4 frag(v2f_img i) : SV_Target
            {
                float2 p = PixelOf(i.uv);
                int n = (int)_Samples;
                float2 large = _Size.xy * n;
                float4 sum = 0;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        sum += Texel(_MainTex, p * n + float2(x, y), large);
                return Texel(_Pixels, p, _Size.xy) + sum / (n * n);
            }
            ENDCG
        }

        // 1: usage: used before (_Usage), or any sample hit (_MainTex holds the hits).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            sampler2D _Usage;
            float _Samples;
            float4 frag(v2f_img i) : SV_Target
            {
                float2 p = PixelOf(i.uv);
                if (Texel(_Usage, p, _Size.xy).r > 0.5) return 1;
                int n = (int)_Samples;
                float2 large = _Size.xy * n;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        if (Texel(_MainTex, p * n + float2(x, y), large).r > 0.5) return 1;
                return 0;
            }
            ENDCG
        }
    }
}
