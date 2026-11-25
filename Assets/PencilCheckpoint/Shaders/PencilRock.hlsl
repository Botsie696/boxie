#ifndef PENCIL_CP_ROCK_INCLUDED
#define PENCIL_CP_ROCK_INCLUDED

// Pencil checkpoint: the scene's own Polybox rocks (PixelRockLit, 3.9/3.10-only copy) and the checkpoint's own cliffs
// (PencilProp) drawn in graphite: faces toward the camera-relative light stay paper with a few contour strokes, faces
// away are hatched and cross-hatched, tops get scribbled grass tufts. Strokes are laid in a plane along the face
// (world space), so a whole cliff reads as one drawing; the post pass draws its outline.
// Needs PencilCommon.hlsl.

half PcRockInk(float3 wp, float3 n, float scale, float key)
{
    float S = max(scale, 0.25);
    float2 nh = n.xz;
    float2 t = dot(nh, nh) > 1e-4 ? normalize(float2(-nh.y, nh.x)) : float2(1, 0);
    float2 q = float2(dot(wp.xz, t), wp.y) / S;
    float aa = max(length(fwidth(q)), 1e-4) * 0.75;
    half lit = PcLit(n);
    half tone = saturate(lerp(0.95h, 0.2h, lit) + (half)((PixNoise(wp / S * 0.35) - 0.5) * 0.3));
    float w = 0.012;
    half ink;
    if (n.y > 0.6)
    {
        // Tops: paper with tufts of grass strokes.
        float2 g = wp.xz / S * 2.5;
        float2 c = floor(g);
        float2 f = frac(g) - 0.5;
        float h = PixHash(float3(c, key));
        half tuft = 0.0h;
        if (h > 0.55)
        {
            tuft = PcLine(PcSeg(f, float2(-0.1, -0.2), float2(-0.2, 0.2)), 0.03, aa * 2.5);
            tuft = max(tuft, PcLine(PcSeg(f, float2(0.0, -0.2), float2(0.03, 0.25)), 0.03, aa * 2.5));
            tuft = max(tuft, PcLine(PcSeg(f, float2(0.1, -0.2), float2(0.22, 0.15)), 0.03, aa * 2.5));
        }
        ink = max(tuft * 0.8h, PcHatch(wp.xz / S, 0.3, 0.12, w * 0.6, max(length(fwidth(wp.xz / S)), 1e-4), key) * 0.25h);
    }
    else
    {
        ink = PcTone(q, tone, 0.09, w * 1.2, aa, key);
        // Strata: long wandering contour strokes across the face.
        float sy = q.y * 1.4 + sin(q.x * 0.7) * 0.25 + sin(q.x * 2.3) * 0.08;
        float sr = floor(sy);
        half on = (half)step(PcHash(sr + key), 0.6) * (half)step(0.25, frac(q.x * 0.11 + PcHash(sr) * 3.0));
        ink = max(ink, PcLine((frac(sy) - 0.5) / 1.4, w * 1.1, aa) * on);
        // Cracks running down.
        float cx = q.x * 0.9 + sin(q.y * 1.7) * 0.2;
        float ci = floor(cx);
        half crack = PcLine((frac(cx) - 0.5) / 0.9, w * 0.9, aa) * (half)step(PcHash(ci * 7.0 + key), 0.25) * (half)step(0.4, frac(q.y * 0.2 + PcHash(ci)));
        ink = max(ink, crack);
    }
    return ink;
}

half3 PcRockColor(float3 wp, float3 n)
{
    return PcInk(PcRockInk(wp, normalize(n), _PcLightDir.w, 3.0));
}

#endif
