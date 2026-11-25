// The black inner threshold of a zone door (TrialMonument2): an arch-shaped tear in reality.
//
// Drawn on a quad that PortalDoor sizes to the arch's bounding box; the fragment shader cuts the
// quad down to the arch opening (a rectangle topped with a half circle) and discards the rest.
//
//   - It is OPAQUE and writes depth, so anything that ends up behind it - the runner as it crosses
//     the door - is hidden by it: the runner is sliced by the plane and disappears into the dark.
//   - It writes STENCIL 1 over the opening (Replace). MonumentBlock.shader has _StencilRef /
//     _StencilComp, so a block material set to "NotEqual, Ref 1" is masked out wherever the void
//     is on screen.
//   - The rim is torn: the edge distance is warped by noise, so the opening reads as a rip with a
//     glowing lip rather than a clean cut. Inside, a slow spiral and a few stars sit on near black.
//   - _Open (0..1) irises the opening open/closed round its centre.
Shader "TrialMonument/PortalVoid"
{
    Properties
    {
        _VoidColor ("Void", Color) = (0.004, 0.003, 0.012, 1)
        [HDR] _RimColor ("Rim Glow (HDR)", Color) = (0.6, 0.95, 1.2, 1)
        _AccentColor ("Swirl / Stars", Color) = (0.35, 0.55, 1, 1)
        _HalfWidth ("Opening Half Width", Float) = 1.9
        _BottomY ("Opening Bottom", Float) = -1.9
        _SpringY ("Height Where The Arch Starts", Float) = 1.0
        _Open ("Open", Range(0, 1)) = 1
        _Intensity ("Glow Intensity", Range(0, 3)) = 1
        _RimWidth ("Rim Width", Range(0.02, 0.6)) = 0.2
        _Tear ("Tear Roughness", Range(0, 0.4)) = 0.14
        _SwirlSpeed ("Swirl Speed", Float) = 0.5
        _Rect ("Rectangular Opening (0 = arch)", Float) = 0
        _TopY ("Opening Top (rectangle)", Float) = 4.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+1" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Stencil { Ref 1 Comp Always Pass Replace }
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _VoidColor;
                half4 _RimColor;
                half4 _AccentColor;
                float _HalfWidth;
                float _BottomY;
                float _SpringY;
                half _Open;
                half _Intensity;
                half _RimWidth;
                half _Tear;
                float _SwirlSpeed;
                float _Rect;
                float _TopY;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), f.x),
                            lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
            }

            // Distance to the edge of the arch opening, positive inside.
            float ArchDistance(float2 p)
            {
                if (_Rect > 0.5) return min(min(_HalfWidth - abs(p.x), p.y - _BottomY), _TopY - p.y);
                return p.y > _SpringY
                    ? _HalfWidth - length(p - float2(0, _SpringY))
                    : min(_HalfWidth - abs(p.x), p.y - _BottomY);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float top = _Rect > 0.5 ? _TopY : _SpringY + _HalfWidth;
                float2 p = float2((i.uv.x * 2 - 1) * _HalfWidth, lerp(_BottomY, top, i.uv.y));
                float2 centre = float2(0, (_BottomY + top) * 0.5);

                // Iris: shrink the opening towards its centre as _Open falls.
                float open = max(_Open, 0.001);
                float2 q = (p - centre) / open + centre;

                float t = _Time.y;
                float torn = (vnoise(q * 2.7 + float2(0, t * 0.35)) - 0.5) * 2.0 * _Tear
                           + (vnoise(q * 7.3 - float2(t * 0.5, 0)) - 0.5) * _Tear * 0.6;
                float d = (ArchDistance(q) + torn) * open;
                clip(d);

                // Inside: near black with a slow spiral and a scatter of stars.
                float2 c = q - centre;
                float r = length(c);
                float a = atan2(c.y, c.x);
                float arms = 0.5 + 0.5 * sin(a * 3.0 + r * 3.2 - t * _SwirlSpeed * 3.0);
                float swirl = arms * smoothstep(0.2, 1.7, r) * 0.22;
                float2 sc = q * 5.0 + float2(0, t * 0.15);
                float sh = hash21(floor(sc));
                float star = step(0.93, sh) * (1.0 - smoothstep(0.0, 0.3, length(frac(sc) - 0.5)));
                star *= 0.6 + 0.4 * sin(t * 2.0 + sh * 40.0);

                half3 col = _VoidColor.rgb + _AccentColor.rgb * (swirl * _Intensity + star * 0.8);
                // Rectangular door: the colour of the far side rises from the foot of the panel.
                if (_Rect > 0.5)
                {
                    float h = saturate((p.y - _BottomY) / (_TopY - _BottomY));
                    col += _AccentColor.rgb * pow(1.0 - h, 2.2) * 0.55 * _Intensity;
                }

                // Glowing torn lip.
                float lip = exp(-d / max(0.001, _RimWidth));
                col += _RimColor.rgb * (lip * lip * _Intensity * 1.4 + lip * 0.25);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
