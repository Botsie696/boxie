using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Score (top left, +1 for every new path leg the runner reaches) and the game-over panel with a
/// restart button. Added to the runner by CubeRailRunner.Awake; the UI is built in code so the
/// scene needs no extra objects.
/// </summary>
public class RunnerHUD : MonoBehaviour
{
    CubeRailRunner runner;
    DynamicPathGenerator generator;

    int score;
    Text scoreText;
    CanvasGroup scoreGroup; // TUTORIAL ADDITION
    Text finalScoreText;
    GameObject gameOverPanel;
    GameObject playPanel;

    // ---- Checkpoint hooks (MainScene prop camera 3.6; null / unused everywhere else) ----
    /// <summary>Return true for a game-over tap that must NOT restart the run (it landed on an extra button).</summary>
    [System.NonSerialized] public System.Func<Vector2, bool> tapBlocker;
    /// <summary>The game-over panel and its MENU button (built in Start), for scenes that add buttons beside it.</summary>
    public RectTransform GameOverPanelRect => gameOverPanel != null ? (RectTransform)gameOverPanel.transform : null;
    public RectTransform MenuButtonRect => menuRect;
    /// <summary>The corner score is drawn by another HUD (3.6 Hud36).</summary>
    [System.NonSerialized] public bool hideCornerScore;
    /// <summary>Another prompt starts the run (3.6 tap-to-play): the PLAY panel never shows.</summary>
    [System.NonSerialized] public bool hidePlayPanel;
    /// <summary>True once PlaceMenuInRow moved MENU into the panel button row.</summary>
    public bool MenuInRow { get; private set; }
    /// <summary>Moves MENU from the top-right corner into the game-over panel button row (centre-anchored position, size).</summary>
    public void PlaceMenuInRow(Vector2 pos, Vector2 size)
    {
        if (menuRect == null) return;
        menuRect.anchorMin = menuRect.anchorMax = menuRect.pivot = new Vector2(0.5f, 0.5f);
        menuRect.anchoredPosition = pos;
        menuRect.sizeDelta = size;
        MenuInRow = true;
        var label = menuRect.GetComponentInChildren<Text>();
        if (label != null) label.rectTransform.sizeDelta = size - new Vector2(10f, 10f);
    }

    void Start()
    {
        runner = GetComponent<CubeRailRunner>();
        generator = FindFirstObjectByType<DynamicPathGenerator>();
        BuildUI();
        SetScore(0);
    }

    void Update()
    {
        if (runner == null) return;
        if (scoreGroup != null) scoreGroup.alpha = TutorialHud.Alpha; // TUTORIAL ADDITION

        if (playPanel.activeSelf && (hidePlayPanel || !runner.IsWaitingToStart)) playPanel.SetActive(false);
        // The corner score is replaced by the big one on the game-over screen.
        scoreText.gameObject.SetActive(!hideCornerScore && !runner.IsWaitingToStart && !gameOverPanel.activeSelf && !runner.IsRebuilding);
        if (runner.IsWaitingToStart) return;

        // A soft restart leaves the game-over state without reloading the scene.
        if (!runner.IsGameOver && gameOverPanel.activeSelf)
        {
            gameOverPanel.SetActive(false);
            seq = null;
        }

        if (runner.IsGameOver)
        {
            UpdateGameOver();
            return;
        }

        // The generator keeps the count, so the number on screen is the same one the path
        // generator rerolls its turn odds and steps the speed on.
        if (generator == null) return;
        if (generator.DisplayScore != score) SetScore(generator.DisplayScore); // + close-call bonus (0 without a DifficultyProfile)
    }

    // ------------------------------------------------------------------ game over

    const float FadeIn = 0.8f;
    const float TapLockout = 0.6f; // frantic taps as the runner falls must not restart instantly
    CanvasGroup overlayGroup;
    Image vignette;
    Text bestText, nearMissText, promptText;
    RectTransform coinRow, menuRect;
    Image coinIcon;
    Text coinCountText;
    const float SlideSeconds = 0.6f;
    static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
    GameOverSequence seq;
    float overlayT;
    string BestKey => EndlessRun.CurrentBestKey(SceneManager.GetActiveScene().name);   // an endless run keeps its own best per level

    void UpdateGameOver()
    {
        if (seq == null) seq = runner.GetComponent<GameOverSequence>();
        if (seq != null && !seq.OverlayReady) return;

        if (!gameOverPanel.activeSelf)
        {
            int best = PlayerPrefs.GetInt(BestKey, 0);
            bool newBest = score > best;
            if (newBest) { PlayerPrefs.SetInt(BestKey, score); PlayerPrefs.Save(); }
            finalScoreText.text = score.ToString();
            bestText.text = newBest ? "NEW BEST" : "BEST  " + best;
            int gap = best - score + 1;
            bool near = !newBest && best > 0 && score >= Mathf.CeilToInt(best * 0.9f);
            nearMissText.text = near ? "Only " + gap + (gap == 1 ? " point" : " points") + " to a new Best..." : "";
            var coins = runner.GetComponent<CoinCounterUI>();
            bool haveCoins = coins != null && coins.CoinSprite != null;
            if (haveCoins)
            {
                coinIcon.sprite = coins.CoinSprite;
                coinCountText.text = coins.Count.ToString();
                coins.SetHidden(true); // its counter now lives in the game-over screen
            }
            coinRow.gameObject.SetActive(haveCoins);
            gameOverPanel.SetActive(true);
            overlayT = 0f;
        }

        overlayT += Time.unscaledDeltaTime;
        float f = Mathf.Clamp01(overlayT / FadeIn);
        overlayGroup.alpha = f;
        vignette.color = new Color(0.03f, 0.06f, 0.18f, 0.55f * f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(overlayT * 4f);

        // Score glides in from the left, coins from the right, to where they rest.
        float es = EaseOut(overlayT / SlideSeconds);
        finalScoreText.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-700f, 0f, es), 160f);
        var fc = finalScoreText.color; fc.a = es; finalScoreText.color = fc;
        if (coinRow.gameObject.activeSelf)
        {
            float ec = EaseOut((overlayT - 0.15f) / SlideSeconds);
            coinRow.anchoredPosition = new Vector2(Mathf.Lerp(700f, 0f, ec), -240f);
            coinCountText.color = new Color(1f, 0.93f, 0.7f, ec);
            coinIcon.color = new Color(1f, 1f, 1f, ec);
        }
        promptText.color = new Color(0.75f, 0.97f, 1f, Mathf.Lerp(0.55f, 1f, pulse));

        if (Input.GetKeyDown(KeyCode.R) || overlayT < TapLockout) return;
        bool tapped = Input.GetKeyDown(KeyCode.Space);
        Vector2 pos = Input.mousePosition;
        if (Input.GetMouseButtonDown(0)) tapped = true;
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began) { tapped = true; pos = Input.GetTouch(0).position; }
        if (tapped && !RectTransformUtility.RectangleContainsScreenPoint(menuRect, pos, null)
            && !TutorialReplayButton.Contains(pos) && (tapBlocker == null || !tapBlocker(pos))) runner.Restart(); // TUTORIAL ADDITION: the "?" (false where there is none)
    }

    void SetScore(int value)
    {
        score = value;
        if (scoreText != null) scoreText.text = score.ToString();
    }

    // ------------------------------------------------------------------ UI

    void BuildUI()
    {
        var canvasGO = new GameObject("RunnerHUDCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Keep the score clear of the notch
        var safe = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeArea));
        safe.transform.SetParent(canvasGO.transform, false);
        scoreGroup = safe.AddComponent<CanvasGroup>(); // TUTORIAL ADDITION: score is hidden until the tutorial hands over

        // Score, top left
        scoreText = MakeText(safe.transform, "Score", font, 90, TextAnchor.UpperLeft, Color.white);
        var srt = scoreText.rectTransform;
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0, 1);
        srt.anchoredPosition = new Vector2(50, -50);
        srt.sizeDelta = new Vector2(500, 130);
        scoreText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3, -3);

        // Game over: dark-blue vignette, big score, best, near-miss line, pulsing runner outline + prompt.
        // No buttons - tap anywhere rebuilds (UpdateGameOver); only a small menu link in the corner.
        gameOverPanel = new GameObject("GameOverPanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        gameOverPanel.transform.SetParent(canvasGO.transform, false);
        var prt = (RectTransform)gameOverPanel.transform;
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        vignette = gameOverPanel.GetComponent<Image>();
        vignette.color = new Color(0.03f, 0.06f, 0.18f, 0f);
        vignette.raycastTarget = false;
        overlayGroup = gameOverPanel.GetComponent<CanvasGroup>();

        var pale = new Color(0.75f, 0.97f, 1f);
        finalScoreText = MakeText(gameOverPanel.transform, "FinalScore", font, 260, TextAnchor.MiddleCenter, Color.white);
        Place(finalScoreText.rectTransform, new Vector2(0, 160), new Vector2(900, 320));
        finalScoreText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(4, -4);

        bestText = MakeText(gameOverPanel.transform, "Best", font, 64, TextAnchor.MiddleCenter, pale);
        Place(bestText.rectTransform, new Vector2(0, -20), new Vector2(900, 100));

        nearMissText = MakeText(gameOverPanel.transform, "NearMiss", font, 46, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.6f));
        Place(nearMissText.rectTransform, new Vector2(0, -110), new Vector2(1000, 80));

        // Coin count, under the best / near-miss lines; it slides in from the right (UpdateGameOver)
        var row = new GameObject("CoinRow", typeof(RectTransform));
        row.transform.SetParent(gameOverPanel.transform, false);
        coinRow = (RectTransform)row.transform;
        Place(coinRow, new Vector2(0, -240), new Vector2(600, 120));
        coinIcon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        coinIcon.transform.SetParent(coinRow, false);
        Place(coinIcon.rectTransform, new Vector2(-70, 0), new Vector2(100, 100));
        coinIcon.preserveAspect = true;
        coinIcon.raycastTarget = false;
        coinCountText = MakeText(coinRow, "Count", font, 80, TextAnchor.MiddleLeft, new Color(1f, 0.93f, 0.7f));
        coinCountText.rectTransform.anchorMin = coinCountText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        coinCountText.rectTransform.pivot = new Vector2(0f, 0.5f);
        coinCountText.rectTransform.anchoredPosition = new Vector2(-5, 2);
        coinCountText.rectTransform.sizeDelta = new Vector2(300, 100);
        coinCountText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3, -3);
        coinRow.gameObject.SetActive(false);

        promptText = MakeText(gameOverPanel.transform, "Prompt", font, 56, TextAnchor.MiddleCenter, pale);
        Place(promptText.rectTransform, new Vector2(0, -560), new Vector2(1000, 90));
        promptText.text = "Tap Anywhere to Rebuild";

        // Small menu link, top right
        var menuGO = new GameObject("MainMenuButton", typeof(RectTransform), typeof(Image), typeof(Button));
        menuGO.transform.SetParent(gameOverPanel.transform, false);
        menuRect = (RectTransform)menuGO.transform;
        menuRect.anchorMin = menuRect.anchorMax = menuRect.pivot = new Vector2(1, 1);
        menuRect.anchoredPosition = new Vector2(-40, -60);
        menuRect.sizeDelta = new Vector2(260, 100);
        var menuImg = menuGO.GetComponent<Image>();
        menuImg.color = new Color(1f, 1f, 1f, 0.12f);
        var menuBtn = menuGO.GetComponent<Button>();
        menuBtn.targetGraphic = menuImg;
        menuBtn.onClick.AddListener(() => SceneManager.LoadScene("MainUi"));
        var menuLabel = MakeText(menuGO.transform, "Label", font, 40, TextAnchor.MiddleCenter, pale);
        Place(menuLabel.rectTransform, Vector2.zero, new Vector2(250, 90));
        menuLabel.text = "MENU";
        menuLabel.raycastTarget = false;

        gameOverPanel.SetActive(false);
        BuildPlayPanel(canvasGO.transform, font);
    }

    void BuildPlayPanel(Transform canvas, Font font)
    {
        playPanel = new GameObject("PlayPanel", typeof(RectTransform), typeof(Image));
        playPanel.transform.SetParent(canvas, false);
        var prt = (RectTransform)playPanel.transform;
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        playPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

        var btnGO = new GameObject("PlayButton", typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(playPanel.transform, false);
        Place((RectTransform)btnGO.transform, new Vector2(0, -200), new Vector2(520, 170));
        var img = btnGO.GetComponent<Image>();
        img.color = new Color(0.25f, 0.9f, 0.85f, 1f);
        var btn = btnGO.GetComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        btn.colors = colors;
        btn.onClick.AddListener(() => runner.BeginRun());

        var label = MakeText(btnGO.transform, "Label", font, 80, TextAnchor.MiddleCenter, new Color(0.05f, 0.15f, 0.2f));
        Place(label.rectTransform, Vector2.zero, new Vector2(500, 150));
        label.text = "PLAY";
        label.raycastTarget = false;
    }

    static Text MakeText(Transform parent, string name, Font font, int size, TextAnchor anchor, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.alignment = anchor;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
