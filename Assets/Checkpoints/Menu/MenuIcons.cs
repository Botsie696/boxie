using UnityEngine;

/// <summary>
/// Procedural UI sprites for the menu and shop (made once, cached): a rounded rect for 9-sliced pills and buttons, the coin,
/// lock, gear, cube, play arrow, back arrow, a square spark and a vertical gradient. White shapes, tinted by the Image colour.
/// </summary>
public static class MenuIcons
{
    static Sprite pill, coin, lockIcon, gear, cube, play, back, spark, gradient, ring, circle, softPill, infinity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { pill = coin = lockIcon = gear = cube = play = back = spark = gradient = ring = circle = softPill = infinity = null; }

    delegate float Sdf(float x, float y);   // signed distance in pixels (negative inside)

    static Texture2D Draw(int size, Sdf sdf, System.Func<float, float, Color> shade = null)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float a = Mathf.Clamp01(0.5f - sdf(fx, fy));
                Color c = shade != null ? shade(fx / size, fy / size) : Color.white;
                c.a *= a;
                px[y * size + x] = c;
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    static float Box(float x, float y, float cx, float cy, float hx, float hy, float r)
    {
        float qx = Mathf.Abs(x - cx) - hx + r, qy = Mathf.Abs(y - cy) - hy + r;
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    static float Circle(float x, float y, float cx, float cy, float r) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;

    static Sprite Make(Texture2D t, Vector4 border = default) =>
        Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);

    /// <summary>Rounded rectangle, 9-sliced (radius 32 px at 1x).</summary>
    public static Sprite Pill => pill != null ? pill : pill = Make(Draw(128, (x, y) => Box(x, y, 64, 64, 63, 63, 32)), new Vector4(40, 40, 40, 40));

    /// <summary>A gold coin with an inner ring (drawn in colour; leave the Image white).</summary>
    public static Sprite Coin => coin != null ? coin : coin = Make(Draw(128, (x, y) => Circle(x, y, 64, 64, 60), (u, v) =>
    {
        float d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f));
        Color rim = new Color(0.95f, 0.62f, 0.12f), face = new Color(1f, 0.82f, 0.28f), shine = new Color(1f, 0.93f, 0.6f);
        Color c = d > 0.40f ? rim : (d > 0.33f ? new Color(0.98f, 0.7f, 0.16f) : Color.Lerp(face, shine, Mathf.Clamp01((v - 0.35f) * 1.6f)));
        return c;
    }));

    public static Sprite Lock => lockIcon != null ? lockIcon : lockIcon = Make(Draw(128, (x, y) =>
    {
        float body = Box(x, y, 64, 46, 40, 32, 9);
        float shackleOuter = Box(x, y, 64, 84, 28, 30, 26);
        float shackleInner = Box(x, y, 64, 84, 15, 22, 14);
        float shackle = Mathf.Max(shackleOuter, -shackleInner);
        shackle = Mathf.Max(shackle, 66f - y);           // only above the body
        float keyhole = Mathf.Min(Circle(x, y, 64, 52, 7), Box(x, y, 64, 40, 3.5f, 10, 2));
        return Mathf.Max(Mathf.Min(body, shackle), -keyhole);
    }));

    public static Sprite Gear => gear != null ? gear : gear = Make(Draw(128, (x, y) =>
    {
        float dx = x - 64, dy = y - 64;
        float r = Mathf.Sqrt(dx * dx + dy * dy);
        float ang = Mathf.Atan2(dy, dx);
        float teeth = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Cos(ang * 8f) - 0.1f) * 2.2f));
        float outer = r - (44f + 12f * teeth);
        return Mathf.Max(outer, -(r - 17f));
    }));

    /// <summary>An isometric cube (three shaded faces) - the shop button and the cards' fallback icon.</summary>
    public static Sprite Cube => cube != null ? cube : cube = Make(Draw(128, (x, y) =>
    {
        // hexagon outline of an iso cube
        float dx = Mathf.Abs(x - 64), dy = y - 64;
        float hex = Mathf.Max(dx * 0.866f + Mathf.Abs(dy) * 0.5f - 52f, dx - 45f);
        return hex;
    }, (u, v) =>
    {
        float x = u - 0.5f, y = v - 0.5f;
        bool top = y > 0f && Mathf.Abs(x) * 0.577f < y + 0.001f || y > Mathf.Abs(x) * 0.577f;
        if (top) return new Color(1f, 1f, 1f);
        return x < 0f ? new Color(0.78f, 0.78f, 0.82f) : new Color(0.6f, 0.6f, 0.68f);
    }));

    public static Sprite Play => play != null ? play : play = Make(Draw(128, (x, y) =>
    {
        // rounded triangle pointing right
        float d1 = (x - 104f) * 0.866f + Mathf.Abs(y - 64f) * 0.5f;
        float d2 = 30f - x;
        return Mathf.Max(d1, d2) + 0f;
    }));

    public static Sprite Back => back != null ? back : back = Make(Draw(128, (x, y) =>
    {
        // chevron pointing left
        float dx = x - 52f, dy = Mathf.Abs(y - 64f);
        float d = Mathf.Abs(dx - dy * 0.95f) * 0.72f - 9f;
        return Mathf.Max(d, Mathf.Max(dy - 38f, 18f - x));
    }));

    public static Sprite Spark => spark != null ? spark : spark = Make(Draw(32, (x, y) => Box(x, y, 16, 16, 13, 13, 2)));

    /// <summary>A thin rounded-square outline (focus / equipped frames), 9-sliced.</summary>
    public static Sprite Ring => ring != null ? ring : ring = Make(Draw(128, (x, y) =>
    {
        float o = Box(x, y, 64, 64, 63, 63, 32);
        return Mathf.Max(o, -(o + 6f));
    }), new Vector4(40, 40, 40, 40));

    /// <summary>A filled circle (round buttons).</summary>
    public static Sprite Round => circle != null ? circle : circle = Make(Draw(128, (x, y) => Circle(x, y, 64, 64, 63)));

    /// <summary>A thick lemniscate stroke (the ENDLESS button).</summary>
    public static Sprite Infinity => infinity != null ? infinity : infinity = Make(Draw(128, (x, y) =>
    {
        const int N = 64;
        float best = 1e9f, px = 0f, py = 0f;
        for (int i = 0; i <= N; i++)
        {
            float t = i * (Mathf.PI * 2f / N), s = Mathf.Sin(t), k = 1f / (1f + s * s);
            float qx = 64f + 54f * Mathf.Cos(t) * k, qy = 64f + 70f * s * Mathf.Cos(t) * k;
            if (i > 0)
            {
                float ex = qx - px, ey = qy - py, h = Mathf.Clamp01(((x - px) * ex + (y - py) * ey) / (ex * ex + ey * ey + 1e-6f));
                float dx = x - (px + ex * h), dy = y - (py + ey * h);
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dy * dy));
            }
            px = qx; py = qy;
        }
        return best - 8.5f;
    }));

    /// <summary>A soft, blurred rounded rect (glow shadows), 9-sliced: alpha falls off over ~28 px outside a pill.</summary>
    public static Sprite SoftPill => softPill != null ? softPill : softPill = Make(Draw(160, (x, y) => -1f, (u, v) =>
    {
        float d = Box(u * 160f, v * 160f, 80, 80, 40, 40, 40);
        return new Color(1f, 1f, 1f, Mathf.Exp(-Mathf.Max(0f, d) / 11f) * (d < 0f ? 1f : 1f));
    }), new Vector4(70, 70, 70, 70));

    /// <summary>Vertical gradient, opaque at the bottom, clear at the top (bars fading over the bands).</summary>
    public static Sprite Gradient => gradient != null ? gradient : gradient = Make(Draw(8, (x, y) => -1f, (u, v) => new Color(1f, 1f, 1f, Mathf.SmoothStep(1f, 0f, v))));
}
