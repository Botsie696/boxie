using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "close call" moment and the run's small success cues (DifficultyProfile.feedbackFX). Added to the runner
/// at run time by DynamicPathGenerator when the scene has a profile, so no scene carries it by hand.
///
/// On a rescue (CubeRailRunner.Rescued: a correct input slightly late, saved):
///   * "+1" pops out of the runner, overshoots, then rises and fades (the bonus point, DynamicPathGenerator.AddBonus)
///     with a small "CLOSE!" under it - near the runner, never centred, 0.75 s in all;
///   * the screen edges flash warm gold for a beat; the score in the corner punches;
///   * RunnerTrailFX: a white-gold shockwave and a spray of sparks;
///   * ActiveFaceHighlight flashes white-hot; the chevrons on show flare (TurnFaceArrows.Burst);
///   * a 35 ms hit-stop at timeScale 0.4 (never inside a boost);
///   * (CubeRailRunner itself plays the medium haptic.)
///
/// Also: a small chevron flare when a corner is made with time to spare, a flash when a boost is cleared,
/// and the trail glowing a little brighter as the wave builds to its peak. Purely visual.
/// </summary>
[DisallowMultipleComponent]
public class CloseCallFX : MonoBehaviour
{
    [Tooltip("Colour of the +1 and the shockwave.")]
    public Color accent = new Color(1f, 0.82f, 0.25f);
    [Tooltip("Gold glow along the screen edges on a close call. Off by default; logic kept.")]
    public bool showEdgeFlash = false;
    [Tooltip("The +N / CLOSE! popup next to the runner (N = the close-call streak). The diamond also flies to the diamond counter (CloseCallDiamondUI).")]
    public bool showPopup = true;
    [Tooltip("Size multiplier of the +1 / CLOSE! popup.")]
    public float popupSize = 0.5f;
    [Tooltip("Seconds the +1 is on screen.")]
    public float popupSeconds = 0.75f;
    [Tooltip("Screen pixels (at 1920 tall) the +1 rises while it fades.")]
    public float popupRise = 150f;
    [Tooltip("Seconds of slack at a corner that count as 'made it with time to spare' (small chevron flare).")]
    public float cleanCornerSlack = 0.25f;

    CubeRailRunner runner;
    DynamicPathGenerator gen;
    RunnerTrailFX trail;
    ActiveFaceHighlight lane;
    TurnFaceArrows arrows;
    Camera cam;

    RectTransform canvasRect;
    RectTransform popup;
    Text plusText, closeText;
    Outline plusOutline;
    Image edgeFlash;
    Text hudScore;
    Vector3 hudScoreScale = Vector3.one;
    float popupT = -1f, edgeT = -1f, scoreT = -1f;
    Vector3 popupWorld;
    Coroutine hitStop;
    int lastBoostId = -1;
    bool wasOnBoost;

    void Start()
    {
        runner = GetComponent<CubeRailRunner>();
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        trail = GetComponent<RunnerTrailFX>();
        lane = GetComponent<ActiveFaceHighlight>();
        arrows = gen != null ? gen.GetComponent<TurnFaceArrows>() : null;
        if (runner != null) { runner.Rescued += OnRescued; runner.Landed += OnLanded; runner.TapTaken += OnTap; }
        BuildUI();
        BuildRim();
        if (GetComponent<CloseCallDiamondUI>() == null) gameObject.AddComponent<CloseCallDiamondUI>();
    }

    void OnDestroy()
    {
        if (runner != null) { runner.Rescued -= OnRescued; runner.Landed -= OnLanded; runner.TapTaken -= OnTap; }
        if (canvasRect != null) Destroy(canvasRect.gameObject);
    }

    // ------------------------------------------------------------------ events

    void OnRescued(CubeRailRunner.RescueInfo info)
    {
        popupWorld = info.position;
        edgeT = 0f;
        scoreT = 0f;
        if (showPopup && popup != null)
        {
            if (plusText != null) plusText.text = "+" + Mathf.Max(1, gen != null ? gen.CloseCallStreak : 1);   // the streak: +1, +2, +3 ...
            popupT = 0f;
            popup.gameObject.SetActive(true);
        }
        if (edgeFlash != null && showEdgeFlash) edgeFlash.gameObject.SetActive(true);

        if (trail != null) trail.CloseCallBurst(accent);
        if (lane != null) lane.Flash(1.3f);
        if (arrows != null) arrows.Burst(1f);

        var P = runner.Profile;
        if (P != null && P.rescueHitStop && !info.onBoost && P.hitStopSeconds > 0f && (TimeLocked == null || !TimeLocked()))
        {
            if (hitStop != null) StopCoroutine(hitStop);
            hitStop = StartCoroutine(HitStop(P.hitStopSeconds, P.hitStopTimeScale));
        }
    }

    /// <summary>Pause hook (3.6): while this returns true the hit-stop is skipped (the pause owns time). Null elsewhere.</summary>
    public static System.Func<bool> TimeLocked;

    float savedScale = 1f;
    IEnumerator HitStop(float seconds, float scale)
    {
        savedScale = Time.timeScale > 0.0001f ? Time.timeScale : 1f;
        Time.timeScale = savedScale * scale;
        yield return new WaitForSecondsRealtime(seconds / Mathf.Max(0.1f, savedScale));
        // Only put it back if nothing else changed it meanwhile.
        if (Mathf.Abs(Time.timeScale - savedScale * scale) < 0.0001f) Time.timeScale = savedScale;
        hitStop = null;
    }

    void OnDisable()
    {
        if (hitStop != null) { Time.timeScale = savedScale; hitStop = null; }
    }

    // A corner made with time to spare: a small flare. A boost cleared: a flash and a ring.
    int tapsSinceLanding;
    float lastTapAt = -10f;
    void OnTap(int dir) { tapsSinceLanding++; lastTapAt = Time.time; }

    void OnLanded()
    {
        if (runner == null || gen == null) return;
        float slack = runner.LastLandingTime - lastTapAt;
        if (tapsSinceLanding > 0 && slack > cleanCornerSlack && arrows != null) arrows.Burst(0.35f);
        tapsSinceLanding = 0;

        bool onBoost = gen.PlayerOnZigZagBoost;
        if (wasOnBoost && !onBoost && !runner.IsGameOver && trail != null) trail.SmallBurst(new Color(0.6f, 1f, 0.95f), 6f);
        wasOnBoost = onBoost;
        lastBoostId = gen.PlayerBoostId;
    }

    // ------------------------------------------------------------------ per frame

    void LateUpdate()
    {
        if (runner == null) return;
        if (cam == null) cam = Camera.main;
        float dt = Time.unscaledDeltaTime;

        // Trail brightness rides the wave: calm after the portal, brighter through the build, hottest at the peak.
        if (trail != null && gen != null)
        {
            float goal = 1f;
            switch (gen.PlayerWavePhase)
            {
                case WavePhase.Build: goal = 1.1f; break;
                case WavePhase.Peak: goal = 1.25f; break;
            }
            if (gen.PlayerOnZigZagBoost) goal = 1.3f;
            trail.externalIntensity = Mathf.MoveTowards(trail.externalIntensity, goal, dt * 0.6f);
        }

        AnimatePopup(dt);
        AnimateEdge(dt);
        AnimateScore(dt);
        if (rimRenderer != null && cubeRenderer != null) rimRenderer.enabled = cubeRenderer.enabled; // hides with the shatter
    }

    MeshRenderer rimRenderer, cubeRenderer;

    static float EaseOutBack(float x) { const float c = 2.2f; x -= 1f; return 1f + (c + 1f) * x * x * x + c * x * x; }

    void AnimatePopup(float dt)
    {
        if (popup == null || popupT < 0f) return;
        popupT += dt;
        float u = popupT / popupSeconds;
        if (u >= 1f || cam == null) { popupT = -1f; popup.gameObject.SetActive(false); return; }

        // Follows the runner (it is fast) from just above it, so it reads as coming off the cube.
        Vector3 anchor = Vector3.Lerp(popupWorld, transform.position, 0.6f);
        Vector3 sp = cam.WorldToScreenPoint(anchor);
        if (sp.z < 0f) { popup.gameObject.SetActive(false); return; }
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out Vector2 local);

        float pop = EaseOutBack(Mathf.Clamp01(u / 0.22f));                 // 0 -> overshoot -> 1 in 0.16 s
        float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.15f) / 0.85f));
        float fade = 1f - Mathf.Clamp01((u - 0.55f) / 0.45f);
        popup.anchoredPosition = local + new Vector2(70f, 95f + popupRise * rise);
        popup.localScale = Vector3.one * Mathf.Lerp(0.2f, 1f, pop) * popupSize * (1f + 0.08f * Mathf.Sin(u * Mathf.PI));
        popup.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-12f, 4f, Mathf.Clamp01(u / 0.3f)));

        Color c = Color.Lerp(Color.white, accent, Mathf.Clamp01(u * 3f));
        c.a = fade;
        plusText.color = c;
        if (plusOutline != null) plusOutline.effectColor = new Color(0.35f, 0.12f, 0f, 0.85f * fade);
        closeText.color = new Color(1f, 0.95f, 0.8f, fade * Mathf.Clamp01((u - 0.08f) * 6f));
    }

    void AnimateEdge(float dt)
    {
        if (edgeFlash == null || edgeT < 0f) return;
        edgeT += dt;
        float u = edgeT / 0.35f;
        if (u >= 1f) { edgeT = -1f; edgeFlash.gameObject.SetActive(false); return; }
        float a = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI) * 0.275f;
        edgeFlash.color = new Color(accent.r, accent.g * 0.9f, accent.b * 0.6f, a);
    }

    void AnimateScore(float dt)
    {
        if (hudScore == null)
        {
            var go = GameObject.Find("RunnerHUDCanvas/SafeArea/Score");
            if (go != null) { hudScore = go.GetComponent<Text>(); hudScoreScale = hudScore.rectTransform.localScale; }
        }
        if (hudScore == null || scoreT < 0f) return;
        scoreT += dt;
        float u = scoreT / 0.4f;
        if (u >= 1f) { scoreT = -1f; hudScore.rectTransform.localScale = hudScoreScale; hudScore.color = Color.white; return; }
        float k = Mathf.Sin(u * Mathf.PI);
        hudScore.rectTransform.localScale = hudScoreScale * (1f + 0.35f * k);
        hudScore.color = Color.Lerp(Color.white, accent, k);
    }

    // ------------------------------------------------------------------ building

    void BuildUI()
    {
        var canvasGO = new GameObject("CloseCallCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 101;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = (RectTransform)canvasGO.transform;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Edge flash: a soft frame, brightest at the screen edges, transparent in the middle.
        var edgeGO = new GameObject("EdgeFlash", typeof(RectTransform), typeof(Image));
        edgeGO.transform.SetParent(canvasGO.transform, false);
        var ert = (RectTransform)edgeGO.transform;
        ert.anchorMin = Vector2.zero; ert.anchorMax = Vector2.one; ert.offsetMin = ert.offsetMax = Vector2.zero;
        edgeFlash = edgeGO.GetComponent<Image>();
        edgeFlash.sprite = MakeEdgeSprite(64);
        edgeFlash.raycastTarget = false;
        edgeFlash.color = new Color(1f, 0.8f, 0.3f, 0f);
        edgeGO.SetActive(false);

        var root = new GameObject("CloseCallPopup", typeof(RectTransform));
        root.transform.SetParent(canvasGO.transform, false);
        popup = (RectTransform)root.transform;
        popup.anchorMin = popup.anchorMax = new Vector2(0.5f, 0.5f);
        popup.pivot = new Vector2(0.5f, 0.5f);
        popup.sizeDelta = new Vector2(320f, 220f);

        plusText = MakeText(popup, "Plus", font, 132, new Vector2(0f, 20f), new Vector2(320f, 160f));
        plusText.text = "+1";
        plusOutline = plusText.gameObject.AddComponent<Outline>();
        plusOutline.effectDistance = new Vector2(5f, -5f);
        var sh = plusText.gameObject.AddComponent<Shadow>();
        sh.effectDistance = new Vector2(0f, -8f);
        sh.effectColor = new Color(0f, 0f, 0f, 0.35f);

        closeText = MakeText(popup, "Close", font, 44, new Vector2(0f, -62f), new Vector2(320f, 60f));
        closeText.text = "CLOSE!";
        closeText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -3f);
        root.SetActive(false);
    }

    static Text MakeText(Transform parent, string name, Font font, int size, Vector2 pos, Vector2 box)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        return t;
    }

    // A 9-sliced-looking soft frame: alpha rises toward the border.
    static Sprite MakeEdgeSprite(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "CloseCallEdge" };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Min(x, n - 1 - x) / (n * 0.5f), dy = Mathf.Min(y, n - 1 - y) / (n * 0.5f);
                float d = Mathf.Min(dx, dy);
                float a = Mathf.Pow(1f - Mathf.Clamp01(d / 0.35f), 2.2f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    // A bright rim round the runner so it reads at the widest zoom (DifficultyProfile.runnerRimMaterial).
    void BuildRim()
    {
        var P = runner != null ? runner.Profile : null;
        if (P == null || P.runnerRimMaterial == null) return;
        Transform mesh = transform.Find("VisualWrapper/PivotContainer/CubeMesh");
        if (mesh == null || mesh.Find("RunnerRim") != null) return;
        var mf = mesh.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        var rim = new GameObject("RunnerRim");
        rim.layer = mesh.gameObject.layer;
        rim.transform.SetParent(mesh, false);
        rim.transform.localScale = Vector3.one * P.runnerRimScale;
        rim.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        var r = rim.AddComponent<MeshRenderer>();
        r.sharedMaterial = P.runnerRimMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        rimRenderer = r;
        cubeRenderer = mesh.GetComponent<MeshRenderer>();
    }
}
