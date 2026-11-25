// Storybook checkpoint: the paper pass. A full-screen pass after post-processing (URP FullScreenPassRendererFeature on
// StorybookCP_Renderer) that prints the frame on watercolour paper:
//   tone      colours pulled a little toward the paper's warm cream (the reference's aged print), blacks lifted to ink brown
//   paper     cold-press grain (fibres + tooth, fixed to the screen) darkens the washes unevenly, more in the mid tones
//   edges     the sheet darkens softly toward its edges, like a scanned page
// Only where the storybook look is in force (follows the look wipe). The UI is drawn after it, untouched.
Shader "Hidden/StorybookCheckpoint/Paper"
{
    Properties
    {
        _Warmth ("Toward the Paper Tone", Range(0, 1)) = 0.16
        _Grain ("Paper Grain", Range(0, 0.3)) = 0.08
        _InkLift ("Ink-brown Shadows", Range(0, 1)) = 0.35
        _EdgeDarken ("Page Edge Darkening", Range(0, 1)) = 0.22
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "StorybookPaper"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Assets/StorybookCheckpoint/Shaders/StorybookCommon.hlsl"

            float _Warmth, _Grain, _InkLift, _EdgeDarken;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half sb = SbAt(uv);
                if (sb < 0.001h) return src;

                half3 c = src.rgb;
                half3 paper = _SbPaper.rgb;
                half lum = PixLum(c);
                // Tone: toward the cream paper, shadows toward ink brown instead of black.
                c = lerp(c, c * paper / max(PixLum(paper), 1e-3h), (half)_Warmth);
                c = lerp(c, max(c, _SbInk.rgb * 1.3h), (half)_InkLift * (1.0h - saturate(lum * 4.0h)));

                // Paper: fibres (stretched noise) + tooth, in screen pixels at 1080p scale.
                float2 sp = uv * _ScreenParams.xy * (1080.0 / max(_ScreenParams.y, 1.0));
                float fib = PixNoise(float3(sp.x * 0.09, sp.y * 0.35, 1.0)) * 0.5 + PixNoise(float3(sp * 0.6, 2.0)) * 0.3 + PixHash(float3(floor(sp), 3.0)) * 0.2;
                half mid = 1.0h - abs(lum - 0.55h) * 1.2h;
                c *= 1.0h - (half)(fib - 0.45) * (half)_Grain * 2.0h * saturate(mid + 0.4h);

                // Page edges.
                float2 e = uv - 0.5;
                half edge = (half)smoothstep(0.35, 0.75, length(e * float2(1.0, 0.8)));
                c *= 1.0h - edge * (half)_EdgeDarken * 0.5h;

                return half4(lerp(src.rgb, c, sb), src.a);
            }
            ENDHLSL
        }
    }
}
