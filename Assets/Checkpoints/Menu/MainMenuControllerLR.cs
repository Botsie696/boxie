using System.Collections;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// MainUILR (experiment): the MainUi checkpoint menu laid out left to right. The same slanted look (leaning edges, hairline
/// dividers with a sweeping highlight, darkness for far locks, live dioramas), but the checkpoints are tall cards in a
/// horizontal row, cardsInView (2) on screen at once, snapping one card at a time.
///
///   Layout      1080 x 1920 reference, match 0.5, inside Screen.safeArea. Top bar (title + best, coin pill, settings),
///               the card row, a progress map, the bottom bar in the thumb zone (SHOP, PLAY, LEVEL 1) - same as MainUi.
///   Cards       each a live window onto a checkpoint's first legs (MenuDioramaRig), edges leaning like "/" by
///               cardSlopeDegrees; neighbours share their edges. A dark scrim at the bottom carries the big number + name.
///   Frontier    the menu opens on [furthest unlocked, next locked]: what you can play next to what you are chasing.
///               The current card's number is yellow ("CURRENT"); the next locked card shows "NEXT" and a progress bar
///               (best vs its score) - a visible, nearly-full goal pulls the player into one more run.
///   Map         a row of diamonds under the cards (one per checkpoint): white = unlocked, yellow = current, dim = locked,
///               a line filled up to the current one plus the share of the way to the next, and a bracket over the two
///               cards in view. It is the page indicator, the progress overview and a shortcut (tap a diamond to go there).
///   Snap        drag / flick / wheel, then the row settles on the nearest card (a flick carries up to two cards) with a
///               selection haptic per card passed.
///   Idle        after a few seconds without input the PLAY button breathes gently (the one thing to do here).
///   Shop        derives from MainMenuController so ShopController (and ControlSettings) keep working unchanged; the few
///               base members they read are filled in through reflection - MainMenuController itself is not modified.
/// Every animation runs on unscaled time; nothing is allocated per frame.
/// </summary>
public class MainMenuControllerLR : MainMenuController
{
    [Header("Left-to-right row")]
    [Tooltip("UI/CheckpointCard shader (each card makes its own material).")]
    public Shader cardShader;
    [Range(1, 3)] public int cardsInView = 2;
    [Range(3f, 14f)] public float cardSlopeDegrees = 7f;
    [Tooltip("Height of the progress map strip between the cards and the bottom bar.")]
    public float mapHeight = 170f;
    [Tooltip("Card camera framing vs gameplay (the tall cards are phone-shaped, so less pull-back than the bands).")]
    [Range(0.8f, 2.5f)] public float cardPullBack = 1.3f;
    [Tooltip("Seconds the row takes to settle on a card.")]
    public float snapSeconds = 0.16f;
    [Tooltip("Seconds without input before the PLAY button starts to breathe.")]
    public float idleBreathAfter = 4f;

    const float CardPad = 34f;

    class Card
    {
        public int index, requirement;
        public CheckpointProfile profile;
        public CheckpointCardGraphic g;
        public RectTransform rt, label, track, fill;
        public TextMeshProUGUI number, line, tag, progress;
        public Image lockImg;
        public float baseX;
        public bool unlocked, black, visible, current, next;
        public float bright = 1f, desat, grain, blackK;
        public float tBright = 1f, tDesat, tGrain, tBlack;
        public float press, flare, burst, danger, shake, jiggle, intro;
        public float celebrate = -1f;
        public float shapeW = -1f, shapeH = -1f;
        public float fillTarget, fillShown;
    }

    Card[] cards;
    Canvas lrCanvas;
    RectTransform safe, viewport, content, overlay, topBar, bottomBar, card, settingsSheet, coinPill, map, mapLine, mapFill, mapBracket;
    RectTransform[] nodes;
    Image[] nodeImgs;
    TextMeshProUGUI[] nodeNums;
    CanvasGroup topGroup, bottomGroup, cardGroup, mapGroup;
    ScrollRect scroll;
    TextMeshProUGUI coinText, playSub, cardTitle, cardBody, cardBest;
    RectTransform coinIconRt, playRt, cardFill;
    Image flash;
    RawImage shopIconRaw;
    float slope, cardW, viewW = -1f, viewH = -1f;
    float shownCoins, coinPunch, cardTimer = -1f, cardFillTarget, cardFillShown;
    int lastCoinValue = int.MinValue, highest = 1, best, lastPage = -1;
    bool launchingLR, dragging, snapping;
    float snapTarget, snapVel, idleX, stillTime, idleTime;
    readonly Vector3[] corners = new Vector3[4];
    readonly System.Collections.Generic.Dictionary<Material, Material> shadowMats = new System.Collections.Generic.Dictionary<Material, Material>();

    // ---- the base members ShopController reads (Canvas, ShopIcon / SetShopIconTexture, Launching, ShopRequested) ----
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly FieldInfo BaseCanvas = typeof(MainMenuController).GetField("canvas", Private);
    static readonly FieldInfo BaseShopIcon = typeof(MainMenuController).GetField("shopIconRaw", Private);
    static readonly FieldInfo BaseLaunching = typeof(MainMenuController).GetField("launching", Private);
    static readonly FieldInfo BaseShopEvent = typeof(MainMenuController).GetField("ShopRequested", Private);

    string BestKey => "RunnerBest_" + GameLaunchRequest.GameScene;
    int Count => cards != null ? cards.Length : 0;
    int MaxPage => Mathf.Max(0, Count - cardsInView);

    void SetLaunching(bool on)
    {
        launchingLR = on;
        if (BaseLaunching != null) BaseLaunching.SetValue(this, on);
    }

    void RaiseShop()
    {
        var a = BaseShopEvent != null ? BaseShopEvent.GetValue(this) as System.Action : null;
        a?.Invoke();
    }

    // Declaring Awake / Start / Update here means Unity calls these and not the base class's.
    void Awake()
    {
        Application.targetFrameRate = 60;
        Time.timeScale = 1f;
        SaveSystem.EnsureLoaded();
        best = PlayerPrefs.GetInt(BestKey, 0);
        // "Reach score X" never lies: a best that already reached a checkpoint's score has unlocked it
        if (checkpoints != null)
            for (int n = SaveSystem.HighestCheckpoint + 1; n <= checkpoints.Count && best >= checkpoints.NominalLeg(n); n++)
                SaveSystem.RecordCheckpoint(n, null, checkpoints.Count);
        highest = Mathf.Clamp(SaveSystem.HighestCheckpoint, 1, checkpoints != null ? checkpoints.Count : 1);
        slope = Mathf.Tan(cardSlopeDegrees * Mathf.Deg2Rad);
        if (rig != null)
        {
            // one card at the reference: half the width, the height between the bars and the map, plus its lean
            float h = 1920f - topBarHeight - bottomBarHeight - mapHeight;
            rig.bandReferencePixels = new Vector2(1080f / Mathf.Max(1, cardsInView) + h * slope, h);
            rig.pullBack = cardPullBack;
            rig.Build();
        }
        if (BaseCanvas == null || BaseShopIcon == null || BaseLaunching == null || BaseShopEvent == null)
            Debug.LogWarning("[MenuLR] a MainMenuController member was renamed - the shop may not open from this menu.");
        BuildUI();
        if (BaseCanvas != null) BaseCanvas.SetValue(this, lrCanvas);
        if (BaseShopIcon != null) BaseShopIcon.SetValue(this, shopIconRaw);
        shownCoins = SaveSystem.Coins;
    }

    void Start() => StartCoroutine(Intro());

    // ------------------------------------------------------------------ build

    void BuildUI()
    {
        var root = new GameObject("MenuCanvas", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        lrCanvas = root.AddComponent<Canvas>();
        lrCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        lrCanvas.sortingOrder = 10;
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

        BuildRow();
        BuildTopBar();
        BuildBottomBar();
        BuildMap();
        BuildCard();
        BuildSettings();

        overlay = NewRect(root.transform, "Overlay");
        Stretch(overlay);
        flash = NewImage(overlay, "PortalFlash", null, new Color(1f, 0.95f, 0.82f, 0f));
        Stretch(flash.rectTransform);
        flash.raycastTarget = false;
    }

    void BuildRow()
    {
        int count = checkpoints != null ? checkpoints.Count : 0;

        var vp = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
        vp.transform.SetParent(safe, false);
        viewport = (RectTransform)vp.transform;
        Stretch(viewport);
        viewport.offsetMin = new Vector2(0f, bottomBarHeight + mapHeight);
        viewport.offsetMax = new Vector2(0f, -topBarHeight);
        vp.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        content = NewRect(viewport, "Content");
        content.anchorMin = new Vector2(0f, 0f);
        content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, 0.5f);

        scroll = vp.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.vertical = false;
        scroll.horizontal = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.12f;
        scroll.inertia = true;             // only so ScrollRect measures the drag velocity: the release stops it and snaps (UpdateSnap)
        scroll.decelerationRate = 0.12f;
        scroll.scrollSensitivity = 30f;
        var watch = vp.AddComponent<RowDragWatch>();
        watch.begin = () => { dragging = true; snapping = false; };
        watch.end = OnRowReleased;

        cards = new Card[count];
        for (int i = 0; i < count; i++)
        {
            CheckpointProfile p = checkpoints.checkpoints[i];
            var c = new Card { index = i + 1, profile = p, requirement = checkpoints.NominalLeg(i + 1) };
            var go = new GameObject("Card " + c.index, typeof(RectTransform));
            go.transform.SetParent(content, false);
            c.rt = (RectTransform)go.transform;
            c.rt.anchorMin = new Vector2(0f, 0f);
            c.rt.anchorMax = new Vector2(0f, 1f);
            c.rt.pivot = new Vector2(0.5f, 0.5f);
            c.g = go.AddComponent<CheckpointCardGraphic>();
            c.g.slope = slope;
            if (cardShader != null) c.g.Init(cardShader);
            else Debug.LogError("[MenuLR] cardShader (UI/CheckpointCard) is not assigned.");
            c.g.texture = rig != null ? rig.TextureFor(c.index) : null;
            c.g.color = Color.white;
            if (c.g.Instance != null) { c.g.Instance.SetColor("_BurstColor", good); c.g.Instance.SetColor("_DangerColor", danger); }

            var input = go.AddComponent<BandInput>();
            int idx = i;
            input.down = () => { cards[idx].press = 1f; idleTime = 0f; MenuFeedback.Selection(); };
            input.up = () => cards[idx].press = 0f;
            input.click = () => OnCardTapped(cards[idx]);

            // label: on the dark scrim at the bottom, each row following the card's leaning left edge
            c.label = NewRect(c.rt, "Label");
            Stretch(c.label);
            c.tag = NewText(c.label, "Tag", bodyFont, 24f, good, TextAlignmentOptions.BottomLeft);
            Place(c.tag.rectTransform, Vector2.zero, Vector2.zero, new Vector2(400f, 32f), Vector2.zero);
            c.tag.characterSpacing = 10f;
            AddShadow(c.tag);
            c.number = NewText(c.label, "Number", titleFont, 100f, Color.white, TextAlignmentOptions.BottomLeft);
            Place(c.number.rectTransform, Vector2.zero, Vector2.zero, new Vector2(300f, 110f), Vector2.zero);
            c.number.text = c.index.ToString("00");
            AddShadow(c.number);
            c.lockImg = NewImage(c.label, "Lock", MenuIcons.Lock, Color.white);
            Place(c.lockImg.rectTransform, Vector2.zero, Vector2.zero, new Vector2(34f, 34f), new Vector2(0.5f, 0.5f));
            c.lockImg.raycastTarget = false;
            c.line = NewText(c.label, "Line", bodyFont, 30f, Color.white, TextAlignmentOptions.BottomLeft);
            Place(c.line.rectTransform, Vector2.zero, Vector2.zero, new Vector2(440f, 44f), Vector2.zero);
            c.line.characterSpacing = 6f;
            AddShadow(c.line);

            // goal bar (only on the next locked card): best vs its score
            Image track = NewImage(c.label, "Track", MenuIcons.Pill, new Color(1f, 1f, 1f, 0.16f));
            track.pixelsPerUnitMultiplier = 32f / 6f;
            track.raycastTarget = false;
            c.track = track.rectTransform;
            Place(c.track, Vector2.zero, Vector2.zero, new Vector2(400f, 12f), Vector2.zero);
            Image fill = NewImage(c.track, "Fill", MenuIcons.Pill, new Color(1f, 1f, 1f, 0.9f));
            fill.pixelsPerUnitMultiplier = 32f / 6f;
            fill.raycastTarget = false;
            c.fill = fill.rectTransform;
            c.fill.anchorMin = Vector2.zero; c.fill.anchorMax = new Vector2(0f, 1f); c.fill.pivot = new Vector2(0f, 0.5f);
            c.fill.sizeDelta = new Vector2(12f, 0f);
            c.progress = NewText(c.track, "Progress", bodyFont, 24f, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.BottomRight);
            Place(c.progress.rectTransform, new Vector2(1f, 1f), new Vector2(0f, 10f), new Vector2(300f, 32f), new Vector2(1f, 0f));
            c.progress.characterSpacing = 4f;
            AddShadow(c.progress);
            cards[i] = c;
        }
        RefreshLocks(true);
        for (int i = 0; i < count; i++) cards[i].rt.SetSiblingIndex(i);   // each card draws over the one on its left: its divider lies on top
    }

    // Sizes everything that depends on the viewport (safe area / aspect); re-run when it changes.
    void Layout()
    {
        Rect r = viewport.rect;
        if (Mathf.Abs(r.width - viewW) < 0.5f && Mathf.Abs(r.height - viewH) < 0.5f) return;
        float page = cardW > 0f ? -content.anchoredPosition.x / cardW : 0f;
        viewW = r.width; viewH = r.height;
        cardW = viewW / Mathf.Max(1, cardsInView);
        float run = viewH * slope;
        content.sizeDelta = new Vector2(Count * cardW, 0f);
        for (int i = 0; i < Count; i++)
        {
            Card c = cards[i];
            c.baseX = (i + 0.5f) * cardW;
            c.rt.sizeDelta = new Vector2(cardW + run, 0f);
            c.rt.anchoredPosition = new Vector2(c.baseX, 0f);
            c.g.cardWidth = cardW;
            c.shapeW = -1f;
            c.line.rectTransform.sizeDelta = new Vector2(cardW - CardPad * 2f - 50f, 44f);
            c.track.sizeDelta = new Vector2(cardW - CardPad * 2f - 24f, 12f);
            PlaceRows(c);
        }
        content.anchoredPosition = new Vector2(-Mathf.Clamp(page, 0f, MaxPage) * cardW, 0f);
        LayoutMap();
    }

    void Row(RectTransform rt, float y, float extraX) => rt.anchoredPosition = new Vector2(CardPad + extraX + y * slope, y);

    void PlaceRows(Card c)
    {
        bool goal = c.track.gameObject.activeSelf;
        float y = 44f;
        if (goal) { Row(c.track, y, 0f); y += 34f; }
        Row(c.line.rectTransform, y, c.unlocked ? 0f : 48f);
        c.lockImg.rectTransform.anchoredPosition = new Vector2(CardPad + 17f + (y + 20f) * slope, y + 20f);
        y += 44f;
        Row(c.number.rectTransform, y, -4f);
        y += 108f;
        Row(c.tag.rectTransform, y, 0f);
    }

    void BuildMap()
    {
        int count = Count;
        map = NewRect(safe, "Map");
        map.anchorMin = new Vector2(0f, 0f);
        map.anchorMax = new Vector2(1f, 0f);
        map.pivot = new Vector2(0.5f, 0f);
        map.offsetMin = new Vector2(margin, bottomBarHeight);
        map.offsetMax = new Vector2(-margin, bottomBarHeight + mapHeight);
        mapGroup = map.gameObject.AddComponent<CanvasGroup>();

        Image bracket = NewImage(map, "InView", MenuIcons.Pill, new Color(1f, 1f, 1f, 0.09f));
        bracket.pixelsPerUnitMultiplier = 32f / 34f;
        bracket.raycastTarget = false;
        mapBracket = bracket.rectTransform;
        Place(mapBracket, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(100f, 68f), new Vector2(0f, 0.5f));

        Image line = NewImage(map, "Line", null, new Color(1f, 1f, 1f, 0.14f));
        line.raycastTarget = false;
        mapLine = line.rectTransform;
        Place(mapLine, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(100f, 4f), new Vector2(0f, 0.5f));
        Image fill = NewImage(map, "Fill", null, new Color(1f, 1f, 1f, 0.75f));
        fill.raycastTarget = false;
        mapFill = fill.rectTransform;
        Place(mapFill, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 4f), new Vector2(0f, 0.5f));

        nodes = new RectTransform[count];
        nodeImgs = new Image[count];
        nodeNums = new TextMeshProUGUI[count];
        for (int i = 0; i < count; i++)
        {
            // a diamond (a square turned 45 degrees: no round glows), a small number under it, a generous tap area
            Image hit = NewImage(map, "Node " + (i + 1), null, new Color(0f, 0f, 0f, 0f));
            nodes[i] = hit.rectTransform;
            Place(nodes[i], new Vector2(0f, 0.5f), Vector2.zero, new Vector2(88f, 120f), new Vector2(0.5f, 0.5f));
            var btn = hit.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            int n = i + 1;
            btn.onClick.AddListener(() => { idleTime = 0f; MenuFeedback.Selection(); SnapTo(PageShowing(n)); });
            Image d = NewImage(nodes[i], "Diamond", null, Color.white);
            d.raycastTarget = false;
            Place(d.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20f, 20f), new Vector2(0.5f, 0.5f));
            d.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            nodeImgs[i] = d;
            nodeNums[i] = NewText(nodes[i], "N", bodyFont, 22f, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.Center);
            Place(nodeNums[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(80f, 30f), new Vector2(0.5f, 0.5f));
            nodeNums[i].SetText("{0}", n);
        }
        RefreshMap();
    }

    float MapStep => Count > 1 ? (map.rect.width - 80f) / (Count - 1) : 0f;
    float NodeX(float zeroBased) => 40f + zeroBased * MapStep;

    void LayoutMap()
    {
        if (map == null) return;
        for (int i = 0; i < Count; i++) nodes[i].anchoredPosition = new Vector2(NodeX(i), 10f);
        mapLine.anchoredPosition = new Vector2(NodeX(0), 10f);
        mapLine.sizeDelta = new Vector2(NodeX(Count - 1) - NodeX(0), 4f);
        RefreshMap();
    }

    void RefreshMap()
    {
        if (nodes == null) return;
        for (int i = 0; i < Count; i++)
        {
            Card c = cards[i];
            bool cur = c.index == highest;
            float s = cur ? 30f : c.unlocked ? 20f : 17f;
            nodeImgs[i].rectTransform.sizeDelta = new Vector2(s, s);
            nodeImgs[i].color = cur ? good : c.unlocked ? Color.white : new Color(1f, 1f, 1f, c.black ? 0.14f : 0.3f);
            nodeNums[i].color = new Color(1f, 1f, 1f, cur ? 0.9f : c.unlocked ? 0.6f : 0.3f);
        }
        // filled to the current checkpoint, plus the share of the way to the next one the best score has covered
        float to = highest - 1;
        Card nx = highest < Count ? cards[highest] : null;
        if (nx != null)
        {
            int from = cards[highest - 1].requirement;
            to += Mathf.Clamp01((float)(best - from) / Mathf.Max(1, nx.requirement - from));
        }
        mapFill.anchoredPosition = new Vector2(NodeX(0), 10f);
        mapFill.sizeDelta = new Vector2(NodeX(to) - NodeX(0), 4f);
    }

    void BuildTopBar()
    {
        topBar = NewRect(safe, "TopBar");
        topBar.anchorMin = new Vector2(0f, 1f);
        topBar.anchorMax = new Vector2(1f, 1f);
        topBar.pivot = new Vector2(0.5f, 1f);
        topBar.sizeDelta = new Vector2(0f, topBarHeight);
        topGroup = topBar.gameObject.AddComponent<CanvasGroup>();
        // a soft dark gradient under the bar, reaching over the top of the cards (and up into the notch)
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
        if (best > 0)
        {
            title.rectTransform.anchoredPosition = new Vector2(margin, cy + 12f);
            var bestText = NewText(topBar, "Best", bodyFont, 26f, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Left);
            Place(bestText.rectTransform, new Vector2(0f, 1f), new Vector2(margin + 4f, cy - 38f), new Vector2(400f, 36f), new Vector2(0f, 0.5f));
            bestText.SetText("BEST {0}", best);
            bestText.characterSpacing = 8f;
            AddShadow(bestText);
        }

        Button gearBtn = NewButton(topBar, "Settings", MenuIcons.Round, panel, out RectTransform gearRt);
        Place(gearRt, new Vector2(1f, 1f), new Vector2(-margin, cy), new Vector2(96f, 96f), new Vector2(1f, 0.5f));
        Image gearIcon = NewImage(gearRt, "Icon", MenuIcons.Gear, new Color(1f, 1f, 1f, 0.92f));
        Place(gearIcon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(52f, 52f), new Vector2(0.5f, 0.5f));
        gearIcon.raycastTarget = false;
        var gearHit = NewImage(gearRt, "Hit", null, new Color(0f, 0f, 0f, 0f));
        Place(gearHit.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(124f, 124f), new Vector2(0.5f, 0.5f));
        gearBtn.onClick.AddListener(() => ShowSettings(true));

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
        fade.rectTransform.offsetMax = new Vector2(0f, mapHeight + 40f);
        fade.raycastTarget = false;
        Image solid = NewImage(bottomBar, "Solid", null, background);
        solid.rectTransform.anchorMin = Vector2.zero;
        solid.rectTransform.anchorMax = new Vector2(1f, 0f);
        solid.rectTransform.offsetMin = new Vector2(0f, -400f);
        solid.rectTransform.offsetMax = new Vector2(0f, 122f);
        solid.raycastTarget = false;

        const float h = 140f, y = 56f;
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
        shopBtn.onClick.AddListener(() => { idleTime = 0f; MenuFeedback.Selection(); RaiseShop(); });

        Button fromBtn = NewButton(bottomBar, "Level1", MenuIcons.Round, panel, out RectTransform fromStartRt);
        Place(fromStartRt, new Vector2(1f, 0f), new Vector2(-margin, y), new Vector2(h, h), new Vector2(1f, 0f));
        Image playIcon = NewImage(fromStartRt, "Icon", MenuIcons.Play, Color.white);
        Place(playIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(6f, 0f), new Vector2(60f, 60f), new Vector2(0.5f, 0.5f));
        playIcon.raycastTarget = false;
        var l1Label = NewText(bottomBar, "Level1Label", bodyFont, 24f, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Center);
        Place(l1Label.rectTransform, new Vector2(1f, 0f), new Vector2(-margin - h * 0.5f, y - 6f), new Vector2(160f, 34f), new Vector2(0.5f, 1f));
        l1Label.text = "LEVEL 1";
        l1Label.characterSpacing = 8f;
        fromBtn.onClick.AddListener(() => Launch(1, CardOf(1)));

        // PLAY: the one yellow thing, full pill, soft glow under it, presses down, breathes when the player idles
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
        playBtn.onClick.AddListener(() => Launch(highest, CardOf(highest)));
        RefreshPlayButton();
    }

    void RefreshPlayButton() => playSub.SetText(highest > 1 ? "CHECKPOINT {0}" : "FROM THE START", highest);

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
        if (cards == null) return;
        if (rig != null) rig.ClearRequests();
        if (Suspended) return;

        Layout();
        UpdateSnap(dt);
        UpdateIdle(dt);
        UpdateMapLive();

        viewport.GetWorldCorners(corners);
        float vMin = corners[0].x, vMax = corners[2].x, vMid = (vMin + vMax) * 0.5f, vW = Mathf.Max(1f, vMax - vMin);
        float settle = dt / Mathf.Max(0.05f, brightnessSeconds);
        float now = Time.unscaledTime;

        for (int i = 0; i < cards.Length; i++)
        {
            Card c = cards[i];
            if (c.celebrate < 0f)
            {
                c.bright = Mathf.MoveTowards(c.bright, c.tBright, settle);
                c.desat = Mathf.MoveTowards(c.desat, c.tDesat, settle);
                c.grain = Mathf.MoveTowards(c.grain, c.tGrain, settle);
                c.blackK = Mathf.MoveTowards(c.blackK, c.tBlack, settle);
            }

            c.rt.GetWorldCorners(corners);
            float cMin = corners[0].x, cMax = corners[2].x;
            c.visible = cMax > vMin && cMin < vMax;
            bool active = c.visible || c.g.expand > 0f;
            bool renders = c.blackK < 0.999f;
            if (rig != null && active && renders)
            {
                int every = c.g.expand > 0f || c.celebrate >= 0f ? 1 : c.unlocked ? 2 : 3;
                rig.Request(c.index, every, c.g.expand > 0f);
            }
            if (!active) continue;

            c.flare = Mathf.MoveTowards(c.flare, c.press, dt * 8f);
            float scale = Mathf.Lerp(1f, 0.97f, Smooth(c.flare));
            c.burst = Mathf.MoveTowards(c.burst, 0f, dt * 0.9f);
            c.danger = Mathf.MoveTowards(c.danger, 0f, dt * 2.6f);
            float shakeX = 0f;
            if (c.shake > 0f) { c.shake = Mathf.Max(0f, c.shake - dt); shakeX = Mathf.Sin(c.shake * 70f) * 18f * (c.shake / 0.4f); }
            if (c.jiggle > 0f)
            {
                c.jiggle = Mathf.Max(0f, c.jiggle - dt);
                c.lockImg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(c.jiggle * 55f) * 16f * (c.jiggle / 0.5f));
            }
            if (c.g.expand <= 0f)
            {
                c.rt.localScale = new Vector3(scale, scale, 1f);
                c.rt.anchoredPosition = new Vector2(c.baseX + shakeX + (1f - Smooth(c.intro)) * 160f, 0f);
            }
            if (c.track.gameObject.activeSelf && Mathf.Abs(c.fillShown - c.fillTarget) > 0.0005f)
            {
                c.fillShown = Mathf.MoveTowards(c.fillShown, c.fillTarget * Smooth(c.intro), dt * 0.8f);
                c.fill.sizeDelta = new Vector2(Mathf.Max(12f, c.track.rect.width * c.fillShown), 0f);
            }
            float centre = (cMin + cMax) * 0.5f;
            float parallax = (centre - vMid) / vW * 0.05f;
            float fade = rig != null ? rig.FadeFor(c.index) : 1f;
            c.g.SetLook(c.bright * fade, c.desat, c.grain, c.blackK, c.profile != null ? c.profile.lockedTeaseColor : coolLocked);
            float sp = Mathf.Repeat(now + i * 0.83f, sweepPeriod) / 1.3f;   // ~1.3 s sweep up the divider every sweepPeriod, staggered
            c.g.SetFx(c.flare, c.burst, c.danger, parallax, Smooth(c.intro), sp <= 1f ? sp : -1f);
            Rect r = c.rt.rect;
            if (c.g.expand > 0f || Mathf.Abs(r.width - c.shapeW) > 0.5f || Mathf.Abs(r.height - c.shapeH) > 0.5f)
            {
                c.shapeW = r.width; c.shapeH = r.height; c.g.PushShape();
            }
        }
        Shader.SetGlobalFloat("_MenuTime", now);
    }

    // ------------------------------------------------------------------ snapping

    float PageAt(float x) => cardW > 0f ? -x / cardW : 0f;
    int PageShowing(int checkpoint) => Mathf.Clamp(checkpoint - 1, 0, MaxPage);

    void OnRowReleased()
    {
        dragging = false;
        idleTime = 0f;
        float p = PageAt(content.anchoredPosition.x);
        // a flick carries on: project the release velocity, at most two cards past the nearest one
        float projected = p - scroll.velocity.x * 0.18f / Mathf.Max(1f, cardW);
        int near = Mathf.RoundToInt(p);
        int target = Mathf.Clamp(Mathf.RoundToInt(projected), near - 2, near + 2);
        SnapTo(target);
    }

    void SnapTo(int page)
    {
        snapTarget = -Mathf.Clamp(page, 0, MaxPage) * cardW;
        snapping = true;
        snapVel = 0f;
        scroll.StopMovement();
    }

    void UpdateSnap(float dt)
    {
        float x = content.anchoredPosition.x;
        if (snapping && !dragging)
        {
            x = Mathf.SmoothDamp(x, snapTarget, ref snapVel, snapSeconds, Mathf.Infinity, dt);
            if (Mathf.Abs(x - snapTarget) < 0.5f && Mathf.Abs(snapVel) < 5f) { x = snapTarget; snapping = false; }
            content.anchoredPosition = new Vector2(x, 0f);
        }
        else if (!dragging && cardW > 0f)
        {
            // the mouse wheel (or the elastic edge) left the row between cards: settle once it stops moving
            if (Mathf.Abs(x - idleX) > 0.01f) stillTime = 0f; else stillTime += dt;
            float nearest = -Mathf.Clamp(Mathf.Round(PageAt(x)), 0, MaxPage) * cardW;
            if (stillTime > 0.12f && Mathf.Abs(x - nearest) > 0.5f) SnapTo(Mathf.RoundToInt(PageAt(x)));
        }
        idleX = content.anchoredPosition.x;

        int pg = Mathf.Clamp(Mathf.RoundToInt(PageAt(idleX)), 0, MaxPage);
        if (pg != lastPage)
        {
            if (lastPage >= 0 && !launchingLR) MenuFeedback.Selection();   // a tick per card passed
            lastPage = pg;
        }
    }

    void UpdateMapLive()
    {
        if (map == null) return;
        float p = Mathf.Clamp(PageAt(content.anchoredPosition.x), -0.3f, MaxPage + 0.3f);
        float x0 = NodeX(p) - 38f, x1 = NodeX(p + cardsInView - 1) + 38f;
        mapBracket.anchoredPosition = new Vector2(x0, 10f);
        mapBracket.sizeDelta = new Vector2(x1 - x0, 68f);
        // the current checkpoint's diamond breathes a little
        if (highest >= 1 && highest <= Count)
        {
            float s = 1f + 0.14f * Mathf.Sin(Time.unscaledTime * 2.6f) * Mathf.Sin(Time.unscaledTime * 2.6f);
            nodeImgs[highest - 1].rectTransform.localScale = new Vector3(s, s, 1f);
        }
    }

    void UpdateIdle(float dt)
    {
        if (Input.touchCount > 0 || Input.GetMouseButton(0) || dragging || launchingLR) idleTime = 0f;
        else idleTime += dt;
        float amp = Smooth((idleTime - idleBreathAfter) / 1f);
        float wave = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / 1.6f);
        float s = 1f + 0.035f * amp * wave;
        playRt.localScale = new Vector3(s, s, 1f);
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
            coinPill.sizeDelta = new Vector2(14f + 56f + 12f + coinText.preferredWidth + 26f, 80f);
        }
        coinPunch = Mathf.MoveTowards(coinPunch, 0f, dt * 3f);
        float s = 1f + 0.18f * Smooth(coinPunch) * (Mathf.Abs(target - shownCoins) > 0.5f ? 1f : coinPunch);
        coinIconRt.localScale = new Vector3(s, s, 1f);
    }

    // ------------------------------------------------------------------ locks / darkness

    void RefreshLocks(bool snap)
    {
        int count = cards.Length;
        for (int i = 0; i < count; i++)
        {
            Card c = cards[i];
            int d = c.index - highest;
            c.unlocked = d <= 0;
            c.current = d == 0;
            c.next = d == 1;
            float k = darkness.Evaluate(Mathf.Max(0, d));
            c.tBright = c.unlocked ? 1f : Mathf.Max(k, 0f);
            c.tDesat = c.unlocked ? 0f : Mathf.Clamp01(0.2f + 0.22f * d);
            c.tGrain = c.unlocked ? 0f : 0.02f + 0.012f * d;
            c.tBlack = !c.unlocked && k <= 0.001f ? 1f : 0f;
            if (snap) { c.bright = c.tBright; c.desat = c.tDesat; c.grain = c.tGrain; c.blackK = c.tBlack; }
            c.black = c.tBlack > 0.5f;

            Color tint = c.profile != null && c.profile.look != null ? c.profile.lockedTeaseColor : coolLocked;
            tint = Color.Lerp(tint, new Color(0.85f, 0.88f, 0.95f), 0.6f);   // a hint only: yellow is kept for PLAY, coins and unlocks
            if (c.next) tint = coolLocked;
            c.g.SetDivider(tint, c.black ? 0.22f : 0.42f);

            c.label.gameObject.SetActive(true);
            c.lockImg.gameObject.SetActive(!c.unlocked);
            if (c.unlocked) c.line.text = c.index == 1 ? "START" : (c.profile != null ? c.profile.displayName.ToUpperInvariant() : "");
            else c.line.SetText("SCORE {0}", c.requirement);
            float a = c.unlocked ? 1f : (c.black ? 0.42f : 0.85f);
            c.number.color = c.current ? good : new Color(1f, 1f, 1f, a);
            c.line.color = new Color(1f, 1f, 1f, c.unlocked ? 0.92f : a * 0.9f);
            c.lockImg.color = new Color(1f, 1f, 1f, a);

            // CURRENT (yellow, where PLAY goes) / NEXT (cool, what you are chasing)
            c.tag.gameObject.SetActive(c.current || c.next);
            c.tag.text = c.current ? "CURRENT" : "NEXT";
            c.tag.color = c.current ? good : coolLocked;

            // the goal bar: the next locked card shows how far the best run already got
            bool goal = c.next && !c.black;
            c.track.gameObject.SetActive(goal);
            if (goal)
            {
                c.fillTarget = c.requirement > 0 ? Mathf.Clamp01((float)best / c.requirement) : 0f;
                int toGo = Mathf.Max(0, c.requirement - best);
                if (best > 0) c.progress.SetText("{0} TO GO", toGo);
                else c.progress.SetText("SCORE {0}", c.requirement);
            }
            PlaceRows(c);
        }
        RefreshMap();
    }

    Card CardOf(int checkpoint) => cards != null && checkpoint >= 1 && checkpoint <= cards.Length ? cards[checkpoint - 1] : null;

    // ------------------------------------------------------------------ taps

    void OnCardTapped(Card c)
    {
        if (launchingLR) return;
        idleTime = 0f;
        if (c.unlocked) { Launch(c.index, c); return; }
        c.shake = 0.4f;
        c.jiggle = 0.5f;
        c.danger = 1f;
        MenuFeedback.Light();
        ShowCard(c);
    }

    void ShowCard(Card c)
    {
        cardTitle.SetText("Checkpoint {0} is locked", c.index);
        cardBody.SetText("Reach score {0} in a run to unlock it.", c.requirement);
        cardBest.SetText("BEST {0} / {1}", best, c.requirement);
        cardFillTarget = c.requirement > 0 ? Mathf.Clamp01((float)best / c.requirement) : 0f;
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
        idleTime = 0f;
        MenuFeedback.Selection();
        settingsSheet.gameObject.SetActive(show);
        settingsSheet.SetAsLastSibling();
    }

    // ------------------------------------------------------------------ launch

    public new void Launch(int checkpoint) => Launch(checkpoint, CardOf(checkpoint));

    void Launch(int checkpoint, Card c)
    {
        if (launchingLR) return;
        SetLaunching(true);
        MenuFeedback.Medium();
        StartCoroutine(LaunchRoutine(checkpoint, c));
    }

    IEnumerator LaunchRoutine(int checkpoint, Card c)
    {
        AsyncOperation op = GameLaunchRequest.LoadGame(checkpoint, false);
        scroll.enabled = false;
        snapping = false;
        if (c != null && c.visible && !c.black)
        {
            c.press = 0f;
            c.rt.SetParent(overlay, true);
            c.rt.localScale = Vector3.one;
            flash.rectTransform.SetAsLastSibling();
            c.rt.GetLocalCorners(corners);
            Vector2 lo = overlay.InverseTransformPoint(c.rt.TransformPoint(corners[0]));
            Vector2 hi = overlay.InverseTransformPoint(c.rt.TransformPoint(corners[2]));
            Rect full = overlay.rect;
            Vector2 fromMin = lo - full.min, fromMax = hi - full.max;
            c.rt.anchorMin = Vector2.zero; c.rt.anchorMax = Vector2.one; c.rt.pivot = new Vector2(0.5f, 0.5f);
            c.label.gameObject.SetActive(false);
            // the card grows to the whole screen; its picture is cropped to keep the world's proportions
            float rtAspect = c.g.texture != null ? (float)c.g.texture.width / c.g.texture.height : 0.55f;
            float scrAspect = full.width / Mathf.Max(1f, full.height);
            float cropW = scrAspect < rtAspect ? scrAspect / rtAspect : 1f;
            float cropH = scrAspect < rtAspect ? 1f : rtAspect / scrAspect;
            mapGroup.alpha = 0f;
            for (float t = 0f; t < launchExpandSeconds; t += Time.unscaledDeltaTime)
            {
                float k = EaseInOut(t / launchExpandSeconds);
                c.rt.offsetMin = Vector2.Lerp(fromMin, Vector2.zero, k);
                c.rt.offsetMax = Vector2.Lerp(fromMax, Vector2.zero, k);
                c.g.expand = k;
                float w = Mathf.Lerp(1f, cropW, k), h = Mathf.Lerp(1f, cropH, k);
                c.g.uvRect = new Rect(0.5f - w * 0.5f, 0.5f - h * 0.5f, w, h);
                topGroup.alpha = bottomGroup.alpha = 1f - k;
                yield return null;
            }
            c.g.expand = 1f;
            c.rt.offsetMin = c.rt.offsetMax = Vector2.zero;
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
        Layout();
        int newly = SaveSystem.NewlyUnlocked;
        Card target = newly > 1 ? CardOf(newly) : CardOf(highest);
        // open on the frontier: the furthest unlocked card next to the one being chased
        int page = PageShowing(highest);
        if (newly > 1) page = Mathf.Max(0, PageShowing(newly) - 1);   // the celebration slides the row over to it
        content.anchoredPosition = new Vector2(-page * cardW, 0f);
        idleX = content.anchoredPosition.x;
        lastPage = page;
        if (newly > 1)
        {
            int was = highest;
            highest = Mathf.Max(1, newly - 1);
            RefreshLocks(true);
            highest = was;
            RefreshLocks(false);
            if (target != null) { target.celebrate = 0f; target.lockImg.gameObject.SetActive(true); }
        }

        float t0 = Time.unscaledTime;
        float total = introStagger * 4f + 0.6f;
        while (Time.unscaledTime - t0 < total)
        {
            float t = Time.unscaledTime - t0;
            for (int i = 0; i < cards.Length; i++)
                cards[i].intro = Mathf.Clamp01((t - Mathf.Clamp(i - page, 0, 3) * introStagger * 1.5f) / 0.5f);
            float k = Smooth(Mathf.Clamp01(t / 0.5f));
            topGroup.alpha = k; bottomGroup.alpha = k; mapGroup.alpha = k;
            topBar.anchoredPosition = new Vector2(0f, (1f - k) * 40f);
            bottomBar.anchoredPosition = new Vector2(0f, (1f - k) * -60f);
            yield return null;
        }
        for (int i = 0; i < cards.Length; i++) cards[i].intro = 1f;
        topGroup.alpha = bottomGroup.alpha = mapGroup.alpha = 1f;
        topBar.anchoredPosition = bottomBar.anchoredPosition = Vector2.zero;

        if (newly > 1 && target != null) yield return Celebrate(target);
    }

    IEnumerator Celebrate(Card c)
    {
        int page = PageShowing(c.index);
        float to = -page * cardW, from = content.anchoredPosition.x;
        if (Mathf.Abs(to - from) > 1f)
            for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
            {
                content.anchoredPosition = new Vector2(Mathf.Lerp(from, to, EaseInOut(t / 0.5f)), 0f);
                yield return null;
            }
        content.anchoredPosition = new Vector2(to, 0f);
        idleX = to;

        MenuFeedback.Medium();
        Vector3 lockPos = c.lockImg.rectTransform.position;
        c.lockImg.gameObject.SetActive(false);
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
        float fromBright = c.bright, fromDesat = c.desat, fromGrain = c.grain, fromBlack = c.blackK;
        c.burst = 1f;
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            float k = Smooth(t / dur);
            c.bright = Mathf.Lerp(fromBright, 1f, k);
            c.desat = Mathf.Lerp(fromDesat, 0f, k);
            c.grain = Mathf.Lerp(fromGrain, 0f, k);
            c.blackK = Mathf.Lerp(fromBlack, 0f, k);
            c.burst = Mathf.Max(c.burst, 1f - k * 0.7f);
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < pieces.Length; i++)
            {
                vel[i] += new Vector2(0f, i < Shards ? -1400f : -200f) * dt;
                pieces[i].anchoredPosition += vel[i] * dt;
                pieces[i].localRotation = Quaternion.Euler(0f, 0f, t * (i % 2 == 0 ? 540f : -420f));
                Color col = imgs[i].color; col.a = 1f - Smooth(t / (dur * (i < Shards ? 0.6f : 0.9f))); imgs[i].color = col;
            }
            yield return null;
        }
        for (int i = 0; i < pieces.Length; i++) Destroy(pieces[i].gameObject);
        c.celebrate = -1f;
        SaveSystem.ClearNewlyUnlocked();
        RefreshLocks(false);
        RefreshPlayButton();
    }

    // ------------------------------------------------------------------ helpers

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    static float EaseInOut(float x) { x = Mathf.Clamp01(x); return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) * 0.5f; }

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
}

/// <summary>Tells the LR menu when a drag on its row begins and ends (the ScrollRect on the same object does the dragging).</summary>
public class RowDragWatch : MonoBehaviour, IBeginDragHandler, IEndDragHandler
{
    public System.Action begin, end;
    public void OnBeginDrag(PointerEventData e) => begin?.Invoke();
    public void OnEndDrag(PointerEventData e) => end?.Invoke();
}
