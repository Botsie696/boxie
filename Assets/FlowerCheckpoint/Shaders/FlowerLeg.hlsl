#ifndef FLOWER_CP_LEG_INCLUDED
#define FLOWER_CP_LEG_INCLUDED

// Flower checkpoint: the leg texture (FlatFaceBlockPixel). Every leg is a strip of painted meadow:
//   top     lime grass laid in long brush strokes along the leg, scattered with flowers of every blossom colour (two
//           sizes), a darker rim at the edges
//   sides   deep moss in flat strokes, a grass lip along the top edge with pointed tufts and small flowers, pale stone
//           peeking through low down; wisteria-like flower drips hang from the lip in some columns - a green stem with
//           clusters that grow toward the tip - and the long ones run off the bottom edge
//   bottom  dark root soil with root strokes; the long drips carry on round the edge
// The face colour's lit / shade relation (the look's left / right faces and the per-cube tint) shades the paint; edges
// are anti-aliased with the world size of a screen pixel. Hanging geometry under the legs is FlowerCheckpointController's.
// Needs FlowerCommon.hlsl and the object matrices.

// The drips of one side (sideKey) at along-leg x: returns coverage, colour in dc. t = distance down from the lip
// (face heights; > the face height = round the bottom edge).
half FlDrip(float x, float t, float sideKey, float key, float aa, out half3 dc)
{
    dc = 0;
    const float cw = 0.13;
    float colI = floor(x / cw);
    float hc = PixHash(float3(colI, sideKey, key + 7.3));
    if (hc > _FlParams.y || t < 0.0) return 0;
    float len = lerp(0.22, max(_FlParams.z, 0.3), PixHash(float3(colI, sideKey, key + 2.1)));
    if (t > len + 0.05) return 0;
    float wob = sin(t * 18.0 + colI * 2.3) * 0.012;
    float xc = (colI + 0.5) * cw + (PixHash(float3(colI, sideKey, key + 3.3)) - 0.5) * cw * 0.4 + wob;
    float dx = x - xc;
    half3 bloom = FlBloom(frac(PixHash(float3(colI, sideKey, key + 9.1)) * 0.999));
    // Green stem.
    half cov = (half)(1.0 - smoothstep(0.007 - aa, 0.007 + aa, abs(dx))) * (half)step(t, len);
    dc = (half3)_FlMossDark.rgb * 0.8h;
    // Clusters along the stem, bigger toward the tip.
    float step_ = 0.07;
    float k = floor(t / step_);
    [unroll] for (int j = 0; j < 2; j++)
    {
        float kk = k + j;
        float ty = (kk + 0.5) * step_;
        if (ty > len) continue;
        float grow = saturate(ty / max(len, 1e-3));
        float r = lerp(0.022, 0.05, grow);
        float ox = (PixHash(float3(colI, kk, key + 5.0)) - 0.5) * 0.03;
        float d = length(float2(dx - ox, t - ty));
        half b = (half)(1.0 - smoothstep(r - aa, r + aa, d));
        if (b > 0.0h)
        {
            half3 bc = bloom * (half)lerp(0.8, 1.15, PixHash(float3(colI, kk, key + 6.0)));
            bc = lerp(bc, half3(1, 1, 1), (half)(1.0 - smoothstep(r * 0.25, r * 0.6, d)) * 0.35h);
            dc = lerp(dc, bc, b);
            cov = max(cov, b);
        }
    }
    return cov;
}

half3 FlLegColor(float3 posOS, float3 nOS, float3 nWS, float3 wp, half3 col, half3 lLeft, half3 lRight, half3 tint, float4 leg)
{
    half tintMax = max(max(tint.r, tint.g), max(tint.b, 1e-3h));
    float aa = max(length(fwidth(wp)) * 0.75, 1e-4);

    float3 origin = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
    float3 legW = normalize(float3(unity_ObjectToWorld._m02, unity_ObjectToWorld._m12, unity_ObjectToWorld._m22));
    bool hasLeg = leg.w > 0.5;
    float u = hasLeg ? leg.x + posOS.z : dot(origin, legW) + posOS.z + 500.0;
    float3 lateral = origin - legW * dot(origin, legW);
    float serial = hasLeg ? floor(leg.z + 0.5) : floor(frac(dot(round(lateral * 2.0), float3(0.173, 0.311, 0.071))) * 977.0);
    float key = serial * 1.37;

    float3 an = abs(nOS);
    bool cap = an.z > 0.5;
    float3 upOS = normalize(mul((float3x3)unity_WorldToObject, float3(0, 1, 0)));
    bool level = abs(upOS.z) < 0.5;
    float3 latOS = level ? normalize(cross(upOS, float3(0, 0, 1))) : float3(1, 0, 0);
    float v = dot(posOS, upOS) + 0.5;                         // 0 bottom .. 1 top
    float lat = dot(posOS, latOS) + 0.5;                      // 0 .. 1 across

    half lumMax = max(max(PixLum(lLeft), PixLum(lRight)) * tintMax, 1e-3h);
    half k = saturate(PixLum(col) / lumMax);
    half shadeMul = lerp(0.62h, 1.0h, k) * tintMax;
    half brushAmt = (half)_FlGrass.a;
    half3 c;

    bool top = nWS.y > 0.6;
    bool bottom = nWS.y < -0.6;

    if (top)
    {
        float2 p = (level && !cap) ? float2(u, lat) : posOS.xy + 0.5;
        half s = FlBrush(p * float2(1.0, 1.6), key);
        half g = FlBands(saturate(s * 1.15h), 3.0h);
        c = lerp(_FlGrassDark.rgb, _FlGrass.rgb, lerp(0.75h, g, brushAmt));
        // Lime flecks (sunlit blades) and a darker rim at the edges.
        c = lerp(c, _FlGrass.rgb * 1.18h, (half)step(0.82, PixNoise(float3(p * float2(9.0, 22.0), key + 1.0))) * 0.6h);
        float edge = (level && !cap) ? min(lat, 1.0 - lat) : 0.5 - max(abs(posOS.x), abs(posOS.y));
        c *= lerp(0.8h, 1.0h, (half)smoothstep(0.02, 0.1, edge));
        // Flowers: big ones, then a scatter of tiny ones.
        half3 fc; half ctr;
        half f = FlFlower(p, 0.5, _FlParams.x, key + 11.0, aa, fc, ctr);
        c = lerp(c, fc, f);
        c = lerp(c, half3(1.0h, 0.93h, 0.75h), ctr * f * 0.85h);
        half f2 = FlFlower(p + 0.11, 0.24, _FlParams.x * 0.6, key + 23.0, aa, fc, ctr);
        c = lerp(c, fc * 1.05h, f2 * (1.0h - f));
        c *= tintMax;
    }
    else if (bottom)
    {
        // Root soil: dark, root strokes across the leg; the long drips run on round both edges.
        float2 p = level ? float2(u, lat) : posOS.xy + 0.5;
        half s = FlBrush(p * float2(2.0, 1.0), key + 40.0);
        c = _FlSoil.rgb * lerp(0.8h, 1.15h, s);
        float root = abs(frac(p.y * 3.0 + sin(p.x * 4.0 + key) * 0.25) - 0.5);
        c = lerp(c, _FlMossDark.rgb * 0.7h, (half)(1.0 - smoothstep(0.03, 0.06, root)) * 0.5h);
        if (level && !cap)
        {
            float side = lat < 0.5 ? 0.0 : 1.0;
            float t = 0.76 + min(lat, 1.0 - lat);              // down from the lip, round the edge
            half3 dc;
            half d = FlDrip(u, t, side, key, aa, dc);
            c = lerp(c, dc, d);
        }
        c *= tintMax;
    }
    else
    {
        // ---- moss sides ----
        float side = level ? (dot(nOS, latOS) > 0.0 ? 1.0 : 0.0) : 0.0;
        float x = level ? (cap ? lat + 977.0 * sign(nOS.z) : u) : ((an.x > 0.5 ? posOS.y : posOS.x) + 0.5) + (an.x > 0.5 ? 31.0 : 57.0);
        float y = level ? v : -wp.y;
        half s = FlBrush(float2(x * 1.3, y * 0.8), key + 60.0 + side * 9.0);
        half m = FlBands(saturate(s * 1.2h - 0.05h), 3.0h);
        c = lerp(_FlMossDark.rgb, _FlMoss.rgb, lerp(0.6h, m, brushAmt));
        // Pale stone peeking through low down.
        half stone = (half)step(0.72, PixNoise(float3(x * 2.4, y * 3.0, key + 5.0))) * (half)(1.0 - smoothstep(0.35, 0.6, y));
        c = lerp(c, _FlStone.rgb * 0.92h, stone * 0.85h);
        // Flowers growing out of the moss, sparser toward the bottom.
        {
            half3 fc; half ctr;
            half f = FlFlower(float2(x, y), 0.26, _FlParams.x * 0.6 * saturate(y + 0.3), key + 90.0 + side, aa, fc, ctr);
            c = lerp(c, fc, f);
            c = lerp(c, half3(1.0h, 0.93h, 0.75h), ctr * f * 0.7h);
        }

        if (level)
        {
            // Grass lip with pointed tufts.
            float tuft = 0.05 * abs(sin(x * 23.0 + PixHash(float3(floor(x * 3.6), side, key)) * 6.0));
            float lipEdge = 0.76 - tuft;
            if (y > lipEdge)
            {
                half g = FlBrush(float2(x, y) * float2(1.0, 3.0), key + 70.0);
                c = lerp(_FlGrassDark.rgb, _FlGrass.rgb, 0.55h + g * 0.45h);
                half3 fc; half ctr;
                half f = FlFlower(float2(x, y), 0.2, _FlParams.x, key + 80.0 + side, aa, fc, ctr);
                c = lerp(c, fc, f * (half)step(lipEdge + 0.03, y));
            }
            // A thin shadow under the lip.
            c *= 1.0h - (half)(1.0 - smoothstep(0.0, 0.05, lipEdge - y)) * (half)step(y, lipEdge) * 0.3h;
            // Flower drips from the lip.
            half3 dc;
            half d = FlDrip(x, 0.76 - y, side, key, aa, dc);
            c = lerp(c, dc, d);
        }
        c *= shadeMul;
    }
    return c;
}

#endif
