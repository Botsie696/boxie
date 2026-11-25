// Voxel checkpoint: a square pixel particle (the drifting block bits round the camera and the bits the runner kicks up).
// A flat square with a darker one-pixel border, colour from the particle. Only where the voxel look is in force.
Shader "VoxelCheckpoint/Spark"
{
    Properties { }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "Spark"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/VoxelCheckpoint/Shaders/VoxelCommon.hlsl"

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
                half vx = VxAt(GetNormalizedScreenSpaceUV(i.positionCS));
                if (vx < 0.001h) return 0;
                float2 d = abs(i.uv * 2.0 - 1.0);
                float m = max(d.x, d.y);
                half cov = (half)step(m, 0.8);
                half3 c = i.color.rgb;
                if (m > 0.55) c *= 0.6h;
                else if (i.uv.x < 0.4 && i.uv.y > 0.6) c = c * 1.25h + 0.05h;
                half a = cov * i.color.a * vx;
                return half4(c * a, a);
            }
            ENDHLSL
        }
    }
}
