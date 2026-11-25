#ifndef STORYBOOK_CP_LEG_INCLUDED
#define STORYBOOK_CP_LEG_INCLUDED

// Storybook checkpoint: the leg texture (FlatFaceBlockPixel). Every leg is painted as one building of the reference, in
// pen and watercolour, running on from cube to cube (u = along the leg from _ToonLeg, so a window can span two cubes):
//   top     a tile roof - a ridge down the middle, tile courses either side (one side lit, one in shade), dark eaves,
//           hipped ends, a dormer now and then - or, on a bridge, a flagstone walk between two parapets
//   sides   a roof overhang along the top edge with a hatched shadow under it, then the walls of the leg's house theme
//           (picked per leg, neighbours differ):
//             0 stone keep     pale limestone blocks, arched windows in red-brown frames
//             1 half-timbered  plaster panels in dark timber with braces over a stone ground floor, small paned windows
//             2 plank house    warm boards, square windows with open shutters
//             3 tower          rough round-ish stone, tall slit windows, slate roof
//             4 bridge         stone arches (the teal sky seen through), voussoir rings, parapet coping
//           wide "gallery" windows sometimes span several cubes
//   bottom  the dark underside with joists
// The face colour's lit / shade relation (the look's left / right faces and the per-cube tint) shades the pigments; pen
// lines are anti-aliased with the world size of a screen pixel and wobble a little, like a hand drawing.
// Needs StorybookCommon.hlsl and the object matrices.

// Stone blocks on a 2D wall coordinate (cube units): returns the block's tone, joint = pen coverage of the joints.
half SbBlocks(float2 p, float rowH, float len, float key, float aa, float pen, out half joint)
{
    float row = floor(p.y / rowH);
    float off = PixHash(float3(row, key, 2.3)) * len;
    float x = p.x + off;
    float bx = floor(x / len);
    float inX = x - bx * len, inY = p.y - row * rowH;
    float wob = (PixNoise(float3(p * 9.0, key)) - 0.5) * 0.02;
    half jh = SbPen(inY + wob, pen * 0.75, aa);
    jh = max(jh, SbPen(inY - rowH + wob, pen * 0.75, aa));
    half jv = SbPen(inX + wob, pen * 0.75, aa) * step(0.08, inY) * step(inY, rowH - 0.04);
    // Sketchy: some joints are left out.
    half keep = step(0.3, PixNoise(float3(p * 3.1, key + 5.0)));
    joint = max(jh, jv) * keep;
    float hb = PixHash(float3(bx, row, key + 1.1));
    return (half)lerp(0.9, 1.07, hb);
}

// Paints one window (centred at x = 0, from v0 to v1, half width hw) of the theme. Returns true when the pixel is in it.
bool SbWindowAt(float x, float v, float v0, float v1, float hw, int theme, bool gallery, float aa, float pen,
                half shadeMul, inout half3 c, inout half ink)
{
    float frame = 0.028;
    bool arched = theme == 0 && !gallery;
    float vTop = arched ? v1 - hw : v1;
    float ax = abs(x);
    float dArch = arched && v > vTop ? length(float2(x, v - vTop)) - hw : max(ax - hw, max(v0 - v, v - v1));
    float dRect = arched && v > vTop ? dArch : max(ax - hw, max(v0 - v, v - vTop));
    float d = arched ? dArch : dRect;
    if (d > frame + pen * 2.0) return false;

    // Shutters (plank house): flat boards either side of the frame.
    if (theme == 2 && !gallery && d > frame)
    {
        float sx = ax - hw - frame;
        if (sx > 0.0 && sx < hw * 0.9 && v > v0 && v < v1)
        {
            c = _SbShutter.rgb * shadeMul;
            ink = max(ink, SbPen(sx - hw * 0.9, pen, aa));
            ink = max(ink, SbPen(frac(v * 18.0) / 18.0 - 0.0275, pen * 0.4, aa) * 0.6h);
            return true;
        }
    }
    if (d > frame) { ink = max(ink, SbPen(d - frame, pen, aa)); return d < frame + pen; }

    if (d > 0.0)
    {
        c = _SbShutter.rgb * shadeMul;                     // the frame
        ink = max(ink, SbPen(d, pen * 0.7, aa));
        return true;
    }
    // The glass: dark interior, a pale pane reflection, mullions.
    half3 glass = _SbWindow.rgb * shadeMul;
    glass = lerp(glass, _SbPaper.rgb * 0.55h, (half)(0.25 * step(0.0, x - v * 0.4 + 0.1) * step(x - v * 0.4, 0.05)));
    c = glass;
    float paneW = gallery ? 0.12 : hw;
    float mx = (frac(x / paneW + 0.5) - 0.5) * paneW;
    half mull = SbPen(mx, pen * 0.6, aa) * (gallery || theme == 1 ? 1.0h : 0.0h);
    mull = max(mull, SbPen(v - (v0 + v1) * 0.5, pen * 0.6, aa) * (theme == 3 ? 0.0h : 1.0h));
    c = lerp(c, _SbShutter.rgb * shadeMul, mull);
    // Window sill.
    return true;
}

half3 SbLegColor(float3 posOS, float3 nOS, float3 nWS, float3 wp, half3 col, half3 lLeft, half3 lRight, half3 tint, float4 leg)
{
    half tintMax = max(max(tint.r, tint.g), max(tint.b, 1e-3h));
    float aa = max(length(fwidth(wp)) * 0.75, 1e-4);
    float pen = max(_SbParams.x, 0.004);

    float3 origin = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
    float3 legW = normalize(float3(unity_ObjectToWorld._m02, unity_ObjectToWorld._m12, unity_ObjectToWorld._m22));
    bool hasLeg = leg.w > 0.5;
    float L = hasLeg ? max(leg.y, 1.0) : 1000.0;
    float u = hasLeg ? leg.x + posOS.z : dot(origin, legW) + posOS.z + 500.0;
    float3 lateral = origin - legW * dot(origin, legW);
    float serial = hasLeg ? floor(leg.z + 0.5) : floor(frac(dot(round(lateral * 2.0), float3(0.173, 0.311, 0.071))) * 977.0);

    // The leg's theme: a bridge now and then (long enough legs), else one of four houses, never the same as the leg before.
    float hB = PixHash(float3(serial, 3.7, 1.3));
    bool bridge = hB < _SbParams.z && L >= 4.0;
    int theme = (int)floor(PixHash(float3(serial, 8.1, 4.9)) * 4.0);
    int prevTheme = (int)floor(PixHash(float3(serial - 1.0, 8.1, 4.9)) * 4.0);
    if (theme == prevTheme) theme = (theme + 1) % 4;
    if (bridge) theme = 4;
    float key = serial * 1.37;
    half3 roofCol = theme == 3 ? _SbSlate.rgb : (PixHash(float3(serial, 1.9, 7.7)) < 0.3 ? _SbRoofAlt.rgb : _SbRoof.rgb);
    bool flatRoof = !bridge && PixHash(float3(serial, 5.5, 2.2)) > _SbParams.w;   // a few legs are battlements, not roofs

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
    half3 c;
    half ink = 0;

    bool top = nWS.y > 0.6;
    bool bottom = nWS.y < -0.6;

    if (top)
    {
        if (!level || cap)
        {
            // A climbing leg's cap: a flat stone top with an inked border.
            half j;
            half t = SbBlocks(posOS.xy * 1.0 + 0.5, 0.25, 0.33, key, aa, pen, j);
            c = _SbStone.rgb * t * tintMax;
            ink = max(j * 0.6h, SbPen(0.5 - max(abs(posOS.x), abs(posOS.y)), pen, aa));
        }
        else if (bridge || flatRoof)
        {
            // Flagstone walk between two parapets (bridge) or a battlement walk (flat roof).
            float edge = min(lat, 1.0 - lat);
            half j;
            half t = SbBlocks(float2(u, lat), 0.26, 0.42, key + 3.0, aa, pen, j);
            c = (bridge ? _SbStone.rgb : _SbStoneShade.rgb * 1.1h) * t * tintMax;
            ink = j * 0.55h;
            float par = 0.11;
            if (edge < par)
            {
                c = _SbStone.rgb * 1.06h * tintMax;
                // Merlons on the battlement: gaps every half cube.
                if (!bridge && frac(u * 2.0) > 0.55) c = _SbStoneShade.rgb * 0.8h * tintMax;
                ink = max(ink, SbPen(frac(u * 2.0) - 0.55, pen * 0.6, aa) * (bridge ? 0.0h : 1.0h));
            }
            ink = max(ink, SbPen(edge - par, pen, aa));
            ink = max(ink, SbPen(edge, pen * 1.2, aa));
        }
        else
        {
            // Tile roof: ridge down the middle, courses either side, eaves, hipped ends.
            float fromRidge = abs(lat - 0.5);                   // 0 ridge .. 0.5 eave
            float e = min(u, L - u);                            // distance to the leg's nearer end
            bool hip = e < fromRidge * 0.9 + 0.0 && e < 0.45;
            half side = lat < 0.5 ? 1.0h : 0.82h;              // one slope faces the light
            float course = 0.085;
            float cu = hip ? e : fromRidge;
            float rowF = cu / course;
            float row = floor(rowF);
            float along = hip ? (lat - 0.5) : u;
            float jx = frac(along / 0.16 + row * 0.5);
            // Scalloped tile ends: each tile's lower edge bows a little.
            float bow = (jx - 0.5) * (jx - 0.5) * 0.05;
            float inRow = frac(rowF) * course;
            half tile = lerp(0.9h, 1.08h, (half)PixHash(float3(floor(along / 0.16 + row * 0.5), row, key + 9.0)));
            c = roofCol * tile * side * tintMax;
            ink = max(ink, SbPen(inRow - bow - course * 0.5, pen * 0.55, aa) * 0.8h);
            ink = max(ink, SbPen((jx - 0.5) * 0.16, pen * 0.45, aa) * 0.55h * step(0.02, inRow));
            // Ridge cap.
            if (fromRidge < 0.045 && !hip) c = _SbRoofDark.rgb * tintMax;
            ink = max(ink, SbPen(fromRidge - 0.045, pen * 0.8, aa) * (hip ? 0.0h : 1.0h));
            // Hip lines from the ridge's ends to the corners.
            ink = max(ink, SbPen((e - fromRidge * 0.9) * 0.74, pen * 0.9, aa) * step(e, 0.5));
            // Dark eaves along both long edges.
            float edge = 0.5 - fromRidge;
            if (edge < 0.045) c = _SbRoofDark.rgb * 0.85h * tintMax;
            ink = max(ink, SbPen(edge - 0.045, pen * 0.7, aa));
            ink = max(ink, SbPen(edge, pen * 1.3, aa));
            // A dormer now and then on the lit slope (away from the leg's ends).
            float dk = floor(u / 2.5);
            float dx = u - (dk + 0.5) * 2.5;
            if (PixHash(float3(dk, key, 31.0)) < 0.45 && e > 0.8 && lat > 0.12 && lat < 0.36 && abs(dx) < 0.17)
            {
                float dy = lat - 0.24;
                bool inRoof = dy < 0.0 && abs(dx) < 0.17 + dy * 1.4;   // its little gable, toward the ridge
                c = inRoof ? roofCol * 0.85h * tintMax : (abs(dx) < 0.07 && dy > 0.02 && dy < 0.1 ? _SbWindow.rgb : _SbPlaster.rgb) * tintMax;
                ink = max(ink, SbPen(abs(dx) - 0.17, pen * 0.7, aa));
                ink = max(ink, SbPen(dy - 0.11, pen * 0.7, aa) * step(abs(dx), 0.17));
            }
        }
    }
    else if (bottom)
    {
        // The underside: dark, with joists across the leg.
        c = _SbTimber.rgb * 0.9h * tintMax;
        ink = SbPen(frac(u * 3.0) / 3.0 - 0.166, pen * 0.8, aa) * 0.6h;
    }
    else
    {
        // ---- walls ----
        float x = level ? (cap ? lat + 977.0 * sign(nOS.z) : u) : ((an.x > 0.5 ? posOS.y : posOS.x) + 0.5) + (an.x > 0.5 ? 31.0 : 57.0);
        float y = level ? v : -wp.y;
        float eave = level ? 0.86 : 99.0;
        bool climbing = !level;

        half j = 0;
        if (theme == 0 || theme == 3 || climbing)
        {
            half t = SbBlocks(float2(x, y), theme == 3 ? 0.12 : 0.15, theme == 3 ? 0.24 : 0.34, key, aa, pen, j);
            c = (theme == 3 ? lerp(_SbStone.rgb, _SbStoneShade.rgb, 0.25h) : _SbStone.rgb) * t * shadeMul;
            ink = j * 0.7h;
        }
        else if (theme == 1)
        {
            // Stone ground floor, plaster + timber upper floor.
            if (y < 0.42)
            {
                half t = SbBlocks(float2(x, y), 0.14, 0.3, key, aa, pen, j);
                c = _SbStone.rgb * 0.95h * t * shadeMul;
                ink = j * 0.6h;
            }
            else
            {
                c = _SbPlaster.rgb * shadeMul;
                float bw = 0.035;
                float panel = 0.5;
                float px = frac(x / panel) * panel;
                float pk = floor(x / panel);
                half beam = step(abs(y - 0.44), bw) + step(abs(px - 0.0), bw * 0.8) + step(abs(px - panel), bw * 0.8);
                // Braces: one diagonal per panel, alternating; some panels an X.
                float ty = saturate((y - 0.46) / max(eave - 0.46, 0.01));
                float dA = abs(px / panel - ty) * panel, dB = abs(px / panel - (1.0 - ty)) * panel;
                float hp = PixHash(float3(pk, key, 4.4));
                if (hp < 0.4) beam += step(dA, bw * 0.7);
                else if (hp < 0.7) beam += step(dB, bw * 0.7);
                else if (hp < 0.85) beam += step(min(dA, dB), bw * 0.7);
                c = lerp(c, _SbTimber.rgb * shadeMul, saturate(beam));
                ink = max(ink, SbPen(abs(y - 0.44) - bw, pen * 0.6, aa));
            }
        }
        else if (theme == 2)
        {
            // Boards.
            float bh = 0.085;
            float br = floor(y / bh);
            half t = lerp(0.88h, 1.08h, (half)PixHash(float3(br, floor(x / 1.3 + br * 0.37), key)));
            c = _SbWood.rgb * t * shadeMul;
            ink = SbPen(frac(y / bh) * bh - bh * 0.5, pen * 0.45, aa) * 0.6h;
        }
        else
        {
            // Bridge: stone with arches.
            half t = SbBlocks(float2(x, y), 0.15, 0.36, key, aa, pen, j);
            c = _SbStone.rgb * t * shadeMul;
            ink = j * 0.6h;
            if (level && !cap)
            {
                float nSpan = max(1.0, round(L / 3.0));
                float S = L / nSpan;
                float ax = (frac(u / S) - 0.5) * S;
                float rx = S * 0.36, ry = 0.58;
                float r = length(float2(ax / rx, (y + 0.02) / ry));
                if (r < 1.0)
                {
                    // Under the arch: the sky beyond, darker toward the soffit.
                    c = lerp(_SbSky.rgb, _SbSky.rgb * 0.55h, (half)smoothstep(0.55, 1.0, r)) * tintMax;
                    ink = SbHatch(float2(x, y), _SbParams.y * 0.7, aa, pen * 0.35) * (half)smoothstep(0.75, 1.0, r) * 0.6h;
                }
                else if (r < 1.2)
                {
                    // Voussoirs: a ring of wedge stones.
                    float ang = atan2(y + 0.02, ax);
                    float wedge = frac(ang / 0.2);
                    c = _SbStone.rgb * 1.05h * lerp(0.92h, 1.06h, (half)PixHash(float3(floor(ang / 0.2), key, 6.0))) * shadeMul;
                    ink = SbPen((wedge - 0.5) * 0.2 * r * 0.5, pen * 0.5, aa) * 0.7h;
                }
                ink = max(ink, SbPen((r - 1.0) * ry, pen * 1.1, aa));
                ink = max(ink, SbPen((r - 1.2) * ry, pen * 0.6, aa) * 0.7h);
            }
        }

        // Windows (not on bridges, kept off the leg's ends).
        if (theme < 4 && y < eave)
        {
            float P = theme == 1 ? 0.5 : (PixHash(float3(serial, 2.2, 0.4)) < 0.5 ? 1.0 : 1.5);
            float wk = floor(x / P);
            float wx = x - (wk + 0.5) * P;
            float hw2 = PixHash(float3(wk, key, 12.0));
            bool gallery = hw2 < 0.22 && !climbing;
            float gk = floor(x / (P * 3.0));
            if (gallery) { P *= 3.0; wx = x - (gk + 0.5) * P; }
            float hw = gallery ? P * 0.4 : (theme == 3 ? 0.045 : theme == 0 ? 0.1 : 0.12);
            float v0 = theme == 1 ? 0.52 : theme == 3 ? 0.28 : 0.3;
            float v1 = theme == 1 ? 0.76 : theme == 3 ? 0.74 : (theme == 0 ? 0.7 : 0.66);
            if (climbing) { v0 = frac(y) * 0.0 + 0.2; v1 = 0.75; }
            float yy = climbing ? frac(y) : y;
            bool skip = PixHash(float3(gallery ? gk : wk, key, 17.0)) < (gallery ? 0.0 : 0.3);
            bool nearEnd = level && !cap && (x - wx - hw < 0.3 || x - wx + hw > L - 0.3);
            if (cap) { wx = x - 977.0 * sign(nOS.z) - 0.5; skip = PixHash(float3(serial, 44.0, sign(nOS.z))) < 0.4; nearEnd = false; }
            if (!skip && !nearEnd)
                SbWindowAt(wx, yy, v0, v1, hw, theme, gallery, aa, pen, shadeMul, c, ink);
        }

        if (level)
        {
            // Quoins at the leg's ends, the ends inked.
            if (!cap && (u < 0.14 || u > L - 0.14) && theme != 2)
            {
                float q = floor(y / 0.15);
                if (fmod(q, 2.0) < 1.0) c = _SbStone.rgb * 1.08h * shadeMul;
            }
            if (!cap) { ink = max(ink, SbPen(u, pen * 1.4, aa)); ink = max(ink, SbPen(u - L, pen * 1.4, aa)); }
            ink = max(ink, SbPen(y, pen * 1.2, aa));

            // Shadow under the eave (hatched), then the overhang.
            if (y < eave && y > eave - 0.14)
            {
                half sh = (half)smoothstep(eave - 0.14, eave, y);
                c *= 1.0h - 0.22h * sh;
                ink = max(ink, SbHatch(float2(x, y), _SbParams.y, aa, pen * 0.35) * sh * 0.7h);
            }
            if (y >= eave)
            {
                if (bridge || flatRoof)
                {
                    c = _SbStone.rgb * 1.08h * shadeMul;          // parapet coping
                    if (flatRoof && frac(x * 2.0) > 0.55) c = _SbStoneShade.rgb * 0.75h * shadeMul;
                }
                else
                {
                    c = roofCol * 0.9h * shadeMul;
                    float jx = frac(x / 0.16);
                    ink = max(ink, SbPen((jx - 0.5) * 0.16, pen * 0.45, aa) * 0.6h);
                }
            }
            // The eave's scalloped edge.
            float scal = bridge || flatRoof ? 0.0 : (frac(x / 0.16) - 0.5) * (frac(x / 0.16) - 0.5) * 0.06;
            ink = max(ink, SbPen(y - eave + scal, pen * 1.1, aa));
        }

        // Pen hatching on the shade side.
        half dark = saturate((0.92h - k) * 3.0h);
        if (dark > 0.01h)
            ink = max(ink, SbHatch(float2(x, y), _SbParams.y, aa, pen * 0.32) * dark * _SbStone.a *
                           (half)step(0.35, PixNoise(float3(x * 2.0, y * 2.0, key))));
    }

    c *= SbWash(wp + key);
    half3 inkCol = lerp(_SbInk.rgb, c * 0.35h, 0.2h);
    return lerp(c, inkCol, saturate(ink) * _SbInk.a);
}

#endif
