// Storybook checkpoint: the pen pass. A copy of the toon ink pass (ToonInkOutline, left untouched) for the storybook
// look: sepia pen lines where depth / normals break (silhouettes against the sky, other objects, sharp creases), the
// width breathing along the line like a nib, a few gaps so it reads hand drawn. Lines fade with the depth below the
// runner. Only where the storybook look is in force (StorybookCommon.hlsl, follows the look wipe).
Shader "Hidden/StorybookCheckpoint/Ink"
{
    Properties
    {
        _Width ("Line Width (pixels at 1920 high)", Range(0.5, 6)) = 2.0
        _DepthThreshold ("Silhouette Threshold (world units off the surface)", Range(0.02, 4)) = 0.3
        _NormalThreshold ("Crease Threshold (dot of normals)", Range(-1, 1)) = 0.6
        _FadeBelow ("Fade: start / full (units below the runner)", Vector) = (30, 90, 0, 0)
        _Strength ("Strength", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "StorybookInk"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Assets/StorybookCheckpoint/Shaders/StorybookCommon.hlsl"

            float _Width, _DepthThreshold, _NormalThreshold, _Strength;
            float4 _FadeBelow;
            float _PropFogRefY;

            bool IsSky(float raw)
            {
            #if UNITY_REVERSED_Z
                return raw <= 1e-6;
            #else
                return raw >= 1.0 - 1e-6;
            #endif
            }

            float3 WorldAt(float2 uv, float raw)
            {
            #if !UNITY_REVERSED_Z
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
            #endif
                return ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
            }

            half Edge(float2 uv, float3 pc, float3 nc, float2 offset)
            {
                float2 u2 = uv + offset;
                float raw = SampleSceneDepth(u2);
                if (IsSky(raw)) return 1.0h;
                float3 p = WorldAt(u2, raw);
                half sil = step(_DepthThreshold, abs(dot(p - pc, nc)));
                half crease = step(dot(SampleSceneNormals(u2), nc), _NormalThreshold);
                return max(sil, crease);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half sb = SbAt(uv);
                if (sb < 0.001h) return src;

                float raw = SampleSceneDepth(uv);
                if (IsSky(raw)) return src;
                float3 pc = WorldAt(uv, raw);
                float3 nc = normalize(SampleSceneNormals(uv));

                // The nib: width breathes along the line (world-anchored noise), gaps here and there.
                float nib = 0.65 + 0.7 * PixNoise(pc * 1.3);
                float px = _Width * nib * (_ScreenParams.y / 1920.0);
                float2 o = px / _ScreenParams.xy;
                half e = Edge(uv, pc, nc, float2(o.x, 0));
                e = max(e, Edge(uv, pc, nc, float2(-o.x, 0)));
                e = max(e, Edge(uv, pc, nc, float2(0, o.y)));
                e = max(e, Edge(uv, pc, nc, float2(0, -o.y)));
                half d = Edge(uv, pc, nc, o * 0.7071);
                d = max(d, Edge(uv, pc, nc, -o * 0.7071));
                e = max(e, d * 0.7h);
                e *= (half)lerp(0.55, 1.0, step(0.18, PixNoise(pc * 2.7 + 5.0)));

                float below = _PropFogRefY - pc.y;
                half fade = 1.0h - smoothstep(_FadeBelow.x, _FadeBelow.y, below);
                half ink = e * fade * sb * _Strength * _SbInk.a;
                half3 inkCol = lerp(_SbInk.rgb, src.rgb * 0.35h, 0.25h);
                return half4(lerp(src.rgb, inkCol, ink), src.a);
            }
            ENDHLSL
        }
    }
}
