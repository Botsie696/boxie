// Additive glow for TurnFaceArrows: a baked "bloom" so the chevrons glow without post-processing.
// Texture: A = coverage (stroke + soft halo), G = how close to the stroke's centre (the hot core).
Shader "Runner/ChevronGlow"
{
    Properties
    {
        _MainTex ("Chevron", 2D) = "white" {}
        [HDR] _Color ("Glow Colour", Color) = (1, 0.78, 0.15, 1)
        [HDR] _CoreColor ("Core Colour", Color) = (1, 0.97, 0.8, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1.4
        // Always (8) when the face points away from the camera, so the hint shows through the cube.
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+200" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest [_ZTest]
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _Color;
            half4 _CoreColor;
            half _Intensity;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; UNITY_FOG_COORDS(1) };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 t = tex2D(_MainTex, i.uv);
                half3 c = lerp(_Color.rgb, _CoreColor.rgb, t.g) * (t.a * i.color.a * _Intensity);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, half4(0, 0, 0, 0));
                return half4(c, 1);
            }
            ENDCG
        }
    }
}
