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
        struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
        struct v2f { float4 position : SV_POSITION; float4 value : TEXCOORD0; };
        // 1 or -1: which way clip space's y runs through a render texture on this graphics API,
        // found by a test draw (TextureGpu.PaintFlip).
        float _FlipY;
        v2f vert(appdata v)
        {
            v2f o;
            // Straight to clip space, without matrices: the UV's 0–1 becomes -1–1.
            o.position = float4(v.vertex.x * 2 - 1, (v.vertex.y * 2 - 1) * _FlipY, 0.5, 1);
            o.value = v.color;
            return o;
        }
        ENDCG

        // 0: the values.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target { return i.value; }
            ENDCG
        }

        // 1: one hit per covered sample.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target { return float4(1, 0, 0, 0); }
            ENDCG
        }
    }
}
