using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MainScene prop camera 3.11 only (on the "HudTheme" object): restyles the top HUD per checkpoint so the pause button,
/// the score, the coin pill and the diamond pill belong to the theme and not only the scene behind them. It finds the
/// pieces the HUD scripts already build (Hud36Canvas / CoinHUDCanvas / DiamondHUDCanvas) by name and never changes their
/// size, position, text or behaviour - only colours, a thin rim, and (Pixel, Voxel, Sketchbook) square corners.
///   pill / button   fill colour + alpha, a rim of the theme's accent
///   text            count / score colour
///   icons           a tint over the coin and diamond sprites (white = as drawn)
///   pause bars      the two bars' colour
/// Checkpoint = DynamicPathGenerator.PortalsTaken + 1; blends over blendSeconds after a portal, snaps on a restart.
/// All in 3.11; nothing is changed in the other scenes (this component is not in them).
/// </summary>
[DefaultExecutionOrder(970)]
public class HudThemer311 : MonoBehaviour
{
    [System.Serializable]
    public class HudLook
    {
        public string name = "Checkpoint";
        public Color fill = new Color(0.04f, 0.05f, 0.09f, 0.45f);
        public Color rim = new Color(1f, 1f, 1f, 0.25f);
        public Color text = Color.white;
        public Color shadow = new Color(0f, 0f, 0f, 0.5f);
        [Tooltip("Tint over the coin icon and the coins flying into the pill (white = as drawn).")]
        public Color coinTint = Color.white;
        [Tooltip("Tint over the diamond icon and the diamonds flying into the pill.")]
        public Color gemTint = Color.white;
        public Color bars = Color.white;
        [Tooltip("Rim thickness in reference pixels (1080 x 1920).")]
        [Range(0f, 10f)] public float rimWidth = 3f;
        [Tooltip("Square corners (no rounded pill) - Pixel, Voxel, Sketchbook.")]
        public bool square = false;

        public HudLook Copy() => (HudLook)MemberwiseClone();

        public static void Lerp(HudLook a, HudLook b, float t, HudLook into)
        {
            into.fill = Color.Lerp(a.fill, b.fill, t);
            into.rim = Color.Lerp(a.rim, b.rim, t);
            into.text = Color.Lerp(a.text, b.text, t);
            into.shadow = Color.Lerp(a.shadow, b.shadow, t);
            into.coinTint = Color.Lerp(a.coinTint, b.coinTint, t);
            into.gemTint = Color.Lerp(a.gemTint, b.gemTint, t);
            into.bars = Color.Lerp(a.bars, b.bars, t);
            into.rimWidth = Mathf.Lerp(a.rimWidth, b.rimWidth, t);
            into.square = t < 0.5f ? a.square : b.square;
        }
    }

    public DynamicPathGenerator pathGen;
    [Tooltip("Checkpoint 1, 2, 3 ... (10 entries). Past the end = the last entry.")]
    public HudLook[] looks = new HudLook[0];
    [Range(0.1f, 4f)] public float blendSeconds = 1.6f;

    /// <summary>One styled element: the original image becomes the rim, a child "Fill" inset by the rim is the colour.</summary>
    class Panel
    {
        public Image outer, fill;
        public Sprite sprite;
        public float outerPpum, height;
        public Text[] texts = new Text[0];
        public TextMeshProUGUI[] tmps = new TextMeshProUGUI[0];
        public Image[] icons = new Image[0];
        public Image[] bars = new Image[0];
        public Shadow[] shadows = new Shadow[0];
        public Transform canvas;
        public float[] outline = new float[0];
        public Image[] flyers = new Image[0];
        public int flyerCount = -1;
    }

    Panel coin, gem, pause, score;
    readonly HudLook current = new HudLook();
    public HudLook Current => current;

    HudLook from, to;
    float blendT = 1f, nextFind;
    int checkpoint = -1;

    static HudLook Make(string name, string fill, float fa, string rim, float ra, string text, string coinT, string gemT, string bars, float rimW, bool square)
    {
        Color f = Color.black, r = Color.white, t = Color.white, ci = Color.white, gi = Color.white, b = Color.white;
        ColorUtility.TryParseHtmlString(fill, out f); f.a = fa;
        ColorUtility.TryParseHtmlString(rim, out r); r.a = ra;
        ColorUtility.TryParseHtmlString(text, out t);
        ColorUtility.TryParseHtmlString(coinT, out ci);
        ColorUtility.TryParseHtmlString(gemT, out gi);
        ColorUtility.TryParseHtmlString(bars, out b);
        return new HudLook { name = name, fill = f, rim = r, text = t, coinTint = ci, gemTint = gi, bars = b, rimWidth = rimW, square = square,
                             shadow = new Color(0f, 0f, 0f, 0.45f) };
    }

    /// <summary>Checkpoint 1 ... 10 (also what Reset puts in the inspector).</summary>
    public static HudLook[] DefaultLooks() => new[]
    {
        Make("1 Start - slate stone, warm haze",        "#1b2230", 0.55f, "#c9b99a", 0.55f, "#f6ecd6", "#ffffff", "#ffffff", "#f6ecd6", 3f, false),
        Make("2 Crimson - Penrose red on indigo",       "#1a1030", 0.62f, "#ff5a4a", 0.85f, "#ffe0d2", "#ffc9a8", "#ff8fa8", "#ffe0d2", 3f, false),
        Make("3 Toon - overgrown ruins",                "#232f36", 0.72f, "#86ad5a", 0.95f, "#eaf3d6", "#e8ffb0", "#9fffd0", "#dcecc0", 4f, false),
        Make("4 Pixel - jellyfish grotto",              "#0a1248", 0.80f, "#3fd08a", 1.00f, "#c4fff0", "#d6ffb0", "#9affea", "#c4fff0", 4f, true),
        Make("5 Storybook - ink and parchment",         "#f3e6c8", 0.90f, "#5a4631", 0.95f, "#4a3626", "#ffd9a0", "#ffb894", "#4a3626", 4f, false),
        Make("6 Flower - blossom vale",                 "#22402b", 0.78f, "#f2a9c2", 0.95f, "#fff0d6", "#ffe0c0", "#ffa8d8", "#fff0d6", 3f, false),
        Make("7 Voxel - brick meadow",                  "#363636", 0.85f, "#62c548", 1.00f, "#ffffff", "#ffffff", "#a8ffa0", "#ffffff", 4f, true),
        Make("8 Pencil - sketchbook",                   "#f2efe7", 0.93f, "#3b3936", 0.95f, "#2e2c2a", "#cfc8a8", "#b4bec8", "#2e2c2a", 3f, true),
        Make("9 Space - rogue planet",                  "#0c1240", 0.78f, "#6f90ff", 0.92f, "#e0e7ff", "#ffe8c8", "#c8c8ff", "#e0e7ff", 3f, false),
        Make("10 Spice Route - India / Arabia",         "#531c36", 0.86f, "#e9a53c", 0.97f, "#ffe9bf", "#ffd890", "#ff9ab0", "#ffe9bf", 4f, false),
    };

    void Reset() { looks = DefaultLooks(); }

    void Start()
    {
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (looks == null || looks.Length == 0) looks = DefaultLooks();
        Snap(CurrentCheckpoint());
        if (GetComponent<LevelProgressBar311>() == null) gameObject.AddComponent<LevelProgressBar311>();
    }

    int CurrentCheckpoint() => pathGen != null ? pathGen.PortalsTaken + 1 : 1;

    HudLook LookFor(int cp) => looks[Mathf.Clamp(cp - 1, 0, looks.Length - 1)];

    void Snap(int cp)
    {
        checkpoint = cp;
        from = to = LookFor(cp);
        HudLook.Lerp(to, to, 1f, current);
        blendT = 1f;
    }

    void LateUpdate()
    {
        if (looks == null || looks.Length == 0) return;
        int cp = CurrentCheckpoint();
        if (cp != checkpoint)
        {
            if (cp == checkpoint + 1) { from = current.Copy(); to = LookFor(cp); blendT = 0f; checkpoint = cp; }
            else Snap(cp);
        }
        if (blendT < 1f)
        {
            blendT = Mathf.MoveTowards(blendT, 1f, Time.unscaledDeltaTime / blendSeconds);
            HudLook.Lerp(from, to, Mathf.SmoothStep(0f, 1f, blendT), current);
        }

        // The HUD scripts build their canvases in Start; find them (and again if one is rebuilt).
        if ((coin == null || gem == null || pause == null || score == null || coin.outer == null || gem.outer == null
             || pause.outer == null || score.outer == null) && Time.unscaledTime >= nextFind)
        {
            nextFind = Time.unscaledTime + 0.5f;
            FindPanels();
        }
        Apply(coin, true);
        Apply(gem, true);
        Apply(pause, false);
        Apply(score, false);
    }

    void FindPanels()
    {
        if (coin == null || coin.outer == null) coin = Wrap("CoinHUDCanvas", "CoinCounter", false);
        if (gem == null || gem.outer == null) gem = Wrap("DiamondHUDCanvas", "DiamondCounter", false);
        if (pause == null || pause.outer == null) pause = Wrap("Hud36Canvas", "PauseHit/Visual", true);
        if (score == null || score.outer == null) score = Wrap("Hud36Canvas", "SafeArea/ScorePill", false);
    }

    static Transform FindDeep(Transform root, string path)
    {
        // "A/B" path anywhere under root (the HUD puts these under SafeArea).
        string[] parts = path.Split('/');
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != parts[parts.Length - 1]) continue;
            Transform p = t;
            bool ok = true;
            for (int i = parts.Length - 2; i >= 0 && ok; i--)
            {
                p = p.parent;
                ok = p != null && p.name == parts[i];
            }
            if (ok) return t;
        }
        return null;
    }

    Panel Wrap(string canvasName, string path, bool isPause)
    {
        GameObject c = GameObject.Find(canvasName);
        if (c == null) return null;
        // Hud36 puts its pieces under SafeArea; a "SafeArea/" prefix in the path is matched by name only.
        Transform t = FindDeep(c.transform, path.Replace("SafeArea/", ""));
        if (t == null) return null;
        var outer = t.GetComponent<Image>();
        if (outer == null) return null;

        var p = new Panel { outer = outer, sprite = outer.sprite, outerPpum = outer.pixelsPerUnitMultiplier, canvas = c.transform };
        p.height = Mathf.Max(1f, outer.rectTransform.rect.height);

        var fillGo = new GameObject("ThemeFill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(t, false);
        fillGo.transform.SetAsFirstSibling();
        p.fill = fillGo.GetComponent<Image>();
        p.fill.raycastTarget = false;
        p.fill.sprite = outer.sprite;
        p.fill.type = outer.type;
        var fr = p.fill.rectTransform;
        fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.pivot = new Vector2(0.5f, 0.5f);

        p.texts = t.GetComponentsInChildren<Text>(true);
        p.tmps = t.GetComponentsInChildren<TextMeshProUGUI>(true);
        p.outline = new float[p.tmps.Length];
        for (int k = 0; k < p.tmps.Length; k++) p.outline[k] = p.tmps[k].outlineWidth;
        p.shadows = t.GetComponentsInChildren<Shadow>(true);
        var icons = new System.Collections.Generic.List<Image>();
        var bars = new System.Collections.Generic.List<Image>();
        foreach (var im in t.GetComponentsInChildren<Image>(true))
        {
            if (im == outer || im == p.fill) continue;
            if (im.name == "Icon") icons.Add(im);
            else if (isPause && im.name == "Bar") bars.Add(im);
        }
        p.icons = icons.ToArray();
        p.bars = bars.ToArray();
        return p;
    }

    void Apply(Panel p, bool tintIcons)
    {
        if (p == null || p.outer == null || p.fill == null) return;
        float rim = current.rimWidth;
        bool square = current.square;
        // The original image is the rim colour; the child is the fill, inset by the rim.
        p.outer.sprite = square ? null : p.sprite;
        p.outer.color = current.rim;
        p.fill.sprite = square ? null : p.sprite;
        p.fill.color = current.fill;
        var fr = p.fill.rectTransform;
        fr.offsetMin = new Vector2(rim, rim);
        fr.offsetMax = new Vector2(-rim, -rim);
        if (!square && p.sprite != null && p.sprite.border.sqrMagnitude > 0f && p.height > rim * 2f + 1f)
            p.fill.pixelsPerUnitMultiplier = p.outerPpum * p.height / (p.height - rim * 2f);

        foreach (var t in p.texts) if (t != null && t.name != "Streak") t.color = current.text;
        // Dark ink on a light pill: the score font's own outline would smudge the digits, so it goes while the fill is light.
        bool lightFill = current.fill.r * 0.3f + current.fill.g * 0.59f + current.fill.b * 0.11f > 0.6f;
        for (int k = 0; k < p.tmps.Length; k++)
        {
            var t = p.tmps[k];
            if (t == null) continue;
            Color c = current.text; c.a = t.color.a; t.color = c;
            t.outlineWidth = lightFill ? 0f : p.outline[k];
        }
        foreach (var s in p.shadows) if (s != null && !(s is Outline)) s.effectColor = current.shadow;
        Color iconTint = p == gem ? current.gemTint : current.coinTint;
        foreach (var ic in p.icons) if (ic != null) ic.color = iconTint;
        foreach (var b in p.bars) if (b != null) b.color = current.bars;

        // The coins / diamonds flying into the pill (pooled children of the same canvas) carry the same tint.
        if (tintIcons && p.canvas != null)
        {
            if (p.flyerCount != p.canvas.childCount)
            {
                var list = new System.Collections.Generic.List<Image>();
                for (int i = 0; i < p.canvas.childCount; i++)
                {
                    Transform ch = p.canvas.GetChild(i);
                    if (ch.name == "FlyingCoin" || ch.name == "FlyingDiamond") { var fi = ch.GetComponent<Image>(); if (fi != null) list.Add(fi); }
                }
                p.flyers = list.ToArray();
                p.flyerCount = p.canvas.childCount;
            }
            foreach (var fi in p.flyers) if (fi != null) fi.color = iconTint;
        }
    }

    void OnDisable()
    {
        // Leave the HUD as built: the rim image goes back to its original sprite and colour.
        Restore(coin);
        Restore(gem);
        Restore(pause);
        Restore(score);
    }

    void Restore(Panel p)
    {
        if (p == null || p.outer == null) return;
        p.outer.sprite = p.sprite;
        p.outer.color = new Color(0.04f, 0.05f, 0.09f, 0.45f);
        if (p.fill != null) Destroy(p.fill.gameObject);
    }
}
