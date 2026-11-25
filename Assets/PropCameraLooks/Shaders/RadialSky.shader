// Backdrop for the "MainScene prop camera 1/2/3" look scenes. Drawn on the quad MonumentSkyBackdrop
// keeps filling the camera's view just in front of the far plane (it sets _Aspect), so it replaces
// the skybox without touching the camera.
//
//   A vertical gradient (_TopColor -> _BottomColor), a round glow (_MidColor at _GlowCenter, falling
//   off over _GlowRadius - aspect corrected, so it stays a circle on a phone), a darkening vignette
//   toward the corners, faint concentric rings (the soft banding in the teal reference) and a fine
//   static grain. No stars.
//
// Property names follow MonumentSky's (_TopColor/_MidColor/_BottomColor/_GlowColor/_StarColor) so
// PortalZoneController can read the opening palette from it as it does there.
Shader "PropLooks/RadialSky"
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

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor, _BottomColor, _MidColor, _GlowColor, _VignetteColor, _StarColor;
                float4 _GlowCenter;
                half _GlowRadius, _GlowStrength, _GlowFalloff, _HazeHeight, _HazeStrength;
                half _Vignette, _Rings, _RingCount, _Grain;
                float _Aspect;
            CBUFFER_END

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
                float2 uv = i.uv;
                half3 col = lerp(_BottomColor.rgb, _TopColor.rgb, smoothstep(0.0, 1.0, uv.y));

                // Round glow, measured in screen heights so it stays circular at any aspect.
                float2 d = uv - _GlowCenter.xy;
                d.x *= max(_Aspect, 0.01);
                float r = length(d) / max(_GlowRadius, 0.01);
                half g = pow(saturate(1.0 - r), _GlowFalloff) * _GlowStrength;
                // Faint concentric banding in the glow.
                g *= 1.0 + _Rings * sin(r * _RingCount * 6.2831);
                col = lerp(col, _MidColor.rgb, saturate(g));

                // Warm haze rising from the bottom of the frame.
                if (_HazeHeight > 0.001)
                {
                    half h = saturate(1.0 - uv.y / _HazeHeight);
                    col = lerp(col, _GlowColor.rgb, h * h * _HazeStrength);
                }

                // Vignette toward the corners.
                float2 v = (uv - 0.5) * float2(1.0, 1.0);
                half vig = saturate(dot(v, v) * 2.2);
                col = lerp(col, _VignetteColor.rgb, vig * _Vignette);

                // Fine static grain.
                col += (hash21(floor(i.positionCS.xy)) - 0.5) * 2.0 * _Grain;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
