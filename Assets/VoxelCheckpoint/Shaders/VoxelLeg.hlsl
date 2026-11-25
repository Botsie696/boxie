#ifndef VOXEL_CP_LEG_INCLUDED
#define VOXEL_CP_LEG_INCLUDED

// Voxel checkpoint: the leg texture (FlatFaceBlockPixel). Every leg is the reference's slab bridge - in its own colours:
//   top     a bright pixel slab (one of ten colours, never the same as the leg before), one tile per cube with a dark seam,
//           a light bevel pixel row on one long edge and a dark one on the other, worn lighter / darker pixel blotches
//   sides   the slab's own colour as a thick lip along the top, dripping down over the bricks in pixel columns of random
//           length (the darker last pixel of each drip), a 1-pixel shadow under the lip; below it a brick wall in one of
//           six brick colours (staggered courses, dark mortar, a light corner pixel per brick)
//   bottom  darker bricks
// Climbing legs (vertical) carry the slab colour on their sides, tiled per cube. The face colour's lit / shade relation
// (the look's left / right faces and the per-cube tint) shades the texture so the two visible sides always differ.
// Needs VoxelCommon.hlsl and the object matrices.

half3 VxLegColor(float3 posOS, float3 nOS, float3 nWS, float3 wp, half3 col, half3 lLeft, half3 lRight, half3 tint, float4 leg)
{
    half tintMax = max(max(tint.r, tint.g), max(tint.b, 1e-3h));
    float px = max(_VxParams.z, 2.0);

    float3 origin = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
    float3 legW = normalize(float3(unity_ObjectToWorld._m02, unity_ObjectToWorld._m12, unity_ObjectToWorld._m22));
    bool hasLeg = leg.w > 0.5;
    float u = hasLeg ? leg.x + posOS.z : dot(origin, legW) + posOS.z + 500.0;
    float3 lateral = origin - legW * dot(origin, legW);
    float serial = hasLeg ? floor(leg.z + 0.5) : floor(frac(dot(round(lateral * 2.0), float3(0.173, 0.311, 0.071))) * 977.0);
    serial = abs(serial);
    float key = serial * 1.37;
    half3 topC = VxLegTop(serial);
    half3 brickC = VxLegBrick(serial);

    float3 an = abs(nOS);
    bool cap = an.z > 0.5;
    float3 upOS = normalize(mul((float3x3)unity_WorldToObject, float3(0, 1, 0)));
    bool level = abs(upOS.z) < 0.5;
    float3 latOS = level ? normalize(cross(upOS, float3(0, 0, 1))) : float3(1, 0, 0);
    float v = dot(posOS, upOS) + 0.5;                         // 0 bottom .. 1 top
    float lat = dot(posOS, latOS) + 0.5;                      // 0 .. 1 across

    half lumMax = max(max(PixLum(lLeft), PixLum(lRight)) * tintMax, 1e-3h);
    half k = saturate(PixLum(col) / lumMax);
    half shadeMul = lerp(0.68h, 1.0h, k) * tintMax;

    bool top = nWS.y > 0.6;
    bool bottom = nWS.y < -0.6;
    half3 c;

    if (top || !level)
    {
        // ---- the slab: one tile per cube along the leg ----
        // x runs along the leg (one tile per cube), y across.
        float2 q = (level && !cap) ? float2(u, lat) : posOS.xy + 0.5;
        if (!level && !top) q = float2(wp.y, (an.x > 0.5 ? posOS.y : posOS.x) + 0.5);
        float2 p = floor(q * px);
        float2 lp = p - floor(q) * px;                       // pixel inside the tile
        float h = PixHash(float3(p, key + 3.0));
        half blot = (half)PixNoise(float3(p * 0.3, key + 8.0));
        c = topC * (blot > 0.62h ? 1.08h : blot < 0.3h ? 0.9h : 1.0h) * (half)lerp(0.93, 1.06, h);
        if (h > 0.94) c = topC * 1.2h + 0.03h;
        else if (h < 0.05) c = topC * 0.78h;
        // Seam between tiles and the bevel.
        if (lp.x < 1.0) c = topC * 0.62h;
        else if (lp.x < 2.0) c *= 1.1h;
        if (top && level && !cap)
        {
            float lpy = floor(lat * px);
            if (lpy >= px - 1.0) c = topC * 1.22h + 0.04h;
            else if (lpy < 1.0) c = topC * 0.7h;
        }
        c *= top ? tintMax : shadeMul;
    }
    else if (bottom)
    {
        float2 q = cap ? posOS.xy + 0.5 : float2(u, lat);
        c = VxBrick(q * px, brickC * 0.72h, key) * tintMax;
    }
    else
    {
        // ---- sides: slab lip, drips, bricks ----
        float side = cap ? 2.0 : (dot(nOS, latOS) > 0.0 ? 1.0 : 0.0);
        float x = cap ? lat + 977.0 * sign(nOS.z) : u;
        float2 p = floor(float2(x, v) * px);
        float r = px - 1.0 - p.y;                             // pixel row from the top edge
        float lipRows = max(1.0, floor(px * _VxParams.w + 0.5));
        float hc = PixHash(float3(p.x, side, key + 7.3));
        float drip = 0.0;
        if (hc < _VxParams.x)
        {
            float hl = PixHash(float3(p.x, side, key + 2.1));
            drip = 1.0 + floor(hl * hl * _VxParams.y);
            // Neighbouring drips merge into wider runs now and then.
            if (PixHash(float3(floor(p.x / 2.0), side, key + 4.4)) > 0.7) drip += 1.0;
        }
        float hp = PixHash(float3(p, key + 5.0));
        if (r < lipRows + drip)
        {
            c = topC * (half)lerp(0.92, 1.05, hp);
            if (r >= lipRows && r >= lipRows + drip - 1.0) c = topC * 0.72h;   // the drip's darker last pixel
            if (r < 1.0) c = topC * 1.12h;                                    // the slab's lit edge
        }
        else
        {
            c = VxBrick(float2(x, v) * px, brickC, key + side);
            if (r < lipRows + drip + 1.0) c *= 0.6h;                          // the shadow under the lip / drip
        }
        c *= shadeMul;
    }
    return c;
}

#endif
