// Toon checkpoint: the valley floor the cliffs stand on. A wide disc under the track (ToonCheckpointController keeps it
// below the lowest track in view and raises it as the route climbs) painted as cartoon meadow: big soft patches in three
// flat greens, short grass strokes, the track's cast shadow in cool teal. It fades out at its rim into the sky and takes
// the same chasm fog as the rocks, so cliffs and ground meet in one mist. Visible only where the toon look is in force.
Shader "ToonCheckpoint/Ground"
{
    Properties
    {
        _GrassLight ("Grass Light", Color) = (0.55, 0.78, 0.45, 1)
        _GrassMid ("Grass Mid", Color) = (0.35, 0.62, 0.38, 1)
        _GrassDark ("Grass Dark", Color) = (0.2, 0.44, 0.33, 1)
        _ShadowColor ("Cast Shadow", Color) = (0.24, 0.42, 0.45, 1)
        _ShadowStrength ("Cast Shadow Strength", Range(0, 1)) = 0.6
        _PatchScale ("Patch Size (world units)", Float) = 26
        _StrokeDensity ("Grass Strokes per Unit", Float) = 0.9
        _Radius ("Rim Fade: start / end (share of the disc radius)", Vector) = (0.45, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-400" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Ground"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/PropCameraLooks/LookTransition/Shaders/LookChasmFog.hlsl"
            #include "Assets/ToonCheckpoint/Shaders/ToonCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GrassLight, _GrassMid, _GrassDark, _ShadowColor;
                half _ShadowStrength;
                float _PatchScale, _StrokeDensity;
                float4 _Radius;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                half toon = ToonAt(suv);
                if (toon < 0.001h) discard;
                float3 wp = i.positionWS;

                // Meadow: three flat greens in big soft-edged patches.
                float n = ToonNoise(wp / _PatchScale) * 0.7 + ToonNoise(wp / (_PatchScale * 0.37) + 4.1) * 0.3;
                float aa = fwidth(n) + 1e-4;
                half3 c = lerp(_GrassDark.rgb, _GrassMid.rgb, smoothstep(0.42 - aa, 0.42 + aa, n));
                c = lerp(c, _GrassLight.rgb, smoothstep(0.6 - aa, 0.6 + aa, n));

                // Short grass strokes (dashes along x, world anchored).
                float gx = wp.x * _StrokeDensity, gz = wp.z * _StrokeDensity * 3.0;
                float cell = ToonHash(float3(floor(gx), floor(gz), 3.0));
                float fx = frac(gx), fz = frac(gz);
                half stroke = step(0.72, cell) * step(abs(fz - 0.5), 0.12) * step(abs(fx - 0.5), 0.3);
                c *= 1.0h - stroke * 0.18h;

                // The track's shadow, in cool teal.
                float4 sc = TransformWorldToShadowCoord(wp);
                Light sun = GetMainLight(sc);
                c = lerp(c, c * _ShadowColor.rgb * 1.6h, (1.0h - sun.shadowAttenuation) * _ShadowStrength);

                // Paper grain.
                c *= 1.0h + (ToonNoise(wp * 1.9) - 0.5h) * _ToonLightDir.w;

                // Same mist as the rocks.
                c = ApplyChasmFog(c, wp, i.positionCS);

                float d = length(wp.xz - _ToonGround.xz) / max(_ToonGround.w, 1.0);
                half rim = 1.0h - smoothstep(_Radius.x, _Radius.y, d);
                return half4(c, rim * toon);
            }
            ENDHLSL
        }
    }
}
