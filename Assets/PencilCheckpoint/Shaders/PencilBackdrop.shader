// Pencil checkpoint: the sky behind everything. A screen-filling quad pushed to the far plane, drawn after the opaque
// scene with a depth test, so it only shows where nothing else was drawn. A page of a sketchbook, back to front:
//   sky        bare paper, a soft band of graphite hatching laid across the top
//   moon       a big compass-drawn circle with a faint second stroke, lightly shaded on one side (the town reference)
//   clouds     scribbled cumulus outlines, hatched underneath, drifting
//   mountains  jagged outline strokes only, no fill (the spaceship study's horizon)
//   town       a far skyline of roofs, spires and a clock tower, outlined and lightly hatched
//   birds      flocks of pencil "m" strokes, wings flapping, crossing the sky
// Visible only where the pencil look is in force (PencilCommon.hlsl, follows the look wipe). Driven by
// PencilCheckpointController (_PcBackdrop).
Shader "PencilCheckpoint/Backdrop"
{
    Properties
    {
        _SkyHatch ("Sky Hatching", Range(0, 1)) = 0.55
        _CloudCover ("Cloud Cover", Range(0, 1)) = 0.45
        _CloudSpeed ("Cloud Drift", Range(0, 0.05)) = 0.006
        _Birds ("Bird Flocks", Range(0, 8)) = 5
        _LineWidth ("Line Width (screen heights)", Range(0.0005, 0.005)) = 0.0016
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+502" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "PencilBackdrop"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/PencilCheckpoint/Shaders/PencilCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _SkyHatch, _CloudCover, _CloudSpeed, _Birds, _LineWidth;
            CBUFFER_END
            float4 _PcBackdrop;   // x: offset from the camera's yaw (screen widths), y: time, zw: camera travel (screen heights)

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

            float Fbm(float x, float seed)
            {
                return PixNoise(float3(x, seed, 0.5)) * 0.6 + PixNoise(float3(x * 2.3, seed, 1.5)) * 0.28 + PixNoise(float3(x * 5.1, seed, 2.5)) * 0.12;
            }

            // A skyline height (screen heights) at x for a far town: roofs, spires and a clock tower.
            float Town(float x, float seed, out float kind)
            {
                float cw = 0.045;
                float ci = floor(x / cw);
                float f = frac(x / cw);
                float h = PcHash(ci + seed);
                float base = 0.03 + h * 0.05;
                kind = 0.0;
                float roof = base + (1.0 - abs(f - 0.5) * 2.0) * 0.025 * step(0.3, h);        // gables
                if (h > 0.86) { roof = base + 0.06 + max(0.0, 1.0 - abs(f - 0.5) * 6.0) * 0.07; kind = 1.0; }   // spire
                if (PcHash(ci * 0.37 + seed + 4.0) > 0.95) { roof = 0.16 + (f > 0.25 && f < 0.75 ? 0.0 : -0.1) + max(0.0, 1.0 - abs(f - 0.5) * 5.0) * 0.04; kind = 2.0; }   // clock tower
                return roof;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half pc = PcAt(uv);
                if (pc < 0.001h) return 0;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float yaw = _PcBackdrop.x;
                float t = _PcBackdrop.y;
                float2 cam = _PcBackdrop.zw;
                float2 s = float2(uv.x * aspect, uv.y);
                float aa = 1.2 / max(_ScreenParams.y, 1.0);
                float w = _LineWidth;
                half ink = 0.0h;

                // ---- sky: a band of hatching across the top, thinning down ----
                float2 hs = s * 90.0;
                half skyTone = (half)(smoothstep(0.45, 1.0, uv.y) * _SkyHatch);
                ink = max(ink, PcHatchW(hs, 0.35, 1.0, 0.09, aa * 90.0, 2.0, 0.03) * (half)smoothstep(0.05, 0.25, skyTone) * 0.55h);
                ink = max(ink, PcHatchW(hs, -0.4, 1.2, 0.08, aa * 90.0, 5.0, 0.03) * (half)smoothstep(0.35, 0.55, skyTone) * 0.45h);

                // ---- moon: compass circle, a second faint stroke, shaded crescent ----
                float2 mc = float2(aspect * 0.72 + yaw * 0.08, 0.74 - cam.y * 0.02);
                float mr = 0.085;
                float md = length(s - mc);
                ink = max(ink, PcLine(md - mr, w * 1.2, aa) * 0.85h);
                ink = max(ink, PcLine(md - mr - 0.004 - sin(atan2(s.y - mc.y, s.x - mc.x) * 3.0) * 0.002, w * 0.7, aa) * 0.45h);
                if (md < mr)
                {
                    half cres = (half)smoothstep(-0.02, 0.06, length(s - mc - float2(0.05, 0.03)) - mr * 0.95);
                    ink = max(ink, PcHatch(s * 140.0, 0.785, 1.0, 0.1, aa * 140.0, 9.0) * cres * 0.6h);
                    ink *= 1.0h - (half)smoothstep(0.0, 0.01, mr - md) * (1.0h - cres) * 0.8h;   // the moon is clean paper
                }

                // ---- scribbled clouds ----
                float drift = t * _CloudSpeed;
                for (int k = 0; k < 4; k++)
                {
                    float cx = frac(PcHash(k * 3.1) + drift * (0.6 + k * 0.15) + yaw * 0.2 + cam.x * 0.15) * (aspect + 0.6) - 0.3;
                    float cy = 0.6 + PcHash(k * 5.7) * 0.28;
                    if (PcHash(k * 9.1) > _CloudCover + 0.3) continue;
                    float2 c0 = float2(cx, cy);
                    float d = 1e3;
                    for (int j = 0; j < 5; j++)
                    {
                        float2 o = float2((j - 2.0) * 0.045, (1.0 - abs(j - 2.0) * 0.5) * 0.025);
                        d = min(d, length(s - c0 - o) - (0.035 + PcHash(k * 7.0 + j) * 0.02));
                    }
                    d = max(d, c0.y - 0.012 - s.y);     // flat base
                    ink = max(ink, PcLine(d, w, aa) * 0.75h);
                    if (d < 0.0)
                    {
                        ink *= 0.25h;   // clouds are paper in front of the hatched sky
                        half under = (half)smoothstep(0.035, 0.0, s.y - c0.y + 0.012);
                        ink = max(ink, PcHatch(s * 160.0, 0.6, 1.0, 0.1, aa * 160.0, k) * under * 0.6h);
                    }
                }

                // ---- jagged far mountains: outline strokes only ----
                {
                    float x = s.x + yaw * 0.15 + cam.x * 0.1;
                    float hM = 0.24 + Fbm(x * 3.0, 4.0) * 0.16 + abs(frac(x * 2.2) - 0.5) * 0.08 - cam.y * 0.03;
                    ink = max(ink, PcLine(s.y - hM, w, aa) * 0.7h);
                    ink = max(ink, PcLine(s.y - hM + 0.012 + 0.006 * sin(x * 40.0), w * 0.6, aa) * 0.3h * (half)step(0.5, frac(x * 3.0)));
                    if (s.y < hM) ink *= 0.6h;
                    if (s.y < hM && s.y > hM - 0.04) ink = max(ink, PcHatch(s * 150.0, -0.9, 1.0, 0.09, aa * 150.0, 3.0) * 0.35h * (half)step(0.5, Fbm(x * 9.0, 7.0)));
                }

                // ---- the far town skyline, with a clock tower ----
                {
                    float x = s.x + yaw * 0.3 + cam.x * 0.25 + 3.7;
                    float kind;
                    float hT = 0.12 + Town(x, 11.0, kind) - cam.y * 0.05;
                    float dT = s.y - hT;
                    ink = max(ink, PcLine(dT, w * 1.1, aa) * 0.8h);
                    if (dT < 0.0)
                    {
                        // Walls: light hatching, windows as dark dots, vertical edges of the houses.
                        ink *= 0.3h;
                        ink = max(ink, PcHatch(s * 170.0, 0.785, 1.0, 0.1, aa * 170.0, 6.0) * 0.4h);
                        float cw = 0.045;
                        float f = frac(x / cw);
                        ink = max(ink, PcLine((f - 0.02) * cw, w * 0.7, aa) * 0.6h);
                        float2 wg = float2(frac(x / (cw / 3.0)), frac(s.y / 0.018));
                        ink = max(ink, (half)(step(abs(wg.x - 0.5), 0.15) * step(abs(wg.y - 0.5), 0.2) * step(PixHash(float3(floor(x / (cw / 3.0)), floor(s.y / 0.018), 2.0)), 0.35)) * 0.7h);
                        if (kind > 1.5)
                        {
                            float ci = floor(x / cw);
                            float2 cc = float2((ci + 0.5) * cw, 0.12 + 0.14 - cam.y * 0.05);
                            float cd = length(float2((x - cc.x), s.y - cc.y));
                            ink = max(ink, PcLine(cd - 0.009, w * 0.8, aa));
                            ink = max(ink, PcLine(PcSeg(float2(x, s.y), cc, cc + float2(0.0, 0.006)), w * 0.6, aa));
                            ink = max(ink, PcLine(PcSeg(float2(x, s.y), cc, cc + float2(0.005, 0.0)), w * 0.6, aa));
                        }
                    }
                    // A ground line under the town, broken.
                    ink = max(ink, PcLine(s.y - 0.1 + cam.y * 0.05, w, aa) * 0.5h * (half)step(0.3, frac(x * 4.0)));
                }

                // ---- birds: flocks of "m" strokes, flapping ----
                for (int b = 0; b < 8; b++)
                {
                    if (b >= (int)_Birds) break;
                    float speed = 0.012 + PcHash(b * 2.3) * 0.012;
                    float bx = frac(PcHash(b * 7.7) + t * speed + yaw * 0.25) * (aspect + 0.4) - 0.2;
                    float by = 0.5 + PcHash(b * 4.1) * 0.38 + sin(t * 0.3 + b) * 0.02;
                    for (int j = 0; j < 4; j++)
                    {
                        float2 o = float2(-j * 0.035 - PcHash(b * 3.0 + j) * 0.015, (PcHash(b * 5.0 + j) - 0.5) * 0.05);
                        float2 p = s - float2(bx, by) - o;
                        float size = 0.012 + PcHash(b + j * 1.3) * 0.006;
                        if (abs(p.x) > size * 1.2 || abs(p.y) > size) continue;
                        float flap = sin(t * (6.0 + PcHash(b * 9.0 + j) * 3.0) + j * 1.7) * 0.6;
                        float2 q = p / size;
                        float wx = abs(q.x);
                        float wy = (0.5 + flap * 0.4) * wx - 0.9 * wx * wx * (1.0 - flap * 0.3);
                        ink = max(ink, PcLine((q.y - wy) * size, w * 1.1, aa) * (half)step(wx, 1.0));
                    }
                }

                half3 c = PcInk(ink);
                return half4(c * pc, pc);
            }
            ENDHLSL
        }
    }
}
