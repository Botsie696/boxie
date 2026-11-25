// Soft additive dot for the rising sparks in "MainScene prop camera 4". Particle vertex colour x
// _Color, round falloff from the quad's UVs - no texture, so nothing to strip from a build.
Shader "PropLooks/Mote"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 0.85, 0.4, 1)
        _Softness ("Softness", Range(0.05, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
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
                half _Softness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half d = length(i.uv - 0.5) * 2.0;
                half a = saturate(1.0 - smoothstep(1.0 - _Softness, 1.0, d));
                a *= i.color.a;
                return half4(_Color.rgb * i.color.rgb * a, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
