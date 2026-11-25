// Pixel checkpoint: the pixel-art pass. A full-screen pass (URP FullScreenPassRendererFeature on PixelCP_Renderer, after
// post-processing) that turns the frame into the reference's pixel art:
//   pixels    the screen is cut into square cells (_PixPost.y pixels at 1920 high); each cell takes the average of four
//             taps inside it - one flat colour per cell. Once the look is fully in, the camera renders at low resolution
//             (PixelCheckpointController.pixelRenderScale) and a cell is one rendered pixel, so this costs almost nothing
//   palette   each cell snaps to the reference's measured palette (_PixPalette, sRGB, set by PixelCheckpointController):
//             the two nearest palette colours, picked between with a 4x4 ordered dither - the reference's checker
//             transitions (_PixPost.w) - and blended over the cell colour by _PixPost.z
// Only where the pixel look is in force (PixelCommon.hlsl, follows the look wipe, decided per cell so the wipe edge is
// pixelated too). Nowhere else does this pass change a pixel. The UI (overlay canvases) is drawn after it, untouched.
Shader "Hidden/PixelCheckpoint/PixelPost"
{
    Properties
    {
        _PixelSize ("Cell Size (pixels at 1920 high)", Range(1, 16)) = 5
        _PaletteMix ("Palette Snap", Range(0, 1)) = 0.8
        _Dither ("Dither", Range(0, 1)) = 0.8
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "PixelPost"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Assets/PixelCheckpoint/Shaders/PixelCommon.hlsl"

            float _PixelSize, _PaletteMix, _Dither;
            float4 _PixPalette[32];      // sRGB colours, a = 1 for a used entry
            float4 _PixPost;             // x palette count, y cell size (pixels at 1920 high, 0 = material), z palette mix, w dither

            half3 ToSRGB(half3 c) { return LinearToSRGB(max(c, 0.0h)); }
            half3 ToLinear(half3 c) { return SRGBToLinear(c); }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float size = _PixPost.y > 0.5 ? _PixPost.y : _PixelSize;
                float px = max(1.0, round(size * (_ScreenParams.y / 1920.0)));
                float2 res = _ScreenParams.xy;
                float2 cell = floor(uv * res / px);
                float2 centre = (cell + 0.5) * px / res;

                half pix = PixAt(centre);
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
                if (pix < 0.001h) return src;

                float2 o = px > 1.5 ? (px * 0.25) / res : float2(0, 0);
                half3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, centre + float2(o.x, o.y)).rgb;
                c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, centre + float2(-o.x, o.y)).rgb;
                c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, centre + float2(o.x, -o.y)).rgb;
                c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, centre + float2(-o.x, -o.y)).rgb;
                c *= 0.25h;

                half3 s = ToSRGB(c);
                half3 outS = s;
                int count = (int)_PixPost.x;
                if (count > 1)
                {
                    // Two nearest palette colours (perceptual-ish weights on sRGB).
                    const half3 wgt = half3(0.30h, 0.59h, 0.11h) * 3.0h;
                    float d1 = 1e9, d2 = 1e9;
                    half3 c1 = s, c2 = s;
                    for (int k = 0; k < 32; k++)
                    {
                        if (k >= count) break;
                        half3 pk = (half3)_PixPalette[k].rgb;
                        half3 dd = s - pk;
                        float d = dot(dd * dd, wgt);
                        if (d < d1) { d2 = d1; c2 = c1; d1 = d; c1 = pk; }
                        else if (d < d2) { d2 = d; c2 = pk; }
                    }
                    // Where between the two the cell sits, then an ordered dither decides which one it shows.
                    half3 ab = c2 - c1;
                    half tt = saturate(dot(s - c1, ab * wgt) / max(dot(ab * ab, wgt), 1e-5));
                    half thr = lerp(0.5h, (half)PixBayer(cell), (half)_PixPost.w);
                    half3 pick = tt > thr ? c2 : c1;
                    half mix = (half)_PixPost.z;
                    outS = lerp(s, pick, mix);
                }
                half3 outL = ToLinear(outS);
                return half4(lerp(src.rgb, outL, pix), src.a);
            }
            ENDHLSL
        }
    }
}
