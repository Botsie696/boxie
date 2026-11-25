#ifndef SPICE_CP_LEG_INCLUDED
#define SPICE_CP_LEG_INCLUDED

// Spice Route checkpoint: the leg texture (FlatFaceBlockPixel). Every leg is a painted strip of craft; the first half of
// the level is India, the second the Arabian / Ottoman world (SrHalfOf(serial)). Six designs per half, picked so that
// neighbouring legs never repeat (serial x 5 mod 6), each in one of three palettes (changes every 6 legs) - no two legs
// of a half look alike. Every leg keeps the same value structure so the path always reads against the sky: a light top
// (the deck) framed by a gold zari trim, saturated sides, a dark underside.
//
// India                                              Arabia
//   0 temple stone - carved lotus slabs; niches        0 girih - 8-point star strapwork; pointed arcade with lanterns
//     with Ganesha and diya flames on the sides        1 Iznik - white tiles with tulips, carnations, saz leaves; chintamani
//   1 rangoli - dotted petal mandalas; lotus vine      2 mashrabiya - turned-wood lattice over amber light; screens
//   2 mirror work - shisha discs on rani pink;         3 carpet - medallions, rosettes, zig-zag borders; trefoil border
//     bandhani dots                                    4 zellige - star-and-cross tiles; horseshoe arches, muqarnas
//   3 Madhubani - peacocks and paisleys; gold          5 ebru - Turkish marbled paper; gold rosettes
//     elephants walking along the sides
//   4 palace marble - pietra dura flowers; cusped arches with jaali
//   5 zari - gold paisleys on peacock blue; scallops
// Needs SpiceCommon.hlsl and the object matrices. leg = (along start, length, serial, hasLeg) as FlowerLeg.hlsl.

half3 SrHex(float r, float g, float b) { return SrLin(half3(r, g, b) / 255.0h); }

// Palettes: ground (top), ink, c1, c2, c3, side, sideDark.
void SrIndia(int p, out half3 g, out half3 ink, out half3 c1, out half3 c2, out half3 c3, out half3 sd, out half3 sdk)
{
    if (p == 0)      { g = SrHex(244, 228, 198); ink = SrHex(110, 26, 42); c1 = SrHex(224, 69, 123); c2 = SrHex(242, 154, 46); c3 = SrHex(30, 140, 138); sd = SrHex(222, 72, 122); sdk = SrHex(120, 26, 70); }
    else if (p == 1) { g = SrHex(247, 227, 181); ink = SrHex(58, 26, 16); c1 = SrHex(216, 52, 42); c2 = SrHex(31, 110, 156); c3 = SrHex(46, 139, 87); sd = SrHex(240, 150, 40); sdk = SrHex(140, 64, 16); }
    else             { g = SrHex(243, 230, 208); ink = SrHex(26, 26, 64); c1 = SrHex(52, 70, 150); c2 = SrHex(247, 183, 51); c3 = SrHex(216, 52, 42); sd = SrHex(46, 80, 160); sdk = SrHex(20, 30, 80); }
}

void SrArabia(int p, out half3 g, out half3 ink, out half3 c1, out half3 c2, out half3 c3, out half3 sd, out half3 sdk)
{
    if (p == 0)      { g = SrHex(243, 239, 230); ink = SrHex(14, 30, 78); c1 = SrHex(28, 63, 158); c2 = SrHex(39, 163, 181); c3 = SrHex(200, 50, 44); sd = SrHex(30, 66, 160); sdk = SrHex(12, 26, 80); }
    else if (p == 1) { g = SrHex(239, 230, 210); ink = SrHex(16, 32, 24); c1 = SrHex(31, 122, 90); c2 = SrHex(216, 168, 72); c3 = SrHex(28, 63, 158); sd = SrHex(30, 120, 92); sdk = SrHex(12, 56, 40); }
    else             { g = SrHex(233, 220, 192); ink = SrHex(26, 20, 48); c1 = SrHex(42, 46, 110); c2 = SrHex(200, 50, 44); c3 = SrHex(216, 168, 72); sd = SrHex(64, 50, 130); sdk = SrHex(26, 20, 64); }
}

half3 SrLegColor(float3 posOS, float3 nOS, float3 nWS, float3 wp, half3 col, half3 lLeft, half3 lRight, half3 tint, float4 leg)
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

    bool arab = SrHalfOf(serial) > 0.5;
    int kind = (int)fmod(serial * 5.0, 6.0);
    int pal = (int)fmod(floor(serial / 6.0) + (arab ? 1.0 : 0.0), 3.0);
    half3 g, ink, c1, c2, c3, sd, sdk;
    if (arab) SrArabia(pal, g, ink, c1, c2, c3, sd, sdk); else SrIndia(pal, g, ink, c1, c2, c3, sd, sdk);
    half3 white = SrHex(246, 240, 228);

    float3 an = abs(nOS);
    bool cap = an.z > 0.5;
    float3 upOS = normalize(mul((float3x3)unity_WorldToObject, float3(0, 1, 0)));
    bool level = abs(upOS.z) < 0.5;
    float3 latOS = level ? normalize(cross(upOS, float3(0, 0, 1))) : float3(1, 0, 0);
    float v = dot(posOS, upOS) + 0.5;
    float lat = dot(posOS, latOS) + 0.5;

    half lumMax = max(max(PixLum(lLeft), PixLum(lRight)) * tintMax, 1e-3h);
    half k = saturate(PixLum(col) / lumMax);
    half shadeMul = lerp(0.62h, 1.0h, k) * tintMax;

    bool top = nWS.y > 0.6;
    bool bottom = nWS.y < -0.6;
    half3 c;

    if (top)
    {
        float2 p = (level && !cap) ? float2(u, lat) : posOS.xy + 0.5;
        float edge = (level && !cap) ? min(lat, 1.0 - lat) : 0.5 - max(abs(posOS.x), abs(posOS.y));
        float2 q = float2(frac(p.x) - 0.5, p.y - 0.5);            // one cube's cell, centred
        float cellI = floor(p.x);
        c = g;

        if (!arab)
        {
            if (kind == 0)
            {
                // Temple stone: pink sandstone slabs, a carved lotus medallion on each, a bead line along the trim.
                half3 stone = lerp(SrHex(226, 160, 120), g, 0.35h);
                c = stone * (0.92h + 0.12h * (half)SrNoise2(p * float2(3.0, 5.0) + key));
                c *= 1.0h - SrLine(min(frac(p.x), 1.0 - frac(p.x)), 0.012, aa) * 0.35h;
                float lotus = SrPetals(q, 8.0, 0.3, 0.45, 0.0);
                c = lerp(c, lerp(c, c1, 0.45h), SrFill(lotus, aa));
                c = lerp(c, ink * 0.8h, SrLine(lotus, 0.012, aa) * 0.7h);
                float inner = SrPetals(q, 8.0, 0.16, 0.5, 0.39);
                c = lerp(c, c2, SrFill(inner, aa) * 0.7h);
                c = lerp(c, SrGold(p, 0.9h), SrFill(length(q) - 0.05, aa));
                float corner = length(abs(q) - float2(0.5, 0.36)) - 0.05;
                c = lerp(c, c3, SrFill(corner, aa) * 0.8h);
            }
            else if (kind == 1)
            {
                // Rangoli: an outer ring of dots, two layers of petals, a centre, small diamonds between the cubes.
                float r = length(q);
                float ang = atan2(q.y, q.x);
                float dots = length(float2(r - 0.37, (frac(ang / 6.2832 * 12.0) - 0.5) * 6.2832 * 0.37 / 12.0)) - 0.03;
                c = lerp(c, c1, SrFill(dots, aa));
                float pet = SrPetals(q, 8.0, 0.31, 0.55, 0.0);
                c = lerp(c, c1, SrFill(pet, aa));
                c = lerp(c, ink, SrLine(pet, 0.01, aa) * 0.8h);
                float pet2 = SrPetals(q, 8.0, 0.2, 0.6, 0.39);
                c = lerp(c, c2, SrFill(pet2, aa));
                c = lerp(c, c3, SrFill(r - 0.075, aa));
                c = lerp(c, white, SrFill(r - 0.03, aa));
                float dia = abs(frac(p.x) < 0.5 ? frac(p.x) : frac(p.x) - 1.0) + abs(q.y) * 0.7 - 0.07;
                c = lerp(c, c3, SrFill(dia, aa));
            }
            else if (kind == 2)
            {
                // Mirror work: rani pink cloth, mirror discs in a staggered grid with stitched rings, diamonds between.
                c = lerp(c1, g, 0.12h) * (0.94h + 0.1h * (half)SrNoise2(p * 8.0));
                float2 mp = float2(p.x * 2.0, p.y * 2.0 + 0.5);
                float row = floor(mp.y);
                float2 mq = float2(frac(mp.x + fmod(row, 2.0) * 0.5) - 0.5, frac(mp.y) - 0.5);
                float md = length(mq);
                half shine = (half)PixHash(float3(floor(mp.x + fmod(row, 2.0) * 0.5), row, key));
                half3 mirror = lerp(SrHex(150, 160, 175), half3(1.0h, 1.0h, 1.0h), saturate(shine * 0.8h + (half)(0.5 - mq.y) * 0.4h));
                c = lerp(c, c2, SrLine(md - 0.27, 0.035, aa) * (half)step(0.5, frac(atan2(mq.y, mq.x) * 4.0)));
                c = lerp(c, ink, SrLine(md - 0.21, 0.012, aa));
                c = lerp(c, mirror, SrFill(md - 0.18, aa));
                float2 dq = float2(frac(mp.x + fmod(row, 2.0) * 0.5) , frac(mp.y)) - float2(0.0, 0.0);
                float dd = abs(dq.x - (dq.x > 0.5 ? 1.0 : 0.0)) + abs(dq.y - (dq.y > 0.5 ? 1.0 : 0.0)) - 0.1;
                c = lerp(c, c3, SrFill(dd, aa));
            }
            else if (kind == 3)
            {
                // Madhubani: cream paper, a double ink border, peacocks and paisleys in alternate cubes.
                c = g;
                c = lerp(c, ink, saturate(SrLine(edge - 0.13, 0.01, aa) + SrLine(edge - 0.17, 0.006, aa)));
                if (fmod(cellI, 2.0) < 0.5)
                {
                    float pk = SrPeacock(q * float2(1.0, 1.0) + float2(0.08, 0.22), 0.42);
                    c = lerp(c, c2, SrFill(pk, aa));
                    c = lerp(c, ink, SrLine(pk, 0.012, aa));
                    // Eyes on the train.
                    float2 tq = q + float2(0.08, 0.22) - float2(-0.22, 0.12);
                    float eyes = length(float2(frac(tq.x * 8.0) - 0.5, tq.y * 8.0)) / 8.0 - 0.025;
                    c = lerp(c, c3, SrFill(eyes, aa) * SrFill(pk, aa));
                }
                else
                {
                    float pz = SrPaisley(SrRot(q, 2.4), 0.16);
                    c = lerp(c, c1, SrFill(pz, aa));
                    c = lerp(c, ink, SrLine(pz, 0.012, aa));
                    float pz2 = SrPaisley(SrRot(q, 2.4) * 1.8, 0.16) / 1.8;
                    c = lerp(c, c2, SrFill(pz2, aa));
                }
                // Hatching in the border band.
                half hatch = (half)step(0.5, frac((p.x + p.y) * 14.0)) * (half)step(edge, 0.13) * (half)step(0.09, edge);
                c = lerp(c, c1, hatch * 0.7h);
            }
            else if (kind == 4)
            {
                // Palace marble: veined white, an inlaid flower on each slab, a faint green trellis.
                c = lerp(white, g, 0.25h);
                half vein = SrLine(sin(p.x * 3.0 + SrFbm(p * 2.0 + key) * 6.0) * 0.1, 0.004, aa) * 0.25h;
                c *= 1.0h - vein;
                float tr = min(abs(frac((p.x + p.y) * 2.0) - 0.5), abs(frac((p.x - p.y) * 2.0) - 0.5)) / 2.0;
                c = lerp(c, c3, SrLine(tr, 0.006, aa) * 0.6h);
                float fl = SrPetals(q, 4.0, 0.2, 0.7, 0.785);
                float leaves = SrPetals(q, 4.0, 0.3, 0.85, 0.0);
                c = lerp(c, c3, SrFill(leaves, aa));
                c = lerp(c, c1, SrFill(fl, aa));
                c = lerp(c, SrGold(p, 0.8h), SrLine(fl, 0.01, aa));
                c = lerp(c, c2, SrFill(length(q) - 0.05, aa));
            }
            else
            {
                // Zari: gold paisleys and gold dots on peacock blue.
                c = lerp(SrHex(24, 92, 140), c1, 0.25h) * (0.92h + 0.12h * (half)SrNoise2(p * 6.0));
                float2 pq = float2(frac(p.x * 1.5) - 0.5, p.y - 0.5);
                float flip = fmod(floor(p.x * 1.5), 2.0) < 0.5 ? 1.0 : -1.0;
                float pz = SrPaisley(SrRot(pq * float2(flip, 1.0), 0.6), 0.13);
                c = lerp(c, SrGold(p, 0.85h), SrFill(pz, aa));
                c = lerp(c, c2, SrFill(SrPaisley(SrRot(pq * float2(flip, 1.0), 0.6) * 2.2, 0.13) / 2.2, aa));
                float2 dq = frac(p * float2(6.0, 6.0)) - 0.5;
                half dotOn = (half)step(0.7, PixHash(float3(floor(p * 6.0), key)));
                c = lerp(c, SrGold(p, 1.0h), SrFill(length(dq) / 6.0 - 0.012, aa) * dotOn * (1.0h - SrFill(pz - 0.04, aa)));
            }
        }
        else
        {
            if (kind == 0)
            {
                // Girih: 8-point stars with white strapwork, lines running from the star tips to the next star.
                c = c1;
                float s = SrStar8(q, 0.3);
                c = lerp(c, c2, SrFill(s, aa));
                float straps = min(min(abs(q.x + q.y), abs(q.x - q.y)) * 0.7071, 1e3);
                half strapOn = (half)step(0.0, s);
                c = lerp(c, white, SrLine(s, 0.022, aa));
                c = lerp(c, white, SrLine(straps, 0.016, aa) * strapOn);
                c = lerp(c, ink, SrLine(s - 0.045, 0.006, aa));
                float s2 = SrStar8(SrRot(q, 0.3927), 0.12);
                c = lerp(c, fmod(cellI, 2.0) < 0.5 ? c3 : SrGold(p, 0.9h), SrFill(s2, aa));
            }
            else if (kind == 1)
            {
                // Iznik: white half-unit tiles with cobalt joints; tulip / carnation / saz leaf per tile.
                float2 tp = p * 2.0;
                float2 tq = frac(tp) - 0.5;
                float th = PixHash(float3(floor(tp), key));
                c = white * (0.95h + 0.06h * (half)SrNoise2(p * 9.0));
                if (th < 0.4)
                {
                    float stem = SrSeg(tq, float2(0.0, -0.4), float2(0.0, 0.0)) - 0.02;
                    float pc = SrEllipse(tq - float2(0.0, 0.12), float2(0.09, 0.17));
                    float pl = SrEllipse(SrRot(tq - float2(-0.1, 0.08), -0.5), float2(0.06, 0.15));
                    float pr = SrEllipse(SrRot(tq - float2(0.1, 0.08), 0.5), float2(0.06, 0.15));
                    c = lerp(c, c3 * 0.85h + c1 * 0.15h, SrFill(stem, aa));
                    c = lerp(c, c3, SrFill(min(pc, min(pl, pr)), aa));
                    c = lerp(c, c1, SrLine(min(pc, min(pl, pr)), 0.012, aa));
                }
                else if (th < 0.75)
                {
                    float fan = max(length(tq + float2(0.0, 0.15)) - 0.3, -(tq.y + 0.15));
                    float teeth = SrLine(length(tq + float2(0.0, 0.15)) - 0.3, 0.03, aa) * (half)step(0.5, frac(atan2(tq.y + 0.15, tq.x) * 3.0));
                    c = lerp(c, c1, SrFill(fan, aa));
                    c = lerp(c, c2, SrFill(max(length(tq + float2(0.0, 0.15)) - 0.18, -(tq.y + 0.15)), aa));
                    c = lerp(c, c3, teeth);
                }
                else
                {
                    float leaf = SrEllipse(SrRot(tq, 0.7), float2(0.38, 0.1));
                    c = lerp(c, c2, SrFill(leaf, aa));
                    c = lerp(c, c1, SrLine(SrRot(tq, 0.7).y, 0.008, aa) * SrFill(leaf, aa));
                    c = lerp(c, c1, SrLine(leaf, 0.012, aa));
                }
                c = lerp(c, c1, SrLine(0.5 - max(abs(tq.x), abs(tq.y)), 0.014, aa));
            }
            else if (kind == 2)
            {
                // Mashrabiya: turned-wood lattice with beads over warm amber light, a star frame in each cube.
                half3 wood = SrHex(92, 52, 30), woodLit = SrHex(150, 96, 56);
                half3 light = lerp(SrHex(255, 170, 70), SrHex(255, 220, 140), (half)SrNoise2(p * 1.5 + key)) * (0.85h + 0.3h * (half)_SrParams.y);
                c = light;
                float2 lq = frac(p * 6.0) - 0.5;
                float bar = min(abs(lq.x), abs(lq.y)) / 6.0;
                float bead = length(lq) / 6.0 - 0.028;
                half w = max(SrLine(bar, 0.012, aa), SrFill(bead, aa));
                half3 wc = lerp(wood, woodLit, (half)saturate(0.5 - lq.y));
                c = lerp(c, wc, w);
                float s = SrStar8(q, 0.36);
                c = lerp(c, wood * 0.8h, SrLine(s, 0.025, aa));
                c = lerp(c, SrGold(p, 0.8h), SrFill(length(q) - 0.045, aa));
            }
            else if (kind == 3)
            {
                // Carpet: a navy field, a lozenge medallion every two cubes, gold rosettes, a zig-zag border band.
                c = c1 * (0.92h + 0.1h * (half)SrNoise2(p * 10.0));
                float2 mq = float2((frac(p.x * 0.5) - 0.5) * 2.0, p.y - 0.5);
                float loz = abs(mq.x) / 0.7 + abs(mq.y) / 0.3 - 1.0;
                c = lerp(c, white, SrFill(loz * 0.25, aa));
                c = lerp(c, c2, SrLine(loz * 0.25, 0.015, aa));
                float loz2 = abs(mq.x) / 0.42 + abs(mq.y) / 0.18 - 1.0;
                c = lerp(c, c3, SrFill(loz2 * 0.15, aa));
                c = lerp(c, c2, SrFill(SrStar8(mq, 0.07), aa));
                float2 rq = frac(p * float2(3.0, 3.0)) - 0.5;
                float ros = min(length(rq - float2(0.1, 0.0)), min(length(rq + float2(0.1, 0.0)), min(length(rq - float2(0.0, 0.1)), length(rq + float2(0.0, 0.1))))) / 3.0 - 0.012;
                c = lerp(c, SrGold(p, 0.8h), SrFill(ros, aa) * (half)step(0.0, loz));
                // Border band with a running zig-zag.
                half band = (half)(smoothstep(0.07 - aa, 0.07 + aa, edge) * (1.0 - smoothstep(0.19 - aa, 0.19 + aa, edge)));
                float zz = abs(frac(p.x * 4.0) - 0.5) * 0.12 + 0.09;
                half3 bc = lerp(white, c2, (half)step(edge, zz));
                c = lerp(c, bc, band);
            }
            else if (kind == 4)
            {
                // Zellige: star-and-cross tiling - stars in the cells, crosses at the corners, dark grout.
                float2 zp = p * 2.0;
                float2 zq = frac(zp) - 0.5;
                float star = SrStar8(zq, 0.4);
                float2 cq = frac(zp + 0.5) - 0.5;
                float cross = min(SrBox(cq, float2(0.2, 0.07)), SrBox(cq, float2(0.07, 0.2)));
                float chk = fmod(floor(zp.x) + floor(zp.y), 2.0);
                c = white;
                c = lerp(c, chk < 0.5 ? c1 : c2, SrFill(star, aa));
                c = lerp(c, c3, SrFill(cross, aa) * (1.0h - SrFill(star, aa)));
                c = lerp(c, ink, max(SrLine(star, 0.012, aa), SrLine(cross, 0.01, aa)) * 0.85h);
            }
            else
            {
                // Ebru: Turkish marbled paper - stripes combed into waves, gold veins.
                float x = p.x * 1.6 + 0.22 * sin(p.y * 12.0 + p.x * 0.5) + (SrFbm(p * float2(1.2, 2.5) + key) - 0.5) * 1.4;
                float bands = x * 2.2;
                float bi = floor(bands);
                float bf = frac(bands);
                int ci = (int)fmod(bi, 4.0);
                half3 bc = ci == 0 ? c1 : ci == 1 ? white : ci == 2 ? c2 : lerp(c1, ink, 0.4h);
                c = bc;
                c = lerp(c, SrGold(p, 0.9h), SrLine(bf - 0.03, 0.025, aa * 2.2) * 0.9h);
                // Stone-drops (nonpareil spots) on some bands.
                float2 sp = frac(p * float2(2.5, 3.0) + float2(0.0, bi * 0.37)) - 0.5;
                half drop = SrFill(length(sp) / 2.7 - 0.04, aa) * (half)step(0.55, PixHash(float3(floor(p * float2(2.5, 3.0)), key)));
                c = lerp(c, lerp(white, c2, 0.4h), drop * 0.8h);
            }
        }

        // The zari trim along both edges: a gold band with dark dots, an ink line inside it.
        half trim = (half)(1.0 - smoothstep(0.055 - aa, 0.055 + aa, edge));
        half3 zari = SrGold(p * 1.3, 0.85h);
        float2 zd = float2(frac(p.x * 6.0) - 0.5, (edge - 0.028) * 6.0);
        zari = lerp(zari, arab ? c1 : ink, SrFill(length(zd) / 6.0 - 0.011, aa) * 0.7h);
        c = lerp(c, zari, trim);
        c = lerp(c, ink, SrLine(edge - 0.062, 0.006, aa) * 0.8h);
        c *= 1.0h + SrGrain(p * 6.0 + key) * (half)_SrParams.z;
        c *= tintMax;
    }
    else if (bottom)
    {
        float2 p = level ? float2(u, lat) : posOS.xy + 0.5;
        c = sdk * (0.85h + 0.2h * (half)SrNoise2(p * 4.0));
        float2 dq = frac(p * 3.0) - 0.5;
        float m = arab ? SrStar8(dq, 0.18) : length(dq) - 0.12;
        c = lerp(c, arab ? lerp(white, c2, 0.3h) : SrGold(p, 0.7h), SrFill(m / 3.0, aa) * 0.8h);
        c *= tintMax;
    }
    else
    {
        // ---- the walls ----
        float side = level ? (dot(nOS, latOS) > 0.0 ? 1.0 : 0.0) : 0.0;
        float x = level ? (cap ? lat + 977.0 * sign(nOS.z) : u) : ((an.x > 0.5 ? posOS.y : posOS.x) + 0.5) + (an.x > 0.5 ? 31.0 : 57.0);
        float y = level ? v : frac(-wp.y);
        float2 p = float2(x, y);
        float2 q = float2(frac(x) - 0.5, y);
        float cellI = floor(x);
        half3 base = lerp(sdk, sd, (half)(0.75 + 0.25 * SrNoise2(p * float2(2.0, 4.0) + key)));
        c = base;

        if (!arab)
        {
            if (kind == 0)
            {
                // Temple niches: cusped arches with a Ganesha (even) or a diya flame (odd), pilasters, mouldings.
                c = lerp(SrHex(214, 140, 104), sd, 0.3h) * (0.9h + 0.15h * (half)SrNoise2(p * 5.0 + key));
                float arch = SrCuspArch(q - float2(0.0, 0.16), 0.27, 0.36);
                half inArch = SrFill(arch, aa);
                half3 niche = lerp(ink * 0.5h, SrHex(255, 160, 70) * 0.6h, (half)saturate(0.5 - y) * 0.9h);
                c = lerp(c, SrGold(p, 0.75h), SrLine(arch + 0.03, 0.022, aa));
                c = lerp(c, niche, inArch);
                if (fmod(cellI, 2.0) < 0.5)
                {
                    float gan = SrGanesha(q - float2(0.0, 0.47), 0.17);
                    c = lerp(c, SrGold(p * 2.0, 0.95h), SrFill(gan, aa) * inArch);
                }
                else
                {
                    float2 fq = q - float2(0.0, 0.32);
                    float flame = SrEllipse(fq - float2(0.0, 0.06), float2(0.045, 0.1));
                    c = lerp(c, SrHex(255, 230, 150) * (1.0h + 0.4h * (half)_SrParams.y), SrFill(flame, aa));
                    c += SrHex(255, 150, 50) * (half)(exp(-length(fq) * 9.0) * 0.5) * inArch;
                    c = lerp(c, SrGold(p, 0.7h), SrFill(SrBox(fq + float2(0.0, 0.04), float2(0.09, 0.03)), aa));
                }
                c = lerp(c, sdk, SrLine(abs(q.x) - 0.47, 0.02, aa));
                c = lerp(c, ink * 0.7h, max(SrLine(y - 0.12, 0.012, aa), SrLine(y - 0.86, 0.012, aa)));
            }
            else if (kind == 1)
            {
                // Lotus vine on saffron: a waving stem, leaves either side, a lotus at every crest.
                float wave = 0.5 + 0.16 * sin(x * 6.2832);
                c = lerp(c, SrHex(46, 120, 70), SrLine(y - wave, 0.018, aa));
                float lx = frac(x * 2.0) - 0.5;
                float lside = fmod(floor(x * 2.0), 2.0) < 0.5 ? 1.0 : -1.0;
                float leafD = SrEllipse(SrRot(float2(lx, y - wave - lside * 0.11), lside * 0.6), float2(0.12, 0.05));
                c = lerp(c, c3, SrFill(leafD, aa));
                float2 lq = float2(frac(x + 0.25) - 0.5, y - 0.66);
                float lotus = SrPetals(lq * float2(1.0, 1.4), 5.0, 0.13, 0.5, 1.5708);
                c = lerp(c, lerp(c1, white, 0.25h), SrFill(max(lotus, -lq.y - 0.02), aa));
                c = lerp(c, ink, SrLine(max(lotus, -lq.y - 0.02), 0.008, aa));
            }
            else if (kind == 2)
            {
                // Bandhani: tie-dye dots in diamond clusters on maroon.
                c = lerp(SrHex(122, 30, 44), sdk, 0.2h) * (0.9h + 0.15h * (half)SrNoise2(p * 6.0));
                float2 bp = p * float2(3.0, 3.0);
                float2 bq = frac(bp) - 0.5;
                float clusterD = 1e3;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float2 o = float2(i == 0 ? 0.18 : i == 1 ? -0.18 : 0.0, i == 2 ? 0.18 : i == 3 ? -0.18 : 0.0);
                    clusterD = min(clusterD, length(bq - o) - 0.06);
                }
                c = lerp(c, fmod(floor(bp.x) + floor(bp.y), 2.0) < 0.5 ? SrHex(250, 220, 120) : white, SrFill(clusterD / 3.0, aa));
                c = lerp(c, c2, SrFill(length(bq) / 3.0 - 0.012, aa));
            }
            else if (kind == 3)
            {
                // Madhubani frieze: gold elephants walking on indigo, saw-tooth borders.
                c = lerp(SrHex(30, 44, 110), sd, 0.3h);
                float ex = frac(x / 1.6) * 1.6 - 0.8;
                float el = SrElephant(float2(ex + 0.25, y - 0.2), 0.52);
                c = lerp(c, SrGold(p * 1.5, 0.9h), SrFill(el, aa));
                c = lerp(c, c1, SrFill(SrBox(float2(ex + 0.27, y - 0.58), float2(0.14, 0.05)), aa) * SrFill(el, aa));
                float saw = abs(frac(x * 5.0) - 0.5) * 0.2;
                c = lerp(c, c2, (half)step(y, 0.1 - saw) + (half)step(0.9 + saw, y));
            }
            else if (kind == 4)
            {
                // Palace wall: rose plaster, a cusped arch round a jaali lattice glowing from inside, turquoise spandrels.
                c = lerp(SrHex(236, 160, 168), sd, 0.2h) * (0.92h + 0.1h * (half)SrNoise2(p * 4.0));
                float arch = SrCuspArch(q - float2(0.0, 0.12), 0.3, 0.38);
                half inA = SrFill(arch, aa);
                half spand = SrFill(SrBox(q - float2(0.0, 0.8), float2(0.45, 0.1)), aa) * (1.0h - inA);
                c = lerp(c, SrHex(70, 190, 190), spand);
                float2 jq = frac(float2((q.x + q.y) * 9.0, (q.x - q.y) * 9.0)) - 0.5;
                half lattice = SrLine(min(abs(jq.x), abs(jq.y)) / 9.0, 0.008, aa);
                half3 glow = lerp(SrHex(255, 190, 90), SrHex(255, 230, 160), (half)(0.6 - y)) * (0.8h + 0.4h * (half)_SrParams.y);
                c = lerp(c, lerp(glow, white * 0.92h, lattice), inA);
                c = lerp(c, white, SrLine(arch, 0.02, aa));
                c = lerp(c, SrGold(p, 0.8h), SrLine(arch - 0.035, 0.008, aa));
            }
            else
            {
                // Scallops: rows of fish-scale arches (the collage's blue panel), gold edged.
                float2 sp = float2(x * 4.0, y * 5.0);
                float row = floor(sp.y);
                float2 sq = float2(frac(sp.x + fmod(row, 2.0) * 0.5) - 0.5, frac(sp.y));
                float sc = length(sq - float2(0.0, 0.0)) - 0.55;
                half3 rc = fmod(row, 2.0) < 0.5 ? SrHex(40, 90, 160) : SrHex(60, 130, 190);
                c = rc * (0.88h + 0.2h * (half)SrNoise2(p * 7.0 + key));
                c = lerp(c, SrGold(p, 0.85h), SrLine(sc, 0.03, aa * 5.0));
            }
        }
        else
        {
            if (kind == 0)
            {
                // Arcade: pointed arches with a hanging lantern, turquoise tile spandrels.
                c = lerp(c2, sd, 0.5h) * (0.9h + 0.12h * (half)SrNoise2(p * 6.0));
                float arch = SrArch(q - float2(0.0, 0.08), 0.3, 0.38, 0.4);
                half inA = SrFill(arch, aa);
                half3 inside = lerp(sdk * 0.6h, SrHex(255, 170, 80) * 0.5h, (half)saturate(0.9 - y) * 0.6h);
                c = lerp(c, white, SrLine(arch, 0.025, aa));
                c = lerp(c, inside, inA);
                float2 lq = q - float2(0.0, 0.46);
                float lantern = min(SrBox(lq, float2(0.05, 0.08)), SrSeg(lq, float2(0.0, 0.08), float2(0.0, 0.3)) - 0.006);
                c = lerp(c, SrGold(p, 0.9h), SrFill(lantern, aa) * inA);
                c += SrHex(255, 180, 80) * (half)(exp(-length(lq) * 10.0) * 0.6 * _SrParams.y) * inA;
                float2 dq = frac(p * float2(8.0, 8.0)) - 0.5;
                c = lerp(c, white, SrFill(length(dq) / 8.0 - 0.01, aa) * (1.0h - inA) * 0.8h);
            }
            else if (kind == 1)
            {
                // Chintamani: three white balls and two wavy red stripes on cobalt, turquoise borders.
                c = lerp(c1, sd, 0.3h);
                float2 cq = float2(frac(x * 1.5) - 0.5, y - 0.5);
                float balls = min(length(cq - float2(-0.12, 0.08)), min(length(cq - float2(0.12, 0.08)), length(cq - float2(0.0, -0.1)))) - 0.07;
                c = lerp(c, white, SrFill(balls, aa));
                c = lerp(c, c3, SrFill(balls + 0.04, aa));
                float2 wq = float2(frac(x * 1.5 + 0.5) - 0.5, y - 0.5);
                float stripes = min(abs(wq.y - 0.08 - 0.06 * sin(wq.x * 12.0)), abs(wq.y + 0.08 - 0.06 * sin(wq.x * 12.0 + 1.0)));
                c = lerp(c, c3, SrLine(stripes, 0.025, aa) * (half)step(abs(wq.x), 0.3));
                c = lerp(c, c2, (half)step(y, 0.1) + (half)step(0.9, y));
            }
            else if (kind == 2)
            {
                // Mashrabiya screens in arches, amber light behind, gold studs.
                c = SrHex(110, 66, 38) * (0.85h + 0.25h * (half)SrNoise2(float2(x * 8.0, y * 0.8)));
                float arch = SrArch(q - float2(0.0, 0.1), 0.3, 0.4, 0.3);
                half inA = SrFill(arch, aa);
                float2 lq = frac(p * float2(10.0, 10.0)) - 0.5;
                half bars = max(SrLine(min(abs(lq.x), abs(lq.y)) / 10.0, 0.009, aa), SrFill(length(lq) / 10.0 - 0.018, aa));
                half3 amber = SrHex(255, 176, 80) * (0.8h + 0.4h * (half)_SrParams.y);
                c = lerp(c, lerp(amber, SrHex(70, 40, 22), bars), inA);
                c = lerp(c, SrGold(p, 0.8h), SrLine(arch, 0.018, aa));
                c = lerp(c, SrGold(p, 0.9h), SrFill(length(float2(abs(q.x) - 0.42, y - 0.5)) - 0.03, aa));
            }
            else if (kind == 3)
            {
                // Carpet border: reciprocal triangles (ivory / red) between gold lines.
                c = c1;
                float tx = frac(x * 4.0);
                float tri = y - 0.25 - abs(tx - 0.5) * 1.0;
                half up = (half)step(0.0, tri) * (half)step(y, 0.75);
                c = lerp(c, fmod(floor(x * 4.0), 2.0) < 0.5 ? white : c2, up * (half)step(0.25, y));
                c = lerp(c, SrGold(p, 0.85h), saturate(SrLine(y - 0.2, 0.02, aa) + SrLine(y - 0.8, 0.02, aa)));
                c = lerp(c, c3, (half)step(y, 0.12) + (half)step(0.88, y));
            }
            else if (kind == 4)
            {
                // Horseshoe arches over zellige, a muqarnas band of small arches along the top.
                c = lerp(SrHex(236, 222, 196), sd, 0.15h);
                float hs = length(q - float2(0.0, 0.42)) - 0.26;
                float horse = min(hs, SrBox(q - float2(0.0, 0.2), float2(0.21, 0.22)));
                half inH = SrFill(horse, aa);
                float2 zq = frac(p * 6.0) - 0.5;
                half chk = (half)fmod(floor(p.x * 6.0) + floor(p.y * 6.0), 2.0);
                half3 tiles = lerp(c1, white, chk);
                c = lerp(c, tiles, (half)step(y, 0.18) * (1.0h - inH));
                c = lerp(c, lerp(sdk, SrHex(255, 170, 80) * 0.6h, (half)saturate(0.8 - y) * 0.5h), inH);
                c = lerp(c, c2, SrLine(horse, 0.02, aa));
                float mx = frac(x * 6.0) - 0.5;
                float muq = length(float2(mx, (y - 0.8) * 3.0)) - 0.45;
                c = lerp(c, SrGold(p, 0.85h), (half)step(0.8, y) * (half)step(0.0, -muq) * 0.8h);
                c = lerp(c, white, SrLine(y - 0.8, 0.01, aa));
            }
            else
            {
                // Gold eight-point rosettes on cobalt, a rope border.
                c = lerp(c1, sd, 0.3h) * (0.9h + 0.12h * (half)SrNoise2(p * 5.0));
                float2 rq = float2(frac(x) - 0.5, y - 0.5);
                float s = SrStar8(rq, 0.24);
                c = lerp(c, SrGold(p, 0.9h), SrFill(s, aa));
                c = lerp(c, c2, SrFill(length(rq) - 0.1, aa));
                c = lerp(c, white, SrFill(length(rq) - 0.04, aa));
                float rope = frac(x * 10.0 + y * 3.0);
                half ropeOn = (half)step(y, 0.1) + (half)step(0.9, y);
                c = lerp(c, SrGold(p, lerp(0.6h, 1.0h, (half)rope)), ropeOn);
            }
        }

        // Gold lip along the top edge, a shadow line along the bottom, paper grain, the face's own light.
        c = lerp(c, SrGold(p * 1.7, lerp(0.6h, 1.0h, k)), (half)smoothstep(0.935, 0.965, y) * 0.85h);
        c *= lerp(0.72h, 1.0h, (half)smoothstep(0.0, 0.1, y));
        c *= 1.0h + SrGrain(p * 6.0 + key) * (half)_SrParams.z;
        c = lerp(c * half3(0.86h, 0.86h, 1.0h), c, k);   // cool shade side, as a painter would
        c *= shadeMul;
    }
    return c;
}

#endif
