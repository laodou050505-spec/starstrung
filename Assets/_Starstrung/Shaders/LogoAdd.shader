// 标志 / 图标贴图（白色透明底 PNG）：叠加混合，_Alpha 控制显隐。不改色、不拉伸（方片比例由脚本按贴图宽高设）。
Shader "Starstrung/LogoAdd"
{
    Properties
    {
        _MainTex ("贴图", 2D) = "white" {}
        _Color ("颜色", Color) = (0.85, 0.87, 0.95, 1)
        _Alpha ("显隐", Range(0, 1)) = 1
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Color; float _Alpha;
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos = TransformObjectToHClip(v.pos.xyz); o.uv = v.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                return half4(_Color.rgb * t.rgb * t.a * _Alpha, 0);
            }
            ENDHLSL
        }
    }
}
