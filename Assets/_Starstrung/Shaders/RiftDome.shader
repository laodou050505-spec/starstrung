// 时空裂缝（11-25 按用户要求重做）：
//   一开始没有裂缝，是真实房间；每通一关裂开一道不规则的口子（共 20 道，前后左右头顶随机分散），已有的口子随关卡一点点变大；
//   两道口子长到碰上就合成一个大口，边界依然不规则，而且外轮廓一直在轻轻起伏变化；
//   口子里露出的是同一片星空天空盒（按方向采样，世界固定）；结局 _Final：口子张满整个球面、边光消失，只剩星空。
// 做法：包住玩家的球壳，每个像素取方向 d：
//   每道口子 = 一个球冠（中心方向 + 角半径），先对 20 个球冠做平滑并集（合并处圆滑、不留接缝），
//   再统一加一层随时间漂移的 3D 噪声当边界（同一张噪声场 → 合并后的大口边界是一个整体，不规则且会动）。
// 性能：先算 20 个点积（很便宜），离所有口子都远的像素直接丢弃，只有口子里 / 边上才算星空。
Shader "Starstrung/RiftDome"
{
    Properties
    {
        _Final ("结局全开", Range(0, 1)) = 0
        _ColA ("星云 1", Color) = (0.12, 0.3, 1.0, 1)
        _ColB ("星云 2", Color) = (0.55, 0.22, 0.95, 1)
        _ColC ("点缀", Color) = (0.95, 0.35, 0.8, 1)
        _Rim ("边光", Color) = (0.65, 0.9, 1.0, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _Final; float4 _ColA, _ColB, _ColC, _Rim;
            // 每道口子：xyz = 中心方向（单位向量），w = 角半径（弧度，0 = 还没裂开）
            float4 _B[20];

            struct A { float4 pos : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float3 posOS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = TransformObjectToHClip(v.pos.xyz); o.posOS = v.pos.xyz;
                return o;
            }

            float h1(float n) { return frac(sin(n) * 43758.5453); }
            float h3(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
            float n1(float x) { float i = floor(x), f = frac(x); f = f * f * (3 - 2 * f); return lerp(h1(i), h1(i + 1), f); }
            float n3(float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(h3(i), h3(i + float3(1, 0, 0)), f.x), lerp(h3(i + float3(0, 1, 0)), h3(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(h3(i + float3(0, 0, 1)), h3(i + float3(1, 0, 1)), f.x), lerp(h3(i + float3(0, 1, 1)), h3(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }
            // 平滑并集（11-25 用户：裂缝合并后的交界要自然）：两道开口的边缘在交汇处顺着弯过去，不出现尖折角；k = 圆滑半径（弧度）
            float smax(float a, float b, float k) { float h = saturate(0.5 + 0.5 * (a - b) / k); return lerp(b, a, h) + k * h * (1 - h); }
            float fbm3(float3 p) { float a = 0.5, s = 0; [unroll] for (int k = 0; k < 4; k++) { s += a * n3(p); p = p * 2.03 + 5.7; a *= 0.5; } return s; }

            // 星点：3D 格子里随机一颗（在单位球面上切出来，方向固定 → 整片天空是同一个天空盒）
            float stars(float3 d, float k, float dens, float size)
            {
                float3 p = d * k; float3 c = floor(p);
                float h = h3(c);
                if (h < 1 - dens) return 0;
                float3 o = float3(h3(c + 3.1), h3(c + 7.7), h3(c + 1.3)) - 0.5;
                float dd = length(frac(p) - 0.5 - o * 0.6);
                float tw = 0.6 + 0.4 * sin(_Time.y * (0.7 + 2.5 * h) + h * 30);
                return smoothstep(size, 0, dd) * tw;
            }
            // 少数四芒亮星（按屏幕方向画十字芒：用切平面坐标）
            float3 flare(float3 d, float k, float dens)
            {
                float3 p = d * k; float3 c = floor(p);
                float h = h3(c + 11.0);
                if (h < 1 - dens) return 0;
                float3 o = float3(h3(c + 5.1), h3(c + 2.7), h3(c + 9.3)) - 0.5;
                float3 q = frac(p) - 0.5 - o * 0.5;
                float3 up = abs(d.y) < 0.95 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 ax = normalize(cross(up, d)), ay = cross(d, ax);
                float2 uv = float2(dot(q, ax), dot(q, ay));
                float s = 1.0 + 1.6 * pow(frac(h * 13.7), 2.0);
                float tw = 0.65 + 0.35 * sin(_Time.y * (0.8 + 2.0 * h) + h * 40);
                float core = exp(-dot(uv, uv) * 2600 / (s * s));
                float cr = (exp(-abs(uv.x) * 240 / s) * exp(-abs(uv.y) * 10 / s) + exp(-abs(uv.y) * 240 / s) * exp(-abs(uv.x) * 10 / s)) * 0.8;
                float3 col = lerp(float3(1, 0.97, 0.92), frac(h * 7.3) < 0.5 ? float3(0.6, 0.9, 1.0) : float3(1.0, 0.85, 0.6), frac(h * 3.1) * 0.6);
                return col * (core * 1.6 + cr) * tw;
            }

            // 裂缝后面的星空天空盒
            float3 skybox(float3 d)
            {
                float3 deep = lerp(float3(0.004, 0.006, 0.022), float3(0.012, 0.016, 0.05), saturate(d.y * 0.5 + 0.5));
                // 一道斜贯天空的银河带（主角）：带里星云丝 + 密集星尘 + 暗尘带；带外是干净的深色星空
                float bx = dot(d, normalize(float3(0.35, 0.82, -0.45)));
                float warp = (fbm3(d * 3.0) - 0.5) * 0.25;
                float band = exp(-pow((bx + warp) / 0.26, 2.0));
                float core = exp(-pow((bx + warp * 0.6) / 0.09, 2.0));
                float neb = fbm3(d * 4.5 + float3(_Time.y * 0.003, 0, 0));
                float wisp = smoothstep(0.42, 0.85, fbm3(d * 9.0 - 3.0 + neb * 1.2));
                float3 nc = lerp(_ColA.rgb, _ColB.rgb, smoothstep(0.4, 0.75, neb));
                nc = lerp(nc, _ColC.rgb, smoothstep(0.62, 0.8, neb) * 0.5);
                float dust = smoothstep(0.6, 0.8, fbm3(d * 5.0 + 7.0)) * core;   // 暗尘带：银河中间几缕暗线
                float3 c = deep + nc * (band * (0.25 + 0.55 * wisp) + 0.04 * wisp) * (1 - dust * 0.35);
                c += float3(0.75, 0.82, 1.0) * core * 0.12 * (1 - dust);
                float s1 = stars(d, 60.0, 0.3, 0.18);
                float s2 = stars(d, 140.0, 0.28 + 0.35 * band, 0.22) * 0.6;
                float s3 = stars(d, 320.0, 0.5 * band, 0.3) * 0.45;
                c += float3(0.88, 0.93, 1.0) * (s1 + s2) + lerp(float3(0.8, 0.85, 1), float3(1, 0.9, 0.8), core) * s3;
                return c + flare(d, 22.0, 0.14) * 1.25;
            }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.posOS);
                float tm = _Time.y;
                // 1) 20 个球冠的平滑并集：U > 0 在某道口子里；Rw = 附近口子的加权半径（决定这一带边界起伏的尺度）
                float U = -10.0, Rw = 0.0, wS = 0.0;
                [loop] for (int k = 0; k < 20; k++)
                {
                    float R = _B[k].w; if (R <= 0) continue;
                    float dist = acos(clamp(dot(d, _B[k].xyz), -1, 1));
                    float e = R - dist;
                    U = smax(U, e, 0.13);   // 圆滑半径大一点：两个口子靠近时提前用圆滑的桥连起来，夹缝里不留小孤岛、不出现 X 形细腰
                    float w = exp(-max(-e, 0) * 12.0); Rw += R * w; wS += w;
                }
                Rw = wS > 1e-4 ? Rw / wS : 0.05;
                // 结局：张满整个球面
                U = lerp(U, 4.0, _Final * _Final);
                if (U < -0.35) discard;
                // 2) 不规则、会动的边界：大起伏跟口子大小走，细碎撕裂是固定的小幅度；噪声随时间缓慢漂移 → 外轮廓一直轻轻变化
                float3 q = d + float3(sin(tm * 0.05), cos(tm * 0.043), sin(tm * 0.037)) * 0.12;
                float big = (fbm3(q * 2.6 + 4.0) - 0.5) * 1.6;
                float mid = n3(q * 7.0 + tm * 0.03) - 0.5;
                float fine = n3(q * 22.0 - tm * 0.05) * 0.6 + n3(q * 55.0) * 0.4 - 0.5;
                float breath = sin(tm * 0.7 + dot(d, float3(3.1, 1.7, 2.3))) * 0.012;
                // 小口子相对起伏更大（刚裂开就是撕裂的形状，不是圆圈）；大口子的起伏有上限（不长成尖刺）
                float E0 = U + big * min(Rw * 1.5, 0.32);
                // 中、细两层起伏只在边界附近起作用：开口里面不会被抠出房间“孤岛”，外面也不会冒出脱离主体的小碎口
                float nearEdge = 1.0 - smoothstep(0.03, 0.09, abs(E0));
                float E = E0 + (mid * min(Rw * 0.7, 0.09) + fine * min(Rw * 0.35, 0.028)) * nearEdge + breath * saturate(Rw * 8.0);
                E = lerp(E, 4.0, _Final * _Final);
                float px = max(fwidth(E), 1e-4);
                float rimFade = 1.0 - _Final;   // 结局：边光消失，只剩星空

                float3 col = 0; float alpha = 0;
                if (E > -px * 2)
                {
                    float3 sky = skybox(d);
                    alpha = smoothstep(-px, px, E);
                    // 靠近边缘微微亮（像光从裂口溢出），很窄
                    sky += _Rim.rgb * exp(-max(E, 0) / 0.008) * 0.2 * rimFade;
                    col = sky * alpha;
                }
                // 边光：发丝细线 + 很淡的柔光；颜色、亮点按方向取连续噪声（合并后的大口一圈颜色连续，没有跳变）
                float rim = exp(-abs(E) / max(0.0014, px * 1.1)) * 0.85 + exp(-abs(E) / 0.008) * 0.08;
                float3 rimC = lerp(_Rim.rgb, _ColB.rgb * 1.3, n3(d * 5.0 + float3(0, tm * 0.15, 0)));
                rim *= 0.8 + 1.2 * pow(n3(d * 22.0 + float3(tm * 0.4, 0, 0)), 4.0);
                col += rimC * rim * 1.3 * rimFade;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
