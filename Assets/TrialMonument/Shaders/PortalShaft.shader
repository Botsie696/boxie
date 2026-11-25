// A soft light shaft for the EXIT tower's doorway (PortalDoor, MainScene prop): a long ribbon that is
// brightest at the door, thins out along its length and is soft at its sides, with a slow shimmer.
// uv.x runs across the ribbon, uv.y along it.
//
// Unlike TrialMonument/PortalRay (purely additive, so it vanishes on a pale sky) this writes premultiplied
// colour with SOME coverage: the shaft adds light AND tints what is behind it, so a warm ray stays
// readable against a bright background.
Shader "TrialMonument/PortalShaft"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 0.72, 0.36, 1)
        _Fade ("Fade", Range(0, 2)) = 1
        _Falloff ("Falloff", Range(0.3, 4)) = 1.1
        _Cover ("Coverage (0 = additive, 1 = alpha)", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Shaft"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Fade;
                half _Falloff;
                half _Cover;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; return o; }
            half4 frag(Varyings i) : SV_Target
            {
                float along = i.uv.y;
                float across = 1.0 - abs(i.uv.x * 2.0 - 1.0);
                float body = pow(saturate(1.0 - along), _Falloff) * smoothstep(0.0, 0.05, along);
                float edge = smoothstep(0.0, 0.85, across);
                float shimmer = 0.82 + 0.18 * sin(_Time.y * 2.1 + along * 8.0 + i.uv.x * 4.0);
                float a = saturate(body * edge * shimmer * _Fade);
                return half4(_Color.rgb * a, a * _Cover);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
