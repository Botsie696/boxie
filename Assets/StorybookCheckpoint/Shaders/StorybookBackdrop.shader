// Storybook checkpoint: the sky behind everything (the reference's backdrop). Drawn as a screen-filling quad pushed to the
// far plane, after the opaque scene and before the transparents, with a depth test - so it only shows where nothing else
// was drawn. Back to front:
//   sky        a teal watercolour wash, paler and warmer toward the horizon, pigment pooled unevenly
//   distant    pale blue-grey castle spires on far hills, a thin pen outline (parallax: they move least)
//   clouds     big cream cumulus banks with scalloped pen outlines and a few inner curls, a grey wash on their bellies,
//              drifting slowly (two layers, the nearer one moves more with the camera)
//   birds      a few pen "v"s gliding across
// Visible only where the storybook look is in force (StorybookCommon.hlsl, follows the look wipe). Driven by
// StorybookCheckpointController (_SbBackdrop).
Shader "StorybookCheckpoint/Backdrop"
{
    Properties
    {
        _SkyTop ("Sky Top", Color) = (0.42, 0.62, 0.6, 1)
        _SkyMid ("Sky Mid", Color) = (0.55, 0.72, 0.69, 1)
        _SkyLow ("Sky Low (haze)", Color) = (0.85, 0.82, 0.7, 1)
        _CloudColor ("Cloud", Color) = (0.95, 0.92, 0.84, 1)
        _CloudShade ("Cloud Belly", Color) = (0.74, 0.76, 0.72, 1)
        _FarColor ("Distant Castles", Color) = (0.6, 0.68, 0.68, 1)
        _InkColor ("Pen Ink", Color) = (0.2, 0.14, 0.1, 1)
        _CloudCover ("Cloud Cover", Range(0, 1)) = 0.52
        _CloudSpeed ("Cloud Drift", Range(0, 0.05)) = 0.006
        _PenWidth ("Pen Width (pixels at 1920 high)", Range(0.5, 4)) = 1.6
        _FarAmount ("Distant Castles", Range(0, 1)) = 0.8
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+502" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "StorybookBackdrop"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/StorybookCheckpoint/Shaders/StorybookCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SkyTop, _SkyMid, _SkyLow, _CloudColor, _CloudShade, _FarColor, _InkColor;
                half _CloudCover, _CloudSpeed, _PenWidth, _FarAmount;
            CBUFFER_END
            float4 _SbBackdrop;   // x: offset from the camera's yaw (screen widths), y: time, zw: camera travel (screen heights)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float2 p = v.uv * 2.0 - 1.0;
                #if UNITY_REVERSED_Z
                    o.positionCS = float4(p, 1e-6, 1.0);
                #else
                    o.positionCS = float4(p, 1.0 - 1e-6, 1.0);
                #endif
                o.uv = v.uv;
                #if UNITY_UV_STARTS_AT_TOP
                    if (_ProjectionParams.x < 0) o.uv.y = 1.0 - o.uv.y;
                #endif
                return o;
            }

            float Fbm(float2 p)
            {
                float a = PixNoise(float3(p, 0.5)) * 0.5;
                a += PixNoise(float3(p * 2.03 + 5.3, 1.5)) * 0.27;
                a += PixNoise(float3(p * 4.1 + 1.7, 2.5)) * 0.15;
                a += PixNoise(float3(p * 8.3 + 9.1, 3.5)) * 0.08;
                return a;
            }

            // A cloud bank: the field (> 0 inside) built from puffy fbm, bunched into banks.
            float CloudField(float2 p, float cover, float seed)
            {
                float bank = PixNoise(float3(p.x * 0.35 + seed, seed, 0.7));
                float puff = (PixNoise(float3(p * 7.0, seed + 1.0)) - 0.5) * 0.14;   // scalloped cumulus edges
                float f = Fbm(p * float2(1.0, 1.6) + seed) * 0.85 + bank * 0.25 + puff;
                return f - lerp(0.85, 0.45, cover);
            }

            half Pen(float d, float px) { return (half)(1.0 - smoothstep(px * 0.5, px * 0.5 + 1.0, abs(d))); }

            void Over(inout half3 c, inout half a, half3 col, half alpha)
            {
                c = lerp(c, col, alpha);
                a = a + alpha * (1.0h - a);
            }

            void Cloud(float2 p, float cover, float seed, float px, inout half3 c, inout half a)
            {
                float f = CloudField(p, cover, seed);
                float fw = max(fwidth(f), 1e-5);
                float dPix = f / fw;                         // signed distance to the outline in pixels
                if (dPix < -2.5) return;
                // Body: cream, a grey wash on the belly (lower part of each puff).
                float below = CloudField(p + float2(0.0, 0.06), cover, seed);
                half belly = (half)saturate((f - below) * 9.0 + 0.35);
                half3 body = lerp(_CloudColor.rgb, _CloudShade.rgb, belly * 0.75h);
                body *= 0.96h + 0.08h * (half)PixNoise(float3(p * 6.0, seed));
                half inside = (half)saturate(dPix + 0.5);
                Over(c, a, body, inside);
                // Pen outline (broken now and then) and a few curls inside.
                half keep = (half)step(0.22, PixNoise(float3(p * 5.0, seed + 3.0)));
                half ink = Pen(dPix, px) * keep;
                float curl = (f - 0.07) / fw;
                ink = max(ink, Pen(curl, px * 0.8) * (half)step(0.85, PixNoise(float3(p * 3.0, seed + 7.0))) * (half)step(f, 0.12) * inside);
                Over(c, a, _InkColor.rgb, ink * 0.85h);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.positionCS.xy / _ScreenParams.xy;
                #if UNITY_UV_STARTS_AT_TOP
                    if (_ProjectionParams.x < 0) uv.y = 1.0 - uv.y;
                #endif
                float2 suvS = GetNormalizedScreenSpaceUV(i.positionCS);
                half sb = SbAt(suvS);
                if (sb < 0.001h) return 0;

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float t = _SbBackdrop.y;
                float yaw = _SbBackdrop.x;
                float2 cam = _SbBackdrop.zw;
                float2 p = float2(i.uv.x * aspect, i.uv.y);
                float px = _PenWidth * (_ScreenParams.y / 1920.0);

                // ---- sky wash ----
                half3 c = lerp(_SkyMid.rgb, _SkyTop.rgb, (half)smoothstep(0.45, 1.0, p.y));
                c = lerp(_SkyLow.rgb, c, (half)smoothstep(0.02, 0.4, p.y));
                c *= 0.95h + 0.1h * (half)Fbm(p * 2.5 + float2(yaw * 0.3, 0.0));
                half a = 1.0h;

                // ---- distant castles on far hills ----
                if (_FarAmount > 0.001)
                {
                    float fx = p.x + yaw * 0.25 + cam.x * 0.3;
                    float hill = 0.16 + 0.05 * Fbm(float2(fx * 1.3, 2.0)) - cam.y * 0.15;
                    float towers = 0.0;
                    float cell = floor(fx * 3.0);
                    [unroll] for (int k = -1; k <= 1; k++)
                    {
                        float cc = cell + k;
                        float h = PixHash2(float2(cc, 4.0));
                        if (h > 0.55) continue;
                        float cx = (cc + 0.3 + 0.4 * PixHash2(float2(cc, 9.0))) / 3.0;
                        float w = lerp(0.012, 0.024, PixHash2(float2(cc, 2.0)));
                        float th = hill + lerp(0.06, 0.16, PixHash2(float2(cc, 5.0)));
                        float dx = abs(fx - cx);
                        bool body = dx < w && p.y < th;
                        bool cone = p.y >= th && p.y < th + w * 2.6 && dx < w * 1.25 * (1.0 - (p.y - th) / (w * 2.6));
                        if (body || cone) towers = 1.0;
                    }
                    half far = (half)max(step(p.y, hill), towers);
                    if (far > 0.0h)
                    {
                        half3 fc = lerp(_FarColor.rgb, _SkyLow.rgb, 0.35h);
                        Over(c, a, fc, far * (half)_FarAmount);
                    }
                    float fwh = max(fwidth(p.y - hill), 1e-5);
                    Over(c, a, _InkColor.rgb, Pen((p.y - hill) / fwh, px * 0.7) * 0.35h * (half)_FarAmount);
                }

                // ---- clouds: far bank, then near bank ----
                float drift = t * _CloudSpeed;
                Cloud(p * 3.4 + float2(drift * 0.6 + yaw * 0.6 + cam.x * 0.6, cam.y * 0.6 - 0.3), _CloudCover * 0.9, 11.0, px * 0.8, c, a);
                Cloud(p * 2.4 + float2(drift + yaw + cam.x, cam.y - 0.1), _CloudCover, 3.0, px, c, a);

                // ---- birds ----
                [unroll] for (int b = 0; b < 3; b++)
                {
                    float bt = frac(t * (0.012 + b * 0.004) + b * 0.37);
                    float2 bc = float2(bt * (aspect + 0.4) - 0.2, 0.62 + 0.12 * b + 0.015 * sin(t * 0.7 + b));
                    float2 q = (p - bc) / 0.012;
                    float flap = 0.35 + 0.25 * sin(t * 6.0 + b * 2.0);
                    float vline = abs(q.y - abs(q.x) * flap) * 0.012 * _ScreenParams.y;
                    half bird = Pen(vline, px * 0.9) * (half)step(abs(q.x), 1.0);
                    Over(c, a, _InkColor.rgb, bird * 0.8h);
                }

                return half4(c * a * sb, a * sb);
            }
            ENDHLSL
        }
    }
}
