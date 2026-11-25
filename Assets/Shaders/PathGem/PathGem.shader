// The diamond that floats over a corner of the path (see DynamicPathGeneratorGems.cs).
//
// A hollow outline, drawn on one camera-facing quad: the rhombus is |u| + |v| = 1 in the quad's own
// UVs, so there is no geometry to it and no texture to sample. The line is anti-aliased against the
// pixel footprint and never allowed to fall below a pixel, so the marker keeps the same crisp
// outline whether it is close up or far down the track.
Shader "Custom/PathGem"
{
    Properties
    {
        [HDR] _Color("Colour", Color) = (1, 0.85, 0.45, 1)
        _Thickness("Outline thickness", Range(0.005, 0.4)) = 0.07
        _Glow("Bleed around the line", Range(0, 2)) = 0.45
        _Falloff("Bleed falloff", Range(1, 40)) = 12
        _Fill("Inside fill", Range(0, 1)) = 0
        _Fade("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Name "Gem"
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
                half _Thickness;
                half _Glow;
                half _Falloff;
                half _Fill;
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

                float2 p = input.uv * 2.0 - 1.0;
                float d = abs(p.x) + abs(p.y);     // 1 exactly on the rhombus
                float dist = abs(d - 1.0);

                // One pixel's worth of d, so the line is smooth and never thinner than a pixel.
                float aa = fwidth(d) + 1e-5;
                float half_width = max(_Thickness, aa);
                float line_ = 1.0 - smoothstep(half_width, half_width + aa, dist);

                float bleed = exp(-dist * _Falloff) * _Glow;
                float fill = saturate(1.0 - d) * _Fill;

                half a = saturate(line_ + bleed + fill) * _Color.a * _Fade;
                return half4(_Color.rgb * a, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
