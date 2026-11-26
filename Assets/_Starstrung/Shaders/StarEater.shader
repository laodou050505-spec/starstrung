// 吞星兽头（Tripo P2 模型 + 4K 贴图）。贴图里已经画了明暗，这里只加一层轻光照和材质感：
// - 漫反射：环境 + 主光（半兰伯特，柔）；
// - 黄铜（贴图里金黄、饱和的地方）：锐利高光 + 一点反射的暖色；
// - 漆面甲片（深紫）：较宽的高光 + 冷紫色轮廓光（像星光擦过），带一点虹彩偏色；
// - 水晶（淡紫、亮、低饱和）：自发光 + 轮廓更亮（半透明感）；
// - 嘴里的星云（洋红、高饱和）：_Mouth（0 暗 → 1 亮）控制自发光，张嘴时向 _MouthColor（它要的颜色）偏；星云慢慢旋转闪烁；
// - _Flash 受击闪白；_Alpha 用来出现 / 消失（溶解：按噪声逐片消散，带亮边）。
Shader "Starstrung/StarEater"
{
    Properties
    {
        _MainTex ("贴图", 2D) = "white" {}
        _Mouth ("嘴里亮度", Range(0, 1)) = 0.2
        _MouthColor ("嘴里颜色", Color) = (0.9, 0.35, 0.85, 1)
        _Flash ("受击闪白", Range(0, 1)) = 0
        _Alpha ("显隐（溶解）", Range(0, 1)) = 1
        _Rim ("轮廓光", Color) = (0.55, 0.45, 1.0, 1)
        _Debug ("调试：分区染色", Range(0, 1)) = 0
        // 眼睛（11-25 用户：注意眼睛）：物体空间里射线量出来的眼睛中心 / 朝向（另一只按 224° 对称面镜像），半宽 / 半高
        _EyeA ("左眼中心", Vector) = (-0.0667, 0.0543, -0.3568, 0)
        _EyeAN ("左眼朝向", Vector) = (0.76, 0.10, -0.65, 0)
        _EyeB ("右眼中心", Vector) = (-0.3543, 0.0543, -0.0791, 0)
        _EyeBN ("右眼朝向", Vector) = (-0.673, 0.099, 0.733, 0)
        _EyeR ("眼睛半宽 / 半高", Vector) = (0.036, 0.029, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _Mouth, _Flash, _Alpha, _Debug; float4 _MouthColor, _Rim, _EyeA, _EyeAN, _EyeB, _EyeBN, _EyeR;
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 nWS : TEXCOORD1; float3 wp : TEXCOORD2; float3 os : TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wp = TransformObjectToWorld(v.pos.xyz); o.pos = TransformWorldToHClip(o.wp);
                o.nWS = TransformObjectToWorldNormal(v.n); o.uv = v.uv; o.os = v.pos.xyz;
                return o;
            }
            float h3(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
            float n3(float3 p) { float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(h3(i), h3(i + float3(1,0,0)), f.x), lerp(h3(i + float3(0,1,0)), h3(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(h3(i + float3(0,0,1)), h3(i + float3(1,0,1)), f.x), lerp(h3(i + float3(0,1,1)), h3(i + float3(1,1,1)), f.x), f.y), f.z); }
            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y); float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }
            // 一只眼：投到眼睛的切平面上（x 沿眼宽、y 朝上），返回 (遮罩, 颜色)
            float4 Eye(float3 os, float3 c, float3 nrm, float t, float seed)
            {
                float3 n = normalize(nrm);
                float3 rt = normalize(cross(float3(0, 1, 0), n)), up = cross(n, rt);
                float3 off = os - c;
                if (abs(dot(off, n)) > _EyeR.x * 1.2) return 0;            // 只管眼球表面附近
                float2 e = float2(dot(off, rt), dot(off, up)) / _EyeR.xy;
                float r = length(e);
                float m = 1.0 - smoothstep(0.86, 1.0, r);
                if (m <= 0) return 0;
                float ang = atan2(e.y, e.x);
                // 虹膜：琥珀金 → 中心亮黄，放射状纤维纹 + 慢慢流动的星屑
                float fib = n3(float3(ang * 6.0, r * 4.0, seed)) * 0.6 + n3(float3(ang * 17.0, r * 9.0, seed + 3.0)) * 0.4;
                float3 iris = lerp(float3(1.0, 0.42, 0.06), float3(1.0, 0.86, 0.35), smoothstep(0.85, 0.1, r)) * (0.75 + 0.5 * fib);
                iris *= 1.35 + 0.2 * sin(t * 1.3 + seed);                   // 微微呼吸、略带辉光（眼睛是全头最有神的地方）
                iris = lerp(iris, float3(0.25, 0.08, 0.02), smoothstep(0.72, 0.95, r));   // 外圈收暗（角膜缘）
                // 竖瞳：两头尖的竖缝，边上一圈细细的洋红光
                float slitW = 0.2 * sqrt(saturate(1.0 - e.y * e.y));
                float slit = 1.0 - smoothstep(slitW - 0.05, slitW, abs(e.x));
                float slitRim = smoothstep(slitW + 0.09, slitW, abs(e.x)) * (1 - slit);
                float3 col = lerp(iris, float3(0.015, 0.005, 0.03), slit) + float3(1.0, 0.35, 0.75) * slitRim * 0.5;
                // 高光：左上一个小亮点 + 右下一个更小的（像宝石的反光，让眼睛“活”）
                float2 h1p = e - float2(-0.38, 0.4), h2p = e - float2(0.35, -0.42);
                col += float3(1, 0.98, 0.95) * (exp(-dot(h1p, h1p) * 90.0) * 2.2 + exp(-dot(h2p, h2p) * 260.0) * 0.9);
                return float4(col, m);
            }
            half4 frag(V i) : SV_Target
            {
                float t = _Time.y;
                // 溶解（出现 / 消失）
                float dn = n3(i.os * 9.0) * 0.7 + n3(i.os * 23.0) * 0.3;
                clip(_Alpha - dn * 0.98 - 0.01);
                float edge = smoothstep(0.08, 0.0, _Alpha - dn * 0.98);

                float3 alb = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                // 11-25 修：GPU 采到的是线性值，原阈值是按贴图原色（sRGB）调的 → 黄铜只认出 4.5%（实际约 16%）、水晶 0.3%（实际约 10%）。
                // 先转回 sRGB 再判定；阈值按贴图统计：金色 h 0.035～0.096、s 0.34～0.54；淡紫水晶 h 0.61～0.82、s < 0.5、v > 0.55
                float3 albS = LinearToSRGB(alb);
                float3 hsv = rgb2hsv(albS);
                float brass = smoothstep(0.015, 0.035, hsv.x) * smoothstep(0.13, 0.1, hsv.x) * smoothstep(0.22, 0.34, hsv.y) * smoothstep(0.28, 0.4, hsv.z);
                float mouth = (smoothstep(0.85, 0.88, hsv.x) + step(hsv.x, 0.012)) * smoothstep(0.45, 0.6, hsv.y) * smoothstep(0.3, 0.45, hsv.z);
                float crystal = smoothstep(0.58, 0.63, hsv.x) * smoothstep(0.86, 0.82, hsv.x) * smoothstep(0.55, 0.4, hsv.y) * smoothstep(0.5, 0.62, hsv.z);
                float lacquer = saturate(1 - brass - mouth - crystal);
                if (_Debug > 0.5) return half4(brass, crystal, mouth, 1) + half4(0.15, 0.15, 0.15, 0) * lacquer;

                float3 n = normalize(i.nWS);
                float3 vd = normalize(GetWorldSpaceViewDir(i.wp));
                Light L = GetMainLight();
                float ndl = dot(n, L.direction) * 0.5 + 0.5;
                float3 h = normalize(L.direction + vd);
                float ndh = saturate(dot(n, h));
                float fr = pow(1.0 - saturate(dot(n, vd)), 3.0);
                float3 amb = SampleSH(n);

                // 11-25 用户：贴图和游戏不搭（手绘灰紫漆木感）→ 改成游戏同一套材质语言：夜空色漆壳里透出星点和星云、
                // 香槟金黄铜（和星核星环一个颜色）、宝石水晶、青→紫轮廓光（和时空裂缝边光同色）。贴图只取明暗细节，不取它的灰紫底色。
                float lum = dot(albS, float3(0.299, 0.587, 0.114));
                float detail = saturate(lum * 1.7);                       // 贴图里的刻纹 / 甲片缝 → 明暗细节
                float diff = 0.55 + 0.45 * ndl;
                float3 vdO = TransformWorldToObjectDir(-vd);              // 物体空间里的视线（往壳里看）

                // 漆面：深靛蓝 → 蓝紫（按细节和朝向），里面两层视差星点 + 淡星云
                float3 night = lerp(float3(0.012, 0.016, 0.06), float3(0.07, 0.09, 0.34), detail * detail) * diff;   // 深靛蓝 → 夜空蓝紫（不发粉）
                float3 q1 = (i.os + vdO * 0.04) * 70.0, q2 = (i.os + vdO * 0.12) * 32.0;
                float s1 = step(0.9, h3(floor(q1))) * pow(saturate(1.0 - length(frac(q1) - 0.5) * 2.6), 5.0) * (0.5 + 0.5 * sin(t * (1.5 + 3.0 * h3(floor(q1) + 2.0)) + h3(floor(q1)) * 30.0));
                float s2 = step(0.95, h3(floor(q2) + 7.0)) * pow(saturate(1.0 - length(frac(q2) - 0.5) * 2.4), 4.0) * (0.6 + 0.4 * sin(t * 0.9 + h3(floor(q2)) * 20.0));
                float neb = n3((i.os + vdO * 0.08) * 6.0 + float3(0, t * 0.03, 0));
                float3 nebC = lerp(float3(0.1, 0.45, 1.0), float3(0.85, 0.3, 0.9), smoothstep(0.45, 0.75, n3(i.os * 3.0 + 5.0)));
                float3 lac = night + nebC * smoothstep(0.5, 0.85, neb) * 0.16 + float3(0.85, 0.92, 1.0) * (s1 * 1.6 + s2 * 0.9) * (0.4 + 0.6 * (1 - detail));
                lac += pow(ndh, 40.0) * 0.5 * L.color + pow(ndh, 10.0) * 0.06;
                float3 irid = 0.5 + 0.5 * cos(6.2831 * (fr * 1.1 + float3(0.0, 0.33, 0.67)));
                float rimK = pow(1.0 - saturate(dot(n, vd)), 5.0);   // 只在真正的外轮廓（fr 是 3 次方，斜面多的龙头会整片发白）
                lac += rimK * lerp(float3(0.55, 0.85, 1.0), float3(0.7, 0.45, 1.0), saturate(i.os.y * 3.0 + 0.5)) * 0.7 + rimK * irid * 0.1;

                // 黄铜：香槟金（亮度跟贴图细节走），锐利高光 + 暖色掠光
                float3 gold = float3(1.0, 0.6, 0.16) * (0.35 + 0.9 * detail) * diff;   // 细金边会和两边的甲片混色，所以金色本身要更饱和   // 和星核星环同一种暖香槟金
                gold += (pow(ndh, 70.0) * 1.2 + pow(ndh, 9.0) * 0.15) * float3(1.0, 0.72, 0.35) * L.color + rimK * float3(1.0, 0.7, 0.3) * 0.35;

                // 水晶：紫水晶 → 洋红的宝石，内发光 + 虹彩边，慢慢明暗
                float3 gem = lerp(float3(0.28, 0.14, 0.8), float3(0.75, 0.3, 0.95), saturate(detail * 1.2 - 0.3));   // 深紫水晶 → 洋红（不发粉）
                float3 crys = gem * (0.35 + 0.6 * fr) * (0.85 + 0.15 * sin(t * 2.0 + i.os.y * 20.0)) + irid * fr * 0.25 + pow(ndh, 90.0) * 1.2;

                float3 col = lac * lacquer + gold * brass + crys * crystal;
                // 眼睛：盖在原来平涂的金色虹膜上
                float4 eA = Eye(i.os, _EyeA.xyz, _EyeAN.xyz, t, 1.3), eB = Eye(i.os, _EyeB.xyz, _EyeBN.xyz, t, 4.7);
                col = lerp(col, eA.rgb, eA.a); col = lerp(col, eB.rgb, eB.a);
                // 嘴里的星云
                float swirl = 0.75 + 0.25 * sin(t * 1.5 + atan2(i.os.z + 0.22, i.os.x + 0.2) * 3.0 + length(i.os.xz) * 20.0);
                float3 mc = lerp(lerp(float3(0.5, 0.15, 0.55), float3(0.95, 0.35, 0.85), detail), _MouthColor.rgb * (0.6 + alb), saturate(_Mouth));   // 嘴里平时是暗洋红星云
                col = lerp(col, mc * (0.25 + 2.4 * _Mouth * swirl), mouth);
                // 受击闪白 + 溶解亮边
                col += float3(1, 0.95, 0.9) * _Flash * (0.6 + 0.4 * fr);
                col += float3(1.0, 0.7, 1.0) * edge * 2.5;
                return half4(col, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex v2
            #pragma fragment f2
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 pos : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V v2(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos = TransformObjectToHClip(v.pos.xyz); return o; }
            half4 f2(V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
