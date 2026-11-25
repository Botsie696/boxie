#ifndef STORYBOOK_CP_COMMON_INCLUDED
#define STORYBOOK_CP_COMMON_INCLUDED

// Storybook checkpoint ("Storybook Keep", MainScene prop camera 3.9 checkpoint 5 + MainScene prop camera Storybook): shared
// helpers for the ink-and-watercolour castle look (pen lines in sepia ink, flat watercolour washes on cream paper, orange
// tile roofs, pale limestone, teal sky with cumulus clouds).
//
// Where the look is on screen: StorybookCheckpointController writes _SbLook every frame - x = 1 when the look in force (the
// director's _LTA slots) is the storybook look, y = 1 when the look being wiped in (_LTB) is, z = 1 for a scene that is
// storybook for the whole run. The look wipe (LookWipe.hlsl) picks per pixel, so every storybook shader switches along the
// same edge as the colours. Without the controller everything here is zero = no change.

#include "Assets/PixelCheckpoint/Shaders/PixelCommon.hlsl"   // LookWipe + the hashes / noise

float4 _SbLook;         // x look A is storybook, y look B is, z always
float4 _SbInk;          // rgb pen ink (linear), a line strength
float4 _SbPaper;        // rgb paper (linear), a watercolour blotch strength
float4 _SbStone;        // rgb limestone (linear), a hatch strength
float4 _SbStoneShade;   // rgb limestone in shade (linear)
float4 _SbPlaster;      // rgb plaster / cream walls (linear)
float4 _SbTimber;       // rgb dark timber beams (linear)
float4 _SbWood;         // rgb warm plank wood (linear)
float4 _SbRoof;         // rgb orange roof tiles (linear)
float4 _SbRoofDark;     // rgb roof in shade / the eaves (linear)
float4 _SbRoofAlt;      // rgb second roof colour (weathered red-brown) (linear)
float4 _SbSlate;        // rgb slate roof (blue-grey) (linear)
float4 _SbWindow;       // rgb window interior (linear)
float4 _SbShutter;      // rgb window frames / shutters (linear)
float4 _SbGreen;        // rgb foliage (linear)
float4 _SbGreenDark;    // rgb foliage in shade (linear)
float4 _SbSky;          // rgb sky teal (linear) - the arches' see-through
float4 _SbParams;       // x pen width (cube units), y hatch lines per unit, z bridge share of the legs, w roof share of the tops
float4 _SbLightDir;     // xyz camera-relative light for the rocks / props, w rock pen scale

// Storybook amount on this pixel's side of the wipe (0 other look .. 1 storybook).
half SbAt(float2 screenUV)
{
    half w = LTWipeAt(screenUV);
    return (half)max(_SbLook.z, lerp(_SbLook.x, _SbLook.y, w));
}

// A pen line: d = distance to the line's centre, w = half width, aa = world units per screen pixel.
half SbPen(float d, float w, float aa)
{
    return (half)(1.0 - smoothstep(w - aa, w + aa, abs(d)));
}

// Watercolour wash: the pigment pools unevenly (low-frequency blotches + a finer granulation).
half SbWash(float3 p)
{
    float a = PixNoise(p * 0.55) * 0.65 + PixNoise(p * 2.3 + 7.1) * 0.35;
    return (half)(1.0 + (a - 0.5) * 0.32 * _SbPaper.a);
}

// Diagonal pen hatching (0..1 coverage) on a 2D coordinate, lines per unit = density.
half SbHatch(float2 p, float density, float aa, float width)
{
    float t = (p.x + p.y) * density;
    float d = (frac(t) - 0.5) / density;
    return SbPen(d, width, aa);
}

#endif
