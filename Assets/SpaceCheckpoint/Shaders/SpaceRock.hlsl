#ifndef SPACE_CP_ROCK_INCLUDED
#define SPACE_CP_ROCK_INCLUDED

// Space checkpoint: the far Martian rocks (PixelRockLit). Mesas and spires of layered red sandstone laid in oil:
//   stone     rust-orange toward the camera-relative light, violet-brown in shade (the reference's shadows are purple),
//             three flat paint values broken by long vertical drags of the brush
//   strata    wobbling sediment bands by height - ochre, brick, cream - the layered look of a mesa wall
//   dust      what faces up is pale pink-cream dust, ragged at its edge, with the odd darker boulder dab
// Colour depends only on world position and normal, so stacked pieces read as one cliff. Needs SpaceCommon.hlsl.

half3 SpRockColor(float3 wp, float3 n)
{
    float3 L = normalize(_SpLightDir.xyz);
    float S = max(_SpLightDir.w, 0.1);

    float big = PixNoise(wp * 0.05);
    float ndl = dot(n, L) + (big - 0.5) * 0.45;
    float2 nh = n.xz;
    float2 t = dot(nh, nh) > 1e-4 ? normalize(float2(-nh.y, nh.x)) : float2(1, 0);
    float a = dot(wp.xz, t) * S;
    float y = wp.y * S;

    // Vertical brush drags.
    half stroke = (half)(PixNoise(float3(a * 1.2, y * 0.1, 3.0)) * 0.65 + PixNoise(float3(a * 3.1, y * 0.28, 8.0)) * 0.35);
    half lit = SpBands((half)saturate(smoothstep(-0.3, 0.5, ndl) * 0.85 + (stroke - 0.5) * 0.5), 3.0h);
    half3 c = lerp(_SpRockShade.rgb, _SpRockLit.rgb, lit);
    c *= 0.93h + 0.14h * stroke;

    // Sediment strata: height bands that wobble across the face.
    float wob = (PixNoise(float3(a * 0.12, 1.0, y * 0.02)) - 0.5) * 3.0;
    float band = y * 0.42 + wob;
    float bi = floor(band);
    half bh = (half)PixHash(float3(bi, 7.0, 3.0));
    half3 bc = bh > 0.62h ? _SpRockBand1.rgb : _SpRockBand0.rgb;
    half inBand = (half)step(0.35, bh) * (half)(1.0 - smoothstep(0.55, 0.62, frac(band)));
    c = lerp(c, bc * lerp(0.7h, 1.05h, lit), inBand * 0.55h);
    // A dark thin seam between strata.
    c *= 1.0h - (half)(1.0 - smoothstep(0.02, 0.05, min(frac(band), 1.0 - frac(band)))) * 0.18h;

    // Pale dust on the tops, ragged at the edge.
    float upN = n.y + (big - 0.5) * 0.45 + (PixNoise(wp * 0.5) - 0.5) * 0.35;
    if (upN > 0.5)
    {
        half dab = (half)PixNoise(float3(wp.xz * 0.8, wp.y * 0.3));
        half3 g = lerp(_SpRockDust.rgb * 0.78h, _SpRockDust.rgb, SpBands(saturate(dab * 1.25h), 3.0h));
        g = lerp(g, _SpRockBand1.rgb * 0.8h, (half)step(0.78, PixNoise(wp * 1.3 + 5.0)) * 0.6h);
        c = g * lerp(0.82h, 1.0h, lit);
    }
    return c;
}

#endif
