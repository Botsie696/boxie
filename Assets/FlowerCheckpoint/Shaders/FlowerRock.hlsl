#ifndef FLOWER_CP_ROCK_INCLUDED
#define FLOWER_CP_ROCK_INCLUDED

// Flower checkpoint: the cliffs (PixelRockLit, 3.9/3.10-only copy). The reference's pale karst in flat brush strokes:
//   stone     warm pale lilac toward the camera-relative light, blue-violet in shade, in three flat paint values broken
//             by long vertical strokes (the way the painting drags the brush down a cliff)
//   moss      what faces up is sage-green moss with lime dabs, spilling over the edges in ragged tongues
//   blossom   clumps of flowering shrubs on the ledges - magenta, crimson, pink, purple, blue, orange - each clump one
//             colour with lighter dabs, and green vines trailing down the faces from the moss
// Colour depends only on world position and normal, so stacked pieces read as one cliff.
// Needs FlowerCommon.hlsl.

half3 FlRockColor(float3 wp, float3 n)
{
    float3 L = normalize(_FlLightDir.xyz);
    float S = max(_FlLightDir.w, 0.1);

    float big = PixNoise(wp * 0.06);
    float ndl = dot(n, L) + (big - 0.5) * 0.5;
    // A coordinate along the face (horizontal) and down it.
    float2 nh = n.xz;
    float2 t = dot(nh, nh) > 1e-4 ? normalize(float2(-nh.y, nh.x)) : float2(1, 0);
    float a = dot(wp.xz, t) * S;
    float y = wp.y * S;

    // Vertical brush strokes on the stone: long in y, narrow in x.
    half stroke = (half)(PixNoise(float3(a * 1.4, y * 0.12, 3.0)) * 0.65 + PixNoise(float3(a * 3.3, y * 0.3, 8.0)) * 0.35);
    half lit = FlBands((half)saturate(smoothstep(-0.25, 0.45, ndl) * 0.85 + (stroke - 0.5) * 0.45), 3.0h);
    half3 c = lerp(_FlStoneShade.rgb, _FlStone.rgb, lit);
    c *= 0.94h + 0.12h * stroke;

    // Green vines trailing down the steep faces (they start under the moss and thin out).
    half steep = 1.0h - (half)smoothstep(0.4, 0.7, abs(n.y));
    float vcol = a * 0.9 + (PixNoise(float3(y * 0.08, a * 0.05, 4.0)) - 0.5) * 1.2;
    float vd = abs(frac(vcol) - 0.5);
    half vine = (half)(1.0 - smoothstep(0.05, 0.09, vd)) * (half)step(0.62, PixNoise(float3(floor(vcol) * 3.1, 1.0, 9.0)))
              * (half)step(0.35, PixNoise(float3(floor(vcol) * 1.7, y * 0.05, 2.0))) * steep;
    c = lerp(c, lerp(_FlCliffMoss.rgb * 0.7h, _FlCliffMoss.rgb, lit), vine * 0.85h);

    // Moss on what faces up, ragged at its edge.
    float upN = n.y + (big - 0.5) * 0.45 + (PixNoise(wp * 0.5) - 0.5) * 0.35;
    if (upN > 0.5)
    {
        half dab = (half)PixNoise(float3(wp.xz * 0.9, wp.y * 0.3));
        half3 g = lerp(_FlCliffMoss.rgb * 0.78h, _FlCliffMoss.rgb, FlBands(saturate(dab * 1.2h), 3.0h));
        g = lerp(g, _FlCliffMossLight.rgb, (half)step(0.7, PixNoise(wp * 1.3 + 5.0)) * 0.8h);
        c = g * lerp(0.82h, 1.0h, lit);

        // Blossom clumps on the ledges: each clump one colour (cell hash), with lighter dabs.
        float cs = 2.6;
        float3 cell = floor(wp / cs);
        half clump = (half)smoothstep(0.55, 0.62, PixNoise(wp * 0.33 + 17.0) + (PixHash(cell) - 0.5) * 0.1);
        clump *= (half)step(1.0 - _FlParams.w, PixHash(cell + 3.0));
        if (clump > 0.0h)
        {
            half3 bc = FlBloom(PixHash(floor(wp / 7.0) + 9.0));
            half leaf = (half)PixNoise(wp * 2.2 + 1.0);
            bc *= lerp(0.72h, 1.1h, FlBands(leaf, 3.0h)) * lerp(0.85h, 1.0h, lit);
            c = lerp(c, bc, clump);
        }
    }
    return c;
}

#endif
