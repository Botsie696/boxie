// Pencil checkpoint: the runner (totem body, eye, cape) while the pencil look is in force - PencilCheckpointController
// swaps these materials in and puts the originals back. A hollow drawing: bare paper, hatching only on the side away
// from the camera-relative light, a scribble shadow line low on the body; the outline comes from the pencil line pass
// (DepthNormals written here). _Tone darkens a part (pupil = solid graphite, cape = mid hatching).
Shader "PencilCheckpoint/Runner"
{
    Properties
    {
        _Tone ("Tone (0 paper .. 1 graphite)", Range(0, 1)) = 0.1
        _Solid ("Solid Graphite", Range(0, 1)) = 0
        _Spacing ("Hatch Spacing (object units)", Float) = 0.07
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/PencilCheckpoint/Shaders/PencilCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _Tone, _Solid, _Spacing;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 normalOS : TEXCOORD2; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.positionOS = v.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.normalOS = v.normalOS;
                return o;
            }
            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                float3 an = abs(i.normalOS);
                float2 q = an.x > an.y && an.x > an.z ? i.positionOS.zy : an.y > an.z ? i.positionOS.xz : i.positionOS.xy;
                float aa = max(length(fwidth(q)), 1e-5) * 0.75;
                half lit = PcLit(n);
                half tone = saturate(_Tone + (1.0h - lit) * 0.55h - 0.1h);
                float s = max(_Spacing, 0.005);
                half ink = PcTone(q / s, tone, 1.0, 0.13, aa / s, 7.0);
                ink = max(ink, (half)_Solid * (half)lerp(0.8, 0.95, PixHash(float3(floor(q / s * 4.0), 1.0))));
                return half4(PcInk(ink), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
