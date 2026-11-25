// Soul cubes: one flat colour for the small parts - eyes, pupils, brows, mouth, cheeks, ears, crown, a knit hat.
// _ToneAmount fades the game's face tones in (0 = flat, for eyes that must always read). Optional knit ribs (round the
// object's Y axis) and horizontal stripes. _Cull Off for the drawn-by-code mouth.
Shader "SoulCubes/SoulFlat"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _ToneAmount ("Face tones", Range(0, 1)) = 1
        _RibFreq ("Knit ribs (around)", Range(0, 60)) = 0
        _RibAmount ("Knit rib depth", Range(0, 0.5)) = 0
        _StripeColor ("Stripe colour", Color) = (1, 1, 1, 1)
        _StripeFreq ("Stripes per unit (y)", Range(0, 40)) = 0
        _StripeAmount ("Stripes", Range(0, 1)) = 0
        _TopShade ("Top tone", Range(0, 1.5)) = 1
        _LitShade ("Lit side tone", Range(0, 1.5)) = 0.88
        _ShadeShade ("Shaded side tone", Range(0, 1.5)) = 0.68
        _BottomShade ("Bottom tone", Range(0, 1.5)) = 0.55
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _StripeColor;
            half _ToneAmount, _RibFreq, _RibAmount, _StripeFreq, _StripeAmount;
            half _TopShade, _LitShade, _ShadeShade, _BottomShade, _Cull;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "SoulFlat"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SoulCommon.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 local : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.local = v.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i, bool facing : SV_IsFrontFace) : SV_Target
            {
                half3 col = _BaseColor.rgb;
                if (_StripeAmount > 0 && _StripeFreq > 0)
                    col = lerp(col, _StripeColor.rgb, SoulStripe(i.local.y * _StripeFreq) * _StripeAmount);
                if (_RibAmount > 0 && _RibFreq > 0)
                {
                    float ang = atan2(i.local.z, i.local.x) / 6.2831853;
                    float rib = 0.5 + 0.5 * sin(ang * _RibFreq * 6.2831853);
                    col *= 1.0 - _RibAmount * rib;
                }
                float3 n = normalize(i.normalWS) * (facing ? 1.0 : -1.0);
                half tone = SoulTone(n, _TopShade, _LitShade, _ShadeShade, _BottomShade);
                col *= lerp(1.0, tone, _ToneAmount);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #define SOUL_SHADOW_PASS
            #include "SoulPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #include "SoulPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDN
            #include "SoulPasses.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
