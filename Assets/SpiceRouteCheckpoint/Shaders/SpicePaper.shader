// Spice Route checkpoint: the paper pass. A full-screen pass after post-processing (URP FullScreenPassRendererFeature on
// SpiceCP_Renderer) that lays the frame on hand-made paper like the references' mixed-media collage:
//   pigment   slow blotches of uneven pigment (a little lighter / darker) and a whisper of coloured paper showing
//             through - pink, saffron, teal - so flat areas read as gouache, not as a render
//   grain     paper fibres and tooth, fixed to the screen at a 1080p scale
//   wear      sparse pale specks where the paint is distressed
//   colour    a touch more saturation, a warm vignette
// Cheap: one texture read and four value noises a pixel. Only where the spice look is in force (follows the look wipe).
Shader "Hidden/SpiceRouteCheckpoint/Paper"
{
    Properties
    {
        _Pigment ("Pigment Blotches", Range(0, 0.3)) = 0.09
        _Tint ("Coloured Paper Showing Through", Range(0, 0.2)) = 0.05
        _Grain ("Paper Grain", Range(0, 0.2)) = 0.06
        _Wear ("Distressed Specks", Range(0, 1)) = 0.35
        _Saturation ("Extra Saturation", Range(0, 0.5)) = 0.12
        _Vignette ("Warm Vignette", Range(0, 0.6)) = 0.22
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "SpicePaper"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Assets/SpiceRouteCheckpoint/Shaders/SpiceCommon.hlsl"

            float _Pigment, _Tint, _Grain, _Wear, _Saturation, _Vignette;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half sp = SrAt(uv);
                if (sp < 0.001h) return src;

                float2 cp = uv * _ScreenParams.xy * (1080.0 / max(_ScreenParams.y, 1.0));
                half3 c = src.rgb;
                half lum = PixLum(c);
                c = max(lerp(half3(lum, lum, lum), c, 1.0h + (half)_Saturation), 0.0h);

                // Pigment blotches + coloured paper showing through.
                float b1 = SrNoise2(cp * 0.0045 + 3.1);
                float b2 = SrNoise2(cp * 0.011 + 7.7);
                c *= 1.0h + (half)((b1 - 0.5) * 2.0 * _Pigment);
                half3 paperTint = b2 < 0.33 ? half3(1.0h, 0.55h, 0.7h) : b2 < 0.66 ? half3(1.0h, 0.75h, 0.4h) : half3(0.45h, 0.8h, 0.8h);
                c = lerp(c, c * paperTint * 1.2h, (half)(_Tint * smoothstep(0.2, 0.8, b1)));

                // Fibres + tooth.
                float fib = SrNoise2(float2(cp.x * 0.35, cp.y * 0.09)) * 0.6 + PixHash2(floor(cp * 0.5)) * 0.4;
                c *= 1.0h + (half)((fib - 0.5) * _Grain * 2.0);

                // Distressed specks: small pale flecks of bare paper, mostly on darker paint.
                float sp1 = PixHash2(floor(cp * 0.25));
                half speck = (half)(step(0.9975, sp1) * _Wear) * (1.0h - saturate(lum * 2.0h));
                c = lerp(c, half3(0.93h, 0.88h, 0.78h), speck);

                // Warm vignette.
                float2 vq = uv - 0.5;
                half vig = (half)saturate(dot(vq, vq) * 2.2);
                c = lerp(c, c * half3(0.78h, 0.6h, 0.62h), vig * (half)_Vignette);

                return half4(lerp(src.rgb, c, sp), src.a);
            }
            ENDHLSL
        }
    }
}
