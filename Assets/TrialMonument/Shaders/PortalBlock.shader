// The stone of a zone-door archway (PortalDoor, MainScene prop): a flat, unlit, OPAQUE block.
//
//   - Opaque and depth-writing, so the pillars and roof hide whatever is behind them - the runner as it
//     goes into the archway is cut off by the recessed void AND by the stone around it, not blended over.
//   - Colour = _Color (the zone accent) x vertex colour, which carries a fixed per-face grey so a box
//     reads as a solid block without any lighting.
//   - _Fade (0..1) dissolves it in and out with a screen-space dither instead of alpha, so the tower can
//     grow in with the track without the depth problems of a transparent material.
//   - Cull Off: the depth buffer sorts the faces, and the built-in sphere / cylinder need no winding care.
Shader "TrialMonument/PortalBlock"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _Fade ("Fade (1 = solid)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Fade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float4 screenPos : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_Fade < 0.999)
                {
                    float2 px = i.screenPos.xy / i.screenPos.w * _ScreenParams.xy;
                    float n = frac(52.9829189 * frac(dot(px, float2(0.06711056, 0.00583715))));
                    clip(_Fade - n);
                }
                return half4(_Color.rgb * i.color.rgb, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
