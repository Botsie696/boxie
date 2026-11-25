#ifndef PENCIL_CP_LEG_INCLUDED
#define PENCIL_CP_LEG_INCLUDED

// Pencil checkpoint: the leg texture (FlatFaceBlockPixel). Legs read as drawn, almost hollow shapes: the paper shows
// through the top, a doubled sketch stroke runs inside every long edge (the post pass draws the true outline), and the
// sides carry the graphite shading - light hatching on the side toward the light, cross-hatching on the side away.
// The level moves through four sketchbook pages (stage = quarter of the level's legs, or a cycle in the pencil scene):
//   0  sketchbook   loose scribbled hatching, smudges, wandering construction strokes (the robot)
//   1  drafting     smooth graded graphite, panel seams, rivets, vent slots (the spaceship study)
//   2  old town     plaster with timber posts, braces, beams and dark windows; cobbles on top (the medieval street)
//   3  dungeon      big staggered stones, dense diagonal hatching, lit corners, stipple; flagstones on top (the dungeon)
// Art legs (a share of the legs, whole leg): a framed frieze of a famous French / Italian drawing motif along both sides -
// Renaissance acanthus scroll, French fleur-de-lis, Roman arcade, Leonardo's circle-and-square + gears, French laurel swags.
// Obstacles (role 3) are near-solid graphite (danger reads as the darkest thing on the page); the runner cube is hollow.
// Needs PencilCommon.hlsl and the object matrices.

// ---------------------------------------------------------------- stage pages (side faces, q = (along, up 0..1))

half PcSideSketch(float2 q, half tone, float aa, float key)
{
    float s = max(_PcParams.x, 4.0);
    tone += (half)((PixNoise(float3(q * float2(0.9, 2.5), key)) - 0.5) * 0.35);   // smudges
    half ink = PcTone(q, tone, 1.0 / s, _PcParams.y * 0.7, aa, key);
    // Wandering construction strokes, like the robot's loose outlines.
    float r = floor(q.x * 0.8);
    float yv = 0.14 + 0.16 * PcHash(r + key) + 0.02 * sin(q.x * 3.1 + r);
    ink = max(ink, PcLine(q.y - yv, _PcParams.y * 0.6, aa) * (half)step(PcHash(r * 3.0 + key), 0.55) * 0.75h);
    float yv2 = 0.8 + 0.1 * PcHash(r + key + 9.0) + 0.015 * sin(q.x * 4.3);
    ink = max(ink, PcLine(q.y - yv2, _PcParams.y * 0.5, aa) * (half)step(PcHash(r * 5.0 + key), 0.4) * 0.6h);
    // Flicks of graphite.
    float2 c = floor(q * float2(9.0, 9.0));
    if (PixHash(float3(c, key + 4.0)) > 0.94)
    {
        float2 f = frac(q * 9.0) - 0.5;
        ink = max(ink, PcLine(PcSeg(f, float2(-0.2, -0.1), float2(0.2, 0.15)), 0.05, aa * 9.0) * 0.8h);
    }
    return ink;
}

half PcSideDrafting(float2 q, half tone, float aa, float key)
{
    // Smooth graded graphite, darker low down, streaked along the leg like a soft pencil laid flat.
    half t = saturate(tone * 0.85h + (half)(1.0 - q.y) * 0.25h);
    half streak = (half)lerp(0.75, 1.0, PixNoise(float3(q.x * 2.5, q.y * 45.0, key)));
    half ink = t * 0.72h * streak;
    float w = _PcParams.y;
    // Panels: seams of random spacing, a double line at each.
    float P = 1.6;
    float pi = floor(q.x / P);
    float x = q.x - pi * P;
    ink = max(ink, PcLine(x, w, aa));
    ink = max(ink, PcLine(x - 0.06, w * 0.6, aa) * 0.7h);
    ink = max(ink, PcLine(q.y - 0.64, w, aa));
    ink = max(ink, PcLine(q.y - 0.6, w * 0.6, aa) * 0.6h);
    // Rivets.
    float2 rp = float2((frac(q.x * 4.0) - 0.5) * 0.25, q.y - 0.82);
    ink = max(ink, PcLine(length(rp) - 0.017, w * 0.6, aa));
    // Vent slots in some panels.
    if (PcHash(pi + key) < 0.5)
    {
        float d = 1e3;
        for (int k = 0; k < 3; k++) d = min(d, PcBox(float2(x, q.y), float2(0.75, 0.2 + k * 0.1), float2(0.3, 0.018)));
        ink = max(ink, PcLine(d, w * 0.7, aa));
        ink = max(ink, (half)step(d, 0.0) * 0.85h);
    }
    // Hatch hint in the lower band.
    ink = max(ink, PcHatch(q, 0.785, 0.120, w * 0.6, aa, key) * (half)smoothstep(0.35, 0.05, q.y) * 0.7h);
    return ink;
}

half PcSideTown(float2 q, half tone, float aa, float key)
{
    float w = _PcParams.y;
    float s = max(_PcParams.x, 4.0);
    half ink = PcTone(q, tone * 0.7h, 1.0 / s, w * 0.6, aa, key);   // plaster
    float B = 1.25;
    float bi = floor(q.x / B);
    float x = q.x - bi * B;
    float2 p = float2(x, q.y);
    // Timber: top beam, sill, post at the bay start.
    float beam = min(PcBox(p, float2(B * 0.5, 0.905), float2(B * 0.6, 0.045)), PcBox(p, float2(B * 0.5, 0.09), float2(B * 0.6, 0.04)));
    beam = min(beam, PcBox(p, float2(0.04, 0.5), float2(0.04, 0.5)));
    float h = PcHash(bi + key);
    float2 wc = float2(B * 0.58, 0.5);
    float win = PcBox(p, wc, float2(0.2, 0.2));
    if (h < 0.45)
    {
        // A diagonal brace across the bay.
        beam = min(beam, PcSeg(p, float2(0.08, 0.13), float2(B * 0.95, 0.86)) - 0.03);
        win = 1e3;
    }
    half timber = (half)step(beam, 0.0);
    ink = lerp(ink, PcTone(q, 0.78h, 0.068, w * 0.8, aa, key + 2.0), timber);
    ink = max(ink, PcLine(beam, w, aa));
    // The window: dark pane, paper mullions, a sill line under it.
    if (win < 0.5)
    {
        half pane = (half)step(win, 0.0);
        ink = lerp(ink, max(PcTone(q, 0.92h, 0.060, w, aa, key + 4.0), 0.55h), pane);
        float mull = min(abs(p.x - wc.x), abs(p.y - wc.y));
        ink = lerp(ink, 0.0h, PcLine(mull, 0.018, aa) * pane);
        ink = max(ink, PcLine(win, w * 1.2, aa));
        ink = max(ink, PcLine(PcBox(p, wc - float2(0.0, 0.23), float2(0.24, 0.0)), w, aa));
    }
    return ink;
}

half PcSideDungeon(float2 q, half tone, float aa, float key)
{
    float w = _PcParams.y;
    float rowF = q.y * 3.0;
    float row = floor(rowF);
    float rv = frac(rowF);
    float L = 0.85;
    float off = fmod(abs(row), 2.0) * 0.45;
    float bx = (q.x + off) / L;
    float bi = floor(bx);
    float fx = frac(bx);
    float hs = PcHash(bi * 3.7 + row * 11.1 + key);
    // Each stone: darker low and to the right, a lit top-left corner, its own value.
    half t = tone + (half)((hs - 0.5) * 0.25) + (half)((1.0 - rv) * 0.22 + fx * 0.15);
    t -= (half)(step(fx, 0.3) * step(0.65, rv) * 0.3);
    half ink = PcHatch(q, 0.9, 0.075, w * 0.75, aa, key) * (half)smoothstep(0.1, 0.25, t);
    ink = max(ink, PcHatch(q, -0.7, 0.090, w * 0.6, aa, key + 1.0) * (half)smoothstep(0.55, 0.7, t));
    // Joints (thick, as the stones are big), a chipped corner now and then.
    float jx = min(fx, 1.0 - fx) * L;
    float jy = min(rv, 1.0 - rv) / 3.0;
    ink = max(ink, PcLine(min(jx, jy), w * 1.6, aa));
    // Stipple.
    if (PixHash(float3(floor(q * 38.0), key + 7.0)) > 0.975) ink = max(ink, 0.8h);
    // A crack in some stones.
    if (hs > 0.8)
    {
        float2 sp = float2(fx * L, rv / 3.0);
        float cr = PcSeg(sp, float2(L * 0.3, 0.33), float2(L * 0.45, 0.17));
        cr = min(cr, PcSeg(sp, float2(L * 0.45, 0.17), float2(L * 0.38, 0.05)));
        ink = max(ink, PcLine(cr, w * 0.7, aa));
    }
    return ink;
}

// ---------------------------------------------------------------- tops (q = (along, across 0..1))

half PcTop(int stage, float2 q, half shadeEdge, float aa, float key)
{
    float w = _PcParams.y;
    half ink = 0.0h;
    // A soft shading band along the long edge away from the light (all pages).
    float edgeD = shadeEdge > 0.5h ? q.y : 1.0 - q.y;
    ink = max(ink, PcHatch(q, 0.6, 0.105, w * 0.6, aa, key) * (half)smoothstep(0.22, 0.02, edgeD) * 0.8h);
    if (stage == 0)
    {
        float2 c = floor(q * 7.0);
        if (PixHash(float3(c, key + 4.0)) > 0.9)
        {
            float2 f = frac(q * 7.0) - 0.5;
            ink = max(ink, PcLine(PcSeg(f, float2(-0.15, 0.0), float2(0.15, 0.08)), 0.04, aa * 7.0) * 0.6h);
        }
    }
    else if (stage == 1)
    {
        float x = q.x - floor(q.x / 1.6) * 1.6;
        ink = max(ink, PcLine(x, w, aa));
        ink = max(ink, PcLine(abs(q.y - 0.5) - 0.2, w * 0.7, aa) * 0.8h);
        float2 rp = float2((frac(q.x * 2.0) - 0.5) * 0.5, abs(q.y - 0.5) - 0.32);
        ink = max(ink, PcLine(length(rp) - 0.015, w * 0.6, aa));
    }
    else if (stage == 2)
    {
        // Cobbles: four rows of rounded stones, staggered.
        float rf = q.y * 4.0;
        float r = floor(rf);
        float bx = (q.x + fmod(r, 2.0) * 0.17) / 0.34;
        float2 f = float2(frac(bx) - 0.5, frac(rf) - 0.5);
        float d = PcBox(f * float2(0.34, 0.25), 0, float2(0.13, 0.085)) - 0.03;
        ink = max(ink, PcLine(d, w * 0.8, aa) * 0.75h);
        ink = max(ink, (half)step(0.0, d) * 0.25h);
    }
    else
    {
        // Flagstones with dark gaps, like the dungeon floor.
        float x = frac(q.x / 0.75 + step(0.5, q.y) * 0.5) * 0.75;
        float gap = min(min(x, 0.75 - x), abs(q.y - 0.5));
        ink = max(ink, PcLine(gap, w * 1.8, aa));
        ink = max(ink, PcHatch(q, 0.785, 0.135, w * 0.5, aa, key + 2.0) * 0.35h);
    }
    return ink;
}

// ---------------------------------------------------------------- art legs (side faces, q = (along, up 0..1))

half PcArtAcanthus(float2 q, float aa, float w, float key, out half fill)
{
    float P = 1.6;
    float k = 6.2831853 / P;
    float y = 0.5 + 0.2 * sin(q.x * k);
    float dy = 0.2 * k * cos(q.x * k);
    float stem = (q.y - y) / sqrt(1.0 + dy * dy);
    half ink = PcLine(stem, w * 1.3, aa);
    ink = max(ink, PcLine(stem - 0.025, w * 0.6, aa) * 0.6h);
    // Spiral curls in the hollow beside every crest.
    float half_ = P * 0.5;
    float ci = floor((q.x - P * 0.25) / half_ + 0.5);
    float cu = P * 0.25 + ci * half_;
    float up = fmod(abs(ci), 2.0) < 0.5 ? 1.0 : -1.0;   // crest up -> curl below it
    float2 c = float2(cu + 0.08 * up, 0.5 - 0.13 * up);
    float2 d = q - c;
    float r = length(d);
    float th = atan2(d.y, d.x) * up;
    float kk = 0.045;
    float sp = r / kk - th / 6.2831853;
    float sd = (frac(sp) - 0.5) * kk;
    half curl = PcLine(sd, w, aa) * (half)step(r, 0.17) * (half)step(0.012, r);
    ink = max(ink, curl);
    // Acanthus leaves along the stem (lens shapes leaning with it), hatched.
    float li = floor(q.x / (P * 0.25));
    float lu = (li + 0.5) * P * 0.25;
    float ly = 0.5 + 0.2 * sin(lu * k);
    float side = fmod(abs(li), 2.0) < 0.5 ? 1.0 : -1.0;
    float rot = atan(0.2 * k * cos(lu * k)) + side * 0.7;
    float le = PcEllipse(q, float2(lu, ly + side * 0.07), float2(0.11, 0.035), rot);
    ink = max(ink, PcLine(le, w, aa));
    fill = (half)step(le, 0.0);
    // A centre vein.
    ink = max(ink, PcLine(PcEllipse(q, float2(lu, ly + side * 0.07), float2(0.11, 0.0001), rot), w * 0.5, aa) * fill * 0.7h);
    return ink;
}

half PcArtFleur(float2 q, float aa, float w, float key, out half fill)
{
    float T = 0.9;
    float x = q.x - (floor(q.x / T) + 0.5) * T;
    float2 p = float2(x, q.y - 0.5);
    // Centre petal: a stretched vesica.
    float2 pv = float2(p.x, (p.y - 0.02) * 0.55);
    float d = max(length(pv - float2(0.06, 0.0)) - 0.1, length(pv + float2(0.06, 0.0)) - 0.1);
    // Side petals: crescents curling out and down.
    float2 ps = float2(abs(p.x), p.y);
    float cres = max(length(ps - float2(0.13, 0.06)) - 0.095, -(length(ps - float2(0.105, 0.1)) - 0.075));
    cres = max(cres, -p.y - 0.02);
    d = min(d, cres);
    // Crossbar, stem and foot.
    d = min(d, PcBox(p, float2(0.0, -0.04), float2(0.14, 0.022)));
    d = min(d, PcBox(p, float2(0.0, -0.12), float2(0.028, 0.06)));
    d = min(d, length(ps - float2(0.06, -0.18)) - 0.03);
    fill = (half)step(d, 0.0);
    half ink = PcLine(d, w * 1.2, aa);
    // Diamonds between the lilies.
    float2 pd = float2(abs(q.x - floor(q.x / T) * T - 0.0), q.y - 0.5);
    float2 pd2 = float2(min(pd.x, T - pd.x), pd.y);
    float dia = abs(pd2.x) + abs(pd2.y) * 0.6 - 0.05;
    ink = max(ink, PcLine(dia, w, aa));
    return ink;
}

half PcArtArcade(float2 q, float aa, float w, float key, out half fill)
{
    float B = 1.1;
    float x = q.x - (floor(q.x / B) + 0.5) * B;
    float2 p = float2(x, q.y);
    float spring = 0.58;
    float ro = B * 0.5 - 0.05, ri = ro - 0.07;
    float2 a = p - float2(0.0, spring);
    float r = length(a);
    bool above = p.y >= spring;
    // The opening under the arch: deep loggia shadow.
    float open = above ? r - ri : abs(p.x) - ri;
    open = max(open, 0.12 - p.y);
    fill = (half)step(open, 0.0);
    half ink = PcLine(open, w * 1.2, aa);
    if (above)
    {
        ink = max(ink, PcLine(r - ro, w, aa));
        // Voussoirs: radial joints in the arch band.
        float ang = atan2(a.y, a.x);
        float vj = abs(frac(ang / 0.2618 + 0.5) - 0.5) * 0.2618 * r;
        ink = max(ink, PcLine(vj, w * 0.7, aa) * (half)step(ri, r) * (half)step(r, ro));
        // Keystone.
        ink = max(ink, PcLine(PcBox(p, float2(0.0, spring + ro - 0.03), float2(0.035, 0.05)), w, aa));
    }
    else
    {
        // Pilasters with capitals between the bays.
        float px = abs(p.x) - (ri + 0.035);
        ink = max(ink, PcLine(abs(px) - 0.035, w, aa) * (half)step(p.y, spring));
        ink = max(ink, PcLine(PcBox(float2(abs(p.x), p.y), float2(B * 0.5, spring - 0.02), float2(0.07, 0.02)), w, aa));
        ink = max(ink, PcLine(PcBox(float2(abs(p.x), p.y), float2(B * 0.5, 0.13), float2(0.065, 0.015)), w, aa));
    }
    // Roundels in the spandrels.
    ink = max(ink, PcLine(length(float2(B * 0.5 - abs(p.x), p.y - 0.84)) - 0.05, w, aa));
    // Cornice.
    ink = max(ink, PcLine(p.y - 0.12, w, aa));
    return ink;
}

half PcArtLeonardo(float2 q, float aa, float w, float key, out half fill)
{
    float T = 1.0;
    float ti = floor(q.x / T);
    float2 p = float2(q.x - (ti + 0.5) * T, q.y - 0.5);
    half ink = 0.0h;
    fill = 0.0h;
    if (fmod(abs(ti), 2.0) < 0.5)
    {
        // Circle and square (Vitruvian), with construction lines.
        ink = PcLine(length(p) - 0.33, w * 1.2, aa);
        float sq = PcBox(p, float2(0.0, -0.035), float2(0.295, 0.295));
        ink = max(ink, PcLine(sq, w * 1.2, aa));
        ink = max(ink, PcLine(abs(p.x) - abs(p.y + 0.035), w * 0.5, aa) * (half)step(sq, 0.0) * 0.55h);
        ink = max(ink, PcLine(p.x, w * 0.5, aa) * 0.45h);
        // A figure: head and outstretched arms / legs, in a few strokes.
        ink = max(ink, PcLine(length(p - float2(0.0, 0.2)) - 0.045, w, aa));
        float body = PcSeg(p, float2(0.0, 0.155), float2(0.0, -0.08));
        body = min(body, PcSeg(p, float2(-0.29, 0.1), float2(0.29, 0.1)));
        body = min(body, PcSeg(p, float2(0.0, -0.08), float2(-0.13, -0.32)));
        body = min(body, PcSeg(p, float2(0.0, -0.08), float2(0.13, -0.32)));
        body = min(body, PcSeg(p, float2(0.0, 0.11), float2(-0.25, 0.27)) + 0.3 * step(0.0, p.x));
        body = min(body, PcSeg(p, float2(0.0, 0.11), float2(0.25, 0.27)) + 0.3 * step(p.x, 0.0));
        ink = max(ink, PcLine(body, w * 1.1, aa));
        fill = (half)step(length(p) - 0.33, 0.0) * 0.25h;
    }
    else
    {
        // A cog wheel with spokes.
        float r = length(p);
        float th = atan2(p.y, p.x);
        float rg = 0.25 + 0.035 * clamp(cos(th * 12.0) * 3.0, -1.0, 1.0);
        float g = r - rg;
        ink = PcLine(g, w * 1.2, aa);
        ink = max(ink, PcLine(r - 0.2, w * 0.7, aa));
        ink = max(ink, PcLine(r - 0.06, w, aa));
        float spoke = abs(frac(th / 1.0472 + 0.5) - 0.5) * 1.0472 * r;
        ink = max(ink, PcLine(spoke - 0.012, w * 0.8, aa) * (half)step(0.06, r) * (half)step(r, 0.2));
        fill = (half)step(g, 0.0) * (half)step(0.2, r);
    }
    // Mirror-writing notes along the bottom.
    float2 n = float2(q.x * 7.0, (q.y - 0.17) * 30.0);
    float word = step(0.35, PcHash(floor(q.x * 2.2) + key));
    ink = max(ink, PcLine((n.y - sin(n.x * 9.0) * 0.4) / 30.0, w * 0.55, aa) * (half)step(abs(n.y), 0.8) * (half)word * 0.7h);
    return ink;
}

half PcArtSwag(float2 q, float aa, float w, float key, out half fill)
{
    float T = 1.2;
    float x = q.x - floor(q.x / T) * T;
    float s = x / T;
    float y = 0.8 - 1.28 * s * (1.0 - s);
    float dy = -1.28 * (1.0 - 2.0 * s) / T;
    float thick = lerp(0.02, 0.06, sin(s * 3.14159));
    float d = (q.y - y) / sqrt(1.0 + dy * dy);
    float band = abs(d) - thick;
    fill = (half)step(band, 0.0);
    half ink = PcLine(band, w * 1.1, aa);
    // Laurel leaves: little lenses along the swag, alternating up / down.
    float li = floor(x / 0.07);
    float lx = (li + 0.5) * 0.07;
    float ls = lx / T;
    float ly = 0.8 - 1.28 * ls * (1.0 - ls);
    float sd = fmod(li, 2.0) < 0.5 ? 1.0 : -1.0;
    float le = PcEllipse(float2(x, q.y), float2(lx, ly + sd * (lerp(0.02, 0.06, sin(ls * 3.14159)) + 0.012)), float2(0.035, 0.013), atan(-1.28 * (1.0 - 2.0 * ls) / T) + sd * 0.6);
    ink = max(ink, PcLine(le, w * 0.8, aa));
    // Bows and ribbons at the nails, a tassel at the low point.
    float xn = min(x, T - x);
    float2 pn = float2(xn, q.y - 0.82);
    ink = max(ink, PcLine(length(pn) - 0.035, w, aa));
    ink = max(ink, PcLine(PcSeg(float2(xn, q.y), float2(0.01, 0.79), float2(0.05 + 0.015 * sin(q.y * 30.0), 0.55)), w * 0.8, aa));
    float2 pt = float2(x - T * 0.5, q.y - (0.48 - 0.1));
    ink = max(ink, PcLine(PcSeg(pt, float2(0.0, 0.1), float2(0.0, 0.0)), w, aa));
    ink = max(ink, PcLine(PcBox(pt, float2(0.0, -0.04), float2(0.018, 0.04)), w, aa));
    fill = max(fill, (half)step(PcBox(pt, float2(0.0, -0.04), float2(0.018, 0.04)), 0.0));
    return ink;
}

half PcArt(int motif, float2 q, half tone, float aa, float key)
{
    float w = _PcParams.y;
    half fill = 0.0h;
    half ink;
    if (motif == 0) ink = PcArtAcanthus(q, aa, w, key, fill);
    else if (motif == 1) ink = PcArtFleur(q, aa, w, key, fill);
    else if (motif == 2) ink = PcArtArcade(q, aa, w, key, fill);
    else if (motif == 3) ink = PcArtLeonardo(q, aa, w, key, fill);
    else ink = PcArtSwag(q, aa, w, key, fill);
    // Shading: the motif's solids hatched by the face's light, the field left nearly bare, the frame bands darker.
    half t = tone;
    half field = PcHatch(q, 0.785, 0.135, w * 0.5, aa, key) * (half)smoothstep(0.3, 0.6, t) * 0.5h;
    half solid = PcTone(q, saturate(0.45h + t * 0.5h), 0.075, w * 0.7, aa, key + 3.0);
    half frame = (half)(step(q.y, 0.1) + step(0.9, q.y));
    half frameInk = PcTone(q, saturate(0.5h + t * 0.4h), 0.090, w * 0.7, aa, key + 5.0);
    half o = lerp(field, solid, fill);
    o = lerp(o, frameInk, frame);
    o = max(o, ink * (1.0h - frame));
    o = max(o, PcSketchEdge(q.y - 0.1, q.x, w, aa, key + 1.0));
    o = max(o, PcSketchEdge(q.y - 0.9, q.x, w, aa, key + 2.0));
    return o;
}

// ---------------------------------------------------------------- the leg

int PcStageOf(float serial)
{
    float n = max(_PcStage.w, 1.0);
    if (_PcStage.z > 0.5) return (int)fmod(floor(serial / _PcStage.z), n);
    float span = max(_PcStage.y - _PcStage.x, 1.0);
    return (int)clamp(floor((serial - _PcStage.x) / span * n), 0.0, n - 1.0);
}

half3 PcLegColor(float3 posOS, float3 nOS, float3 nWS, float3 wp, float4 leg, half role)
{
    float aa = max(max(length(fwidth(posOS)), 1e-4), length(fwidth(wp)) * 0.0) * 0.75;

    float3 origin = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
    float3 legW = normalize(float3(unity_ObjectToWorld._m02, unity_ObjectToWorld._m12, unity_ObjectToWorld._m22));
    bool hasLeg = leg.w > 0.5;
    float u = hasLeg ? leg.x + posOS.z : dot(origin, legW) + posOS.z + 500.0;
    float3 lateral = origin - legW * dot(origin, legW);
    float serial = hasLeg ? floor(leg.z + 0.5) : floor(frac(dot(round(lateral * 2.0), float3(0.173, 0.311, 0.071))) * 977.0);
    serial = abs(serial);
    float key = serial * 1.37;

    float3 an = abs(nOS);
    bool cap = an.z > 0.5;
    float3 upOS = normalize(mul((float3x3)unity_WorldToObject, float3(0, 1, 0)));
    bool level = abs(upOS.z) < 0.5;
    float3 latOS = level ? normalize(cross(upOS, float3(0, 0, 1))) : float3(1, 0, 0);
    float v = dot(posOS, upOS) + 0.5;
    float lat = dot(posOS, latOS) + 0.5;

    bool top = nWS.y > 0.6;
    bool bottom = nWS.y < -0.6;
    half lit = PcLit(nWS);
    half sideTone = lerp(0.88h, 0.42h, lit);
    float w = _PcParams.y;

    // Obstacles: the darkest thing on the page.
    if (role > 2.5h)
    {
        float2 qo = top ? wp.xz : float2(dot(wp.xz, normalize(float2(-nWS.z, nWS.x) + 1e-4)), wp.y);
        half io = max(PcTone(qo, 0.95h, 0.053, w, aa, 17.0), 0.9h);
        return PcInk(io);
    }

    // The runner cube: hollow - paper, sketch strokes round every face, a little hatching away from the light.
    if (role > 1.5h)
    {
        float2 qr = an.x > 0.5 ? posOS.yz : an.y > 0.5 ? posOS.xz : posOS.xy;
        float er = 0.5 - max(abs(qr.x), abs(qr.y));
        half ir = PcHatch(qr, 0.785, 0.105, w * 0.6, aa, 5.0) * (half)smoothstep(0.35, 0.7, 1.0 - lit) * 0.8h;
        ir = max(ir, PcSketchEdge(er - 0.03, qr.x + qr.y, w * 1.3, aa, 3.0));
        return PcInk(ir);
    }

    int stage = PcStageOf(serial);
    bool art = level && !cap && !top && !bottom && hasLeg && PcHash(serial * 2.9 + 4.0) < _PcParams.z && role < 1.5h;
    int motif = (int)fmod(serial * 3.0 + 1.0, 5.0);

    half ink;
    float edgeV;
    float edgeT;
    if (top && level && !cap)
    {
        float2 q = float2(u, lat);
        // Shade band on the edge away from the light: which side of the top faces the light?
        float3 latW = normalize(mul((float3x3)unity_ObjectToWorld, latOS));
        half shadeEdge = (half)step(0.0, dot(latW, _PcLightDir.xyz));
        ink = PcTop(stage, q, shadeEdge, aa, key);
        edgeV = lat;
        edgeT = u;
    }
    else if (bottom)
    {
        float2 q = cap ? posOS.xy + 0.5 : float2(u, lat);
        ink = PcTone(q, 0.85h, 0.068, w * 0.8, aa, key);
        edgeV = 0.5;
        edgeT = u;
    }
    else
    {
        float2 q;
        if (!level) q = float2(wp.y, (an.x > 0.5 ? posOS.y : posOS.x) + 0.5);
        else if (cap) q = float2(lat + 37.0 * sign(nOS.z), v);
        else q = float2(u, v);
        if (top) { ink = PcTop(stage, q, 0.0h, aa, key); }
        else if (art) ink = PcArt(motif, q, sideTone, aa, key);
        else if (stage == 0) ink = PcSideSketch(q, sideTone, aa, key);
        else if (stage == 1) ink = PcSideDrafting(q, sideTone, aa, key);
        else if (stage == 2) ink = PcSideTown(q, sideTone, aa, key);
        else ink = PcSideDungeon(q, sideTone, aa, key);
        edgeV = level ? v : q.y;
        edgeT = q.x;
    }
    // The doubled sketch strokes inside the long edges (and round the end caps).
    if (!bottom)
    {
        ink = max(ink, PcSketchEdge(edgeV - 0.035, edgeT, w, aa, key + 11.0));
        ink = max(ink, PcSketchEdge(0.965 - edgeV, edgeT, w, aa, key + 13.0));
        if (cap) ink = max(ink, PcSketchEdge(min(lat, 1.0 - lat) - 0.035, v, w, aa, key + 15.0));
    }
    // The faces still part a little in value without strokes, so a bare page never flattens the leg.
    half faceVal = top ? 1.0h : bottom ? 0.8h : lerp(0.9h, 0.99h, lit);
    return PcInk(ink) * faceVal;
}

#endif
