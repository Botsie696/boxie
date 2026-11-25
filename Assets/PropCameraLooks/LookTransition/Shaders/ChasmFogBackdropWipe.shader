// LookTransition copy of Hidden/ChasmFogBackdrop (Assets/Shaders/ChasmFog, left untouched): the same full-screen gradient,
// read through LookChasmFog.hlsl so the part of the screen the look wipe has passed shows the next look's backdrop.
// With the wipe idle it draws exactly what the original does. MainScene prop camera 3.2's ChasmFog points at it.
Shader "Hidden/LookTransition/ChasmFogBackdropWipe"
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
            #include "LookChasmFog.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
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
