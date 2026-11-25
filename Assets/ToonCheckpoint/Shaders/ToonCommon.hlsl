#ifndef TOON_CP_COMMON_INCLUDED
#define TOON_CP_COMMON_INCLUDED

// Toon checkpoint (MainScene prop camera 3.7 checkpoint 3 + MainScene prop camera Toon): shared helpers.
//
// Where the toon look is on screen: the block role's look slot 15.z (AtmosphereLook.PackBlock leaves it 0 for every
// captured look; Look_Toon writes 1). LookTransitionDirector puts the look in force in _LTA and the next one in _LTB, and
// the wipe (LookWipe.hlsl) says per pixel which of the two the pixel shows - so every toon shader (path, rocks, trail,
// clouds, ground, ink pass) switches on and off along the same wipe edge as the colours. Without a director both arrays
// are zero and nothing here does anything.
//
// The toon palette itself is constant for the run and comes from ToonCheckpointController (globals below).

#include "Assets/PropCameraLooks/LookTransition/Shaders/LookWipe.hlsl"

#ifndef TOON_LT_SLOTS_DECLARED
#define TOON_LT_SLOTS_DECLARED
float4 _LTA[128];
float4 _LTB[128];
#endif

float4 _ToonInk;          // rgb ink (linear), a line strength
float4 _ToonMoss;         // rgb moss in light (linear), a moss amount
float4 _ToonMossDark;     // rgb moss in shade (linear), a hatching strength
float4 _ToonRockLit;      // rgb sunlit stone (linear)
float4 _ToonRockMid;      // rgb stone turning away (linear)
float4 _ToonRockShade;    // rgb stone in shade (cool, linear)
float4 _ToonHighlight;    // rgb warm sunlit edge (linear), a strength
float4 _ToonLightDir;     // xyz toon light (camera relative, set every frame), w paper grain
float4 _ToonGround;       // xz ground disc centre, y ground height, w disc radius (0 = no ground)
float4 _ToonParams;       // x ink width (object units on the path), y side band darkening, z hatch density (per unit), w rock hatch scale

// Toon amount of the look on this pixel's side of the wipe (0 original look .. 1 toon).
half ToonAt(float2 screenUV)
{
    half w = LTWipeAt(screenUV);
    return (half)lerp(_LTA[15].z, _LTB[15].z, w);
}

float ToonHash(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

// Smooth value noise anchored to the world: one pattern runs across every cube of a leg and every piece of a rock.
float ToonNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = ToonHash(i), n100 = ToonHash(i + float3(1, 0, 0));
    float n010 = ToonHash(i + float3(0, 1, 0)), n110 = ToonHash(i + float3(1, 1, 0));
    float n001 = ToonHash(i + float3(0, 0, 1)), n101 = ToonHash(i + float3(1, 0, 1));
    float n011 = ToonHash(i + float3(0, 1, 1)), n111 = ToonHash(i + float3(1, 1, 1));
    return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
}

half ToonLum(half3 c) { return dot(c, half3(0.2126h, 0.7152h, 0.0722h)); }

// Thin diagonal pen strokes in world space (continuous across cubes and rock pieces), broken into dashes by noise.
// 1 on a stroke, 0 between strokes.
half ToonHatch(float3 wp, float density, float breakup)
{
    float h = (wp.x + wp.y * 1.35 + wp.z * 0.8) * density;
    float d = abs(frac(h) - 0.5);                 // 0 in the middle of the gap, 0.5 on the stroke
    float aa = fwidth(h) + 1e-4;
    half stroke = smoothstep(0.5 - 0.11 - aa, 0.5 - 0.11 + aa, d);
    // Dashes: strokes come and go along their length.
    half dash = step(0.5, ToonNoise(wp * float3(1.7, 0.6, 1.7) + floor(h) * 3.1));
    return stroke * dash * breakup;
}

// The cartoon stone of the rocks and cliffs: two hard cel bands from the toon light with a hand-drawn (wobbly)
// terminator, cool shade, moss on what faces up, pen hatching in the shade, a little paper grain. Colour depends only
// on the world position and normal, so stacked rock pieces read as one mass.
half3 ToonRockColor(float3 wp, float3 n)
{
    float3 L = normalize(_ToonLightDir.xyz);
    float big = ToonNoise(wp * 0.06);
    float small = ToonNoise(wp * 0.35 + 7.1);
    half ndl = dot(n, L) + (big - 0.5) * 0.45 + (small - 0.5) * 0.12;
    half lit = smoothstep(0.02h, 0.08h, ndl);
    half full = smoothstep(0.42h, 0.48h, ndl);
    half3 c = lerp(_ToonRockShade.rgb, _ToonRockMid.rgb, lit);
    c = lerp(c, _ToonRockLit.rgb, full);

    // Moss grows on what faces up, its edge broken by the noise.
    half up = smoothstep(0.42h, 0.5h, n.y + (big - 0.5) * 0.5 + (small - 0.5) * 0.18);
    half3 moss = lerp(_ToonMossDark.rgb, _ToonMoss.rgb, lit);
    c = lerp(c, moss, up * _ToonMoss.a);

    // Long vertical weathering streaks down the cliff faces.
    half streak = ToonNoise(float3(wp.x * 0.45, wp.y * 0.03, wp.z * 0.45));
    c *= 1.0h + (streak - 0.5h) * 0.22h * (1.0h - up);

    // Where a cliff meets the valley floor: a grassy skirt and a soft contact shade, so it stands IN the ground.
    if (_ToonGround.w > 0.0)
    {
        float above = wp.y - _ToonGround.y;
        half skirt = (1.0h - smoothstep(0.0h, 1.6h + big * 2.6h, above)) * step(-30.0, above);
        half contact = (1.0h - smoothstep(0.0h, 6.0h, above)) * step(-30.0, above);
        c *= 1.0h - contact * 0.3h;
        c = lerp(c, lerp(_ToonMossDark.rgb, _ToonMoss.rgb, 0.35h), skirt * 0.9h);
    }

    // Pen hatching in the shade only.
    half hatch = ToonHatch(wp, _ToonParams.z * _ToonParams.w, 1.0h) * (1.0h - lit) * _ToonMossDark.a;
    c = lerp(c, c * _ToonInk.rgb * 3.0h, hatch * 0.55h);

    // Paper grain.
    c *= 1.0h + (ToonNoise(wp * 2.3 + 3.3) - 0.5h) * _ToonLightDir.w;
    return c;
}

#endif
