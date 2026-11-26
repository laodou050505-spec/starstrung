// 星核星云球：球内体积光线步进（深蓝 → 紫 → 洋红的星云丝，青色星尘，暖白的心随心跳起伏），
// 表面有菲涅尔冷光边；_Cracks（0～3）在球面上点亮对应数量的发光裂纹（预警时 _CrackWarn 让下一道先闪）。
Shader "Starstrung/CoreNebula"
{
    Properties
    {
        _ColDeep ("深色", Color) = (0.01, 0.025, 0.12, 1)
        _ColMid ("中色", Color) = (0.12, 0.32, 1.0, 1)
        _ColHot ("亮丝", Color) = (0.6, 0.95, 1.0, 1)
        _ColAccent ("点缀", Color) = (0.95, 0.35, 0.85, 1)
        _ColHeart ("星心", Color) = (1.0, 0.88, 0.7, 1)
        _ColRim ("边光", Color) = (0.45, 0.65, 1.0, 1)
        _Pulse ("心跳", Range(0, 1)) = 0.5
        _Cracks ("裂纹数", Range(0, 3)) = 0
        _CrackWarn ("裂纹预警", Range(0, 1)) = 0
        _Flash ("受击闪光", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _QUALITY_LOW
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _ColDeep, _ColMid, _ColHot, _ColAccent, _ColHeart, _ColRim;
            float _Pulse, _Cracks, _CrackWarn, _Flash;

            struct A { float4 pos : POSITION; float3 n : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float3 posOS : TEXCOORD0; float3 camOS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = TransformObjectToHClip(v.pos.xyz); o.posOS = v.pos.xyz;
                o.camOS = TransformWorldToObject(GetCameraPositionWS());
                return o;
            }

            float hash3(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float vnoise(float3 x)
            {
                float3 i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash3(i), hash3(i + float3(1,0,0)), f.x), lerp(hash3(i + float3(0,1,0)), hash3(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash3(i + float3(0,0,1)), hash3(i + float3(1,0,1)), f.x), lerp(hash3(i + float3(0,1,1)), hash3(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            float fbm(float3 p) { float a = 0.5, s = 0; [unroll] for (int k = 0; k < 4; k++) { s += a * vnoise(p); p = p * 2.05 + 9.3; a *= 0.5; } return s * 1.07; }

            // 第 k 道裂纹：球面上一段弯曲的大圆弧（带噪声抖动）+ 两三根分叉，返回 (主干, 光晕)
            // 11-24 用户：裂开要更明显 → 主干更粗、白热的芯 + 琥珀色宽光晕 + 分叉，裂开后边缘还在“渗光”闪烁
            float2 crack(float3 n, int k)
            {
                // 三道裂纹都长在朝玩家的那一面（玩家在星核的 -z、偏上方），轴取“过中点的大圆”的法线
                float3 mids[3] = { normalize(float3(0.45, 0.45, -0.8)), normalize(float3(-0.55, 0.25, -0.8)), normalize(float3(0.05, -0.15, -1.0)) };
                float3 tilts[3] = { float3(0.2, 1, 0.1), float3(1, 0.5, 0), float3(0.8, -0.6, 0) };
                float3 ax = normalize(cross(mids[k], tilts[k]));
                float off = (vnoise(n * 9.0 + k * 13.0) - 0.5) * 0.09 + (vnoise(n * 26.0 + k * 7.0) - 0.5) * 0.03;
                float d = abs(dot(n, ax) + off);
                float extent = smoothstep(0.2, 0.65, dot(n, mids[k]));            // 比以前长
                float w = 0.02 + 0.016 * vnoise(n * 14.0 + k);
                float core = smoothstep(w, w * 0.2, d) * extent;
                float glow = exp(-d / 0.09) * extent;
                // 分叉：沿主干两侧斜出去的短裂纹
                [unroll] for (int b = 0; b < 3; b++)
                {
                    float3 bax = normalize(ax + mids[k] * (0.9 + 0.35 * b) * (b % 2 == 0 ? 1 : -1) + cross(ax, mids[k]) * 0.6);
                    float bd = abs(dot(n, bax) + (vnoise(n * 18.0 + b * 5.0 + k) - 0.5) * 0.05);
                    float bext = smoothstep(0.55, 0.8, dot(n, normalize(mids[k] + cross(ax, mids[k]) * (b - 1) * 0.5))) * extent;
                    core += smoothstep(w * 0.6, w * 0.1, bd) * bext * 0.8;
                    glow += exp(-bd / 0.05) * bext * 0.5;
                }
                return float2(saturate(core), glow);
            }

            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _Time.y;
                float3 p0 = i.posOS;                // 球半径 0.5
                float3 n = normalize(p0);
                float3 rd = normalize(p0 - i.camOS);
                float ndv = saturate(dot(n, -rd));
                float b = dot(p0, rd), c0 = dot(p0, p0) - 0.25;
                float tExit = -b + sqrt(max(0.0, b * b - c0));
                #if defined(_QUALITY_LOW)
                    const int STEPS = 9;
                #else
                    const int STEPS = 16;
                #endif
                float sl = tExit / STEPS;
                float3 acc = 0; float trans = 1;
                float2x2 rot = float2x2(cos(t * 0.07), -sin(t * 0.07), sin(t * 0.07), cos(t * 0.07));
                [unroll] for (int k = 0; k < STEPS; k++)
                {
                    float3 q = p0 + rd * (sl * (k + 0.5));
                    float r = length(q) * 2.0;                             // 0 球心 ~ 1 表面
                    float3 qn = q * 3.0; qn.xz = mul(rot, qn.xz);
                    float w = fbm(qn * 0.8 + t * 0.04);
                    float d = fbm(qn + w * 2.0 - t * 0.03);
                    float ridge = pow(saturate(1.0 - abs(d - 0.55) * 3.5), 3.0);
                    float3 col = lerp(_ColDeep.rgb, _ColMid.rgb * 0.7, smoothstep(0.45, 0.8, d));
                    col = lerp(col, _ColHot.rgb, ridge * 0.85);
                    col = lerp(col, _ColAccent.rgb, smoothstep(0.62, 0.8, w) * 0.45);      // 零星洋红
                    float heart = pow(saturate(1.0 - r * 1.9), 4.0) * (0.5 + 1.0 * _Pulse);
                    col += _ColHeart.rgb * heart * 3.0;
                    float3 cell = floor(qn * 6.0);
                    float dust = step(0.94, hash3(cell)) * pow(saturate(1.0 - length(frac(qn * 6.0) - 0.5) * 3.5), 6.0);
                    col += float3(0.6, 0.9, 1.0) * dust * 3.0;
                    float dens = (0.05 + ridge * 1.4 + heart * 2.0) * (1.0 - r * 0.5);
                    float a = saturate(dens * sl * 5.0);
                    acc += col * a * trans;
                    trans *= 1.0 - a;
                }
                acc += _ColDeep.rgb * 0.6 * trans;
                acc += _ColRim.rgb * pow(1.0 - ndv, 4.0) * 0.6;           // 冷光边（只在轮廓一圈）
                // 裂纹
                float crC = 0, crG = 0;
                [unroll] for (int j = 0; j < 3; j++)
                {
                    float on = saturate(_Cracks - j);
                    float warn = (j == (int)floor(_Cracks + 0.001)) ? _CrackWarn * (0.5 + 0.5 * sin(t * 18.0)) : 0;
                    float2 c = crack(n, j) * max(on, warn);
                    crC += c.x; crG += c.y;
                }
                float seep = 0.75 + 0.25 * sin(t * 7.0 + vnoise(n * 6.0) * 8.0);        // 裂缝渗光：一明一暗地跳
                // 裂得越多，球体本身越暗（光都从裂缝里漏出来），三道全裂时只剩裂缝在烧
                acc *= 1.0 - 0.22 * saturate(_Cracks);
                acc *= 1.0 - 0.35 * saturate(_Cracks - 2.0);
                acc = lerp(acc, float3(0.02, 0.0, 0.03), saturate(crC) * 0.9);                // 裂缝本身是深的
                acc += float3(1.0, 0.97, 0.9) * crC * 3.2 * seep;                              // 白热的芯
                acc += float3(1.0, 0.55, 0.25) * crG * 1.1 * seep;                             // 琥珀色宽光晕
                acc += float3(1.0, 0.85, 0.7) * _Flash * 0.7;   // 护眼：头显里整屏高亮很刺眼，闪白压到原来一半
                return half4(acc, 1);
            }
            ENDHLSL
        }
    }
}
