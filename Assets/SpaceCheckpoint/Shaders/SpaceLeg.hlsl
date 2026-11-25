#ifndef SPACE_CP_LEG_INCLUDED
#define SPACE_CP_LEG_INCLUDED

// Space checkpoint: the leg texture (FlatFaceBlockPixel). Every leg is a strip of spaceship, painted in thick oil:
//   top     the deck - pale periwinkle hull plate laid in long brush strokes
//   sides   the walls - royal blue (lit) / deep indigo (shade), portholes, glass corridors, pipes
//   bottom  the underbelly - hot coral-orange with glowing thruster vents
// Four designs by leg serial, and one leg in every four is a whole SHIP (cream hull, cockpit canopy, engine glow):
//   0 ship      cream deck with a blue spine + canopy, side: window strip, orange racing stripe, engine nozzles
//   1 hull      riveted plates with hazard edge stripes, side: round portholes with warm light and crew
//   2 corridor  deck grating with cyan light strips, side: a long glass band onto a lit interior with consoles
//   3 engine    vented deck with glowing slits and thruster rings, side: conduits with glowing joints
// Needs SpaceCommon.hlsl and the object matrices. leg = (along start, length, serial, hasLeg) as FlowerLeg.hlsl.

half3 SpLegColor(float3 posOS, float3 nOS, float3 nWS, float3 wp, half3 col, half3 lLeft, half3 lRight, half3 tint, float4 leg)
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

    // One leg in four is a ship (which one of each four is random), the rest cycle through hull / corridor / engine.
    float grp = floor(serial / 4.0);
    float pick = floor(PixHash(float3(grp, 5.3, 1.9)) * 4.0);
    bool ship = abs((serial - grp * 4.0) - pick) < 0.5;
    float hk = PixHash(float3(serial, 2.2, 7.7));
    int kind = ship ? 0 : (hk < 0.34 ? 1 : (hk < 0.67 ? 2 : 3));

    float3 an = abs(nOS);
    bool cap = an.z > 0.5;
    float3 upOS = normalize(mul((float3x3)unity_WorldToObject, float3(0, 1, 0)));
    bool level = abs(upOS.z) < 0.5;
    float3 latOS = level ? normalize(cross(upOS, float3(0, 0, 1))) : float3(1, 0, 0);
    float v = dot(posOS, upOS) + 0.5;                         // 0 bottom .. 1 top
    float lat = dot(posOS, latOS) + 0.5;                      // 0 .. 1 across

    half lumMax = max(max(PixLum(lLeft), PixLum(lRight)) * tintMax, 1e-3h);
    half k = saturate(PixLum(col) / lumMax);
    half shadeMul = lerp(0.6h, 1.0h, k) * tintMax;
    half brushAmt = (half)_SpTop.a;
    half3 c;

    bool top = nWS.y > 0.6;
    bool bottom = nWS.y < -0.6;
    half3 cream = _SpCream.rgb, accent = _SpAccent.rgb, glow = _SpGlow.rgb, window = _SpWindow.rgb;

    if (top)
    {
        float2 p = (level && !cap) ? float2(u, lat) : posOS.xy + 0.5;
        half s = SpBrush(p * float2(1.0, 1.6), key);
        half g = SpBands(saturate(s * 1.15h), 3.0h);
        c = lerp(_SpTopDark.rgb, _SpTop.rgb, lerp(0.7h, g, brushAmt));
        // Warm / cool dabs: oil colour is never one flat value.
        half warm = (half)smoothstep(0.55, 0.8, PixNoise(float3(p * float2(0.45, 2.6), key + 7.0)));
        c = lerp(c, c * half3(1.14h, 0.98h, 0.8h), warm * 0.55h);
        half ridge = SpRidge(saturate(s * 1.15h), 3.0h);
        c += ridge * 0.05h;

        float edge = (level && !cap) ? min(lat, 1.0 - lat) : 0.5 - max(abs(posOS.x), abs(posOS.y));
        float cu = frac(u) - 0.5;
        half3 dk = _SpTopDark.rgb * 0.45h;

        if (kind == 0)
        {
            // Ship deck: cream plating, a blue spine, two orange pinstripes, the cockpit canopy, chevrons.
            c = lerp(c, cream * lerp(0.82h, 1.0h, g), 0.62h);
            half spine = (half)(1.0 - smoothstep(0.075 - aa, 0.075 + aa, abs(lat - 0.5)));
            c = lerp(c, _SpSide.rgb * 1.25h, spine * 0.85h);
            half pin = (half)(max(1.0 - smoothstep(0.016 - aa, 0.016 + aa, abs(lat - 0.28)), 1.0 - smoothstep(0.016 - aa, 0.016 + aa, abs(lat - 0.72))));
            c = lerp(c, accent, pin);
            float ch = abs(frac(u * 0.5 - abs(lat - 0.5) * 1.3) - 0.5);
            c = lerp(c, accent * 0.9h, (half)(1.0 - smoothstep(0.035 - aa, 0.035 + aa, ch)) * 0.55h * (1.0h - spine));
            // Canopy: a dark teal glass ellipse with a sky-bright highlight, every 6 units.
            float cx = (frac(u / 6.0) - 0.5) * 6.0;
            float ell = length(float2(cx / 0.95, (lat - 0.5) / 0.3));
            half glass = (half)(1.0 - smoothstep(1.0 - aa * 4.0, 1.0 + aa * 4.0, ell));
            half3 gc = lerp(half3(0.02h, 0.12h, 0.2h), glow * 0.7h, saturate((half)(0.5 - cx * 0.3) + (half)(lat - 0.5)));
            gc = lerp(gc, half3(1, 1, 1), (half)(1.0 - smoothstep(0.0, 0.35, length(float2(cx + 0.35, lat - 0.62) * float2(1.0, 3.0)))) * 0.7h);
            half frame = (half)(1.0 - smoothstep(1.0 + aa * 4.0, 1.18 + aa * 4.0, ell)) - glass;
            c = lerp(c, cream * 0.9h, saturate(frame));
            c = lerp(c, gc, glass);
        }
        else if (kind == 1)
        {
            // Riveted plates, dark seams, hazard stripes along both edges.
            float d = min(frac(u), 1.0 - frac(u));
            half seam = (half)(1.0 - smoothstep(0.018 - aa, 0.018 + aa, d));
            c = lerp(c, dk, seam * 0.8h);
            c = lerp(c, c * 1.18h + 0.03h, (half)(1.0 - smoothstep(0.018, 0.045, d)) * (1.0h - seam) * 0.35h);   // lit lip of each plate
            float2 q = abs(float2(cu, lat - 0.5)) - float2(0.4, 0.34);
            half rivet = (half)(1.0 - smoothstep(0.04 - aa, 0.04 + aa, length(q)));
            c = lerp(c, cream * 0.9h, rivet);
            float hz = frac((u + lat) * 3.0);
            half band = (half)(smoothstep(0.035, 0.045, edge) * (1.0 - smoothstep(0.1, 0.11, edge)));
            c = lerp(c, step(0.5, hz) > 0.5 ? accent : dk, band * 0.9h);
            // A numbered hatch on some plates.
            half hatch = (half)step(0.72, PixHash(float3(floor(u), key, 4.0)));
            half hb = SpBox(float2(cu, lat - 0.5), float2(0.18, 0.16), aa);
            c = lerp(c, c * 0.72h, hb * hatch);
            c = lerp(c, accent * 0.8h, SpBox(float2(cu, lat - 0.5), float2(0.12, 0.02), aa) * hatch);
        }
        else if (kind == 2)
        {
            // Corridor deck: grating, glowing cyan light strips down both sides, dark floor seams.
            half gr = (half)(step(0.5, frac(lat * 9.0)) * 0.5 + step(0.5, frac(u * 3.0)) * 0.5);
            c *= lerp(0.86h, 1.0h, gr);
            half strips = (half)max(1.0 - smoothstep(0.03 - aa, 0.03 + aa, abs(lat - 0.2)), 1.0 - smoothstep(0.03 - aa, 0.03 + aa, abs(lat - 0.8)));
            half halo = (half)max(1.0 - smoothstep(0.03, 0.13, abs(lat - 0.2)), 1.0 - smoothstep(0.03, 0.13, abs(lat - 0.8)));
            half dash = (half)step(frac(u * 1.0 + 0.1), 0.72);
            c = lerp(c, glow * 0.6h, halo * dash * 0.35h);
            c = lerp(c, glow * 1.3h, strips * dash);
            float d = min(frac(u), 1.0 - frac(u));
            c = lerp(c, dk, (half)(1.0 - smoothstep(0.014 - aa, 0.014 + aa, d)) * 0.6h);
        }
        else
        {
            // Engine deck: slatted vents with the furnace glowing through, a thruster ring every 4 units.
            float sl = frac(u * 3.0);
            half vent = (half)(smoothstep(0.14, 0.16, lat) * (1.0 - smoothstep(0.84, 0.86, lat)) * step(0.2, sl) * step(sl, 0.62));
            half3 fur = lerp(dk * 0.6h, accent * 1.25h, (half)smoothstep(0.2, 0.62, sl));
            c = lerp(c, fur, vent);
            float rx = (frac(u / 4.0) - 0.5) * 4.0;
            float rr = length(float2(rx, lat - 0.5));
            half ring = (half)(1.0 - smoothstep(0.03 - aa, 0.03 + aa, abs(rr - 0.3)));
            c = lerp(c, cream * 0.95h, ring * 0.9h);
            c = lerp(c, window * 1.2h, (half)(1.0 - smoothstep(0.05, 0.26, rr)) * 0.8h);
        }

        // Cream paint ridge along the edges: the thick lip of the hull plate.
        c = lerp(c, cream * 0.95h, (half)(1.0 - smoothstep(0.012, 0.05, edge)) * 0.55h);
        c *= tintMax;
    }
    else if (bottom)
    {
        // The underbelly: coral plates, a round glowing thruster vent every 1.5 units.
        float2 p = level ? float2(u, lat) : posOS.xy + 0.5;
        half s = SpBrush(p * float2(2.0, 1.0), key + 40.0);
        c = lerp(_SpBottomDark.rgb, _SpBottom.rgb, SpBands(saturate(s * 1.1h), 3.0h));
        float vx = (frac(p.x / 1.5) - 0.5) * 1.5;
        float r = length(float2(vx, p.y - 0.5));
        c = lerp(c, accent * 1.1h, (half)(1.0 - smoothstep(0.1, 0.42, r)) * 0.75h);
        c = lerp(c, _SpMagenta.rgb * 0.9h, (half)smoothstep(0.38, 0.55, r) * (half)(1.0 - smoothstep(0.55, 0.7, r)) * 0.4h);
        c = lerp(c, cream * 0.95h, (half)(1.0 - smoothstep(0.06, 0.22, r)) * 0.9h);
        c = lerp(c, _SpBottomDark.rgb * 0.5h, (half)(1.0 - smoothstep(0.014 - aa, 0.014 + aa, abs(r - 0.34))) * 0.7h);
        c *= tintMax;
    }
    else
    {
        // ---- the walls ----
        float side = level ? (dot(nOS, latOS) > 0.0 ? 1.0 : 0.0) : 0.0;
        float x = level ? (cap ? lat + 977.0 * sign(nOS.z) : u) : ((an.x > 0.5 ? posOS.y : posOS.x) + 0.5) + (an.x > 0.5 ? 31.0 : 57.0);
        float y = level ? v : -wp.y;
        half s = SpBrush(float2(x * 1.1, y * 0.9), key + 60.0 + side * 9.0);
        half m = SpBands(saturate(s * 1.2h - 0.05h), 3.0h);
        c = lerp(_SpSideDark.rgb, _SpSide.rgb, lerp(0.55h, m, brushAmt));
        half warm = (half)smoothstep(0.6, 0.85, PixNoise(float3(x * 0.6, y * 2.2, key + 3.0)));
        c = lerp(c, c * half3(1.25h, 0.9h, 0.7h), warm * 0.4h);
        c += SpRidge(saturate(s * 1.2h - 0.05h), 3.0h) * 0.04h;

        half hh = (half)PixHash(float3(floor(x), key, side + 1.0));
        if (kind == 0)
        {
            // Ship hull: cream upper plating, a window strip, an orange racing stripe, nose / tail engine glow.
            half upper = (half)smoothstep(0.34, 0.4, y);
            c = lerp(c, cream * lerp(0.78h, 0.98h, k), upper * 0.8h);
            half win = SpBox(float2(frac(x * 1.0) - 0.5, y - 0.66), float2(0.4, 0.09), aa);
            half3 wc = lerp(half3(0.02h, 0.08h, 0.2h), window * 1.1h, (half)(0.4 + 0.6 * PixHash(float3(floor(x), key, 3.0))));
            c = lerp(c, wc, win * step(0.4, y) * (half)(1.0 - step(0.9, y)));
            half stripe = (half)(smoothstep(0.2 - aa, 0.2 + aa, y) * (1.0 - smoothstep(0.3 - aa, 0.3 + aa, y)));
            c = lerp(c, accent, stripe * 0.95h);
            float sx = frac(x / 6.0);
            half eng = (half)(1.0 - smoothstep(0.1, 0.14, sx));
            half3 ec = lerp(accent * 1.2h, window * 1.3h, (half)(0.5 + 0.5 * sin(y * 30.0)));
            c = lerp(c, ec, eng * (half)smoothstep(0.1, 0.18, y) * (1.0h - (half)smoothstep(0.82, 0.9, y)));
        }
        else if (kind == 1)
        {
            // Portholes: a cream frame, warm interior light, a crew silhouette in some.
            float d = abs(y - 0.5);
            c = lerp(c, c * 0.55h, (half)(1.0 - smoothstep(0.012 - aa, 0.012 + aa, d)));
            float px = (frac(x / 1.25) - 0.5) * 1.25;
            float2 q = float2(px, y - 0.62);
            half frame = SpDisc(q, 0.205, aa);
            half hole = SpDisc(q, 0.155, aa);
            c = lerp(c, cream * 0.9h, frame);
            half3 room = lerp(window * 0.45h, window * 1.25h, (half)saturate(0.5 + (0.62 - y) * 3.0));
            room = hh > 0.82h ? lerp(room, glow * 0.9h, 0.7h) : hh < 0.12h ? lerp(room, _SpMagenta.rgb * 1.1h, 0.7h) : room;
            c = lerp(c, room * (half)_SpParams.y, hole);
            half crew = (half)(hh > 0.45h && hh < 0.7h);
            half body = max(SpDisc(q - float2(0.0, 0.02), 0.04, aa), SpBox(q - float2(0.0, -0.1), float2(0.09, 0.06), aa));
            c = lerp(c, half3(0.04h, 0.02h, 0.1h), body * hole * crew);
            // Vertical ribs.
            float rd = min(frac(x / 2.5), 1.0 - frac(x / 2.5)) * 2.5;
            half rib = (half)(1.0 - smoothstep(0.02 - aa, 0.02 + aa, rd));
            c = lerp(c, c * 0.6h, rib * 0.6h);
        }
        else if (kind == 2)
        {
            // Glass corridor: a long window band onto a lit interior with consoles and a ceiling light bar.
            half band = (half)(smoothstep(0.34 - aa, 0.34 + aa, y) * (1.0 - smoothstep(0.82 - aa, 0.82 + aa, y)));
            half3 back = lerp(_SpSideDark.rgb * 0.5h, glow * 0.55h, (half)smoothstep(0.34, 0.8, y) * 0.5h);
            float cx = frac(x) - 0.5;
            half cellH = (half)PixHash(float3(floor(x), key, side + 5.0));
            half3 sc = cellH > 0.66h ? glow : cellH > 0.33h ? _SpMagenta.rgb : accent;
            half scr = SpBox(float2(cx, y - 0.55), float2(0.27, 0.1), aa);
            back = lerp(back, sc * 1.25h, scr);
            back = lerp(back, window * 1.3h, SpBox(float2(cx, y - 0.78), float2(0.34, 0.015), aa));       // ceiling light bar
            back = lerp(back, _SpSide.rgb * 0.7h, (half)(1.0 - smoothstep(0.4, 0.46, y)));                  // the floor
            c = lerp(c, back, band);
            // Cream mullions and frame.
            half mull = (half)(1.0 - smoothstep(0.03 - aa, 0.03 + aa, abs(cx) - 0.47));
            half frameLine = (half)max(1.0 - smoothstep(0.016 - aa, 0.016 + aa, abs(y - 0.34)), 1.0 - smoothstep(0.016 - aa, 0.016 + aa, abs(y - 0.82)));
            c = lerp(c, cream * 0.9h, saturate(max(mull * band, frameLine)));
        }
        else
        {
            // Conduits: three horizontal pipes with cylinder shading and glowing joints.
            [unroll] for (int i = 0; i < 3; i++)
            {
                float yc = 0.2 + 0.3 * i;
                float dy = (y - yc) / 0.1;
                half pipe = (half)(1.0 - smoothstep(1.0 - aa * 8.0, 1.0 + aa * 8.0, abs(dy)));
                if (pipe > 0.0h)
                {
                    half sh = (half)(1.0 - dy * dy * 0.5 - max(-dy, 0.0) * 0.3);
                    half3 pc = (i == 1) ? _SpSide.rgb * 1.25h : accent * 0.85h;
                    pc *= lerp(0.55h, 1.2h, sh);
                    float jx = frac(x / 1.6);
                    half joint = (half)(1.0 - smoothstep(0.03, 0.05, abs(jx - 0.5)));
                    pc = lerp(pc, window * 1.3h, joint * 0.9h);
                    c = lerp(c, pc, pipe);
                }
            }
            half hazard = (half)(step(y, 0.06)) * (half)step(0.5, frac((x + y) * 3.0));
            c = lerp(c, accent * 0.9h, hazard);
        }

        // Cream lip along the top edge (the thick paint ridge), dark shadow line along the bottom.
        c = lerp(c, cream * lerp(0.7h, 1.0h, k), (half)smoothstep(0.93, 0.97, y) * 0.7h);
        c *= lerp(0.7h, 1.0h, (half)smoothstep(0.0, 0.1, y));
        c *= shadeMul;
    }
    return c;
}

#endif
