// 房间里的星：一个网格画几百颗面朝相机的小星（四芒星 + 暗星点），每颗的闪烁周期、相位都不同（错峰闪烁）。
// 顶点数据：uv = 方片角（0..1），uv2 = (大小, 相位, 周期, 种类 0 点 / 1 四芒)，color = 颜色。
Shader "Starstrung/SkyStars"
{
    Properties { _Intensity ("亮度", Range(0, 4)) = 1.2 _Alpha ("显隐", Range(0, 1)) = 1 }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" }
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
            float _Intensity, _Alpha;
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 d : TEXCOORD1; float4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; float kind : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float t = _Time.y;
                float tw = 0.5 + 0.5 * sin(t * 6.2831 / v.d.z + v.d.y);
                tw = v.d.w > 0.5 ? pow(tw, 3.0) * 1.3 + 0.08 : 0.35 + 0.65 * tw;   // 四芒星闪得更明显，暗星点轻轻呼吸
                float3 c = TransformObjectToWorld(v.pos.xyz);
                float3 vc = TransformWorldToView(c);
                float size = v.d.x * (v.d.w > 0.5 ? 0.6 + 0.6 * tw : 1.0);
                vc.xy += (v.uv * 2 - 1) * size;
                o.pos = mul(UNITY_MATRIX_P, float4(vc, 1));
                o.uv = v.uv * 2 - 1;
                o.col = float4(v.col.rgb * tw, v.col.a);
                o.kind = v.d.w;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float2 p = i.uv; float r = length(p);
                float dotv = exp(-r * 7.0);
                float star = exp(-r * 12.0) * 1.4 + (exp(-abs(p.x) * 45.0) * saturate(1 - abs(p.y)) + exp(-abs(p.y) * 45.0) * saturate(1 - abs(p.x))) * 0.9;
                float v = i.kind > 0.5 ? star : dotv;
                v *= smoothstep(1.0, 0.8, max(abs(p.x), abs(p.y)));
                return half4(i.col.rgb * v * _Intensity * _Alpha, 0);
            }
            ENDHLSL
        }
    }
}
