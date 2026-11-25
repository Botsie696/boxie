// Backdrop for the TrialMonument scene: a smooth height gradient (dark desaturated navy at the
// top, lighter dusty blue at the bottom), a soft pale glow low in the frame, and a field of
// small twinkling stars baked in procedurally. Drawn on a quad that MonumentSkyBackdrop keeps
// filling the camera's view just in front of the far plane.
Shader "TrialMonument/MonumentSky"
{
    Properties
    {
        _TopColor ("Top (navy)", Color) = (0.17, 0.20, 0.25, 1)
        _MidColor ("Middle", Color) = (0.33, 0.40, 0.47, 1)
        _BottomColor ("Bottom (dusty blue)", Color) = (0.50, 0.61, 0.69, 1)
        _MidPoint ("Middle Height", Range(0.05, 0.95)) = 0.5
        _GlowColor ("Low Glow", Color) = (0.68, 0.77, 0.83, 1)
        _GlowCenter ("Glow Centre (uv)", Vector) = (0.5, 0.2, 0, 0)
        _GlowRadius ("Glow Radius", Range(0.05, 1.5)) = 0.55
        _GlowStrength ("Glow Strength", Range(0, 1)) = 0.35
        _Grain ("Grain", Range(0, 0.05)) = 0.012

        [Header(Stars)]
        _StarColor ("Star Colour", Color) = (1, 1, 1, 1)
        _StarDensity ("Star Cells Across", Range(10, 200)) = 70
        _StarChance ("Star Chance per Cell", Range(0, 1)) = 0.16
        _StarSize ("Star Size", Range(0.02, 0.5)) = 0.13
        _StarBrightness ("Star Brightness", Range(0, 2)) = 0.9
        _TwinkleSpeed ("Twinkle Speed", Range(0, 5)) = 1.1
        _StarFadeBottom ("Fewer Stars Near Bottom", Range(0, 1)) = 0.45
        _StarOffset ("Star Offset (set by script)", Vector) = (0, 0, 0, 0)
        _Aspect ("Aspect (set by script)", Float) = 0.5625
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+500" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Sky"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Zone palette + warp (PortalZoneController, TrialMonument2). Globals: _ZoneBlend 0 (the
            // default, nobody sets it) leaves the material as authored, so TrialMonument is unchanged.
            half4 _ZoneSkyTop, _ZoneSkyMid, _ZoneSkyBottom, _ZoneSkyGlow, _ZoneStarColor;
            half4 _ZoneOldSkyTop, _ZoneOldSkyMid, _ZoneOldSkyBottom, _ZoneOldSkyGlow, _ZoneOldStarColor;
            float _ZoneWipeX;   // pixels right of this screen x (0..1) still show the old palette; 2 = none
            half _ZoneBlend;
            half _ZoneWarp;   // 0 = still stars .. 1 = stars streaking past at warp speed
            half _ZoneVoid;   // 0 = normal sky .. 1 = darkened for the void between zones
            float _ZoneWarpScroll; // how far the star field has rushed by, integrated by the script so a changing warp never jumps it

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor, _MidColor, _BottomColor, _GlowColor, _StarColor;
                float4 _GlowCenter, _StarOffset;
                half _MidPoint, _GlowRadius, _GlowStrength, _Grain;
                half _StarDensity, _StarChance, _StarSize, _StarBrightness, _TwinkleSpeed, _StarFadeBottom;
                float _Aspect;
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

            half4 frag(Varyings i) : SV_Target
            {
                float y = i.uv.y;
                half oldSide = step(_ZoneWipeX, i.positionCS.x / _ScreenParams.x);
                half3 topC = lerp(_TopColor.rgb, lerp(_ZoneSkyTop.rgb, _ZoneOldSkyTop.rgb, oldSide), _ZoneBlend);
                half3 midC = lerp(_MidColor.rgb, lerp(_ZoneSkyMid.rgb, _ZoneOldSkyMid.rgb, oldSide), _ZoneBlend);
                half3 botC = lerp(_BottomColor.rgb, lerp(_ZoneSkyBottom.rgb, _ZoneOldSkyBottom.rgb, oldSide), _ZoneBlend);
                half3 glowC = lerp(_GlowColor.rgb, lerp(_ZoneSkyGlow.rgb, _ZoneOldSkyGlow.rgb, oldSide), _ZoneBlend);
                half3 starC = lerp(_StarColor.rgb, lerp(_ZoneStarColor.rgb, _ZoneOldStarColor.rgb, oldSide), _ZoneBlend);

                half3 col = y < _MidPoint
                    ? lerp(botC, midC, smoothstep(0, 1, y / _MidPoint))
                    : lerp(midC, topC, smoothstep(0, 1, (y - _MidPoint) / (1 - _MidPoint)));

                float2 g = (i.uv - _GlowCenter.xy) * float2(_Aspect, 1);
                col = lerp(col, glowC, _GlowStrength * saturate(1 - length(g) / _GlowRadius) * saturate(1 - length(g) / _GlowRadius));

                // The void between zones: the whole sky sinks to near black; only the stars stay.
                col *= 1.0h - 0.9h * _ZoneVoid;

                // Stars: one candidate per grid cell, placed at a random spot inside it.
                float2 sp = float2(i.uv.x * _Aspect, i.uv.y) * _StarDensity + _StarOffset.xy;
                // Warp: the star field rushes down the screen and every star is drawn out into a streak.
                sp.y += _ZoneWarpScroll;
                float2 cell = floor(sp);
                float2 f = frac(sp);
                float h = hash21(cell);
                float2 pos = float2(hash21(cell + 17.1), hash21(cell + 43.7)) * 0.7 + 0.15;
                float size = _StarSize * lerp(0.45, 1.0, hash21(cell + 91.3)) * (1.0 + _ZoneWarp * 0.6);
                float aa = fwidth(sp.x) * 0.75;
                float2 dd = f - pos;
                dd.y /= 1.0 + _ZoneWarp * 9.0;
                float star = 1 - smoothstep(size - aa, size + aa, length(dd));
                float twinkle = 0.55 + 0.45 * sin(_Time.y * _TwinkleSpeed * lerp(0.6, 1.8, hash21(cell + 5.9)) + h * 40.0);
                float chance = saturate(_StarChance + _ZoneWarp * 0.22);
                float present = step(h, chance) * lerp(1 - _StarFadeBottom, 1, saturate(y * 1.4));
                col = lerp(col, starC, saturate(star * twinkle * present * _StarBrightness * (1.0 + _ZoneWarp * 1.6)));

                col += (hash21(i.uv * 1000.0 + frac(_Time.y)) - 0.5) * _Grain;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
