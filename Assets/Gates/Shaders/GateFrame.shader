// Triangle gate frame and its special coin (DynamicPathGeneratorGate.cs, MainScene prop camera 3.6).
//
//   Faces      Vertex colour r = 1 on the broad faces of a bar, 0 on its bevels: broad faces take _BaseColor,
//              bevels _EdgeColor, so every bar has crisp dark edges at any distance.
//   Stripes    Vertex colour g = 1 on bars that carry hazard stripes; uv.x runs along the bar (world units),
//              uv.y across it, giving diagonal bands of _StripeColor.
//   Light      Flat and bright: a little shade from the main light's direction, no PBR, so it reads like the
//              flat-face track instead of a shiny prop.
//   Glow       _EmissionColor * _Glow on the broad faces. _Glow is driven per gate (pulse, pass flash) through a
//              MaterialPropertyBlock.
Shader "Gates/GateFrame"
{
    Properties
    {
        _BaseColor ("Face Colour", Color) = (1, 0.86, 0.05, 1)
        _EdgeColor ("Bevel Colour", Color) = (0.08, 0.05, 0.03, 1)
        _StripeColor ("Stripe Colour", Color) = (0.08, 0.05, 0.03, 1)
        _StripeFreq ("Stripes per Unit", Range(0.5, 6)) = 2.2
        _StripeAmount ("Stripe Strength", Range(0, 1)) = 1
        _ShadeAmount ("Shade from Light Direction", Range(0, 1)) = 0.3
        [HDR] _EmissionColor ("Glow Colour", Color) = (1, 0.7, 0.1, 1)
        _Glow ("Glow (driven per gate)", Range(0, 4)) = 0.3
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _EdgeColor, _StripeColor, _EmissionColor;
            half _StripeFreq, _StripeAmount, _ShadeAmount, _Glow;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                half4 color : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                half face = i.color.r;

                half3 col = lerp(_EdgeColor.rgb, _BaseColor.rgb, face);

                // Diagonal hazard bands on the striped bars' broad faces.
                float band = frac((i.uv.x + i.uv.y) * _StripeFreq);
                float aa = fwidth(band) + 1e-4;
                half stripe = smoothstep(0.5 - aa, 0.5 + aa, band) * (1.0h - smoothstep(1.0 - aa, 1.0, band));
                col = lerp(col, _StripeColor.rgb, stripe * _StripeAmount * i.color.g * face);

                Light mainLight = GetMainLight();
                half lit = saturate(dot(n, mainLight.direction) * 0.5h + 0.5h);
                col *= lerp(1.0h, 0.55h + 0.45h * lit, _ShadeAmount);

                col += _EmissionColor.rgb * (_Glow * face);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v) { UNITY_SETUP_INSTANCE_ID(v); Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
