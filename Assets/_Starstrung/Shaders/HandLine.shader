// 手绘细线（界面的星座线、图标）：LineRenderer 用。uv.x 沿线 0..1，uv.y 横跨 0..1。
// 两头收尖、中段笔压重；横向柔边；顶点色 alpha = 整体显隐。几何上的微弯 / 抖动由脚本生成（UIKit.HandPolyline）。
Shader "Starstrung/HandLine"
{
    Properties
    {
        _Intensity ("亮度", Range(0, 4)) = 1.2
        _Taper ("两头收尖长度", Range(0, 0.5)) = 0.12
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
            float _Intensity, _Taper;
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos = TransformObjectToHClip(v.pos.xyz); o.uv = v.uv; o.col = v.col; return o; }
            half4 frag(V i) : SV_Target
            {
                float x = i.uv.x, y = abs(i.uv.y * 2 - 1);
                float ends = smoothstep(0.0, _Taper, x) * smoothstep(1.0, 1.0 - _Taper, x);
                float pres = 0.75 + 0.25 * sin(saturate(x) * PI);
                float w = lerp(0.35, 1.0, ends) * pres;                 // 收尖：越到头越细
                float core = smoothstep(w, w * 0.35, y);
                float a = core * lerp(0.25, 1.0, ends) * i.col.a;
                return half4(i.col.rgb * a * _Intensity, 0);
            }
            ENDHLSL
        }
    }
}
