Shader "Inside/WindowGlow"
{
    Properties
    {
        _Color ("Color", Color) = (0.3, 0.35, 0.5, 1)
        _GlowColor ("Glow Color", Color) = (0.4, 0.45, 0.6, 1)
        _GlowStrength ("Glow Strength", Float) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            fixed4 _Color;
            fixed4 _GlowColor;
            float _GlowStrength;

            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                // Subtle pane divisions
                float2 pane = frac(i.uv * float2(3.0, 4.0));
                float frameX = step(pane.x, 0.06) + step(0.94, pane.x);
                float frameY = step(pane.y, 0.06) + step(0.94, pane.y);
                float frame = saturate(frameX + frameY);
                fixed4 col = lerp(_GlowColor, _Color * _GlowStrength, 0.5);
                col = lerp(col, _Color * 0.1, frame);
                return col;
            }
            ENDCG
        }
    }
}