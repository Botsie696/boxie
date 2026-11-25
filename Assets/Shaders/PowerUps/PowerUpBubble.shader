Shader "Custom/PowerUpBubble"
{
    // A translucent additive sphere with a hexagon-cell pattern, a brighter fresnel edge and a
    // brightness wave that sweeps over the cells. No texture, no lights.
    Properties
    {
        [HDR] _Color("Colour", Color) = (0.18, 0.83, 0.54, 1)
        _Alpha("Alpha", Range(0, 1)) = 0.25
        _Cells("Hex cells", Range(2, 20)) = 7
        _Edge("Fresnel edge", Range(0, 4)) = 1.6
        _Wave("Wave (0..1 sweeps once)", Range(-1, 2)) = -1
        _Flash("White flash", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+12" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Alpha;
                float _Cells;
                float _Edge;
                float _Wave;
                float _Flash;
            CBUFFER_END
            struct A { float4 p : POSITION; float3 n : NORMAL; };
            struct V { float4 p : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; float3 o : TEXCOORD2; };
            V vert(A a)
            {
                V o;
                o.p = TransformObjectToHClip(a.p.xyz);
                o.n = TransformObjectToWorldNormal(a.n);
                o.v = GetWorldSpaceViewDir(TransformObjectToWorld(a.p.xyz));
                o.o = normalize(a.p.xyz);
                return o;
            }
            // 0 on a hexagon-cell edge, rising to ~1 in a centre. p is in cell units (cell radius 1).
            float HexCell(float2 p)
            {
                p = abs(p);
                float d = max(dot(p, float2(0.8660254, 0.5)), p.y);
                return saturate((1.0 - d) * 4.0);
            }
            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 v = normalize(i.v);
                float fres = pow(1.0 - saturate(dot(n, v)), 2.2);
                float2 uv = float2(atan2(i.o.z, i.o.x) / 6.2831853 + 0.5, i.o.y * 0.5 + 0.5) * _Cells * float2(2.0, 1.0);
                float2 g = uv * float2(1.0, 1.7320508);
                float2 a = fmod(g, float2(3.0, 3.4641016)) - float2(1.5, 1.7320508);
                float2 b = fmod(g + float2(1.5, 1.7320508), float2(3.0, 3.4641016)) - float2(1.5, 1.7320508);
                float2 c = dot(a, a) < dot(b, b) ? a : b;
                float lines = 1.0 - HexCell(c);
                float wave = saturate(1.0 - abs((i.o.y * 0.5 + 0.5) - _Wave) * 4.0);
                float cell = 0.15 + 0.85 * lines + wave * (0.5 + lines);
                float k = _Alpha * (0.35 + cell) + fres * _Edge * _Alpha * 2.0;
                float3 col = lerp(_Color.rgb, float3(1, 1, 1), _Flash) * k * (1.0 + _Flash * 3.0);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
