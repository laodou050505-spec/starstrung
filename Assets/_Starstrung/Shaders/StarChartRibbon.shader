// 星图刻度带：沿路线铺一条带状网格，在着色器里画两条边线和刻度。
// uv.x = 沿路线的米数，uv.y = 横向 -1..1（边线在 ±_Rail）。
// 线宽按屏幕像素算（fwidth），远近都是一两个像素的细线、不锯齿不闪；线外有一圈很淡的光晕。
// 顶点颜色 = 这一段的亮度和色调（串经过时变亮、越危险越暖、倒流区冷蓝），每帧由脚本写入。
// 叠加混合：画在透视画面上像发光的金线。
Shader "Starstrung/StarChartRibbon"
{
    Properties
    {
        _Rail ("边线位置（横向）", Range(0.5, 1)) = 0.87
        _MinorEvery ("短刻度间隔（米）", Float) = 0.2
        _MinorLen ("短刻度长度（占带宽）", Range(0, 1)) = 0.28
        _LineCore ("线芯宽度（像素）", Float) = 0.9
        _Glow ("光晕强度", Range(0, 1)) = 0.22
        _Intensity ("整体亮度", Range(0, 4)) = 1.4
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
            float _SS_Grow;

            float _Rail, _MinorEvery, _MinorLen, _LineCore, _Glow, _Intensity;

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; UNITY_VERTEX_OUTPUT_STEREO };

            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = TransformObjectToHClip(v.pos.xyz); o.uv = v.uv; o.col = v.col; return o;
            }

            // 到一条线的距离（单位同 d）换算成像素后，算出抗锯齿的线芯 + 光晕
            // d：到线的距离（单位同 pxPerUnit 的分母）；glowW：光晕的物理宽度（同单位）
            float lineMask(float d, float pxPerUnit, float glowW, out float glow)
            {
                float px = d * pxPerUnit;
                glow = exp(-d / glowW) * saturate(px / 2.0);    // 光晕按物理距离衰减，线芯处不重复叠加
                return saturate(1.0 - (px - _LineCore * 0.5));
            }

            half4 frag(V i) : SV_Target
            {
                float y = abs(i.uv.y);
                float m = i.uv.x;
                float pyu = 1.0 / max(fwidth(i.uv.y), 1e-5);   // 横向每单位多少像素
                float pxm = 1.0 / max(fwidth(m), 1e-5);        // 沿线每米多少像素

                // 两条边线
                float g1; float rail = lineMask(abs(y - _Rail), pyu, 0.035, g1);
                // 刻度：在 0.1、0.3、0.5…… 米处；整米附近的是贯穿刻度
                float k = round((m - _MinorEvery * 0.5) / _MinorEvery);
                float mk = _MinorEvery * 0.5 + _MinorEvery * k;
                float g2; float tick = lineMask(abs(m - mk), pxm, 0.004, g2);
                bool major = abs(mk - round(mk)) < 0.06;
                float inner = major ? 0.0 : _Rail - _MinorLen * 2.0 * _Rail;
                float span = step(inner, y) * step(y, _Rail);
                tick *= span; g2 *= span;

                float a = rail + tick * (major ? 0.75 : 0.55) + (g1 + g2 * 0.5) * _Glow;
                a *= smoothstep(1.0, 0.93, y);   // 带子边缘之前完全淡出，不露硬边
                // 远处像素太细时，线会断续闪烁：按像素密度把亮度摊匀
                float fadeFar = saturate(pyu / 6.0);
                a *= lerp(0.6, 1.0, fadeFar);
                a *= smoothstep(_SS_Grow, _SS_Grow - 0.8, m);   // 进关时跟着路线一起从入口长出来
                return half4(i.col.rgb * a * _Intensity, 0);
            }
            ENDHLSL
        }
    }
}
