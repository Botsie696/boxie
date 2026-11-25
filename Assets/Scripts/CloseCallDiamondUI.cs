using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Diamond counter under the coin counter (top right). Every close call (DynamicPathGenerator.CloseCallAwarded)
/// sends a diamond flying from the runner into the counter; the count goes up by one and stays for the run.
/// Beside the pill a "+N" shows the streak: the close call pays N score (1, 2, 3 ...) and the label goes away
/// when a leg passes without one. Built in code on its own canvas like CoinCounterUI. Added to the runner by
/// CloseCallFX.
/// </summary>
public class CloseCallDiamondUI : MonoBehaviour
{
    [Range(0.1f, 1.5f)] public float flySeconds = 0.5f;
    [Tooltip("A '+N' beside the pill while the streak runs. Off: the popup next to the runner already shows it.")]
    public bool showStreakLabel = false;

    class Flyer { public RectTransform rt; public Vector2 from; public float t; public int total; public float arc; }

    const float Margin = 40f;
    const float PillHeight = 120f;
    const float IconSize = 104f;
    const float GapBelowCoins = 16f;
    const float PunchSeconds = 0.22f;

    DynamicPathGenerator gen;
    Canvas canvas;
    CanvasGroup group;
    RectTransform pill, icon;
    Text countText, streakText;
    readonly List<Flyer> flying = new List<Flyer>();
    readonly Stack<Flyer> spare = new Stack<Flyer>();
    int landedTotal, shownStreak, flyerSerial;
    float punch, streakPunch;
    Rect appliedSafeArea;
    Vector3 appliedScreen;
    Texture2D gemTex, pillTex;
    Sprite gemSprite, pillSprite;

    void Start()
    {
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        if (gen == null) { enabled = false; return; }
        BuildUI();
        SetCount(gen.Diamonds);
        landedTotal = gen.Diamonds;
        gen.CloseCallAwarded += OnAwarded;
    }

    void OnDestroy()
    {
        if (gen != null) gen.CloseCallAwarded -= OnAwarded;
        if (canvas != null) Destroy(canvas.gameObject);
        if (gemSprite != null) Destroy(gemSprite);
        if (pillSprite != null) Destroy(pillSprite);
        if (gemTex != null) Destroy(gemTex);
        if (pillTex != null) Destroy(pillTex);
    }

    void OnAwarded(int points, int streak, int total)
    {
        shownStreak = streak;
        streakPunch = 1f;
        Camera cam = Camera.main;
        Vector3 screen = cam != null ? cam.WorldToScreenPoint(transform.position) : Vector3.zero;
        if (cam == null || screen.z < 0f || flying.Count >= 8) { Land(total); return; }

        Flyer f = spare.Count > 0 ? spare.Pop() : NewFlyer();
        f.from = screen; f.t = 0f; f.total = total;
        f.arc = (flyerSerial++ % 2 == 0 ? 1f : -1f) * 70f;
        f.rt.position = screen;
        f.rt.localScale = Vector3.one;
        f.rt.gameObject.SetActive(true);
        flying.Add(f);
    }

    void Update()
    {
        if (canvas == null) return;
        // A new run (generator reset the count): snap back.
        if (gen.Diamonds < landedTotal) { landedTotal = gen.Diamonds; SetCount(landedTotal); }
        if (gen.CloseCallStreak == 0) shownStreak = 0;

        // Follows the coin counter: hidden with it on the game-over screen and in the tutorial.
        var coins = GetComponent<CoinCounterUI>();
        bool coinHidden = coins != null && coins.IsHidden;
        group.alpha = coinHidden ? 0f : TutorialHud.Alpha;
        FitSafeArea();

        float dt = CoinCounterUI.FreezeFlights ? 0f : Time.unscaledDeltaTime;
        Vector2 target = icon.position;
        float k = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        for (int i = flying.Count - 1; i >= 0; i--)
        {
            Flyer f = flying[i];
            f.t += dt / Mathf.Max(0.05f, flySeconds);
            if (f.t >= 1f)
            {
                Land(f.total);
                f.rt.gameObject.SetActive(false);
                spare.Push(f);
                int last = flying.Count - 1;
                flying[i] = flying[last];
                flying.RemoveAt(last);
                continue;
            }
            float e = f.t * f.t * (3f - 2f * f.t);
            e *= e;
            Vector2 d = target - f.from;
            Vector2 side = d.sqrMagnitude > 1f ? new Vector2(-d.y, d.x).normalized : Vector2.zero;
            f.rt.position = f.from + d * e + side * (Mathf.Sin(f.t * Mathf.PI) * f.arc * k);
            float s = f.t < 0.15f ? Mathf.Lerp(0.4f, 1f, f.t / 0.15f) : Mathf.Lerp(1f, 0.85f, e);
            f.rt.localScale = new Vector3(s, s, 1f);
        }

        if (punch > 0f)
        {
            punch = Mathf.Max(0f, punch - dt / PunchSeconds);
            float p2 = punch * punch;
            icon.localScale = Vector3.one * (1f + 0.35f * p2);
            countText.rectTransform.localScale = Vector3.one * (1f + 0.18f * p2);
        }

        bool show = showStreakLabel && shownStreak > 0;
        if (streakText.gameObject.activeSelf != show) streakText.gameObject.SetActive(show);
        if (show)
        {
            streakText.text = "+" + shownStreak;
            streakText.rectTransform.anchoredPosition = new Vector2(-16f, 0f);
            streakPunch = Mathf.Max(0f, streakPunch - dt / 0.3f);
            streakText.rectTransform.localScale = Vector3.one * (1f + 0.5f * streakPunch * streakPunch);
        }
    }

    void Land(int total)
    {
        if (total <= landedTotal) return;
        landedTotal = total;
        SetCount(total);
        punch = 1f;
    }

    void SetCount(int value)
    {
        countText.text = value.ToString();
        float w = PillHeight + 8f + countText.preferredWidth + 40f;
        pill.sizeDelta = new Vector2(Mathf.Max(PillHeight * 2f, w), PillHeight);
    }

    void FitSafeArea()
    {
        icon.sizeDelta = new Vector2(IconSize, IconSize) * CoinCounterUI.IconScale;
        Rect sa = Screen.safeArea;
        float hs = CoinCounterUI.HudScale;
        var screen = new Vector3(Screen.width, Screen.height, canvas.scaleFactor * hs);
        if (sa == appliedSafeArea && screen == appliedScreen) return;
        appliedSafeArea = sa; appliedScreen = screen;
        float k = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        float right = (Screen.width - sa.xMax) / k;
        float top = (Screen.height - sa.yMax) / k;
        pill.anchoredPosition = new Vector2(-(Margin + right), -(Margin + top + (PillHeight + GapBelowCoins) * hs));
        pill.localScale = new Vector3(hs, hs, 1f);
    }

    // ------------------------------------------------------------------ UI

    void BuildUI()
    {
        var canvasGO = new GameObject("DiamondHUDCanvas");
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 101;
        group = canvasGO.AddComponent<CanvasGroup>();
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        gemSprite = MakeGemSprite(128, out gemTex);
        pillSprite = MakePillSprite(64, out pillTex);

        pill = new GameObject("DiamondCounter", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        pill.SetParent(canvasGO.transform, false);
        pill.anchorMin = pill.anchorMax = pill.pivot = new Vector2(1f, 1f);
        pill.sizeDelta = new Vector2(PillHeight * 2f, PillHeight);
        var pillImg = pill.GetComponent<Image>();
        pillImg.sprite = pillSprite;
        pillImg.type = Image.Type.Sliced;
        pillImg.pixelsPerUnitMultiplier = 31f / (PillHeight * 0.5f);
        pillImg.color = new Color(0.04f, 0.05f, 0.09f, 0.45f);
        pillImg.raycastTarget = false;

        icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        icon.SetParent(pill, false);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
        icon.pivot = new Vector2(0.5f, 0.5f);
        icon.anchoredPosition = new Vector2(PillHeight * 0.5f, 0f);
        icon.sizeDelta = new Vector2(IconSize, IconSize);
        var iconImg = icon.GetComponent<Image>();
        iconImg.sprite = gemSprite; iconImg.preserveAspect = true; iconImg.raycastTarget = false;

        countText = MakeText(pill, "Count", font, Mathf.RoundToInt(80 * CoinCounterUI.TextScale), new Color(0.75f, 0.95f, 1f), TextAnchor.MiddleLeft, new Vector2(0f, 0.5f));
        countText.rectTransform.anchoredPosition = new Vector2(PillHeight + 8f, 2f);
        countText.rectTransform.sizeDelta = new Vector2(400f, PillHeight);

        streakText = MakeText(pill, "Streak", font, 84, new Color(1f, 0.82f, 0.25f), TextAnchor.MiddleRight, new Vector2(1f, 0.5f));
        streakText.rectTransform.anchorMin = streakText.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        streakText.rectTransform.sizeDelta = new Vector2(300f, PillHeight);
        streakText.gameObject.AddComponent<Outline>().effectColor = new Color(0.35f, 0.12f, 0f, 0.85f);
        streakText.gameObject.SetActive(false);

        FitSafeArea();
    }

    static Text MakeText(Transform parent, string name, Font font, int size, Color color, TextAnchor align, Vector2 pivot)
    {
        var t = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        t.rectTransform.SetParent(parent, false);
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        t.rectTransform.pivot = pivot;
        t.font = font; t.fontSize = size; t.fontStyle = FontStyle.Bold; t.alignment = align; t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3, -3);
        return t;
    }

    Flyer NewFlyer()
    {
        var rt = new GameObject("FlyingDiamond", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        rt.SetParent(canvas.transform, false);
        rt.sizeDelta = new Vector2(IconSize * 0.7f, IconSize * 0.7f);
        var img = rt.GetComponent<Image>();
        img.sprite = gemSprite; img.preserveAspect = true; img.raycastTarget = false;
        rt.gameObject.SetActive(false);
        return new Flyer { rt = rt };
    }

    // A cut gem seen from the side: flat table on top, crown, pointed pavilion; cornered, dark outline, no glow.
    static Sprite MakeGemSprite(int size, out Texture2D tex)
    {
        tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { name = "DiamondIcon", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var outline = new Color(0.04f, 0.28f, 0.42f);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float edge = GemEdge(u, v);                       // > 0 inside, distance to the outline
                if (edge <= 0f) { px[y * size + x] = new Color32(0, 0, 0, 0); continue; }
                Color c = GemFacet(u, v);
                if (edge < 0.07f) c = outline;
                c.a = Mathf.Clamp01(edge * size * 0.5f);
                px[y * size + x] = c;
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    const float GemTop = 0.62f, GemGirdle = 0.18f, GemBottom = -0.92f, GemWide = 0.95f, GemTable = 0.58f;

    static float GemEdge(float u, float v)
    {
        if (v > GemTop || v < GemBottom) return -1f;
        float hw = v >= GemGirdle
            ? Mathf.Lerp(GemWide, GemTable, (v - GemGirdle) / (GemTop - GemGirdle))
            : GemWide * (v - GemBottom) / (GemGirdle - GemBottom);
        return Mathf.Min(hw - Mathf.Abs(u), v - GemBottom, GemTop - v) * 0.7f;
    }

    static Color GemFacet(float u, float v)
    {
        var table = new Color(0.86f, 0.98f, 1f);
        var lit = new Color(0.45f, 0.87f, 1f);
        var mid = new Color(0.22f, 0.66f, 0.95f);
        var dark = new Color(0.1f, 0.45f, 0.82f);
        if (v >= GemGirdle)
        {
            float edgeU = Mathf.Lerp(GemWide, GemTable, (v - GemGirdle) / (GemTop - GemGirdle));
            if (Mathf.Abs(u) < GemTable * 0.55f && v > GemTop * 0.62f) return table;          // table
            return u < 0f ? (Mathf.Abs(u) > edgeU * 0.6f ? mid : lit) : (Mathf.Abs(u) > edgeU * 0.6f ? dark : mid);
        }
        float t = (v - GemBottom) / (GemGirdle - GemBottom);                                      // 0 tip .. 1 girdle
        float w = Mathf.Abs(u) / Mathf.Max(0.01f, GemWide * t);
        if (w < 0.34f) return u < 0f ? lit : mid;                                                // centre facets
        return u < 0f ? mid : dark;
    }

    static Sprite MakePillSprite(int size, out Texture2D tex)
    {
        tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { name = "DiamondPill", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f, r = size * 0.5f - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        float b = size * 0.5f - 1f;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(b, b, b, b));
    }
}
