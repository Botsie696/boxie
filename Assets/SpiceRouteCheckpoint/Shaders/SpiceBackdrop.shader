// Spice Route checkpoint: the painting behind everything. Drawn as a screen-filling quad at the far plane after the opaque
// scene (depth tested, so it only shows where nothing else was drawn). Two paintings on hand-made paper, swapped by a
// torn-paper sweep when the runner crosses into the other half (_SrTear):
//
// India (a Vishvarupa miniature + a Rajasthani collage)
//   sky       night-blue top with gold stars, a rayed golden sun and a crescent moon, scroll clouds with curled edges
//             (the miniature's sky); below them warm parchment, apricot, peach, rose and plum paper
//   colossus  a giant crowned blue god with a fan of crowned heads and a mandala halo, the worlds painted inside his
//             body (tiny temples and figures in bands), a lotus in his raised hand - faded by distance
//   hills     a far violet range with hill forts (crenellated walls, bastions, chhatri domes), a near terracotta range
//             with temple shikharas, and a needle rock with a white temple on top
//   life      peacocks on the near ridge (one dancing with its fan open), kites on long strings, birds
//   collage   a blue fish-scale panel torn into the top-left corner, gold-leaf flecks
// Arabia (a Cairo / Istanbul dusk, an Arabian-nights palace)
//   sky       a dusk ramp - violet, plum, red-orange, amber, rose, lavender, indigo - stars, a faint gold girih net,
//             a big pale moon with craters and a gold crescent
//   city      three skyline layers: far lavender domes and pencil minarets, a violet city with lit windows (a grand
//             mosque with semi-domes and four minarets, a palace of teal onion domes), dark minarets and palms in front
//   life      flying carpets drifting across, flocks of birds
// Visible only where the spice look is in force (follows the look wipe). Driven by SpiceRouteCheckpointController.
Shader "SpiceRouteCheckpoint/Backdrop"
{
    Properties
    {
        _Colossus ("Colossus", Range(0, 1)) = 1
        _Haze ("Distance Haze On The Colossus", Range(0, 1)) = 0.4
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+502" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "SpiceBackdrop"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/SpiceRouteCheckpoint/Shaders/SpiceCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Colossus, _Haze;
            CBUFFER_END
            float4 _SrBackdrop;   // x: offset from the camera's yaw (screen widths), y: time, zw: camera travel

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

            static const float3 kNight   = float3(0.06, 0.12, 0.28);
            static const float3 kNight2  = float3(0.12, 0.23, 0.45);
            static const float3 kCloud   = float3(0.80, 0.83, 0.86);
            static const float3 kCloudInk= float3(0.16, 0.22, 0.36);
            static const float3 kParch   = float3(0.95, 0.87, 0.71);
            static const float3 kApricot = float3(0.96, 0.76, 0.48);
            static const float3 kPeach   = float3(0.95, 0.63, 0.48);
            static const float3 kRose    = float3(0.85, 0.46, 0.54);
            static const float3 kPlum    = float3(0.43, 0.23, 0.37);
            static const float3 kGoldS   = float3(0.88, 0.66, 0.24);

            float Wrap(float x, float period) { return (frac(x / period + 0.5) - 0.5) * period; }
            void Over(inout float3 c, float3 col, float k) { c = lerp(c, col, saturate(k)); }
            float Fill(float d, float aa) { return 1.0 - smoothstep(-aa, aa, d); }
            float Ln(float d, float w, float aa) { return 1.0 - smoothstep(w - aa, w + aa, abs(d)); }

            float3 Gold(float2 p, float lit)
            {
                float n = SrNoise2(p * 40.0);
                float3 c = lerp(float3(0.62, 0.42, 0.12), float3(0.95, 0.75, 0.32), saturate(lit * 0.8 + n * 0.3));
                return lerp(c, float3(1.0, 0.93, 0.68), step(0.9, PixHash2(floor(p * 160.0))) * 0.7);
            }

            // ======================================================================== India

            float3 IndiaRamp(float y)
            {
                float3 c = kPlum;
                c = lerp(c, kRose,    smoothstep(0.06, 0.2, y));
                c = lerp(c, kPeach,   smoothstep(0.2, 0.32, y));
                c = lerp(c, kApricot, smoothstep(0.32, 0.44, y));
                c = lerp(c, kParch,   smoothstep(0.44, 0.56, y));
                return c;
            }

            // A crowned head: face ellipse, almond eyes, tilak, tiered gold crown. Coverage into c.
            void Head(float2 P, float2 ctr, float s, float3 face, float aa, inout float3 c)
            {
                float2 d = (P - ctr) / s;
                if (abs(d.x) > 1.6 || d.y < -1.4 || d.y > 2.8) return;
                float a = aa / s;
                float crown = max(abs(d.x) * 1.5 + (d.y - 0.6) * 0.55 - 0.95, -(d.y - 0.55));
                crown = min(crown, length(d - float2(0.0, 2.25)) - 0.22);
                float ear = min(length(d - float2(-0.95, -0.05)) - 0.22, length(d - float2(0.95, -0.05)) - 0.22);
                float fc = min(SrEllipse(d, float2(0.82, 1.0)), ear);
                Over(c, kGoldS * 0.6, Fill(crown - 0.08, a));
                Over(c, Gold(P, 0.8 + 0.2 * sin(d.y * 12.0)), Fill(crown, a));
                Over(c, kCloudInk * 0.6, Fill(fc - 0.06, a));
                Over(c, face, Fill(fc, a));
                float eyes = min(SrEllipse(d - float2(-0.33, 0.1), float2(0.22, 0.09)), SrEllipse(d - float2(0.33, 0.1), float2(0.22, 0.09)));
                Over(c, float3(0.98, 0.95, 0.88), Fill(eyes, a));
                Over(c, float3(0.06, 0.04, 0.06), Fill(min(length(d - float2(-0.3, 0.1)), length(d - float2(0.36, 0.1))) - 0.07, a));
                Over(c, float3(0.85, 0.15, 0.12), Fill(SrEllipse(d - float2(0.0, 0.45), float2(0.06, 0.16)), a));
                Over(c, float3(0.55, 0.12, 0.15), Fill(SrEllipse(d - float2(0.0, -0.5), float2(0.22, 0.07)), a));
                // Earrings.
                Over(c, Gold(P, 0.9), Fill(min(length(d - float2(-0.95, -0.35)), length(d - float2(0.95, -0.35))) - 0.13, a));
            }

            void Colossus(float2 P, float xc, float aa, float t, inout float3 c)
            {
                const float S = 0.62;
                P = float2(xc, 0.5) + (P - float2(xc, 0.57)) / S;
                aa /= S;
                float lx = P.x - xc;
                if (abs(lx) > 0.32 || P.y > 0.82) return;
                float3 before = c;
                float2 hc = float2(xc, 0.5);

                // The halo: a saffron disc with mandala rings of petals behind the heads.
                float2 hq = P - hc;
                float hr = length(hq);
                if (hr < 0.2)
                {
                    float3 halo = lerp(float3(0.98, 0.72, 0.36), float3(0.98, 0.86, 0.6), smoothstep(0.2, 0.05, hr));
                    float pet1 = SrPetals(hq, 24.0, 0.19, 0.18, t * 0.02);
                    float pet2 = SrPetals(hq, 16.0, 0.145, 0.25, -t * 0.03);
                    halo = lerp(halo, float3(0.92, 0.45, 0.3), Ln(pet1, 0.0025, aa));
                    halo = lerp(halo, Gold(P, 0.9), Ln(pet2, 0.003, aa));
                    halo = lerp(halo, float3(0.85, 0.3, 0.4), Ln(hr - 0.17, 0.002, aa));
                    float rays = abs(frac(atan2(hq.y, hq.x) / 6.2832 * 48.0) - 0.5);
                    halo = lerp(halo, float3(1.0, 0.9, 0.62), step(rays, 0.12) * smoothstep(0.1, 0.13, hr) * (1.0 - smoothstep(0.135, 0.14, hr)) * 0.6);
                    Over(c, halo, Fill(hr - 0.2, aa) * 0.92);
                    Over(c, float3(0.55, 0.18, 0.2), Ln(hr - 0.2, 0.002, aa));
                }

                // Body: shoulders, chest, torso down behind the hills - the worlds painted inside it.
                float sh = SrEllipse(P - float2(xc, 0.39), float2(0.15, 0.075));
                float torso = SrBox(P - float2(xc, 0.25), float2(0.11 - (0.3 - P.y) * 0.05, 0.14));
                float armL = SrSeg(P, float2(xc - 0.13, 0.4), float2(xc - 0.21, 0.5)) - 0.022;
                float foreL = SrSeg(P, float2(xc - 0.21, 0.5), float2(xc - 0.19, 0.58)) - 0.018;
                float armR = SrSeg(P, float2(xc + 0.13, 0.4), float2(xc + 0.19, 0.46)) - 0.022;
                float foreR = SrSeg(P, float2(xc + 0.19, 0.46), float2(xc + 0.17, 0.53)) - 0.018;
                float handR = SrEllipse(P - float2(xc + 0.168, 0.548), float2(0.017, 0.022));
                float body = min(min(sh, torso), min(min(armL, foreL), min(min(armR, foreR), handR)));
                if (body < aa * 2.0)
                {
                    float3 skin = float3(0.2, 0.36, 0.62);
                    float3 world = lerp(float3(0.14, 0.33, 0.36), skin, 0.35);
                    // Bands of painted worlds: tiny temples, people, trees.
                    float band = floor(P.y * 40.0);
                    float2 cq = float2(frac(P.x * 60.0 + band * 0.37) - 0.5, frac(P.y * 40.0) - 0.5);
                    float h = PixHash(float3(floor(P.x * 60.0 + band * 0.37), band, 3.0));
                    float3 ink = h < 0.3 ? float3(0.93, 0.85, 0.65) : h < 0.55 ? float3(0.85, 0.66, 0.25) : h < 0.75 ? float3(0.8, 0.3, 0.25) : float3(0.3, 0.6, 0.35);
                    float temple = min(SrBox(cq - float2(0.0, -0.2), float2(0.3, 0.2)), max(abs(cq.x) * 1.4 + cq.y - 0.35, -cq.y));
                    float figure = min(length(cq - float2(0.0, 0.25)) - 0.13, max(abs(cq.x) * 2.0 - (0.15 - cq.y) * 0.9, cq.y - 0.15));
                    float m = h < 0.5 ? temple : figure;
                    world = lerp(world, ink, Fill(m / 40.0, aa) * step(0.2, frac(h * 7.0)));
                    world = lerp(world, float3(0.07, 0.15, 0.22), Ln(frac(P.y * 40.0) - 0.02, 0.04, 0.05) * 0.5);
                    bool limb = min(min(armL, foreL), min(min(armR, foreR), handR)) < 0.0;
                    float3 bc = limb ? skin * (0.9 + 0.2 * SrNoise2(P * 80.0)) : world;
                    // Jewellery: a gold necklace on the shoulders, armlets.
                    bc = lerp(bc, Gold(P, 0.9), Ln(length((P - float2(xc, 0.43)) * float2(1.0, 1.6)) - 0.07, 0.006, aa));
                    bc = lerp(bc, Gold(P, 0.9), Fill(min(length(P - float2(xc - 0.17, 0.45)), length(P - float2(xc + 0.16, 0.43))) - 0.016, aa));
                    Over(c, kCloudInk * 0.5, Fill(body - 0.003, aa));
                    Over(c, bc, Fill(body, aa));
                    // A lotus in the raised left hand.
                    float2 lq = P - float2(xc - 0.19, 0.6);
                    float lotus = SrPetals(lq, 7.0, 0.03, 0.5, 1.5708);
                    Over(c, float3(0.95, 0.55, 0.65), Fill(max(lotus, -lq.y - 0.005), aa));
                    Over(c, float3(0.7, 0.2, 0.35), Ln(max(lotus, -lq.y - 0.005), 0.0015, aa));
                }

                // The fan of heads: the main blue face, then crowned heads round it in an arc, alternately coloured.
                float3 faces[5] = { float3(0.93, 0.89, 0.8), float3(0.36, 0.62, 0.4), float3(0.78, 0.36, 0.25), float3(0.92, 0.72, 0.36), float3(0.4, 0.56, 0.82) };
                [unroll] for (int i = 3; i >= 0; i--)
                {
                    float ang = 0.55 + i * 0.36;
                    float s = 0.026 - i * 0.002;
                    float2 off = float2(sin(ang), cos(ang) - 0.25) * 0.095;
                    Head(P, hc + float2(-off.x, off.y), s, faces[i % 5], aa, c);
                    Head(P, hc + float2(off.x, off.y), s, faces[(i + 2) % 5], aa, c);
                }
                Head(P, hc + float2(0.0, -0.005), 0.034, float3(0.3, 0.5, 0.82), aa, c);

                // Far away: a share of the paper's warm haze over all of him.
                c = lerp(before, lerp(c, lerp(before, IndiaRamp(P.y), 0.5) * 0.9 + 0.1, _Haze), _Colossus);
            }

            void Scrolls(float2 P, float yb, float thick, float aa, float t, inout float3 c)
            {
                // Scroll clouds: a bank of scalloped puffs with an inked edge and a curled line inside each.
                float x = P.x * 18.0 + t * 0.02;
                float ci = floor(x);
                float cf = frac(x) - 0.5;
                float rh = 0.012 + 0.012 * PixHash2(float2(ci, 5.0));
                float puff = length(float2(cf / 18.0, (P.y - yb) * 1.2)) - (0.02 + rh);
                float bank = max(min(puff, P.y - yb), yb - thick - P.y);
                if (bank < 0.05)
                {
                    float3 cc = lerp(kCloud * 0.86, kCloud, smoothstep(-0.03, 0.0, bank));
                    Over(c, cc, Fill(bank, aa));
                    Over(c, kCloudInk, Ln(bank, 0.0012, aa));
                    float curl = length(float2(cf / 18.0, (P.y - yb) * 1.2) - float2(0.004, 0.004)) - (0.012 + rh * 0.5);
                    Over(c, kCloudInk * 1.4, Ln(curl, 0.0009, aa) * step(0.0, P.y - yb - 0.002) * Fill(bank, aa));
                }
            }

            float3 India(float2 P, float2 uv, float aspect, float aa, float t)
            {
                float flow = (SrFbm(float2(P.x * 1.4, P.y * 5.0 + t * 0.004)) - 0.5) * 0.05;
                float y = uv.y + flow;
                float3 c = IndiaRamp(y);
                c *= 0.95 + 0.1 * SrNoise2(float2(P.x * 3.0, y * 16.0));

                // ---- the night sky over the paper, ended below by scroll clouds ----
                float yb = 0.645 + 0.012 * sin(P.x * 7.0 + 1.0) + (SrFbm(float2(P.x * 3.0, 1.0)) - 0.5) * 0.04;
                if (uv.y > yb - 0.06)
                {
                    float3 night = lerp(kNight2, kNight, smoothstep(yb, 1.0, uv.y));
                    night *= 0.85 + 0.3 * SrFbm(P * float2(6.0, 10.0));
                    float3 sky = night;
                    // Stars: gold dots and small four-point stars.
                    float2 g = P * 70.0;
                    float2 gi = floor(g);
                    float h = PixHash2(gi);
                    if (h > 0.8)
                    {
                        float2 d = frac(g) - (float2(PixHash2(gi + 3.7), PixHash2(gi + 9.1)) * 0.6 + 0.2);
                        float tw = 0.7 + 0.3 * sin(t * (1.0 + h * 2.0) + h * 30.0);
                        float star = h > 0.95 ? min(abs(d.x) + abs(d.y) * 4.0, abs(d.y) + abs(d.x) * 4.0) - 0.3 : length(d) - 0.12;
                        sky = lerp(sky, kGoldS * 1.25, Fill(star / 70.0, aa) * tw);
                    }
                    // The sun (left): a gold disc with wavy rays. The crescent moon (right).
                    float2 sc = float2(aspect * 0.2, 0.86);
                    float2 sq = P - sc;
                    float sr = length(sq);
                    float ra = atan2(sq.y, sq.x);
                    float rays = sr - (0.05 + 0.012 * sin(ra * 16.0) + 0.006 * sin(ra * 32.0 + sr * 200.0));
                    Over(sky, float3(0.95, 0.55, 0.18), Fill(rays, aa) * step(0.035, sr));
                    Over(sky, Gold(P, 0.95), Fill(sr - 0.036, aa));
                    Over(sky, float3(0.85, 0.35, 0.12), Ln(sr - 0.036, 0.0015, aa));
                    float2 mc = float2(aspect * 0.83, 0.9);
                    float moon = max(length(P - mc) - 0.03, -(length(P - mc - float2(0.012, 0.008)) - 0.026));
                    Over(sky, float3(0.98, 0.94, 0.78), Fill(moon, aa));
                    // Little scroll puffs drifting in the night.
                    Scrolls(P + float2(t * 0.003, 0.0), 0.8 + 0.02 * sin(P.x * 3.0), 0.012, aa, t, sky);
                    // The blue fish-scale panel torn into the top-left corner (the collage's piece of patterned paper).
                    float panel = max(P.x - (0.12 + 0.02 * SrNoise2(float2(uv.y * 30.0, 2.0))), 0.86 + 0.02 * SrNoise2(float2(P.x * 30.0, 4.0)) - uv.y);
                    if (panel < 0.01)
                    {
                        float2 fp = float2(P.x * 30.0, uv.y * 36.0);
                        float row = floor(fp.y);
                        float2 fq = float2(frac(fp.x + fmod(row, 2.0) * 0.5) - 0.5, frac(fp.y));
                        float3 fc = lerp(float3(0.18, 0.36, 0.62), float3(0.32, 0.55, 0.78), SrNoise2(P * 30.0));
                        fc = lerp(fc, Gold(P, 0.85), Ln(length(fq) - 0.55, 0.06, 0.06));
                        fc = lerp(fc, kParch, step(0.8, SrNoise2(P * 120.0)) * 0.45);   // distressed
                        Over(sky, fc, Fill(panel, aa));
                        Over(sky, float3(1.0, 0.98, 0.93), Ln(panel, 0.0015, aa));
                    }
                    Over(c, sky, smoothstep(yb - 0.002, yb + 0.002, uv.y));
                    Scrolls(P, yb, 0.03, aa, t, c);
                }

                // ---- the colossus ----
                if (_Colossus > 0.01)
                {
                    float xc = aspect * 0.5 + Wrap(P.x - aspect * 0.5, 1.8) - (P.x - aspect * 0.5);
                    Colossus(P, xc, aa, t, c);
                }

                // ---- kites on strings ----
                [unroll] for (int k = 0; k < 3; k++)
                {
                    float kx = aspect * (0.18 + 0.32 * k) + 0.02 * sin(t * 0.4 + k * 2.0);
                    float ky = 0.6 + 0.07 * k + 0.012 * sin(t * 0.7 + k);
                    float2 kq = SrRot(P - float2(kx, ky), 0.25 * sin(t * 0.5 + k * 1.3));
                    float kite = abs(kq.x) + abs(kq.y) * 0.8 - 0.016;
                    float3 kc = k == 0 ? float3(0.92, 0.3, 0.5) : k == 1 ? float3(0.98, 0.6, 0.15) : float3(0.25, 0.65, 0.45);
                    Over(c, kc, Fill(kite, aa));
                    Over(c, float3(0.3, 0.1, 0.15), Ln(min(abs(kq.x), abs(kq.y)), 0.0006, aa) * Fill(kite, aa));
                    float tail = SrSeg(P, float2(kx, ky - 0.018), float2(kx + 0.004 * sin(t + k), ky - 0.04)) - 0.0015;
                    Over(c, kc, Fill(tail, aa));
                    float2 sp = P - float2(kx, ky);
                    float strg = abs(sp.x - (sp.y) * (-0.35 + 0.1 * k) - 0.002 * sin(sp.y * 40.0));
                    Over(c, float3(0.35, 0.2, 0.2), Ln(strg, 0.0004, aa) * step(P.y, ky) * 0.5);
                }

                // ---- far hills with forts ----
                float fx = P.x + _SrBackdrop.z * 0.6;
                float ridgeF = 0.3 + 0.05 * SrFbm(float2(fx * 3.0, 3.0)) + 0.025 * sin(fx * 5.0);
                float cell = floor(fx * 9.0);
                float fort = PixHash2(float2(cell, 11.0));
                float wallTop = ridgeF;
                if (fort > 0.45)
                {
                    float lf = frac(fx * 9.0);
                    float span = step(0.15, lf) * step(lf, 0.85);
                    float merlon = step(0.5, frac(fx * 260.0)) * 0.004;
                    float bast = step(abs(lf - 0.18), 0.04) + step(abs(lf - 0.82), 0.04);
                    wallTop = ridgeF + span * (0.016 + merlon) + bast * 0.012;
                    float dome = length(float2((lf - 0.5) / 9.0, uv.y - ridgeF - 0.028)) - 0.009;
                    if (fort > 0.75 && dome < 0.0) wallTop = max(wallTop, uv.y + 0.0001);
                }
                if (uv.y < wallTop)
                {
                    float3 m = lerp(float3(0.55, 0.47, 0.66), float3(0.72, 0.6, 0.74), smoothstep(ridgeF - 0.12, ridgeF, uv.y));
                    m = lerp(m, float3(0.95, 0.85, 0.82), step(ridgeF + 0.002, uv.y) * 0.45);   // the fort walls catch the light
                    float wins = step(0.75, PixHash2(floor(float2(fx * 220.0, uv.y * 200.0)))) * step(ridgeF + 0.004, uv.y);
                    m = lerp(m, float3(0.45, 0.2, 0.35), wins * 0.6);
                    m = lerp(m, IndiaRamp(uv.y + 0.05), 0.3);
                    c = m;
                    Over(c, float3(0.98, 0.8, 0.6), Ln(uv.y - wallTop, 0.0012, aa) * 0.6);
                }

                // ---- near hills: terracotta, temple shikharas, a needle rock with a white temple ----
                float nx = P.x + _SrBackdrop.z * 1.2;
                float ridgeN = 0.19 + 0.06 * SrFbm(float2(nx * 4.0, 7.0));
                float nc = floor(nx * 5.0);
                float nf = frac(nx * 5.0) - 0.5;
                float th = PixHash2(float2(nc, 21.0));
                float topN = ridgeN;
                if (th > 0.55)
                {
                    float ty = (uv.y - ridgeN) / 0.06;
                    float hw = 0.022 * pow(saturate(1.0 - ty), 0.6);
                    if (abs(nf / 5.0) < hw && ty > 0.0 && ty < 1.0) topN = uv.y + 0.0001;
                    if (length(float2(nf / 5.0, uv.y - ridgeN - 0.064)) < 0.004) topN = uv.y + 0.0001;
                }
                // The needle rock.
                float needleX = Wrap(nx - aspect * 0.8, 1.3);
                float needle = abs(needleX) - 0.03 * saturate(1.0 - (uv.y - 0.15) / 0.3) - 0.008;
                bool rock = needle < 0.0 && uv.y < 0.45;
                bool shrine = SrBox(float2(needleX, uv.y - 0.462), float2(0.016, 0.012)) < 0.0 || length(float2(needleX, uv.y - 0.474)) < 0.01;
                if (uv.y < topN || rock || shrine)
                {
                    float3 m = lerp(float3(0.5, 0.2, 0.26), float3(0.78, 0.38, 0.32), smoothstep(ridgeN - 0.15, ridgeN, uv.y));
                    m *= 0.9 + 0.2 * SrNoise2(float2(nx * 40.0, uv.y * 80.0));
                    if (rock && uv.y > ridgeN) m = lerp(float3(0.62, 0.36, 0.34), float3(0.8, 0.55, 0.45), step(0.0, needleX));
                    if (shrine) m = needleX > 0.0 ? float3(0.94, 0.9, 0.84) : float3(0.98, 0.95, 0.9);
                    if (uv.y > ridgeN && th > 0.55 && !rock && !shrine) m = lerp(float3(0.85, 0.55, 0.38), float3(0.95, 0.72, 0.5), step(0.0, nf));
                    c = m;
                }
                // A zig-zag pilgrim path up the needle.
                if (rock && uv.y > 0.2) Over(c, float3(0.95, 0.85, 0.7), Ln(needleX - 0.02 * (abs(frac(uv.y * 14.0) - 0.5) * 2.0 - 0.5), 0.0008, aa) * 0.8);

                // ---- peacocks flying far off like birds: blue body, rufous wings beating, a long green train with gold eyes ----
                [unroll] for (int b = 0; b < 3; b++)
                {
                    float r = 0.028 - b * 0.005;
                    float2 bp = float2(frac(t * (0.007 + b * 0.002) + b * 0.37) * (aspect + 0.5) - 0.25, 0.5 + 0.07 * b + 0.01 * sin(t * 0.7 + b * 2.0));
                    float2 q = (P - bp) / r;
                    if (abs(q.x) > 2.2 || abs(q.y) > 1.4) continue;
                    float flap = sin(t * 5.0 + b * 1.9);
                    float body = SrEllipse(SrRot(q, 0.12), float2(0.36, 0.14));
                    float neck = SrSeg(q, float2(0.25, 0.05), float2(0.5, 0.18)) - 0.06;
                    float head = length(q - float2(0.54, 0.2)) - 0.08;
                    float beak = SrSeg(q, float2(0.6, 0.2), float2(0.72, 0.17)) - 0.02;
                    float crest = SrSeg(q, float2(0.53, 0.27), float2(0.5, 0.4)) - 0.015;
                    float wing = SrSeg(q, float2(0.05, 0.06), float2(-0.12, 0.06 + 0.55 * flap)) - lerp(0.12, 0.04, saturate(abs(q.y - 0.06) / 0.55));
                    float train = SrEllipse(SrRot(q - float2(-0.95, -0.1), 0.1), float2(0.7, 0.1 + 0.06 * saturate(-q.x - 0.5)));
                    float a2 = aa / r;
                    float3 tc = lerp(float3(0.12, 0.45, 0.35), float3(0.22, 0.6, 0.42), SrNoise2(q * 6.0));
                    float eyes = length(float2(frac(q.x * 3.0) - 0.5, (q.y + 0.1 + (q.x + 0.95) * 0.1) * 3.0)) - 0.22;
                    tc = lerp(tc, Gold(P, 0.9), Fill(eyes, 0.05) * step(q.x, -0.4));
                    Over(c, tc, Fill(train, a2));
                    Over(c, float3(0.1, 0.32, 0.7), Fill(min(min(body, neck), min(head, crest)), a2));
                    Over(c, float3(0.85, 0.7, 0.4), Fill(beak, a2));
                    Over(c, float3(0.72, 0.42, 0.2), Fill(wing, a2));
                    Over(c, float3(0.05, 0.12, 0.25), Ln(min(min(body, wing), train), 0.03, a2) * 0.6);
                }

                // ---- birds ----
                [unroll] for (int i = 0; i < 5; i++)
                {
                    float2 bp = float2(frac(t * 0.008 + i * 0.21) * (aspect + 0.2) - 0.1, 0.5 + 0.03 * i + 0.008 * sin(t * 0.6 + i));
                    float2 d = P - bp;
                    float flap = 0.4 + 0.3 * sin(t * 6.0 + i * 1.7);
                    float wing = min(SrSeg(d, float2(-0.008, 0.008 * flap), float2(0.0, 0.0)), SrSeg(d, float2(0.0, 0.0), float2(0.008, 0.008 * flap))) - 0.0008;
                    Over(c, float3(0.25, 0.12, 0.2), Fill(wing, aa));
                }
                return c;
            }

            // ======================================================================== Arabia

            float3 ArabRamp(float y)
            {
                float3 c = float3(0.18, 0.14, 0.31);
                c = lerp(c, float3(0.48, 0.23, 0.42), smoothstep(0.04, 0.14, y));
                c = lerp(c, float3(0.85, 0.38, 0.23), smoothstep(0.14, 0.24, y));
                c = lerp(c, float3(0.94, 0.63, 0.31), smoothstep(0.24, 0.34, y));
                c = lerp(c, float3(0.95, 0.78, 0.56), smoothstep(0.34, 0.44, y));
                c = lerp(c, float3(0.85, 0.63, 0.66), smoothstep(0.44, 0.55, y));
                c = lerp(c, float3(0.54, 0.48, 0.72), smoothstep(0.55, 0.67, y));
                c = lerp(c, float3(0.2, 0.25, 0.5), smoothstep(0.67, 0.82, y));
                c = lerp(c, float3(0.08, 0.1, 0.27), smoothstep(0.82, 1.0, y));
                return c;
            }

            // One skyline layer. Returns coverage; col gets the layer colour (lit windows if lit > 0).
            float Skyline(float2 P, float x, float y0, float hMin, float hMax, float cellW, float seed, float lit, float aa, out float3 winCol)
            {
                winCol = float3(0, 0, 0);
                float ci = floor(x / cellW);
                float lf = frac(x / cellW);
                float h = lerp(hMin, hMax, PixHash2(float2(ci, seed)));
                float typ = PixHash2(float2(ci, seed + 7.0));
                float lx = (lf - 0.5) * cellW;
                float ly = P.y - y0;
                float cover = 0.0;
                float bw = cellW * lerp(0.35, 0.48, PixHash2(float2(ci, seed + 3.0)));
                // The building.
                cover = max(cover, step(abs(lx), bw) * step(ly, h));
                if (typ < 0.45)
                {
                    // A dome (a little onion on some), with a finial.
                    float dr = bw * 0.85;
                    float2 dq = float2(lx, ly - h);
                    float onion = typ < 0.2 ? 1.25 : 1.0;
                    float dome = length(dq * float2(1.0, 1.0 / onion)) - dr;
                    if (typ < 0.2) dome = max(dome, abs(dq.x) - dr * (1.0 - smoothstep(dr * 0.6, dr * 1.45, dq.y)));
                    cover = max(cover, Fill(dome, aa) * step(0.0, dq.y));
                    cover = max(cover, step(abs(dq.x), 0.0012) * step(dq.y, dr * onion + 0.012));
                }
                else if (typ < 0.7)
                {
                    // A pencil minaret beside a low block.
                    float mx = lx - bw * 0.7;
                    float mh = h + lerp(0.05, 0.12, PixHash2(float2(ci, seed + 9.0)));
                    float shaft = step(abs(mx), 0.0035) * step(ly, mh);
                    float balcony = step(abs(mx), 0.006) * (step(abs(ly - mh * 0.72), 0.002) + step(abs(ly - mh * 0.9), 0.0016));
                    float cone = step(0.0, ly - mh) * step(abs(mx), 0.0035 * (1.0 - (ly - mh) / 0.02));
                    cover = max(cover, saturate(shaft + balcony + cone));
                }
                // Lit windows (small arched rectangles).
                if (lit > 0.0)
                {
                    float2 wg = float2(lx * 260.0, ly * 160.0);
                    float2 wi = floor(wg);
                    float2 wf = frac(wg);
                    float on = step(0.62, PixHash(float3(wi, ci + seed))) * step(abs(lx), bw - 0.003) * step(ly, h - 0.006) * step(0.004, ly);
                    float win = step(abs(wf.x - 0.5), 0.22) * step(abs(wf.y - 0.45), 0.3);
                    winCol = lerp(float3(1.0, 0.62, 0.25), float3(1.0, 0.85, 0.5), PixHash(float3(wi, 2.0))) * on * win * lit;
                }
                return saturate(cover);
            }

            void GrandMosque(float2 P, float lx, float y0, float s, float3 col, float aa, inout float3 c)
            {
                float2 q = float2(lx, P.y - y0) / s;
                if (abs(q.x) > 1.2 || q.y > 1.6) return;
                float a = aa / s;
                float base = SrBox(q - float2(0.0, 0.18), float2(0.75, 0.18));
                float drum = SrBox(q - float2(0.0, 0.44), float2(0.3, 0.08));
                float dome = max(length(q - float2(0.0, 0.5)) - 0.34, -(q.y - 0.5));
                float semiL = max(length(q - float2(-0.38, 0.36)) - 0.2, -(q.y - 0.36));
                float semiR = max(length(q - float2(0.38, 0.36)) - 0.2, -(q.y - 0.36));
                float smalls = min(max(length(q - float2(-0.62, 0.36)) - 0.1, -(q.y - 0.36)), max(length(q - float2(0.62, 0.36)) - 0.1, -(q.y - 0.36)));
                float fin = SrSeg(q, float2(0.0, 0.84), float2(0.0, 0.96)) - 0.012;
                float m = min(min(min(base, drum), min(dome, fin)), min(min(semiL, semiR), smalls));
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float mx = i < 2 ? (i == 0 ? -0.95 : -0.82) : (i == 2 ? 0.82 : 0.95);
                    float mh = i == 0 || i == 3 ? 1.25 : 1.12;
                    float shaft = SrBox(q - float2(mx, mh * 0.5), float2(0.025, mh * 0.5));
                    float bal = min(SrBox(q - float2(mx, mh * 0.72), float2(0.045, 0.012)), SrBox(q - float2(mx, mh * 0.9), float2(0.04, 0.01)));
                    float cone = max(abs(q.x - mx) - 0.025 * (1.0 - (q.y - mh) / 0.18), -(q.y - mh));
                    cone = max(cone, q.y - mh - 0.18);
                    m = min(m, min(min(shaft, bal), cone));
                }
                Over(c, col, Fill(m, a));
                // Lit arched windows round the drum and the base.
                float2 wq = float2(frac(q.x * 10.0) - 0.5, q.y);
                float w = min(SrBox(wq - float2(0.0, 0.44), float2(0.2, 0.04)), SrBox(wq - float2(0.0, 0.16), float2(0.18, 0.06)));
                Over(c, float3(1.0, 0.72, 0.35), Fill(w / 10.0, a) * Fill(m, a) * step(abs(q.x), 0.7));
            }

            void Palace(float2 P, float lx, float y0, float s, float aa, inout float3 c)
            {
                float2 q = float2(lx, P.y - y0) / s;
                if (abs(q.x) > 1.0 || q.y > 1.7) return;
                float a = aa / s;
                float3 wall = float3(0.36, 0.3, 0.42);
                float body = SrBox(q - float2(0.0, 0.25), float2(0.85, 0.25));
                float crenel = SrBox(float2(frac(q.x * 12.0) - 0.5, q.y - 0.53), float2(0.25, 0.03));
                Over(c, wall, Fill(min(body, max(crenel / 12.0, abs(q.x) - 0.85)), a));
                // Towers with teal onion domes and gold tips.
                [unroll] for (int i = 0; i < 5; i++)
                {
                    float tx = -0.7 + i * 0.35;
                    float th = i == 2 ? 0.95 : (i % 2 == 0 ? 0.62 : 0.78);
                    float tw = i == 2 ? 0.2 : 0.11;
                    float tower = SrBox(q - float2(tx, th * 0.5), float2(tw, th * 0.5));
                    Over(c, wall * 1.08, Fill(tower, a));
                    float2 dq = q - float2(tx, th);
                    float dome = length(dq * float2(1.0, 0.8)) - tw * 1.25;
                    dome = max(dome, abs(dq.x) - tw * 1.25 * (1.0 - smoothstep(tw * 0.6, tw * 2.2, dq.y)));
                    dome = max(dome, -dq.y);
                    float3 dc = lerp(float3(0.12, 0.38, 0.45), float3(0.22, 0.55, 0.6), SrNoise2(dq * 40.0));
                    dc = lerp(dc, Gold(P, 0.85), Ln(frac(dq.x * 18.0 + dq.y * 9.0) - 0.5, 0.08, 0.1) * 0.5);
                    Over(c, dc, Fill(dome, a) * step(0.0, dq.y));
                    Over(c, Gold(P, 0.95), Fill(SrSeg(q, float2(tx, th + tw * 2.4), float2(tx, th + tw * 2.4 + 0.12)) - 0.012, a));
                    float2 wq = q - float2(tx, th * 0.6);
                    Over(c, float3(1.0, 0.75, 0.38), Fill(SrArch(wq * float2(1.0, 1.0), 0.03, 0.05, 0.4), a) * Fill(tower, a));
                }
                // The gate, glowing.
                Over(c, float3(1.0, 0.68, 0.3), Fill(SrArch(q - float2(0.0, 0.0), 0.12, 0.2, 0.4), a));
            }

            float3 Arabia(float2 P, float2 uv, float aspect, float aa, float t)
            {
                float flow = (SrFbm(float2(P.x * 1.2, P.y * 4.0 + t * 0.004)) - 0.5) * 0.05;
                float y = uv.y + flow;
                float3 c = ArabRamp(y);
                c *= 0.95 + 0.1 * SrNoise2(float2(P.x * 2.5, y * 14.0));

                // ---- stars and a faint gold girih net in the night part ----
                float dark = smoothstep(0.62, 0.85, uv.y);
                if (dark > 0.01)
                {
                    float2 g = P * 60.0;
                    float2 gi = floor(g);
                    float h = PixHash2(gi + 4.0);
                    if (h > 0.8)
                    {
                        float2 d = frac(g) - (float2(PixHash2(gi + 1.7), PixHash2(gi + 5.1)) * 0.6 + 0.2);
                        float tw = 0.65 + 0.35 * sin(t * (1.2 + h * 2.0) + h * 40.0);
                        float star = h > 0.94 ? min(abs(d.x) + abs(d.y) * 5.0, abs(d.y) + abs(d.x) * 5.0) - 0.35 : length(d) - 0.1;
                        Over(c, float3(1.0, 0.92, 0.7), Fill(star / 60.0, aa) * tw * dark);
                    }
                    float2 gq = frac(P * 9.0) - 0.5;
                    float net = min(abs(SrStar8(gq, 0.32)), min(abs(gq.x + gq.y), abs(gq.x - gq.y)) * 0.7071);
                    Over(c, kGoldS, Ln(net / 9.0, 0.0006, aa) * dark * 0.18);
                }

                // ---- the big pale moon with craters, the gold crescent ----
                float2 mc = float2(aspect * 0.3, 0.6);
                float mr = length(P - mc);
                Over(c, float3(0.98, 0.9, 0.78), (1.0 - smoothstep(0.07, 0.13, mr)) * 0.22);
                if (mr < 0.072)
                {
                    float3 moon = lerp(float3(0.93, 0.9, 0.86), float3(0.78, 0.8, 0.9), saturate((P.x - mc.x) * 8.0 + 0.4));
                    float2 cq = (P - mc) * 40.0;
                    float2 ci = floor(cq);
                    float ch = PixHash2(ci + 2.0);
                    float crater = length(frac(cq) - 0.5) - lerp(0.12, 0.38, ch);
                    moon = lerp(moon, moon * 0.82, Ln(crater / 40.0, 0.0008, aa) * step(0.6, ch));
                    moon = lerp(moon, moon * 0.9, Fill(crater / 40.0, aa) * step(0.75, ch));
                    Over(c, moon, Fill(mr - 0.07, aa));
                    Over(c, float3(0.45, 0.4, 0.55), Ln(mr - 0.07, 0.0012, aa) * 0.6);
                }
                float2 cc = float2(aspect * 0.78, 0.87);
                float cres = max(length(P - cc) - 0.028, -(length(P - cc - float2(0.011, 0.007)) - 0.025));
                Over(c, Gold(P, 0.95), Fill(cres, aa));

                // ---- flying carpets ----
                [unroll] for (int k = 0; k < 2; k++)
                {
                    float cx = frac(t * 0.006 + k * 0.5) * (aspect + 0.4) - 0.2;
                    float cy = 0.5 + 0.08 * k + 0.01 * sin(t * 0.5 + k * 3.0);
                    float2 q = P - float2(cx, cy);
                    q.y -= 0.006 * sin(q.x * 90.0 - t * 3.0);
                    q.y -= q.x * 0.15;
                    float rug = SrBox(q, float2(0.035, 0.008));
                    if (rug < 0.01)
                    {
                        float3 rc = k == 0 ? float3(0.7, 0.16, 0.22) : float3(0.16, 0.25, 0.55);
                        rc = lerp(rc, Gold(P, 0.9), Ln(SrBox(q, float2(0.03, 0.004)), 0.0012, aa));
                        rc = lerp(rc, float3(0.95, 0.88, 0.7), Fill(SrStar8(q, 0.004), aa));
                        Over(c, rc, Fill(rug, aa));
                        float fringe = step(0.035, abs(q.x)) * step(abs(q.x), 0.04) * step(abs(q.y), 0.008) * step(0.5, frac(q.y * 400.0));
                        Over(c, Gold(P, 0.8), fringe);
                        // A rider: a small dark figure with a turban.
                        float rider = min(SrEllipse(q - float2(0.0, 0.016), float2(0.006, 0.01)), length(q - float2(0.0, 0.03)) - 0.005);
                        Over(c, float3(0.15, 0.1, 0.2), Fill(rider, aa));
                    }
                }

                // ---- skyline: far lavender domes and minarets ----
                float3 wc;
                float x1 = P.x + _SrBackdrop.z * 0.5;
                float far = Skyline(P, x1, 0.27, 0.03, 0.07, 0.06, 3.0, 0.0, aa, wc);
                Over(c, lerp(float3(0.62, 0.52, 0.72), ArabRamp(0.3), 0.35), far);

                // ---- the violet city with lit windows, a grand mosque and a palace ----
                float x2 = P.x + _SrBackdrop.z * 1.0;
                float mid = Skyline(P, x2, 0.2, 0.03, 0.09, 0.045, 11.0, 1.0, aa, wc);
                float3 midCol = float3(0.33, 0.27, 0.5);
                Over(c, midCol, mid);
                c += wc * mid;
                GrandMosque(P, Wrap(x2 - aspect * 0.55, 1.2), 0.21, 0.11, midCol * 1.12, aa, c);
                Palace(P, Wrap(x2 - aspect * 0.05 - 0.6, 1.2), 0.2, 0.07, aa, c);

                // ---- dark near silhouettes: minarets, domes, palms ----
                float x3 = P.x + _SrBackdrop.z * 1.6;
                float near = Skyline(P, x3, 0.12, 0.02, 0.08, 0.07, 23.0, 0.6, aa, wc);
                float3 nearCol = float3(0.16, 0.11, 0.24);
                Over(c, nearCol, near);
                c += wc * near * 0.8;
                float pc = floor(x3 / 0.09);
                if (PixHash2(float2(pc, 31.0)) > 0.55)
                {
                    float px = (frac(x3 / 0.09) - 0.5) * 0.09;
                    float2 base = float2(0.0, 0.12);
                    float ph = 0.1 + 0.04 * PixHash2(float2(pc, 32.0));
                    float trunk = abs(px - 0.01 * sin((P.y - base.y) / ph * 2.0)) - 0.0018;
                    float2 top = float2(0.01 * sin(2.0), base.y + ph);
                    float2 fq = float2(px, P.y) - top;
                    float fronds = 1e3;
                    [unroll] for (int f = 0; f < 6; f++)
                    {
                        float a = -2.6 + f * 1.05;
                        float2 dir = float2(cos(a), sin(a) * 0.6 + 0.15);
                        float along = clamp(dot(fq, dir), 0.0, 0.035);
                        float2 pt = dir * along - float2(0.0, along * along * 6.0);
                        fronds = min(fronds, length(fq - pt) - 0.0035 * (1.0 - along / 0.035));
                    }
                    Over(c, nearCol, Fill(min(max(trunk, P.y - top.y), fronds), aa) * step(base.y - 0.01, P.y));
                }

                // ---- birds ----
                [unroll] for (int i = 0; i < 6; i++)
                {
                    float2 bp = float2(frac(t * 0.01 + i * 0.13) * (aspect + 0.2) - 0.1, 0.66 + 0.025 * i + 0.01 * sin(t * 0.5 + i));
                    float2 d = P - bp;
                    float flap = 0.4 + 0.3 * sin(t * 7.0 + i * 2.1);
                    float wing = min(SrSeg(d, float2(-0.007, 0.007 * flap), float2(0.0, 0.0)), SrSeg(d, float2(0.0, 0.0), float2(0.007, 0.007 * flap))) - 0.0007;
                    Over(c, float3(0.12, 0.08, 0.18), Fill(wing, aa));
                }
                return c;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float t = _SrBackdrop.y;
                float2 uv = input.uv;
                uv.x += _SrBackdrop.x;
                                float2 P = float2(uv.x * aspect, uv.y);
                float aa = max(fwidth(P.y), 1e-4) * 1.2;

                // Which painting: the torn edge sweeps from the left (low) to the right (high) as _SrTear.x goes 0 -> 1.
                float e = input.uv.x * 0.75 + (1.0 - input.uv.y) * 0.25 + (SrFbm(input.uv * float2(6.0, 14.0)) - 0.5) * 0.08;
                float edge = _SrTear.x * 1.2 - 0.1;
                bool after = e < edge;
                float hv = after ? _SrTear.z : _SrTear.y;
                float3 c;
                [branch] if (hv < 0.5) c = India(P, uv, aspect, aa, t);
                else c = Arabia(P, uv, aspect, aa, t);

                // The torn paper rim and its shadow on the painting underneath, only while the sweep runs.
                if (_SrTear.x > 0.001 && _SrTear.x < 0.999 && abs(_SrTear.y - _SrTear.z) > 0.5)
                {
                    float de = (e - edge) / max(fwidth(e), 1e-4);
                    float rim = 1.0 - smoothstep(0.0, 3.0 + 4.0 * SrNoise2(input.uv * 300.0), abs(de));
                    float shadow = (1.0 - smoothstep(0.0, 18.0, de)) * step(0.0, de);
                    c *= 1.0 - shadow * 0.35;
                    c = lerp(c, float3(0.98, 0.95, 0.88), rim * step(-1.0, -de) * 0.95 + rim * 0.5);
                }

                // Paper grain and gold-leaf flecks over the whole painting.
                float2 cp = input.uv * _ScreenParams.xy / max(_ScreenParams.y, 1.0) * 900.0;
                c *= 1.0 + (SrNoise2(cp * float2(0.6, 0.15)) - 0.5) * 0.08 + (PixHash2(floor(cp * 0.7)) - 0.5) * 0.04;
                float fleck = step(0.9985, PixHash2(floor(cp * 0.3)));
                c = lerp(c, Gold(P, 1.0), fleck * 0.8);

                float3 lin = SrLin(saturate(c));
                half sp = SrAt(input.uv);
                return half4(lin * sp, sp);
            }
            ENDHLSL
        }
    }
}
