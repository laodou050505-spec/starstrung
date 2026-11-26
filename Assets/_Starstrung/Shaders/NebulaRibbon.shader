// 星云气：沿路线的两条交叉带（一横一竖），用流动的分形噪声画柔软的云雾，叠加混合。
// uv.x = 沿线米数，uv.y = 横向 -1..1；顶点颜色 r = 亮度（串经过时变亮），g = 暖色程度。
// 只做很淡的一层“气”，让粒子星流有体积和颜色底子。
Shader "Starstrung/NebulaRibbon"
{
    Properties
    {
        _ColA ("主色", Color) = (0.15, 0.35, 1.0, 1)
        _ColB ("副色", Color) = (0.55, 0.2, 0.95, 1)
        _ColC ("点缀", Color) = (0.95, 0.3, 0.75, 1)
        _Intensity ("亮度", Range(0, 2)) = 0.35
        _Scale ("噪声尺度", Float) = 2.2
        _Flow ("流速", Float) = 0.12
        _FromM ("起点（米）", Float) = 0
        _Length ("终点（米）", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
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
            float4 _ColA, _ColB, _ColC; float _Intensity, _Scale, _Flow, _FromM, _Length; float _SS_Grow;

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; float3 n : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; float facing : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(v.pos.xyz);
                o.pos = TransformWorldToHClip(ws); o.uv = v.uv; o.col = v.col;
                // 带子侧对相机时淡出（避免看到一片薄片的边）
                float3 n = normalize(TransformObjectToWorldNormal(v.n));
                o.facing = abs(dot(n, normalize(GetCameraPositionWS() - ws)));
                return o;
            }

            float h2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float n2(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(h2(i), h2(i + float2(1, 0)), f.x), lerp(h2(i + float2(0, 1)), h2(i + float2(1, 1)), f.x), f.y); }
            float fbm2(float2 p) { float a = 0.5, s = 0; [unroll] for (int k = 0; k < 4; k++) { s += a * n2(p); p = p * 2.03 + 7.1; a *= 0.5; } return s; }

            half4 frag(V i) : SV_Target
            {
                float y = i.uv.y;
                float2 p = float2(i.uv.x * _Scale - _Time.y * _Flow, y * 1.1);
                float w = fbm2(p * 0.7 + float2(0, _Time.y * 0.05));
                float d = fbm2(p + w * 1.8);
                float body = exp(-y * y * 3.0);                              // 中间浓、两边淡
                float cloud = smoothstep(0.35, 0.85, d) * body;
                float3 col = lerp(_ColA.rgb, _ColB.rgb, smoothstep(0.3, 0.8, w));
                col = lerp(col, _ColC.rgb, smoothstep(0.72, 0.9, d) * 0.6);
                float lit = 0.5 + i.col.r * 1.3;
                col = lerp(col, col * float3(1.3, 0.85, 0.7), i.col.g * 0.5);
                // 两头渐隐（入口从裂口里慢慢浮现，进星核前慢慢收掉，不露截断的硬边）
                float ends = smoothstep(_FromM, _FromM + 0.8, i.uv.x) * smoothstep(_Length, _Length - 1.2, i.uv.x) * smoothstep(_SS_Grow, _SS_Grow - 1.2, i.uv.x);
                float a = cloud * lit * _Intensity * smoothstep(0.15, 0.6, i.facing) * smoothstep(1.0, 0.6, abs(y)) * ends;
                return half4(col * a, 0);
            }
            ENDHLSL
        }
    }
}
