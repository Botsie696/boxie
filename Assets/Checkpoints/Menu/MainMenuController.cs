using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// MainUi "checkpoint stack": a portrait screen of slanted bands, one per checkpoint, each a live window onto the first legs
/// a real run lays there (MenuDioramaRig). Built in code at start (TextMeshPro, procedural sprites).
///
///   Layout      1080 x 1920 reference, match 0.5, inside Screen.safeArea, 40 px margins. Top bar (title, glass coin pill,
///               round settings), bottom bar in the thumb zone (round SHOP with the equipped cube, FROM START, PLAY).
///               Seven bands of bandHeight fill the screen between them; 8-10 scroll. Edges rise slopeDegrees.
///   Dividers    a thin hairline tinted toward the band below, the band above casting a soft shadow onto it, a highlight
///               sweeping along each divider every few seconds (staggered); a cool tint in front of the next locked band.
///   Darkness    `darkness` (distance past the furthest unlocked checkpoint -> brightness). 0 = black: that band is not
///               rendered at all and shows only its number, a lock and its requirement. Changes animate.
///   Locks       every locked band reads "SCORE X" (X from the shared CheckpointSet: CheckpointSet.NominalLeg). A tap shakes
///               it and slides up a card: "Checkpoint N is locked. Reach score X in a run to unlock it." with best vs X.
///   Launch      unlocked tap: press-in, the band grows to the whole screen, a portal flash, 3.6 loads at that checkpoint.
///   Unlock      SaveSystem.newlyUnlockedCheckpoint: scroll to it, the lock shatters, it brightens, the divider bursts.
/// Every animation runs on unscaled time; nothing is allocated per frame.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("References")]
    public CheckpointSet checkpoints;
    public MenuDioramaRig rig;
    [Tooltip("UI/CheckpointBand material (each band makes its own copy).")]
    public Material bandMaterial;
    public TMP_FontAsset titleFont;
    public TMP_FontAsset bodyFont;

    [Header("Layout (reference pixels)")]
    public float topBarHeight = 150f;
    public float bottomBarHeight = 230f;
    public float bandHeight = 215f;
    [Range(4f, 18f)] public float slopeDegrees = 9f;
    public float margin = 40f;

    [Header("Darkness (x = checkpoints past the furthest unlocked; y = brightness, 0 = black, not rendered)")]
    public AnimationCurve darkness = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(1f, 0.65f), new Keyframe(2f, 0.35f), new Keyframe(3f, 0.12f), new Keyframe(4f, 0f), new Keyframe(10f, 0f));
    [Tooltip("Seconds bands take to settle at a new brightness (unlocks).")]
    public float brightnessSeconds = 1.2f;

    [Header("Colours (yellow = good / guidance, red = danger, white = neutral)")]
    public Color background = new Color(0.035f, 0.05f, 0.11f, 1f);
    public Color good = new Color(1f, 0.79f, 0.27f, 1f);
    public Color danger = new Color(1f, 0.33f, 0.33f, 1f);
    public Color panel = new Color(1f, 1f, 1f, 0.1f);
    public Color coolLocked = new Color(0.62f, 0.78f, 1f, 1f);

    [Header("Feel")]
    public float introStagger = 0.06f;
    public float launchExpandSeconds = 0.45f;
    public float celebrationSeconds = 1.2f;
    [Tooltip("Seconds between highlight sweeps along a divider (each divider offset).")]
    public float sweepPeriod = 5f;

    // ------------------------------------------------------------------ runtime

    class Band
    {
        public int index, requirement;
        public CheckpointProfile profile;
        public CheckpointBandGraphic g;
        public RectTransform rt, label;
        public TextMeshProUGUI number, line;
        public Image lockImg;
        public RectTransform endlessRt;      // the ENDLESS button (round, right side of the band; unlocked bands only)
        public float baseY;
        public bool unlocked, black, visible, readable;
        public float bright = 1f, desat, grain, blackK;               // shown (animated)
        public float tBright = 1f, tDesat, tGrain, tBlack;           // targets
        public float press, flare, burst, danger, shake, jiggle, intro;
        public float celebrate = -1f;
        public float shapeWidth = -1f;
    }

    Band[] bands;
    Canvas canvas;
    RectTransform safe, viewport, content, overlay, topBar, bottomBar, card, settingsSheet, coinPill;
    CanvasGroup topGroup, bottomGroup, cardGroup;
    ScrollRect scroll;
    TextMeshProUGUI coinText, playSub, cardTitle, cardBody, cardBest;
    RectTransform coinIconRt, fromStartRt, playRt, cardFill;
    Image flash;
    RawImage shopIconRaw;
    float rise, slope, contentHeight, shownCoins, coinPunch, cardTimer = -1f, cardFillTarget, cardFillShown;
    int lastCoinValue = int.MinValue, highest = 1, best;
    bool launching;
    readonly Vector3[] corners = new Vector3[4];
    readonly System.Collections.Generic.Dictionary<Material, Material> shadowMats = new System.Collections.Generic.Dictionary<Material, Material>();

    public event System.Action ShopRequested;
    public Canvas Canvas => canvas;
    public RectTransform SafeRoot => safe;
    public TMP_FontAsset TitleFont => titleFont;
    public TMP_FontAsset BodyFont => bodyFont;
    public bool Launching => launching;
    /// <summary>True while another full-screen view (the shop) covers the stack: no band renders.</summary>
    [System.NonSerialized] public bool Suspended;
    /// <summary>The SHOP button's cube (tinted / textured by the shop).</summary>
    public Graphic ShopIcon => shopIconRaw;
    public RectTransform CoinIcon => coinIconRt;
    /// <summary>Dev: managed bytes allocated by the menu's per-frame code (controller Update + rig LateUpdate), and frames measured.</summary>
    public static long DevAllocBytes; public static int DevFrames;

    string BestKey => "RunnerBest_" + GameLaunchRequest.GameScene;

    void Awake()
    {
        Application.targetFrameRate = 60;
        Time.timeScale = 1f;
        SaveSystem.EnsureLoaded();
        highest = Mathf.Clamp(SaveSystem.HighestCheckpoint, 1, checkpoints != null ? checkpoints.Count : 1);
        best = PlayerPrefs.GetInt(BestKey, 0);
        // "Reach score X" never lies: a best that already reached a checkpoint's score has unlocked it
        if (checkpoints != null)
            for (int n = SaveSystem.HighestCheckpoint + 1; n <= checkpoints.Count && false && best >= checkpoints.NominalLeg(n); n++)   // a level unlocks only by passing its portal
                SaveSystem.RecordCheckpoint(n, null, checkpoints.Count);
        highest = Mathf.Clamp(SaveSystem.HighestCheckpoint, 1, checkpoints != null ? checkpoints.Count : 1);
        slope = Mathf.Tan(slopeDegrees * Mathf.Deg2Rad);
        rise = 1080f * slope;
        if (rig != null)
        {
            rig.bandReferencePixels = new Vector2(1080f, bandHeight + rise);
            rig.Build();
        }
        BuildUI();
        shownCoins = SaveSystem.Coins;
    }

    void Start() => StartCoroutine(Intro());

    /// <summary>Puts the equipped cube's picture on the SHOP button.</summary>
    public void SetShopIconTexture(Texture t)
    {
        if (shopIconRaw == null || t == null) return;
        shopIconRaw.texture = t;
        shopIconRaw.color = Color.white;
    }

    // ------------------------------------------------------------------ build

    void BuildUI()
    {
        var root = new GameObject("MenuCanvas", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.transform.SetParent(transform, false);
        }

        Image bg = NewImage(root.transform, "Background", null, background);
        Stretch(bg.rectTransform);
        bg.raycastTarget = false;

        safe = NewRect(root.transform, "SafeArea");
        Stretch(safe);
        safe.gameObject.AddComponent<SafeArea>();

        BuildStack();
        BuildTopBar();
        BuildBottomBar();
        BuildCard();
        BuildSettings();

        overlay = NewRect(root.transform, "Overlay");
        Stretch(overlay);
        flash = NewImage(overlay, "PortalFlash", null, new Color(1f, 0.95f, 0.82f, 0f));
        Stretch(flash.rectTransform);
        flash.raycastTarget = false;
    }

    void BuildStack()
    {
        int count = checkpoints != null ? checkpoints.Count : 0;

        var vp = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
        vp.transform.SetParent(safe, false);
        viewport = (RectTransform)vp.transform;
        Stretch(viewport);
        vp.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        content = NewRect(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        // band 1's top edge crosses the top bar's line at mid-screen; band 7's bottom edge the bottom bar's
        float top0 = topBarHeight - rise * 0.5f;
        contentHeight = top0 + count * bandHeight + rise + (bottomBarHeight - rise * 0.5f);
        content.sizeDelta = new Vector2(0f, contentHeight);

        scroll = vp.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.12f;
        scroll.decelerationRate = 0.12f;
        scroll.scrollSensitivity = 30f;

        bands = new Band[count];
        for (int i = 0; i < count; i++)
        {
            CheckpointProfile p = checkpoints.checkpoints[i];
            var b = new Band { index = i + 1, profile = p, requirement = checkpoints.NominalLeg(i + 1) };
            var go = new GameObject("Band " + b.index, typeof(RectTransform));
            go.transform.SetParent(content, false);
            b.rt = (RectTransform)go.transform;
            b.rt.anchorMin = new Vector2(0f, 1f);
            b.rt.anchorMax = new Vector2(1f, 1f);
            b.rt.pivot = new Vector2(0.5f, 0.5f);
            b.baseY = -(top0 + i * bandHeight) - (bandHeight + rise) * 0.5f;
            b.rt.sizeDelta = new Vector2(0f, bandHeight + rise);
            b.rt.anchoredPosition = new Vector2(0f, b.baseY);
            b.g = go.AddComponent<CheckpointBandGraphic>();
            b.g.slope = slope;
            b.g.bandHeight = bandHeight;
            b.g.Init(bandMaterial);
            b.g.texture = rig != null ? rig.TextureFor(b.index) : null;
            b.g.color = Color.white;
            b.g.Instance.SetColor("_BurstColor", good);
            b.g.Instance.SetColor("_DangerColor", danger);

            var input = go.AddComponent<BandInput>();
            int idx = i;
            input.down = () => { bands[idx].press = 1f; MenuFeedback.Selection(); };
            input.up = () => bands[idx].press = 0f;
            input.click = () => OnBandTapped(bands[idx]);

            // label: big rounded number + one small line, on the scene, along the slope, a little above the lower edge
            b.label = NewRect(b.rt, "Label");
            b.label.anchorMin = b.label.anchorMax = b.label.pivot = new Vector2(0f, 0f);
            b.label.anchoredPosition = new Vector2(margin + 4f, (margin + 4f) * slope + 22f);
            b.label.sizeDelta = new Vector2(620f, 110f);
            b.label.localRotation = Quaternion.Euler(0f, 0f, slopeDegrees);
            b.number = NewText(b.label, "Number", titleFont, 74f, Color.white, TextAlignmentOptions.BottomLeft);
            Place(b.number.rectTransform, Vector2.zero, Vector2.zero, new Vector2(160f, 90f), Vector2.zero);
            b.number.text = b.index.ToString("00");
            AddShadow(b.number);
            b.lockImg = NewImage(b.label, "Lock", MenuIcons.Lock, Color.white);
            Place(b.lockImg.rectTransform, Vector2.zero, new Vector2(128f, 12f), new Vector2(34f, 34f), Vector2.zero);
            b.lockImg.raycastTarget = false;
            b.line = NewText(b.label, "Line", bodyFont, 30f, Color.white, TextAlignmentOptions.BottomLeft);
            Place(b.line.rectTransform, Vector2.zero, new Vector2(124f, 10f), new Vector2(480f, 44f), Vector2.zero);
            b.line.characterSpacing = 6f;
            AddShadow(b.line);
            BuildEndlessButton(b);
            bands[i] = b;
        }
        RefreshLocks(true);
        for (int i = 0; i < count; i++) bands[i].rt.SetSiblingIndex(i);   // each band draws over the one above: its divider lies on top
    }

    // ENDLESS: a round dark-glass button with an infinity mark on the right of every unlocked band, a small caption under it
    // ("ENDLESS", or "BEST n" once that level has an endless best). It runs that level's theme forever: no next checkpoint,
    // score from 0, its own best per level (EndlessRun / GameLaunchRequest).
    const float EndlessSize = 104f;

    void BuildEndlessButton(Band b)
    {
        Button btn = NewButton(b.rt, "Endless", MenuIcons.Round, new Color(0.02f, 0.03f, 0.08f, 0.5f), out b.endlessRt);
        b.endlessRt.anchorMin = b.endlessRt.anchorMax = new Vector2(1f, 0f);
        b.endlessRt.pivot = new Vector2(0.5f, 0.5f);
        b.endlessRt.sizeDelta = new Vector2(EndlessSize, EndlessSize);
        Image ring = NewImage(b.endlessRt, "Ring", MenuIcons.Round, new Color(1f, 1f, 1f, 0.16f));
        Stretch(ring.rectTransform);
        ring.raycastTarget = false;
        Image inner = NewImage(b.endlessRt, "Inner", MenuIcons.Round, new Color(0.02f, 0.03f, 0.08f, 0.62f));
        Place(inner.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(EndlessSize - 6f, EndlessSize - 6f), new Vector2(0.5f, 0.5f));
        inner.raycastTarget = false;
        Image icon = NewImage(b.endlessRt, "Icon", MenuIcons.Infinity, Color.white);
        Place(icon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(62f, 62f), new Vector2(0.5f, 0.5f));
        icon.raycastTarget = false;

        int endlessBest = PlayerPrefs.GetInt(EndlessRun.BestKey(GameLaunchRequest.GameScene, b.index), 0);
        var cap = NewText(b.endlessRt, "Caption", bodyFont, 22f, new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.Top);
        Place(cap.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -6f), new Vector2(220f, 30f), new Vector2(0.5f, 1f));
        cap.characterSpacing = 6f;
        if (endlessBest > 0) cap.SetText("BEST {0}", endlessBest); else cap.text = "ENDLESS";
        AddShadow(cap);

        btn.onClick.AddListener(() =>
        {
            if (launching || !b.unlocked) return;
            Launch(b.index, b, true);
        });
        PlaceEndless(b, 1080f);
        b.endlessRt.gameObject.SetActive(false);   // shown per frame, only on an unlocked band that is inside the readable zone
    }

    // Right edge, centred on the band's slanted body at that x (the lower edge rises `slope` per pixel to the right),
    // a little high to leave room for the caption.
    void PlaceEndless(Band b, float width)
    {
        float xFromLeft = width - margin - EndlessSize * 0.5f - 6f;
        float y = xFromLeft * slope + bandHeight * 0.5f + 12f;
        b.endlessRt.anchoredPosition = new Vector2(-(margin + EndlessSize * 0.5f + 6f), y);
    }

    void BuildTopBar()
    {
        topBar = NewRect(safe, "TopBar");
        topBar.anchorMin = new Vector2(0f, 1f);
        topBar.anchorMax = new Vector2(1f, 1f);
        topBar.pivot = new Vector2(0.5f, 1f);
        topBar.sizeDelta = new Vector2(0f, topBarHeight);
        topGroup = topBar.gameObject.AddComponent<CanvasGroup>();
        // a soft dark gradient under the bar, reaching over the top of the stack (and up into the notch)
        Image fade = NewImage(topBar, "Fade", MenuIcons.Gradient, background);
        fade.rectTransform.anchorMin = new Vector2(0f, 0f);
        fade.rectTransform.anchorMax = new Vector2(1f, 1f);
        fade.rectTransform.offsetMin = new Vector2(0f, -110f);
        fade.rectTransform.offsetMax = new Vector2(0f, 300f);
        fade.rectTransform.localScale = new Vector3(1f, -1f, 1f);
        fade.raycastTarget = false;

        float cy = -topBarHeight * 0.5f;
        var title = NewText(topBar, "Title", titleFont, 66f, Color.white, TextAlignmentOptions.Left);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(margin, cy), new Vector2(520f, 100f), new Vector2(0f, 0.5f));
        title.text = "BOXICLE";
        title.characterSpacing = 4f;
        AddShadow(title);
        // the best score, small and quiet under the title (only once there is one)
        if (best > 0)
        {
            title.rectTransform.anchoredPosition = new Vector2(margin, cy + 12f);
            var bestText = NewText(topBar, "Best", bodyFont, 26f, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Left);
            Place(bestText.rectTransform, new Vector2(0f, 1f), new Vector2(margin + 4f, cy - 38f), new Vector2(400f, 36f), new Vector2(0f, 0.5f));
            bestText.SetText("BEST {0}", best);
            bestText.characterSpacing = 8f;
            AddShadow(bestText);
        }

        // round settings button, no box (tap area 124)
        Button gearBtn = NewButton(topBar, "Settings", MenuIcons.Round, panel, out RectTransform gearRt);
        Place(gearRt, new Vector2(1f, 1f), new Vector2(-margin, cy), new Vector2(96f, 96f), new Vector2(1f, 0.5f));
        Image gearIcon = NewImage(gearRt, "Icon", MenuIcons.Gear, new Color(1f, 1f, 1f, 0.92f));
        Place(gearIcon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(52f, 52f), new Vector2(0.5f, 0.5f));
        gearIcon.raycastTarget = false;
        var gearHit = NewImage(gearRt, "Hit", null, new Color(0f, 0f, 0f, 0f));
        Place(gearHit.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(124f, 124f), new Vector2(0.5f, 0.5f));
        gearBtn.onClick.AddListener(() => ShowSettings(true));

        // glass coin pill that hugs its content: icon, 12 px, number
        Image pill = NewImage(topBar, "Coins", MenuIcons.Pill, panel);
        pill.pixelsPerUnitMultiplier = 32f / 40f;
        pill.raycastTarget = false;
        coinPill = pill.rectTransform;
        Place(coinPill, new Vector2(1f, 1f), new Vector2(-margin - 96f - 20f, cy), new Vector2(200f, 80f), new Vector2(1f, 0.5f));
        Image ci = NewImage(coinPill, "Icon", MenuIcons.Coin, Color.white);
        Place(ci.rectTransform, new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(56f, 56f), new Vector2(0f, 0.5f));
        ci.raycastTarget = false;
        coinIconRt = ci.rectTransform;
        coinText = NewText(coinPill, "Count", titleFont, 46f, Color.white, TextAlignmentOptions.Left);
        Place(coinText.rectTransform, new Vector2(0f, 0.5f), new Vector2(14f + 56f + 12f, 2f), new Vector2(300f, 70f), new Vector2(0f, 0.5f));
    }

    void BuildBottomBar()
    {
        bottomBar = NewRect(safe, "BottomBar");
        bottomBar.anchorMin = new Vector2(0f, 0f);
        bottomBar.anchorMax = new Vector2(1f, 0f);
        bottomBar.pivot = new Vector2(0.5f, 0f);
        bottomBar.sizeDelta = new Vector2(0f, bottomBarHeight);
        bottomGroup = bottomBar.gameObject.AddComponent<CanvasGroup>();
        Image fade = NewImage(bottomBar, "Fade", MenuIcons.Gradient, background);
        fade.rectTransform.anchorMin = Vector2.zero;
        fade.rectTransform.anchorMax = Vector2.one;
        fade.rectTransform.offsetMin = new Vector2(0f, 120f);
        fade.rectTransform.offsetMax = new Vector2(0f, 160f);
        fade.raycastTarget = false;
        Image solid = NewImage(bottomBar, "Solid", null, background);   // the buttons sit on solid ground (also under the home bar)
        solid.rectTransform.anchorMin = Vector2.zero;
        solid.rectTransform.anchorMax = new Vector2(1f, 0f);
        solid.rectTransform.offsetMin = new Vector2(0f, -400f);
        solid.rectTransform.offsetMax = new Vector2(0f, 122f);
        solid.raycastTarget = false;

        const float h = 140f, y = 56f;
        // SHOP: a round button with the equipped cube, a small label under it
        Button shopBtn = NewButton(bottomBar, "Shop", MenuIcons.Round, panel, out RectTransform shopRt);
        Place(shopRt, new Vector2(0f, 0f), new Vector2(margin, y), new Vector2(h, h), new Vector2(0f, 0f));
        shopIconRaw = new GameObject("Icon", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        shopIconRaw.transform.SetParent(shopRt, false);
        Place(shopIconRaw.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(118f, 118f), new Vector2(0.5f, 0.5f));
        shopIconRaw.texture = MenuIcons.Cube.texture;
        shopIconRaw.raycastTarget = false;
        var shopLabel = NewText(bottomBar, "ShopLabel", bodyFont, 24f, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Center);
        Place(shopLabel.rectTransform, new Vector2(0f, 0f), new Vector2(margin + h * 0.5f, y - 6f), new Vector2(160f, 34f), new Vector2(0.5f, 1f));
        shopLabel.text = "SHOP";
        shopLabel.characterSpacing = 8f;
        shopBtn.onClick.AddListener(() => { MenuFeedback.Selection(); ShopRequested?.Invoke(); });

        // LEVEL 1: a round play button, bottom right, always there - a run from the very start
        Button fromBtn = NewButton(bottomBar, "Level1", MenuIcons.Round, panel, out fromStartRt);
        Place(fromStartRt, new Vector2(1f, 0f), new Vector2(-margin, y), new Vector2(h, h), new Vector2(1f, 0f));
        Image playIcon = NewImage(fromStartRt, "Icon", MenuIcons.Play, Color.white);
        Place(playIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(6f, 0f), new Vector2(60f, 60f), new Vector2(0.5f, 0.5f));
        playIcon.raycastTarget = false;
        var l1Label = NewText(bottomBar, "Level1Label", bodyFont, 24f, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Center);
        Place(l1Label.rectTransform, new Vector2(1f, 0f), new Vector2(-margin - h * 0.5f, y - 6f), new Vector2(160f, 34f), new Vector2(0.5f, 1f));
        l1Label.text = "LEVEL 1";
        l1Label.characterSpacing = 8f;
        fromBtn.onClick.AddListener(() => Launch(1, BandOf(1)));

        // PLAY: the one yellow thing, full pill, soft glow under it, presses down
        playRt = NewRect(bottomBar, "PlayRoot");
        playRt.anchorMin = new Vector2(0f, 0f);
        playRt.anchorMax = new Vector2(1f, 0f);
        playRt.pivot = new Vector2(0.5f, 0f);
        playRt.offsetMin = new Vector2(margin + h + 24f, y);
        playRt.offsetMax = new Vector2(-(margin + h + 24f), y + h);
        Image glow = NewImage(playRt, "Glow", MenuIcons.SoftPill, new Color(good.r, good.g, good.b, 0.38f));
        glow.rectTransform.anchorMin = Vector2.zero; glow.rectTransform.anchorMax = Vector2.one;
        glow.rectTransform.offsetMin = new Vector2(-26f, -36f); glow.rectTransform.offsetMax = new Vector2(26f, 14f);
        glow.raycastTarget = false;
        Button playBtn = NewButton(playRt, "Play", MenuIcons.Pill, good, out RectTransform playFace);
        Stretch(playFace);
        playFace.GetComponent<Image>().pixelsPerUnitMultiplier = 32f / 70f;
        var press = playFace.GetComponent<PressScale>(); if (press != null) press.dropPixels = 6f;
        var playLabel = NewText(playFace, "Label", titleFont, 64f, new Color(0.13f, 0.08f, 0.02f, 1f), TextAlignmentOptions.Center);
        Place(playLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 14f), new Vector2(460f, 80f), new Vector2(0.5f, 0.5f));
        playLabel.text = "PLAY";
        playLabel.characterSpacing = 6f;
        playSub = NewText(playFace, "Sub", bodyFont, 22f, new Color(0.13f, 0.08f, 0.02f, 0.75f), TextAlignmentOptions.Center);
        Place(playSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -36f), new Vector2(460f, 30f), new Vector2(0.5f, 0.5f));
        playSub.characterSpacing = 6f;
        playBtn.onClick.AddListener(() => Launch(highest, BandOf(highest)));
        RefreshPlayButton();
    }

    void RefreshPlayButton()
    {
        playSub.SetText(highest > 1 ? "CHECKPOINT {0}" : "FROM THE START", highest);
    }

    // The locked-tap card: slides up above the bottom bar, tap or 2.5 s to dismiss.
    void BuildCard()
    {
        card = NewRect(safe, "LockedCard");
        Place(card, new Vector2(0.5f, 0f), new Vector2(0f, bottomBarHeight + 24f), new Vector2(1080f - margin * 2f, 230f), new Vector2(0.5f, 0f));
        Image bg = card.gameObject.AddComponent<Image>();
        bg.sprite = MenuIcons.Pill; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 32f / 36f;
        bg.color = new Color(0.07f, 0.09f, 0.17f, 0.96f);
        var btn = card.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => cardTimer = Mathf.Min(cardTimer, 0.25f));
        Image lockI = NewImage(card, "Lock", MenuIcons.Lock, new Color(1f, 1f, 1f, 0.9f));
        Place(lockI.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -38f), new Vector2(44f, 44f), new Vector2(0f, 1f));
        lockI.raycastTarget = false;
        cardTitle = NewText(card, "Title", titleFont, 40f, Color.white, TextAlignmentOptions.Left);
        Place(cardTitle.rectTransform, new Vector2(0f, 1f), new Vector2(100f, -32f), new Vector2(820f, 56f), new Vector2(0f, 1f));
        cardBody = NewText(card, "Body", bodyFont, 30f, new Color(1f, 1f, 1f, 0.78f), TextAlignmentOptions.Left);
        Place(cardBody.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -96f), new Vector2(920f, 44f), new Vector2(0f, 1f));
        Image track = NewImage(card, "Track", MenuIcons.Pill, new Color(1f, 1f, 1f, 0.12f));
        track.pixelsPerUnitMultiplier = 32f / 9f;
        Place(track.rectTransform, new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(640f, 18f), new Vector2(0f, 0f));
        track.raycastTarget = false;
        Image fill = NewImage(track.rectTransform, "Fill", MenuIcons.Pill, new Color(1f, 1f, 1f, 0.85f));
        fill.pixelsPerUnitMultiplier = 32f / 9f;
        fill.raycastTarget = false;
        cardFill = fill.rectTransform;
        cardFill.anchorMin = Vector2.zero; cardFill.anchorMax = new Vector2(0f, 1f); cardFill.pivot = new Vector2(0f, 0.5f);
        cardFill.sizeDelta = new Vector2(18f, 0f);
        cardBest = NewText(card, "Best", bodyFont, 28f, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Right);
        Place(cardBest.rectTransform, new Vector2(1f, 0f), new Vector2(-40f, 30f), new Vector2(300f, 40f), new Vector2(1f, 0f));
        cardBest.characterSpacing = 4f;
        cardGroup = card.gameObject.AddComponent<CanvasGroup>();
        cardGroup.alpha = 0f;
        cardGroup.blocksRaycasts = false;
    }

    void BuildSettings()
    {
        settingsSheet = NewRect(safe, "Settings");
        Stretch(settingsSheet);
        Image dim = settingsSheet.gameObject.AddComponent<Image>();
        dim.color = new Color(0.01f, 0.02f, 0.05f, 0.72f);
        var dimBtn = settingsSheet.gameObject.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(() => ShowSettings(false));

        RectTransform sheet = NewRect(settingsSheet, "Card");
        Place(sheet, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, Debug.isDebugBuild ? 760f : 580f), new Vector2(0.5f, 0.5f));
        Image cardBg = sheet.gameObject.AddComponent<Image>();
        cardBg.sprite = MenuIcons.Pill; cardBg.type = Image.Type.Sliced; cardBg.pixelsPerUnitMultiplier = 32f / 36f;
        cardBg.color = new Color(0.07f, 0.09f, 0.18f, 0.98f);
        var title = NewText(sheet, "Title", titleFont, 60f, Color.white, TextAlignmentOptions.Center);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(700f, 90f), new Vector2(0.5f, 1f));
        title.text = "SETTINGS";
        title.characterSpacing = 4f;

        float rowY = -190f;
        AddChoiceRow(sheet, "HAPTICS", rowY, "ON", "OFF", () => SaveSystem.Data.settings.haptics, v => { SaveSystem.Data.settings.haptics = v; SaveSystem.MarkDirty(); });
        AddChoiceRow(sheet, "CONTROLS", rowY - 150f, "TAP", "FLICK", () => ControlSettings.Mode == ControlMode.TapDoubleTap,
                     v => ControlSettings.Mode = v ? ControlMode.TapDoubleTap : ControlMode.Flick);
        if (Debug.isDebugBuild)
        {
            Button add = NewButton(sheet, "Dev+1000", MenuIcons.Pill, new Color(1f, 1f, 1f, 0.12f), out RectTransform addRt);
            Place(addRt, new Vector2(0.5f, 1f), new Vector2(-190f, rowY - 320f), new Vector2(340f, 120f), new Vector2(0.5f, 0.5f));
            var al = NewText(addRt, "L", bodyFont, 30f, Color.white, TextAlignmentOptions.Center); Stretch(al.rectTransform); al.text = "DEV  +1000";
            add.onClick.AddListener(() => SaveSystem.AddCoins(1000));
            Button reset = NewButton(sheet, "DevReset", MenuIcons.Pill, new Color(danger.r, danger.g, danger.b, 0.25f), out RectTransform resetRt);
            Place(resetRt, new Vector2(0.5f, 1f), new Vector2(190f, rowY - 320f), new Vector2(340f, 120f), new Vector2(0.5f, 0.5f));
            var rl = NewText(resetRt, "L", bodyFont, 30f, Color.white, TextAlignmentOptions.Center); Stretch(rl.rectTransform); rl.text = "DEV  RESET SAVE";
            reset.onClick.AddListener(() => { SaveSystem.ResetAll(); UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.name); });
        }
        settingsSheet.gameObject.SetActive(false);
    }

    void AddChoiceRow(RectTransform sheet, string label, float y, string a, string b, System.Func<bool> isA, System.Action<bool> setA)
    {
        var l = NewText(sheet, label, bodyFont, 36f, Color.white, TextAlignmentOptions.Left);
        Place(l.rectTransform, new Vector2(0f, 1f), new Vector2(70f, y), new Vector2(300f, 120f), new Vector2(0f, 0.5f));
        l.text = label; l.characterSpacing = 6f;
        Button ba = NewButton(sheet, label + a, MenuIcons.Pill, Color.white, out RectTransform ra);
        Place(ra, new Vector2(1f, 1f), new Vector2(-70f - 210f, y), new Vector2(200f, 120f), new Vector2(1f, 0.5f));
        Button bb = NewButton(sheet, label + b, MenuIcons.Pill, Color.white, out RectTransform rb);
        Place(rb, new Vector2(1f, 1f), new Vector2(-70f, y), new Vector2(200f, 120f), new Vector2(1f, 0.5f));
        var ta = NewText(ra, "T", titleFont, 34f, Color.white, TextAlignmentOptions.Center); Stretch(ta.rectTransform); ta.text = a;
        var tb = NewText(rb, "T", titleFont, 34f, Color.white, TextAlignmentOptions.Center); Stretch(tb.rectTransform); tb.text = b;
        Color ink = new Color(0.08f, 0.09f, 0.14f), off = new Color(1f, 1f, 1f, 0.12f), on = Color.white;
        void Show() { bool A = isA(); ba.image.color = A ? on : off; bb.image.color = A ? off : on; ta.color = A ? ink : Color.white; tb.color = A ? Color.white : ink; }
        Show();
        ba.onClick.AddListener(() => { setA(true); Show(); MenuFeedback.Selection(); });
        bb.onClick.AddListener(() => { setA(false); Show(); MenuFeedback.Selection(); });
    }

    // ------------------------------------------------------------------ per frame

    void Update()
    {
        long g0 = System.GC.GetAllocatedBytesForCurrentThread();
        UpdateFrame();
        DevAllocBytes += System.GC.GetAllocatedBytesForCurrentThread() - g0;
        DevFrames++;
    }

    void UpdateFrame()
    {
        float dt = Time.unscaledDeltaTime;
        UpdateCoins(dt);
        UpdateCard(dt);
        if (bands == null) return;
        if (rig != null) rig.ClearRequests();
        if (Suspended) return;

        viewport.GetWorldCorners(corners);
        float vMin = corners[0].y, vMax = corners[1].y, vMid = (vMin + vMax) * 0.5f, vH = Mathf.Max(1f, vMax - vMin);
        float unit = vH / Mathf.Max(1f, viewport.rect.height);
        float zoneBottom = vMin + (bottomBarHeight - 70f) * unit, zoneTop = vMax - topBarHeight * unit;
        float settle = dt / Mathf.Max(0.05f, brightnessSeconds);
        float now = Time.unscaledTime;

        for (int i = 0; i < bands.Length; i++)
        {
            Band b = bands[i];
            // brightness etc. glide to their targets (an unlock re-targets every band)
            if (b.celebrate < 0f)
            {
                b.bright = Mathf.MoveTowards(b.bright, b.tBright, settle);
                b.desat = Mathf.MoveTowards(b.desat, b.tDesat, settle);
                b.grain = Mathf.MoveTowards(b.grain, b.tGrain, settle);
                b.blackK = Mathf.MoveTowards(b.blackK, b.tBlack, settle);
            }

            b.rt.GetWorldCorners(corners);
            float bMin = corners[0].y, bMax = corners[1].y;
            b.visible = bMax > vMin && bMin < vMax;
            bool active = b.visible || b.g.expand > 0f;
            bool renders = b.blackK < 0.999f;   // a black band is not rendered at all
            if (rig != null && active && renders)
            {
                int every = b.g.expand > 0f || b.celebrate >= 0f ? 1 : b.unlocked ? 2 : 3;
                rig.Request(b.index, every, b.g.expand > 0f);
            }
            if (!active) continue;

            if (b.readable && b.celebrate < 0f)
            {
                float ly = b.label.position.y;
                bool show = ly > zoneBottom && ly + 90f * unit < zoneTop;
                if (b.label.gameObject.activeSelf != show) b.label.gameObject.SetActive(show);
                bool endlessShow = show && b.unlocked && !launching;
                if (b.endlessRt != null && b.endlessRt.gameObject.activeSelf != endlessShow) b.endlessRt.gameObject.SetActive(endlessShow);
            }

            b.flare = Mathf.MoveTowards(b.flare, b.press, dt * 8f);
            float scale = Mathf.Lerp(1f, 0.97f, Smooth(b.flare));
            b.burst = Mathf.MoveTowards(b.burst, 0f, dt * 0.9f);
            b.danger = Mathf.MoveTowards(b.danger, 0f, dt * 2.6f);
            float shakeX = 0f;
            if (b.shake > 0f) { b.shake = Mathf.Max(0f, b.shake - dt); shakeX = Mathf.Sin(b.shake * 70f) * 18f * (b.shake / 0.4f); }
            if (b.jiggle > 0f)
            {
                b.jiggle = Mathf.Max(0f, b.jiggle - dt);
                b.lockImg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(b.jiggle * 55f) * 16f * (b.jiggle / 0.5f));
            }
            if (b.g.expand <= 0f)
            {
                b.rt.localScale = new Vector3(scale, scale, 1f);
                b.rt.anchoredPosition = new Vector2(shakeX + (1f - Smooth(b.intro)) * 70f, b.baseY);
            }
            float centre = (bMin + bMax) * 0.5f;
            float parallax = (centre - vMid) / vH * 0.04f;
            float fade = rig != null ? rig.FadeFor(b.index) : 1f;
            b.g.SetLook(b.bright * fade, b.desat, b.grain, b.blackK, b.profile != null ? b.profile.lockedTeaseColor : coolLocked);
            float sp = Mathf.Repeat(now + i * 0.83f, sweepPeriod) / 1.1f;   // ~1.1 s sweep every sweepPeriod, staggered
            b.g.SetFx(b.flare, b.burst, b.danger, parallax, Smooth(b.intro), sp <= 1f ? sp : -1f);
            float w = b.rt.rect.width;
            if (b.g.expand > 0f || Mathf.Abs(w - b.shapeWidth) > 0.5f)
            {
                if (Mathf.Abs(w - b.shapeWidth) > 0.5f && w > 1f) PlaceEndless(b, w);
                b.shapeWidth = w;
                b.g.PushShape();
            }
        }
        Shader.SetGlobalFloat("_MenuTime", now);
    }

    void UpdateCoins(float dt)
    {
        int target = SaveSystem.Coins;
        if (Mathf.Abs(target - shownCoins) > 0.01f)
        {
            float speed = Mathf.Max(30f, Mathf.Abs(target - shownCoins) * 4f);
            shownCoins = Mathf.MoveTowards(shownCoins, target, speed * dt);
            coinPunch = 1f;
        }
        int shown = Mathf.RoundToInt(shownCoins);
        if (shown != lastCoinValue)
        {
            lastCoinValue = shown;
            coinText.SetText("{0}", shown);
            coinText.ForceMeshUpdate();
            coinPill.sizeDelta = new Vector2(14f + 56f + 12f + coinText.preferredWidth + 26f, 80f);   // hugs its content
        }
        coinPunch = Mathf.MoveTowards(coinPunch, 0f, dt * 3f);
        float s = 1f + 0.18f * Smooth(coinPunch) * (Mathf.Abs(target - shownCoins) > 0.5f ? 1f : coinPunch);
        coinIconRt.localScale = new Vector3(s, s, 1f);
    }

    // ------------------------------------------------------------------ locks / darkness

    void RefreshLocks(bool snap)
    {
        int count = bands.Length;
        for (int i = 0; i < count; i++)
        {
            Band b = bands[i];
            int d = b.index - highest;
            b.unlocked = d <= 0;
            float k = darkness.Evaluate(Mathf.Max(0, d));
            b.tBright = b.unlocked ? 1f : Mathf.Max(k, 0f);
            b.tDesat = b.unlocked ? 0f : Mathf.Clamp01(0.2f + 0.22f * d);
            b.tGrain = b.unlocked ? 0f : 0.02f + 0.012f * d;
            b.tBlack = !b.unlocked && k <= 0.001f ? 1f : 0f;
            if (snap) { b.bright = b.tBright; b.desat = b.tDesat; b.grain = b.tGrain; b.blackK = b.tBlack; }
            b.black = b.tBlack > 0.5f;

            // divider: a hint of this band's colour; a cool tint in front of the next locked band
            Color tint = b.profile != null && b.profile.look != null ? b.profile.lockedTeaseColor : coolLocked;
            tint = Color.Lerp(tint, new Color(0.85f, 0.88f, 0.95f), 0.6f);   // a hint only: yellow is kept for PLAY, coins and unlocks
            if (d == 1) tint = coolLocked;
            b.g.SetDivider(tint, b.black ? 0.22f : 0.42f);

            b.readable = true;
            b.label.gameObject.SetActive(true);
            b.lockImg.gameObject.SetActive(!b.unlocked);
            if (!b.unlocked && b.endlessRt != null) b.endlessRt.gameObject.SetActive(false);
            if (b.unlocked) b.line.text = b.index == 1 ? "START" : (b.profile != null ? b.profile.displayName.ToUpperInvariant() : "");
            else b.line.SetText("SCORE {0}", b.requirement);
            b.line.rectTransform.anchoredPosition = new Vector2(b.unlocked ? 124f : 172f, 10f);
            float a = b.unlocked ? 1f : (b.black ? 0.42f : 0.85f);
            b.number.color = new Color(1f, 1f, 1f, a);
            b.line.color = new Color(1f, 1f, 1f, b.unlocked ? 0.92f : a * 0.9f);
            b.lockImg.color = new Color(1f, 1f, 1f, a);
        }
    }

    Band BandOf(int checkpoint) => bands != null && checkpoint >= 1 && checkpoint <= bands.Length ? bands[checkpoint - 1] : null;

    // ------------------------------------------------------------------ taps

    void OnBandTapped(Band b)
    {
        if (launching) return;
        if (b.unlocked) { Launch(b.index, b); return; }
        b.shake = 0.4f;
        b.jiggle = 0.5f;
        b.danger = 1f;
        MenuFeedback.Light();
        ShowCard(b);
    }

    void ShowCard(Band b)
    {
        cardTitle.SetText("Level {0} is locked", b.index);
        cardBody.SetText("Finish the previous level first.");
        cardBest.SetText("");
        cardFillTarget = 0f;
        cardFillShown = 0f;
        cardTimer = 2.5f;
        card.SetAsLastSibling();
    }

    void UpdateCard(float dt)
    {
        if (cardTimer < 0f) return;
        cardTimer -= dt;
        float shown = 2.5f - cardTimer;
        float a = cardTimer > 0.25f ? Smooth(shown / 0.22f) : Mathf.Clamp01(cardTimer / 0.25f);
        cardGroup.alpha = a;
        cardGroup.blocksRaycasts = a > 0.5f;
        card.anchoredPosition = new Vector2(0f, bottomBarHeight + 24f - (1f - Smooth(Mathf.Clamp01(shown / 0.25f))) * 60f);
        cardFillShown = Mathf.MoveTowards(cardFillShown, cardFillTarget, dt * 1.6f);
        float w = ((RectTransform)cardFill.parent).rect.width;
        cardFill.sizeDelta = new Vector2(Mathf.Max(18f, w * cardFillShown), 0f);
        if (cardTimer <= 0f) { cardTimer = -1f; cardGroup.alpha = 0f; cardGroup.blocksRaycasts = false; }
    }

    void ShowSettings(bool show)
    {
        MenuFeedback.Selection();
        settingsSheet.gameObject.SetActive(show);
        settingsSheet.SetAsLastSibling();
    }

    // ------------------------------------------------------------------ launch

    public void Launch(int checkpoint) => Launch(checkpoint, BandOf(checkpoint));

    void Launch(int checkpoint, Band b, bool endless = false)
    {
        if (launching) return;
        launching = true;
        MenuFeedback.Medium();
        StartCoroutine(LaunchRoutine(checkpoint, b, endless));
    }

    IEnumerator LaunchRoutine(int checkpoint, Band b, bool endless)
    {
        AsyncOperation op = GameLaunchRequest.LoadGame(checkpoint, false, endless);
        scroll.enabled = false;
        if (bands != null)
            foreach (Band each in bands)
                if (each.endlessRt != null) each.endlessRt.gameObject.SetActive(false);
        if (b != null && b.visible && !b.black)
        {
            b.press = 0f;
            b.rt.SetParent(overlay, true);
            b.rt.localScale = Vector3.one;
            flash.rectTransform.SetAsLastSibling();
            b.rt.GetLocalCorners(corners);
            Vector2 lo = overlay.InverseTransformPoint(b.rt.TransformPoint(corners[0]));
            Vector2 hi = overlay.InverseTransformPoint(b.rt.TransformPoint(corners[2]));
            Rect full = overlay.rect;
            Vector2 fromMin = lo - full.min, fromMax = hi - full.max;
            b.rt.anchorMin = Vector2.zero; b.rt.anchorMax = Vector2.one; b.rt.pivot = new Vector2(0.5f, 0.5f);
            b.label.gameObject.SetActive(false);
            float rtAspect = b.g.texture != null ? (float)b.g.texture.width / b.g.texture.height : 2.8f;
            float scrAspect = full.width / Mathf.Max(1f, full.height);
            float cropW = Mathf.Clamp01(scrAspect / rtAspect);
            for (float t = 0f; t < launchExpandSeconds; t += Time.unscaledDeltaTime)
            {
                float k = EaseInOut(t / launchExpandSeconds);
                b.rt.offsetMin = Vector2.Lerp(fromMin, Vector2.zero, k);
                b.rt.offsetMax = Vector2.Lerp(fromMax, Vector2.zero, k);
                b.g.expand = k;
                float w = Mathf.Lerp(1f, cropW, k);
                b.g.uvRect = new Rect(0.5f - w * 0.5f, 0f, w, 1f);
                topGroup.alpha = bottomGroup.alpha = 1f - k;
                yield return null;
            }
            b.g.expand = 1f;
            b.rt.offsetMin = b.rt.offsetMax = Vector2.zero;
        }
        for (float t = 0f; t < 0.22f; t += Time.unscaledDeltaTime)
        {
            flash.color = new Color(1f, 0.95f, 0.82f, Smooth(t / 0.22f));
            yield return null;
        }
        flash.color = new Color(1f, 0.95f, 0.82f, 1f);
        while (op != null && op.progress < 0.9f) yield return null;
        if (op != null) op.allowSceneActivation = true;
    }

    // ------------------------------------------------------------------ intro + unlock celebration

    IEnumerator Intro()
    {
        int newly = SaveSystem.NewlyUnlocked;
        Band target = newly > 1 ? BandOf(newly) : BandOf(highest);
        if (target != null && target.index > 5) JumpScrollTo(target);
        if (newly > 1)
        {
            // every band starts where it stood before the unlock, then glides to its new brightness
            int was = highest;
            highest = Mathf.Max(1, newly - 1);
            RefreshLocks(true);
            highest = was;
            RefreshLocks(false);
            if (target != null) { target.celebrate = 0f; target.lockImg.gameObject.SetActive(true); }
        }

        float t0 = Time.unscaledTime;
        float total = introStagger * Mathf.Min(bands.Length, 8) + 0.5f;
        while (Time.unscaledTime - t0 < total)
        {
            float t = Time.unscaledTime - t0;
            for (int i = 0; i < bands.Length; i++) bands[i].intro = Mathf.Clamp01((t - Mathf.Min(i, 7) * introStagger) / 0.45f);
            float k = Smooth(Mathf.Clamp01(t / 0.5f));
            topGroup.alpha = k; bottomGroup.alpha = k;
            topBar.anchoredPosition = new Vector2(0f, (1f - k) * 40f);
            bottomBar.anchoredPosition = new Vector2(0f, (1f - k) * -60f);
            yield return null;
        }
        for (int i = 0; i < bands.Length; i++) bands[i].intro = 1f;
        topGroup.alpha = bottomGroup.alpha = 1f;
        topBar.anchoredPosition = bottomBar.anchoredPosition = Vector2.zero;

        if (newly > 1 && target != null) yield return Celebrate(target);
    }

    void JumpScrollTo(Band b)
    {
        float vH = viewport.rect.height;
        float y = -b.baseY - vH * 0.5f;
        content.anchoredPosition = new Vector2(0f, Mathf.Clamp(y, 0f, Mathf.Max(0f, contentHeight - vH)));
    }

    IEnumerator Celebrate(Band b)
    {
        float vH = viewport.rect.height;
        float to = Mathf.Clamp(-b.baseY - vH * 0.5f, 0f, Mathf.Max(0f, contentHeight - vH));
        float from = content.anchoredPosition.y;
        if (Mathf.Abs(to - from) > 1f)
            for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
            {
                content.anchoredPosition = new Vector2(0f, Mathf.Lerp(from, to, EaseInOut(t / 0.5f)));
                yield return null;
            }

        MenuFeedback.Medium();
        Vector3 lockPos = b.lockImg.rectTransform.position;
        b.lockImg.gameObject.SetActive(false);
        const int Shards = 7, Sparks = 12;
        var pieces = new RectTransform[Shards + Sparks];
        var imgs = new Image[pieces.Length];
        var vel = new Vector2[pieces.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            bool shard = i < Shards;
            Image img = NewImage(overlay, shard ? "Shard" : "Spark", MenuIcons.Spark, shard ? Color.white : good);
            img.raycastTarget = false;
            imgs[i] = img;
            pieces[i] = img.rectTransform;
            pieces[i].position = lockPos;
            pieces[i].sizeDelta = shard ? new Vector2(18f, 18f) : new Vector2(12f, 12f);
            float ang = (i * 137.5f) * Mathf.Deg2Rad;
            vel[i] = shard ? new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) + 0.6f) * Random.Range(360f, 600f)
                           : new Vector2(Random.Range(-260f, 380f), Random.Range(200f, 520f));
        }
        float dur = Mathf.Max(0.6f, celebrationSeconds);
        float fromBright = b.bright, fromDesat = b.desat, fromGrain = b.grain, fromBlack = b.blackK;
        b.burst = 1f;
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            float k = Smooth(t / dur);
            b.bright = Mathf.Lerp(fromBright, 1f, k);
            b.desat = Mathf.Lerp(fromDesat, 0f, k);
            b.grain = Mathf.Lerp(fromGrain, 0f, k);
            b.blackK = Mathf.Lerp(fromBlack, 0f, k);
            b.burst = Mathf.Max(b.burst, 1f - k * 0.7f);
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < pieces.Length; i++)
            {
                vel[i] += new Vector2(0f, i < Shards ? -1400f : -200f) * dt;
                pieces[i].anchoredPosition += vel[i] * dt;
                pieces[i].localRotation = Quaternion.Euler(0f, 0f, t * (i % 2 == 0 ? 540f : -420f));
                Color c = imgs[i].color; c.a = 1f - Smooth(t / (dur * (i < Shards ? 0.6f : 0.9f))); imgs[i].color = c;
            }
            yield return null;
        }
        for (int i = 0; i < pieces.Length; i++) Destroy(pieces[i].gameObject);
        b.celebrate = -1f;
        SaveSystem.ClearNewlyUnlocked();
        RefreshLocks(false);
        RefreshPlayButton();
    }

    // ------------------------------------------------------------------ helpers

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    static float EaseInOut(float x) { x = Mathf.Clamp01(x); return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) * 0.5f; }

    public static RectTransform NewRect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static Image NewImage(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        if (sprite == MenuIcons.Pill || sprite == MenuIcons.SoftPill || sprite == MenuIcons.Ring) img.type = Image.Type.Sliced;
        return img;
    }

    public TextMeshProUGUI NewText(Transform parent, string name, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    public Button NewButton(Transform parent, string name, Sprite sprite, Color color, out RectTransform rt)
    {
        Image img = NewImage(parent, name, sprite, color);
        rt = img.rectTransform;
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
        cb.selectedColor = Color.white;
        cb.fadeDuration = 0.06f;
        b.colors = cb;
        img.gameObject.AddComponent<PressScale>();
        return b;
    }

    // A soft drop shadow (TMP underlay) on a runtime copy of the font material - the font asset is never touched.
    void AddShadow(TextMeshProUGUI t)
    {
        Material src = t.fontSharedMaterial;
        if (src == null) return;
        if (!shadowMats.TryGetValue(src, out Material m))
        {
            m = new Material(src) { name = src.name + " (menu shadow)" };
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.6f));
            m.SetFloat("_UnderlayOffsetX", 0.4f);
            m.SetFloat("_UnderlayOffsetY", -0.6f);
            m.SetFloat("_UnderlaySoftness", 0.55f);
            m.SetFloat("_UnderlayDilate", 0.2f);
            shadowMats[src] = m;
        }
        t.fontSharedMaterial = m;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    public static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}

/// <summary>Forwards a band's pointer events (the ScrollRect keeps the drags).</summary>
public class BandInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler, IPointerExitHandler
{
    public System.Action down, up, click;
    public void OnPointerDown(PointerEventData e) => down?.Invoke();
    public void OnPointerUp(PointerEventData e) => up?.Invoke();
    public void OnPointerExit(PointerEventData e) => up?.Invoke();
    public void OnPointerClick(PointerEventData e) { if (!e.dragging) click?.Invoke(); }
}

/// <summary>Buttons press in a little, optionally sinking a few pixels (unscaled time).</summary>
public class PressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public float pressed = 0.95f;
    public float dropPixels;
    float target = 1f, s = 1f;
    Vector2 rest; bool restKnown;
    public void OnPointerDown(PointerEventData e) => target = pressed;
    public void OnPointerUp(PointerEventData e) => target = 1f;
    public void OnPointerExit(PointerEventData e) => target = 1f;
    void Update()
    {
        if (Mathf.Abs(s - target) < 0.0005f) return;
        s = Mathf.MoveTowards(s, target, Time.unscaledDeltaTime * 2.5f);
        transform.localScale = new Vector3(s, s, 1f);
        if (dropPixels > 0f)
        {
            var rt = (RectTransform)transform;
            if (!restKnown) { rest = rt.anchoredPosition; restKnown = true; }
            rt.anchoredPosition = rest + Vector2.down * dropPixels * Mathf.InverseLerp(1f, pressed, s);
        }
    }
}

/// <summary>Haptics that honour the saved setting.</summary>
public static class MenuFeedback
{
    static bool On => SaveSystem.Data.settings.haptics;
    public static void Selection() { if (On) Haptics.Selection(); }
    public static void Light() { if (On) Haptics.LightImpact(); }
    public static void Medium() { if (On) Haptics.MediumImpact(); }
}
