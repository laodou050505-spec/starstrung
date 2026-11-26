// 标题字标：用 TextMeshPro 的距离场字形（_MainTex.a，0.5 = 字的边），字身里填流动的星云和闪烁的星点，
// 外面一圈柔光；_Reveal 从右往左“写出”字（跟着从右边游过来的星链：显示 x > _Reveal 的部分），写到的前沿有一道亮光。叠加混合（透视画面透得过来）。
Shader "Starstrung/StarText"
{
    Properties
    {
        _MainTex ("字形图集", 2D) = "white" {}
        _ColA ("星云 1", Color) = (0.55, 0.75, 1.0, 1)
        _ColB ("星云 2", Color) = (0.78, 0.55, 1.0, 1)
        _ColC ("点缀", Color) = (1.0, 0.6, 0.9, 1)
        _Intensity ("亮度", Range(0, 4)) = 1.3
        _Reveal ("写出进度（物体空间 x，显示右边）", Float) = -10
        _Alpha ("整体显隐", Range(0, 1)) = 1
        _Glow ("外光", Range(0, 2)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend One OneMinusSrcAlpha   // 预乘透明：字身实、外面一圈深色衬底把字从碎片和光芒里托出来（不再是一团外发光）
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _ColA, _ColB, _ColC; float _Intensity, _Reveal, _Alpha, _Glow;
            struct A { float4 pos : POSITION; float4 uv : TEXCOORD0; float4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 os : TEXCOORD1; float4 col : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos = TransformObjectToHClip(v.pos.xyz); o.uv = v.uv.xy; o.os = v.pos.xyz; o.col = v.col; return o; }

            float h2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float n2(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(h2(i), h2(i + float2(1, 0)), f.x), lerp(h2(i + float2(0, 1)), h2(i + float2(1, 1)), f.x), f.y); }
            float fbm(float2 p) { float s = 0, a = 0.5; [unroll] for (int k = 0; k < 4; k++) { s += a * n2(p); p = p * 2.1 + 3.7; a *= 0.5; } return s; }

            half4 frag(V i) : SV_Target
            {
                float sdf = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                float aa = max(fwidth(sdf), 1e-4) * 0.75;
                float fill = smoothstep(0.5 - aa, 0.5 + aa, sdf);          // 边缘干净（11-24 用户：标题发虚）
                float shade = smoothstep(0.2, 0.47, sdf) * (1 - fill);      // 外面一圈柔和深色衬底
                float t = _Time.y;
                float2 p = i.os.xy;
                // 字身：香槟白（上）→ 淡银紫（下），只留很淡的流动星云和星点
                float vgrad = saturate(p.y * 4.0 + 0.5);
                float3 c = lerp(_ColB.rgb, _ColA.rgb, vgrad);
                float n = fbm(p * 3.0 + float2(t * 0.04, -t * 0.02));
                c *= 0.92 + 0.12 * n;
                float2 g = p * 38.0; float2 ci = floor(g); float2 f = frac(g) - 0.5;
                float hh = h2(ci);
                float star = step(0.86, hh) * smoothstep(0.14, 0.0, length(f - (float2(h2(ci + 3.1), h2(ci + 7.7)) - 0.5) * 0.6)) * (0.5 + 0.5 * sin(t * (1.5 + 3 * hh) + hh * 40));
                c += _ColC.rgb * star * 0.25;
                // 写出：_Reveal 右边显示，前沿一道细亮光
                float rv = smoothstep(_Reveal, _Reveal + 0.04, p.x);
                float front = exp(-abs(p.x - _Reveal) * 30.0) * step(_Reveal - 0.02, p.x) * fill;
                float a = (fill + shade * 0.6) * rv * _Alpha * i.col.a;
                float3 col = (c * fill * _Intensity + float3(0.01, 0.015, 0.05) * shade * 0.6 + float3(0.9, 0.95, 1.0) * front) * rv * _Alpha * i.col.a;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
