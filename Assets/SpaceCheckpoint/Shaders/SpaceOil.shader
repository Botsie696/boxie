// Space checkpoint: the oil-paint pass. A full-screen pass after post-processing (URP FullScreenPassRendererFeature on
// SpaceCP_Renderer) that turns the frame into thick palette-knife / brush work:
//   dabs      a small Kuwahara filter (4 quadrants x 4 bilinear taps, ~3 px): each pixel takes the mean of its calmest
//             quadrant, so areas flatten into paint dabs while edges stay crisp
//   strokes   the sampling offset follows a slow noise flow, so the dabs lean into stroke directions
//   impasto   a fake height field of long strokes lights the paint from the upper left: ridges catch a bright edge, the
//             far side of each ridge a little shade - the "thick paint" read of the references
//   colour    extra saturation (the references are vivid) and a canvas weave
// Only where the space look is in force (follows the look wipe). The UI is drawn after it, untouched.
Shader "Hidden/SpaceCheckpoint/Oil"
{
    Properties
    {
        _Radius ("Dab Radius (pixels at 1080 high)", Range(0.5, 4)) = 2.3
        _Saturation ("Extra Saturation", Range(0, 0.6)) = 0.22
        _Impasto ("Impasto Light", Range(0, 1)) = 0.18
        _Canvas ("Canvas Tooth", Range(0, 0.2)) = 0.05
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "SpaceOil"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Assets/SpaceCheckpoint/Shaders/SpaceCommon.hlsl"

            float _Radius, _Saturation, _Impasto, _Canvas;

            void Quadrant(float2 uv, float2 dx, float2 dy, inout half3 best, inout half bestVar)
            {
                half3 a = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dx * 0.5 + dy * 0.5).rgb;
                half3 b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dx * 1.5 + dy * 0.5).rgb;
                half3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dx * 0.5 + dy * 1.5).rgb;
                half3 d = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dx * 1.5 + dy * 1.5).rgb;
                half3 m = (a + b + c + d) * 0.25h;
                half3 v3 = (a * a + b * b + c * c + d * d) * 0.25h - m * m;
                half v = v3.r + v3.g + v3.b;
                if (v < bestVar) { bestVar = v; best = m; }
            }

            // The paint's height at a 1080p pixel position: long strokes along (ax), narrow across.
            float PaintHeight(float2 cp, float2 ax)
            {
                float2 ay = float2(-ax.y, ax.x);
                float2 q = float2(dot(cp, ax), dot(cp, ay));
                return PixNoise(float3(q.x * 0.045, q.y * 0.08, 2.0)) * 0.7 + PixNoise(float3(q.x * 0.11, q.y * 0.2, 5.0)) * 0.3;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half sp = SpAt(uv);
                if (sp < 0.001h) return src;

                float scale = _Radius * max(_ScreenParams.y, 1.0) / 1080.0;
                float2 texel = 1.0 / max(_ScreenParams.xy, 1.0);
                float2 sPos = uv * float2(_ScreenParams.x / max(_ScreenParams.y, 1.0), 1.0);
                float ang = PixNoise(float3(sPos * 1.3, 3.0)) * 3.1416;
                float2 ax = float2(cos(ang), sin(ang));
                float2 ay = float2(-ax.y, ax.x) * 0.6;
                float2 dx = ax * texel * scale * 0.5, dy = ay * texel * scale * 0.5;

                half3 best = src.rgb;
                half bestVar = 1e4h;
                Quadrant(uv, dx, dy, best, bestVar);
                Quadrant(uv, -dx, dy, best, bestVar);
                Quadrant(uv, dx, -dy, best, bestVar);
                Quadrant(uv, -dx, -dy, best, bestVar);

                half3 c = best;
                half lum = PixLum(c);
                c = max(lerp(half3(lum, lum, lum), c, 1.0h + (half)_Saturation), 0.0h);

                // Impasto: light the stroke height field from the upper left.
                float2 cp = uv * _ScreenParams.xy * (1080.0 / max(_ScreenParams.y, 1.0));
                float h0 = PaintHeight(cp, ax);
                float hx = PaintHeight(cp + float2(1.5, 0.0), ax) - h0;
                float hy = PaintHeight(cp + float2(0.0, 1.5), ax) - h0;
                half slope = (half)((-hx + hy) * 2.0);
                half lit = saturate(slope) , shade = saturate(-slope);
                c *= 1.0h - shade * (half)_Impasto * 0.55h;
                c += lit * (half)_Impasto * 0.18h * (0.4h + lum);

                // Canvas tooth, fixed to the screen at 1080p scale.
                float tooth = PixNoise(float3(cp * 0.45, 1.0)) * 0.6 + PixHash(float3(floor(cp * 0.5), 2.0)) * 0.4;
                c *= 1.0h + (half)(tooth - 0.5) * (half)_Canvas;
                return half4(lerp(src.rgb, c, sp), src.a);
            }
            ENDHLSL
        }
    }
}
