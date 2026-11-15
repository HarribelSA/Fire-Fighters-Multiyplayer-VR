Shader "CityNightPresentation/Nearby Navigation Arrow"
{
    Properties { _Color("Arrow color",Color)=(0.12,0.65,0.8,0.65) }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END
            struct A {float3 p:POSITION;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
            V vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.world=TransformObjectToWorld(i.p);o.p=TransformWorldToHClip(o.world);return o;}
            half4 frag(V i):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);float d=distance(i.world.xz,_WorldSpaceCameraPos.xz);half fade=smoothstep(1.5,4,d)*(1-smoothstep(30,35,d));return half4(_Color.rgb,_Color.a*fade);}
            ENDHLSL
        }
    }
}
