
// Particles for RunnerTrailFX.cs: a soft four-point twinkle (_Shape 0) or a thin shockwave ring (_Shape 1).
// Additive and procedural (no texture). Colour and fade come from the particle's vertex colour, which the
// script already gives in linear space.
Shader "Runner/TrailFX Spark"
{
    Properties
    {
        [Enum(Spark, 0, Ring, 1)] _Shape ("Shape", Float) = 0
        _Intensity ("Intensity", Range(0, 4)) = 1
        _RingWidth ("Ring Width", Range(0.02, 0.5)) = 0.1
        _RayStrength ("Twinkle Rays", Range(0, 2)) = 0.8
        _CameraPull ("Pull Toward Camera", Range(0, 3)) = 0.8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "RunnerTrailSpark"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Shape;
                half _Intensity;
                half _RingWidth;
                half _RayStrength;
                float _CameraPull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS += GetWorldSpaceNormalizeViewDir(positionWS) * _CameraPull;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half2 p = input.uv * 2.0 - 1.0;
                half d = length(p);
                half edge = saturate(1.0 - d);
                half v;
                if (_Shape > 0.5)
                {
                    half r = abs(d - 0.78) / _RingWidth;
                    v = saturate(1.0 - r);
                    v = (v * v + 0.12 * edge * edge) * saturate(edge * 8.0);
                }
                else
                {
                    half2 a = abs(p);
                    half rays = exp(-a.x * 16.0) * saturate(1.0 - a.y) + exp(-a.y * 16.0) * saturate(1.0 - a.x);
                    v = (exp(-d * d * 10.0) + rays * _RayStrength * 0.6) * edge;
                }
                return half4(input.color.rgb * (v * input.color.a * _Intensity), 0.0);
            }
            ENDHLSL
        }
    }
}
