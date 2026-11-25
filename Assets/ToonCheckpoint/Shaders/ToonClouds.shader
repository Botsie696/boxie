// Toon checkpoint: painted cartoon clouds in the sky (the reference's long wispy clouds - cream tops, teal undersides,
// a bright rim). Drawn as a screen-filling quad pushed to the far plane, after the sky and before the transparents, with
// a depth test - so it only ever shows where nothing else was drawn. Visible only where the toon look is in force
// (ToonCommon.hlsl, follows the look wipe). Driven by ToonCheckpointController.
Shader "ToonCheckpoint/Clouds"
{
    Properties
    {
        _LitColor ("Cloud Top", Color) = (0.92, 0.97, 0.88, 1)
        _ShadeColor ("Cloud Underside", Color) = (0.56, 0.81, 0.77, 1)
        _RimColor ("Rim", Color) = (1, 1, 0.96, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.95
        _Cover ("Cover (more = more cloud)", Range(0, 1)) = 0.42
        _Scale ("Scale", Float) = 3.4
        _Low ("Lowest Cloud (screen height)", Range(0, 1)) = 0.5
        _High ("Full Cover From (screen height)", Range(0, 1)) = 0.85
        _Drift ("Drift (screen widths per second)", Float) = 0.004
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+501" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "Clouds"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/ToonCheckpoint/Shaders/ToonCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _LitColor, _ShadeColor, _RimColor;
                half _Opacity, _Cover, _Low, _High;
                float _Scale, _Drift;
            CBUFFER_END
            float4 _ToonCloudOffset;   // x: offset from the camera's yaw (screen widths), y: unused

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float2 p = v.uv * 2.0 - 1.0;
                #if UNITY_REVERSED_Z
                    o.positionCS = float4(p, 1e-6, 1.0);
                #else
                    o.positionCS = float4(p, 1.0 - 1e-6, 1.0);
                #endif
                return o;
            }

            float Fbm(float2 p)
            {
                float a = ToonNoise(float3(p, 0.5)) * 0.55;
                a += ToonNoise(float3(p * 2.1 + 5.3, 1.5)) * 0.3;
                a += ToonNoise(float3(p * 4.3 + 1.7, 2.5)) * 0.15;
                return a;
            }

            float Density(float2 suv, float aspect)
            {
                float2 p = float2((suv.x + _ToonCloudOffset.x) * aspect + _Time.y * _Drift, suv.y);
                // Long, flat, wispy clouds: stretched sideways.
                // Long, flat bodies with lumpy, puffed edges on top.
                return Fbm(p * float2(0.55, 3.2) * _Scale) * 0.82 + Fbm(p * float2(2.4, 7.5) * _Scale + 3.1) * 0.25;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                half toon = ToonAt(suv);
                if (toon < 0.001h) discard;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                half band = smoothstep(_Low, _High, suv.y);
                float thr = 1.0 - _Cover * band;
                float dens = Density(suv, aspect);
                float aa = fwidth(dens) * 0.75 + 1e-4;   // crisp, one-pixel cartoon edge
                half body = smoothstep(thr - aa, thr + aa, dens);
                if (body < 0.002h) discard;
                // Underside in teal shade: the point a little lower is outside the cloud.
                half below = smoothstep(thr - aa, thr + aa, Density(suv - float2(0, 0.022), aspect));
                half above = smoothstep(thr - aa, thr + aa, Density(suv + float2(0, 0.008), aspect));
                half3 c = lerp(_ShadeColor.rgb, _LitColor.rgb, below);
                c = lerp(c, _RimColor.rgb, (1.0h - above) * below * 0.8h);
                return half4(c, body * band * _Opacity * toon);
            }
            ENDHLSL
        }
    }
}
