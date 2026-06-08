Shader "Hidden/SimpleTrueDepthPrototype"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float depthVS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                Varyings output;
                output.positionCS = positionInputs.positionCS;
                output.depthVS = -positionInputs.positionVS.z;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return float4(input.depthVS, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }
}
