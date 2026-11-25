using UnityEngine;

/// <summary>
/// One zone's colours (TrialMonument2): the path, the block shading, the sky and the FX accent.
/// The path colours are the four stops of PathDepthShading's ahead-gradient (near the runner ->
/// far away); the block and sky colours go out as shader globals (see MonumentBlock / MonumentSky).
/// </summary>
[System.Serializable]
public class ZonePalette
{
    public string name = "Zone";

    [Header("Path (near the runner -> far)")]
    public Color n0 = Color.white;
    public Color n1 = Color.white;
    public Color n2 = Color.white;
    public Color n3 = Color.white;
    public Color travelled = Color.gray;

    [Header("Blocks")]
    public Color shadow = Color.gray;
    public Color edge = Color.white;

    [Header("Sky")]
    public Color skyTop = Color.black;
    public Color skyMid = Color.black;
    public Color skyBottom = Color.black;
    public Color skyGlow = Color.white;
    public Color star = Color.white;

    [Header("FX")]
    [Tooltip("Speed lines, streak rings, glows, the door rim and the flash all take this.")]
    public Color accent = Color.cyan;

    // Where the four path colours sit along PathDepthShading's gradient - the authored scene's stops.
    public static readonly float[] GradientTimes = { 0f, 0.3f, 0.65f, 1f };

    public static ZonePalette Lerp(ZonePalette a, ZonePalette b, float t)
    {
        t = Mathf.Clamp01(t);
        return new ZonePalette
        {
            name = t < 0.5f ? a.name : b.name,
            n0 = Color.Lerp(a.n0, b.n0, t), n1 = Color.Lerp(a.n1, b.n1, t),
            n2 = Color.Lerp(a.n2, b.n2, t), n3 = Color.Lerp(a.n3, b.n3, t),
            travelled = Color.Lerp(a.travelled, b.travelled, t),
            shadow = Color.Lerp(a.shadow, b.shadow, t), edge = Color.Lerp(a.edge, b.edge, t),
            skyTop = Color.Lerp(a.skyTop, b.skyTop, t), skyMid = Color.Lerp(a.skyMid, b.skyMid, t),
            skyBottom = Color.Lerp(a.skyBottom, b.skyBottom, t), skyGlow = Color.Lerp(a.skyGlow, b.skyGlow, t),
            star = Color.Lerp(a.star, b.star, t), accent = Color.Lerp(a.accent, b.accent, t)
        };
    }

    /// <summary>
    /// A palette in the same pale, cool-shaded Monument Valley style as the authored scene, at any
    /// hue: near-white blocks tinting to a deeper tone with distance, a tinted shade, a dark sky
    /// climbing to a lighter horizon. sat scales every saturation (neon gold wants more).
    /// </summary>
    public static ZonePalette FromHue(string name, float hue, float sat = 1f, float hueDrift = 0f)
    {
        Color Hsv(float h, float s, float v) => Color.HSVToRGB(Mathf.Repeat(h, 1f), Mathf.Clamp01(s), Mathf.Clamp01(v));
        return new ZonePalette
        {
            name = name,
            n0 = Hsv(hue, 0.10f * sat, 0.98f),
            n1 = Hsv(hue + hueDrift * 0.3f, 0.18f * sat, 0.94f),
            n2 = Hsv(hue + hueDrift * 0.65f, 0.28f * sat, 0.90f),
            n3 = Hsv(hue + hueDrift, 0.40f * sat, 0.86f),
            travelled = Hsv(hue, 0.42f * sat, 0.66f),
            shadow = Hsv(hue, 0.24f * sat, 0.90f),
            edge = Color.white,
            skyTop = Hsv(hue, 0.55f, 0.24f),
            skyMid = Hsv(hue, 0.44f, 0.46f),
            skyBottom = Hsv(hue, 0.30f * sat, 0.70f),
            skyGlow = Hsv(hue, 0.18f * sat, 0.85f),
            star = Hsv(hue, 0.10f, 1f),
            accent = Hsv(hue, 0.55f, 1f)
        };
    }

    /// <summary>The five zones after the opening one that the user asked for, plus two more.</summary>
    public static ZonePalette[] DefaultCycle()
    {
        return new[]
        {
            FromHue("Red",           0.0f,  1.15f),
            FromHue("Blue",          0.60f, 1.15f),
            FromHue("Green",         0.36f, 1.05f),
            FromHue("Purple",        0.76f, 1.10f),
            FromHue("Pinkish Red",   0.975f, 1.10f, -0.02f),
            FromHue("Sunset Orange", 0.065f, 1.35f, 0.02f),
            FromHue("Neon Gold",     0.135f, 1.70f, -0.01f)
        };
    }
}
