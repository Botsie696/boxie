#ifndef STORYBOOK_CP_ROCK_INCLUDED
#define STORYBOOK_CP_ROCK_INCLUDED

// Storybook checkpoint: the rocks (PixelRockLit, 3.9-only copy). The reference's limestone in pen and watercolour:
//   wash      two washes - warm pale stone toward the camera-relative light, cool grey-blue in shade - with a soft
//             painted edge and pooled pigment
//   pen       broken vertical crack strokes down the steep faces, diagonal hatching in the shade; lines keep one screen
//             width at any distance (they are sized by the world size of a pixel)
//   foliage   what faces up grows green bushes: a light wash with dark dabs and an inked, scalloped edge
// Colour depends only on world position and normal, so stacked rock pieces read as one cliff.
// Needs StorybookCommon.hlsl.

half3 SbRockColor(float3 wp, float3 n)
{
    float aa = max(length(fwidth(wp)) * 0.75, 1e-4);
    float pen = aa * 0.9 + 0.006;
    float3 L = normalize(_SbLightDir.xyz);
    float S = max(_SbLightDir.w, 0.1);

    float big = PixNoise(wp * 0.07);
    float ndl = dot(n, L) + (big - 0.5) * 0.45;
    half lit = (half)smoothstep(-0.05, 0.3, ndl);
    half3 c = lerp(_SbStoneShade.rgb, _SbStone.rgb, lit);
    c *= 0.93h + 0.14h * (half)PixNoise(wp * 0.35 + 3.0);

    // A coordinate along the face (horizontal) and down it.
    float2 nh = n.xz;
    float2 t = dot(nh, nh) > 1e-4 ? normalize(float2(-nh.y, nh.x)) : float2(1, 0);
    float a = dot(wp.xz, t);
    float2 fp = float2(a, wp.y) * S;

    half ink = 0;
    half steep = 1.0h - (half)smoothstep(0.45, 0.75, abs(n.y));
    // Vertical cracks, broken and wobbly.
    {
        float col = fp.x * 0.45 + (PixNoise(float3(fp.y * 0.18, fp.x * 0.05, 4.0)) - 0.5) * 0.9;
        float d = (frac(col) - 0.5) / (0.45 * S);
        half keep = (half)step(0.55, PixNoise(float3(floor(col) * 3.1, fp.y * 0.12, 9.0)));
        ink = max(ink, SbPen(d, pen, aa) * keep * steep);
    }
    // Hatching in the shade.
    {
        half dark = 1.0h - lit;
        half h = SbHatch(fp, 1.6, aa, pen * 0.6) * (half)step(0.3, PixNoise(wp * 0.25 + 11.0));
        ink = max(ink, h * dark * 0.85h * _SbStone.a);
    }

    // Foliage on what faces up.
    float upN = n.y + (big - 0.5) * 0.5 + (PixNoise(wp * 0.6) - 0.5) * 0.35;
    float edge = (upN - 0.58) / max(fwidth(upN), 1e-4);
    if (upN > 0.58)
    {
        half dab = (half)smoothstep(0.55, 0.7, PixNoise(wp * 0.9 + 2.0));
        half3 g = lerp(_SbGreen.rgb, _SbGreenDark.rgb, max(dab, 1.0h - lit) * 0.8h);
        g *= 0.92h + 0.16h * (half)PixNoise(wp * 2.2);
        c = g;
        ink = (half)(PixNoise(wp * 1.7) > 0.72 ? 0.35 : 0.0);   // a few leafy pen marks
    }
    ink = max(ink, (half)(1.0 - smoothstep(0.8, 1.8, abs(edge))) * 0.9h);

    c *= SbWash(wp * 0.3);
    half3 inkCol = lerp(_SbInk.rgb, c * 0.35h, 0.2h);
    return lerp(c, inkCol, saturate(ink) * _SbInk.a);
}

#endif
