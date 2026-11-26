// 透视夜蓝罩：一个包住玩家的大球（法线朝里），把真实房间罩上一层夜蓝（方案 7.1：#0A1030 约 32%），像笼在星光里。
// 头显里：写出 alpha = _Amount，系统按 alpha 把透视画面压暗并染蓝；之后的加色发光物体只加颜色不改 alpha。
// 编辑器里没有透视画面，看起来就是深蓝底。最先画（Background+1），不写深度。
Shader "Starstrung/NightVeil"
{
    Properties { _Color ("颜色", Color) = (0.039, 0.063, 0.188, 1) _Amount ("浓度", Range(0, 1)) = 0.32 }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Background+1" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _Color; float _Amount;
            struct A { float4 pos : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos = TransformObjectToHClip(v.pos.xyz); o.dir = v.pos.xyz; return o; }
            half4 frag(V i) : SV_Target
            {
                // 头顶略深、地面略浅，不是一块均匀的蓝
                float up = normalize(i.dir).y;
                float a = _Amount * (0.85 + 0.25 * saturate(up));
                return half4(_Color.rgb * a, a);
            }
            ENDHLSL
        }
    }
}
