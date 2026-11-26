// 粒子 / 连线用的加色着色器：贴图 × 顶点颜色，叠加混合（粒子系统、LineRenderer 都用它）。
Shader "Starstrung/ParticleAdd"
{
    Properties { _MainTex ("贴图", 2D) = "white" {} _Intensity ("亮度", Range(0, 8)) = 1.5 }
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
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex); float _Intensity;
            struct A { float4 pos : POSITION; float4 col : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float4 col : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos = TransformObjectToHClip(v.pos.xyz); o.col = v.col; o.uv = v.uv; return o; }
            half4 frag(V i) : SV_Target { float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv); return half4(t.rgb * i.col.rgb * i.col.a * t.a * _Intensity, 0); }
            ENDHLSL
        }
    }
}
