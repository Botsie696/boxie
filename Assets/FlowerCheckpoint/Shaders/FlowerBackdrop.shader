// Flower checkpoint: the sky behind everything (the reference's backdrop). Drawn as a screen-filling quad pushed to the
// far plane, after the opaque scene and before the transparents, with a depth test - so it only shows where nothing else
// was drawn. Back to front:
//   sky        vivid cyan-blue at the top to a pale aqua haze, laid in flat brush bands
//   swirls     big sweeping white cloud strokes (the painting's wind), slowly drifting, two layers
//   karst      two rows of pale lilac pillars with sage-green caps and the odd blossom dab, rising out of a white valley
//              mist (grounded: their feet are always lost in the mist, never a floating underside)
//   birds      a few white "v"s gliding across
// Visible only where the flower look is in force (FlowerCommon.hlsl, follows the look wipe). Driven by
// FlowerCheckpointController (_FlBackdrop).
Shader "FlowerCheckpoint/Backdrop"
{
    Properties
    {
        _SkyTop ("Sky Top", Color) = (0.1, 0.55, 0.85, 1)
        _SkyMid ("Sky Mid", Color) = (0.35, 0.75, 0.92, 1)
        _SkyLow ("Sky Low (haze)", Color) = (0.8, 0.94, 0.94, 1)
        _SwirlColor ("Swirl", Color) = (0.96, 0.98, 1, 1)
        _SwirlShade ("Swirl Shade", Color) = (0.62, 0.82, 0.92, 1)
        _FarStone ("Pillar Stone", Color) = (0.78, 0.8, 0.86, 1)
        _FarShade ("Pillar Shade", Color) = (0.6, 0.66, 0.78, 1)
        _FarMoss ("Pillar Moss", Color) = (0.55, 0.78, 0.45, 1)
        _MistColor ("Valley Mist", Color) = (0.88, 0.96, 0.95, 1)
        _SwirlCover ("Swirl Cover", Range(0, 1)) = 0.45
        _SwirlSpeed ("Swirl Drift", Range(0, 0.05)) = 0.008
        _PillarAmount ("Pillars", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+502" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "FlowerBackdrop"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/FlowerCheckpoint/Shaders/FlowerCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SkyTop, _SkyMid, _SkyLow, _SwirlColor, _SwirlShade, _FarStone, _FarShade, _FarMoss, _MistColor;
                half _SwirlCover, _SwirlSpeed, _PillarAmount;
            CBUFFER_END
            float4 _FlBackdrop;   // x: offset from the camera's yaw (screen widths), y: time, zw: camera travel (screen heights)

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

            void Over(inout half3 c, inout half a, half3 col, half k) { c = lerp(c, col, k); a = max(a, k); }

            // Sweeping cloud strokes: a noise field stretched along a direction that bends with the field itself.
            void Swirl(float2 p, float cover, float seed, float px, inout half3 c, inout half a)
            {
                float bend = Fbm(p * 0.35 + seed) * 4.0;
                float2 dir = float2(cos(bend), sin(bend) * 0.55);
                float2 q = float2(dot(p, dir), dot(p, float2(-dir.y, dir.x)));
                float f = Fbm(float2(q.x * 0.6, q.y * 4.5) + seed * 3.1);
                float edge = 1.0 - cover * 0.5;
                float fw = max(fwidth(f), 1e-4);
                half body = (half)smoothstep(edge - fw, edge + fw, f);
                if (body <= 0.0h) return;
                // Painted in two flat values: white body, a blue-white shaded underside of each stroke.
                half shade = (half)step(f, edge + 0.04);
                half3 col = lerp(_SwirlColor.rgb, _SwirlShade.rgb, shade * 0.7h);
                Over(c, a, col, body);
            }

            // A row of karst pillars: base line "foot" (screen height), heights / widths by column hash.
            void Pillars(float2 p, float cols, float foot, float hMin, float hMax, float seed, half fade, inout half3 c, inout half a)
            {
                float fx = p.x * cols;
                float ci = floor(fx);
                [unroll] for (int k = -1; k <= 1; k++)
                {
                    float cc = ci + k;
                    float h0 = PixHash2(float2(cc, seed));
                    if (h0 > 0.75) continue;
                    float cx = cc + 0.5 + (PixHash2(float2(cc, seed + 1.0)) - 0.5) * 0.5;
                    float w = lerp(0.18, 0.34, PixHash2(float2(cc, seed + 2.0)));
                    float top = foot + lerp(hMin, hMax, PixHash2(float2(cc, seed + 3.0)));
                    float y = p.y;
                    float dx = fx - cx;
                    // Rounded shoulders, bulging a little, wider at the foot (all in screen units).
                    float ww = w * (1.0 + 0.25 * saturate((top - y) / max(top - foot, 1e-3))) * (1.0 + 0.08 * sin(y * 60.0 + cc)) / cols;
                    float dxs = dx / cols;
                    float shoulder = ww * 0.9;
                    float dTop = y - (top - shoulder);
                    float inside = dTop > 0.0 ? length(float2(dxs, dTop)) - shoulder : abs(dxs) - ww;
                    if (inside > 0.0 || y < foot - 0.2) continue;
                    // Stone: lit on the left, shade on the right, vertical strokes.
                    half stroke = (half)PixNoise(float3(fx * 6.0, y * 8.0, seed));
                    half lit = dxs < -ww * 0.15 ? 1.0h : 0.0h;
                    half3 col = lerp(_FarShade.rgb, _FarStone.rgb, saturate(lit * 0.8h + stroke * 0.35h));
                    // Moss cap and moss tongues down the shoulders.
                    float capLine = top - shoulder * 1.1 - 0.012 * PixNoise(float3(fx * 9.0, seed, 4.0)) - 0.02 * step(0.6, PixNoise(float3(fx * 4.0, seed, 7.0)));
                    if (y > capLine)
                    {
                        col = lerp(_FarMoss.rgb * 0.85h, _FarMoss.rgb * 1.08h, (half)step(0.5, PixNoise(float3(fx * 7.0, y * 30.0, seed + 3.0))));
                        // The odd blossom dab.
                        half bl = (half)step(0.82, PixNoise(float3(fx * 5.0, y * 25.0, seed + 9.0)));
                        col = lerp(col, FlBloom(PixHash2(float2(cc, seed + 6.0))) * 1.05h, bl * 0.8h);
                    }
                    // Fade into the valley mist toward the foot (they rise out of it).
                    half mist = (half)smoothstep(foot + 0.12, foot - 0.02, y);
                    col = lerp(col, _MistColor.rgb, mist);
                    col = lerp(col, _SkyLow.rgb, fade);
                    Over(c, a, col, 1.0h);
                }
            }

            // A band of blossom-tree crowns along "y0": round painted puffs, one bloom colour each, a light dab on top.
            void Canopy(float2 p, float y0, float cell, float seed, half fade, inout half3 c, inout half a)
            {
                float fx = p.x / cell;
                float ci = floor(fx);
                [unroll] for (int k = -1; k <= 1; k++)
                {
                    float cc = ci + k;
                    float h = PixHash2(float2(cc, seed));
                    if (h > 0.8) continue;
                    float r = lerp(0.55, 0.95, PixHash2(float2(cc, seed + 1.0))) * cell;
                    float2 ctr = float2((cc + 0.5 + (PixHash2(float2(cc, seed + 2.0)) - 0.5) * 0.6) * cell, y0 + (PixHash2(float2(cc, seed + 3.0)) - 0.3) * cell);
                    float2 d = p - ctr;
                    float wob = (PixNoise(float3(d * 40.0 / cell, seed)) - 0.5) * 0.18 * r;
                    float dist = length(d * float2(1.0, 1.25)) - r - wob;
                    if (dist > 0.0) continue;
                    half3 col = FlBloom(PixHash2(float2(cc, seed + 4.0)));
                    half lit = (half)step(0.0, dot(normalize(d + 1e-5), float2(-0.6, 0.8)) - 0.1);
                    col *= lerp(0.78h, 1.08h, lit);
                    col = lerp(col, col * 1.2h + 0.06h, (half)step(0.75, PixNoise(float3(p * 90.0, seed))) * lit);
                    col = lerp(col, _MistColor.rgb, fade);
                    Over(c, a, col, 1.0h);
                }
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half fl = FlAt(uv);
                if (fl < 0.001h) return 0;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 p = float2(uv.x * aspect, uv.y);
                float yaw = _FlBackdrop.x;
                float t = _FlBackdrop.y;
                float2 cam = _FlBackdrop.zw;
                float px = 1.0 / max(_ScreenParams.y, 1.0);

                // ---- sky in flat brush bands ----
                float bandN = (PixNoise(float3(p.x * 2.0 + yaw * 0.3, p.y * 14.0, 1.0)) - 0.5) * 0.06;
                float sy = saturate(p.y + bandN);
                half g = FlBands((half)sy, 6.0h);
                half3 c = g > 0.55h ? lerp(_SkyMid.rgb, _SkyTop.rgb, saturate((g - 0.55h) / 0.45h))
                                    : lerp(_SkyLow.rgb, _SkyMid.rgb, saturate((g - 0.12h) / 0.43h));
                half a = 1.0h;

                // ---- sweeping white cloud strokes, far then near ----
                float drift = t * _SwirlSpeed;
                Swirl(p * 1.6 + float2(drift * 0.6 + yaw * 0.5 + cam.x * 0.5, cam.y * 0.5 - 0.6), _SwirlCover * 0.85, 7.0, px, c, a);

                // ---- karst pillars rising out of the mist: far row, then a nearer row ----
                if (_PillarAmount > 0.001)
                {
                    float2 pf = p + float2(yaw * 0.25 + cam.x * 0.25, -cam.y * 0.1);
                    Pillars(pf, 7.0, 0.16, 0.12, 0.34, 3.0, 0.55h, c, a);
                    Canopy(pf, 0.22, 0.032, 21.0, 0.35h, c, a);
                    float2 pn = p + float2(yaw * 0.45 + cam.x * 0.45 + 0.37, -cam.y * 0.18);
                    Pillars(pn, 5.0, 0.08, 0.14, 0.36, 11.0, 0.2h, c, a);
                    Canopy(pn, 0.17, 0.045, 31.0, 0.05h, c, a);
                    // Valley mist band over their feet.
                    half mistBand = (half)smoothstep(0.26, 0.02, p.y + (PixNoise(float3(p.x * 3.0 + drift, 0.0, 5.0)) - 0.5) * 0.06);
                    c = lerp(c, _MistColor.rgb, mistBand * 0.85h);
                }

                Swirl(p * 1.1 + float2(drift + yaw + cam.x, cam.y - 0.2), _SwirlCover, 2.0, px, c, a);

                // ---- birds ----
                [unroll] for (int b = 0; b < 4; b++)
                {
                    float bt = frac(t * (0.01 + b * 0.003) + b * 0.29);
                    float2 bc = float2(bt * (aspect + 0.4) - 0.2, 0.5 + 0.08 * b + 0.01 * sin(t * 0.8 + b));
                    float2 q = (p - bc) / 0.01;
                    float flap = 0.35 + 0.3 * sin(t * 6.0 + b * 2.0);
                    float vline = abs(q.y - abs(q.x) * flap) * 0.01 * _ScreenParams.y;
                    half bird = (half)(1.0 - smoothstep(1.0, 2.2, vline)) * (half)step(abs(q.x), 1.0);
                    Over(c, a, half3(1, 1, 1), bird * 0.9h);
                }

                return half4(c * a * fl, a * fl);
            }
            ENDHLSL
        }
    }
}
