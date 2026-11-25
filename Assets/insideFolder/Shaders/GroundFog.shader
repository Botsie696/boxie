Shader "Inside/GroundFog"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0.15, 0.17, 0.2, 0.55)
        _NoiseScale ("Noise Scale", Float) = 3.0
        _Speed ("Speed", Float) = 0.02
    }
    SubShader
    {
        Tags { "Queue"="Transparent+1" "RenderType"="Transparent" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            fixed4 _FogColor;
            float _NoiseScale;
            float _Speed;

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p); float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i+float2(1,0)), u.x),
                            lerp(hash(i+float2(0,1)), hash(i+float2(1,1)), u.x), u.y);
            }

            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv * _NoiseScale + float2(_Time.y * _Speed, _Time.y * _Speed * 0.7);
                float n = noise(uv) * 0.5 + noise(uv * 2.1 + 0.5) * 0.3 + noise(uv * 4.3) * 0.2;
                float alpha = _FogColor.a * n;
                // edge fade
                float2 edge = abs(i.uv - 0.5) * 2.0;
                float edgeFade = 1.0 - pow(max(edge.x, edge.y), 2.0);
                alpha *= edgeFade;
                return fixed4(_FogColor.rgb, saturate(alpha));
            }
            ENDCG
        }
    }
}