// Full-screen effect layer for the zone change (TrialMonument2). One UI Image carries it.
//
//   _Flash   white (or _FlashColor) screen flash: at 1 the screen is solid.
//   _Dark    black cover (the void between zones), with a vignette so the edge goes first.
//   _Lines   speed lines. Parallel streaks lying along _LineDir, the on-screen direction the runner
//            is travelling, streaming the other way so it reads as rushing forward.
//   _Vignette  extra edge darkening on the approach to a door.
//
// Premultiplied output (Blend One OneMinusSrcAlpha): rgb adds light, a darkens what is behind.
Shader "TrialMonument/PortalOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _FlashColor ("Flash Colour", Color) = (1, 1, 1, 1)
        _LineColor ("Line Colour", Color) = (0.8, 0.95, 1, 1)
        _Flash ("Flash", Range(0, 1)) = 0
        _Dark ("Dark", Range(0, 1)) = 0
        _Lines ("Speed Lines", Range(0, 1)) = 0
        _Vignette ("Vignette", Range(0, 1)) = 0
        _LineDir ("Line Direction (screen)", Vector) = (0, 1, 0, 0)
        _Aspect ("Aspect", Float) = 0.5625
        _Scroll ("Line Scroll", Float) = 0
        _WipeA ("Curtain Leading Edge", Range(0, 1)) = 0
        _WipeB ("Curtain Trailing Edge", Range(0, 1)) = 0
        _CurtainColor ("Curtain Colour", Color) = (0.4, 0.6, 1, 1)
        _CurtainDeep ("Curtain Deep Colour", Color) = (0.02, 0.03, 0.08, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" "CanUseSpriteAtlas" = "True" }

        Pass
        {
            Name "Overlay"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _FlashColor;
                half4 _LineColor;
                half _Flash;
                half _Dark;
                half _Lines;
                half _Vignette;
                float4 _LineDir;
                float _Aspect;
                float _Scroll;
                half _WipeA;
                half _WipeB;
                half4 _CurtainColor;
                half4 _CurtainDeep;
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

            float hash11(float n) { return frac(sin(n * 127.1) * 43758.5453); }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float2 p = (uv - 0.5) * float2(_Aspect, 1.0) * 2.0;   // -1..1 tall, aspect wide
                float edge = saturate(length((uv - 0.5) * 2.0) / 1.42);

                // Speed lines along _LineDir, streaming against it.
                float2 dir = normalize(_LineDir.xy + 1e-5);
                float along = dot(p, dir);
                float perp = dot(p, float2(-dir.y, dir.x));
                float lane = floor(perp * 34.0);
                float h = hash11(lane);
                float h2 = hash11(lane + 71.0);
                float speed = lerp(0.8, 2.4, h2);
                float pos = frac((along * 0.32 + _Scroll * speed) + h);
                // A short bright head and a long fading tail; lanes pick their own length and width.
                float len = lerp(0.05, 0.22, hash11(lane + 13.0));
                float streak = smoothstep(len, 0.0, pos) * smoothstep(0.0, 0.01, pos);
                float fine = abs(frac(perp * 34.0) - 0.5) * 2.0;
                float width = lerp(0.35, 0.8, hash11(lane + 5.0));
                streak *= 1.0 - smoothstep(width * 0.6, width, fine);
                streak *= step(0.42, h);
                // Fewer lines in the middle of the screen, more towards the edges.
                float lines = streak * _Lines * smoothstep(0.05, 0.6, edge);

                half3 rgb = _LineColor.rgb * lines * 0.9h;
                half a = 0;

                // Vignette + dark cover: the edge of the screen darkens first.
                half vig = saturate(_Vignette * smoothstep(0.35, 1.0, edge));
                half dark = saturate(_Dark * (0.75h + 0.25h * edge));
                a = saturate(max(dark, vig));
                rgb *= 1.0h - a * 0.4h;

                // The sweep: a thin vertical line of light passing left to right (PortalZoneController turns
                // the world from one palette to the next behind it).
                float lead = lerp(-0.1, 1.15, _WipeA);
                float wipeOn = step(0.001, _WipeA) * (1.0 - step(0.999, _WipeA));
                float lineCore = exp(-abs(uv.x - lead) * 90.0);
                float lineGlow = exp(-abs(uv.x - lead) * 14.0) * 0.28;
                rgb += _CurtainColor.rgb * (lineCore * 1.5 + lineGlow) * wipeOn;

                // Flash last: at 1 it replaces everything.
                half f = saturate(_Flash);
                rgb = lerp(rgb, _FlashColor.rgb, f);
                a = lerp(a, 1.0h, f);

                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
}
