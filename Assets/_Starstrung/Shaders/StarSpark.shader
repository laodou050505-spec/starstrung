// 星点：每颗星点是一个始终朝向相机的小方片（顶点里展开），各自错频闪烁，带很淡的四芒。
// 顶点数据：position = 星点中心（四个角相同），uv = 角（-1..1），uv2 = (相位, 频率, 亮度, 大小米)，color = 色调。
// 路线上的暗星点、星尘、天上的星都用它。叠加混合。
Shader "Starstrung/StarSpark"
{
    Properties
    {
        _Intensity ("亮度", Range(0, 8)) = 2.0
        _Flare ("四芒", Range(0, 1)) = 0.35
        _MinPx ("最小像素大小", Float) = 1.6
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
            float _SS_GrowK;   // 全局：路线展开进度 0..1（沿线星点随之淡入）

            float _Intensity, _Flare, _MinPx;

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 p2 : TEXCOORD1; float4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float a : TEXCOORD1; float4 col : COLOR; UNITY_VERTEX_OUTPUT_STEREO };

            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 c = TransformObjectToWorld(v.pos.xyz);
                float3 vc = TransformWorldToView(c);
                float tw = 0.5 + 0.5 * sin(_Time.y * v.p2.y + v.p2.x);
                float size = v.p2.w * (0.75 + 0.25 * tw);
                // 太远时保证至少 _MinPx 像素，不至于整颗消失再出现（闪烁）
                // 渲染到贴图 / 某些平台上投影矩阵会上下翻转（_m11 为负），取绝对值
                float pxSize = size / max(-vc.z, 0.05) * _ScreenParams.y * 0.5 * abs(UNITY_MATRIX_P._m11);
                float boost = max(1.0, _MinPx / max(pxSize, 1e-4));
                vc.xy += v.uv * size * boost;
                o.pos = mul(UNITY_MATRIX_P, float4(vc, 1));
                o.uv = v.uv;
                o.a = (0.3 + 0.7 * tw) * v.p2.z / (boost * boost);   // 放大补像素时亮度按面积摊薄
                o.col = v.col;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float r = length(i.uv);
                float core = pow(saturate(1.0 - r), 4.0);
                float flare = (exp(-abs(i.uv.x) * 18.0) * saturate(1.0 - abs(i.uv.y)) + exp(-abs(i.uv.y) * 18.0) * saturate(1.0 - abs(i.uv.x))) * _Flare;
                float a = (core + flare) * i.a * _Intensity;
                return half4(i.col.rgb * a * _SS_GrowK, 0);
            }
            ENDHLSL
        }
    }
}
