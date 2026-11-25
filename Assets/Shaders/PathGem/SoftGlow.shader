// A soft additive halo on a single camera-facing quad: the glow around the path's corner diamonds
// (DynamicPathGeneratorGems.cs) and around the lit windows of the background houses
// (NeonWindowDecorator.cs). The falloff is computed from the quad's UV, so there is no texture to
// sample and no post-processing needed for things to look like they give off light.
Shader "Custom/SoftGlow"
{
    Properties
    {
        [HDR] _Color("Colour", Color) = (1, 0.8, 0.45, 1)
        _Falloff("Falloff", Range(0.5, 8)) = 2.4
        _Core("Core size", Range(0, 0.9)) = 0.12
        _Fade("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Falloff;
                half _Core;
                half _Fade;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half d = saturate(length(input.uv * 2.0h - 1.0h));
                half g = saturate((1.0h - d) / max(1.0h - _Core, 1e-3h));
                g = pow(g, _Falloff);
                half3 rgb = _Color.rgb * (g * _Color.a * _Fade);
                return half4(rgb, g);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
