// 宝石星（《摘星》主角材质）
// - 内部体积星云：视线进入球体时按折射率 1.5 折射，在球内步进采样三维分形噪声，带吸收（宝石色）和发光（星云亮部）；
// - 刻印符号：折射后的视线与“过球心、面朝视线”的平面求交，在那里画距离场符号 —— 符号像封在水晶深处，随视角有轻微视差；
// - 表面：程序星空反射 + 两处锐利高光 + 色散细边（红绿蓝三色菲涅尔幂次不同，边缘一圈虹彩）；
// - 支持 GPU 实例化（每颗星：颜色、符号、种子、高亮、消散）和单通道立体渲染（头显双眼一次画完）。
// 关键字 _QUALITY_LOW（PICO 档）：步数 10 → 6、噪声 3 层 → 2 层。
Shader "Starstrung/GemStar"
{
    Properties
    {
        _Color ("宝石色", Color) = (0.9, 0.2, 0.3, 1)
        _Symbol ("符号 0☆ 1☾ 2◎ 3◇ 4△ -1无", Float) = 0
        _Seed ("种子", Float) = 0
        _Highlight ("高亮（被指着）", Range(0, 1)) = 0
        _Fade ("消散（孤色消散 / 碎裂）", Range(0, 1)) = 0
        _Rainbow ("彩星", Range(0, 1)) = 0
        _Ice ("冰封", Range(0, 1)) = 0
        _Density ("星云浓度", Range(0, 4)) = 1.6
        _Glow ("自发光", Range(0, 4)) = 1.25
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Gem"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _QUALITY_LOW
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float, _Symbol)
                UNITY_DEFINE_INSTANCED_PROP(float, _Seed)
                UNITY_DEFINE_INSTANCED_PROP(float, _Highlight)
                UNITY_DEFINE_INSTANCED_PROP(float, _Fade)
                UNITY_DEFINE_INSTANCED_PROP(float, _Rainbow)
                UNITY_DEFINE_INSTANCED_PROP(float, _Ice)
            UNITY_INSTANCING_BUFFER_END(Props)
            float _Density, _Glow;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posOS : TEXCOORD0;      // 球面点（物体空间，半径 0.5）
                float3 camOS : TEXCOORD1;      // 相机位置（物体空间）
                float3 normalWS : TEXCOORD2;
                float3 viewWS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.posOS = v.positionOS.xyz;
                o.camOS = TransformWorldToObject(GetCameraPositionWS());
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetCameraPositionWS() - ws;
                return o;
            }

            // ───── 噪声 ─────
            float hash3(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float vnoise(float3 x)
            {
                float3 i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash3(i), hash3(i + float3(1,0,0)), f.x), lerp(hash3(i + float3(0,1,0)), hash3(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash3(i + float3(0,0,1)), hash3(i + float3(1,0,1)), f.x), lerp(hash3(i + float3(0,1,1)), hash3(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            float fbm(float3 p)
            {
                float a = 0.5, s = 0.0;
                #if defined(_QUALITY_LOW)
                    [unroll] for (int i = 0; i < 2; i++) { s += a * vnoise(p); p = p * 2.07 + 11.7; a *= 0.5; }
                    s *= 1.25;
                #else
                    [unroll] for (int i = 0; i < 3; i++) { s += a * vnoise(p); p = p * 2.07 + 11.7; a *= 0.5; }
                    s *= 1.12;
                #endif
                return s;
            }

            // ───── 符号距离场 ─────
            float sdStar5(float2 p, float r, float rf)
            {
                const float2 k1 = float2(0.809016994375, -0.587785252292);
                const float2 k2 = float2(-0.809016994375, -0.587785252292);
                p.x = abs(p.x);
                p -= 2.0 * max(dot(k1, p), 0.0) * k1;
                p -= 2.0 * max(dot(k2, p), 0.0) * k2;
                p.x = abs(p.x); p.y -= r;
                float2 ba = rf * float2(-k1.y, k1.x) - float2(0, 1);
                float h = clamp(dot(p, ba) / dot(ba, ba), 0.0, r);
                return length(p - ba * h) * sign(p.y * ba.x - p.x * ba.y);
            }
            float sdTri(float2 p, float r)
            {
                const float k = 1.7320508;
                p.x = abs(p.x) - r; p.y = p.y + r / k;
                if (p.x + k * p.y > 0.0) p = float2(p.x - k * p.y, -k * p.x - p.y) / 2.0;
                p.x -= clamp(p.x, -2.0 * r, 0.0);
                return -length(p) * sign(p.y);
            }
            float symbolDist(float s, float2 p)
            {
                if (s < -0.5) return 1.0;
                if (s < 0.5) return abs(sdStar5(p, 0.5, 0.45));
                if (s < 1.5) return abs(max(length(p) - 0.48, -(length(p - float2(0.23, 0.1)) - 0.4)));
                if (s < 2.5) return min(abs(length(p) - 0.48), abs(length(p) - 0.18));
                if (s < 3.5) return abs(abs(p.x) + abs(p.y) * 1.15 - 0.54) * 0.7071;
                if (s < 4.5) return abs(sdTri(p - float2(0, -0.06), 0.48));
                return 1.0;
            }

            // 程序星空（反射用）：上亮下暗的深蓝 + 稀疏星点
            float3 skyRefl(float3 d)
            {
                float3 c = lerp(float3(0.015, 0.02, 0.05), float3(0.06, 0.07, 0.16), saturate(d.y * 0.5 + 0.5));
                float3 cell = floor(d * 60.0);
                float h = hash3(cell);
                c += step(0.985, h) * smoothstep(0.35, 0.0, length(frac(d * 60.0) - 0.5)) * 1.5;
                return c;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 col = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                float sym = UNITY_ACCESS_INSTANCED_PROP(Props, _Symbol);
                float seed = UNITY_ACCESS_INSTANCED_PROP(Props, _Seed);
                float hi = UNITY_ACCESS_INSTANCED_PROP(Props, _Highlight);
                float fade = UNITY_ACCESS_INSTANCED_PROP(Props, _Fade);
                float rainbow = UNITY_ACCESS_INSTANCED_PROP(Props, _Rainbow);
                float ice = UNITY_ACCESS_INSTANCED_PROP(Props, _Ice);
                float t = _Time.y;

                float3 base = col.rgb;
                if (rainbow > 0.5)
                {
                    float hh = frac(t * 0.08 + seed + i.posOS.y * 0.6);
                    base = saturate(abs(frac(hh + float3(0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0) * 0.9 + 0.05;
                }

                // 物体空间里的视线与法线（球半径 0.5）
                float3 p0 = i.posOS;
                float3 n = normalize(p0);
                float3 rd = normalize(p0 - i.camOS);
                float ndv = saturate(dot(n, -rd));

                // 折射进球内
                float3 rr = refract(rd, n, 1.0 / 1.5);
                // 出射点：沿 rr 穿过半径 0.5 的球
                float b = dot(p0, rr);
                float c0 = dot(p0, p0) - 0.25;
                float tExit = -b + sqrt(max(0.0, b * b - c0));

                // 体积：沿折射光线步进。宝石色按比尔定律吸收（越深越饱和），星云只在几缕“光带”里发光，另有星屑亮点
                #if defined(_QUALITY_LOW)
                    const int STEPS = 6;
                #else
                    const int STEPS = 10;
                #endif
                float stepLen = tExit / STEPS;
                float ang = t * 0.09 + seed * 6.2831;
                float2x2 rot = float2x2(cos(ang), -sin(ang), sin(ang), cos(ang));
                // 吸收系数：颜色越“缺”的通道吸收越强 → 透过来的是浓郁的宝石色
                float3 absorb = (1.0 - base) * 9.0 + 1.4;
                float3 glowC = lerp(base, float3(1.0, 0.96, 0.9), 0.12);   // 11-25：0.35 → 0.12，星云丝不再把整颗洗成奶白
                float3 acc = 0; float3 trans = 1.0;
                [unroll] for (int k = 0; k < STEPS; k++)
                {
                    float3 q = p0 + rr * (stepLen * (k + 0.5));
                    float3 qn = q * 2.6; qn.xz = mul(rot, qn.xz); qn += seed * 17.0;
                    float warp = fbm(qn * 1.3 + t * 0.05);
                    float d = fbm(qn + warp * 1.6);
                    float rad = 1.0 - saturate(length(q) * 2.0);
                    // 光带：只取 fbm 的一条窄“脊”，像星云里的丝
                    float ridge = 1.0 - abs(d - 0.55) * 4.0;
                    float wisp = pow(saturate(ridge), 3.0) * (0.4 + rad);
                    // 星屑：体积里稀疏的亮点
                    float3 cell = floor(qn * 8.0);
                    float h = hash3(cell + seed);
                    float dust = step(0.9, h) * pow(saturate(1.0 - length(frac(qn * 8.0) - 0.5) * 4.0), 6.0);
                    float3 em = glowC * wisp * 1.8 + float3(1, 0.97, 0.92) * dust * 9.0 + base * rad * rad * 0.35;
                    acc += em * trans * stepLen;
                    trans *= exp(-absorb * stepLen * (0.6 + rad));
                }
                // 光穿过宝石后的底色：浓郁、深
                acc += base * base * 0.08 * trans;
                // 亮色（金黄）本身吸收弱、会整颗发亮 → 按亮度归一，五色一样“深”；边缘像厚玻璃一样暗下去
                float luma = dot(base, float3(0.299, 0.587, 0.114));
                acc *= 1.35 * (0.5 / (0.22 + luma)) * (0.3 + 0.7 * pow(ndv, 0.7));

                // 内反射：光线在背面反射回来，碰到程序星空 → 一些锐利的小亮点（宝石的“火”）
                float3 pBack = p0 + rr * tExit;
                float3 nBack = normalize(pBack);
                float3 rIn = reflect(rr, -nBack);
                float3 cellR = floor(rIn * 22.0 + seed * 13.0);
                float hr = hash3(cellR);
                float fire = step(0.955, hr) * pow(saturate(1.0 - length(frac(rIn * 22.0 + seed * 13.0) - 0.5) * 3.0), 8.0);
                acc += lerp(base, float3(1, 1, 1), 0.5) * fire * 2.5 * (0.3 + 0.7 * ndv);

                // 星心：中心一点暖光
                float3 mid = p0 + rr * tExit * 0.5;
                float coreGlow = pow(saturate(1.0 - length(mid) * 2.6), 4.0);
                acc += lerp(base, float3(1, 0.95, 0.85), 0.3) * coreGlow * 0.7;

                // 刻印符号：细、半透明，像刻在水晶深处的发丝线
                float3 pn = normalize(-i.camOS);
                float denom = dot(rr, pn);
                float tp = abs(denom) > 1e-4 ? dot(-p0, pn) / denom : -1.0;
                if (tp > 0.0 && tp < tExit)
                {
                    float3 hp = p0 + rr * tp;
                    float3 up = abs(pn.y) < 0.95 ? float3(0, 1, 0) : float3(1, 0, 0);
                    float3 ax = normalize(cross(up, pn)), ay = cross(pn, ax);
                    float2 uv = float2(dot(hp, ax), dot(hp, ay)) / 0.27;
                    float sd = symbolDist(sym, uv);
                    float aa = fwidth(sd) * 1.2 + 1e-4;
                    float etch = 1.0 - smoothstep(0.028 - aa, 0.028 + aa, sd);
                    float halo = exp(-sd * 14.0) * 0.18;
                    float vis = 0.35 + 0.65 * ndv;
                    acc += lerp(base, float3(1, 1, 1), 0.4) * etch * 0.6 * vis + base * halo * vis;
                }

                // 识别（11-25 用户：鼠标都不好认）：体内颜色再饱和一档，五色在 3～4 米外一眼分得开
                float accL = dot(acc, float3(0.299, 0.587, 0.114));
                acc = max(0, lerp(accL.xxx, acc, 1.45));

                // 冰封：外面一层霜白
                acc = lerp(acc, float3(0.75, 0.88, 1.0) * (0.35 + 0.65 * pow(1.0 - ndv, 1.5)), ice * 0.55 * pow(1.0 - ndv, 1.2));   // 11-25：霜只结在边缘，中心透出本色（远看也认得出是什么色）

                // 表面：反射 + 高光 + 色散细边
                float3 nW = normalize(i.normalWS), vW = normalize(i.viewWS);
                float3 refl = reflect(-vW, nW);
                float fr = pow(1.0 - saturate(dot(nW, vW)), 4.0);
                float3 rim = float3(pow(1.0 - ndv, 5.0), pow(1.0 - ndv, 6.0), pow(1.0 - ndv, 7.5));   // 色散：三色幂次不同
                float3 surf = skyRefl(refl) * (0.12 + fr * 1.0);
                float3 L1 = normalize(float3(0.4, 0.85, 0.35)), L2 = normalize(float3(-0.6, 0.3, -0.5));
                float sp = pow(saturate(dot(refl, L1)), 220.0) * 2.5 + pow(saturate(dot(refl, L2)), 90.0) * 0.5;
                float3 outC = acc * _Glow + surf + rim * lerp(base, float3(1, 1, 1), 0.3) * 0.55 + sp;

                // 被指着：外圈细亮（星盘准星另画，这里只是让星本身也微微醒过来）
                outC += lerp(base, float3(1, 1, 1), 0.25) * pow(1.0 - ndv, 2.0) * hi * 1.2;
                outC += acc * _Glow * hi * 0.4;   // 被指着：整颗醒过来（配合放大一圈和准星）
                // 消散：变暗、星云抽空
                outC *= 1.0 - fade * 0.75;
                return half4(outC, 1.0);
            }
            ENDHLSL
        }
    }
}
