// 特殊星的标识（一次实例化画完所有）：面朝相机的方片，按 _Shape 画不同的图：
//   1 双子 = 两个交扣的圆环；2 星爆 = 放射的尖刺 + 中心亮点；3 停摆 = 带刻度的环 + 一段转得很慢的弧。
// 用加色混合，套在宝石星外面，远看也能一眼认出这不是普通星。_K 控制亮度（出现 / 消失）。
Shader "Starstrung/StarBadge"
{
    Properties { _Color ("颜色", Color) = (1, 0.9, 0.7, 1) _Intensity ("亮度", Range(0, 6)) = 1.4 }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" }
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
                UNITY_DEFINE_INSTANCED_PROP(float, _Shape)
                UNITY_DEFINE_INSTANCED_PROP(float, _K)
                UNITY_DEFINE_INSTANCED_PROP(float, _Rot)
            UNITY_INSTANCING_BUFFER_END(P)
            float _Intensity;
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 c = TransformObjectToWorld(float3(0, 0, 0));
                float s = length(TransformObjectToWorldDir(float3(1, 0, 0), false));
                float3 vc = TransformWorldToView(c);
                float rot = UNITY_ACCESS_INSTANCED_PROP(P, _Rot);
                float2 q = v.uv * 2 - 1;
                q = float2(q.x * cos(rot) - q.y * sin(rot), q.x * sin(rot) + q.y * cos(rot));
                vc.xy += q * s;
                o.pos = mul(UNITY_MATRIX_P, float4(vc, 1));
                o.uv = v.uv * 2 - 1;
                return o;
            }
            float ring(float d, float r, float w) { return saturate(1.0 - abs(d - r) / w); }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 col = UNITY_ACCESS_INSTANCED_PROP(P, _Color);
                float shape = UNITY_ACCESS_INSTANCED_PROP(P, _Shape);
                float k = UNITY_ACCESS_INSTANCED_PROP(P, _K);
                float rot = UNITY_ACCESS_INSTANCED_PROP(P, _Rot);
                float2 p = i.uv;
                float r = length(p);
                float a = atan2(p.y, p.x);
                float v = 0;
                if (shape < 1.5)
                {
                    // 双子：两个交扣的圆环（倾斜的 8 字）
                    float2 p1 = p - float2(-0.26, 0); float2 p2 = p - float2(0.26, 0);
                    v = ring(length(p1 * float2(1.0, 1.35)), 0.36, 0.045) + ring(length(p2 * float2(1.0, 1.35)), 0.36, 0.045);
                }
                else if (shape < 2.5)
                {
                    // 星爆：八根放射尖刺 + 中心亮点
                    float sp = pow(abs(cos(a * 4.0)), 6.0);
                    v = sp * smoothstep(0.25, 0.55, r) * smoothstep(1.0, 0.62, r) * 1.4 + exp(-r * 9.0) * 0.9;
                }
                else
                {
                    // 停摆：一圈刻度环 + 一段慢慢转的弧
                    float tick = pow(abs(cos(a * 9.0)), 24.0) * ring(r, 0.62, 0.06);
                    float arc = ring(r, 0.44, 0.05) * step(0.5, frac((a - rot * 2.0) / 6.2831 + 1.0));
                    v = tick * 0.9 + arc * 1.2;
                }
                v *= smoothstep(1.0, 0.86, max(abs(p.x), abs(p.y)));
                return half4(col.rgb * v * _Intensity * k, 0);
            }
            ENDHLSL
        }
    }
}
