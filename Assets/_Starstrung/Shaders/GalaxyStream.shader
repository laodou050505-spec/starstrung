// 银河星流：沿路线流动、绕路线慢慢旋转的发光粒子（全部在显卡里算位置）。
// 路线烘焙成贴图 _TrackTex（宽 = 采样数，高 4 行）：
//   第 0 行 = 位置 xyz + 半带宽 w；第 1 行 = 侧向 xyz；第 2 行 = 上向 xyz；第 3 行 r = 这一段的亮度（串经过时变亮），g = 暖色程度。
// 每个粒子是一个四角相同的方片：position.x = 起点米数，position.y = 绕路线的角度，position.z = 离中心的距离（带宽倍数）；
// uv = 角（-1..1）；uv2 = (速度米/秒, 大小米, 颜色种子, 闪烁相位)。
// 颜色：中心青白 → 电光蓝 → 外圈紫，零星洋红斑点和带四芒的白色亮星；柔和的散景圆点。叠加混合。
Shader "Starstrung/GalaxyStream"
{
    Properties
    {
        _TrackTex ("路线贴图", 2D) = "black" {}
        _FromM ("起点（米）", Float) = 0
        _Length ("终点（米）", Float) = 10
        _Swirl ("旋转（弧度/秒）", Float) = 0.25
        _Twist ("旋臂扭转（弧度/米）", Float) = 1.6
        _Intensity ("亮度", Range(0, 8)) = 1.6
        _MinPx ("最小像素", Float) = 1.4
        _ColCore ("中心色", Color) = (0.75, 0.95, 1.0, 1)
        _ColMid ("中层色", Color) = (0.18, 0.45, 1.0, 1)
        _ColOuter ("外圈色", Color) = (0.48, 0.25, 0.95, 1)
        _ColAccent ("点缀色", Color) = (1.0, 0.3, 0.75, 1)
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
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_TrackTex); SAMPLER(sampler_TrackTex);
            float4 _TrackTex_TexelSize;
            float _FromM, _Length, _Swirl, _Twist, _Intensity, _MinPx;
            float _SS_Grow;   // 全局：路线已从入口展开到第几米（进关时从 0 往上长，退出时收回）
            float4 _ColCore, _ColMid, _ColOuter, _ColAccent;

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 p2 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : TEXCOORD1; float2 shape : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };

            float4 row(float x, int r) { return SAMPLE_TEXTURE2D_LOD(_TrackTex, sampler_TrackTex, float2(x, (r + 0.5) * 0.25), 0); }
            float hash1(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float span = max(_Length - _FromM, 0.01);
                float m = _FromM + frac((v.pos.x - _FromM + v.p2.x * _Time.y) / span) * span;
                float x = (m - _FromM) / span * (1.0 - _TrackTex_TexelSize.x) + _TrackTex_TexelSize.x * 0.5;
                float4 r0 = row(x, 0), r1 = row(x, 1), r2 = row(x, 2), r3 = row(x, 3);
                float ang = v.pos.y + _Twist * m + _Swirl * _Time.y * (0.6 + 0.8 * hash1(v.p2.z * 7.1));
                float rad = v.pos.z;
                // 外飘的星屑慢慢呼吸着往外散
                rad *= 1.0 + 0.12 * sin(_Time.y * 0.4 + v.p2.w) * step(1.2, rad);
                float3 c = r0.xyz + (r1.xyz * cos(ang) * 1.25 + r2.xyz * sin(ang) * 0.7) * rad * r0.w;
                // 头尾淡入淡出（入口处从裂口里涌出来，星核处收进去）
                float ends = smoothstep(0.0, 0.03, (m - _FromM) / span) * smoothstep(1.0, 0.985, (m - _FromM) / span);
                ends *= smoothstep(_SS_Grow, _SS_Grow - 0.8, m);

                float3 vc = TransformWorldToView(TransformObjectToWorld(c));
                float seed = v.p2.z;
                float tw = 0.55 + 0.45 * sin(_Time.y * (1.0 + 3.0 * hash1(seed * 3.3)) + v.p2.w);
                float size = v.p2.y;
                // 11-25：离眼睛近的粒子缩小变暗（以前脚边一团团大散景比宝石星还抢眼，宝石星才是要看的东西）
                float nearK = smoothstep(0.7, 2.2, -vc.z);
                size *= lerp(0.45, 1.0, nearK);
                float pxSize = size / max(-vc.z, 0.05) * _ScreenParams.y * 0.5 * abs(UNITY_MATRIX_P._m11);
                float boost = max(1.0, _MinPx / max(pxSize, 1e-4));
                vc.xy += v.uv * size * boost;
                o.pos = mul(UNITY_MATRIX_P, float4(vc, 1));
                o.uv = v.uv;

                // 颜色：按离中心的距离从青白 → 蓝 → 紫；少量洋红、少量白色亮星
                float3 col = lerp(_ColCore.rgb, _ColMid.rgb, smoothstep(0.0, 0.45, rad));
                col = lerp(col, _ColOuter.rgb, smoothstep(0.45, 1.1, rad));
                float kind = hash1(seed * 17.7);
                float isAccent = step(0.94, kind);
                float isStar = step(0.975, hash1(seed * 5.3));
                col = lerp(col, _ColAccent.rgb, isAccent * 0.85);
                col = lerp(col, float3(1, 0.97, 0.92), isStar);
                float lit = r3.r;
                float warm = r3.g;
                col = lerp(col, col * float3(1.25, 0.85, 0.7), warm * 0.6);
                float big = smoothstep(0.008, 0.02, size);                       // 大散景：更柔、更暗
                float bright = (0.35 + 0.65 * tw) * (0.55 + lit * 1.2) * ends * (isStar > 0.5 ? 2.2 : 1.0) * (1.0 - smoothstep(0.9, 2.5, rad) * 0.75) * lerp(1.6, 0.45, big) * lerp(0.3, 1.0, nearK);
                o.col = float4(col * bright / (boost * boost), 1);
                // shape.x = 柔和程度（0 锐利小点 → 1 柔散景），shape.y = 是否带四芒
                o.shape = float2(max(hash1(seed * 9.1), big), isStar);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float r = length(i.uv);
                float soft = i.shape.x;
                // 散景圆点：边缘柔和程度不同（有的像远处虚化的星团、有的锐利）
                float disc = soft > 0.6 ? smoothstep(1.0, 0.55, r) * 0.55 + pow(saturate(1.0 - r), 3.0) * 0.45
                                         : pow(saturate(1.0 - r), 3.5);
                float flare = i.shape.y * (exp(-abs(i.uv.x) * 16.0) * saturate(1.0 - abs(i.uv.y)) + exp(-abs(i.uv.y) * 16.0) * saturate(1.0 - abs(i.uv.x))) * 0.9;
                return half4(i.col.rgb * (disc + flare) * _Intensity, 0);
            }
            ENDHLSL
        }
    }
}
