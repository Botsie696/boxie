// Pixel checkpoint: the blue fireflies. A hard-edged square per particle (no round glow) with a brighter square core,
// coloured by the particle's vertex colour, added on top. Only where the pixel look is in force (PixelCommon.hlsl).
Shader "PixelCheckpoint/Mote"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 4)) = 1.2
        _Core ("Core Size", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "PixelMote"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/PixelCheckpoint/Shaders/PixelCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Intensity, _Core;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half pix = PixAt(GetNormalizedScreenSpaceUV(i.positionCS));
                if (pix < 0.001h) discard;
                float2 q = abs(i.uv - 0.5) * 2.0;
                half core = step(max(q.x, q.y), _Core);
                half3 c = i.color.rgb * lerp(0.55h, 1.0h, core) + core * 0.25h;
                return half4(c * i.color.a * _Intensity * pix, 0);
            }
            ENDHLSL
        }
    }
}
