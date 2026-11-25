#ifndef SPACE_CP_COMMON_INCLUDED
#define SPACE_CP_COMMON_INCLUDED

// Space checkpoint ("Rogue Planet", MainScene prop camera 3.10 checkpoint 9 + MainScene prop camera Space): shared helpers
// for the oil-painted deep-space look (references: a thick-impasto purple/blue spaceship over a planet, a ringed planet
// over an orange sunset, launching shuttles in teal / violet / orange smoke, a pale red planet with mesas).
//
// Where the look is on screen: SpaceCheckpointController writes _SpLook every frame - x = 1 when the look in force (the
// director's _LTA slots) is the space look, y = 1 when the look being wiped in (_LTB) is, z = 1 for a scene that is
// space for the whole run. The look wipe (LookWipe.hlsl) says per pixel which of the two it shows, so every space shader
// switches along the same edge as the colours. Without the controller everything here is zero = no change.

#include "Assets/PixelCheckpoint/Shaders/PixelCommon.hlsl"   // LTWipeAt + the hashes / noise

float4 _SpLook;        // x look A is space, y look B is, z always
float4 _SpTop;         // rgb leg top (hull deck) facing the light (linear), a brush strength
float4 _SpTopDark;     // rgb leg top in shade (linear)
float4 _SpSide;        // rgb leg side lit (linear)
float4 _SpSideDark;    // rgb leg side in shade (linear)
float4 _SpBottom;      // rgb leg underside (linear)
float4 _SpBottomDark;  // rgb underside in shade (linear)
float4 _SpAccent;      // rgb hazard / engine orange (linear)
float4 _SpGlow;        // rgb cyan light strips, screens (linear)
float4 _SpWindow;      // rgb warm interior light (linear)
float4 _SpCream;       // rgb ship hull white (linear)
float4 _SpMagenta;     // rgb console magenta (linear)
float4 _SpRockLit;     // rgb mars rock facing the light (linear)
float4 _SpRockShade;   // rgb mars rock in shade (linear, violet-brown)
float4 _SpRockBand0;   // rgb strata (linear)
float4 _SpRockBand1;   // rgb strata (linear)
float4 _SpRockDust;    // rgb pale dust on top surfaces (linear)
float4 _SpParams;      // x ship-leg share (of 4), y window glow, z paint ridge strength, w unused
float4 _SpLightDir;    // xyz camera-relative light for the rocks, w rock brush scale

// Space amount on this pixel's side of the wipe (0 other look .. 1 space).
half SpAt(float2 screenUV)
{
    half w = LTWipeAt(screenUV);
    return (half)max(_SpLook.z, lerp(_SpLook.x, _SpLook.y, w));
}

// A flat brush stroke field: long dabs along x, so colour breaks into oil strokes instead of smooth gradients.
half SpBrush(float2 p, float key)
{
    float a = PixNoise(float3(p.x * 0.7, p.y * 4.5, key)) * 0.6 + PixNoise(float3(p.x * 1.9 + 3.0, p.y * 10.0, key + 4.0)) * 0.4;
    return (half)a;
}

// Snap a 0..1 shade into a few flat paint values with soft edges (thick paint, not an airbrush).
half SpBands(half x, half steps)
{
    half s = x * steps;
    half f = frac(s);
    return (floor(s) + smoothstep(0.3h, 0.7h, f)) / steps;
}

// Paint ridge: a bright thin line where a stroke's value steps (the lit edge of thick paint).
half SpRidge(half x, half steps)
{
    half f = frac(x * steps);
    return (half)(smoothstep(0.0, 0.12, f) * (1.0 - smoothstep(0.12, 0.3, f)));
}

// Coverage of a disc / rounded box, anti-aliased with the footprint of one screen pixel.
half SpDisc(float2 d, float r, float aa) { return (half)(1.0 - smoothstep(r - aa, r + aa, length(d))); }
half SpBox(float2 d, float2 h, float aa)
{
    float2 q = abs(d) - h;
    float s = max(q.x, q.y);
    return (half)(1.0 - smoothstep(-aa, aa, s));
}

#endif
