// Pencil checkpoint: the paper pass (FullScreenPassRendererFeature "PencilPaper" on PencilCP_Renderer, after
// post-processing). Prints the frame as graphite on rough sketch paper:
//   tone     every colour to its grey (the page is black and white), mapped between the graphite and the paper
//   paper    rough cold-press tooth fixed to the screen: graphite catches on the bumps, so darks break up and lights
//            keep a faint texture; a few faint eraser smudges
//   edges    the sheet greys softly toward its edges, like a photographed page
// Only where the pencil look is in force (follows the look wipe). The UI is drawn after it, untouched.
Shader "Hidden/PencilCheckpoint/Paper"
{
    Properties
    {
        _Tooth ("Paper Tooth", Range(0, 0.4)) = 0.16
        _Contrast ("Graphite Contrast", Range(0.5, 2)) = 1.15
        _EdgeDarken ("Page Edge Darkening", Range(0, 1)) = 0.3
        _Smudge ("Smudges", Range(0, 0.3)) = 0.06
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "PencilPaper"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Assets/PencilCheckpoint/Shaders/PencilCommon.hlsl"

            float _Tooth, _Contrast, _EdgeDarken, _Smudge;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half pc = PcAt(uv);
                if (pc < 0.001h) return src;

                half3 paper = _PcPaper.rgb;
                half3 lead = _PcGraphite.rgb;
                half pl = max(PixLum(paper), 1e-3h);
                // Tone: grey value relative to the paper, contrast around the middle.
                half v = saturate(PixLum(src.rgb) / pl);
                v = saturate((v - 0.5h) * (half)_Contrast + 0.5h);
                half dark = 1.0h - v;

                // Paper tooth: bumps at a few scales, in 1080p pixels.
                float2 sp = uv * _ScreenParams.xy * (1080.0 / max(_ScreenParams.y, 1.0));
                float tooth = PixNoise(float3(sp * 0.45, 1.0)) * 0.5 + PixNoise(float3(sp * 1.3, 2.0)) * 0.3 + PixHash(float3(floor(sp), 3.0)) * 0.2;
                // Graphite skips the pits of the paper: darks get lighter specks, lights a faint grain.
                half skip = (half)saturate((tooth - 0.55) * 3.0);
                dark *= 1.0h - skip * (half)_Tooth * 2.0h * saturate(dark * 2.0h);
                dark = max(dark, (half)(0.5 - tooth) * (half)_Tooth * 0.35h);
                // Eraser smudges: big soft blotches.
                half smudge = (half)PixNoise(float3(sp * 0.004, 4.0));
                dark = saturate(dark + (smudge - 0.5h) * (half)_Smudge);

                half3 c = lerp(paper, lead, dark);
                float2 e = uv - 0.5;
                half edge = (half)smoothstep(0.3, 0.8, length(e * float2(1.0, 0.75)));
                c *= 1.0h - edge * (half)_EdgeDarken * 0.4h;
                return half4(lerp(src.rgb, c, pc), src.a);
            }
            ENDHLSL
        }
    }
}
