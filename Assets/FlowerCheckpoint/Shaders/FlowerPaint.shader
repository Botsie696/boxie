// Flower checkpoint: the paint pass. A full-screen pass after post-processing (URP FullScreenPassRendererFeature on
// FlowerCP_Renderer) that turns the frame into flat brush work:
//   dabs      a small Kuwahara filter (4 quadrants x 4 bilinear taps, ~3 px): each pixel takes the mean of its calmest
//             quadrant, so areas flatten into dabs while edges stay crisp
//   strokes   the sampling offset follows a slow noise flow, so the dabs lean into stroke directions
//   colour    a little extra saturation (the reference is vivid) and a canvas tooth
// Only where the flower look is in force (follows the look wipe). The UI is drawn after it, untouched.
Shader "Hidden/FlowerCheckpoint/Paint"
{
    Properties
    {
        _Radius ("Dab Radius (pixels at 1080 high)", Range(0.5, 4)) = 2.2
        _Saturation ("Extra Saturation", Range(0, 0.6)) = 0.18
        _Canvas ("Canvas Tooth", Range(0, 0.2)) = 0.05
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "FlowerPaint"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Assets/FlowerCheckpoint/Shaders/FlowerCommon.hlsl"

            float _Radius, _Saturation, _Canvas;

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

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half fl = FlAt(uv);
                if (fl < 0.001h) return src;

                float scale = _Radius * max(_ScreenParams.y, 1.0) / 1080.0;
                float2 texel = 1.0 / max(_ScreenParams.xy, 1.0);
                // Stroke direction: a slow flow field in screen space.
                float2 sp = uv * float2(_ScreenParams.x / max(_ScreenParams.y, 1.0), 1.0);
                float ang = PixNoise(float3(sp * 5.0, 3.0)) * 6.283;
                float2 ax = float2(cos(ang), sin(ang));
                float2 ay = float2(-ax.y, ax.x) * 0.6;   // dabs are longer than wide
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
                // Canvas tooth, fixed to the screen at 1080p scale.
                float2 cp = uv * _ScreenParams.xy * (1080.0 / max(_ScreenParams.y, 1.0));
                float tooth = PixNoise(float3(cp * 0.45, 1.0)) * 0.6 + PixHash(float3(floor(cp * 0.5), 2.0)) * 0.4;
                c *= 1.0h + (half)(tooth - 0.5) * (half)_Canvas;
                return half4(lerp(src.rgb, c, fl), src.a);
            }
            ENDHLSL
        }
    }
}
