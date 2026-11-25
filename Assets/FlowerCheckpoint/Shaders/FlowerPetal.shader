// Flower checkpoint: a drifting petal (particles from FlowerCheckpointController - the falling petals round the camera and
// the petals shed behind the runner). A flat painted petal: an almond with a notched tip, a darker base, a light stroke
// down the middle. Colour from the particle colour. Only where the flower look is in force (follows the look wipe).
Shader "FlowerCheckpoint/Petal"
{
    Properties { }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "Petal"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/FlowerCheckpoint/Shaders/FlowerCommon.hlsl"

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half fl = FlAt(GetNormalizedScreenSpaceUV(i.positionCS));
                if (fl < 0.001h) return 0;
                float2 p = i.uv * 2.0 - 1.0;                       // -1..1
                // Almond: two arcs meeting at the ends, a notch at the tip (+y).
                float w = 0.62 * (1.0 - p.y * p.y);
                float notch = 0.18 * saturate(1.0 - abs(p.x) / 0.18) * step(0.6, p.y);
                float d = abs(p.x) - w;
                float aa = fwidth(d) + 1e-4;
                half cov = (half)(1.0 - smoothstep(-aa, aa, d)) * (half)step(p.y, 0.98 - notch);
                half3 c = i.color.rgb;
                c *= lerp(0.75h, 1.08h, (half)saturate(p.y * 0.5 + 0.5));      // darker toward the base
                c = lerp(c, c * 1.25h + 0.05h, (half)(1.0 - smoothstep(0.04, 0.09, abs(p.x))) * 0.5h);
                half a = cov * i.color.a * fl;
                return half4(c * a, a);
            }
            ENDHLSL
        }
    }
}
