#ifndef PIXEL_CP_LEG_INCLUDED
#define PIXEL_CP_LEG_INCLUDED

// Pixel checkpoint: the leg texture (FlatFaceBlockPixel). A Minecraft-like texel grid (_PixParams.x texels per cube) laid
// over every leg so the texels run on from cube to cube:
//   top     glowing jade moss, texel by texel (dark / mid / light / a few glowing), a bright rim along the long edges
//   sides   the moss drips over the top edge - every texel column hangs its own length, the long ones end in a glowing
//           bead - over coral stone bricks (offset rows, dark mortar, a lit top row per brick, per-brick and per-texel
//           variation)
//   bottom  coral cobbles in dark mortar
// Climbing legs are bricks all round with moss on the cap that faces up. The face colours (the look's top / left / right /
// bottom, with the per-cube tint) give the hue and the lit / shade side; this only paints the pattern into them.
// Needs PixelCommon.hlsl and the object matrices.

half3 PixLegColor(float3 posOS, float3 nOS, float3 nWS, float3 wp, half3 col, half3 lLeft, half3 lRight, half3 tint)
{
    float T = max(_PixParams.x, 2.0);
    half tintMax = max(max(tint.r, tint.g), max(tint.b, 1e-3h));
    float3 origin = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
    float3 legW = normalize(float3(unity_ObjectToWorld._m02, unity_ObjectToWorld._m12, unity_ObjectToWorld._m22));
    float cubeAlong = round(dot(origin, legW));
    float3 key = round(origin * 2.0);
    float legKey = key.x * 0.173 + key.y * 0.311 + key.z * 0.071;
    float3 an = abs(nOS);
    bool cap = an.z > 0.5;

    float3 upOS = normalize(mul((float3x3)unity_WorldToObject, float3(0, 1, 0)));
    bool level = abs(upOS.z) < 0.5;                                    // a flat leg (else it climbs / drops)
    float3 latOS = level ? normalize(cross(upOS, float3(0, 0, 1))) : float3(1, 0, 0);
    float h = dot(posOS, upOS);                                       // -0.5 bottom .. 0.5 top across the section
    float u = (posOS.z + 0.5) * T + cubeAlong * T;                    // along the whole leg
    float lat = (dot(posOS, latOS) + 0.5) * T;                        // across the top / bottom / caps

    half sideLight = saturate(PixLum(col) / max(max(PixLum(lLeft), PixLum(lRight)) * tintMax, 1e-3h));
    half k = lerp(0.62h, 1.0h, sideLight);                           // the shade side's moss and mortar sit darker

    bool top = nWS.y > 0.6;
    bool bottom = nWS.y < -0.6;

    // ---- top: moss ----
    if (top)
    {
        float2 tex = level && !cap ? float2(floor(u), floor(lat)) : float2(floor((posOS.x + 0.5) * T), floor((posOS.y + 0.5) * T));
        float hm = PixHash(float3(tex, legKey + 1.7));
        half3 m = hm < 0.14 ? _PixMossDark.rgb : hm < 0.62 ? _PixMoss.rgb : hm < 1.0 - _PixMossGlow.a ? _PixMossLight.rgb : _PixMossGlow.rgb;
        // A bright rim where the top meets the drips.
        if (level && !cap && (tex.y < 0.5 || tex.y > T - 1.5)) m = hm < 0.5 ? _PixMossLight.rgb : _PixMossGlow.rgb;
        return lerp(col, m * tintMax, _PixMoss.a);
    }

    // ---- bottom: cobbles ----
    if (bottom)
    {
        float2 tex = level && !cap ? float2(floor(u), floor(lat)) : float2(floor((posOS.x + 0.5) * T), floor((posOS.y + 0.5) * T));
        float rowShift = floor(PixHash(float3(floor(tex.y / 3.0), legKey, 5.5)) * 3.0);
        float2 cell = float2(floor((tex.x + rowShift) / 3.0), floor(tex.y / 3.0));
        float2 inCell = float2(tex.x + rowShift - cell.x * 3.0, tex.y - cell.y * 3.0);
        half mortar = step(1.5, max(inCell.x, inCell.y));
        half stone = lerp(0.8h, 1.1h, PixHash(float3(cell, legKey + 9.0))) * lerp(0.94h, 1.06h, PixHash(float3(tex, 2.0)));
        return lerp(col * stone, _PixMortar.rgb * k * tintMax * 0.8h, mortar);
    }

    // ---- sides (and the end caps): moss drips over stone bricks ----
    float column, row;
    if (level)
    {
        column = cap ? floor(lat) + 977.0 * sign(nOS.z) : floor(u);
        row = floor((0.5 - h) * T);                                   // 0 = the texel row under the top edge
    }
    else
    {
        column = floor(((an.x > 0.5 ? posOS.y : posOS.x) + 0.5) * T) + (an.x > 0.5 ? 31.0 : 57.0) * sign(dot(nOS, float3(1, 1, 0)));
        row = floor(-wp.y * T);
    }

    // Bricks: rows of 3 texels (the last one mortar), 6 texels long (the last one mortar), every other row offset by 3.
    float brickRow = floor(row / 3.0);
    float inY = row - brickRow * 3.0;
    float offset = fmod(abs(brickRow), 2.0) * 3.0;
    float bx = floor((column + offset) / 6.0);
    float inX = column + offset - bx * 6.0;
    half mortar = max(step(1.5, inY), step(4.5, inX));
    float hb = PixHash(float3(bx, brickRow, legKey + 3.3));
    half shade = lerp(0.9h, 1.06h, hb);
    if (hb > 0.92) shade = 0.72h;                                     // a darker, older brick here and there
    shade *= inY < 0.5 ? 1.12h : 0.96h;                               // the lit top row of each brick
    shade *= lerp(0.97h, 1.03h, PixHash(float3(column, row, legKey + 7.1)));
    half3 c = lerp(col * shade, _PixMortar.rgb * k * tintMax, mortar);

    // Drips (level legs only: they hang from the top edge).
    if (level && row >= 0.0)
    {
        float r1 = PixHash(float3(column, legKey, 11.3));
        float r2 = PixHash(float3(column, legKey, 23.9));
        float len = 1.0 + floor(r1 * 2.0);
        if (r2 < 0.32) len += 1.0 + floor(PixHash(float3(column, legKey, 37.1)) * max(_PixMossDark.a - 3.0, 1.0));
        if (row < len)
        {
            half3 m = row < 0.5 ? _PixMoss.rgb : _PixMossDark.rgb;
            if (len >= 3.0 && row > len - 1.5) m = _PixMossGlow.rgb;  // the bead at the end of a long drip
            else if (row < 0.5 && r1 > 0.6) m = _PixMossLight.rgb;
            c = lerp(c, m * k * tintMax, _PixMoss.a);
        }
    }
    return c;
}

#endif
