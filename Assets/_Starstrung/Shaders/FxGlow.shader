// 发光小件合集（叠加混合）：
// - _Mode 0 四芒星闪光（面朝相机的方片，中心光点 + 四道芒 + 光晕）；
// - _Mode 1 光带（环形带网格：uv.x 沿环 0..1，像彗尾一样一头亮一头淡，沿环流动）；
// - _Mode 2 水晶（有棱有面：按面法线算边缘光，青紫色）；
// - _Mode 4 横向光芒（方片按物体缩放拉长：中间一条细亮线，往两头渐淡，像镜头的变形光斑）；
// - _Mode 3 光幕（圆柱面：uv.y 从下到上；底部亮、往上渐隐，细竖纹里有光团往上流，侧边柔和不出硬边）。
Shader "Starstrung/FxGlow"
{
    Properties
    {
        _Color ("颜色", Color) = (0.7, 0.85, 1, 1)
        _Color2 ("副色", Color) = (0.6, 0.35, 1, 1)
        _Mode ("模式", Float) = 0
        _Intensity ("亮度", Range(0, 8)) = 1.5
        _Speed ("流速", Float) = 0.25
        _Billboard ("面朝相机", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("深度测试", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend One One
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _Color, _Color2; float _Mode, _Intensity, _Speed, _Billboard;

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float3 n : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 nWS : TEXCOORD1; float3 vWS : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };

            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                if (_Mode < 0.5 && _Billboard > 0.5)
                {
                    // 四芒星：物体原点处展开一个面朝相机的方片（方片大小 = 物体缩放）
                    float3 c = TransformObjectToWorld(float3(0, 0, 0));
                    float s = length(TransformObjectToWorldDir(float3(1, 0, 0), false));
                    float3 vc = TransformWorldToView(c);
                    vc.xy += (v.uv * 2 - 1) * s;
                    o.pos = mul(UNITY_MATRIX_P, float4(vc, 1));
                }
                else o.pos = TransformObjectToHClip(v.pos.xyz);
                o.uv = v.uv;
                float3 ws = TransformObjectToWorld(v.pos.xyz);
                o.nWS = TransformObjectToWorldNormal(v.n);
                o.vWS = GetCameraPositionWS() - ws;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float t = _Time.y;
                if (_Mode < 0.5)
                {
                    float2 p = i.uv * 2 - 1;
                    float r = length(p);
                    float core = exp(-r * 9.0) * 1.5 + exp(-r * 4.0) * 0.1;
                    float flare = exp(-abs(p.x) * 40.0) * pow(saturate(1 - abs(p.y)), 2.0) + exp(-abs(p.y) * 40.0) * pow(saturate(1 - abs(p.x)), 2.0);
                    float diag = (exp(-abs(p.x - p.y) * 60.0) + exp(-abs(p.x + p.y) * 60.0)) * pow(saturate(1 - r), 3.0) * 0.25;
                    float edge = smoothstep(1.0, 0.75, max(abs(p.x), abs(p.y))) * smoothstep(1.0, 0.85, r);   // 方片边缘之前完全淡出
                    return half4(lerp(_Color2.rgb, _Color.rgb, saturate(1 - r * 2)) * (core + flare + diag) * edge * _Intensity, 0);
                }
                if (_Mode < 1.5)
                {
                    float u = frac(i.uv.x - t * _Speed);
                    float head = pow(u, 6.0) * 1.6 + pow(u, 1.5) * 0.35;            // 彗尾：一头亮一头淡
                    float yy = i.uv.y * 2 - 1;
                    float across = exp(-yy * yy * 6.0);
                    float3 c = lerp(_Color2.rgb, _Color.rgb, pow(u, 3.0));
                    float face = smoothstep(0.12, 0.5, abs(dot(normalize(i.nWS), normalize(i.vWS))));   // 侧对相机时淡出，不成一条刮痕
                    return half4(c * head * across * face * _Intensity, 0);
                }
                if (_Mode > 3.5)
                {
                    float2 p4 = i.uv * 2 - 1;
                    float core4 = exp(-abs(p4.y) * 9.0) * pow(saturate(1 - abs(p4.x)), 2.2);
                    float hot = exp(-abs(p4.y) * 30.0) * pow(saturate(1 - abs(p4.x)), 6.0) * 1.5;
                    float edge4 = smoothstep(1.0, 0.8, abs(p4.y));
                    return half4(lerp(_Color2.rgb, _Color.rgb, saturate(hot)) * (core4 + hot) * edge4 * _Intensity, 0);
                }
                if (_Mode > 2.5)
                {
                    float y = i.uv.y;
                    float rise = pow(1.0 - y, 2.6) * smoothstep(0.0, 0.1, y);              // 底亮顶淡，底边也有一点过渡
                    float cols = i.uv.x * 56.0;
                    float h = frac(sin(floor(cols) * 12.9898) * 43758.5453);
                    float px = max(fwidth(cols), 1e-4);
                    float streak = saturate(0.5 - (abs(frac(cols) - 0.5) - 0.06) / px) * (0.25 + 0.75 * h);   // 抗锯齿细竖纹
                    float pk = pow(frac(y * 1.3 - t * _Speed * (0.6 + h) + h * 7.0), 5.0);  // 往上流的光团
                    float3 n3 = normalize(i.nWS), v3 = normalize(i.vWS);
                    float side = lerp(0.08, 1.0, smoothstep(0.0, 0.6, abs(dot(n3, v3))));  // 侧边柔和
                    float3 c3 = lerp(_Color2.rgb, _Color.rgb, saturate(y * 1.5 + pk * 0.5));
                    return half4(c3 * (rise * 0.35 + streak * (0.15 + pk * 1.6) * rise) * side * _Intensity, 0);
                }
                float3 n = normalize(i.nWS), vv = normalize(i.vWS);
                float fr = pow(1.0 - abs(dot(n, vv)), 2.5);
                float3 c = lerp(_Color2.rgb * 0.25, _Color.rgb, fr);
                return half4(c * (0.25 + fr * 1.4) * _Intensity, 0);
            }
            ENDHLSL
        }
    }
}
