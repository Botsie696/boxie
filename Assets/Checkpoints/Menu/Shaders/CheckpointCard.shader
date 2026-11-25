// MainUILR checkpoint row: one card = a live RenderTexture (the checkpoint's legs) inside a parallelogram whose left and
// right edges lean like "/" (they move right as they rise), the vertical twin of UI/CheckpointBand. Everything is computed
// in the card rect's own pixels (_RectPx), so the lean is exact at any resolution and neighbouring cards share their edges.
//   mask      left edge x = y * _Slope, right edge x = _CardW + y * _Slope (antialiased over one screen pixel)
//   divider   on the LEFT edge: a 2-3 px near-white hairline tinted toward this card (_DividerColor), the card on the left
//             casting a soft shadow onto this one (stacked layers), a highlight that sweeps up along it (_Sweep 0..1)
//   scrim     a dark gradient up from the bottom so the card's number and name always read
//   lock      brightness, desaturation, grain; _Black = 1: nothing of the scene, a very dark fog gradient with faint noise
//   fx        _SeamFlare (press, white), _Burst (unlock, yellow), _Danger (locked tap, red), _Expand (launch: whole rect)
Shader "UI/CheckpointCard"
{
    Properties
    {
        [PerRendererData] _MainTex ("Diorama", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _RectPx ("Rect size (px, reference)", Vector) = (755, 1360, 0, 0)
        _Slope ("Edge lean (x per px of height)", Float) = 0.158
        _CardW ("Card width between edges (px)", Float) = 540
        _DividerColor ("Divider tint", Color) = (1, 1, 1, 1)
        _DividerAlpha ("Divider opacity", Float) = 0.42
        _DividerWidth ("Divider width (px)", Float) = 2.5
        _ShadowStrength ("Shadow from the card on the left", Float) = 0.55
        _ShadowReach ("Shadow reach (px)", Float) = 26
        _Sweep ("Highlight sweep 0-1 (<0 idle)", Float) = -1
        _SeamFlare ("Press 0-1", Float) = 0
        _Burst ("Unlock burst 0-1", Float) = 0
        _BurstColor ("Unlock colour", Color) = (1, 0.82, 0.3, 1)
        _Danger ("Danger 0-1", Float) = 0
        _DangerColor ("Danger colour", Color) = (1, 0.3, 0.3, 1)
        _DrawSeam ("Draw left divider", Float) = 1
        _Vignette ("Vignette", Float) = 0.22
        _Scrim ("Bottom scrim", Float) = 0.6
        _ScrimHeight ("Bottom scrim height (0-1 of the card)", Float) = 0.34
        _Parallax ("Parallax uv shift (x)", Float) = 0
        _Bright ("Brightness", Float) = 1
        _Desat ("Desaturation", Float) = 0
        _Grain ("Grain", Float) = 0
        _Black ("Black (no scene) 0-1", Float) = 0
        _TeaseColor ("Locked hint colour", Color) = (0.6, 0.7, 0.9, 1)
        _Expand ("Expand to full rect 0-1", Float) = 0
        _Alpha ("Alpha", Float) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 world : TEXCOORD1; };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color, _DividerColor, _BurstColor, _DangerColor, _TeaseColor;
            float4 _RectPx, _ClipRect;
            float _Slope, _CardW, _DividerAlpha, _DividerWidth, _ShadowStrength, _ShadowReach, _Sweep;
            float _SeamFlare, _Burst, _Danger, _DrawSeam, _Vignette, _Scrim, _ScrimHeight, _Parallax, _Bright, _Desat, _Grain, _Black, _Expand, _Alpha;
            float _MenuTime;

            v2f vert (appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 px = i.uv * _RectPx.xy;                 // card-rect pixels, origin bottom-left
                float slope = lerp(_Slope, 0, _Expand);
                float cardW = lerp(_CardW, _RectPx.x, _Expand);
                float xl = px.y * slope;
                float xr = cardW + px.y * slope;
                float aa = max(fwidth(px.x), 0.75);
                float cosA = rsqrt(1 + slope * slope);
                float dLeft = (px.x - xl) * cosA;              // + inside
                float dRight = (xr - px.x) * cosA;
                float inside = saturate(dLeft / aa + 0.5) * saturate(dRight / aa + 0.5);

                // ---- the picture ----
                float2 uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                uv.x += _Parallax;
                fixed3 c = tex2D(_MainTex, uv).rgb;
                float lum = dot(c, float3(0.299, 0.587, 0.114));
                c = lerp(c, lum.xxx, saturate(_Desat));
                float g = (Hash(floor(px * 0.5) + floor(_MenuTime * 20)) - 0.5) * _Grain;
                c = c * _Bright + g * max(_Bright, 0.15);

                // black (far locked): no scene, a very dark fog gradient with faint drifting noise
                // (linear colour space: these are tiny on purpose - 0.003 linear is about 4% on screen)
                float fogN = Hash(floor(px * 0.25) + floor(_MenuTime * 8)) * 0.0018;
                fixed3 dark = lerp(fixed3(0.0012, 0.0016, 0.003), _TeaseColor.rgb * 0.0045, saturate(i.uv.y * 0.8 + 0.1)) + fogN;
                c = lerp(c, dark, saturate(_Black));

                // the card on the left lies on this one: a soft shadow inside the left edge
                c *= 1 - _ShadowStrength * exp(-max(dLeft, 0) / max(_ShadowReach, 1)) * (1 - _Expand);
                float vy = abs(i.uv.y - 0.5) * 2;
                c *= 1 - _Vignette * vy * vy * (1 - _Expand);
                // bottom scrim: the label always sits on a calm dark ground
                float sk = 1 - smoothstep(0, max(_ScrimHeight, 0.01), i.uv.y);
                c *= 1 - _Scrim * sk * sk * (1 - _Expand);
                c = lerp(c, c * (1 + 0.35 * _SeamFlare), _SeamFlare);

                fixed3 rgb = c * inside;
                float a = inside;

                // ---- the divider: hairline + sweep (+ press / unlock / danger) ----
                if (_DrawSeam > 0.5 && _Expand < 0.999)
                {
                    float d = abs(dLeft);
                    float hl = saturate((_DividerWidth * 0.5 + aa * 0.5 - d) / aa);
                    float sweep = 0;
                    if (_Sweep >= 0)
                    {
                        float sy = _Sweep * (_RectPx.y + 500) - 250;
                        sweep = exp(-(px.y - sy) * (px.y - sy) / (2 * 140 * 140));
                    }
                    fixed3 lc = lerp(fixed3(1, 1, 1), _DividerColor.rgb, 0.45);
                    lc = lerp(lc, _BurstColor.rgb, saturate(_Burst));
                    lc = lerp(lc, _DangerColor.rgb, saturate(_Danger));
                    float la = saturate(_DividerAlpha * (1 + _SeamFlare * 0.8) + sweep * 0.55 + _Burst * 0.6 + _Danger * 0.5) * (1 - _Expand);
                    float spill = exp(-d / 5) * (sweep * 0.35 + _Burst * 0.6 + _Danger * 0.3);
                    rgb = lerp(rgb, lc, hl * la);
                    rgb += lc * spill * 0.6;
                    a = max(a, hl * la);
                }

                fixed4 col = fixed4(rgb, a) * _Alpha * i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                col *= UnityGet2DClipping(i.world.xy, _ClipRect);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
