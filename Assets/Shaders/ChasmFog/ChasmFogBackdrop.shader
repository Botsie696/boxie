// The chasm's background: ChasmFog's gradient drawn over the whole screen before any geometry, so every fogged rock ends in the
// exact colour behind it. The mesh is a full-screen triangle given straight in clip space; ChasmFog.cs draws it.
Shader "Hidden/ChasmFogBackdrop"
{
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "None" }

        Pass
        {
            Name "ChasmFogBackdrop"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ChasmFog.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // Half way between the near and far clip values, whichever way round the platform's depth runs.
                output.positionCS = float4(input.positionOS.xy, (UNITY_NEAR_CLIP_VALUE + UNITY_RAW_FAR_CLIP_VALUE) * 0.5, 1.0);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(ChasmFogColor(GetNormalizedScreenSpaceUV(input.positionCS)), 1.0);
            }
            ENDHLSL
        }
    }
}
