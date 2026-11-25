// The glowing sea of light under the track in "MainScene prop camera 4" (the orange Monument Valley
// canyon reference). Drawn on the plane MonumentShadowFloor keeps a gap below the lowest visible
// track and centred on the runner (it sets _FadeCenter).
//
//   Glow     bright yellow at the centre, deeper orange-gold toward _FadeRadius, with slow drifting
//            haze (two octaves of value noise, world anchored) so it reads as light, not paint.
//   Shadow   the track's shadow falls on it as a soft warm stain (blurred shadow-map taps, as in
//            MonumentShadowFloor) - the one cue that says how high the track hangs above the glow.
//   Edge     alpha fades out toward _FadeRadius so the plane melts into the backdrop, never showing
//            its edge.
Shader "PropLooks/GlowFloor"
{
    Properties
    {
        _CenterColor ("Centre Glow", Color) = (1, 0.87, 0.1, 1)
        _EdgeColor ("Outer Glow", Color) = (1, 0.7, 0.12, 1)
        _HazeColor ("Haze Streaks", Color) = (1, 0.95, 0.6, 1)
        _HazeStrength ("Haze Strength", Range(0, 1)) = 0.25
        _HazeScale ("Haze Scale (world)", Float) = 0.06
        _HazeSpeed ("Haze Drift Speed", Float) = 0.35
        _ShadowTint ("Shadow Tint", Color) = (0.85, 0.45, 0.12, 1)
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.45
        _Blur ("Shadow Blur (world)", Range(0, 4)) = 1.4
        _FadeCenter ("Fade Centre (set by script)", Vector) = (0, 0, 0, 0)
        _InnerRadius ("Full Glow Radius", Float) = 18
        _FadeRadius ("Fade Radius", Float) = 70
        _Opacity ("Opacity", Range(0, 1)) = 0.92
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-60" "IgnoreProjector" = "True" }

        Pass
        {
            Name "GlowFloor"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
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
                half4 _CenterColor, _EdgeColor, _HazeColor, _ShadowTint;
                half _HazeStrength, _ShadowStrength, _Opacity;
                float _HazeScale, _HazeSpeed, _Blur, _InnerRadius, _FadeRadius;
                float4 _FadeCenter;
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

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 x)
            {
                float2 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), f.x),
                            lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
            }

            half ShadowAt(float3 p)
            {
                // The cascaded map directly: the screen-space shadow texture only knows the depth
                // buffer, which this floor never writes.
                return MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float dist = length(i.positionWS.xz - _FadeCenter.xz);
                half r = saturate((dist - _InnerRadius) / max(1.0, _FadeRadius - _InnerRadius));
                half3 col = lerp(_CenterColor.rgb, _EdgeColor.rgb, smoothstep(0.0, 1.0, r));

                // Slow drifting haze.
                float t = _Time.y * _HazeSpeed;
                float2 p = i.positionWS.xz * _HazeScale;
                float n = vnoise(p + float2(t, t * 0.6)) * 0.6 + vnoise(p * 2.3 - float2(t * 0.7, -t)) * 0.4;
                col = lerp(col, _HazeColor.rgb, saturate((n - 0.45) * 2.0) * _HazeStrength);

                // Blurred track shadow.
                static const float2 disk[8] = {
                    float2(1, 0), float2(0.707, 0.707), float2(0, 1), float2(-0.707, 0.707),
                    float2(-1, 0), float2(-0.707, -0.707), float2(0, -1), float2(0.707, -0.707) };
                half s = ShadowAt(i.positionWS) * 2;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float rr = (k & 1) ? _Blur : _Blur * 0.5;
                    s += ShadowAt(i.positionWS + float3(disk[k].x, 0, disk[k].y) * rr);
                }
                s /= 10.0;
                col *= lerp(half3(1, 1, 1), _ShadowTint.rgb, (1 - s) * _ShadowStrength);

                half alpha = _Opacity * (1.0 - smoothstep(0.55, 1.0, dist / max(1.0, _FadeRadius)));
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
