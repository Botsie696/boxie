// Soul cubes: cloth from SoulCloth (cape, scarf tail, shirt tail, floppy hat top). Two-sided: outside colour on the
// front faces, lining on the back. uv.y runs from the pinned edge (0) to the hem / tip (1), uv.x across / around.
// Optional stripes along the length (nightcap), knit ribs across (beanie), and a hem band (a cape's gold edge).
Shader "SoulCubes/SoulCloth"
{
    Properties
    {
        _OuterColor ("Outside", Color) = (0.82, 0.16, 0.2, 1)
        _InnerColor ("Lining", Color) = (1, 0.8, 0.3, 1)
        _StripeColor ("Stripes", Color) = (1, 0.95, 0.85, 1)
        _StripeFreq ("Stripes along the length", Range(0, 20)) = 0
        _StripeAmount ("Stripes", Range(0, 1)) = 0
        _RibFreq ("Knit ribs across", Range(0, 40)) = 0
        _RibAmount ("Knit rib depth", Range(0, 0.5)) = 0
        _HemColor ("Hem", Color) = (1, 0.8, 0.3, 1)
        _HemWidth ("Hem width (uv)", Range(0, 0.4)) = 0
        _TopShade ("Top tone", Range(0, 1.5)) = 1
        _LitShade ("Lit side tone", Range(0, 1.5)) = 0.86
        _ShadeShade ("Shaded side tone", Range(0, 1.5)) = 0.64
        _BottomShade ("Bottom tone", Range(0, 1.5)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _OuterColor, _InnerColor, _StripeColor, _HemColor;
            half _StripeFreq, _StripeAmount, _RibFreq, _RibAmount, _HemWidth;
            half _TopShade, _LitShade, _ShadeShade, _BottomShade;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "SoulCloth"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SoulCommon.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i, bool facing : SV_IsFrontFace) : SV_Target
            {
                half3 col = facing ? _OuterColor.rgb : _InnerColor.rgb;
                if (_StripeAmount > 0 && _StripeFreq > 0)
                    col = lerp(col, _StripeColor.rgb, SoulStripe(i.uv.y * _StripeFreq) * _StripeAmount);
                if (_RibAmount > 0 && _RibFreq > 0)
                    col *= 1.0 - _RibAmount * (0.5 + 0.5 * sin(i.uv.x * _RibFreq * 6.2831853));
                if (_HemWidth > 0)
                    col = lerp(col, _HemColor.rgb, SoulFill((1.0 - _HemWidth) - i.uv.y));
                // a touch darker toward the pinned edge, like a fold
                col *= lerp(0.88, 1.0, smoothstep(0.0, 0.25, i.uv.y));
                float3 n = normalize(i.normalWS) * (facing ? 1.0 : -1.0);
                col *= SoulTone(n, _TopShade, _LitShade, _ShadeShade, _BottomShade);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off
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
            Cull Off
            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDN
            #include "SoulPasses.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
