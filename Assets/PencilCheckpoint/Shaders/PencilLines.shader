// Pencil checkpoint: the line pass (FullScreenPassRendererFeature "PencilLines" on PencilCP_Renderer, before
// post-processing; needs depth + normals). Graphite outlines where depth / normals break - silhouettes, other objects,
// creases - drawn twice with a small world-anchored wobble between the strokes, so every edge reads as sketched by hand
// (the robot's doubled lines). Line weight breathes along the stroke, a few gaps, lighter with distance below the runner.
// Only where the pencil look is in force (PencilCommon.hlsl, follows the look wipe).
Shader "Hidden/PencilCheckpoint/Lines"
{
    Properties
    {
        _Width ("Line Width (pixels at 1920 high)", Range(0.5, 6)) = 2.2
        _DepthThreshold ("Silhouette Threshold (world units off the surface)", Range(0.02, 4)) = 0.25
        _NormalThreshold ("Crease Threshold (dot of normals)", Range(-1, 1)) = 0.7
        _Wobble ("Second Stroke Offset (pixels at 1920 high)", Range(0, 8)) = 2.5
        _FadeBelow ("Fade: start / full (units below the runner)", Vector) = (35, 110, 0, 0)
        _Strength ("Strength", Range(0, 1)) = 0.9
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "PencilLines"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Assets/PencilCheckpoint/Shaders/PencilCommon.hlsl"

            float _Width, _DepthThreshold, _NormalThreshold, _Wobble, _Strength;
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

            half EdgeAt(float2 uv, float px)
            {
                float raw = SampleSceneDepth(uv);
                if (IsSky(raw)) return 0.0h;
                float3 pc = WorldAt(uv, raw);
                float3 nc = normalize(SampleSceneNormals(uv));
                float2 o = px / _ScreenParams.xy;
                half e = Edge(uv, pc, nc, float2(o.x, 0));
                e = max(e, Edge(uv, pc, nc, float2(-o.x, 0)));
                e = max(e, Edge(uv, pc, nc, float2(0, o.y)));
                e = max(e, Edge(uv, pc, nc, float2(0, -o.y)));
                return e;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half pc = PcAt(uv);
                if (pc < 0.001h) return src;

                float raw = SampleSceneDepth(uv);
                if (IsSky(raw)) return src;
                float3 wp = WorldAt(uv, raw);
                float sc = _ScreenParams.y / 1920.0;

                // First stroke: the edge itself, weight breathing along the line.
                float nib = 0.7 + 0.6 * PixNoise(wp * 1.1);
                half e1 = EdgeAt(uv, _Width * nib * sc);
                // Second stroke: the same edge looked up from a little off to the side (world-anchored wobble), lighter.
                float2 wob = float2(PixNoise(wp * 0.6 + 3.0) - 0.5, PixNoise(wp * 0.6 + 9.0) - 0.5) * 2.0 * _Wobble * sc / _ScreenParams.xy;
                half e2 = EdgeAt(uv + wob, _Width * 0.7 * sc);
                half gaps = (half)lerp(0.5, 1.0, step(0.2, PixNoise(wp * 2.3 + 5.0)));
                half e = max(e1 * gaps, e2 * 0.55h);
                // Graphite grain along the stroke.
                e *= (half)lerp(0.7, 1.0, PixHash(float3(floor(uv * _ScreenParams.xy * 0.5), 7.0)));

                float below = _PropFogRefY - wp.y;
                half fade = 1.0h - smoothstep(_FadeBelow.x, _FadeBelow.y, below) * 0.7h;
                half ink = e * fade * pc * _Strength;
                return half4(lerp(src.rgb, _PcGraphite.rgb, ink), src.a);
            }
            ENDHLSL
        }
    }
}
