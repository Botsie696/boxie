using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Spice Route checkpoint's procedural architecture and life, as SpiceMesh kits in local units (base at y = 0,
/// facing +Z). SpiceRouteCheckpointController builds them once and merges a cliff top's worth into one mesh.
///   India    Ganesha (gold / sindoor / marble), Nagara temple (curved shikhara, amalaka, kalasha, flag, porch, sub-spires),
///            gopuram, Rajput hill palace (bastions, chhatris, jharokhas), chhatri, white shrine with a red dome, elephant,
///            peacock with an open fan, coconut palm, banyan, toran bunting
///   Arabia   Ottoman mosque (cascading domes, pencil minarets), palace of onion domes, houses with lit windows, sandstone
///            towers, cypress, date palm, lantern
///   Cliffs   tall single-piece sheer cliffs (no stacking): jagged, ribbed with fissures, strata ledges, a rock-cut temple
///            front on the Indian ones; twins with the same shape in Arabian limestone
/// </summary>
public static class SpiceProps
{
    static Color H(string hex) => SpiceMesh.H(hex);
    static float R(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

    // ------------------------------------------------------------------ India

    /// <summary>Seated Ganesha, ~1 high, facing +Z. variant 0 gold, 1 sindoor orange, 2 white marble.</summary>
    public static SpiceMesh Ganesha(int variant)
    {
        var m = new SpiceMesh();
        Color body = variant == 0 ? H("E0A83A") : variant == 1 ? H("E8662A") : H("F2ECE2");
        int bk = variant == 0 ? SpiceMesh.Gold : variant == 1 ? SpiceMesh.Paint : SpiceMesh.Marble;
        Color gold = H("E8B040"), red = H("D8342A"), marigold = H("F7A21B"), ivory = H("F6EEDC"), lotusPink = H("E87A9A");
        var rng = new System.Random(5 + variant);

        // Lotus pedestal: a petal ring (ribbed lathe) on a round plinth.
        m.Lathe(Vector3.zero, new[] { 0.5f, 0.5f, 0.46f }, new[] { 0f, 0.05f, 0.08f }, 16, 0f, H("C89A6A"), SpiceMesh.Carved);
        m.Lathe(Vector3.zero, new[] { 0.46f, 0.52f, 0.4f }, new[] { 0.08f, 0.13f, 0.17f }, 16, 0f, lotusPink, SpiceMesh.Paint, true, new[] { 1f, 0.82f });
        // Crossed legs, belly, chest, head.
        m.Blob(new Vector3(0f, 0.25f, 0.06f), new Vector3(0.36f, 0.1f, 0.24f), rng, 0.05f, body, bk);
        m.Blob(new Vector3(0f, 0.38f, 0.07f), new Vector3(0.25f, 0.21f, 0.22f), rng, 0.04f, body, bk);
        m.Blob(new Vector3(0f, 0.56f, 0.0f), new Vector3(0.2f, 0.14f, 0.15f), rng, 0.04f, body, bk);
        m.Blob(new Vector3(0f, 0.74f, 0.03f), new Vector3(0.15f, 0.15f, 0.14f), rng, 0.03f, body, bk);
        // Fan ears (flat discs facing forward), with a red inner rim.
        for (int s = -1; s <= 1; s += 2)
        {
            m.Blob(new Vector3(s * 0.2f, 0.74f, -0.02f), new Vector3(0.15f, 0.17f, 0.025f), null, 0f, body, bk, Quaternion.Euler(0f, -s * 12f, s * 8f));
            m.Blob(new Vector3(s * 0.21f, 0.73f, 0.0f), new Vector3(0.1f, 0.12f, 0.02f), null, 0f, H("E87060"), SpiceMesh.Paint, Quaternion.Euler(0f, -s * 12f, s * 8f));
        }
        // Trunk curling to his left, onto a modak.
        m.Tube(new[] { new Vector3(0f, 0.7f, 0.15f), new Vector3(0f, 0.6f, 0.21f), new Vector3(-0.02f, 0.5f, 0.24f), new Vector3(-0.07f, 0.43f, 0.25f), new Vector3(-0.13f, 0.42f, 0.24f), new Vector3(-0.16f, 0.46f, 0.23f) },
               new[] { 0.055f, 0.045f, 0.038f, 0.03f, 0.024f, 0.018f }, 7, body, bk);
        // Tusks: the right one broken short.
        m.Tube(new[] { new Vector3(0.06f, 0.67f, 0.15f), new Vector3(0.08f, 0.62f, 0.2f) }, new[] { 0.018f, 0.004f }, 5, ivory, SpiceMesh.Paint);
        m.Tube(new[] { new Vector3(-0.06f, 0.67f, 0.15f), new Vector3(-0.09f, 0.6f, 0.21f), new Vector3(-0.1f, 0.57f, 0.24f) }, new[] { 0.018f, 0.01f, 0.003f }, 5, ivory, SpiceMesh.Paint);
        // Tilak and eyes.
        m.Box(new Vector3(0f, 0.81f, 0.155f), new Vector3(0.012f, 0.03f, 0.008f), 0f, red, SpiceMesh.Paint);
        m.Box(new Vector3(-0.06f, 0.77f, 0.14f), new Vector3(0.022f, 0.01f, 0.01f), 0f, H("2A1A10"), SpiceMesh.Paint);
        m.Box(new Vector3(0.06f, 0.77f, 0.14f), new Vector3(0.022f, 0.01f, 0.01f), 0f, H("2A1A10"), SpiceMesh.Paint);
        // Tall tiered crown (mukut) and its finial.
        m.Lathe(new Vector3(0f, 0f, 0.0f), new[] { 0.13f, 0.12f, 0.1f, 0.11f, 0.08f, 0.085f, 0.05f, 0.03f, 0f },
                new[] { 0.84f, 0.9f, 0.94f, 0.97f, 1.02f, 1.05f, 1.1f, 1.15f, 1.22f }, 10, 0f, gold, SpiceMesh.Gold, true, new[] { 1f, 0.9f });
        // Halo (prabhavali): an arch of gold behind him.
        var arc = new List<Vector3>();
        var rad = new List<float>();
        for (int i = 0; i <= 12; i++)
        {
            float a = Mathf.Lerp(-15f, 195f, i / 12f) * Mathf.Deg2Rad;
            arc.Add(new Vector3(Mathf.Cos(a) * 0.38f, 0.66f + Mathf.Sin(a) * 0.42f, -0.17f));
            rad.Add(0.03f);
        }
        m.Tube(arc.ToArray(), rad.ToArray(), 5, gold, SpiceMesh.Gold);
        for (int i = 1; i < 12; i += 2) m.Prism(arc[i] + Vector3.up * 0.0f, 5, 0.04f, 0f, 0f, 0.09f, 0f, gold, SpiceMesh.Gold);
        // Four arms: upper ones raised (axe and lotus), lower right in blessing, lower left holding a modak.
        for (int s = -1; s <= 1; s += 2)
        {
            m.Tube(new[] { new Vector3(s * 0.17f, 0.6f, 0f), new Vector3(s * 0.31f, 0.65f, 0.02f), new Vector3(s * 0.34f, 0.78f, 0.05f) }, new[] { 0.05f, 0.04f, 0.035f }, 6, body, bk);
            if (s < 0) m.Lathe(new Vector3(-0.34f, 0.8f, 0.05f), new[] { 0.02f, 0.07f, 0.05f, 0f }, new[] { 0f, 0.03f, 0.07f, 0.09f }, 8, 0f, lotusPink, SpiceMesh.Paint, true, new[] { 1f, 0.6f });
            else m.Box(new Vector3(0.36f, 0.86f, 0.05f), new Vector3(0.012f, 0.09f, 0.012f), 0f, gold, SpiceMesh.Gold);
            if (s > 0) m.Box(new Vector3(0.38f, 0.93f, 0.05f), new Vector3(0.05f, 0.03f, 0.008f), 0f, gold, SpiceMesh.Gold);
            m.Tube(new[] { new Vector3(s * 0.16f, 0.52f, 0.04f), new Vector3(s * 0.24f, 0.44f, 0.12f), new Vector3(s * 0.2f, s > 0 ? 0.56f : 0.4f, 0.2f) }, new[] { 0.045f, 0.04f, 0.035f }, 6, body, bk);
        }
        m.Box(new Vector3(0.2f, 0.6f, 0.21f), new Vector3(0.045f, 0.06f, 0.012f), 0f, body, bk);            // blessing palm
        m.Blob(new Vector3(-0.2f, 0.42f, 0.24f), new Vector3(0.045f, 0.04f, 0.045f), null, 0f, H("F4E2B0"), SpiceMesh.Paint);   // modak
        // Marigold garland and a gold necklace.
        for (int i = 0; i < 11; i++)
        {
            float a = Mathf.Lerp(200f, 340f, i / 10f) * Mathf.Deg2Rad;
            Vector3 gp = new Vector3(Mathf.Cos(a) * 0.17f, 0.6f + Mathf.Sin(a) * 0.08f, 0.1f + Mathf.Sin(a) * -0.05f);
            m.Blob(gp, Vector3.one * 0.028f, null, 0f, i % 3 == 1 ? red : marigold, SpiceMesh.Paint);
        }
        // His mouse, at the front of the pedestal.
        m.Blob(new Vector3(0.24f, 0.21f, 0.32f), new Vector3(0.05f, 0.04f, 0.07f), null, 0f, H("8A7A6A"), SpiceMesh.Paint);
        m.Blob(new Vector3(0.24f, 0.22f, 0.38f), new Vector3(0.025f, 0.025f, 0.03f), null, 0f, H("8A7A6A"), SpiceMesh.Paint);
        // Diyas in front.
        for (int s = -1; s <= 1; s += 2)
        {
            m.Lathe(new Vector3(s * 0.3f, 0.17f, 0.36f), new[] { 0.03f, 0.05f, 0.05f }, new[] { 0f, 0.02f, 0.03f }, 6, 0f, H("B8602A"), SpiceMesh.Paint);
            m.Lathe(new Vector3(s * 0.3f, 0.2f, 0.36f), new[] { 0.015f, 0.012f, 0f }, new[] { 0f, 0.03f, 0.06f }, 5, 0f, H("FFD070"), SpiceMesh.Glow);
        }
        return m;
    }

    static float[] Ribs(int n, float deep) { var r = new float[n]; for (int i = 0; i < n; i++) r[i] = i % 2 == 0 ? 1f : deep; return r; }

    /// <summary>One curved Nagara spire (shikhara) of base radius rb from y0, height h, with its amalaka and kalasha.</summary>
    static void Shikhara(SpiceMesh m, Vector3 c, float rb, float y0, float h, Color stone, Color gold, bool flag)
    {
        var r = new[] { rb, rb * 0.98f, rb * 0.93f, rb * 0.84f, rb * 0.71f, rb * 0.55f, rb * 0.38f };
        var y = new float[r.Length];
        for (int i = 0; i < r.Length; i++) y[i] = y0 + h * Mathf.Pow(i / (float)(r.Length - 1), 0.85f);
        m.Lathe(c, r, y, 16, 0f, stone, SpiceMesh.Carved, false, new[] { 1f, 0.9f, 0.96f, 0.9f });
        float top = y0 + h;
        m.Lathe(c, new[] { rb * 0.36f, rb * 0.48f, rb * 0.36f }, new[] { top, top + rb * 0.1f, top + rb * 0.2f }, 16, 0f, stone, SpiceMesh.Carved, true, Ribs(16, 0.8f));
        float k = top + rb * 0.2f;
        m.Lathe(c, new[] { rb * 0.1f, rb * 0.15f, rb * 0.06f, rb * 0.02f, 0f }, new[] { k, k + rb * 0.12f, k + rb * 0.24f, k + rb * 0.32f, k + rb * 0.5f }, 8, 0f, gold, SpiceMesh.Gold);
        if (flag)
        {
            float py = k + rb * 0.5f;
            m.Box(c + new Vector3(0f, py + rb * 0.45f, 0f), new Vector3(0.015f, rb * 0.45f, 0.015f), 0f, H("6A4A2A"), SpiceMesh.Paint);
            Vector3 f0 = c + new Vector3(0f, py + rb * 0.9f, 0f);
            m.Tri(f0, f0 + new Vector3(rb * 0.75f, -rb * 0.12f, 0f), f0 + new Vector3(0f, -rb * 0.3f, 0f), H("F07A1E"), SpiceMesh.Fabric);
        }
    }

    public static SpiceMesh NagaraTemple(System.Random rng)
    {
        var m = new SpiceMesh();
        Color stone = H(rng.NextDouble() < 0.5 ? "D9946A" : "E0B080"), plinth = H("C88A60"), gold = H("E8B040");
        m.Box(new Vector3(0f, 0.15f, 0.3f), new Vector3(1.7f, 0.15f, 2.0f), 0f, plinth, SpiceMesh.Carved);
        m.Box(new Vector3(0f, 0.4f, 0.3f), new Vector3(1.45f, 0.1f, 1.75f), 0f, plinth, SpiceMesh.Carved);
        // Sanctum and its spire, four smaller spires against it.
        m.Box(new Vector3(0f, 1.1f, -0.3f), new Vector3(0.85f, 0.6f, 0.85f), 0f, stone, SpiceMesh.Carved);
        Shikhara(m, new Vector3(0f, 0f, -0.3f), 0.95f, 1.7f, 2.9f, stone, gold, true);
        for (int i = 0; i < 4; i++)
        {
            Vector3 o = new Vector3(i < 2 ? -0.75f : 0.75f, 0f, (i % 2 == 0 ? -1f : 1f) * 0.75f - 0.3f);
            Shikhara(m, o, 0.36f, 1.7f, 1.1f, stone, gold, false);
        }
        // Porch (mandapa) in front: pillars, a stepped pyramid roof, a small dome; the door glowing.
        m.Box(new Vector3(0f, 0.55f, 1.2f), new Vector3(0.8f, 0.05f, 0.65f), 0f, plinth, SpiceMesh.Carved);
        for (int i = 0; i < 4; i++)
            m.Box(new Vector3(i < 2 ? -0.62f : 0.62f, 0.95f, (i % 2 == 0 ? 0.7f : 1.7f)), new Vector3(0.07f, 0.35f, 0.07f), 0f, stone, SpiceMesh.Carved);
        for (int t = 0; t < 3; t++)
            m.Box(new Vector3(0f, 1.35f + t * 0.17f, 1.2f), new Vector3(0.85f - t * 0.2f, 0.085f, 0.7f - t * 0.17f), 0f, stone, SpiceMesh.Carved);
        m.Dome(new Vector3(0f, 0f, 1.2f), 0.3f, 1.85f, 0.32f, 10, stone, SpiceMesh.Carved);
        m.Lathe(new Vector3(0f, 0f, 1.2f), new[] { 0.05f, 0.07f, 0f }, new[] { 2.17f, 2.25f, 2.4f }, 6, 0f, gold, SpiceMesh.Gold);
        m.Box(new Vector3(0f, 1.0f, 0.56f), new Vector3(0.25f, 0.4f, 0.02f), 0f, H("FFB050"), SpiceMesh.Glow);
        // Steps down the front.
        for (int s = 0; s < 3; s++) m.Box(new Vector3(0f, 0.08f + s * 0.12f, 2.35f - s * 0.15f), new Vector3(0.45f, 0.06f, 0.1f), 0f, plinth, SpiceMesh.Carved);
        return m;
    }

    public static SpiceMesh Gopuram(System.Random rng)
    {
        var m = new SpiceMesh();
        Color[] tiers = { H("F2E6C8"), H("E89A6A"), H("6AC0B0"), H("F2C24A"), H("E87A9A") };
        m.Box(new Vector3(0f, 0.5f, 0f), new Vector3(1.3f, 0.5f, 0.75f), 0f, H("D9B48A"), SpiceMesh.Carved);
        m.Box(new Vector3(0f, 0.38f, 0.76f), new Vector3(0.3f, 0.38f, 0.02f), 0f, H("FFB050"), SpiceMesh.Glow);
        float y = 1f;
        for (int t = 0; t < 5; t++)
        {
            float w = 1.25f - t * 0.17f, d = 0.72f - t * 0.08f, h = 0.42f;
            m.Box(new Vector3(0f, y + h * 0.5f, 0f), new Vector3(w, h * 0.5f, d), 0f, tiers[t % tiers.Length], SpiceMesh.Paint);
            m.Box(new Vector3(0f, y + h + 0.03f, 0f), new Vector3(w + 0.05f, 0.03f, d + 0.05f), 0f, H("C84030"), SpiceMesh.Paint);
            // A row of painted figures along the front and back.
            int figs = 5 - t / 2;
            for (int f = 0; f < figs; f++)
            {
                float fx = Mathf.Lerp(-w * 0.8f, w * 0.8f, figs == 1 ? 0.5f : f / (float)(figs - 1));
                Color fc = tiers[(f + t + 2) % tiers.Length];
                m.Blob(new Vector3(fx, y + h * 0.55f, d + 0.04f), new Vector3(0.07f, 0.14f, 0.05f), rng, 0.15f, fc, SpiceMesh.Paint);
                m.Blob(new Vector3(fx, y + h * 0.55f, -d - 0.04f), new Vector3(0.07f, 0.14f, 0.05f), rng, 0.15f, fc, SpiceMesh.Paint);
            }
            y += h + 0.06f;
        }
        // Barrel-vault crown with five gold pots.
        m.Lathe(new Vector3(0f, 0f, 0f), new[] { 0.32f, 0.36f, 0.3f, 0f }, new[] { y, y + 0.15f, y + 0.3f, y + 0.36f }, 8, 0f, H("E87A9A"), SpiceMesh.Paint, true, null, 0.35f);
        for (int k = 0; k < 5; k++)
            m.Lathe(new Vector3(-0.5f + k * 0.25f, 0f, 0f), new[] { 0.05f, 0.07f, 0.02f, 0f }, new[] { y + 0.25f, y + 0.33f, y + 0.42f, y + 0.5f }, 6, 0f, H("E8B040"), SpiceMesh.Gold);
        return m;
    }

    static void ChhatriAt(SpiceMesh m, Vector3 c, float s, Color stone, Color dome, Color tip)
    {
        m.Box(c + new Vector3(0f, 0.04f * s, 0f), new Vector3(0.32f, 0.04f, 0.32f) * s, 0f, stone, SpiceMesh.Carved);
        for (int i = 0; i < 4; i++)
            m.Box(c + new Vector3((i < 2 ? -0.24f : 0.24f) * s, 0.3f * s, (i % 2 == 0 ? -0.24f : 0.24f) * s), new Vector3(0.035f, 0.25f, 0.035f) * s, 0f, stone, SpiceMesh.Carved);
        m.Box(c + new Vector3(0f, 0.58f * s, 0f), new Vector3(0.36f, 0.04f, 0.36f) * s, 0f, stone, SpiceMesh.Carved);
        m.Dome(c, 0.27f * s, 0.62f * s, 0.3f * s, 10, dome, SpiceMesh.Paint);
        m.Lathe(c, new[] { 0.03f * s, 0.05f * s, 0f }, new[] { 0.9f * s, 0.96f * s, 1.08f * s }, 6, 0f, tip, SpiceMesh.Gold);
    }

    public static SpiceMesh Chhatri(System.Random rng)
    {
        var m = new SpiceMesh();
        ChhatriAt(m, Vector3.zero, 1.4f, H("E8C090"), H(rng.NextDouble() < 0.5 ? "F4EEE4" : "E0A070"), H("E8B040"));
        return m;
    }

    public static SpiceMesh RajputPalace(System.Random rng)
    {
        var m = new SpiceMesh();
        Color wall = H(rng.NextDouble() < 0.5 ? "E8A090" : "EEC08A"), white = H("F4EEE4"), red = H("C84030"), gold = H("E8B040");
        m.Box(new Vector3(0f, 0.7f, 0f), new Vector3(1.6f, 0.7f, 1.0f), 0f, wall, SpiceMesh.Paint);
        for (int i = -7; i <= 7; i++)
        {
            m.Box(new Vector3(i * 0.21f, 1.47f, 1.0f), new Vector3(0.06f, 0.07f, 0.04f), 0f, wall, SpiceMesh.Paint);
            m.Box(new Vector3(i * 0.21f, 1.47f, -1.0f), new Vector3(0.06f, 0.07f, 0.04f), 0f, wall, SpiceMesh.Paint);
        }
        // Upper storey with arched windows glowing, jharokha balconies with little domes.
        m.Box(new Vector3(0.2f, 1.75f, -0.15f), new Vector3(1.0f, 0.35f, 0.65f), 0f, white, SpiceMesh.Paint);
        for (int w = -3; w <= 3; w++)
        {
            m.Box(new Vector3(0.2f + w * 0.26f, 1.75f, 0.51f), new Vector3(0.06f, 0.12f, 0.01f), 0f, H("FFB860"), SpiceMesh.Glow);
            m.Box(new Vector3(w * 0.42f, 0.8f, 1.01f), new Vector3(0.08f, 0.16f, 0.01f), 0f, H("FFB860"), SpiceMesh.Glow);
        }
        for (int j = -1; j <= 1; j++)
        {
            Vector3 jc = new Vector3(j * 0.9f, 1.0f, 1.12f);
            m.Box(jc, new Vector3(0.18f, 0.16f, 0.12f), 0f, white, SpiceMesh.Paint);
            m.Box(jc + new Vector3(0f, -0.2f, 0f), new Vector3(0.2f, 0.04f, 0.14f), 0f, wall, SpiceMesh.Carved);
            m.Dome(jc + new Vector3(0f, 0.16f, 0f), 0.17f, 0f, 0.16f, 8, red, SpiceMesh.Paint);
        }
        // Corner bastions with chhatris.
        for (int b = 0; b < 4; b++)
        {
            Vector3 bc = new Vector3(b < 2 ? -1.6f : 1.6f, 0f, b % 2 == 0 ? -1.0f : 1.0f);
            m.Prism(bc, 10, 0.38f, 0.33f, 0f, 1.7f, 0f, wall, SpiceMesh.Paint);
            ChhatriAt(m, bc + Vector3.up * 1.7f, 0.9f, white, b % 2 == 0 ? red : white, gold);
        }
        ChhatriAt(m, new Vector3(0.2f, 2.1f, -0.15f), 1.2f, white, red, gold);
        return m;
    }

    public static SpiceMesh WhiteShrine(System.Random rng)
    {
        var m = new SpiceMesh();
        Color white = H("F4EEE4"), red = H("C84030");
        float w = R(rng, 0.45f, 0.7f), h = R(rng, 0.6f, 1.0f);
        m.Box(new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h * 0.5f, w * 0.85f), 0f, white, SpiceMesh.Paint);
        m.Box(new Vector3(0f, h + 0.04f, 0f), new Vector3(w + 0.05f, 0.04f, w * 0.85f + 0.05f), 0f, red, SpiceMesh.Paint);
        m.Dome(new Vector3(0f, 0f, 0f), w * 0.6f, h + 0.08f, w * 0.7f, 10, red, SpiceMesh.Paint);
        m.Box(new Vector3(0f, h * 0.35f, w * 0.86f), new Vector3(0.1f, h * 0.3f, 0.01f), 0f, H("FFB050"), SpiceMesh.Glow);
        return m;
    }

    public static SpiceMesh Elephant(System.Random rng)
    {
        var m = new SpiceMesh();
        Color hide = H("8A8494"), red = H("C8322C"), gold = H("E8B040");
        m.Blob(new Vector3(0f, 0.62f, 0f), new Vector3(0.36f, 0.3f, 0.52f), rng, 0.05f, hide, SpiceMesh.Paint);
        m.Blob(new Vector3(0f, 0.78f, 0.52f), new Vector3(0.22f, 0.22f, 0.2f), rng, 0.05f, hide, SpiceMesh.Paint);
        for (int s = -1; s <= 1; s += 2)
            m.Blob(new Vector3(s * 0.2f, 0.78f, 0.42f), new Vector3(0.04f, 0.2f, 0.17f), null, 0f, hide, SpiceMesh.Paint);
        m.Tube(new[] { new Vector3(0f, 0.72f, 0.68f), new Vector3(0f, 0.5f, 0.74f), new Vector3(0f, 0.25f, 0.76f), new Vector3(0f, 0.1f, 0.82f) }, new[] { 0.07f, 0.055f, 0.045f, 0.035f }, 6, hide, SpiceMesh.Paint);
        for (int l = 0; l < 4; l++)
            m.Prism(new Vector3(l < 2 ? -0.18f : 0.18f, 0f, l % 2 == 0 ? -0.3f : 0.3f), 6, 0.1f, 0.1f, 0f, 0.45f, 0f, hide, SpiceMesh.Paint);
        // Caparison: a red blanket with a gold border, a gold headdress.
        m.Box(new Vector3(0f, 0.86f, 0f), new Vector3(0.38f, 0.06f, 0.36f), 0f, red, SpiceMesh.Fabric);
        m.Box(new Vector3(0f, 0.68f, 0f), new Vector3(0.39f, 0.16f, 0.3f), 0f, red, SpiceMesh.Fabric);
        m.Box(new Vector3(0f, 0.52f, 0f), new Vector3(0.395f, 0.025f, 0.31f), 0f, gold, SpiceMesh.Gold);
        m.Box(new Vector3(0f, 0.92f, 0.6f), new Vector3(0.1f, 0.08f, 0.1f), 0f, gold, SpiceMesh.Gold);
        if (rng.NextDouble() < 0.5)
        {
            m.Box(new Vector3(0f, 1.02f, -0.02f), new Vector3(0.2f, 0.1f, 0.22f), 0f, gold, SpiceMesh.Gold);
            m.Dome(new Vector3(0f, 0f, -0.02f), 0.18f, 1.12f, 0.2f, 8, red, SpiceMesh.Paint, 0.3f);
        }
        return m;
    }

    public static SpiceMesh Peacock(System.Random rng)
    {
        var m = new SpiceMesh();
        Color blue = H("1E5AA8"), green = H("2E8A6A"), gold = H("E8B040");
        m.Blob(new Vector3(0f, 0.35f, 0.05f), new Vector3(0.14f, 0.13f, 0.22f), rng, 0.05f, blue, SpiceMesh.Paint);
        m.Tube(new[] { new Vector3(0f, 0.4f, 0.18f), new Vector3(0f, 0.55f, 0.22f), new Vector3(0f, 0.68f, 0.24f) }, new[] { 0.05f, 0.04f, 0.035f }, 6, blue, SpiceMesh.Paint);
        m.Blob(new Vector3(0f, 0.72f, 0.26f), new Vector3(0.05f, 0.05f, 0.06f), null, 0f, blue, SpiceMesh.Paint);
        m.Prism(new Vector3(0f, 0.72f, 0.32f), 4, 0.015f, 0f, 0f, 0.05f, 0f, H("C8A070"), SpiceMesh.Paint);
        for (int c = -1; c <= 1; c++) m.Box(new Vector3(c * 0.015f, 0.8f, 0.25f), new Vector3(0.004f, 0.04f, 0.004f), 0f, blue, SpiceMesh.Paint);
        for (int l = -1; l <= 1; l += 2) m.Box(new Vector3(l * 0.05f, 0.11f, 0.05f), new Vector3(0.012f, 0.11f, 0.012f), 0f, H("8A7A5A"), SpiceMesh.Paint);
        // The fan: feathers radiating behind, upright.
        int n = 15;
        Vector3 root = new Vector3(0f, 0.38f, -0.12f);
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Lerp(10f, 170f, i / (float)(n - 1)) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), -0.15f).normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.forward).normalized * 0.075f;
            Vector3 tip = root + dir * 0.85f;
            m.Tri(root, tip + side, tip - side, green, SpiceMesh.Feather);
            m.Tri(tip - side, tip + side, tip + dir * 0.06f, gold, SpiceMesh.Feather);
        }
        return m;
    }

    public static SpiceMesh Palm(System.Random rng, bool date)
    {
        var m = new SpiceMesh();
        Color trunk = H(date ? "9A7A50" : "8A6A4A"), leaf = H(date ? "5A8A3A" : "3E8A4A");
        float h = R(rng, 2.4f, 3.4f), lean = R(rng, -0.4f, 0.4f);
        var pts = new Vector3[6];
        var rad = new float[6];
        for (int i = 0; i < 6; i++)
        {
            float f = i / 5f;
            pts[i] = new Vector3(lean * f * f, h * f, 0f);
            rad[i] = Mathf.Lerp(0.1f, 0.06f, f);
        }
        m.Tube(pts, rad, 6, trunk, SpiceMesh.Paint);
        Vector3 top = pts[5];
        int fronds = 8;
        for (int i = 0; i < fronds; i++)
        {
            float a = i * 360f / fronds + R(rng, -10f, 10f);
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            Vector3 side = Quaternion.Euler(0f, a + 90f, 0f) * Vector3.forward;
            Vector3 p0 = top;
            float len = R(rng, 1.0f, 1.4f);
            for (int s = 0; s < 4; s++)
            {
                float f0 = s / 4f, f1 = (s + 1) / 4f;
                Vector3 q0 = top + dir * len * f0 + Vector3.up * (0.35f * Mathf.Sin(f0 * 2.4f) - 0.5f * f0 * f0);
                Vector3 q1 = top + dir * len * f1 + Vector3.up * (0.35f * Mathf.Sin(f1 * 2.4f) - 0.5f * f1 * f1);
                float w0 = 0.22f * (1f - f0), w1 = 0.22f * (1f - f1);
                m.Quad(q0 - side * w0, q1 - side * w1, q1 + side * w1, q0 + side * w0, leaf, SpiceMesh.Foliage);
            }
        }
        if (!date) for (int k = 0; k < 3; k++) m.Blob(top + Quaternion.Euler(0f, k * 120f, 0f) * Vector3.forward * 0.1f - Vector3.up * 0.1f, Vector3.one * 0.07f, null, 0f, H("6A5A2A"), SpiceMesh.Paint);
        else m.Blob(top - Vector3.up * 0.15f, new Vector3(0.18f, 0.12f, 0.18f), rng, 0.3f, H("C8782A"), SpiceMesh.Paint);
        return m;
    }

    public static SpiceMesh Banyan(System.Random rng)
    {
        var m = new SpiceMesh();
        Color bark = H("7A5A42"), leaf = H(rng.NextDouble() < 0.5 ? "2E6A3A" : "3A7A3A");
        m.Prism(Vector3.zero, 6, 0.22f, 0.16f, 0f, 1.2f, 0f, bark, SpiceMesh.Paint);
        for (int i = 0; i < 4; i++)
        {
            Vector3 o = Quaternion.Euler(0f, i * 90f + 30f, 0f) * Vector3.forward * 0.7f;
            m.Prism(o, 5, 0.04f, 0.04f, 0f, 1.3f, 0f, bark, SpiceMesh.Paint);   // aerial roots
        }
        m.Blob(new Vector3(0f, 1.6f, 0f), new Vector3(1.2f, 0.6f, 1.1f), rng, 0.2f, leaf, SpiceMesh.Foliage);
        m.Blob(new Vector3(0.5f, 2.0f, 0.2f), new Vector3(0.7f, 0.45f, 0.7f), rng, 0.25f, leaf, SpiceMesh.Foliage);
        m.Blob(new Vector3(-0.5f, 1.9f, -0.2f), new Vector3(0.7f, 0.45f, 0.6f), rng, 0.25f, leaf, SpiceMesh.Foliage);
        return m;
    }

    public static SpiceMesh Bunting(System.Random rng)
    {
        var m = new SpiceMesh();
        Color pole = H("6A4A2A");
        Color[] flags = { H("F07A1E"), H("E0457B"), H("2E8B57"), H("D8342A"), H("F4E6C8"), H("2B4C9A") };
        float span = R(rng, 2.2f, 3.2f);
        m.Box(new Vector3(-span * 0.5f, 0.8f, 0f), new Vector3(0.03f, 0.8f, 0.03f), 0f, pole, SpiceMesh.Paint);
        m.Box(new Vector3(span * 0.5f, 0.8f, 0f), new Vector3(0.03f, 0.8f, 0.03f), 0f, pole, SpiceMesh.Paint);
        int n = 9;
        for (int i = 0; i < n; i++)
        {
            float f0 = (i + 0.15f) / n, f1 = (i + 0.85f) / n;
            float x0 = Mathf.Lerp(-span * 0.5f, span * 0.5f, f0), x1 = Mathf.Lerp(-span * 0.5f, span * 0.5f, f1);
            float y0 = 1.55f - 0.35f * Mathf.Sin(f0 * Mathf.PI), y1 = 1.55f - 0.35f * Mathf.Sin(f1 * Mathf.PI);
            Vector3 a = new Vector3(x0, y0, 0f), b = new Vector3(x1, y1, 0f), c = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f - 0.32f, 0f);
            m.Tri(a, b, c, flags[i % flags.Length], SpiceMesh.Fabric);
        }
        return m;
    }

    // ------------------------------------------------------------------ Arabia

    static void Minaret(SpiceMesh m, Vector3 c, float h, float r, Color stone, Color cap, Color gold)
    {
        m.Prism(c, 12, r * 1.3f, r * 1.2f, 0f, h * 0.12f, 0f, stone, SpiceMesh.Paint);
        m.Prism(c, 12, r, r * 0.9f, h * 0.12f, h, 0f, stone, SpiceMesh.Paint);
        foreach (float b in new[] { 0.62f, 0.84f })
        {
            m.Prism(c, 12, r * 1.6f, r * 1.6f, h * b - 0.06f, h * b + 0.04f, 0f, stone, SpiceMesh.Carved);
            m.Box(c + Vector3.up * (h * b + 0.18f), new Vector3(r * 0.95f, 0.08f, r * 0.95f), 0f, H("FFC060"), SpiceMesh.Glow);
        }
        m.Lathe(c, new[] { r * 1.05f, 0f }, new[] { h, h + h * 0.22f }, 12, 0f, cap, SpiceMesh.Tiles);
        m.Lathe(c, new[] { 0.02f, 0.04f, 0.02f, 0.03f, 0f }, new[] { h * 1.22f, h * 1.24f, h * 1.27f, h * 1.29f, h * 1.33f }, 6, 0f, gold, SpiceMesh.Gold);
    }

    public static SpiceMesh Mosque(System.Random rng)
    {
        var m = new SpiceMesh();
        Color stone = H(rng.NextDouble() < 0.5 ? "EADFCB" : "E3CFAE"), lead = H(rng.NextDouble() < 0.6 ? "7E8CA0" : "2A9AA8"), gold = H("E8B040"), glow = H("FFB860");
        m.Box(new Vector3(0f, 0.7f, 0f), new Vector3(1.5f, 0.7f, 1.5f), 0f, stone, SpiceMesh.Paint);
        for (int s = 0; s < 4; s++)
        {
            Quaternion q = Quaternion.Euler(0f, s * 90f, 0f);
            for (int w = -2; w <= 2; w++)
            {
                m.Box(q * new Vector3(w * 0.52f, 0.5f, 1.51f), new Vector3(0.1f, 0.16f, 0.01f), s * 90f, glow, SpiceMesh.Glow);
                m.Box(q * new Vector3(w * 0.52f, 1.05f, 1.51f), new Vector3(0.07f, 0.1f, 0.01f), s * 90f, glow, SpiceMesh.Glow);
            }
        }
        // Drum, main dome, cascading half-domes, corner domes.
        m.Prism(Vector3.zero, 16, 1.0f, 1.0f, 1.4f, 1.85f, 0f, stone, SpiceMesh.Paint);
        for (int w = 0; w < 12; w++)
        {
            Quaternion q = Quaternion.Euler(0f, w * 30f, 0f);
            m.Box(q * new Vector3(0f, 1.62f, 1.0f), new Vector3(0.06f, 0.1f, 0.01f), w * 30f, glow, SpiceMesh.Glow);
        }
        m.Dome(Vector3.zero, 1.05f, 1.85f, 1.0f, 16, lead, SpiceMesh.Tiles);
        m.Lathe(Vector3.zero, new[] { 0.04f, 0.08f, 0.03f, 0.06f, 0f }, new[] { 2.85f, 2.92f, 3.0f, 3.06f, 3.2f }, 6, 0f, gold, SpiceMesh.Gold);
        for (int s = 0; s < 4; s++)
        {
            Vector3 hc = Quaternion.Euler(0f, s * 90f, 0f) * new Vector3(0f, 0f, 1.05f);
            m.Dome(hc, 0.55f, 1.4f, 0.55f, 12, lead, SpiceMesh.Tiles);
            Vector3 cc = new Vector3(s < 2 ? -1.1f : 1.1f, 0f, s % 2 == 0 ? -1.1f : 1.1f);
            m.Dome(cc, 0.32f, 1.4f, 0.32f, 10, lead, SpiceMesh.Tiles);
        }
        // Pencil minarets: two or four.
        int mins = rng.NextDouble() < 0.5 ? 2 : 4;
        for (int i = 0; i < mins; i++)
        {
            Vector3 mc = mins == 2 ? new Vector3(i == 0 ? -1.85f : 1.85f, 0f, 1.85f) : new Vector3(i < 2 ? -1.85f : 1.85f, 0f, i % 2 == 0 ? -1.85f : 1.85f);
            Minaret(m, mc, R(rng, 4.6f, 5.6f), 0.15f, stone, lead, gold);
        }
        return m;
    }

    public static SpiceMesh OnionPalace(System.Random rng)
    {
        var m = new SpiceMesh();
        Color wall = H(rng.NextDouble() < 0.5 ? "C8A070" : "D9B88A"), teal = H(rng.NextDouble() < 0.5 ? "2A7A8A" : "3A5AA8"), gold = H("E8B040"), glow = H("FFB050");
        m.Box(new Vector3(0f, 0.6f, 0f), new Vector3(1.6f, 0.6f, 1.2f), 0f, wall, SpiceMesh.Carved);
        for (int i = -7; i <= 7; i++)
        {
            m.Box(new Vector3(i * 0.22f, 1.27f, 1.2f), new Vector3(0.07f, 0.07f, 0.04f), 0f, wall, SpiceMesh.Carved);
            m.Box(new Vector3(i * 0.22f, 1.27f, -1.2f), new Vector3(0.07f, 0.07f, 0.04f), 0f, wall, SpiceMesh.Carved);
        }
        m.Box(new Vector3(0f, 0.42f, 1.21f), new Vector3(0.28f, 0.42f, 0.01f), 0f, glow, SpiceMesh.Glow);
        // Central tower and onion dome, four towers round it.
        m.Prism(new Vector3(0f, 0f, -0.1f), 12, 0.65f, 0.6f, 1.2f, 2.9f, 0f, wall, SpiceMesh.Paint);
        for (int w = 0; w < 6; w++) m.Box(Quaternion.Euler(0f, w * 60f, 0f) * new Vector3(0f, 2.3f, 0.62f) + new Vector3(0f, 0f, -0.1f), new Vector3(0.07f, 0.16f, 0.01f), w * 60f, glow, SpiceMesh.Glow);
        m.Dome(new Vector3(0f, 0f, -0.1f), 0.72f, 2.9f, 1.6f, 14, teal, SpiceMesh.Tiles, 0.4f, 8);
        m.Lathe(new Vector3(0f, 0f, -0.1f), new[] { 0.03f, 0.06f, 0f }, new[] { 4.5f, 4.6f, 4.9f }, 6, 0f, gold, SpiceMesh.Gold);
        for (int t = 0; t < 4; t++)
        {
            Vector3 tc = new Vector3(t < 2 ? -1.35f : 1.35f, 0f, t % 2 == 0 ? -0.95f : 0.95f);
            float th = R(rng, 1.8f, 2.5f);
            m.Prism(tc, 10, 0.32f, 0.28f, 0f, th, 0f, wall, SpiceMesh.Paint);
            m.Box(tc + new Vector3(0f, th * 0.7f, 0.29f), new Vector3(0.05f, 0.1f, 0.01f), 0f, glow, SpiceMesh.Glow);
            m.Dome(tc, 0.36f, th, 0.8f, 10, teal, SpiceMesh.Tiles, 0.4f, 7);
            m.Lathe(tc, new[] { 0.02f, 0.04f, 0f }, new[] { th + 0.8f, th + 0.86f, th + 1.02f }, 5, 0f, gold, SpiceMesh.Gold);
        }
        return m;
    }

    public static SpiceMesh House(System.Random rng)
    {
        var m = new SpiceMesh();
        string[] cols = { "E8D8B8", "D9B48A", "C98A6A", "F0E6D2", "B8C8D0", "E8C0A0", "D8A0A0", "A8B8A0" };
        Color wall = H(cols[rng.Next(cols.Length)]), glow = H(rng.NextDouble() < 0.5 ? "FFB050" : "FFD080"), wood = H("8A5A3A");
        float w = R(rng, 0.6f, 1.1f), d = R(rng, 0.5f, 0.9f), h = R(rng, 0.8f, 2.4f);
        m.Box(new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h * 0.5f, d), 0f, wall, SpiceMesh.Paint);
        // Parapet.
        m.Box(new Vector3(0f, h + 0.05f, d), new Vector3(w, 0.05f, 0.03f), 0f, wall, SpiceMesh.Paint);
        m.Box(new Vector3(0f, h + 0.05f, -d), new Vector3(w, 0.05f, 0.03f), 0f, wall, SpiceMesh.Paint);
        m.Box(new Vector3(w, h + 0.05f, 0f), new Vector3(0.03f, 0.05f, d), 0f, wall, SpiceMesh.Paint);
        m.Box(new Vector3(-w, h + 0.05f, 0f), new Vector3(0.03f, 0.05f, d), 0f, wall, SpiceMesh.Paint);
        double kind = rng.NextDouble();
        if (kind < 0.3) m.Dome(new Vector3(R(rng, -w * 0.4f, w * 0.4f), 0f, 0f), Mathf.Min(w, d) * 0.5f, h, Mathf.Min(w, d) * 0.5f, 10, H(rng.NextDouble() < 0.5 ? "F0E6D2" : "6A9AB0"), SpiceMesh.Paint);
        else if (kind < 0.55) m.Box(new Vector3(0f, h * 0.8f, d + 0.12f), new Vector3(w * 0.6f, h * 0.15f, 0.12f), 0f, wood, SpiceMesh.Paint);   // an oriel
        // Lit windows.
        int rows = Mathf.Max(1, Mathf.RoundToInt(h / 0.55f));
        for (int r = 0; r < rows; r++)
            for (int c = -1; c <= 1; c++)
            {
                if (rng.NextDouble() < 0.35) continue;
                float wy = (r + 0.6f) * h / (rows + 0.3f);
                m.Box(new Vector3(c * w * 0.55f, wy, d + 0.01f), new Vector3(0.07f, 0.1f, 0.01f), 0f, glow, SpiceMesh.Glow);
                if (rng.NextDouble() < 0.6) m.Box(new Vector3(w + 0.01f, wy, c * d * 0.5f), new Vector3(0.01f, 0.1f, 0.07f), 0f, glow, SpiceMesh.Glow);
            }
        return m;
    }

    public static SpiceMesh SandTower(System.Random rng)
    {
        var m = new SpiceMesh();
        Color stone = H(rng.NextDouble() < 0.5 ? "E3C79A" : "EAD6B0"), dome = H(rng.NextDouble() < 0.5 ? "8A6AB0" : "D85A3A"), glow = H("FFC070");
        float h = R(rng, 3.2f, 4.6f), r = R(rng, 0.45f, 0.6f);
        m.Lathe(Vector3.zero, new[] { r * 1.1f, r, r * 0.95f, r * 0.9f }, new[] { 0f, h * 0.3f, h * 0.7f, h }, 14, 0f, stone, SpiceMesh.Paint);
        m.Dome(Vector3.zero, r * 0.92f, h, r * 1.1f, 12, dome, SpiceMesh.Tiles);
        for (int i = 0; i < 5; i++)
        {
            Quaternion q = Quaternion.Euler(0f, i * 72f + R(rng, -10f, 10f), 0f);
            float wy = R(rng, 0.4f, h - 0.4f);
            m.Box(q * new Vector3(0f, wy, r * 0.97f), new Vector3(0.08f, 0.08f, 0.02f), i * 72f, glow, SpiceMesh.Glow);
        }
        m.Prism(Vector3.zero, 14, r * 1.25f, r * 1.25f, h * 0.62f, h * 0.65f, 0f, stone, SpiceMesh.Carved);
        return m;
    }

    public static SpiceMesh Cypress(System.Random rng)
    {
        var m = new SpiceMesh();
        float h = R(rng, 1.6f, 2.6f);
        m.Prism(Vector3.zero, 5, 0.06f, 0.05f, 0f, 0.3f, 0f, H("5A4030"), SpiceMesh.Paint);
        m.Blob(new Vector3(0f, 0.25f + h * 0.5f, 0f), new Vector3(0.25f, h * 0.5f, 0.25f), rng, 0.1f, H("2E4A3A"), SpiceMesh.Foliage);
        return m;
    }

    public static SpiceMesh Lantern(System.Random rng)
    {
        var m = new SpiceMesh();
        m.Box(new Vector3(0f, 0.6f, 0f), new Vector3(0.025f, 0.6f, 0.025f), 0f, H("3A2A20"), SpiceMesh.Paint);
        m.Box(new Vector3(0.12f, 1.18f, 0f), new Vector3(0.12f, 0.015f, 0.015f), 0f, H("3A2A20"), SpiceMesh.Paint);
        m.Lathe(new Vector3(0.22f, 0f, 0f), new[] { 0.04f, 0.09f, 0.07f, 0f }, new[] { 0.9f, 1.0f, 1.1f, 1.18f }, 8, 0f, H(rng.NextDouble() < 0.5 ? "FFB040" : "FF8A50"), SpiceMesh.Glow);
        return m;
    }

    // ------------------------------------------------------------------ cliffs

    public class Cliff
    {
        public SpiceMesh india, arabia;
        public List<Vector4> tops = new List<Vector4>();   // local x, z, top y, 1
        public bool pedestal;                              // a colossus stands on it (India) / a landmark (Arabia)
        public Vector3 anchor;                             // local centre of the top
        public float topRadius;
    }

    /// <summary>A tall canyon cliff (the reference's sheer walls), unit footprint, base at y = 0, `height` high: three
    /// blocky jagged tiers stepping back into terraces, olive greenery on every ledge, and life built onto it - in India
    /// carved balconies with domed roofs, white shrines with red domes and a rock-cut temple front; in Arabia houses
    /// cantilevered off the faces, small domes and lit windows. Both twins share the rock (same random numbers for the
    /// shape); only the buildings differ.</summary>
    public static Cliff BuildCliff(int seed, float height, bool pedestal)
    {
        var cl = new Cliff { pedestal = pedestal };
        for (int pass = 0; pass < 2; pass++)
        {
            var rng = new System.Random(seed);
            var det = new System.Random(seed * 31 + pass * 7 + 3);
            var m = new SpiceMesh();
            bool arab = pass == 1;
            Color rock = arab ? H("EADBC0") : H("EDB892");
            Color rockDeep = arab ? H("DCC6A2") : H("E0A07C");
            Color green = arab ? H("A8A060") : H("8AA83A");
            float[] tierR = pedestal ? new[] { 0.46f, 0.39f, 0.3f } : new[] { 0.47f, 0.41f, 0.34f };
            float[] tierY = { 0f, height * (0.52f + 0.06f * (float)rng.NextDouble()), height * (0.8f + 0.05f * (float)rng.NextDouble()), height };
            var centres = new Vector3[3];
            for (int k = 0; k < 3; k++)
            {
                int sides = 7 + rng.Next(3);
                var jag = new float[sides];
                for (int i = 0; i < sides; i++) jag[i] = 0.8f + 0.28f * (float)rng.NextDouble();
                // Each tier set back a little off centre, like eroded terraces.
                centres[k] = k == 0 ? Vector3.zero : centres[k - 1] + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.05f, 0f, ((float)rng.NextDouble() - 0.5f) * 0.05f);
                float y0 = k == 0 ? 0f : tierY[k] - 0.02f;
                m.Lathe(centres[k], new[] { tierR[k], tierR[k] * 0.985f, tierR[k] }, new[] { y0, (y0 + tierY[k + 1]) * 0.5f, tierY[k + 1] }, sides,
                        (float)rng.NextDouble() * 360f, k == 1 ? rockDeep : rock, SpiceMesh.Rock, k == 0, jag);
            }
            // Greenery on the ledges and the top rim, moss trailing down under each ledge.
            for (int k = 0; k < 3; k++)
            {
                float yl = tierY[k + 1];
                float rin = k < 2 ? tierR[k + 1] : 0f, rout = tierR[k] * 0.9f;
                int tufts = k < 2 ? 9 : 6;
                for (int g = 0; g < tufts; g++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float rr = Mathf.Lerp(Mathf.Max(rin * 1.05f, rout * 0.6f), rout, (float)rng.NextDouble());
                    Vector3 gp = centres[k] + new Vector3(Mathf.Cos(a) * rr, yl, Mathf.Sin(a) * rr);
                    m.Blob(gp, new Vector3(0.035f, 0.015f, 0.03f), rng, 0.3f, green, SpiceMesh.Foliage);
                }
                for (int g = 0; g < 3; g++)
                {
                    float a = (float)rng.NextDouble() * 360f;
                    Quaternion q = Quaternion.Euler(0f, a, 0f);
                    float rr = tierR[k] * 0.93f;
                    float len = 0.12f + 0.15f * (float)rng.NextDouble();
                    m.Box(centres[k] + q * new Vector3(0f, yl - len * 0.5f, rr), new Vector3(0.025f, len * 0.5f, 0.012f), a, green, SpiceMesh.Foliage);
                }
            }
            // Buildings clinging to the faces.
            int clinging = 6 + det.Next(4);
            for (int b = 0; b < clinging; b++)
            {
                int k = det.Next(2);
                float a = (float)det.NextDouble() * 360f;
                Quaternion q = Quaternion.Euler(0f, a, 0f);
                float y = Mathf.Lerp(tierY[k] + 0.15f, tierY[k + 1] - 0.08f, (float)det.NextDouble());
                if (k == 0 && y < height * 0.25f) y += height * 0.2f;
                float rr = tierR[k] * 0.86f;
                Vector3 face = centres[k] + q * new Vector3(0f, y, rr);
                float w = 0.04f + 0.04f * (float)det.NextDouble(), h = 0.05f + 0.06f * (float)det.NextDouble(), d = 0.05f + 0.03f * (float)det.NextDouble();
                if (!arab)
                {
                    // A carved balcony (jharokha) with a little dome, glowing inside.
                    m.Box(face + q * new Vector3(0f, 0f, d * 0.5f), new Vector3(w, h * 0.5f, d * 0.5f), a, H("F0D2B0"), SpiceMesh.Carved);
                    m.Box(face + q * new Vector3(0f, -h * 0.5f - 0.008f, d * 0.55f), new Vector3(w * 1.1f, 0.01f, d * 0.6f), a, H("C88A60"), SpiceMesh.Carved);
                    m.Box(face + q * new Vector3(0f, -0.005f, d + 0.002f), new Vector3(w * 0.5f, h * 0.3f, 0.004f), a, H("FFB860"), SpiceMesh.Glow);
                    m.Dome(face + q * new Vector3(0f, 0f, d * 0.5f), w * 0.9f, h * 0.5f, w * 0.8f, 8, det.NextDouble() < 0.5 ? H("C84030") : H("F4EEE4"), SpiceMesh.Paint);
                }
                else
                {
                    // A house cantilevered off the wall: pale plaster, two lit windows, a beam under it, sometimes a dome.
                    string[] cols = { "F0E6D2", "E8C0A0", "D8A0A0", "B8C8D0", "E8D8B8" };
                    Color hc = H(cols[det.Next(cols.Length)]);
                    m.Box(face + q * new Vector3(0f, 0f, d * 0.5f), new Vector3(w, h * 0.5f, d * 0.5f), a, hc, SpiceMesh.Paint);
                    m.Box(face + q * new Vector3(-w * 0.45f, 0.005f, d + 0.002f), new Vector3(w * 0.2f, h * 0.18f, 0.004f), a, H("FFC070"), SpiceMesh.Glow);
                    m.Box(face + q * new Vector3(w * 0.45f, 0.005f, d + 0.002f), new Vector3(w * 0.2f, h * 0.18f, 0.004f), a, H("FFC070"), SpiceMesh.Glow);
                    m.Box(face + q * new Vector3(0f, -h * 0.5f - 0.006f, d * 0.5f), new Vector3(0.006f, 0.006f, d * 0.5f), a, H("6A4A2A"), SpiceMesh.Paint);
                    if (det.NextDouble() < 0.4) m.Dome(face + q * new Vector3(0f, 0f, d * 0.5f), Mathf.Min(w, d) * 0.6f, h * 0.5f, Mathf.Min(w, d) * 0.6f, 8, H(det.NextDouble() < 0.5 ? "4A8AA8" : "F0E6D2"), SpiceMesh.Tiles);
                }
            }
            // On the two ledges: a few small buildings.
            for (int k = 0; k < 2; k++)
            {
                int n = 2 + det.Next(3);
                for (int b = 0; b < n; b++)
                {
                    float a = (float)det.NextDouble() * Mathf.PI * 2f;
                    float rr = Mathf.Lerp(tierR[k + 1] * 1.08f, tierR[k] * 0.86f, (float)det.NextDouble());
                    Vector3 p = centres[k] + new Vector3(Mathf.Cos(a) * rr, tierY[k + 1], Mathf.Sin(a) * rr);
                    float s = 0.03f + 0.025f * (float)det.NextDouble();
                    float yaw = -a * Mathf.Rad2Deg + 90f;
                    if (!arab)
                    {
                        m.Box(p + Vector3.up * s, new Vector3(s, s, s), yaw, H("F4EEE4"), SpiceMesh.Paint);
                        m.Dome(p, s * 0.75f, s * 2f, s * 0.8f, 8, H("C84030"), SpiceMesh.Paint);
                    }
                    else
                    {
                        m.Box(p + Vector3.up * s * 0.8f, new Vector3(s * 1.2f, s * 0.8f, s), yaw, H(det.NextDouble() < 0.5 ? "EADFCB" : "E3CFAE"), SpiceMesh.Paint);
                        if (det.NextDouble() < 0.5) m.Dome(p, s * 0.7f, s * 1.6f, s * 0.7f, 8, H("7E8CA0"), SpiceMesh.Tiles);
                    }
                }
            }
            // A rock-cut temple front (India) / a row of cave windows (Arabia) high on one face.
            {
                float fa = (float)rng.NextDouble() * 360f;
                Quaternion fq = Quaternion.Euler(0f, fa, 0f);
                float fy = tierY[1] - 0.25f;
                float fr = tierR[0] * 0.84f;
                if (!arab)
                {
                    Color carved = H("F0C49A");
                    m.Box(fq * new Vector3(0f, fy, fr), new Vector3(0.16f, 0.14f, 0.02f), fa, carved, SpiceMesh.Carved);
                    for (int p = -2; p <= 2; p++) m.Box(fq * new Vector3(p * 0.065f, fy - 0.02f, fr + 0.03f), new Vector3(0.011f, 0.09f, 0.011f), fa, carved, SpiceMesh.Carved);
                    m.Box(fq * new Vector3(0f, fy - 0.03f, fr + 0.01f), new Vector3(0.028f, 0.065f, 0.01f), fa, H("FFB050"), SpiceMesh.Glow);
                    m.Box(fq * new Vector3(0f, fy + 0.1f, fr + 0.025f), new Vector3(0.17f, 0.018f, 0.015f), fa, carved, SpiceMesh.Carved);
                    m.Prism(fq * new Vector3(0f, 0f, fr + 0.01f), 8, 0.055f, 0f, fy + 0.12f, fy + 0.22f, 0f, carved, SpiceMesh.Carved);
                }
                else
                {
                    for (int w = 0; w < 5; w++)
                    {
                        Quaternion wq = Quaternion.Euler(0f, fa + w * 18f - 36f, 0f);
                        m.Box(wq * new Vector3(0f, fy - 0.06f * (w % 2), fr + 0.005f), new Vector3(0.018f, 0.028f, 0.01f), fa + w * 18f - 36f, H("FFB860"), SpiceMesh.Glow);
                    }
                }
            }
            if (pedestal)
                m.Lathe(centres[2], new[] { tierR[2] * 0.95f, tierR[2] * 0.95f, tierR[2] * 0.8f, tierR[2] * 0.8f, tierR[2] * 0.68f }, new[] { height, height + 0.03f, height + 0.03f, height + 0.06f, height + 0.06f }, 12, 0f, arab ? H("EADFCB") : H("D9A070"), SpiceMesh.Carved);
            if (!arab) cl.india = m; else cl.arabia = m;
            if (pass == 0)
            {
                cl.topRadius = tierR[2];
                cl.anchor = centres[2] + new Vector3(0f, height + (pedestal ? 0.06f : 0f), 0f);
                var tr = new System.Random(seed + 991);
                for (int i = 0; i < 90; i++)
                {
                    float a = (float)tr.NextDouble() * Mathf.PI * 2f;
                    float rr = Mathf.Sqrt((float)tr.NextDouble()) * tierR[2] * 0.78f;
                    cl.tops.Add(new Vector4(centres[2].x + Mathf.Cos(a) * rr, centres[2].z + Mathf.Sin(a) * rr, height, 1f));
                }
            }
        }
        return cl;
    }
}
