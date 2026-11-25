// Storybook checkpoint: the props on the rocks (trees, little houses with roofs, towers with flags) made by
// StorybookCheckpointController. Pigment in the vertex colour (linear), the kind of surface in uv.x (0 stone / plaster,
// 1 roof tiles, 2 foliage, 3 cloth / wood), the flag's wave weight in the vertex alpha. Painted like the rest of the look:
// a lit and a cool shade wash from the camera-relative light, pen hatching in the shade, tile courses / blocks / leafy dabs
// in pen, pooled pigment; the outline comes from the storybook ink pass (it reads the DepthNormals written here).
// Only drawn where the storybook look is in force (follows the look wipe); takes the chasm fog like the rocks.
Shader "StorybookCheckpoint/Prop"
{
    Properties
    {
        _WaveSpeed ("Flag Wave Speed", Float) = 5
        _WaveAmount ("Flag Wave Amount", Float) = 0.22
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/StorybookCheckpoint/Shaders/StorybookCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _WaveSpeed, _WaveAmount;
        CBUFFER_END

        float3 Wave(float3 posOS, float w)
        {
            float t = _Time.y * _WaveSpeed;
            float s = posOS.x * 3.0;
            posOS.z += sin(t + s) * _WaveAmount * w;
            posOS.y += sin(t * 0.7 + s * 1.3) * _WaveAmount * 0.25 * w;
            return posOS;
        }
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
                float3 p = Wave(v.positionOS.xyz, v.color.a);
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.kind = v.uv.x;
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                half sb = SbAt(suv);
                clip(sb - 0.5h);

                float3 wp = i.positionWS;
                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                float aa = max(length(fwidth(wp)) * 0.75, 1e-4);
                float pen = aa * 0.9 + 0.004;
                float3 L = normalize(_SbLightDir.xyz);
                int kind = (int)(i.kind + 0.5);

                half3 base = i.color.rgb;
                float big = PixNoise(wp * 0.6);
                float ndl = dot(n, L) + (big - 0.5) * (kind == 2 ? 0.8 : 0.25);
                half lit = (half)smoothstep(-0.05, 0.25, ndl);
                half3 shade = base * half3(0.62h, 0.68h, 0.74h);       // a cool shade wash
                half3 c = lerp(shade, base, lit);
                half ink = 0;

                if (kind == 1)
                {
                    // Tile courses round a cone / along a roof slope.
                    float row = frac(wp.y / 0.16);
                    ink = max(ink, SbPen((row - 0.5) * 0.16, pen * 0.6, aa) * 0.7h);
                    float ang = atan2(wp.z - unity_ObjectToWorld._m23, wp.x - unity_ObjectToWorld._m03);
                    float jx = frac(ang * 4.0 + floor(wp.y / 0.16) * 0.5);
                    ink = max(ink, SbPen((jx - 0.5) * 0.12, pen * 0.45, aa) * 0.4h);
                    c *= lerp(0.92h, 1.06h, (half)PixHash(float3(floor(wp.y / 0.16), floor(ang * 4.0), 3.0)));
                }
                else if (kind == 2)
                {
                    // Leaves: dark dabs, light flecks.
                    half dab = (half)smoothstep(0.55, 0.72, PixNoise(wp * 3.2));
                    c = lerp(c, shade * 0.8h, dab * 0.7h);
                    c = lerp(c, base * 1.15h, (half)step(0.8, PixNoise(wp * 5.0 + 7.0)) * lit * 0.6h);
                    ink = (half)(PixNoise(wp * 4.4 + 2.0) > 0.78 ? 0.5 : 0.0);
                }
                else if (kind == 0)
                {
                    // Stone / plaster: a few block lines.
                    float row = frac(wp.y / 0.22);
                    half keep = (half)step(0.45, PixNoise(wp * 2.0));
                    ink = max(ink, SbPen((row - 0.5) * 0.22, pen * 0.5, aa) * keep * 0.5h);
                }
                // Hatching in the shade.
                ink = max(ink, SbHatch(float2(dot(wp.xz, float2(0.7, 0.7)), wp.y), 9.0, aa, pen * 0.5) * (1.0h - lit) * 0.7h);

                c *= SbWash(wp * 0.8);
                half3 inkCol = lerp(_SbInk.rgb, c * 0.35h, 0.2h);
                c = lerp(c, inkCol, saturate(ink) * _SbInk.a);
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
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(Wave(v.positionOS.xyz, v.color.a)); return o; }
            half frag(Varyings i) : SV_Target { clip(SbAt(GetNormalizedScreenSpaceUV(i.positionCS)) - 0.5h); return i.positionCS.z; }
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(Wave(v.positionOS.xyz, v.color.a));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(SbAt(GetNormalizedScreenSpaceUV(i.positionCS)) - 0.5h);
                return half4(NormalizeNormalPerPixel(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
