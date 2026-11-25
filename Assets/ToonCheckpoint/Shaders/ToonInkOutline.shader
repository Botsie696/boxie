// Toon checkpoint: the ink pass. A full-screen pass (URP FullScreenPassRendererFeature on ToonCP_Renderer, before the
// transparents) that draws dark pen lines where the scene's depth / normals break:
//   - silhouettes: a neighbour pixel is far off this pixel's own surface plane (another object, or the sky behind)
//   - creases:     the surface normal turns sharply (a leg's long edges, the end of a leg, a rock's big facets)
// Neighbouring cubes of one leg lie in one plane with one normal, so the joints between them never get a line - a leg
// reads as one block. Lines fade out with the depth below the runner, as the fog does, and only appear on the side of
// the look wipe where the toon look is in force (ToonCommon.hlsl). Nowhere else does this pass change a pixel.
Shader "Hidden/ToonCheckpoint/InkOutline"
{
    Properties
    {
        _Width ("Line Width (pixels at 1920 high)", Range(0.5, 6)) = 2.2
        _DepthThreshold ("Silhouette Threshold (world units off the surface)", Range(0.02, 4)) = 0.35
        _NormalThreshold ("Crease Threshold (dot of normals)", Range(-1, 1)) = 0.55
        _FadeBelow ("Fade: start / full (units below the runner)", Vector) = (26, 80, 0, 0)
        _Strength ("Strength", Range(0, 1)) = 0.9
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "InkOutline"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Assets/ToonCheckpoint/Shaders/ToonCommon.hlsl"

            float _Width, _DepthThreshold, _NormalThreshold, _Strength;
            float4 _FadeBelow;
            float _PropFogRefY;

            float RawDepth(float2 uv) { return SampleSceneDepth(uv); }

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
                float raw = RawDepth(u2);
                if (IsSky(raw)) return 1.0h;                       // the sky right next to geometry: silhouette
                float3 p = WorldAt(u2, raw);
                float off = abs(dot(p - pc, nc));
                half sil = step(_DepthThreshold, off);
                float3 n = SampleSceneNormals(u2);
                half crease = step(dot(n, nc), _NormalThreshold);
                return max(sil, crease);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                half toon = ToonAt(uv);
                if (toon < 0.001h) return src;

                float raw = RawDepth(uv);
                if (IsSky(raw)) return src;                        // lines are drawn on the geometry side only
                float3 pc = WorldAt(uv, raw);
                if (_PropFogRefY - pc.y > _FadeBelow.y) return src;   // fully faded: no lines, skip the taps
                float3 nc = normalize(SampleSceneNormals(uv));

                float px = _Width * (_ScreenParams.y / 1920.0);
                float2 o = px / _ScreenParams.xy;
                half e = Edge(uv, pc, nc, float2(o.x, 0));
                e = max(e, Edge(uv, pc, nc, float2(-o.x, 0)));
                e = max(e, Edge(uv, pc, nc, float2(0, o.y)));
                e = max(e, Edge(uv, pc, nc, float2(0, -o.y)));
                // Mobile: four taps (the diagonals were dropped: half the depth + normal reads per pixel).

                float below = _PropFogRefY - pc.y;
                half fade = 1.0h - smoothstep(_FadeBelow.x, _FadeBelow.y, below);
                half ink = e * fade * toon * _Strength * _ToonInk.a;
                half3 inkCol = lerp(_ToonInk.rgb, src.rgb * 0.35h, 0.25h);
                return half4(lerp(src.rgb, inkCol, ink), src.a);
            }
            ENDHLSL
        }
    }
}
