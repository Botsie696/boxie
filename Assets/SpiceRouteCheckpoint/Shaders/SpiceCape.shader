// Spice Route checkpoint: the runner's cape (RunnerCape.cs mesh, uv.x across, uv.y 0 shoulders .. 1 hem) while the spice
// look is in force. Silk with a gold zari border and small woven motifs, shaded like DepthCues37/RunnerCape:
//   India    a rani-pink dupatta - gold border with a scalloped inner edge, small gold buttis, a saffron lining
//   Arabia   an indigo / turquoise silk - gold border, small eight-point stars, a turquoise lining
// The half follows the environment (SrEnvHalf). Two-sided, matte with a soft silk sheen across the folds.
Shader "SpiceRouteCheckpoint/Cape"
{
    Properties
    {
        _TopShade ("Facing up", Range(0, 1.5)) = 1
        _LitShade ("Lit side", Range(0, 1)) = 0.72
        _ShadeShade ("Shade side", Range(0, 1)) = 0.5
        _BottomShade ("Facing down", Range(0, 1)) = 0.34
        _ShoulderShade ("Shade at the shoulders", Range(0, 1)) = 0.35
        _Brightness ("Brightness", Range(0.2, 1.5)) = 1
        _Sheen ("Silk Sheen", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Cape"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/SpiceRouteCheckpoint/Shaders/SpiceCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _TopShade, _LitShade, _ShadeShade, _BottomShade, _ShoulderShade, _Brightness, _Sheen;
            CBUFFER_END
            float4 _DCPlayer;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float2 uv : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                float2 uv = i.uv;
                float aa = max(fwidth(uv.x) + fwidth(uv.y), 1e-4);
                half h = SrEnvHalf();

                half3 silkI = SrLin(half3(0.9h, 0.22h, 0.47h)), liningI = SrLin(half3(0.96h, 0.58h, 0.16h));
                half3 silkA = SrLin(half3(0.12h, 0.17h, 0.5h)), liningA = SrLin(half3(0.12h, 0.6h, 0.66h));
                half3 gold = SrGold(uv * 3.0, 0.85h);

                // Distance to the border (sides and hem, not the shoulders).
                float edge = min(min(uv.x, 1.0 - uv.x) * 1.6, 1.0 - uv.y);
                half border = SrFill(edge - 0.14, aa);
                float scal = edge - 0.14 - 0.03 * abs(sin(uv.y * 40.0 + uv.x * 40.0));

                half3 outI = silkI;
                float2 bq = frac(uv * float2(4.0, 5.0)) - 0.5;
                outI = lerp(outI, gold, SrFill(SrPetals(bq, 5.0, 0.16, 0.5, 1.5708) / 4.0, aa) * (1.0h - border));
                outI = lerp(outI, gold, border);
                outI = lerp(outI, SrLin(half3(0.98h, 0.62h, 0.2h)), SrLine(scal, 0.008, aa));
                half3 outA = silkA;
                outA = lerp(outA, gold, SrFill(SrStar8(bq, 0.16) / 4.0, aa) * (1.0h - border));
                outA = lerp(outA, gold, border);
                outA = lerp(outA, liningA, SrLine(edge - 0.16, 0.01, aa));

                half3 col = front ? lerp(outI, outA, h) : lerp(liningI, liningA, h);

                float3 camRight = normalize(UNITY_MATRIX_V[0].xyz);
                float3 flat = n - float3(0, n.y, 0);
                float side = dot(flat, camRight) / max(length(flat), 1e-4) * (_DCPlayer.x < 0 ? -1.0 : 1.0);
                half sideTone = lerp(_ShadeShade, _LitShade, smoothstep(-0.35, 0.35, side));
                half up = smoothstep(0.2, 0.9, n.y);
                half down = smoothstep(0.2, 0.9, -n.y);
                half tone = lerp(lerp(sideTone, _TopShade, up), _BottomShade, down);
                tone *= 1.0 - _ShoulderShade * (1.0 - smoothstep(0.0, 0.35, uv.y));
                half sheen = (half)pow(saturate(1.0 - abs(dot(n, normalize(UNITY_MATRIX_V[2].xyz)))), 3.0) * _Sheen;
                return half4(col * tone * _Brightness + col * sheen, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };
            V vertDepth(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half fragDepth(V i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
    }
    FallBack Off
}
