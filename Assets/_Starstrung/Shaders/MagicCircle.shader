// 魔法阵光盘（星核下方、入口、时空裂缝都用）：一张方片上画多层细圆环、刻度、七芒星连线、四芒小星，
// 每一层以不同速度、方向旋转。不用符文。叠加混合。
// 线条是“手绘感”：圆环略不圆、粗细和深浅随笔压变化、偶尔断笔并有一笔淡淡的重描；星形连线微微弯、两头出头并收尖；
// 刻度长短角度略不齐、虚线长短不一。幅度都很小（约半径的 0.5%），不歪歪扭扭。_Seed 让每层的“笔迹”不同。
// _Spin.xyzw = 各层转速（弧度/秒）：外圈刻度、七芒星、内圈虚线、最内小圈
Shader "Starstrung/MagicCircle"
{
    Properties
    {
        _ColA ("主色", Color) = (0.55, 0.45, 1.0, 1)
        _ColB ("亮色", Color) = (0.75, 0.9, 1.0, 1)
        _ColC ("点缀", Color) = (1.0, 0.45, 0.85, 1)
        _Spin ("各层转速", Vector) = (0.08, -0.05, 0.15, -0.25)
        _Intensity ("亮度", Range(0, 4)) = 1.3
        _Warn ("预警闪烁", Range(0, 1)) = 0
        _Points ("星形顶点数", Float) = 7
        _Mask ("画哪些：外环刻度 / 星形 / 虚线环 / 内弧", Vector) = (1, 1, 1, 1)
        _Seed ("笔迹种子", Float) = 0
        _Hand ("手绘程度", Range(0, 2)) = 1
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
            float4 _ColA, _ColB, _ColC, _Spin, _Mask; float _Intensity, _Warn, _Points, _Seed, _Hand;

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos = TransformObjectToHClip(v.pos.xyz); o.uv = v.uv * 2 - 1; return o; }

            float2 rot(float2 p, float a) { float c = cos(a), s = sin(a); return float2(c * p.x - s * p.y, s * p.x + c * p.y); }
            // 抗锯齿细线：d 到线的距离，px 每单位像素数，w 线宽（像素）
            float ln(float d, float px, float w) { return saturate(1.0 - (abs(d) * px - w)); }
            float hsh(float n) { return frac(sin(n * 12.9898 + _Seed * 7.13) * 43758.5453); }
            // 绕一圈是周期的起伏（角度 a 上没有接缝），-1..1
            float wob(float a, float s) { return sin(a * 3.0 + s) * 0.5 + sin(a * 7.0 + s * 1.7) * 0.3 + sin(a * 13.0 + s * 2.9) * 0.2; }

            // 手绘圆环：半径带一点起伏，粗细 / 深浅随“笔压”变，偶尔断笔；再淡淡重描一笔（略错开）
            float handRing(float r, float a, float R, float px, float w, float s)
            {
                float hd = _Hand;
                float rr = R + wob(a, s) * 0.0045 * hd;
                float pres = 0.72 + 0.28 * wob(a * 1.0 + 1.3, s + 4.0);
                float gap = 1.0 - smoothstep(0.72, 0.86, wob(a, s + 9.0)) * 0.85 * saturate(hd);   // 断笔处
                float main = ln(r - rr, px, w * pres) * (0.65 + 0.35 * pres) * gap;
                float rr2 = R + wob(a, s + 21.0) * 0.006 * hd + 0.0035 * hd;
                float again = ln(r - rr2, px, w * 0.55) * 0.28 * smoothstep(0.1, 0.6, wob(a, s + 33.0)) * saturate(hd);   // 重描的那一笔，只出现在一部分
                return saturate(main + again);
            }

            // 手绘直线：微微弯（一笔划过去的弧度）+ 细小抖动，两头出头并收尖，中段笔压最重
            float handSeg(float2 p, float2 a, float2 b, float px, float w, float s)
            {
                float hd = _Hand;
                float2 ba = b - a; float L = length(ba); float2 dir = ba / L, nrm = float2(-dir.y, dir.x);
                float2 pa = p - a;
                float u = dot(pa, dir) / L, perp = dot(pa, nrm);
                float uc = saturate(u);
                perp -= ((hsh(s) - 0.5) * 0.028 * sin(uc * PI) + 0.0022 * sin(uc * 17.0 + s * 3.1) + 0.0014 * sin(uc * 43.0 + s)) * hd;
                float over = (0.02 + 0.03 * hsh(s + 1.0)) / L * hd;      // 出头的长度
                float ends = smoothstep(-over, 0.0, u) * smoothstep(1.0 + over, 1.0, u);
                ends = u < 0 || u > 1 ? ends * ends : 1.0;              // 出头的部分收尖变淡
                float pres = 0.7 + 0.3 * sin(uc * PI);
                return ln(perp, px, w * pres * lerp(0.4, 1.0, ends)) * ends * (0.7 + 0.3 * pres) * step(-over, u) * step(u, 1.0 + over);
            }

            half4 frag(V i) : SV_Target
            {
                float2 p = i.uv; float t = _Time.y;
                float r = length(p);
                float px = 1.0 / max(fwidth(r), 1e-5);
                float3 col = 0;
                float sd = _Seed * 5.31;

                // 外圈：双细环 + 刻度（每 5° 短、每 30° 长；每根刻度的角度、长短略不齐）
                float2 q = rot(p, t * _Spin.x);
                float a = atan2(q.y, q.x);
                float ring = handRing(r, a, 0.95, px, 0.6, sd) + handRing(r, a, 0.9, px, 0.5, sd + 11.0) * 0.7;
                float stA = PI / 36.0, stB = PI / 6.0;
                float iA = floor(a / stA + 0.5), iB = floor(a / stB + 0.5);
                float tickA = abs(a - (iA + (hsh(iA) - 0.5) * 0.18 * _Hand) * stA) * r;
                float tickB = abs(a - (iB + (hsh(iB + 50.0) - 0.5) * 0.06 * _Hand) * stB) * r;
                float inA = 0.915 - (hsh(iA + 7.0) - 0.3) * 0.012 * _Hand, inB = 0.86 - (hsh(iB + 9.0) - 0.5) * 0.02 * _Hand;
                float ticks = ln(tickA, px, 0.4) * smoothstep(inA - 0.004, inA + 0.004, r) * step(r, 0.95) * 0.35 * (0.6 + 0.4 * hsh(iA + 3.0))
                            + ln(tickB, px, 0.5) * smoothstep(inB - 0.006, inB + 0.01, r) * step(r, 0.95) * 0.8;
                col += _ColB.rgb * (ring * 0.7 + ticks) * 0.75 * _Mask.x;

                // 星形连线（{N/3} 星形多边形）+ 顶点上的四芒小星
                float2 q2 = rot(p, t * _Spin.y);
                float star = 0, sparks = 0;
                int N = (int)_Points;
                [loop] for (int k = 0; k < 9; k++)
                {
                    if (k >= N) break;
                    float a0 = k * 2.0 * PI / N + PI * 0.5, a1 = (k + 3) * 2.0 * PI / N + PI * 0.5;
                    // 顶点位置也略有出入（手画的不会每个顶点都正好落在圆上）
                    float2 v0 = float2(cos(a0), sin(a0)) * (0.72 + (hsh(k + 20.0) - 0.5) * 0.012 * _Hand);
                    float2 v1 = float2(cos(a1), sin(a1)) * (0.72 + (hsh(fmod(k + 3.0, N) + 20.0) - 0.5) * 0.012 * _Hand);
                    star += handSeg(q2, v0, v1, px, 0.5, k * 1.7 + sd);
                    float2 dv = q2 - v0;
                    sparks += exp(-length(dv) * 60.0) + (exp(-abs(dv.x) * 220.0) * saturate(1 - abs(dv.y) * 14) + exp(-abs(dv.y) * 220.0) * saturate(1 - abs(dv.x) * 14)) * 0.8;
                }
                float a2 = atan2(q2.y, q2.x);
                col += (_ColA.rgb * saturate(star) * 0.8 + _ColB.rgb * sparks * 1.5 + _ColA.rgb * handRing(r, a2, 0.72, px, 0.5, sd + 5.0) * 0.6) * _Mask.y;

                // 内圈虚线环（转得快；每段长短不一）
                float2 q3 = rot(p, t * _Spin.z);
                float a3 = atan2(q3.y, q3.x);
                float i3 = floor(a3 / (PI / 12.0));
                float dash = smoothstep(0.0, 0.04, frac(a3 / (PI / 12.0)) - (0.38 + (hsh(i3 + 70.0) - 0.5) * 0.3 * _Hand));
                col += _ColC.rgb * handRing(r, a3, 0.5, px, 0.6, sd + 17.0) * dash * 0.9 * _Mask.z;
                // 最内小圈 + 四段弧
                float2 q4 = rot(p, t * _Spin.w);
                float a4 = atan2(q4.y, q4.x);
                float i4 = floor(a4 / (PI * 0.5));
                float arcs = smoothstep(0.0, 0.05, frac(a4 / (PI * 0.5)) - (0.2 + hsh(i4 + 90.0) * 0.12 * _Hand));
                col += (_ColB.rgb * handRing(r, a4, 0.3, px, 0.6, sd + 23.0) * arcs * 0.8 + _ColA.rgb * handRing(r, a4, 0.26, px, 0.4, sd + 29.0) * 0.5) * _Mask.w;

                // 整体：中心柔光 + 外缘淡出 + 预警闪烁
                col += _ColA.rgb * exp(-r * 4.0) * 0.25 * max(_Mask.x, _Mask.y);
                col *= smoothstep(1.0, 0.97, r);
                col *= 1.0 + _Warn * (0.6 + 0.6 * sin(t * 16.0));
                return half4(col * _Intensity, 0);
            }
            ENDHLSL
        }
    }
}
