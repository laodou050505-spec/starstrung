// 冲击光环：方片上画两圈发丝细环 + 刻度，随 _K（0→1）从中心扩散开、淡出。由脚本按物体设置 _K 和颜色。
Shader "Starstrung/FxRing"
{
    Properties
    {
        _Color ("颜色", Color) = (0.7, 0.85, 1, 1)
        _K ("进度", Range(0, 1)) = 0
        _Intensity ("亮度", Range(0, 6)) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            UNITY_INSTANCING_BUFFER_START(P)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float, _K)
            UNITY_INSTANCING_BUFFER_END(P)
            float _Intensity;
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos = TransformObjectToHClip(v.pos.xyz); o.uv = v.uv * 2 - 1; return o; }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 col = UNITY_ACCESS_INSTANCED_PROP(P, _Color);
                float k = UNITY_ACCESS_INSTANCED_PROP(P, _K);
                float r = length(i.uv);
                float px = 1.0 / max(fwidth(r), 1e-5);
                float e = 1.0 - pow(1.0 - k, 3.0);                 // 先快后慢
                float R = 0.12 + 0.85 * e;
                float ring = saturate(1.5 - abs(r - R) * px) + saturate(1.2 - abs(r - R * 0.86) * px) * 0.6;
                float a = atan2(i.uv.y, i.uv.x);
                float tick = saturate(1.2 - abs(frac(a / (PI / 12.0) + 0.5) - 0.5) * (PI / 12.0) * r * px) * step(R * 0.86, r) * step(r, R);
                float glow = exp(-abs(r - R) * 18.0) * 0.4;
                float fade = (1.0 - k) * (1.0 - k);
                return half4(col.rgb * (ring + tick * 0.7 + glow) * fade * _Intensity * smoothstep(1.0, 0.95, r), 0);
            }
            ENDHLSL
        }
    }
}
