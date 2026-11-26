// 结局：星核碎成满天星。几千颗面朝相机的星，从星核（起点在核附近随机）沿螺旋飞到房间天空的位置，然后错峰闪烁。
// 顶点：uv = 方片角；uv1 = (大小, 相位, 周期, 种类 0 点 / 1 四芒)；uv2 = 终点（世界坐标）+ 延迟；color = 颜色；顶点位置 = 起点。
Shader "Starstrung/EndingStars"
{
    Properties { _K ("炸开进度", Range(0, 1)) = 0 _Intensity ("亮度", Range(0, 4)) = 1.3 _Center ("星核", Vector) = (0, 1.22, 0.35, 0) }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" }
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
            float _K, _Intensity; float4 _Center;
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 d : TEXCOORD1; float4 to : TEXCOORD2; float4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; float kind : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float t = _Time.y;
                float e = saturate((_K - v.to.w) / (1.0 - v.to.w * 0.9));
                float ee = 1.0 - pow(1.0 - e, 3.0);
                // 螺旋：绕星核竖轴转着飞出去，越往后转得越少
                float3 from = v.pos.xyz, to = v.to.xyz;
                float3 p = lerp(from, to, ee);
                float sw = (1.0 - ee) * 2.4 * (0.6 + 0.4 * frac(v.d.y * 3.1));
                float3 rel = p - _Center.xyz; float c = cos(sw), s = sin(sw);
                p = _Center.xyz + float3(rel.x * c - rel.z * s, rel.y, rel.x * s + rel.z * c);
                p.y += sin(e * 3.14159) * 0.25 * frac(v.d.y * 7.7);
                // 错峰闪烁（飞行中拖着亮，停下后各闪各的）
                float tw = 0.5 + 0.5 * sin(t * 6.2831 / v.d.z + v.d.y);
                tw = v.d.w > 0.5 ? 0.3 + 1.3 * pow(tw, 3.0) : 0.45 + 0.6 * tw;
                tw = lerp(0.85, tw, smoothstep(0.7, 1.0, e));   // 11-25 用户：炸开别太闪 → 飞行中不再拖着高亮（原来 1.4）
                float3 vc = TransformWorldToView(TransformObjectToWorld(p));   // 网格在搭建时的世界坐标 = 物体坐标；天空转动由物体变换带着走
                float size = v.d.x * (v.d.w > 0.5 ? 0.6 + 0.6 * tw : 1.0) * lerp(0.12, 1.0, ee);
                // 护眼：视角大小封顶约 0.7°（星从手里飞出时离眼睛只有半米，以前十字星会糊满视野）；离眼 1 米内的星再压暗
                float dz = max(-vc.z, 0.05);
                size = min(size, dz * 0.012);
                tw *= lerp(0.35, 1.0, smoothstep(0.3, 1.0, dz));   // 手边的星淡淡看得见（有“从手里散开”的感觉），但不刺眼
                vc.xy += (v.uv * 2 - 1) * size;
                o.pos = mul(UNITY_MATRIX_P, float4(vc, 1));
                o.uv = v.uv * 2 - 1;
                o.col = float4(v.col.rgb * tw, step(0.001, _K - v.to.w));
                o.kind = v.d.w;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float2 p = i.uv; float r = length(p);
                float dotv = exp(-r * 7.0);
                float star = exp(-r * 12.0) * 1.4 + (exp(-abs(p.x) * 45.0) * saturate(1 - abs(p.y)) + exp(-abs(p.y) * 45.0) * saturate(1 - abs(p.x))) * 0.9;
                float v = (i.kind > 0.5 ? star : dotv) * smoothstep(1.0, 0.8, max(abs(p.x), abs(p.y)));
                return half4(i.col.rgb * v * _Intensity * i.col.a, 0);
            }
            ENDHLSL
        }
    }
}
