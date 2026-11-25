Shader "Inside/SpotlightCone"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.95, 0.7, 0.18)
        _FalloffPow ("Falloff Power", Float) = 1.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100
        Cull Front
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 localPos : TEXCOORD0; float3 localNorm : TEXCOORD1; };

            fixed4 _Color;
            float _FalloffPow;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.localPos = v.vertex.xyz;
                o.localNorm = v.normal;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Fade from tip (y=1) to base (y=0)
                float t = saturate(1.0 - i.localPos.y);
                float alpha = _Color.a * pow(t, _FalloffPow);
                return fixed4(_Color.rgb, alpha);
            }
            ENDCG
        }
    }
}