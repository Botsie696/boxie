// Toon checkpoint copy of Runner/TrailFX Face Ribbon (Shaders/RunnerTrail/RunnerFaceTrail.shader, left untouched). Pixel for
// pixel the same, except where the toon look is in force (ToonCommon.hlsl, follows the look wipe): there the glow ribbon
// becomes a flat cartoon speed-stripe - hard edges, ink border, cream core, the tail chopped into dashes that thin out.
// Original notes:
// The runner's trail laid flat on the face of the path it rides (FaceHuggingTrail.cs).
// Same look as Runner/TrailFX Ribbon (same property names, so RunnerTrailFX's per-frame block drives it), but:
//  - no pull toward the camera: the ribbon is depth tested against the path, so on a hidden face the leg covers it
//  - nothing is drawn through a cube: the fin (FaceHuggingTrail) is what keeps a hidden face readable
// uv.x runs 0 at the runner to 1 at the tail, uv.y across the width; uv2.x is the distance along the trail (for dashes).
// Vertex colour rgb above 1 brightens a stretch (the fold over an edge).
Shader "ToonCheckpoint/TrailFX Face Ribbon Toon"
{
    Properties
    {
        [HDR] _HeadColor ("Head Colour", Color) = (0.85, 1, 1, 1)
        [HDR] _MidColor ("Mid Colour", Color) = (0.25, 0.85, 1, 1)
        [HDR] _TailColor ("Tail Colour", Color) = (0.42, 0.3, 1, 1)
        [HDR] _CoreColor ("Core Colour", Color) = (1, 1, 1, 1)
        _CoreWidth ("Core Width", Range(0.01, 1)) = 0.14
        _CoreLength ("Core Length", Range(0.01, 1)) = 0.25
        _GlowFalloff ("Glow Falloff", Range(0.5, 12)) = 2.2
        _Intensity ("Intensity", Range(0, 4)) = 1
        _Opacity ("Body Opacity", Range(0, 1)) = 0.55
        _TailPower ("Tail Fade Power", Range(0.2, 4)) = 1.2
        _FlowScale ("Shimmer Scale", Range(0.05, 4)) = 0.8
        _FlowSpeed ("Shimmer Speed", Range(0, 10)) = 1.6
        _FlowStrength ("Shimmer Strength", Range(0, 1)) = 0.55
        _Flash ("Flash", Range(0, 2)) = 0
        [HDR] _FlashColor ("Flash Colour", Color) = (1, 1, 1, 1)
        _CameraPull ("Pull Toward Camera", Range(0, 0.2)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/ToonCheckpoint/Shaders/ToonCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _HeadColor;
            half4 _MidColor;
            half4 _TailColor;
            half4 _CoreColor;
            half _CoreWidth;
            half _CoreLength;
            half _GlowFalloff;
            half _Intensity;
            half _Opacity;
            half _TailPower;
            float _FlowScale;
            float _FlowSpeed;
            half _FlowStrength;
            half _Flash;
            half4 _FlashColor;
            float _CameraPull;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
            float2 uv2 : TEXCOORD1;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            float dist : TEXCOORD2;
        };

        float Hash31(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float ValueNoise(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash31(i),                   Hash31(i + float3(1, 0, 0)), f.x),
                             lerp(Hash31(i + float3(0, 1, 0)), Hash31(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(Hash31(i + float3(0, 0, 1)), Hash31(i + float3(1, 0, 1)), f.x),
                             lerp(Hash31(i + float3(0, 1, 1)), Hash31(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

        Varyings Vert(Attributes input)
        {
            Varyings output;
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionWS = positionWS;
            // Just enough to win the depth test against the face it lies on, not enough to show through the leg.
            positionWS += GetWorldSpaceNormalizeViewDir(positionWS) * _CameraPull;
            output.positionCS = TransformWorldToHClip(positionWS);
            output.color = input.color;
            output.uv = input.uv;
            output.dist = input.uv2.x;
            return output;
        }

        half4 Ribbon(Varyings input)
        {
            half t = saturate(input.uv.x);
            half across = abs(input.uv.y * 2.0 - 1.0);
            half glow = exp(-across * across * _GlowFalloff) * (1.0 - across * across);
            half core = saturate(1.0 - across / _CoreWidth);
            core *= core * saturate(1.0 - t / _CoreLength);

            half3 grad = t < 0.5 ? lerp(_HeadColor.rgb, _MidColor.rgb, t * 2.0)
                                 : lerp(_MidColor.rgb, _TailColor.rgb, t * 2.0 - 1.0);

            float time = _Time.y * _FlowSpeed;
            float3 p = input.positionWS * _FlowScale;
            half n = ValueNoise(p + float3(0.31, 0.63, 0.47) * time) * 0.65
                   + ValueNoise(p * 2.3 - float3(0.5, 0.2, 0.7) * time) * 0.35;
            half shimmer = lerp(1.0, 0.4 + 1.2 * n + 0.9 * n * n * n * n, _FlowStrength);

            half3 col = grad * glow * shimmer
                      + _CoreColor.rgb * core
                      + _FlashColor.rgb * (_Flash * 0.7 * glow * (1.0 - t));
            half fade = pow(1.0 - t, _TailPower) * input.color.a;
            col *= input.color.rgb * fade * _Intensity;
            half4 glowOut = half4(col, saturate(glow * fade * _Opacity));

            // ---- toon: a flat painted speed-stripe instead of the glow ----
            half toon = ToonAt(GetNormalizedScreenSpaceUV(input.positionCS));
            if (toon < 0.001h) return glowOut;
            float aaX = fwidth(across) + 1e-4;
            half body = 1.0h - smoothstep(0.8h - aaX, 0.8h + aaX, across);
            half inkBorder = smoothstep(0.6h - aaX, 0.6h + aaX, across) * body;
            half coreStripe = (1.0h - smoothstep(0.2h - aaX, 0.2h + aaX, across)) * (1.0h - smoothstep(0.5h, 0.56h, t));
            // The tail breaks into dashes that get shorter, then stop.
            float seg = frac(input.dist * 0.9);
            half dashLen = lerp(1.0h, 0.3h, smoothstep(0.3h, 0.8h, t));
            half dashOn = step(seg, dashLen);
            half alive = (1.0h - step(0.9h, t)) * dashOn;
            half3 bodyCol = t < 0.45h ? _MidColor.rgb : lerp(_MidColor.rgb, _TailColor.rgb, saturate((t - 0.45h) / 0.45h) * 0.6h);
            half3 tc = lerp(bodyCol, _CoreColor.rgb, coreStripe);
            tc = lerp(tc, _ToonInk.rgb, inkBorder * 0.85h);
            tc = lerp(tc, _FlashColor.rgb, saturate(_Flash) * 0.5h * (1.0h - t));
            tc *= saturate(input.color.rgb);
            half a = body * alive * (t < 0.5h ? 1.0h : 0.8h) * saturate(input.color.a * 1.5h);
            half4 toonOut = half4(tc * a, a);
            return lerp(glowOut, toonOut, toon);
        }
        ENDHLSL

        // The visible part: on the face, hidden by anything in front of it.
        Pass
        {
            Name "FaceRibbon"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return Ribbon(input); }
            ENDHLSL
        }
    }
}
