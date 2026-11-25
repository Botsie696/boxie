#ifndef FLOWER_CP_COMMON_INCLUDED
#define FLOWER_CP_COMMON_INCLUDED

// Flower checkpoint ("Blossom Vale", MainScene prop camera 3.10 checkpoint 6 + MainScene prop camera Flower): shared
// helpers for the painted flower-valley look (Tam Nguyen's "Somewhere in my mind": vivid cyan sky with white swirls,
// pale lilac karst cliffs with green moss tops, trees in magenta / crimson / pink / purple / blue / orange blossom, lime
// grass, everything laid in with flat brush strokes).
//
// Where the look is on screen: FlowerCheckpointController writes _FlLook every frame - x = 1 when the look in force (the
// director's _LTA slots) is the flower look, y = 1 when the look being wiped in (_LTB) is, z = 1 for a scene that is
// flower for the whole run. The look wipe (LookWipe.hlsl) picks per pixel, so every flower shader switches along the
// same edge as the colours. Without the controller everything here is zero = no change.

#include "Assets/PixelCheckpoint/Shaders/PixelCommon.hlsl"   // LookWipe + the hashes / noise

float4 _FlLook;          // x look A is flower, y look B is, z always
float4 _FlGrass;         // rgb meadow grass (linear), a brush strength
float4 _FlGrassDark;     // rgb grass in shade (linear)
float4 _FlMoss;          // rgb leg-side moss (linear)
float4 _FlMossDark;      // rgb moss in shade (linear)
float4 _FlSoil;          // rgb the underside (linear)
float4 _FlStone;         // rgb cliff stone facing the light (linear)
float4 _FlStoneShade;    // rgb cliff stone in shade (linear)
float4 _FlCliffMoss;     // rgb moss on the cliff tops (linear)
float4 _FlCliffMossLight;// rgb lime highlight on the cliff moss (linear)
float4 _FlBloom0, _FlBloom1, _FlBloom2, _FlBloom3, _FlBloom4, _FlBloom5;   // blossom colours (linear)
float4 _FlParams;        // x flower chance on the leg tops, y drip share of the leg-side columns, z longest drip (face heights), w flowers on the cliffs
float4 _FlLightDir;      // xyz camera-relative light for the rocks / props, w rock brush scale
float4 _FlRunner;        // xyz runner position, w radius the hanging vines clear round it

// Flower amount on this pixel's side of the wipe (0 other look .. 1 flower).
half FlAt(float2 screenUV)
{
    half w = LTWipeAt(screenUV);
    return (half)max(_FlLook.z, lerp(_FlLook.x, _FlLook.y, w));
}

half3 FlBloom(float h)
{
    int i = (int)floor(saturate(h) * 5.999);
    return (half3)(i == 0 ? _FlBloom0.rgb : i == 1 ? _FlBloom1.rgb : i == 2 ? _FlBloom2.rgb
                 : i == 3 ? _FlBloom3.rgb : i == 4 ? _FlBloom4.rgb : _FlBloom5.rgb);
}

// A flat brush stroke field: long dabs along x (0..1), so colour breaks into strokes instead of smooth gradients.
half FlBrush(float2 p, float key)
{
    float a = PixNoise(float3(p.x * 0.9, p.y * 5.0, key)) * 0.6 + PixNoise(float3(p.x * 2.1 + 3.0, p.y * 11.0, key + 4.0)) * 0.4;
    return (half)a;
}

// Snap a 0..1 shade into a few flat paint values with soft edges (painted, not airbrushed).
half FlBands(half x, half steps)
{
    half s = x * steps;
    half f = frac(s);
    return (floor(s) + smoothstep(0.35h, 0.65h, f)) / steps;
}

// One flower per cell (when the cell's hash passes "chance"): five round petals, a pale centre. p in the same units as
// cell; aa = units per screen pixel. Returns the coverage, colour in fc, centre in ctr.
half FlFlower(float2 p, float cell, float chance, float key, float aa, out half3 fc, out half ctr)
{
    fc = 0; ctr = 0;
    float2 g = floor(p / cell);
    float h = PixHash(float3(g, key));
    if (h > chance) return 0;
    float2 jit = (float2(PixHash(float3(g, key + 1.3)), PixHash(float3(g, key + 2.7))) - 0.5) * 0.36;
    float2 d = p / cell - g - 0.5 - jit;
    float r = length(d);
    float ang = atan2(d.y, d.x) + h * 31.0;
    float size = lerp(0.24, 0.38, PixHash(float3(g, key + 4.1)));
    float R = size * (0.7 + 0.3 * abs(cos(ang * 2.5)));
    float aaC = aa / cell + 1e-4;
    half cov = (half)(1.0 - smoothstep(R - aaC, R + aaC, r));
    ctr = (half)(1.0 - smoothstep(size * 0.28 - aaC, size * 0.28 + aaC, r));
    fc = FlBloom(PixHash(float3(g, key + 5.9)));
    fc *= (half)lerp(0.82, 1.12, saturate(r / max(R, 1e-3)));   // a lighter rim, like a loaded brush
    return cov;
}

#endif
