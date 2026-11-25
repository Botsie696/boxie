// MainScene prop camera 3.7 only: the runner's short cape (RunnerCape.cs builds and simulates the mesh every frame).
// Flat, matte, two-sided: the outside and the lining have their own colour, each shaded by which way that side of
// the cloth faces (up = lit, down = in shade, the camera-side tilt picks lit / shade like the path), so folds read
// as soft steps of tone. Darker near the shoulders (the block shades it), a touch lighter at the hem.
// uv.x across the cape, uv.y 0 at the shoulders .. 1 at the hem.
Shader "DepthCues37/RunnerCape"
{
    Properties
    {
        _OuterColor ("Outside", Color) = (0.98, 0.95, 0.88, 1)
        _LiningColor ("Lining", Color) = (0.93, 0.42, 0.34, 1)
        _HemColor ("Hem Band", Color) = (0.93, 0.42, 0.34, 1)
        _HemWidth ("Hem Band Width (share of length)", Range(0, 0.3)) = 0.1
        _TopShade ("Facing up", Range(0, 1.5)) = 1
        _LitShade ("Lit side", Range(0, 1)) = 0.72
        _ShadeShade ("Shade side", Range(0, 1)) = 0.5
        _BottomShade ("Facing down", Range(0, 1)) = 0.34
        _ShoulderShade ("Shade at the shoulders", Range(0, 1)) = 0.35
        _Brightness ("Brightness", Range(0.2, 1.5)) = 1
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

            CBUFFER_START(UnityPerMaterial)
                half4 _OuterColor, _LiningColor, _HemColor;
                half _HemWidth, _TopShade, _LitShade, _ShadeShade, _BottomShade, _ShoulderShade, _Brightness;
            CBUFFER_END
            float4 _DCPlayer;   // x = +1 the camera's right side is lit, -1 the left (DepthCueController)

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
                half3 col = front ? _OuterColor.rgb : _LiningColor.rgb;
                half hem = step(1.0 - _HemWidth, i.uv.y);
                col = lerp(col, _HemColor.rgb, hem);

                float3 camRight = normalize(UNITY_MATRIX_V[0].xyz);
                float3 flat = n - float3(0, n.y, 0);
                float side = dot(flat, camRight) / max(length(flat), 1e-4) * (_DCPlayer.x < 0 ? -1.0 : 1.0);
                half sideTone = lerp(_ShadeShade, _LitShade, smoothstep(-0.35, 0.35, side));
                half up = smoothstep(0.2, 0.9, n.y);
                half down = smoothstep(0.2, 0.9, -n.y);
                half tone = lerp(lerp(sideTone, _TopShade, up), _BottomShade, down);
                tone *= 1.0 - _ShoulderShade * (1.0 - smoothstep(0.0, 0.35, i.uv.y));
                return half4(col * tone * _Brightness, 1);
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
