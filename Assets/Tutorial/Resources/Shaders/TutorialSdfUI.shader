// Tutorial screen shapes, drawn from signed distances so they stay crisp and anti-aliased at any
// resolution (no low-resolution sprites). One material per shape; TutorialUIController sets _Size to the
// rect's size in canvas units, so every distance below is in reference pixels.
//   _Shape 0  ghost thumb: a tapered capsule body smooth-min'd with a round tip pad, plus a faint nail
//   _Shape 1  ring (contact ripple, real-tap ripple)
//   _Shape 2  vertical seam line (fades out at its ends)
//   _Shape 3  vignette (dark at the edges, clear in the middle)
//   _Shape 4  soft disc
// _Softness widens the edge (a drop shadow is the thumb again with a large softness).
Shader "Tutorial/SdfUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Shape ("Shape", Float) = 0
        _Size ("Rect Size (px)", Vector) = (100, 100, 0, 0)
        _Softness ("Edge Softness (px)", Float) = 1.5
        _Stroke ("Stroke (px)", Float) = 2.5
        _Thumb ("Thumb: tip r, base r, length, smooth k", Vector) = (34, 28, 150, 8)
        _Nail ("Nail Lighten", Float) = 0.2

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Shape;
            float4 _Size;
            float _Softness;
            float _Stroke;
            float4 _Thumb;
            float _Nail;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            // Capsule with a different radius at each end: r1 at the origin, r2 at (0, h).
            float sdUnevenCapsule(float2 p, float r1, float r2, float h)
            {
                p.x = abs(p.x);
                float b = (r1 - r2) / h;
                float a = sqrt(saturate(1.0 - b * b));
                float k = dot(p, float2(-b, a));
                if (k < 0.0) return length(p) - r1;
                if (k > a * h) return length(p - float2(0.0, h)) - r2;
                return dot(p, float2(a, b)) - r1;
            }

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            float smin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / max(k, 1e-4));
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            // Coverage of distance d (negative inside) with an edge `soft` px wide, never sharper than a pixel.
            float Cover(float d, float soft)
            {
                float w = max(soft, fwidth(d));
                return 1.0 - smoothstep(-0.5 * w, 0.5 * w, d);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * _Size.xy;
                float alpha = 0.0;
                float shape = _Shape;

                if (shape < 0.5)
                {
                    // Thumb seen from above, tip up (+y).
                    float tipR = _Thumb.x, baseR = _Thumb.y, len = _Thumb.z, k = _Thumb.w;
                    float tipY = len * 0.5 - tipR;
                    float baseY = -len * 0.5 + baseR;
                    float body = sdUnevenCapsule(p - float2(0.0, baseY), baseR, tipR * 0.9, max(1.0, tipY - baseY));
                    float pad = length(p - float2(0.0, tipY)) - tipR;
                    float d = smin(body, pad, k);
                    alpha = Cover(d, _Softness);

                    // Nail: a rounded rectangle near the tip, a little lighter.
                    float nd = sdRoundBox(p - float2(0.0, tipY + tipR * 0.2), float2(tipR * 0.48, tipR * 0.42), tipR * 0.32);
                    float nail = Cover(nd, max(1.0, _Softness));
                    alpha *= 1.0 + _Nail * nail;
                }
                else if (shape < 1.5)
                {
                    float r = min(_Size.x, _Size.y) * 0.5 - _Stroke - _Softness - 1.0;
                    float d = abs(length(p) - r) - _Stroke * 0.5;
                    alpha = Cover(d, _Softness);
                }
                else if (shape < 2.5)
                {
                    float d = abs(p.x) - _Stroke * 0.5;
                    float ends = smoothstep(0.0, 0.15, i.uv.y) * smoothstep(1.0, 0.85, i.uv.y);
                    alpha = Cover(d, _Softness) * ends;
                }
                else if (shape < 3.5)
                {
                    float r = length((i.uv - 0.5) * 2.0);
                    alpha = smoothstep(0.55, 1.45, r);
                }
                else
                {
                    float d = length(p) - (min(_Size.x, _Size.y) * 0.5 - _Softness - 1.0);
                    alpha = Cover(d, _Softness);
                }

                fixed4 c = i.color;
                c.a *= saturate(alpha);
                return c;
            }
            ENDCG
        }
    }
}
