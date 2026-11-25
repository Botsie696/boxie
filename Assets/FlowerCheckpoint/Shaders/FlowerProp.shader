// Flower checkpoint: the props made by FlowerCheckpointController - blossom trees and shrubs on the cliff tops, the
// flower vines hanging under the legs. Pigment in the vertex colour (linear), the kind of surface in uv.x (0 trunk /
// stem, 2 blossom / foliage, 4 hanging vine), the wind sway weight in the vertex alpha. Painted like the rest of the
// look: lit / shade in three flat values from the camera-relative light, brush dabs, a lighter loaded-brush fleck.
// Hanging vines are cut away round the runner (_FlRunner) so they never cover it. Only drawn where the flower look is in
// force (follows the look wipe); takes the chasm fog like the rocks.
Shader "FlowerCheckpoint/Prop"
{
    Properties
    {
        _SwaySpeed ("Sway Speed", Float) = 1.6
        _SwayAmount ("Sway Amount", Float) = 0.12
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/FlowerCheckpoint/Shaders/FlowerCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _SwaySpeed, _SwayAmount;
        CBUFFER_END

        float3 SwayWS(float3 wp, float w)
        {
            float t = _Time.y * _SwaySpeed;
            float ph = wp.x * 0.35 + wp.z * 0.27;
            wp.x += sin(t + ph) * _SwayAmount * w;
            wp.z += sin(t * 0.8 + ph * 1.3 + 1.7) * _SwayAmount * 0.6 * w;
            return wp;
        }

        bool CutForRunner(float3 wp, float kind)
        {
            return kind > 3.5 && _FlRunner.w > 0.0 && distance(wp, _FlRunner.xyz) < _FlRunner.w;
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
                o.positionWS = SwayWS(TransformObjectToWorld(v.positionOS.xyz), v.color.a);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.kind = v.uv.x;
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                clip(FlAt(suv) - 0.5h);
                float3 wp = i.positionWS;
                if (CutForRunner(wp, i.kind)) discard;

                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                float3 L = normalize(_FlLightDir.xyz);
                int kind = (int)(i.kind + 0.5);
                half3 base = i.color.rgb;
                float big = PixNoise(wp * 0.8);
                float ndl = dot(n, L) + (big - 0.5) * (kind >= 2 ? 0.9 : 0.3);
                half lit = FlBands((half)smoothstep(-0.35, 0.45, ndl), 3.0h);
                half3 shade = base * half3(0.6h, 0.62h, 0.78h);      // a cool violet shade
                half3 c = lerp(shade, base, lit);
                if (kind >= 2)
                {
                    // Blossom: dark dabs between the flowers, lighter flecks on the lit side.
                    half dab = (half)smoothstep(0.58, 0.7, PixNoise(wp * 3.4));
                    c = lerp(c, shade * 0.8h, dab * 0.6h);
                    c = lerp(c, base * 1.25h + 0.04h, (half)step(0.78, PixNoise(wp * 5.5 + 7.0)) * lit * 0.7h);
                }
                else
                {
                    c *= 0.9h + 0.2h * (half)PixNoise(float3(wp.xz * 6.0, wp.y * 0.8));   // bark strokes
                }
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
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 wp : TEXCOORD0; float kind : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.wp = SwayWS(TransformObjectToWorld(v.positionOS.xyz), v.color.a);
                o.positionCS = TransformWorldToHClip(o.wp);
                o.kind = v.uv.x;
                return o;
            }
            half frag(Varyings i) : SV_Target
            {
                clip(FlAt(GetNormalizedScreenSpaceUV(i.positionCS)) - 0.5h);
                if (CutForRunner(i.wp, i.kind)) discard;
                return i.positionCS.z;
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 wp : TEXCOORD1; float kind : TEXCOORD2; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.wp = SwayWS(TransformObjectToWorld(v.positionOS.xyz), v.color.a);
                o.positionCS = TransformWorldToHClip(o.wp);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.kind = v.uv.x;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(FlAt(GetNormalizedScreenSpaceUV(i.positionCS)) - 0.5h);
                if (CutForRunner(i.wp, i.kind)) discard;
                return half4(NormalizeNormalPerPixel(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
