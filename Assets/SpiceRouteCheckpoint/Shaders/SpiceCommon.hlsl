#ifndef SPICE_CP_COMMON_INCLUDED
#define SPICE_CP_COMMON_INCLUDED

// Spice Route checkpoint (MainScene prop camera 3.10 checkpoint 10 + MainScene prop camera SpiceRoute): shared helpers for
// the painted-paper look of the last level. Its first half is India (carved temples, Ganesha colossi, rangoli, mirror
// work, Madhubani elephants, a Vishvarupa sky), its second half the Arabian / Ottoman world (girih stars, Iznik tiles,
// mashrabiya, carpets, ebru marbling, a domed skyline at dusk). Everything is gouache on hand-made paper with gold leaf
// (references: a Rajasthani mixed-media collage, a Mughal courtyard watercolour).
//
// Where the look is on screen: SpiceRouteCheckpointController writes _SrLook every frame - x = 1 when the look in force
// (the director's _LTA slots) is the spice look, y = 1 when the look being wiped in (_LTB) is, z = 1 for a scene that is
// spice for the whole run. The look wipe (LookWipe.hlsl) says per pixel which of the two it shows.
// Which half: legs know it from their own serial (_SrHalf: first leg of the level, legs per half); the sky, the light, the
// fog, the coins and the runner follow the runner's half through a torn-paper sweep (_SrTear).
// Without the controller everything here is zero = no change.

#include "Assets/PixelCheckpoint/Shaders/PixelCommon.hlsl"   // LTWipeAt + the hashes / noise

float4 _SrLook;        // x look A is spice, y look B is, z always
float4 _SrHalf;        // x first leg serial of the level, y legs per half, z unused, w unused
float4 _SrTear;        // x sweep progress 0..1, y half before (0 India, 1 Arabia), z half after, w eased blend (y -> z) for globals
float4 _SrLightDir;    // xyz camera-relative light (props, rocks), w rock brush scale
float4 _SrParams;      // x gold glint, y window glow, z paper grain, w leg pattern contrast

// Spice amount on this pixel's side of the wipe (0 other look .. 1 spice).
half SrAt(float2 screenUV)
{
    half w = LTWipeAt(screenUV);
    return (half)max(_SrLook.z, lerp(_SrLook.x, _SrLook.y, w));
}

// 0 = India, 1 = Arabia for a leg serial. Halves alternate (level 10 is India then Arabia; endless legs keep alternating).
float SrHalfOf(float serial)
{
    float h = floor((serial - _SrHalf.x) / max(_SrHalf.y, 1.0));
    return fmod(max(h, 0.0), 2.0);
}

// The half the environment shows now (eased), for things that cannot follow the sweep per pixel.
half SrEnvHalf() { return (half)lerp(_SrTear.y, _SrTear.z, _SrTear.w); }

half3 SrLin(half3 c) { return c * (c * (c * 0.305306011h + 0.682171111h) + 0.012522878h); }   // sRGB -> linear (approx.)

float SrNoise2(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = PixHash2(i), b = PixHash2(i + float2(1, 0)), c = PixHash2(i + float2(0, 1)), d = PixHash2(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float SrFbm(float2 p)
{
    return SrNoise2(p) * 0.5 + SrNoise2(p * 2.07 + 5.3) * 0.27 + SrNoise2(p * 4.13 + 1.7) * 0.15 + SrNoise2(p * 8.3 + 9.1) * 0.08;
}

// ---- coverage helpers (d = signed distance, aa = one pixel's footprint) ----
half SrFill(float d, float aa) { return (half)(1.0 - smoothstep(-aa, aa, d)); }
half SrLine(float d, float w, float aa) { return (half)(1.0 - smoothstep(w - aa, w + aa, abs(d))); }

float SrBox(float2 p, float2 h) { float2 q = abs(p) - h; return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0); }
float SrEllipse(float2 p, float2 r) { return (length(p / r) - 1.0) * min(r.x, r.y); }
float SrSeg(float2 p, float2 a, float2 b) { float2 pa = p - a, ba = b - a; float h = saturate(dot(pa, ba) / dot(ba, ba)); return length(pa - ba * h); }
float2 SrRot(float2 p, float a) { float c = cos(a), s = sin(a); return float2(c * p.x - s * p.y, s * p.x + c * p.y); }

// Polar rose: petals of a lotus / rangoli flower. n petals, outer radius r, petal depth k (0..1).
float SrPetals(float2 p, float n, float r, float k, float phase)
{
    float a = atan2(p.y, p.x) + phase;
    float rr = r * (1.0 - k + k * abs(cos(a * n * 0.5)));
    return length(p) - rr;
}

// Eight-point star (khatam): two squares, one turned 45 degrees.
float SrStar8(float2 p, float r)
{
    float a = SrBox(p, float2(r, r) * 0.7071);
    float b = SrBox(SrRot(p, 0.785398), float2(r, r) * 0.7071);
    return min(a, b);
}

// Pointed (ogee-ish) arch opening: a box under two intersecting circles. Base at y = 0, width 2w, spring height hs.
float SrArch(float2 p, float w, float hs, float pointy)
{
    float body = SrBox(p - float2(0.0, hs * 0.5), float2(w, hs * 0.5));
    float rr = w * (1.0 + pointy);
    float c1 = length(p - float2(w - rr, hs)) - rr;
    float c2 = length(p - float2(rr - w, hs)) - rr;
    float top = max(max(c1, c2), hs - p.y);
    return min(body, top);
}

// Cusped (Mughal) arch: a round arch with three lobes cut into its head.
float SrCuspArch(float2 p, float w, float hs)
{
    float d = SrArch(p, w, hs, 0.25);
    float lobes = min(length(p - float2(0.0, hs + w * 0.62)) - w * 0.28, min(length(p - float2(-w * 0.62, hs + w * 0.05)) - w * 0.26, length(p - float2(w * 0.62, hs + w * 0.05)) - w * 0.26));
    return min(d, lobes);
}

// Paisley (boteh): a teardrop bent into a curl.
float SrPaisley(float2 p, float r)
{
    float a = p.y / r * 0.9;
    p = SrRot(p, -a * 0.6);
    float body = length(p) - r;
    float tail = SrSeg(p, float2(0.0, 0.0), float2(r * 0.9, r * 1.55)) - r * lerp(0.95, 0.1, saturate((p.y) / (r * 1.55)));
    return min(body, tail);
}

// A small Ganesha emblem (face on): head, fan ears, curled trunk, crown. Unit = r.
float SrGanesha(float2 p, float r)
{
    p /= r;
    float head = length(p - float2(0.0, 0.05)) - 0.42;
    float earL = SrEllipse(SrRot(p - float2(-0.52, 0.08), 0.25), float2(0.36, 0.46));
    float earR = SrEllipse(SrRot(p - float2(0.52, 0.08), -0.25), float2(0.36, 0.46));
    float trunk = min(SrSeg(p, float2(0.0, -0.05), float2(0.05, -0.62)), SrSeg(p, float2(0.05, -0.62), float2(0.3, -0.78))) - 0.12;
    float crown = max(SrBox(p - float2(0.0, 0.62), float2(0.26, 0.2)), abs(p.x) * 1.4 + p.y - 1.15);
    float tip = length(p - float2(0.0, 0.92)) - 0.08;
    return min(min(min(head, min(earL, earR)), min(trunk, crown)), tip) * r;
}

// An elephant walking (side view, facing +x), feet on y = 0, height ~ r.
float SrElephant(float2 p, float r)
{
    p /= r;
    float body = SrEllipse(p - float2(0.0, 0.62), float2(0.62, 0.36));
    float head = length(p - float2(0.62, 0.78)) - 0.24;
    float ear = SrEllipse(p - float2(0.5, 0.74), float2(0.15, 0.22));
    float trunk = min(SrSeg(p, float2(0.8, 0.7), float2(0.92, 0.3)), SrSeg(p, float2(0.92, 0.3), float2(1.02, 0.18))) - 0.07;
    float legs = 1e5;
    [unroll] for (int i = 0; i < 4; i++)
    {
        float lx = -0.42 + i * 0.28 + (i > 1 ? 0.06 : 0.0);
        legs = min(legs, SrBox(p - float2(lx, 0.2), float2(0.09, 0.22)));
    }
    float tail = SrSeg(p, float2(-0.6, 0.7), float2(-0.72, 0.42)) - 0.025;
    float howdah = SrBox(p - float2(-0.05, 1.05), float2(0.26, 0.12));
    return min(min(min(body, head), min(ear, trunk)), min(min(legs, tail), howdah)) * r;
}

// A peacock (side view, facing +x) with its train hanging behind, feet on y = 0.
float SrPeacock(float2 p, float r)
{
    p /= r;
    float body = SrEllipse(SrRot(p - float2(0.1, 0.42), 0.4), float2(0.26, 0.16));
    float neck = SrSeg(p, float2(0.22, 0.5), float2(0.32, 0.86)) - 0.06;
    float head = length(p - float2(0.34, 0.9)) - 0.08;
    float beak = SrSeg(p, float2(0.38, 0.9), float2(0.48, 0.88)) - 0.02;
    float crest = min(SrSeg(p, float2(0.33, 0.96), float2(0.3, 1.1)), SrSeg(p, float2(0.35, 0.96), float2(0.38, 1.1))) - 0.012;
    float train = SrEllipse(SrRot(p - float2(-0.52, 0.28), -0.22), float2(0.62, 0.15));
    float legs = min(SrSeg(p, float2(0.08, 0.3), float2(0.06, 0.0)), SrSeg(p, float2(0.16, 0.3), float2(0.18, 0.0))) - 0.018;
    return min(min(min(body, neck), min(head, beak)), min(min(crest, train), legs)) * r;
}

// Paper grain: fibres + tooth, ~0 mean, for multiplying a colour by (1 + grain).
half SrGrain(float2 p)
{
    float f = SrNoise2(float2(p.x * 3.1, p.y * 0.9)) * 0.6 + PixHash2(floor(p * 7.0)) * 0.4;
    return (half)(f - 0.5);
}

// Gold leaf: a warm metallic gold that catches flecks of light.
half3 SrGold(float2 p, half lit)
{
    half3 deep = half3(0.42h, 0.22h, 0.04h), mid = half3(0.80h, 0.52h, 0.12h), hi = half3(1.0h, 0.85h, 0.45h);
    half n = (half)SrNoise2(p * 9.0);
    half3 c = lerp(deep, mid, saturate(lit * 0.9h + n * 0.3h));
    half fleck = (half)step(0.86, PixHash2(floor(p * 23.0))) * (half)_SrParams.x;
    return lerp(c, hi, saturate(fleck * 0.8h + (lit - 0.7h) * 1.5h));
}

#endif
