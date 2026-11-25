// Draws only the parts of the runner that are hidden behind the rail (ZTest Greater), as a flat glowing
// silhouette. When the runner has rolled onto a face the camera cannot see (the far side or underneath),
// this is what keeps it findable. Used by PropLookController's runtime "RunnerXRay" child; the visible
// parts of the cube are untouched.
Shader "PropLooks/RunnerXRay"
{
    Properties
    {
        _Color ("Silhouette Colour", Color) = (0.25, 0.95, 0.9, 1)
        _Alpha ("Silhouette Opacity", Range(0, 1)) = 0.85
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Geometry+100" }

        Pass
        {
            Name "XRay"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Greater
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Alpha;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                // Nudged toward the camera, so the cube's own visible faces never win the "behind" test
                // against themselves (z-fighting): only a rail genuinely in front of the runner does.
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz) + UNITY_MATRIX_V[2].xyz * 0.06;
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Top a touch lighter than the sides so the silhouette still reads as a cube.
                half shade = 0.78h + 0.22h * saturate(normalize(i.normalWS).y * 0.5h + 0.5h);
                return half4(_Color.rgb * shade, _Alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
