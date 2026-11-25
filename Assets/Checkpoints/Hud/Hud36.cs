using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// MainScene prop camera 3.6 top HUD (on the "Hud36" object). One clean top bar for a portrait phone, inside
/// Screen.safeArea with 40 px margins (1080 x 1920 reference):
///   left    PAUSE: a 110 rounded square in the coin pill's style, two white bars, tap area padded to 140.
///   centre  SCORE: a pill matching the coin pill (120 high), 62 pt tabular digits so the width never jitters, the pill
///           widening smoothly with the digits, a small low-contrast BEST under it, a 1.1x punch every 10 points.
///   right   the existing coin pill with the diamond pill under it (CoinCounterUI / CloseCallDiamondUI, untouched
///           look, flights land on the icons wherever they are); the power-up row directly under them, same margin.
/// When the three would collide on a narrow screen the whole bar shrinks together (CoinCounterUI.HudScale).
/// Also: the old corner score is hidden, MENU moves into the game-over panel's button row, the dev camera label is off.
/// Every hook it uses is opt-in, so other scenes (3.5) keep their HUD exactly as it was.
/// </summary>
[DefaultExecutionOrder(900)]   // before CheckpointRunController (1000) builds FROM START beside MENU
public class Hud36 : MonoBehaviour
{
    public TMP_FontAsset scoreFont;
    public TMP_FontAsset labelFont;
    public float margin = 40f;
    public float pillHeight = 120f;
    public float pauseSize = 110f;
    public float pauseHitSize = 140f;
    [Tooltip("3.11: also open pause from the raw touch / click when the EventSystem did not deliver the press (blocked by another canvas, a held second finger). Off = as 3.6-3.10.")]
    public bool rawTouchFallback = false;
    public int milestoneEvery = 10;
    [Tooltip("Size of the coin and diamond icons in the right-hand pills (1 = as before).")]
    public float pickupIconScale = 1f;
    public Color pillColor = new Color(0.04f, 0.05f, 0.09f, 0.45f);
    [Tooltip("3.11: size of the whole top bar (pause, score, coin and diamond pills) as a share of the 1080-wide reference. 1 = as 3.6-3.10.")]
    [Range(0.5f, 1f)] public float hudScale = 1f;
    [Tooltip("3.11: size of the score digits and the coin / diamond counts (1 = as before).")]
    [Range(0.6f, 1.2f)] public float hudTextScale = 1f;
    [Tooltip("3.11: the BEST line under the score: alpha (0.45 = as before).")]
    [Range(0f, 1f)] public float bestAlpha = 0.45f;

    CubeRailRunner runner;
    DynamicPathGenerator gen;
    RunnerHUD hud;
    Canvas canvas;
    CanvasGroup group, scoreGroup;
    RectTransform safe, pauseHit, pauseVisual, scorePill, scoreRt;
    TextMeshProUGUI scoreText, bestText;
    PowerUpHud powerHud;
    float pillW, punch, scale = 1f, lastLayoutW = -1f, lastCoinW = -1f;
    int shownScore = -1, best, lastBestSeen = -1;
    string bestKey;
    Rect lastSafe;

    /// <summary>The pause button's (padded) hit area, for the runner's touch blocker.</summary>
    public RectTransform PauseHitRect => pauseHit;
    public bool PauseVisible => group != null && group.alpha > 0.5f && pauseHit.gameObject.activeInHierarchy;
    /// <summary>Raised when the pause button is pressed (on press, not release).</summary>
    public event System.Action PausePressed;
    /// <summary>Frame of the last press the EventSystem delivered to the pause button (so the raw fallback never doubles it).</summary>
    public static int LastEventDownFrame = -1;

    void Awake() { CoinCounterUI.TextScale = hudTextScale; }

    void Start()
    {
        runner = FindFirstObjectByType<CubeRailRunner>();
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        hud = runner != null ? runner.GetComponent<RunnerHUD>() : null;
        var rig = FindFirstObjectByType<CameraParentRotator>();
#if !UNITY_EDITOR && DEVELOPMENT_BUILD
        if (rig != null) rig.hideModeLabel = true; // editor keeps the mode label for Z/X/C/V/B debugging
#endif
        if (hud != null)
        {
            hud.hideCornerScore = true;
            hud.PlaceMenuInRow(new Vector2(0f, -400f), new Vector2(300f, 120f));
        }
        CoinCounterUI.IconScale = pickupIconScale;
        bestKey = EndlessRun.CurrentBestKey(gameObject.scene.name);
        best = PlayerPrefs.GetInt(bestKey, 0);
        Build();
    }

    void OnDestroy()
    {
        CoinCounterUI.HudScale = 1f;
        CoinCounterUI.IconScale = 1f;
        CoinCounterUI.TextScale = 1f;
        CoinCounterUI.FreezeFlights = false;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    void Build()
    {
        var go = new GameObject("Hud36Canvas", typeof(RectTransform));
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 102;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        if (FindFirstObjectByType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        group = go.AddComponent<CanvasGroup>();

        safe = MainMenuController.NewRect(go.transform, "SafeArea");
        MainMenuController.Stretch(safe);
        safe.gameObject.AddComponent<SafeArea>();

        // pause: padded invisible hit area, the visible rounded square inside it
        Image hit = MainMenuController.NewImage(safe, "PauseHit", null, new Color(0f, 0f, 0f, 0f));
        pauseHit = hit.rectTransform;
        float pad = (pauseHitSize - pauseSize) * 0.5f;
        MainMenuController.Place(pauseHit, new Vector2(0f, 1f), new Vector2(margin - pad, -(margin - pad)), new Vector2(pauseHitSize, pauseHitSize), new Vector2(0f, 1f));
        var press = hit.gameObject.AddComponent<PauseButtonInput>();
        press.down = () => PausePressed?.Invoke();
        Image vis = MainMenuController.NewImage(pauseHit, "Visual", MenuIcons.Pill, pillColor);
        vis.type = Image.Type.Sliced;
        vis.pixelsPerUnitMultiplier = 32f / 30f;   // corner radius ~ 30
        vis.raycastTarget = false;
        pauseVisual = vis.rectTransform;
        MainMenuController.Place(pauseVisual, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(pauseSize, pauseSize), new Vector2(0.5f, 0.5f));
        for (int i = 0; i < 2; i++)
        {
            Image bar = MainMenuController.NewImage(pauseVisual, "Bar", MenuIcons.Pill, Color.white);
            bar.type = Image.Type.Sliced;
            bar.pixelsPerUnitMultiplier = 32f / 6f;
            bar.raycastTarget = false;
            MainMenuController.Place(bar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(i == 0 ? -13f : 13f, 0f), new Vector2(14f, 46f), new Vector2(0.5f, 0.5f));
        }

        // score pill, top centre
        Image pill = MainMenuController.NewImage(safe, "ScorePill", MenuIcons.Pill, pillColor);
        pill.type = Image.Type.Sliced;
        pill.pixelsPerUnitMultiplier = 32f / (pillHeight * 0.5f);
        pill.raycastTarget = false;
        scorePill = pill.rectTransform;
        MainMenuController.Place(scorePill, new Vector2(0.5f, 1f), new Vector2(0f, -margin), new Vector2(180f, pillHeight), new Vector2(0.5f, 1f));
        scoreText = NewText(scorePill, "Score", scoreFont, 62f * hudTextScale / Mathf.Max(0.5f, hudScale), Color.white);
        scoreRt = scoreText.rectTransform;
        MainMenuController.Stretch(scoreRt);
        scoreRt.offsetMin = new Vector2(0f, 4f);
        bestText = NewText(safe, "Best", labelFont, 26f * hudTextScale, new Color(1f, 1f, 1f, bestAlpha));
        MainMenuController.Place(bestText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -(margin + pillHeight + 6f)), new Vector2(300f, 36f), new Vector2(0.5f, 1f));
        bestText.characterSpacing = 8f;
        pillW = 180f;
    }

    TextMeshProUGUI NewText(Transform parent, string name, TMP_FontAsset font, float size, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = c;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    void Update()
    {
        if (runner == null || gen == null) return;
        float dt = Time.unscaledDeltaTime;
        bool show = !runner.IsWaitingToStart && !runner.IsGameOver && !runner.IsRebuilding;
        group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, dt * 6f);
        group.blocksRaycasts = show;
        // First-run tutorial: the score (it restarts from 0 at the hand-over) fades in only then; pause stays.
        if (scoreGroup == null && scorePill != null) scoreGroup = scorePill.gameObject.AddComponent<CanvasGroup>();
        if (scoreGroup != null) scoreGroup.alpha = TutorialHud.Alpha;
        if (bestText != null) bestText.alpha = TutorialHud.Alpha * (bestAlpha / 0.45f);

        int s = gen.DisplayScore;
        if (s != shownScore)
        {
            if (shownScore >= 0 && s > shownScore && milestoneEvery > 0 && s % milestoneEvery == 0) punch = 1f;
            shownScore = s;
            scoreText.SetText("<mspace=0.6em>{0}</mspace>", s);
        }
        // BEST: the saved best (RunnerHUD writes it at game over), only once there is one
        if (runner.IsGameOver || lastBestSeen < 0)
        {
            int b = PlayerPrefs.GetInt(bestKey, 0);
            if (b != lastBestSeen) { lastBestSeen = b; best = b; if (EndlessRun.Active) { if (b > 0) bestText.SetText("ENDLESS  -  BEST {0}", b); else bestText.text = "ENDLESS"; } else if (b > 0) bestText.SetText("BEST {0}", b); else bestText.text = ""; }
        }

        float target = Mathf.Max(170f, scoreText.preferredWidth + 88f);
        pillW = Mathf.Lerp(pillW, target, 1f - Mathf.Exp(-dt * 14f));
        scorePill.sizeDelta = new Vector2(pillW, pillHeight);
        punch = Mathf.MoveTowards(punch, 0f, dt / 0.2f);
        float p = 1f + 0.1f * Mathf.Sin(punch * Mathf.PI);
        scorePill.localScale = new Vector3(p * scale, p * scale, 1f);

        if (rawTouchFallback && PauseVisible && LastEventDownFrame != Time.frameCount) RawPressCheck();
        Layout();
    }

    // 3.11 fallback: a touch (or the editor's mouse) that begins inside the padded pause area opens pause even if the
    // EventSystem never delivered it. Pausing twice is harmless (PauseController ignores it while paused).
    void RawPressCheck()
    {
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            if (t.phase == TouchPhase.Began && RectTransformUtility.RectangleContainsScreenPoint(pauseHit, t.position, null)) { PausePressed?.Invoke(); return; }
        }
        if (Input.touchCount == 0 && Input.GetMouseButtonDown(0)
            && RectTransformUtility.RectangleContainsScreenPoint(pauseHit, Input.mousePosition, null)) PausePressed?.Invoke();
    }

    // The bar shrinks together when the pills would collide; the power-up row follows the pills.
    void Layout()
    {
        float W = safe.rect.width;
        var coins = runner.GetComponent<CoinCounterUI>();
        float coinW = coins != null && coins.Pill != null ? coins.Pill.sizeDelta.x : 240f;
        if (Mathf.Abs(W - lastLayoutW) > 0.5f || Mathf.Abs(coinW - lastCoinW) > 8f || Screen.safeArea != lastSafe)
        {
            lastLayoutW = W; lastCoinW = coinW; lastSafe = Screen.safeArea;
            float half = W * 0.5f - margin - 16f;
            float sRight = half / Mathf.Max(1f, pillW * 0.5f + coinW);
            float sLeft = half / Mathf.Max(1f, pillW * 0.5f + pauseSize);
            scale = Mathf.Clamp(Mathf.Min(hudScale, sRight, sLeft), Mathf.Min(0.6f, hudScale), hudScale);
            CoinCounterUI.HudScale = scale;
            pauseVisual.localScale = new Vector3(scale, scale, 1f);
            // keep the VISIBLE square on the margin whatever its size: the padded hit area is centred on it
            float inset = (pauseHitSize - pauseSize * scale) * 0.5f;
            pauseHit.anchoredPosition = new Vector2(margin - inset, -(margin - inset));

            if (powerHud == null) powerHud = FindFirstObjectByType<PowerUpHud>();
            if (powerHud != null)
            {
                float k = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
                float top = (Screen.height - Screen.safeArea.yMax) / k;
                float right = (Screen.width - Screen.safeArea.xMax) / k;
                powerHud.SetRowPosition(top + margin + (pillHeight * 2f + 16f) * scale + 16f, right + margin);
            }
        }
    }
}

/// <summary>Fires on press (the runner never sees this touch: CubeRailRunner.touchBlocker).</summary>
public class PauseButtonInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public System.Action down;
    public void OnPointerDown(PointerEventData e) { transform.localScale = Vector3.one * 0.94f; Hud36.LastEventDownFrame = Time.frameCount; down?.Invoke(); }
    public void OnPointerUp(PointerEventData e) => transform.localScale = Vector3.one;
}
