using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Coin counter in the top right: a coin icon and the run's count on a soft pill. A collected coin
/// flies from where it was picked up into the icon, and the count ticks up with a small punch as it
/// lands. Built in code like RunnerHUD, on its own canvas, so the score HUD is left alone and only
/// this small canvas re-batches while a coin is in the air. Keeps clear of a notch through
/// Screen.safeArea. Added to the runner by CoinCollector.
/// </summary>
public class CoinCounterUI : MonoBehaviour
{
    [Tooltip("Seconds a collected coin takes to fly into the counter.")]
    [Range(0.1f, 1.5f)] public float flySeconds = 0.5f;
    [Tooltip("Most coins in the air at once; past this the count just ticks up.")]
    [Range(0, 40)] public int maxFlyingCoins = 16;

    class Flyer
    {
        public RectTransform rt;
        public Vector2 from;
        public float t;
        public int total;
        public float arc;
    }

    const float Margin = 40f;
    const float PillHeight = 120f;
    const float IconSize = 104f;
    const float PunchSeconds = 0.22f;

    // ---- 3.6 HUD hooks (defaults = as before) ----
    /// <summary>True while the pause screen is up: coins in the air (here and in CloseCallDiamondUI) hold still.</summary>
    public static bool FreezeFlights;
    /// <summary>Scale of the coin and diamond pills (the 3.6 top bar shrinks them together on very narrow screens).</summary>
    public static float HudScale = 1f;
    /// <summary>Size factor of the coin and diamond icons inside their pills (Hud36.pickupIconScale; 1 = as before).</summary>
    public static float IconScale = 1f;
    /// <summary>Size factor of the count text in the coin and diamond pills (Hud36.hudTextScale; 1 = as before).</summary>
    public static float TextScale = 1f;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetHooks() { FreezeFlights = false; HudScale = 1f; IconScale = 1f; TextScale = 1f; }
    public int FlyingCount => flying.Count;
    public int LandedCount => landedTotal;
    public RectTransform Pill => pill;

    CoinCollector collector;
    Canvas canvas;
    CanvasGroup hudGroup; // TUTORIAL ADDITION
    RectTransform pill;
    RectTransform icon;
    Text countText;
    readonly List<Flyer> flying = new List<Flyer>();
    readonly Stack<Flyer> spare = new Stack<Flyer>();
    int landedTotal;
    int flyerSerial;
    float punch;
    Rect appliedSafeArea;
    Vector3 appliedScreen;
    Texture2D coinTex, pillTex;
    Sprite coinSprite, pillSprite;

    void Start()
    {
        collector = GetComponent<CoinCollector>();
        if (collector == null) { enabled = false; return; }

        BuildUI();
        landedTotal = collector.currentScore;
        SetCount(landedTotal);
        collector.CoinCollected += OnCoinCollected;
    }

    void OnDestroy()
    {
        if (collector != null) collector.CoinCollected -= OnCoinCollected;
        if (canvas != null) Destroy(canvas.gameObject);
        if (coinSprite != null) Destroy(coinSprite);
        if (pillSprite != null) Destroy(pillSprite);
        if (coinTex != null) Destroy(coinTex);
        if (pillTex != null) Destroy(pillTex);
    }

    bool hidden;
    public bool IsHidden => hidden;
    public Sprite CoinSprite => coinSprite;
    public int Count => collector != null ? collector.currentScore : 0;

    /// <summary>Hides the corner counter (the game-over screen shows the count itself).</summary>
    public void SetHidden(bool value) { hidden = value; }

    /// <summary>A new run: count back to 0, coins in flight dropped.</summary>
    public void ResetCount()
    {
        for (int i = flying.Count - 1; i >= 0; i--) { flying[i].rt.gameObject.SetActive(false); spare.Push(flying[i]); }
        flying.Clear();
        hidden = false;
        if (collector != null) collector.currentScore = 0;
        landedTotal = 0;
        if (countText != null) SetCount(0);
    }

    void OnCoinCollected(int total, Vector3 worldPos)
    {
        Camera cam = Camera.main;
        if (canvas == null || cam == null || flying.Count >= maxFlyingCoins)
        {
            Land(total);
            return;
        }

        Vector3 screen = cam.WorldToScreenPoint(worldPos);
        if (screen.z < 0f)
        {
            Land(total);
            return;
        }

        Flyer f = spare.Count > 0 ? spare.Pop() : NewFlyer();
        f.from = screen;
        f.t = 0f;
        f.total = total;
        f.arc = (flyerSerial++ % 2 == 0 ? 1f : -1f) * 70f;   // alternate sides so a run fans out
        f.rt.position = screen;
        f.rt.localScale = Vector3.one;
        f.rt.gameObject.SetActive(true);
        flying.Add(f);
    }

    void Update()
    {
        if (canvas == null) return;
        if (hudGroup != null) hudGroup.alpha = hidden ? 0f : TutorialHud.Alpha; // TUTORIAL ADDITION: hidden until the tutorial hands over
        FitSafeArea();

        float dt = FreezeFlights ? 0f : Time.unscaledDeltaTime;
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

            // Eases out of the pickup, then is pulled into the counter on a slight arc.
            float e = f.t * f.t * (3f - 2f * f.t);
            e *= e;
            Vector2 d = target - f.from;
            Vector2 side = d.sqrMagnitude > 1f ? new Vector2(-d.y, d.x).normalized : Vector2.zero;
            Vector2 p = f.from + d * e + side * (Mathf.Sin(f.t * Mathf.PI) * f.arc * k);
            f.rt.position = p;

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
        icon.sizeDelta = new Vector2(IconSize, IconSize) * IconScale;
        Rect sa = Screen.safeArea;
        var screen = new Vector3(Screen.width, Screen.height, canvas.scaleFactor * HudScale);
        if (sa == appliedSafeArea && screen == appliedScreen) return;
        appliedSafeArea = sa;
        appliedScreen = screen;

        float k = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        float right = (Screen.width - sa.xMax) / k;
        float top = (Screen.height - sa.yMax) / k;
        pill.anchoredPosition = new Vector2(-(Margin + right), -(Margin + top));
        pill.localScale = new Vector3(HudScale, HudScale, 1f);
    }

    // ------------------------------------------------------------------ UI

    void BuildUI()
    {
        var canvasGO = new GameObject("CoinHUDCanvas");
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 101;   // over RunnerHUD (100), so the count still reads over the game-over panel
        hudGroup = canvasGO.AddComponent<CanvasGroup>(); // TUTORIAL ADDITION
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        // No GraphicRaycaster: nothing here is tappable, so touches all go to the game.

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        coinSprite = MakeCoinSprite(128, out coinTex);
        pillSprite = MakePillSprite(64, out pillTex);

        pill = new GameObject("CoinCounter", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        pill.SetParent(canvasGO.transform, false);
        pill.anchorMin = pill.anchorMax = pill.pivot = new Vector2(1f, 1f);
        pill.sizeDelta = new Vector2(PillHeight * 2f, PillHeight);
        var pillImg = pill.GetComponent<Image>();
        pillImg.sprite = pillSprite;
        pillImg.type = Image.Type.Sliced;
        pillImg.pixelsPerUnitMultiplier = 31f / (PillHeight * 0.5f);   // corner radius = half the height
        pillImg.color = new Color(0.04f, 0.05f, 0.09f, 0.45f);
        pillImg.raycastTarget = false;

        icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        icon.SetParent(pill, false);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
        icon.pivot = new Vector2(0.5f, 0.5f);
        icon.anchoredPosition = new Vector2(PillHeight * 0.5f, 0f);
        icon.sizeDelta = new Vector2(IconSize, IconSize);
        var iconImg = icon.GetComponent<Image>();
        iconImg.sprite = coinSprite;
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;

        countText = new GameObject("Count", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        RectTransform trt = countText.rectTransform;
        trt.SetParent(pill, false);
        trt.anchorMin = trt.anchorMax = new Vector2(0f, 0.5f);
        trt.pivot = new Vector2(0f, 0.5f);
        trt.anchoredPosition = new Vector2(PillHeight + 8f, 2f);
        trt.sizeDelta = new Vector2(400f, PillHeight);
        countText.font = font;
        countText.fontSize = Mathf.RoundToInt(80 * TextScale);
        countText.fontStyle = FontStyle.Bold;
        countText.alignment = TextAnchor.MiddleLeft;
        countText.color = new Color(1f, 0.93f, 0.7f);
        countText.horizontalOverflow = HorizontalWrapMode.Overflow;
        countText.verticalOverflow = VerticalWrapMode.Overflow;
        countText.raycastTarget = false;
        countText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3, -3);

        FitSafeArea();
    }

    Flyer NewFlyer()
    {
        var rt = new GameObject("FlyingCoin", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        rt.SetParent(canvas.transform, false);
        rt.sizeDelta = new Vector2(IconSize * 0.6f, IconSize * 0.6f);
        var img = rt.GetComponent<Image>();
        img.sprite = coinSprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
        rt.gameObject.SetActive(false);
        return new Flyer { rt = rt };
    }

    // A gold coin in the colours of Coins_1's two materials (body 1/0.68/0.14, inset face
    // 0.97/0.51/0.13): bevelled rim lit from the top left, a pressed-in face, a dark outline so it
    // reads on any background. Drawn once at start.
    static Sprite MakeCoinSprite(int size, out Texture2D tex)
    {
        tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "CoinIcon",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var outline = new Color(0.4f, 0.19f, 0.02f);
        var rimLit = new Color(1f, 0.88f, 0.4f);
        var rimShade = new Color(0.88f, 0.5f, 0.07f);
        var faceLit = new Color(1f, 0.72f, 0.2f);
        var faceShade = new Color(0.86f, 0.44f, 0.09f);
        var groove = new Color(0.62f, 0.3f, 0.04f);

        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        float r = size * 0.5f - 1.5f;
        float aa = 1.5f / r;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / r;
                float dy = (y - c) / r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float light = Mathf.Clamp01(0.5f + 0.5f * (-0.55f * dx + 0.83f * dy));

                // Inset face is lit the other way round from the rim, so it reads as pressed in.
                Color col = Color.Lerp(faceLit, faceShade, light);
                col = Color.Lerp(col, groove, Edge(d, 0.66f, aa));
                col = Color.Lerp(col, Color.Lerp(rimShade, rimLit, light), Edge(d, 0.71f, aa));
                col = Color.Lerp(col, outline, Edge(d, 0.91f, aa));

                float hx = dx + 0.42f, hy = dy - 0.5f;
                float hl = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) / 0.32f) * (1f - Edge(d, 0.91f, aa));
                col = Color.Lerp(col, Color.white, hl * hl * 0.55f);

                col.a = Mathf.Clamp01((1f - d) / aa);
                px[y * size + x] = col;
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // White disc with a soft edge, 9-sliced into a pill of any width.
    static Sprite MakePillSprite(int size, out Texture2D tex)
    {
        tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "CoinPill",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        float r = size * 0.5f - 1f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                byte a = (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, true);
        float b = size * 0.5f - 1f;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(b, b, b, b));
    }

    static float Edge(float d, float edge, float aa) => Mathf.Clamp01((d - edge) / aa + 0.5f);
}
