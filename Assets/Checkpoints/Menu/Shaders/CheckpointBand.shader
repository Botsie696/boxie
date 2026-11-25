// MainUi checkpoint stack: one band = a live RenderTexture (the checkpoint's legs) inside a parallelogram whose top and
// bottom edges rise from lower-left to upper-right. Everything is computed in the band rect's own pixels (_RectPx), so the
// slope is exact at any resolution and neighbouring bands share their edges with no gap.
//   mask      bottom edge y = x * _Slope, top edge y = _BandH + x * _Slope (antialiased over one screen pixel)
//   divider   on the TOP edge: a 2-3 px near-white hairline tinted toward this band (_DividerColor), the band above casting
//             a soft shadow down onto this one (stacked layers), a highlight that sweeps along it (_Sweep 0..1, <0 = idle)
//   lock      brightness, desaturation, grain; _Black = 1: nothing of the scene, a very dark fog gradient with faint noise
//   fx        _SeamFlare (press, white), _Burst (unlock, yellow), _Danger (locked tap, red), _Expand (launch: whole rect)
Shader "UI/CheckpointBand"
{
    Properties
    {
        [PerRendererData] _MainTex ("Diorama", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _RectPx ("Rect size (px, reference)", Vector) = (1080, 386, 0, 0)
        _Slope ("Edge slope (rise per px)", Float) = 0.158
        _BandH ("Band height between edges (px)", Float) = 215
        _DividerColor ("Divider tint", Color) = (1, 1, 1, 1)
        _DividerAlpha ("Divider opacity", Float) = 0.42
        _DividerWidth ("Divider width (px)", Float) = 2.5
        _ShadowStrength ("Shadow from the band above", Float) = 0.55
        _ShadowReach ("Shadow reach (px)", Float) = 26
        _Sweep ("Highlight sweep 0-1 (<0 idle)", Float) = -1
        _SeamFlare ("Press 0-1", Float) = 0
        _Burst ("Unlock burst 0-1", Float) = 0
        _BurstColor ("Unlock colour", Color) = (1, 0.82, 0.3, 1)
        _Danger ("Danger 0-1", Float) = 0
        _DangerColor ("Danger colour", Color) = (1, 0.3, 0.3, 1)
        _DrawSeam ("Draw top divider", Float) = 1
        _Vignette ("Vignette", Float) = 0.25
        _Parallax ("Parallax uv shift (y)", Float) = 0
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
            float _Slope, _BandH, _DividerAlpha, _DividerWidth, _ShadowStrength, _ShadowReach, _Sweep;
            float _SeamFlare, _Burst, _Danger, _DrawSeam, _Vignette, _Parallax, _Bright, _Desat, _Grain, _Black, _Expand, _Alpha;
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
                float2 px = i.uv * _RectPx.xy;                 // band-rect pixels, origin bottom-left
                float slope = lerp(_Slope, 0, _Expand);
                float bandH = lerp(_BandH, _RectPx.y, _Expand);
                float yb = px.x * slope;
                float yt = bandH + px.x * slope;
                float aa = max(fwidth(px.y), 0.75);
                float cosA = rsqrt(1 + slope * slope);
                float dTop = (yt - px.y) * cosA;               // + inside
                float dBot = (px.y - yb) * cosA;
                float inside = saturate(dTop / aa + 0.5) * saturate(dBot / aa + 0.5);

                // ---- the picture ----
                float2 uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                uv.y += _Parallax;
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

                // the band above lies on this one: a soft shadow under the top edge
                c *= 1 - _ShadowStrength * exp(-max(dTop, 0) / max(_ShadowReach, 1)) * (1 - _Expand);
                float vx = abs(i.uv.x - 0.5) * 2;
                c *= 1 - _Vignette * vx * vx * (1 - _Expand);
                c = lerp(c, c * (1 + 0.35 * _SeamFlare), _SeamFlare);

                fixed3 rgb = c * inside;
                float a = inside;

                // ---- the divider: hairline + sweep (+ press / unlock / danger) ----
                if (_DrawSeam > 0.5 && _Expand < 0.999)
                {
                    float d = abs(dTop);
                    float hl = saturate((_DividerWidth * 0.5 + aa * 0.5 - d) / aa);
                    float sweep = 0;
                    if (_Sweep >= 0)
                    {
                        float sx = _Sweep * (_RectPx.x + 500) - 250;
                        sweep = exp(-(px.x - sx) * (px.x - sx) / (2 * 110 * 110));
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
