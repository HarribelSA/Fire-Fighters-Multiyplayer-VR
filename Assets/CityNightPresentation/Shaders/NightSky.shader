Shader "CityNightPresentation/Night Sky"
{
    Properties
    {
        _Zenith("Zenith", Color)=(0.008,0.018,0.045,1)
        _Horizon("Horizon", Color)=(0.035,0.060,0.10,1)
        _Ground("Ground", Color)=(0.012,0.02,0.035,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Zenith, _Horizon, _Ground;
            CBUFFER_END
            struct A { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 ray:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.positionCS=TransformObjectToHClip(i.vertex.xyz);o.ray=i.vertex.xyz;return o;}
            half4 frag(V i):SV_Target {UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);float y=normalize(i.ray).y;half3 col=y>=0?lerp(_Horizon.rgb,_Zenith.rgb,saturate(y*2)):lerp(_Horizon.rgb,_Ground.rgb,saturate(-y*5));return half4(col,1);}
            ENDHLSL
        }
    }
}
