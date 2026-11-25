// A soft additive light shaft (TrialMonument2 zone doors): a long strip that is brightest at the door
// and fades along its length, soft at its sides, with a slow shimmer. uv.y runs along the shaft.
Shader "TrialMonument/PortalRay"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 0.7, 0.5, 1)
        _Fade ("Fade", Range(0, 2)) = 1
        _Falloff ("Falloff", Range(0.5, 4)) = 1.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Ray"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
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
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; return o; }
            half4 frag(Varyings i) : SV_Target
            {
                float along = i.uv.y;
                float across = 1.0 - abs(i.uv.x * 2.0 - 1.0);
                float body = pow(saturate(1.0 - along), _Falloff) * smoothstep(0.0, 0.06, along);
                float edge = pow(saturate(across), 0.9);
                float shimmer = 0.85 + 0.15 * sin(_Time.y * 2.3 + along * 9.0 + i.uv.x * 5.0);
                return half4(_Color.rgb * (body * edge * shimmer * _Fade), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
