#ifndef PIXEL_CP_COMMON_INCLUDED
#define PIXEL_CP_COMMON_INCLUDED

// Pixel checkpoint ("Jellyfish Grotto", MainScene prop camera 3.9 checkpoint 4 + MainScene prop camera Pixel): shared helpers.
//
// Where the pixel look is on screen: PixelCheckpointController writes _PixLook every frame - x = 1 when the look in force
// (the director's _LTA slots) is the pixel look, y = 1 when the look being wiped in (_LTB) is, z = 1 for a scene that is
// pixel for the whole run. The look wipe (LookWipe.hlsl) says per pixel which of the two the pixel shows, so every pixel
// shader (path, rocks, trail, backdrop, motes, the pixel-art pass) switches along the same edge as the colours.
// Without the controller everything here is zero = no change.

#include "Assets/PropCameraLooks/LookTransition/Shaders/LookWipe.hlsl"

float4 _PixLook;        // x look A is pixel, y look B is pixel, z always pixel
float4 _PixMoss;        // rgb moss (linear), a moss amount
float4 _PixMossLight;   // rgb moss highlight (linear), a drip tip glow
float4 _PixMossDark;    // rgb moss in shade (linear), a longest drip (texels)
float4 _PixMossGlow;    // rgb brightest moss / drip tip (linear), a chance of a glowing moss texel
float4 _PixMortar;      // rgb mortar (linear), a brick variation
float4 _PixCaveMoss, _PixCaveMossLight, _PixCaveMossDark, _PixCaveMossGlow;   // the rocks' / trail's blue moss (linear)
float4 _PixRockLit;     // rgb rock facing the light (linear)
float4 _PixRockMid;     // rgb rock turning away (linear)
float4 _PixRockShade;   // rgb rock in shade (linear)
float4 _PixRockDeep;    // rgb rock in deep shade (linear), a dither strength
float4 _PixShroom;      // rgb mushroom cap (linear), a cap chance per cell
float4 _PixShroomLight; // rgb mushroom cap top / spots (linear), a cap glow
float4 _PixLightDir;    // xyz camera-relative light for the rocks, w rock texels per unit
float4 _PixParams;      // x leg texels per unit, y rock moss threshold, z drip chance on rocks, w unused

// Pixel amount of the look on this pixel's side of the wipe (0 other look .. 1 pixel look).
half PixAt(float2 screenUV)
{
    half w = LTWipeAt(screenUV);
    return (half)max(_PixLook.z, lerp(_PixLook.x, _PixLook.y, w));
}

float PixHash(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float PixHash2(float2 p) { return PixHash(float3(p, 17.31)); }

float PixNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = PixHash(i), n100 = PixHash(i + float3(1, 0, 0));
    float n010 = PixHash(i + float3(0, 1, 0)), n110 = PixHash(i + float3(1, 1, 0));
    float n001 = PixHash(i + float3(0, 0, 1)), n101 = PixHash(i + float3(1, 0, 1));
    float n011 = PixHash(i + float3(0, 1, 1)), n111 = PixHash(i + float3(1, 1, 1));
    return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
}

// 4x4 ordered dither threshold (0..1) of an integer cell.
float PixBayer(float2 cell)
{
    // Bit-interleaved Bayer index (no array: cheap and valid on every target).
    int2 p = int2(fmod(abs(cell), 4.0));
    int x = p.x, y = p.y;
    int xy = x ^ y;
    int v = ((xy & 1) << 3) | ((x & 1) << 2) | ((xy & 2)) | ((x & 2) >> 1);
    return (v + 0.5) / 16.0;
}

half PixLum(half3 c) { return dot(c, half3(0.2126h, 0.7152h, 0.0722h)); }

// ---------------------------------------------------------------------------------------------------------------- rocks
// The cave stone of the reference: deep indigo masses in hard dithered bands from a camera-relative light, glowing
// blue moss on what faces up with drips running down the faces, coral mushroom caps on the ledges. Colour depends only on the world position and
// normal, snapped to a world texel grid, so stacked rock pieces read as one blocky mass.
// glow (rgb) / glowAmt: what should stay bright through the fog (mushrooms, drip tips).
half3 PixRockColor(float3 wp, float3 n, out half3 glow, out half glowAmt)
{
    glow = 0; glowAmt = 0;
    float T = max(_PixLightDir.w, 0.5);
    float3 cell = floor(wp * T);
    float3 wq = (cell + 0.5) / T;
    float3 L = normalize(_PixLightDir.xyz);

    float big = PixNoise(wq * 0.07);
    float ndl = dot(n, L) + (big - 0.5) * 0.5;
    // Hard bands, their edges broken into an ordered dither on the texel grid (the reference's checker transitions).
    float d = (PixBayer(cell.xz + cell.y * 3.0) - 0.5) * 0.22 * _PixRockDeep.a;
    half3 c = _PixRockDeep.rgb;
    c = lerp(c, _PixRockShade.rgb, step(-0.35, ndl + d));
    c = lerp(c, _PixRockMid.rgb, step(0.05, ndl + d));
    c = lerp(c, _PixRockLit.rgb, step(0.45, ndl + d));
    // Texel-to-texel value jitter (stone grain).
    c *= 0.9h + 0.2h * PixHash(cell);

    // Moss on what faces up.
    float upN = n.y + (big - 0.5) * 0.35 + (PixHash(cell + 3.1) - 0.5) * 0.15;
    half moss = step(_PixParams.y, upN);
    float mh = PixHash(cell + 7.7);
    half3 mossCol = mh < 0.25 ? _PixCaveMossDark.rgb : mh < 0.75 ? _PixCaveMoss.rgb : _PixCaveMossLight.rgb;
    c = lerp(c, mossCol * lerp(0.75h, 1.0h, step(0.05, ndl)), moss * _PixMoss.a);

    // Drips down the steep faces: thin vertical runs of moss, a bright bead at the end.
    half steep = 1.0h - step(0.5, abs(n.y));
    float2 colId = float2(floor(wp.x * T), floor(wp.z * T));
    float colH = PixHash2(colId * 1.37 + 5.0);
    float run = frac(wp.y * 0.09 + PixHash2(colId + 2.0) * 3.0);
    half drip = steep * step(colH, _PixParams.z) * step(0.86, run);
    half tip = drip * (1.0h - step(0.875, run));
    c = lerp(c, _PixCaveMoss.rgb, drip);
    c = lerp(c, _PixCaveMossGlow.rgb, tip);
    glow += _PixCaveMossGlow.rgb * tip; glowAmt = max(glowAmt, tip * 0.6h);

    // Coral mushroom caps on the ledges.
    if (n.y > 0.72 && _PixShroom.a > 0.0)
    {
        float2 cc = floor(wp.xz / 2.6);
        float hc = PixHash2(cc * 1.91 + 0.7);
        if (hc < _PixShroom.a)
        {
            float2 centre = (cc + 0.25 + 0.5 * float2(PixHash2(cc + 3.3), PixHash2(cc + 9.1))) * 2.6;
            float r = lerp(0.45, 0.95, PixHash2(cc + 4.4));
            float2 q = (wq.xz - centre) / r;
            float dd = dot(q, q);
            if (dd < 1.0)
            {
                half3 cap = lerp(_PixShroom.rgb, _PixShroomLight.rgb, step(dd, 0.35) * step(q.y, 0.1));
                // A few pale spots.
                cap = lerp(cap, _PixShroomLight.rgb * 1.15h, step(0.86, PixHash(cell + 21.0)) * step(dd, 0.7));
                c = cap;
                glow += cap; glowAmt = max(glowAmt, _PixShroomLight.a);
            }
        }
    }

    return c;
}

#endif
