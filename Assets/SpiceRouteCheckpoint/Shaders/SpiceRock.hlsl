#ifndef SPICE_CP_ROCK_INCLUDED
#define SPICE_CP_ROCK_INCLUDED

// Spice Route checkpoint: any of the scene's own rocks still in view (PixelRockLit) painted as the checkpoint's cliffs -
// pink-ochre sandstone in India, pale limestone in Arabia (the environment's half, SrEnvHalf), in three flat gouache
// values with soft strata and paper grain. The checkpoint's own cliffs use SpiceProp (same colours). Needs SpiceCommon.hlsl.

half3 SrRockPaint(float3 wp, float3 n, half3 lit, half3 shade, half3 band)
{
    float3 L = normalize(_SrLightDir.xyz);
    float S = max(_SrLightDir.w, 0.1);
    float big = SrNoise2(wp.xz * 0.05 + wp.y * 0.02);
    float ndl = dot(n, L) + (big - 0.5) * 0.4;
    float2 nh = n.xz;
    float2 t = dot(nh, nh) > 1e-4 ? normalize(float2(-nh.y, nh.x)) : float2(1, 0);
    float a = dot(wp.xz, t) * S;
    float y = wp.y * S;
    half stroke = (half)SrNoise2(float2(a * 0.9, y * 0.12));
    half l = (half)saturate(smoothstep(-0.3, 0.55, ndl) * 0.85 + (stroke - 0.5) * 0.4);
    half v = (floor(l * 3.0h) + smoothstep(0.3h, 0.7h, frac(l * 3.0h))) / 3.0h;
    half3 c = lerp(shade, lit, v);
    float wob = (SrNoise2(float2(a * 0.1, y * 0.03)) - 0.5) * 2.5;
    float b = y * 0.35 + wob;
    c = lerp(c, band * lerp(0.75h, 1.05h, v), (half)step(0.55, PixHash(float3(floor(b), 7.0, 3.0))) * (half)(1.0 - smoothstep(0.5, 0.6, frac(b))) * 0.4h);
    c *= 1.0h + SrGrain(float2(a, y) * 2.0) * 0.08h;
    if (n.y + (big - 0.5) * 0.4 > 0.55) c = lerp(c, lit * 1.08h, 0.6h);
    return c;
}

half3 SrRockColor(float3 wp, float3 n)
{
    half h = SrEnvHalf();
    half3 lit = lerp(SrLin(half3(0.93h, 0.62h, 0.48h)), SrLin(half3(0.94h, 0.85h, 0.68h)), h);
    half3 shade = lerp(SrLin(half3(0.52h, 0.27h, 0.38h)), SrLin(half3(0.45h, 0.38h, 0.55h)), h);
    half3 band = lerp(SrLin(half3(0.85h, 0.45h, 0.36h)), SrLin(half3(0.86h, 0.7h, 0.5h)), h);
    return SrRockPaint(wp, n, lit, shade, band);
}

#endif
