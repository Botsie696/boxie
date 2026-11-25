using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// MainScene prop camera 3.6 pause (on the "Hud36" object): Playing -> Paused -> Resuming (3, 2, 1) -> Playing.
///
///   Pause       the pause button (on press), Android back, OnApplicationPause(true), focus loss. Never during game over,
///               a death, a rebuild or a checkpoint start's door exit. The button's touch never reaches the runner
///               (CubeRailRunner.touchBlocker), on press, release or a double tap.
///   Paused      timeScale and fixedDeltaTime are saved; time stops through TimeScaleMixer (Source.Pause = 0), audio
///               pauses, gameplay input is blocked (CubeRailRunner.pauseInput), coins in the air hold still
///               (CoinCounterUI.FreezeFlights). A 65% dark screen, a breathing TAP TO CONTINUE, RESTART and MENU.
///   Resuming    3 -> 2 -> 1, one per second (unscaled), each popping in at 1.3x and fading, with a light haptic.
///               The game runs meanwhile, slowed: the pause factor eases in from 0.1 to 1 (fixedDeltaTime follows through
///               the mixer), input is live so a corner can be met. Then GO, and the saved values are back exactly
///               (checked in dev builds). Pausing again during the countdown pauses cleanly.
/// One time owner (the mixer) means a hit-stop or slow-mo elsewhere can never leave time wrong; the close-call hit-stop
/// is skipped while the pause owns time (CloseCallFX.TimeLocked). Everything is prebuilt; nothing allocates per frame.
/// </summary>
public class PauseController : MonoBehaviour
{
    public enum State { Playing, Paused, Resuming }

    public TMP_FontAsset bigFont;
    public TMP_FontAsset labelFont;
    public float countdownSeconds = 3f;
    [Range(0.01f, 1f)] public float resumeStartScale = 0.1f;
    public Color good = new Color(1f, 0.79f, 0.27f, 1f);
    [Tooltip("3.11: a press of the pause button while pausing is not allowed yet (portal door exit, checkpoint start, a rebuild) waits this many seconds and pauses as soon as it is. 0 = the press is dropped, as in 3.6-3.10.")]
    public float queueEarlyPressSeconds = 0f;

    State state = State.Playing;
    CubeRailRunner runner;
    CheckpointRunController ctl;
    Hud36 hud;
    Canvas canvas;
    CanvasGroup pauseGroup;
    RectTransform pauseRoot;
    TextMeshProUGUI tapText, countText, restartSub;
    float savedScale = 1f, savedFixed = 0.02f, othersAtPause = 1f;
    float t, overlayAlpha, goT = -1f;
    int lastShown = -1;
    bool overlayShown;
    float pendingUntil = -1f;

    public State Current => state;

    void Start()
    {
        runner = FindFirstObjectByType<CubeRailRunner>();
        ctl = FindFirstObjectByType<CheckpointRunController>();
        hud = GetComponent<Hud36>();
        if (runner == null) { enabled = false; return; }
        if (hud != null) hud.PausePressed += OnPauseButton;
        runner.touchBlocker = BlocksTouch;
        CloseCallFX.TimeLocked = TimeIsLocked;
        Build();
    }

    void OnDestroy()
    {
        if (state != State.Playing) RestoreTime(false);
        if (runner != null) { runner.touchBlocker = null; runner.pauseInput = false; }
        CloseCallFX.TimeLocked = null;
        CoinCounterUI.FreezeFlights = false;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    bool BlocksTouch(Vector2 screenPos)
    {
        if (state == State.Paused) return true;
        return hud != null && hud.PauseHitRect != null && hud.PauseVisible
               && RectTransformUtility.RectangleContainsScreenPoint(hud.PauseHitRect, screenPos, null);
    }

    bool TimeIsLocked() => state != State.Playing;

    /// <summary>Pausing is allowed only while the runner is really running (not a death, rebuild or checkpoint door exit).</summary>
    public bool CanPause => runner != null && runner.IsRunning && !runner.IsGameOver && !runner.IsRebuilding
                            && (ctl == null || !ctl.InCheckpointEntrance);

    // ------------------------------------------------------------------ UI

    void Build()
    {
        var go = new GameObject("PauseCanvas", typeof(RectTransform));
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();

        // the pause screen: dark, tap anywhere to continue, two buttons in the thumb zone
        Image dim = MainMenuController.NewImage(go.transform, "Pause", null, new Color(0f, 0f, 0f, 0.65f));
        pauseRoot = dim.rectTransform;
        MainMenuController.Stretch(pauseRoot);
        pauseGroup = dim.gameObject.AddComponent<CanvasGroup>();
        var tap = dim.gameObject.AddComponent<Button>();
        tap.transition = Selectable.Transition.None;
        tap.onClick.AddListener(Continue);
        RectTransform safe = MainMenuController.NewRect(pauseRoot, "SafeArea");
        MainMenuController.Stretch(safe);
        safe.gameObject.AddComponent<SafeArea>();

        var paused = NewText(safe, "Paused", labelFont, 40f, new Color(1f, 1f, 1f, 0.7f));
        MainMenuController.Place(paused.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 190f), new Vector2(900f, 60f), new Vector2(0.5f, 0.5f));
        paused.text = "PAUSED";
        paused.characterSpacing = 14f;
        tapText = NewText(safe, "Tap", bigFont, 72f, Color.white);
        MainMenuController.Place(tapText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1000f, 130f), new Vector2(0.5f, 0.5f));
        tapText.text = "TAP TO CONTINUE";

        RectTransform restart = MakeButton(safe, "Restart", "RESTART", new Vector2(-170f, 300f), Restart);
        restartSub = NewText(restart, "Sub", labelFont, 22f, new Color(1f, 1f, 1f, 0.6f));
        MainMenuController.Place(restartSub.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(300f, 30f), new Vector2(0.5f, 0f));
        restartSub.characterSpacing = 4f;
        MakeButton(safe, "Menu", "MENU", new Vector2(170f, 300f), ToMenu);
        pauseRoot.gameObject.SetActive(false);

        // countdown + GO (no raycasts: input is live during the countdown)
        countText = NewText(go.transform, "Countdown", bigFont, 300f, Color.white);
        MainMenuController.Place(countText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(800f, 400f), new Vector2(0.5f, 0.5f));
        countText.gameObject.SetActive(false);
    }

    RectTransform MakeButton(Transform parent, string name, string label, Vector2 pos, UnityEngine.Events.UnityAction action)
    {
        Image img = MainMenuController.NewImage(parent, name, MenuIcons.Pill, new Color(1f, 1f, 1f, 0.14f));
        img.type = Image.Type.Sliced;
        var rt = img.rectTransform;
        MainMenuController.Place(rt, new Vector2(0.5f, 0f), pos, new Vector2(300f, 130f), new Vector2(0.5f, 0.5f));
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(action);
        img.gameObject.AddComponent<PressScale>();
        var t = NewText(rt, "Label", bigFont, 44f, Color.white);
        MainMenuController.Stretch(t.rectTransform);
        t.rectTransform.offsetMin = new Vector2(0f, 10f);
        t.text = label;
        t.characterSpacing = 4f;
        return rt;
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

    // ------------------------------------------------------------------ triggers

    void OnPauseButton()
    {
        if (state == State.Resuming || CanPause) Pause();
        else if (queueEarlyPressSeconds > 0f && runner != null && !runner.IsGameOver && !runner.IsWaitingToStart)
            pendingUntil = Time.unscaledTime + queueEarlyPressSeconds;
    }

    void OnApplicationPause(bool paused)
    {
        if (paused && (state == State.Resuming || (state == State.Playing && CanPause))) Pause();
    }

    void OnApplicationFocus(bool focus)
    {
        if (!focus && (state == State.Resuming || (state == State.Playing && CanPause))) Pause();
    }

    // ------------------------------------------------------------------ states

    public void Pause()
    {
        if (state == State.Paused) return;
        if (state == State.Playing)
        {
            savedScale = Time.timeScale;
            savedFixed = Time.fixedDeltaTime;
            othersAtPause = TimeScaleMixer.ProductWithout(TimeScaleMixer.Source.Pause);
        }
        state = State.Paused;
        TimeScaleMixer.Set(TimeScaleMixer.Source.Pause, 0f);
        runner.pauseInput = true;
        CoinCounterUI.FreezeFlights = true;
        countText.gameObject.SetActive(false);
        goT = -1f;
        if (ctl != null && ctl.IsCheckpointRun) restartSub.SetText("CHECKPOINT {0}", ctl.StartCheckpoint);
        else restartSub.text = "";
        pauseRoot.gameObject.SetActive(true);
        overlayShown = true;
        MenuFeedback.Light();
    }

    void Continue()
    {
        if (state != State.Paused) return;
        state = State.Resuming;
        t = 0f;
        lastShown = -1;
        overlayShown = false;
        runner.pauseInput = false;
        CoinCounterUI.FreezeFlights = false;
        TimeScaleMixer.Set(TimeScaleMixer.Source.Pause, resumeStartScale);
        countText.gameObject.SetActive(true);
    }

    void Restart()
    {
        if (state != State.Paused) return;
        if (ctl != null) ctl.BankCoins();
        RestoreTime(false);
        HideAll();
        runner.Restart();   // a checkpoint run restarts at its checkpoint
    }

    void ToMenu()
    {
        if (state != State.Paused) return;
        if (ctl != null) ctl.BankCoins();
        SaveSystem.Flush();
        RestoreTime(false);
        HideAll();
        SceneManager.LoadScene(GameLaunchRequest.MenuScene);
    }

    void HideAll()
    {
        overlayShown = false;
        overlayAlpha = 0f;
        pauseGroup.alpha = 0f;
        pauseRoot.gameObject.SetActive(false);
        countText.gameObject.SetActive(false);
    }

    // Back to the saved time, exactly; in dev builds, say so loudly if anything disagrees.
    void RestoreTime(bool check)
    {
        TimeScaleMixer.Set(TimeScaleMixer.Source.Pause, 1f);
        state = State.Playing;
        runner.pauseInput = false;
        CoinCounterUI.FreezeFlights = false;
        if (!check || !(Debug.isDebugBuild || Application.isEditor)) return;
        float others = TimeScaleMixer.ProductWithout(TimeScaleMixer.Source.Pause);
        float expectFixed = Mathf.Approximately(others, 1f) || others <= 0.0001f ? TimeScaleMixer.BaseFixedDelta : TimeScaleMixer.BaseFixedDelta * others;
        bool othersSame = Mathf.Abs(others - othersAtPause) < 0.0001f;
        if (Mathf.Abs(Time.timeScale - others) > 0.0001f || Mathf.Abs(Time.fixedDeltaTime - expectFixed) > 0.000001f
            || (othersSame && (Mathf.Abs(Time.timeScale - savedScale) > 0.0001f || Mathf.Abs(Time.fixedDeltaTime - savedFixed) > 0.000001f)))
            Debug.LogError("[Pause] time not restored: timeScale " + Time.timeScale + " (saved " + savedScale + "), fixedDeltaTime " + Time.fixedDeltaTime + " (saved " + savedFixed + ")", this);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (pendingUntil > 0f)
        {
            if (state != State.Playing || runner.IsGameOver || Time.unscaledTime > pendingUntil) pendingUntil = -1f;
            else if (CanPause) { pendingUntil = -1f; Pause(); }
        }

        // Android back: pause, or continue when paused
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (state == State.Paused) Continue();
            else if (state == State.Resuming || CanPause) Pause();
        }

        // the pause screen fades in/out; TAP TO CONTINUE breathes
        overlayAlpha = Mathf.MoveTowards(overlayAlpha, overlayShown ? 1f : 0f, dt / 0.16f);
        if (pauseRoot.gameObject.activeSelf)
        {
            pauseGroup.alpha = overlayAlpha;
            pauseGroup.blocksRaycasts = overlayShown;
            float b = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.6f);
            tapText.alpha = Mathf.Lerp(0.62f, 1f, b);
            float s = 1f + 0.035f * b;
            tapText.rectTransform.localScale = new Vector3(s, s, 1f);
            if (!overlayShown && overlayAlpha <= 0f) pauseRoot.gameObject.SetActive(false);
        }

        if (state == State.Resuming)
        {
            if (runner.IsGameOver || runner.IsRebuilding)
            {
                RestoreTime(false);
                countText.gameObject.SetActive(false);
                return;
            }
            t += dt;
            float k = Mathf.Clamp01(t / countdownSeconds);
            TimeScaleMixer.Set(TimeScaleMixer.Source.Pause, Mathf.Lerp(resumeStartScale, 1f, k * k));   // ease-in
            int n = Mathf.Clamp(3 - Mathf.FloorToInt(t), 1, 3);
            if (n != lastShown)
            {
                lastShown = n;
                countText.SetText("{0}", n);
                countText.color = Color.white;
                MenuFeedback.Light();
            }
            float u = t - Mathf.Floor(t);
            float pop = u < 0.18f ? Mathf.Lerp(1.3f, 1f, Smooth(u / 0.18f)) : 1f;
            countText.rectTransform.localScale = new Vector3(pop, pop, 1f);
            countText.alpha = u < 0.65f ? 1f : 1f - Smooth((u - 0.65f) / 0.35f);
            if (t >= countdownSeconds)
            {
                RestoreTime(true);
                goT = 0f;
                countText.text = "GO";
                countText.color = good;
            }
        }
        else if (goT >= 0f)
        {
            goT += dt;
            float k = Mathf.Clamp01(goT / 0.45f);
            float s = Mathf.Lerp(1.25f, 1f, Smooth(k));
            countText.rectTransform.localScale = new Vector3(s, s, 1f);
            countText.alpha = 1f - Smooth(k);
            if (k >= 1f) { goT = -1f; countText.gameObject.SetActive(false); }
        }
    }

    void LateUpdate()
    {
        // The pause owns time: anything that wrote timeScale behind the mixer's back is undone at once.
        if (state == State.Paused && Time.timeScale != 0f) TimeScaleMixer.Set(TimeScaleMixer.Source.Pause, 0f);
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}
