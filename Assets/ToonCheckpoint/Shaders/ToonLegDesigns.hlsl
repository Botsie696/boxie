#ifndef TOON_LEG_DESIGNS_INCLUDED
#define TOON_LEG_DESIGNS_INCLUDED

// Toon checkpoint: the line art painted along every leg. ToonLegDesigns.cs gives each path cube (per renderer)
//   _ToonLeg  x: the cube's centre along its leg (0 = leg start), y: leg length in cubes, z: leg serial, w: 1 = set
// so a face pixel knows u = its distance from the leg's start (continuous over every cube of the leg) and v = its place
// across the face. A design is picked per leg AND per face from the library below (18 designs), and every repeat is
// fitted to the leg's own length, so each motif starts at one end of the leg and finishes exactly at the other. Each leg
// is also framed by a band near both ends, and its end caps carry an emblem. Nothing here is per cube.
//
// Marks come back as coverage of three paints: ink (dark pen), light (the warm sunlit cream) and moss (green).

#define TOON_TAU 6.2831853

// A line of half width w at distance d, anti-aliased with the pixel footprint aa (never thinner than ~0.75 px).
half TLine(float d, float w, float aa)
{
    w = max(w, aa * 0.75);
    return (half)(1.0 - smoothstep(w - aa, w + aa, d));
}

// Area (d <= 0 inside), anti-aliased.
half TFill(float d, float aa) { return (half)(1.0 - smoothstep(-aa, aa, d)); }

// The repeat closest to p that divides the leg length exactly.
float TFit(float L, float p) { return L / max(1.0, round(L / p)); }

float THash1(float n) { return frac(sin(n * 12.9898 + 78.233) * 43758.5453); }

// u along the leg (0..L), v across the face (-0.5..0.5), L leg length, seed per leg+face, design index.
// thick / thin: line half widths. aa: pixel footprint in face units.
void ToonLegMarks(float u, float v, float L, float seed, int design, float thick, float thin, float aa,
                  inout half ink, inout half light, inout half moss)
{
    float av = abs(v);
    float r1 = THash1(seed), r2 = THash1(seed + 3.7), r3 = THash1(seed + 9.1);

    if (design == 0)            // pinstripes: a thick rule with a thin one either side, near both edges
    {
        ink = max(ink, TLine(abs(av - 0.31), thick, aa));
        ink = max(ink, TLine(abs(av - 0.40), thin, aa));
        light = max(light, TLine(abs(av - 0.22), thin, aa));
    }
    else if (design == 1)       // vine: a sine stem with diamond leaves on every crest
    {
        float P = TFit(L, 2.4 + r1 * 1.4);
        float y = 0.2 * sin(TOON_TAU * u / P + r2 * 6.28);
        moss = max(moss, TLine(abs(v - y), thin * 1.3, aa * 2.0));
        float c = (floor(u / (P * 0.5)) + 0.5) * P * 0.5;          // crest positions
        float yc = 0.2 * sin(TOON_TAU * c / P + r2 * 6.28);
        float leaf = abs(u - c) / 0.2 + abs(v - yc - sign(yc) * 0.09) / 0.07;
        moss = max(moss, TFill(leaf - 1.0, aa * 6.0));
        ink = max(ink, TLine(abs(av - 0.42), thin, aa));
    }
    else if (design == 2)       // twisted rope: two strands crossing, ink with a lit edge
    {
        float P = TFit(L, 1.6 + r1);
        float y = 0.22 * sin(TOON_TAU * u / P);
        ink = max(ink, TLine(abs(v - y), thick * 0.8, aa * 2.0));
        ink = max(ink, TLine(abs(v + y), thick * 0.8, aa * 2.0));
        light = max(light, TLine(abs(v - y - 0.05), thin * 0.7, aa * 2.0));
    }
    else if (design == 3)       // battlement / key: a square wave with crisp risers
    {
        float P = TFit(L, 0.9 + r1 * 0.6);
        float f = frac(u / P);
        float s = f < 0.5 ? 0.16 : -0.16;
        ink = max(ink, TLine(abs(v - s), thick * 0.7, aa));
        float dr = min(abs(f), min(abs(f - 0.5), abs(f - 1.0))) * P;
        ink = max(ink, TLine(dr, thick * 0.7, aa) * step(av, 0.16 + thick * 0.7));
        ink = max(ink, TLine(abs(av - 0.38), thin, aa));
    }
    else if (design == 4)       // chevrons pointing along the run
    {
        float P = TFit(L, 0.7 + r1 * 0.5);
        float c = frac((u - av * 0.9) / P);
        ink = max(ink, TLine(abs(c - 0.5) * P, thin * 1.4, aa * 1.4) * step(av, 0.36));
        light = max(light, TLine(abs(av - 0.42), thin, aa));
    }
    else if (design == 5)       // zig-zag with a parallel lit hairline
    {
        float P = TFit(L, 1.1 + r1 * 0.8);
        float tri = abs(2.0 * frac(u / P) - 1.0) * 2.0 - 1.0;
        float y = 0.24 * tri;
        ink = max(ink, TLine(abs(v - y), thick * 0.75, aa * 2.0));
        light = max(light, TLine(abs(v - y - 0.09), thin * 0.8, aa * 2.0));
    }
    else if (design == 6)       // scallops: arches along both edges
    {
        float P = TFit(L, 0.8 + r1 * 0.5);
        float du = (frac(u / P) - 0.5) * P;
        float d = abs(length(float2(du, av - 0.5)) - P * 0.38);
        ink = max(ink, TLine(d, thin * 1.2, aa));
        light = max(light, TLine(abs(av - 0.08), thin, aa) * step(0.5, r2));
    }
    else if (design == 7)       // diamond chain with a lit core
    {
        float P = TFit(L, 1.0 + r1 * 0.8);
        float du = (frac(u / P) - 0.5) * P;
        float dd = abs(du) / (P * 0.5) + av / 0.24;
        ink = max(ink, TLine(abs(dd - 1.0) * 0.18, thin * 1.2, aa));
        light = max(light, TFill(dd - 0.35, aa * 4.0));
    }
    else if (design == 8)       // dash-and-block code between two hairlines
    {
        float P = TFit(L, 1.2 + r1 * 0.6);
        float f = frac(u / P) * P;
        half dash = step(0.08, f) * step(f, P * 0.55);
        half blk = step(P * 0.68, f) * step(f, P * 0.68 + 0.12);
        ink = max(ink, (dash * TLine(av, thick * 0.6, aa)) + blk * TFill(av - 0.07, aa));
        ink = max(ink, TLine(abs(av - 0.33), thin, aa));
    }
    else if (design == 9)       // one calligraphic brush stroke from start to end: hairline at the ends, fat in the middle
    {
        float t = saturate(u / max(L, 0.001));
        float y = 0.1 * sin(TOON_TAU * (t * (0.6 + r1 * 0.8)) + r2 * 6.28);
        float w = thin * 0.6 + (thick * 1.8) * sin(3.14159 * t);
        ink = max(ink, TLine(abs(v - y), w, aa * 2.0));
        light = max(light, TLine(abs(v - y + w + 0.07), thin * 0.7, aa * 2.0));
    }
    else if (design == 10)      // ladder: two rails, rungs fitted to the leg
    {
        float P = TFit(L, 0.55 + r1 * 0.35);
        ink = max(ink, TLine(abs(av - 0.27), thin * 1.2, aa));
        float dr = abs(frac(u / P) - 0.5) * P;
        ink = max(ink, TLine(dr, thin * 1.6, aa) * step(av, 0.27));
        light = max(light, TLine(abs(av - 0.4), thin, aa));
    }
    else if (design == 11)      // fish scales over the middle band
    {
        float P = TFit(L, 0.5 + r1 * 0.2);
        float row = floor((v + 0.5) / 0.18);
        float du = (frac(u / P + row * 0.5) - 0.5) * P;
        float dv = frac((v + 0.5) / 0.18) * 0.18;
        float d = abs(length(float2(du, dv)) - P * 0.5);
        ink = max(ink, TLine(d, thin, aa) * step(av, 0.32));
    }
    else if (design == 12)      // wave band: a lit ribbon between two sines, inked edges
    {
        float P = TFit(L, 2.0 + r1 * 1.5);
        float a = TOON_TAU * u / P;
        float top = 0.12 + 0.07 * sin(a), bot = -0.12 + 0.07 * sin(a + 2.2);
        light = max(light, TFill(max(v - top, bot - v), aa * 2.0) * 0.8h);
        ink = max(ink, TLine(abs(v - top), thin, aa * 2.0));
        ink = max(ink, TLine(abs(v - bot), thin, aa * 2.0));
    }
    else if (design == 13)      // twin vines winding round each other, leaves on both
    {
        float P = TFit(L, 2.0 + r1);
        float y = 0.17 * sin(TOON_TAU * u / P);
        moss = max(moss, TLine(abs(v - y), thin * 1.2, aa * 2.0));
        moss = max(moss, TLine(abs(v + y), thin * 1.2, aa * 2.0));
        float c = (floor(u / (P * 0.25)) + 0.5) * P * 0.25;
        float leaf = abs(u - c) / 0.13 + abs(av - 0.3) / 0.05;
        moss = max(moss, TFill(leaf - 1.0, aa * 6.0));
    }
    else if (design == 14)      // crosshatched band, lit, with inked borders
    {
        float d1 = abs(frac((u + v) / 0.22) - 0.5) * 0.22;
        float d2 = abs(frac((u - v) / 0.22) - 0.5) * 0.22;
        half band = step(av, 0.2);
        light = max(light, max(TLine(d1, thin * 0.7, aa), TLine(d2, thin * 0.7, aa)) * band);
        ink = max(ink, TLine(abs(av - 0.2), thin * 1.3, aa));
    }
    else if (design == 15)      // rune ticks off a heavy base rule
    {
        ink = max(ink, TLine(abs(v + 0.28), thick * 0.8, aa));
        float cell = floor(u / 0.42);
        float h = THash1(cell + seed * 7.0);
        float f = (frac(u / 0.42) - 0.5) * 0.42;
        float len = 0.12 + h * 0.3;
        ink = max(ink, TLine(abs(f), thin * 1.1, aa) * step(v, -0.28 + len) * step(-0.28, v) * step(0.25, h));
        light = max(light, TLine(abs(v - 0.36), thin, aa));
    }
    else if (design == 16)      // rosettes: ringed medallions fitted along the centre, linked by a hairline
    {
        float P = TFit(L, 1.3 + r1 * 0.7);
        float du = (frac(u / P) - 0.5) * P;
        float rr = length(float2(du, v));
        ink = max(ink, TLine(abs(rr - 0.2), thin * 1.2, aa));
        ink = max(ink, TLine(abs(rr - 0.12), thin * 0.8, aa));
        light = max(light, TFill(rr - 0.05, aa));
        ink = max(ink, TLine(av, thin * 0.6, aa) * step(0.2, rr));
    }
    else                        // 17 terraces: a stepped line climbing to the middle of each repeat and back down
    {
        float P = TFit(L, 1.6 + r1);
        float f = frac(u / P);
        float t = f < 0.5 ? f * 2.0 : (1.0 - f) * 2.0;                 // 0 -> 1 -> 0 over the repeat
        float lvl = min(floor(t * 4.0), 3.0) / 3.0;
        float y = (lvl - 0.5) * 0.4;
        ink = max(ink, TLine(abs(v - y), thick * 0.6, aa));
        float fr = frac(t * 4.0);
        float dRise = min(fr, 1.0 - fr) * P / 8.0;                    // distance in u to the nearest riser
        ink = max(ink, TLine(dRise, thick * 0.6, aa) * step(av, 0.22));
        light = max(light, TLine(abs(av - 0.42), thin, aa));
    }
}

// Frame near both ends of the leg: a heavy rule with a lit hairline inside it.
void ToonLegEnds(float u, float L, float thick, float thin, float aa, inout half ink, inout half light)
{
    float e = min(u, L - u);
    ink = max(ink, TLine(abs(e - 0.16), thick, aa));
    light = max(light, TLine(abs(e - 0.3), thin, aa));
}

// End cap emblem (the square face at the end of a leg): an inked diamond in a lit square.
void ToonLegCap(float2 p, float thick, float thin, float aa, inout half ink, inout half light)
{
    float dia = abs(p.x) + abs(p.y);
    ink = max(ink, TLine(abs(dia - 0.3), thick, aa));
    float sq = max(abs(p.x), abs(p.y));
    light = max(light, TLine(abs(sq - 0.38), thin, aa));
    light = max(light, TFill(dia - 0.1, aa));
}

#endif
