// Additive, unlit world glow for the tutorial (roll arc, sparks, ring burst, hand-over wave, hint strips).
// The bloom is baked into the falloff, like Runner/ChevronGlow, so no post-processing is needed.
// Shape comes from the UVs, not a texture:
//   _Mode 0  ribbon: glow across uv.y, revealed along uv.x up to _Progress (the roll arc and its arrowhead)
//   _Mode 1  round dot (particles)
//   _Mode 2  ring (ring burst)
//   _Mode 3  strip: glow across uv.x, soft at both ends of uv.y (lanes, the hand-over wave, hint strips)
// _ZTest / _Alpha let one mesh be drawn twice: once depth-tested, once ZTest Greater at low alpha so the part
// that goes behind the beam stays faintly visible.
Shader "Tutorial/WorldGlow"
{
    Properties
    {
        _MainTex ("Unused", 2D) = "white" {}
        [HDR] _Color ("Glow Colour", Color) = (1, 0.79, 0.29, 1)
        [HDR] _CoreColor ("Core Colour", Color) = (1, 0.97, 0.85, 1)
        _Intensity ("Intensity", Range(0, 6)) = 2
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Pulse ("Pulse", Range(0, 3)) = 1
        _Progress ("Progress (uv.x reveal)", Float) = 2
        _Mode ("Mode", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+210" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest [_ZTest]
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            half4 _Color;
            half4 _CoreColor;
            half _Intensity;
            half _Alpha;
            half _Pulse;
            float _Progress;
            float _Mode;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; UNITY_FOG_COORDS(1) };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float d;
                float ends = 1.0;
                float reveal = 1.0;

                if (_Mode < 0.5)
                {
                    d = abs(uv.y - 0.5) * 2.0;
                    reveal = saturate((_Progress - uv.x) / 0.04 + 0.5);
                }
                else if (_Mode < 1.5)
                {
                    d = length(uv - 0.5) * 2.0;
                }
                else if (_Mode < 2.5)
                {
                    d = saturate(abs(length(uv - 0.5) * 2.0 - 0.82) / 0.18);
                }
                else
                {
                    d = abs(uv.x - 0.5) * 2.0;
                    ends = smoothstep(0.0, 0.18, uv.y) * smoothstep(1.0, 0.82, uv.y);
                }

                // Hot core, tight halo, wide halo; fades to nothing at the quad's edge.
                float core = saturate(1.0 - d / 0.38);
                core *= core;
                float tight = exp(-d * d * 9.0) * 0.55;
                float wide = exp(-d * d * 2.5) * 0.25;
                float edge = saturate((1.0 - d) * 5.0);
                float cover = saturate(core + tight + wide) * edge * ends * reveal;

                half3 c = lerp(_Color.rgb, _CoreColor.rgb, core) * i.color.rgb * (cover * i.color.a * _Intensity * _Pulse * _Alpha);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, half4(0, 0, 0, 0));
                return half4(c, 1);
            }
            ENDCG
        }
    }
}
