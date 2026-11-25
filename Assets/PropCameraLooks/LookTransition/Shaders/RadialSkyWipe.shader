// LookTransition copy of PropLooks/RadialSky (left untouched). The same sky, but its settings come from two global slots -
// _LTSkyA (the look in force) and _LTSkyB (the next look) - picked per pixel by the look wipe (LookWipe.hlsl), so the sky
// changes along the same edge as everything else. LookTransitionDirector fills the slots with the values of the scene's
// own sky material (see AtmosphereLook.PackSky); the material's colour properties are only kept so other scripts can read them.
Shader "PropLooks/LookTransition/RadialSkyWipe"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.10, 0.14, 0.16, 1)
        _BottomColor ("Bottom", Color) = (0.08, 0.11, 0.13, 1)
        _MidColor ("Radial Centre", Color) = (0.25, 0.32, 0.35, 1)
        _GlowCenter ("Radial Centre (uv)", Vector) = (0.5, 0.55, 0, 0)
        _GlowRadius ("Radial Radius (screen heights)", Range(0.05, 2)) = 0.7
        _GlowStrength ("Radial Strength", Range(0, 1)) = 1
        _GlowFalloff ("Radial Falloff Power", Range(0.3, 4)) = 1.4
        _GlowColor ("Low Warm Haze", Color) = (0, 0, 0, 1)
        _HazeHeight ("Low Haze Height (0..1)", Range(0, 1)) = 0
        _HazeStrength ("Low Haze Strength", Range(0, 1)) = 0
        _VignetteColor ("Vignette Colour", Color) = (0, 0, 0, 1)
        _Vignette ("Vignette", Range(0, 1)) = 0.3
        _Rings ("Concentric Rings", Range(0, 0.05)) = 0
        _RingCount ("Ring Count", Range(2, 40)) = 9
        _Grain ("Grain", Range(0, 0.08)) = 0.015
        [HideInInspector] _StarColor ("unused", Color) = (1, 1, 1, 1)
        _Aspect ("Aspect (set by script)", Float) = 0.5625
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+500" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Sky"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "LookWipe.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor, _BottomColor, _MidColor, _GlowColor, _VignetteColor, _StarColor;
                float4 _GlowCenter;
                half _GlowRadius, _GlowStrength, _GlowFalloff, _HazeHeight, _HazeStrength;
                half _Vignette, _Rings, _RingCount, _Grain;
                float _Aspect;
            CBUFFER_END

            // 0 top, 1 bottom, 2 mid, 3 haze, 4 vignette colour (linear rgb)
            // 5 (glow centre x, y, radius, strength)  6 (falloff, haze height, haze strength, vignette)  7 (rings, ring count, grain, -)
            float4 _LTSkyA[8];
            float4 _LTSkyB[8];

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                half w = LTWipeAt(GetNormalizedScreenSpaceUV(i.positionCS));
                float4 S0 = lerp(_LTSkyA[0], _LTSkyB[0], w);
                float4 S1 = lerp(_LTSkyA[1], _LTSkyB[1], w);
                float4 S2 = lerp(_LTSkyA[2], _LTSkyB[2], w);
                float4 S3 = lerp(_LTSkyA[3], _LTSkyB[3], w);
                float4 S4 = lerp(_LTSkyA[4], _LTSkyB[4], w);
                float4 S5 = lerp(_LTSkyA[5], _LTSkyB[5], w);
                float4 S6 = lerp(_LTSkyA[6], _LTSkyB[6], w);
                float4 S7 = lerp(_LTSkyA[7], _LTSkyB[7], w);

                float2 uv = i.uv;
                half3 col = lerp(S1.rgb, S0.rgb, smoothstep(0.0, 1.0, uv.y));

                float2 d = uv - S5.xy;
                d.x *= max(_Aspect, 0.01);
                float r = length(d) / max(S5.z, 0.01);
                half g = pow(saturate(1.0 - r), S6.x) * S5.w;
                g *= 1.0 + S7.x * sin(r * S7.y * 6.2831);
                col = lerp(col, S2.rgb, saturate(g));

                if (S6.y > 0.001)
                {
                    half h = saturate(1.0 - uv.y / S6.y);
                    col = lerp(col, S3.rgb, h * h * S6.z);
                }

                float2 v = (uv - 0.5) * float2(1.0, 1.0);
                half vig = saturate(dot(v, v) * 2.2);
                col = lerp(col, S4.rgb, vig * S6.w);

                col += (hash21(floor(i.positionCS.xy)) - 0.5) * 2.0 * S7.z;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
