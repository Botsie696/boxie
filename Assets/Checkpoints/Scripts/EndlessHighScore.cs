using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Endless runs only (added by CheckpointRunController when EndlessRun.Active): marks where the level's endless best lies
/// on the track, and celebrates passing it. Everything is tinted with the level's own accent (CheckpointProfile.lockedTeaseColor).
///
///   Gate     A "special block" on the leg whose score is the best: a square frame of four beams with a bright block in each
///            corner, standing round the leg (the runner passes through it; no colliders). It idles with a slow spin and a
///            glow pulse. A second, smaller gate stands at best + 50 % (a stretch goal).
///   Burst    As the score reaches a gate's value the gate flares and bursts outward (and the runner's trail bursts).
///   Popup    The number and "HIGH SCORE" (or "BEST +50%") pop out beside the runner, drawn exactly like the close-call "+1"
///            popup (CloseCallFX: same canvas scale, size multiplier, timing, rise and wobble) but in the theme colour.
///
/// Reads the saved endless best under EndlessRun.BestKey; with no best yet (a level's first endless run) it does nothing.
/// A soft restart (score drops) re-arms everything with the best the finished run saved.
/// </summary>
[DisallowMultipleComponent]
public class EndlessHighScore : MonoBehaviour
{
    [Tooltip("Size multiplier of the popup (CloseCallFX.popupSize = 0.5).")]
    public float popupSize = 0.5f;
    public float popupSeconds = 0.75f;
    [Tooltip("Screen pixels (at 1920 tall) the popup rises while it fades.")]
    public float popupRise = 150f;
    [Tooltip("Gate frame half-width in cubes: the runner (on any face) passes inside it.")]
    public float gateHalf = 1.9f;
    [Tooltip("Second gate at best + this fraction.")]
    public float stretchFraction = 0.5f;

    class Target
    {
        public int score;
        public string label;
        public bool placed, hit;
        public bool big;
        public Gate gate;
    }

    DynamicPathGenerator gen;
    CubeRailRunner runner;
    RunnerTrailFX trail;
    Camera cam;
    Color accent = new Color(1f, 0.82f, 0.25f);
    Color outline = new Color(0.25f, 0.1f, 0f, 0.85f);
    string bestKey;
    int best, lastScore;
    float nextScan;
    readonly Target[] targets = { new Target { big = true }, new Target() };
    Material gateMat;

    RectTransform canvasRect, popup;
    Text bigText, smallText;
    Outline bigOutline;
    float popupT = -1f;
    Vector3 popupWorld;

    /// <summary>The level's profile gives the theme colour (null = the default gold).</summary>
    public void Init(CheckpointProfile theme)
    {
        if (theme != null)
        {
            Color c = theme.lockedTeaseColor;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            accent = Color.HSVToRGB(h, Mathf.Clamp(s, 0.35f, 0.9f), Mathf.Max(v, 0.9f));
            Color.RGBToHSV(accent, out h, out s, out v);
            Color d = Color.HSVToRGB(h, Mathf.Min(1f, s + 0.15f), 0.28f);
            outline = new Color(d.r, d.g, d.b, 0.85f);
        }
    }

    void Start()
    {
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        runner = FindFirstObjectByType<CubeRailRunner>();
        trail = runner != null ? runner.GetComponent<RunnerTrailFX>() : null;
        bestKey = EndlessRun.CurrentBestKey(gameObject.scene.name);
        BuildUI();
        Arm();
    }

    void OnDestroy()
    {
        if (canvasRect != null) Destroy(canvasRect.gameObject);
        if (gateMat != null) Destroy(gateMat);
    }

    // Reads the best and (re)sets both targets.
    void Arm()
    {
        best = PlayerPrefs.GetInt(bestKey, 0);
        foreach (Target t in targets) { t.placed = t.hit = false; t.gate = null; }
        if (best <= 0) { targets[0].score = targets[1].score = 0; return; }
        targets[0].score = best;
        targets[0].label = "HIGH SCORE";
        targets[1].score = best + Mathf.Max(1, Mathf.CeilToInt(best * stretchFraction));
        targets[1].label = "BEST +50%";
    }

    void Update()
    {
        if (gen == null || runner == null) return;
        int s = gen.DisplayScore;
        if (s < lastScore) Arm();   // a restart: the finished run saved its best
        lastScore = s;
        if (best <= 0) return;

        // Gates: lay each one on its leg once that leg exists (a few times a second is plenty).
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.25f;
            foreach (Target t in targets) if (!t.placed && !t.hit) TryPlace(t);
        }

        foreach (Target t in targets)
        {
            if (t.hit || t.score <= 0 || s < t.score) continue;
            t.hit = true;
            if (t.gate != null) t.gate.Burst();
            Celebrate(t);
        }
        AnimatePopup(Time.unscaledDeltaTime);
    }

    // The leg whose arrival shows `t.score` (close-call bonus already earned is allowed for).
    void TryPlace(Target t)
    {
        int serial = t.score + gen.ScoreBase - gen.BonusPoints;
        if (serial <= gen.Score) { t.placed = true; return; }    // already behind us: no gate, the popup still comes
        if (serial > gen.Score + 40) return;                     // not laid yet
        int n = gen.SegmentCount;
        for (int i = n - 1; i >= 0; i--)
        {
            DynamicPathGenerator.Segment seg = gen.SegmentAt(i);
            if (seg == null) continue;
            if (seg.serial > serial) continue;
            if (seg.serial < serial) return;                      // newer legs are all ahead of the target: not laid yet
            if (seg.cubes.Count < 3) return;
            // just after the corner the leg starts at - the score changes as the runner reaches the leg
            GameObject cube = seg.cubes[Mathf.Min(2, seg.cubes.Count - 1)];
            if (cube == null) return;
            Vector3 fwd = seg.direction.sqrMagnitude > 0.01f ? seg.direction.normalized : Vector3.forward;
            t.gate = Gate.Create(cube.transform, fwd, gen.cubeSize, gateHalf * (t.big ? 1f : 0.85f), GateMaterial(), t.big);
            t.placed = true;
            return;
        }
    }

    Material GateMaterial()
    {
        if (gateMat != null) return gateMat;
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        gateMat = new Material(sh) { name = "HighScoreGate (runtime)" };
        if (gateMat.HasProperty("_BaseColor")) gateMat.SetColor("_BaseColor", accent * 0.35f);
        if (gateMat.HasProperty("_Color")) gateMat.SetColor("_Color", accent);
        if (gateMat.HasProperty("_Smoothness")) gateMat.SetFloat("_Smoothness", 0.2f);
        gateMat.EnableKeyword("_EMISSION");
        gateMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        if (gateMat.HasProperty("_EmissionColor")) gateMat.SetColor("_EmissionColor", accent * 2.2f);
        return gateMat;
    }

    void Celebrate(Target t)
    {
        popupWorld = runner.transform.position;
        if (bigText != null)
        {
            bigText.text = t.score.ToString();
            smallText.text = t.label;
            popupT = 0f;
            popup.gameObject.SetActive(true);
        }
        if (trail != null)
        {
            if (t.big) trail.CloseCallBurst(accent); else trail.SmallBurst(accent, 8f);
        }
    }

    // ------------------------------------------------------------------ popup (CloseCallFX look and motion, theme colour)

    static float EaseOutBack(float x) { const float c = 2.2f; x -= 1f; return 1f + (c + 1f) * x * x * x + c * x * x; }

    void AnimatePopup(float dt)
    {
        if (popup == null || popupT < 0f) return;
        if (cam == null) cam = Camera.main;
        popupT += dt;
        float u = popupT / popupSeconds;
        if (u >= 1f || cam == null) { popupT = -1f; popup.gameObject.SetActive(false); return; }

        Vector3 anchor = Vector3.Lerp(popupWorld, runner.transform.position, 0.6f);
        Vector3 sp = cam.WorldToScreenPoint(anchor);
        if (sp.z < 0f) { popup.gameObject.SetActive(false); return; }
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out Vector2 local);

        float pop = EaseOutBack(Mathf.Clamp01(u / 0.22f));
        float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.15f) / 0.85f));
        float fade = 1f - Mathf.Clamp01((u - 0.55f) / 0.45f);
        popup.anchoredPosition = local + new Vector2(-70f, 95f + popupRise * rise);   // the other side of the runner from the +1
        popup.localScale = Vector3.one * Mathf.Lerp(0.2f, 1f, pop) * popupSize * (1f + 0.08f * Mathf.Sin(u * Mathf.PI));
        popup.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(12f, -4f, Mathf.Clamp01(u / 0.3f)));

        Color c = Color.Lerp(Color.white, accent, Mathf.Clamp01(u * 3f));
        c.a = fade;
        bigText.color = c;
        if (bigOutline != null) bigOutline.effectColor = new Color(outline.r, outline.g, outline.b, outline.a * fade);
        smallText.color = new Color(Mathf.Lerp(1f, accent.r, 0.5f), Mathf.Lerp(1f, accent.g, 0.5f), Mathf.Lerp(1f, accent.b, 0.5f), fade * Mathf.Clamp01((u - 0.08f) * 6f));
    }

    void BuildUI()
    {
        var canvasGO = new GameObject("EndlessHighScoreCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 101;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = (RectTransform)canvasGO.transform;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var root = new GameObject("HighScorePopup", typeof(RectTransform));
        root.transform.SetParent(canvasGO.transform, false);
        popup = (RectTransform)root.transform;
        popup.anchorMin = popup.anchorMax = popup.pivot = new Vector2(0.5f, 0.5f);
        popup.sizeDelta = new Vector2(320f, 220f);

        bigText = MakeText(popup, "Number", font, 132, new Vector2(0f, 20f), new Vector2(320f, 160f));
        bigOutline = bigText.gameObject.AddComponent<Outline>();
        bigOutline.effectDistance = new Vector2(5f, -5f);
        var sh = bigText.gameObject.AddComponent<Shadow>();
        sh.effectDistance = new Vector2(0f, -8f);
        sh.effectColor = new Color(0f, 0f, 0f, 0.35f);

        smallText = MakeText(popup, "Label", font, 44, new Vector2(0f, -62f), new Vector2(400f, 60f));
        smallText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -3f);
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

    // ------------------------------------------------------------------ the gate

    /// <summary>A square frame of four beams with a block in each corner, standing across the leg. No colliders. Follows
    /// (and goes with) the cube it stands on.</summary>
    class Gate : MonoBehaviour
    {
        Transform follow;
        Transform[] corners;
        Transform frame;
        float burstT = -1f, phase, cornerSize;
        Vector3 baseScale = Vector3.one;
        Material mat;
        Color emission;

        public static Gate Create(Transform cube, Vector3 forward, float cubeSize, float half, Material shared, bool big)
        {
            var go = new GameObject("HighScoreGate");
            go.transform.position = cube.position;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            go.transform.rotation = Quaternion.LookRotation(forward, up);
            var g = go.AddComponent<Gate>();
            g.follow = cube;
            g.mat = shared;
            g.emission = shared.HasProperty("_EmissionColor") ? shared.GetColor("_EmissionColor") : Color.white;
            float h = half * cubeSize, th = 0.2f * cubeSize, corner = (big ? 0.55f : 0.42f) * cubeSize;

            var frame = new GameObject("Frame").transform;
            frame.SetParent(go.transform, false);
            g.frame = frame;
            Bar(frame, shared, new Vector3(0f, h, 0f), new Vector3(2f * h, th, th));
            Bar(frame, shared, new Vector3(0f, -h, 0f), new Vector3(2f * h, th, th));
            Bar(frame, shared, new Vector3(h, 0f, 0f), new Vector3(th, 2f * h, th));
            Bar(frame, shared, new Vector3(-h, 0f, 0f), new Vector3(th, 2f * h, th));
            g.corners = new Transform[4];
            for (int i = 0; i < 4; i++)
                g.corners[i] = Bar(frame, shared, new Vector3((i & 1) == 0 ? h : -h, (i & 2) == 0 ? h : -h, 0f), Vector3.one * corner);
            g.cornerSize = corner;
            g.phase = Random.value * 6.28f;
            return g;
        }

        static Transform Bar(Transform parent, Material m, Vector3 pos, Vector3 scale)
        {
            GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Collider c = b.GetComponent<Collider>();
            if (c != null) Destroy(c);
            b.name = "Part";
            var r = b.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            b.transform.SetParent(parent, false);
            b.transform.localPosition = pos;
            b.transform.localScale = scale;
            return b.transform;
        }

        public void Burst() { if (burstT < 0f) burstT = 0f; }

        void LateUpdate()
        {
            if (follow == null) { Destroy(gameObject); return; }
            transform.position = follow.position;
            float dt = Time.deltaTime;
            phase += dt;
            // idle: a slow spin round the leg's axis and a gentle breathing
            frame.localRotation = Quaternion.Euler(0f, 0f, phase * 28f);
            float breathe = 1f + 0.035f * Mathf.Sin(phase * 3f);
            for (int i = 0; i < corners.Length; i++) corners[i].localRotation = Quaternion.Euler(phase * 70f, phase * 55f, 0f);
            if (burstT < 0f) { frame.localScale = baseScale * breathe; return; }

            // burst: the frame flares and widens outward, the corner blocks fly out and shrink, 0.45 s, then it is gone
            burstT += dt;
            float u = Mathf.Clamp01(burstT / 0.45f);
            frame.localScale = baseScale * (1f + 0.9f * u * (2f - u));
            float k = 1f - u;
            for (int i = 0; i < corners.Length; i++) corners[i].localScale = Vector3.one * (cornerSize * Mathf.Max(0.01f, k));
            if (u >= 1f) Destroy(gameObject);
        }
    }
}
