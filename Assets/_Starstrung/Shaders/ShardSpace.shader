// 封面的玻璃碎片：一片有厚度的斜长玻璃，里面装着一小块有纵深的深空（视差分层：远星海 / 中星云 / 近亮点），
// 边缘一圈细亮光（到多边形四条边的距离算出来）、侧面整面是亮边，表面一道慢慢扫过的反光。
// 碎片形状（物体空间 xy 的四个角）由脚本按物体设：_P01 = (p0, p1)，_P23 = (p2, p3)。
// 正面里的星空盖住一点透视画面（像房间里开了一扇小窗）；背面只画边光。
Shader "Starstrung/ShardSpace"
{
    Properties
    {
        _P01 ("角 0 / 1", Vector) = (0, 0, 0.3, 0.03)
        _P23 ("角 2 / 3", Vector) = (0.28, -0.04, 0, -0.02)
        _Seed ("种子", Float) = 0
        _Alpha ("显隐", Range(0, 1)) = 1
        _ColA ("星云 1", Color) = (0.15, 0.32, 0.95, 1)
        _ColB ("星云 2", Color) = (0.55, 0.25, 0.95, 1)
        _Rim ("边光", Color) = (0.8, 0.92, 1.0, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _P01, _P23, _ColA, _ColB, _Rim; float _Seed, _Alpha;
            struct A { float4 pos : POSITION; float3 n : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float3 os : TEXCOORD0; float3 nos : TEXCOORD1; float3 cam : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = TransformObjectToHClip(v.pos.xyz); o.os = v.pos.xyz; o.nos = v.n;
                o.cam = TransformWorldToObject(GetCameraPositionWS());
                return o;
            }
            float h2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7)) + _Seed * 17.3) * 43758.5453); }
            float n2(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(h2(i), h2(i + float2(1, 0)), f.x), lerp(h2(i + float2(0, 1)), h2(i + float2(1, 1)), f.x), f.y); }
            float fbm(float2 p) { float s = 0, a = 0.5; [unroll] for (int k = 0; k < 4; k++) { s += a * n2(p); p = p * 2.03 + 5.1; a *= 0.5; } return s; }
            float seg(float2 p, float2 a, float2 b) { float2 pa = p - a, ba = b - a; float h = saturate(dot(pa, ba) / dot(ba, ba)); return length(pa - ba * h); }
            float stars(float2 p, float dens, float size)
            {
                float2 c = floor(p), f = frac(p) - 0.5;
                float h = h2(c);
                float d = length(f - (float2(h2(c + 3.1), h2(c + 7.7)) - 0.5) * 0.7);
                return step(1 - dens, h) * smoothstep(size, 0, d) * (0.6 + 0.4 * sin(_Time.y * (1 + 3 * h) + h * 30));
            }
            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                float t = _Time.y;
                float3 n = normalize(i.nos);
                float2 p = i.os.xy;
                float2 p0 = _P01.xy, p1 = _P01.zw, p2 = _P23.xy, p3 = _P23.zw;
                float d = min(min(seg(p, p0, p1), seg(p, p1, p2)), min(seg(p, p2, p3), seg(p, p3, p0)));
                float px = max(fwidth(d), 1e-5);
                float rim = saturate(1.0 - (d - 0.0006) / (px * 1.5)) * 1.3 + exp(-d / 0.006) * 0.35;
                // 侧面：整面亮边
                if (abs(n.z) < 0.5) return half4(_Rim.rgb * 1.2 * _Alpha, 0.0);
                // 背面（碎片翻过去时）只画边光
                bool facing = (n.z < 0) == front;   // 物体 -z 面朝玩家
                if (!facing) return half4(_Rim.rgb * rim * 0.6 * _Alpha, 0.0);
                // 正面：视差深空
                float3 vd = normalize(i.os - i.cam);
                float2 off = vd.xy / max(abs(vd.z), 0.12);
                float2 q3 = p + off * 0.9;     // 远：星海
                float s3 = stars(q3 * 60.0, 0.32, 0.2) + stars(q3 * 130.0 + 3.0, 0.3, 0.24) * 0.6;
                float2 q2 = p + off * 0.35;    // 中：星云
                float neb = fbm(q2 * 9.0 + float2(t * 0.02, 0) + _Seed);
                float neb2 = fbm(q2 * 18.0 - _Seed + neb * 1.6);
                float3 nc = lerp(_ColA.rgb, _ColB.rgb, smoothstep(0.4, 0.75, neb2));
                float nebA = smoothstep(0.35, 0.85, neb2);
                float2 q1 = p + off * 0.08;    // 近：几颗亮点
                float s1 = stars(q1 * 35.0 + 9.0, 0.18, 0.22);
                float3 col = float3(0.008, 0.012, 0.04) + nc * nebA * 0.75 + float3(0.85, 0.92, 1.0) * s3 + float3(1, 0.96, 0.88) * s1 * 1.4;
                // 玻璃表面：一道慢慢扫过的斜反光 + 靠边更亮
                float sweep = exp(-pow(frac((p.x * 2.0 + p.y * 3.0) * 1.2 - t * 0.12 + _Seed) - 0.5, 2.0) * 300.0) * 0.18;
                col += _Rim.rgb * (sweep + exp(-d / 0.02) * 0.08);
                float a = 0.82;
                col = col * a + _Rim.rgb * rim;
                return half4(col * _Alpha, a * _Alpha);
            }
            ENDHLSL
        }
    }
}
