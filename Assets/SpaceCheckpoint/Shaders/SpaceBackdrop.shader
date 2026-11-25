// Space checkpoint: the sky behind everything (the Saturn oil painting's skybox, with the launching shuttles of the
// teal / violet / amber rocket paintings far off in it). Drawn as a screen-filling quad pushed to the far plane, after
// the opaque scene and before the transparents, with a depth test - so it only shows where nothing else was drawn.
// Back to front:
//   sky       a sunset ramp laid in flat brush bands: plum-red ground haze, orange, gold, pink, violet, royal blue, indigo
//   swirls    long wind strokes across the sky (the painting's thick brush work), slowly drifting
//   stars     fine stars and a few four-point sparkles, twinkling, only where the sky is dark
//   planets   a ringed gold planet with oil-swirl bands, a huge cropped blue-violet planet with city sparks, two moons
//   rockets   shuttles lifting off the far horizon in billowing teal / violet / amber smoke, one climbing at a time or
//             so, each lit from below by its own flame; they are tiny and never anywhere near the path
//   mesas     a far row of red mesas on the horizon with a sunset rim
// Visible only where the space look is in force (SpaceCommon.hlsl, follows the look wipe). Driven by
// SpaceCheckpointController (_SpBackdrop).
Shader "SpaceCheckpoint/Backdrop"
{
    Properties
    {
        _Rockets ("Rocket Launches", Range(0, 1)) = 1
        _Stars ("Stars", Range(0, 1)) = 1
        _SwirlCover ("Swirl Cover", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+502" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "SpaceBackdrop"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/SpaceCheckpoint/Shaders/SpaceCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Rockets, _Stars, _SwirlCover;
            CBUFFER_END
            float4 _SpBackdrop;   // x: offset from the camera's yaw (screen widths), y: time, zw: camera travel (screen heights)

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

            // The painted palette (sRGB values from the references, converted by the compiler's linear math below).
            static const half3 kGround  = half3(0.36, 0.10, 0.20);
            static const half3 kRust    = half3(0.82, 0.24, 0.17);
            static const half3 kOrange  = half3(0.96, 0.60, 0.26);
            static const half3 kGold    = half3(0.98, 0.78, 0.40);
            static const half3 kPink    = half3(0.90, 0.46, 0.64);
            static const half3 kViolet  = half3(0.50, 0.36, 0.84);
            static const half3 kRoyal   = half3(0.17, 0.30, 0.78);
            static const half3 kIndigo  = half3(0.07, 0.10, 0.36);
            static const half3 kNight   = half3(0.04, 0.05, 0.17);

            float Fbm(float2 p)
            {
                float a = PixNoise(float3(p, 0.5)) * 0.5;
                a += PixNoise(float3(p * 2.03 + 5.3, 1.5)) * 0.27;
                a += PixNoise(float3(p * 4.1 + 1.7, 2.5)) * 0.15;
                a += PixNoise(float3(p * 8.3 + 9.1, 3.5)) * 0.08;
                return a;
            }

            half3 SrgbToLinearApprox(half3 c) { return c * (c * (c * 0.305306011h + 0.682171111h) + 0.012522878h); }

            // The sunset ramp in flat paint values: soft steps instead of a smooth gradient.
            half3 Ramp(float y)
            {
                half3 c = kGround;
                c = lerp(c, kRust,   (half)smoothstep(0.02, 0.14, y));
                c = lerp(c, kOrange, (half)smoothstep(0.14, 0.24, y));
                c = lerp(c, kGold,   (half)smoothstep(0.22, 0.30, y) * 0.8h);
                c = lerp(c, kPink,   (half)smoothstep(0.30, 0.42, y));
                c = lerp(c, kViolet, (half)smoothstep(0.42, 0.55, y));
                c = lerp(c, kRoyal,  (half)smoothstep(0.55, 0.70, y));
                c = lerp(c, kIndigo, (half)smoothstep(0.70, 0.86, y));
                c = lerp(c, kNight,  (half)smoothstep(0.86, 1.02, y));
                return c;
            }

            void Over(inout half3 c, half3 col, half k) { c = lerp(c, col, saturate(k)); }

            // A planet: oil-swirl bands, a lit limb, a night side, a glow round it. Returns coverage.
            half Planet(float2 P, float2 ctr, float R, half3 lit, half3 mid, half3 dark, float seed, float swirlAmt, float aa, inout half3 c)
            {
                float2 d = P - ctr;
                float r = length(d);
                // Atmosphere glow outside the limb.
                half halo = (half)(exp(-max(r - R, 0.0) / (R * 0.12)) * 0.35) * (half)step(R, r);
                c = lerp(c, lit * 1.1h, saturate(halo * 0.5h));
                if (r > R + aa) return 0.0h;
                float2 n2 = d / R;
                float z = sqrt(max(1.0 - dot(n2, n2), 0.0));
                // Light from the upper left.
                float3 N = float3(n2, z);
                half ndl = (half)saturate(dot(N, normalize(float3(-0.55, 0.6, 0.55))) * 0.5 + 0.5);
                // Swirling bands: latitude warped by the field itself.
                float warp = Fbm(float2(n2.x * 2.2 + seed, n2.y * 2.2)) * swirlAmt;
                float band = n2.y * 3.2 + warp * 3.0 + sin(n2.x * 2.0 + seed) * 0.4;
                half bv = (half)(0.5 + 0.5 * sin(band * 3.1416));
                half stripe = SpBands(bv, 3.0h);
                half3 body = lerp(mid, lit, stripe);
                // Thick paint blobs.
                half blob = (half)step(0.7, PixNoise(float3(n2 * 5.0 + seed, 2.0)));
                body = lerp(body, lit * 1.15h, blob * 0.5h);
                body = lerp(dark, body, SpBands(ndl, 3.0h));
                body = lerp(body, lit * 1.2h, (half)smoothstep(0.78, 0.97, r / R) * ndl * 0.35h);   // bright limb on the lit side
                half cov = (half)(1.0 - smoothstep(R - aa, R + aa, r));
                c = lerp(c, body, cov);
                return cov;
            }

            // Saturn-style rings; front = the half in front of the planet. Returns coverage.
            half Ring(float2 P, float2 ctr, float R, float tilt, bool frontHalf, float aa, inout half3 c)
            {
                float2 d = P - ctr;
                float cs = cos(tilt), sn = sin(tilt);
                float2 q = float2(d.x * cs + d.y * sn, -d.x * sn + d.y * cs);
                float e = length(float2(q.x, q.y * 3.3)) / R;
                if (e < 1.35 || e > 2.35) return 0.0h;
                bool isFront = q.y < 0.0;
                if (isFront != frontHalf) return 0.0h;
                half band = (half)(0.5 + 0.5 * sin(e * 30.0 + Fbm(float2(e * 6.0, 1.0)) * 6.0));
                half3 rc = lerp(half3(0.18, 0.32, 0.72), half3(0.80, 0.86, 0.96), SpBands(band, 3.0h));
                rc = lerp(rc, kGold, (half)step(0.8, PixNoise(float3(e * 14.0, 3.0, 1.0))) * 0.5h);
                half cov = (half)(smoothstep(1.35, 1.42, e) * (1.0 - smoothstep(2.25, 2.35, e))) * 0.92h;
                // The ring is shadowed by the planet behind it.
                c = lerp(c, rc, cov);
                return cov;
            }

            // A far shuttle lifting off: smoke column (billowing, outlined, lit from below), flame, tiny orbiter.
            void Rocket(float2 P, float aspect, float slot, float period, float phase, float xMin, float xMax,
                        half3 smokeLit, half3 smokeShade, float t, float aa, inout half3 c)
            {
                float tt = t + phase;
                float cyc = floor(tt / period);
                float lt = tt - cyc * period;
                const float climb = 7.5, life = 15.0;
                if (lt > life) return;
                float h1 = PixHash(float3(cyc, slot, 1.0)), h2 = PixHash(float3(cyc, slot, 2.0)), h3 = PixHash(float3(cyc, slot, 3.0));
                float x0 = lerp(xMin, xMax, h1) * aspect;
                float y0 = 0.17 + h2 * 0.05;                 // the launch site, just above the horizon
                float s = saturate(lt / climb);
                float headY = y0 + (0.58 - y0) * s * s;      // it accelerates away
                float lean = (h3 - 0.5) * 0.5;
                float dy = P.y - y0;
                if (dy < -0.015 || P.y > headY + 0.06) return;
                float xc = x0 + lean * dy * dy * 2.2 + sin(dy * 11.0 + cyc * 2.0) * 0.004 * saturate(dy * 6.0);
                float dx = P.x - xc;
                if (abs(dx) > 0.05) return;

                bool flying = lt < climb;
                float span = max(headY - y0, 0.03);
                float age = saturate(1.0 - dy / span);       // 0 at the head .. 1 at the pad
                float grow = 1.0 + saturate((lt - 2.0) / 12.0) * 0.6;
                float r = lerp(0.0025, 0.011, pow(age, 0.75)) * grow;
                float2 bp = float2(dx, dy) * 90.0 + float2(cyc * 7.3, slot * 11.0);
                float n = Fbm(bp + float2(0.0, -t * 0.25));
                float n2 = PixNoise(float3(bp * 2.4, cyc));
                float dens = r - abs(dx) + (n - 0.5) * r * 1.1 + (n2 - 0.5) * r * 0.35;
                half life_a = (half)(1.0 - smoothstep(10.5, life, lt)) * (half)smoothstep(-0.02, 0.01, dy);
                half smoke = (half)smoothstep(-aa, aa, dens) * (half)step(dy, span + 0.02) * life_a;
                if (smoke > 0.001h)
                {
                    // Three paint values from the billow noise, a shaded side away from the pad's glow.
                    half sh = (half)saturate(0.55 + (n - 0.5) * 1.6 + (-dx / max(r, 1e-3)) * 0.25);
                    half3 col = lerp(smokeShade, smokeLit, SpBands(sh, 3.0h));
                    // Fire light from below: the base of the column glows orange / pink.
                    half fire = (half)(1.0 - smoothstep(0.0, 0.2, dy)) * (half)lerp(0.55, 1.0, n);
                    col = lerp(col, lerp(kOrange * 1.15h, kGold, (half)n), fire * 0.85h);
                    // Under-lit edges toward the head.
                    half nearHead = (half)(1.0 - smoothstep(0.0, 0.16, headY - P.y)) * (flying ? 1.0h : 0.0h);
                    col = lerp(col, kOrange * 1.2h, nearHead * 0.45h);
                    // Ink outline, like the line-art clouds.
                    half rim = (half)(1.0 - smoothstep(0.0, 0.0022 + aa, dens));
                    col = lerp(col, smokeShade * 0.28h, rim * 0.75h);
                    c = lerp(c, col, smoke * 0.96h);
                }
                if (flying)
                {
                    float fd = headY - P.y;
                    if (fd > -0.002 && fd < 0.1)
                    {
                        float wf = 0.0042 * (1.0 - fd / 0.1) + 0.0007;
                        half flame = (half)(1.0 - smoothstep(wf - aa, wf + aa, abs(dx))) * (half)(1.0 - fd / 0.1);
                        half3 fc = lerp(kOrange * 1.3h, half3(1.0h, 0.97h, 0.85h), (half)(1.0 - fd / 0.1));
                        c = lerp(c, fc, flame);
                        // A soft warm halo round the engine.
                        float hd = length(float2(dx, fd + 0.01));
                        c += kOrange * (half)(exp(-hd * 90.0) * 0.45);
                    }
                    // The orbiter and its tank: white body, red nose, two boosters.
                    float by = P.y - headY;
                    if (by > 0.0 && by < 0.034)
                    {
                        float w = 0.0052 * (by < 0.027 ? 1.0 : (1.0 - (by - 0.027) / 0.007));
                        half body = (half)(1.0 - smoothstep(w - aa, w + aa, abs(dx)));
                        half3 bc = by > 0.024 ? half3(0.85h, 0.2h, 0.16h) : half3(0.95h, 0.94h, 0.9h);
                        bc = lerp(bc, half3(0.62h, 0.66h, 0.8h), (half)step(0.0, dx) * 0.45h);
                        c = lerp(c, bc, body);
                        // Side boosters.
                        half boost = (half)(1.0 - smoothstep(0.0016 - aa, 0.0016 + aa, abs(abs(dx) - 0.0062))) * (half)step(by, 0.02);
                        c = lerp(c, half3(0.92h, 0.9h, 0.88h), boost);
                    }
                }
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float t = _SpBackdrop.y;
                // Yaw / travel parallax shifts the sky a little.
                float2 uv = input.uv;
                uv.x += _SpBackdrop.x;
                uv.y += _SpBackdrop.w * 0.2;
                float2 P = float2(uv.x * aspect, uv.y);
                float aa = max(fwidth(P.y), 1e-4) * 1.2;

                // ---- sky ramp, bent by slow brush flow so the bands are strokes, not a gradient ----
                float flow = (Fbm(float2(P.x * 1.3, P.y * 5.0 + t * 0.004)) - 0.5) * 0.07;
                float ys = uv.y + flow;
                half3 c = Ramp(ys);
                half band = (half)PixNoise(float3(P.x * 1.6, ys * 14.0, 4.0));
                c *= 0.94h + 0.12h * band;

                // ---- swirl strokes (lighter and darker wind marks across the sky) ----
                {
                    float bend = Fbm(P * 0.9 + float2(0.0, t * 0.006)) * 5.0;
                    float2 dir = float2(cos(bend), sin(bend) * 0.5);
                    float2 q = float2(dot(P, dir), dot(P, float2(-dir.y, dir.x)));
                    float f = Fbm(float2(q.x * 1.1 + t * 0.01, q.y * 7.0) + 3.1);
                    float edge = 1.0 - _SwirlCover * 0.5;
                    half mark = (half)smoothstep(edge, edge + 0.05, f) * (half)smoothstep(0.1, 0.5, uv.y);
                    half3 lighter = c * 1.35h + 0.05h;
                    half3 darker = c * 0.7h;
                    c = lerp(c, f > edge + 0.14 ? darker : lighter, mark * 0.55h);
                }

                // ---- stars ----
                {
                    half dark = (half)smoothstep(0.5, 0.78, uv.y) * _Stars;
                    if (dark > 0.01h)
                    {
                        float2 g = P * 48.0;
                        float2 gi = floor(g);
                        float h = PixHash2(gi);
                        if (h > 0.78)
                        {
                            float2 pos = float2(PixHash2(gi + 3.7), PixHash2(gi + 9.1)) * 0.6 + 0.2;
                            float2 d = frac(g) - pos;
                            float big = step(0.93, h);
                            float rr = lerp(0.07, 0.15, big);
                            float tw = 0.65 + 0.35 * sin(t * (1.5 + h * 3.0) + h * 40.0);
                            half st = (half)(1.0 - smoothstep(rr * 0.5, rr, length(d))) * (half)tw;
                            // Four-point sparkle on the big ones.
                            half spark = (half)(big * max(1.0 - smoothstep(0.0, 0.03, abs(d.x)), 1.0 - smoothstep(0.0, 0.03, abs(d.y))) * (1.0 - smoothstep(0.0, 0.45, length(d))));
                            half3 sc = lerp(half3(1.0h, 0.96h, 0.86h), half3(0.8h, 0.88h, 1.0h), (half)frac(h * 17.0));
                            c = lerp(c, sc, saturate(st + spark * 0.8h) * dark);
                        }
                    }
                }

                // ---- planets (screen position by yaw parallax; sizes in screen heights) ----
                float2 ringedC = float2(0.70 * aspect, 0.745);
                float ringedR = 0.082;
                float ringTilt = -0.42;
                {
                    // The huge blue-violet planet, cropped by the top-left corner, with orange city sparks.
                    float2 bigC = float2(0.0 * aspect, 0.90);
                    Planet(P, bigC, 0.21, half3(0.48, 0.62, 0.98), half3(0.30, 0.30, 0.82), half3(0.06, 0.07, 0.30), 11.0, 0.9, aa, c);
                    float2 dB = (P - bigC) / 0.21;
                    if (length(dB) < 0.96)
                    {
                        half spark = (half)step(0.93, PixNoise(float3(dB * 60.0, 6.0))) * (half)step(0.35, PixNoise(float3(dB * 3.0, 9.0)));
                        c = lerp(c, kOrange * 1.2h, spark * 0.85h);
                    }
                    // Two small moons.
                    Planet(P, float2(0.30 * aspect, 0.64), 0.028, half3(0.82, 0.76, 0.98), half3(0.5, 0.42, 0.78), half3(0.14, 0.12, 0.38), 3.0, 0.5, aa, c);
                    Planet(P, float2(0.92 * aspect, 0.50), 0.02, half3(0.98, 0.8, 0.7), half3(0.82, 0.5, 0.5), half3(0.3, 0.14, 0.3), 7.0, 0.4, aa, c);
                    // The ringed planet: back half of the ring, the body, then the front half.
                    Ring(P, ringedC, ringedR, ringTilt, false, aa, c);
                    Planet(P, ringedC, ringedR, half3(1.0, 0.82, 0.48), half3(0.88, 0.5, 0.2), half3(0.28, 0.12, 0.12), 5.0, 0.7, aa, c);
                    Ring(P, ringedC, ringedR, ringTilt, true, aa, c);
                }

                // ---- far rocket launches (everyone leaving) ----
                if (_Rockets > 0.01h)
                {
                    Rocket(P, aspect, 0.0, 17.0, 3.0,  0.04, 0.2, half3(0.58, 0.84, 0.86), half3(0.16, 0.38, 0.52), t, aa, c);   // teal
                    Rocket(P, aspect, 1.0, 23.0, 11.0, 0.8, 0.96, half3(0.80, 0.78, 0.97), half3(0.30, 0.28, 0.62), t, aa, c);   // violet
                    Rocket(P, aspect, 2.0, 29.0, 20.0, 0.25, 0.75, half3(0.98, 0.78, 0.40), half3(0.62, 0.38, 0.40), t, aa, c);   // amber
                }

                // ---- far mesas on the horizon: red, hazed, with a sunset rim on the tops ----
                {
                    float foot = 0.15;
                    float fx = P.x * 9.0 + _SpBackdrop.z * 3.0;
                    float ci = floor(fx);
                    float ch = lerp(0.025, 0.085, PixHash2(float2(ci, 4.0)));
                    float slope = smoothstep(0.32, 0.5, abs(frac(fx) - 0.5));
                    float top = foot + ch * (1.0 - 0.5 * slope) + (Fbm(float2(P.x * 14.0, 2.0)) - 0.5) * 0.008;
                    if (uv.y < top)
                    {
                        half3 m = lerp(half3(0.50, 0.17, 0.26), half3(0.74, 0.30, 0.30), (half)saturate((uv.y - foot + 0.05) * 5.0));
                        half rim = (half)(1.0 - smoothstep(0.0, 0.012, top - uv.y));
                        m = lerp(m, kOrange, rim * 0.8h);
                        m = lerp(m, Ramp(uv.y + 0.02), 0.35h);   // atmospheric haze
                        c = m;
                    }
                }

                // Colour values above are sRGB-ish; the framebuffer is linear.
                c = SrgbToLinearApprox(saturate(c));
                half sp = SpAt(input.uv);
                return half4(c * sp, sp);
            }
            ENDHLSL
        }
    }
}
