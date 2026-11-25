// Pencil checkpoint: the cliffs and everything on them (houses, towers, trees, bushes, plants) made by
// PencilCheckpointController. Darkness of the material in vertex colour r (0 paper .. 1 dark), the kind of surface in
// uv.x: 0 plaster, 1 roof tiles, 2 foliage, 3 timber / wood, 4 window / door (dark), 5 cliff rock, 6 stone blocks.
// Drawn like the rest of the page: tone from the camera-relative light turned into hatching, plus the kind's own marks
// (tile scallops, scribbled leaf loops, block joints, rock strata); the outline comes from the pencil line pass (it reads
// the DepthNormals written here). Only drawn where the pencil look is in force; takes the chasm fog like the rocks.
Shader "PencilCheckpoint/Prop"
{
    Properties
    {
        _Scale ("Stroke Scale (world units)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/PencilCheckpoint/Shaders/PencilCommon.hlsl"
        #include "Assets/PencilCheckpoint/Shaders/PencilRock.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _Scale;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Assets/PropCameraLooks/LookTransition/Shaders/LookChasmFog.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                float kind : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.kind = v.uv.x;
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                half pc = PcAt(suv);
                clip(pc - 0.5h);

                float3 wp = i.positionWS;
                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                int kind = (int)(i.kind + 0.5);
                float S = max(_Scale, 0.05);
                half ink;
                if (kind == 5)
                {
                    ink = PcRockInk(wp, n, _PcLightDir.w, 5.0);
                }
                else
                {
                    float2 nh = n.xz;
                    float2 tg = dot(nh, nh) > 1e-4 ? normalize(float2(-nh.y, nh.x)) : float2(1, 0);
                    float2 q = (abs(n.y) > 0.7 ? wp.xz : float2(dot(wp.xz, tg), wp.y)) / S;
                    float aa = max(length(fwidth(q)), 1e-4) * 0.75;
                    half lit = PcLit(n);
                    half tone = saturate(lerp(0.7h, 0.04h, lit) + i.color.r * 0.6h);
                    float w = 0.02;
                    if (kind == 2)
                    {
                        // Foliage: scribbled loops, denser in shade.
                        float2 g = q * 3.0;
                        float2 c = floor(g);
                        float2 f = frac(g) - 0.5 + (float2(PixHash(float3(c, 1.0)), PixHash(float3(c, 2.0))) - 0.5) * 0.3;
                        float loop = length(f) - lerp(0.22, 0.34, PixHash(float3(c, 3.0)));
                        ink = PcLine(loop, 0.05, aa * 3.0) * (half)step(PixHash(float3(c, 4.0)), 0.35 + tone * 0.6);
                        ink = max(ink, PcTone(q, tone, 0.09, w * 0.8, aa, 3.0));
                    }
                    else
                    {
                        ink = PcTone(q, tone, 0.1, w, aa, kind);
                        if (kind == 1)
                        {
                            // Roof tiles: courses of scallops.
                            float row = q.y / 0.22;
                            float r = floor(row);
                            float x = frac(q.x / 0.22 + fmod(r, 2.0) * 0.5) - 0.5;
                            float sc = (frac(row) - 0.15) - (1.0 - sqrt(saturate(1.0 - x * x * 4.0))) * 0.6;
                            ink = max(ink, PcLine(sc * 0.22, w * 0.8, aa) * 0.85h);
                        }
                        else if (kind == 3)
                        {
                            // Timber / wood: grain lines along the piece, darker.
                            float gl = frac(q.y / 0.08 + sin(q.x * 3.0) * 0.2);
                            ink = max(ink, PcLine((gl - 0.5) * 0.08, w * 0.6, aa) * 0.7h);
                            ink = max(ink, 0.35h);
                        }
                        else if (kind == 4)
                        {
                            ink = max(ink, PcTone(q, 0.95h, 0.05, w, aa, 9.0));
                            ink = max(ink, 0.6h);
                        }
                        else if (kind == 6)
                        {
                            // Stone blocks.
                            float row = q.y / 0.3;
                            float r = floor(row);
                            float x = frac(q.x / 0.5 + fmod(r, 2.0) * 0.5);
                            ink = max(ink, PcLine(min(min(x, 1.0 - x) * 0.5, min(frac(row), 1.0 - frac(row)) * 0.3), w * 0.9, aa));
                        }
                        else
                        {
                            // Plaster: a few loose wall strokes.
                            float yv = frac(q.y * 1.3 + sin(q.x * 1.7) * 0.1);
                            ink = max(ink, PcLine((yv - 0.5) / 1.3, w * 0.6, aa) * (half)step(0.7, PixNoise(float3(q * 0.8, 4.0))) * 0.6h);
                        }
                    }
                }
                half3 c = PcInk(ink);
                c = ApplyChasmFog(c, wp, i.positionCS);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half4 frag(Varyings i) : SV_Target
            {
                clip(PcAt(GetNormalizedScreenSpaceUV(i.positionCS)) - 0.5h);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(PcAt(GetNormalizedScreenSpaceUV(i.positionCS)) - 0.5h);
                return half4(NormalizeNormalPerPixel(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
