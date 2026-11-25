Shader "Custom/PowerUpFlat"
{
    // Unlit, flat-shaded pickup body: a per-face tint from the face normal (the vertices are not shared,
    // so every face is one brightness, like the totem blocks) plus a soft rim glow.
    Properties
    {
        [HDR] _Color("Colour", Color) = (1, 1, 1, 1)
        [HDR] _Rim("Rim glow", Color) = (1, 1, 1, 1)
        _RimPower("Rim power", Range(0.5, 6)) = 2.5
        _RimAmount("Rim amount", Range(0, 2)) = 0.35
        _Glow("Extra glow", Range(0, 2)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+5" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _Rim;
                float _RimPower;
                float _RimAmount;
                float _Glow;
            CBUFFER_END
            struct A { float4 p : POSITION; float3 n : NORMAL; };
            struct V { float4 p : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; };
            V vert(A a)
            {
                V o;
                o.p = TransformObjectToHClip(a.p.xyz);
                o.n = TransformObjectToWorldNormal(a.n);
                o.v = GetWorldSpaceViewDir(TransformObjectToWorld(a.p.xyz));
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 v = normalize(i.v);
                float l = 0.58 + 0.42 * saturate(dot(n, normalize(float3(0.35, 0.85, 0.4))));
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower) * _RimAmount;
                float3 c = _Color.rgb * (l + _Glow) + _Rim.rgb * rim;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
