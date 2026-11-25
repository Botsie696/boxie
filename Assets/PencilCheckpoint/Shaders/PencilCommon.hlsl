#ifndef PENCIL_CP_COMMON_INCLUDED
#define PENCIL_CP_COMMON_INCLUDED

// Pencil checkpoint ("Graphite Sketchbook", MainScene prop camera 3.10 checkpoint 8 + MainScene prop camera Pencil): shared
// helpers for the black-and-white pencil-on-rough-paper look (references: a scribbly sketch robot, a graphite spaceship
// study, an ink-and-grey medieval street, a hatched dungeon).
//
// Where the look is on screen: PencilCheckpointController writes _PcLook every frame - x = 1 when the look in force (the
// director's _LTA slots) is the pencil look, y = 1 when the look being wiped in (_LTB) is, z = 1 for a scene that is pencil
// for the whole run. Without the controller everything here is zero = no change.
//
// Everything is drawn as graphite on paper: a tone (0 paper .. 1 graphite) built from hatch strokes, sketch lines and
// smudges, turned into a colour by PcInk(). Lines are measured in surface units with screen-space anti-aliasing.

#include "Assets/PixelCheckpoint/Shaders/PixelCommon.hlsl"   // LTWipeAt + the hashes / noise

float4 _PcLook;        // x look A is pencil, y look B is, z always
float4 _PcPaper;       // rgb paper (linear), a paper tooth strength
float4 _PcGraphite;    // rgb graphite (linear), a stroke darkness
float4 _PcParams;      // x hatch lines per unit, y line width, z art-leg share, w sketch wobble
float4 _PcStage;       // x first leg serial of the level, y first serial of the next level, z cycle legs (always scene), w stages
float4 _PcLightDir;    // xyz camera-relative light (world), w rock hatch scale

// Pencil amount on this pixel's side of the wipe (0 other look .. 1 pencil).
half PcAt(float2 screenUV)
{
    half w = LTWipeAt(screenUV);
    return (half)max(_PcLook.z, lerp(_PcLook.x, _PcLook.y, w));
}

float PcHash(float x) { return PixHash(float3(x, 3.17, 9.71)); }

// A pencil line of half width w around d = 0 (d in surface units), aa = the screen footprint of one pixel there.
half PcLine(float d, float w, float aa)
{
    return (half)(1.0 - smoothstep(w - aa, w + aa, abs(d)));
}

// Graphite grain along a stroke: lines are never solid - the paper's tooth breaks them.
half PcGrain(float2 p, float key)
{
    return (half)lerp(0.55, 1.0, PixNoise(float3(p * float2(7.0, 23.0), key)));
}

// One family of parallel hatch strokes: angle a (radians), spacing s, half width w. Strokes are broken into dashes of
// random length so the shading reads as drawn by hand, and wobble a little sideways.
half PcHatchW(float2 p, float a, float s, float w, float aa, float key, float wf)
{
    float2 dir = float2(cos(a), sin(a));
    float2 nrm = float2(-dir.y, dir.x);
    float across = dot(p, nrm) / s;
    float along = dot(p, dir);
    float row = floor(across + 0.5);
    float wob = (sin(along * 2.3 * wf + row * 1.7 + key) * 0.12 + sin(along * 6.1 * wf + row * 0.9) * 0.05) * min(wf * 4.0, 1.0);
    float d = (across - row + wob) * s;
    float seg = along * 1.3 + PcHash(row + key) * 7.0;
    float dash = frac(seg);
    float len = lerp(0.6, 0.97, PcHash(floor(seg) * 1.7 + row * 3.1 + key));
    half on = (half)(step(dash, len));
    half grain = (half)lerp(0.6, 1.0, PixHash(float3(floor(along * 40.0), row, key)));
    return PcLine(d, w * lerp(0.7, 1.25, PcHash(row * 2.3 + key)), aa) * on * grain;
}

half PcHatch(float2 p, float a, float s, float w, float aa, float key) { return PcHatchW(p, a, s, w, aa, key, 1.0); }

// Hatched tone: 0 paper .. 1 dense cross-hatching. Layers switch on with the tone: diagonal, the cross diagonal,
// horizontal fills, then a smudge.
half PcTone(float2 p, half tone, float s, float w, float aa, float key)
{
    half ink = 0.0h;
    ink = max(ink, PcHatch(p, 0.785, s, w, aa, key) * (half)smoothstep(0.08, 0.2, tone));
    ink = max(ink, PcHatch(p, -0.785, s * 1.1, w * 0.9, aa, key + 3.0) * (half)smoothstep(0.38, 0.5, tone));
    ink = max(ink, PcHatch(p, 0.05, s * 0.8, w * 0.85, aa, key + 6.0) * (half)smoothstep(0.62, 0.75, tone));
    ink = max(ink, (half)smoothstep(0.8, 1.0, tone) * 0.55h * (half)lerp(0.6, 1.0, PixHash(float3(floor(p * 30.0), key + 9.0))));
    return ink * (half)saturate(0.4 + tone * 0.9);
}

// Graphite on paper: tone 0..1 -> linear colour. Strokes never reach pure black; paper keeps its tooth.
half3 PcInk(half ink)
{
    return lerp(_PcPaper.rgb, _PcGraphite.rgb, saturate(ink) * (half)_PcGraphite.a);
}

// Sketch outline: two slightly offset wobbly strokes along an edge at distance d (both strokes measured in the same
// units). t runs along the edge (for the wobble).
half PcSketchEdge(float d, float t, float w, float aa, float key)
{
    float wob = _PcParams.w;
    float o1 = (sin(t * 1.9 + key) * 0.6 + sin(t * 4.7 + key * 2.0) * 0.4) * wob;
    float o2 = (sin(t * 1.4 + key * 3.0) * 0.6 + sin(t * 5.3) * 0.4) * wob + wob * 0.9;
    half g = (half)lerp(0.65, 1.0, PixHash(float3(floor(t * 30.0), key, 1.0)));
    half a = PcLine(d - o1, w, aa) * g;
    // The second, lighter stroke stops and starts (the hand lifting).
    half b = PcLine(d - o2, w * 0.7, aa) * 0.6h * (half)step(0.35, frac(t * 0.37 + PcHash(key) * 3.0));
    return max(a, b);
}

// Distance fields (2D).
float PcBox(float2 p, float2 c, float2 h)
{
    float2 d = abs(p - c) - h;
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
}
float PcSeg(float2 p, float2 a, float2 b)
{
    float2 pa = p - a, ba = b - a;
    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
    return length(pa - ba * h);
}
float PcEllipse(float2 p, float2 c, float2 ab, float rot)
{
    float2 q = p - c;
    float cs = cos(rot), sn = sin(rot);
    q = float2(cs * q.x + sn * q.y, -sn * q.x + cs * q.y);
    return (length(q / ab) - 1.0) * min(ab.x, ab.y);
}

// Camera-relative light amount for a normal: 1 facing the light .. 0 away.
half PcLit(float3 n)
{
    return (half)saturate(dot(normalize(n), normalize(_PcLightDir.xyz)) * 0.5 + 0.5);
}

#endif
