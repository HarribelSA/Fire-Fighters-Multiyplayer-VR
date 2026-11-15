Shader "CityNightPresentation/Road Light Pool"
{
    Properties { _Color("Light color",Color)=(0.09,0.065,0.025,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" }
        Pass
        {
            Name "RoadPool"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v) { Varyings o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS=TransformObjectToHClip(v.positionOS); o.uv=v.uv; return o; }
            half4 frag(Varyings i):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); float2 p=i.uv*2-1; half a=saturate(1-dot(p,p)); a=a*a; return half4(_Color.rgb*a,0); }
            ENDHLSL
        }
    }
}
