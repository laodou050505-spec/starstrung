// 选关的粒子银河：上万颗面朝相机的小光点，组成四条对数螺旋臂 + 中心核球 + 散在盘面的星尘。
// - 旋臂上的星沿臂往外流（r 随时间增长，到边缘淡出、回到中心淡入），臂的形状不动（关卡星钉在臂上）；
// - _Burst 0→1：所有光点从 _From（星核位置，盘的局部坐标）旋转着炸开，铺到各自的位置；1→0 反过来收回去；
// - 颜色：中心白青 → 电光蓝 → 紫 → 外缘洋红，少数大一点的白星。
// 顶点：uv = 方片角；uv1 = (r0, 臂角, 横向偏移, 种子)；uv2 = (大小, 色偏, 流速, 种类 0 臂 / 1 核球 / 2 星尘 / 3 柔光星云)。
Shader "Starstrung/GalaxyDisk"
{
    Properties
    {
        _Radius ("半径（米）", Float) = 0.85
        _Wind ("缠绕", Float) = 1.8
        _Burst ("炸开进度", Range(0, 1)) = 1
        _From ("炸开起点（局部）", Vector) = (0, 0, 0, 0)
        _Alpha ("显隐", Range(0, 1)) = 1
        _Intensity ("亮度", Range(0, 4)) = 1.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" }
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
            float _Radius, _Wind, _Burst, _Alpha, _Intensity, _HoleN; float4 _From; float4 _Holes[24];
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 a : TEXCOORD1; float4 b : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; UNITY_VERTEX_OUTPUT_STEREO };

            float3 Pal(float r, float m)
            {
                float3 c0 = float3(0.85, 0.95, 1.0), c1 = float3(0.25, 0.55, 1.0), c2 = float3(0.55, 0.32, 1.0), c3 = float3(0.95, 0.38, 0.85);
                float3 c = r < 0.35 ? lerp(c0, c1, r / 0.35) : r < 0.7 ? lerp(c1, c2, (r - 0.35) / 0.35) : lerp(c2, c3, (r - 0.7) / 0.3);
                return lerp(c, m > 0.85 ? float3(1, 1, 1) : c3, m > 0.85 ? 0.7 : saturate((m - 0.6) * 1.5) * 0.5);
            }

            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float t = _Time.y;
                float r0 = v.a.x, arm = v.a.y, perp = v.a.z, seed = v.a.w;
                float size = v.b.x, cm = v.b.y, speed = v.b.z, kind = v.b.w;
                bool flow = kind < 0.5 || kind > 2.5;
                float r = flow ? frac(r0 + t * speed) : r0;
                float fade = flow ? smoothstep(0.0, 0.08, r) * smoothstep(1.0, 0.82, r) : 1.0;
                float rr = max(r, 0.03);
                float th = arm + log(rr / 0.03) * _Wind * (kind > 1.5 && kind < 2.5 ? 0.0 : 1.0);   // 星尘不缠绕，其余都沿螺旋
                th += kind > 0.5 && kind < 1.5 ? t * (0.25 / (0.2 + r)) * 0.3 : 0.0;   // 核球慢慢转
                float spread = kind < 0.5 || kind > 2.5 ? (0.035 + 0.15 * r) : 0.0;
                float3 p = float3(cos(th) * rr, 0, sin(th) * rr) * _Radius;
                float3 tang = float3(-sin(th), 0, cos(th));
                float3 radial = float3(cos(th), 0, sin(th));
                p += (radial * 0.9 + tang * 0.25) * perp * spread * _Radius;   // 往旋臂两侧散开（螺旋臂的横向近似是径向）
                p.y += (frac(seed * 91.7) - 0.5) * (kind > 0.5 && kind < 1.5 ? 0.06 : 0.03) * _Radius * (1.2 - r);
                // 炸开：从起点旋转着飞到位置（每颗有不同的延迟）
                float e = saturate((_Burst - seed * 0.35) / 0.65);
                e = 1 - pow(1 - e, 3);
                float swirl = (1 - e) * 2.6;
                float cs = cos(swirl), sn = sin(swirl);
                float3 pr = float3(p.x * cs - p.z * sn, p.y, p.x * sn + p.z * cs);
                float3 pos = lerp(_From.xyz, pr, e);
                // 关卡星周围让开一小圈：星尘在星旁边变淡，星和字不被淹没
                float hole = 1.0;
                for (int k = 0; k < 24; k++) { if (k >= (int)_HoleN) break; float dd = length(pr - _Holes[k].xyz); hole = min(hole, smoothstep(0.035, 0.09, dd)); }
                fade *= lerp(1.0, hole, e);
                float3 wc = TransformObjectToWorld(pos);
                float3 vc = TransformWorldToView(wc);
                float tw = 0.75 + 0.25 * sin(t * (1.0 + seed * 3.0) + seed * 40.0);
                vc.xy += (v.uv * 2 - 1) * size * (0.5 + 0.5 * e);
                o.pos = mul(UNITY_MATRIX_P, float4(vc, 1));
                o.uv = v.uv * 2 - 1;
                float bright = kind > 2.5 ? 0.05 : kind > 0.5 && kind < 1.5 ? 0.6 : kind > 1.5 ? 0.4 : 1.0;
                o.col = float4(Pal(saturate(r), cm) * bright * tw, fade * smoothstep(0.0, 0.15, e) * _Alpha);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float r = length(i.uv);
                float a = exp(-r * r * 6.0) * smoothstep(1.0, 0.7, r);
                return half4(i.col.rgb * a * i.col.a * _Intensity, 0);
            }
            ENDHLSL
        }
    }
}
