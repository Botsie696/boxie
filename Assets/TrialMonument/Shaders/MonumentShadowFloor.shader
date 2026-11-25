// Invisible floor that only shows the shadow the track casts on it, as a soft cool-blue stain,
// like the diffuse shadow under the structure in the reference image. The shadow map is sampled
// at several points in a disk around each pixel to blur it far more than URP's own soft shadows,
// and the whole thing fades out towards the edge of _FadeRadius around _FadeCenter so the plane
// itself never shows.
Shader "TrialMonument/MonumentShadowFloor"
{
    Properties
    {
        _ShadowTint ("Shadow Tint", Color) = (0.62, 0.70, 0.86, 1)
        _Strength ("Strength", Range(0, 1)) = 0.55
        _Blur ("Blur Radius (world)", Range(0, 4)) = 1.2
        _FadeCenter ("Fade Centre (set by script)", Vector) = (0, 0, 0, 0)
        _FadeRadius ("Fade Radius", Float) = 45
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-50" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ShadowFloor"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowTint;
                half _Strength;
                float _Blur;
                float4 _FadeCenter;
                float _FadeRadius;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half ShadowAt(float3 p)
            {
                // Always sample the cascaded map directly (the screen-space shadow texture only
                // knows the depth buffer, which this floor never writes).
                float4 sc = TransformWorldToShadowCoord(p);
                return MainLightRealtimeShadow(sc);
            }

            half4 frag(Varyings i) : SV_Target
            {
                static const float2 disk[8] = {
                    float2(1, 0), float2(0.707, 0.707), float2(0, 1), float2(-0.707, 0.707),
                    float2(-1, 0), float2(-0.707, -0.707), float2(0, -1), float2(0.707, -0.707) };

                half s = ShadowAt(i.positionWS) * 2;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float r = (k & 1) ? _Blur : _Blur * 0.5;
                    s += ShadowAt(i.positionWS + float3(disk[k].x, 0, disk[k].y) * r);
                }
                s /= 10.0;

                float dist = length(i.positionWS.xz - _FadeCenter.xz);
                half fade = 1 - smoothstep(_FadeRadius * 0.55, _FadeRadius, dist);
                half amount = (1 - s) * _Strength * fade;
                return half4(lerp(half3(1, 1, 1), _ShadowTint.rgb, amount), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
