using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// MainUi shop: a full screen of its own (no scene load) that slides over the checkpoint stack in 0.35 s. A 3D stage far
/// from the dioramas shows the selected cube rolling round a short looping rail on a pedestal, with its trail and
/// particles. Bottom: a snapping carousel of cards (swipe or arrows) - icon (a real render of the skin), name, price or
/// OWNED / EQUIPPED - and one primary button: BUY (price) / EQUIP / EQUIPPED; when the wallet is short it is greyed, says
/// how many coins are missing and pulses red on a tap. Buying: coins fly from the counter into the cube as the number counts
/// down, the cube spins up while the light dims, then a reveal burst in the rarity colour (+ haptic) and the button
/// flips to EQUIP; one more tap equips with a snap. Skins are cosmetic only. Unscaled time, pooled UI, no per-frame garbage.
/// </summary>
public class ShopController : MonoBehaviour
{
    public SkinCatalog catalog;
    public Material railMaterial;
    public Material trailMaterial;
    public AudioClip buySfx, equipSfx, coinSfx, denySfx;
    public float slideSeconds = 0.35f;

    static readonly Vector3 StageOrigin = new Vector3(-6000f, 0f, 0f);
    static readonly Vector2Int[] Loop = { new Vector2Int(0, 0), new Vector2Int(6, 0), new Vector2Int(6, 4), new Vector2Int(0, 4) };

    MainMenuController menu;
    MenuDioramaRig rig;
    RectTransform root, carouselContent, coinIcon, primaryRt, flightLayer;
    CanvasGroup group;
    RawImage stageImage;
    Image dimImage, flashImage, primaryImg, primaryCoin;
    TextMeshProUGUI nameText, rarityText, tagText, coinText, primaryLabel;
    ScrollRect carousel;

    // stage
    Transform stageRoot, cube, cubeVisual;
    MeshFilter cubeMesh;
    MeshRenderer cubeRenderer;
    TrailRenderer trail;
    GameObject attached;
    GameObject character;          // a soul skin's character, standing in for the cube
    Quaternion heading = Quaternion.identity;
    Camera stageCam;
    RenderTexture stageRT;
    Vector3[] path;
    float[] cum;
    float loopLen, s, spin, spinBoost, stageTime, punch;
    Quaternion roll = Quaternion.identity;

    // cards
    class Card { public RectTransform rt; public Image frame, ring; public TextMeshProUGUI name, price; public Image coin; }
    readonly List<Card> cards = new List<Card>();
    readonly List<RenderTexture> icons = new List<RenderTexture>();
    const float CardW = 300f, CardH = 380f, CardGap = 34f;
    int selected = -1;
    bool snapping, dragging, open, busy;
    float snapTarget, shownCoins, denyT, reveal = -1f;
    int lastCoins = int.MinValue;
    Color rarityFlash;

    // flights + burst (pooled)
    const int FlightCount = 14, BurstCount = 26;
    readonly RectTransform[] flights = new RectTransform[FlightCount];
    readonly Image[] burstImgs = new Image[BurstCount];
    readonly Vector2[] burstVel = new Vector2[BurstCount];

    void Start()
    {
        menu = GetComponent<MainMenuController>();
        rig = GetComponent<MenuDioramaRig>();
        if (catalog == null) catalog = SkinCatalog.Load();
        if (menu == null || catalog == null || catalog.skins.Count == 0) { enabled = false; return; }
        BuildStage();
        RenderIcons();
        BuildUI();
        menu.ShopRequested += Open;
        ApplyEquippedEverywhere();
        root.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ stage

    void BuildStage()
    {
        stageRoot = new GameObject("ShopStage").transform;
        stageRoot.SetParent(transform, false);
        stageRoot.position = StageOrigin;
        Mesh box = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

        // pedestal + rail
        var ped = MakeBox(box, railMaterial, stageRoot, "Pedestal");
        ped.localPosition = new Vector3(3f, -1.25f, 2f);
        ped.localScale = new Vector3(10.5f, 1.5f, 8.5f);
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", new Color(0.16f, 0.19f, 0.3f));
        mpb.SetColor("_EdgeColor", new Color(0.42f, 0.48f, 0.7f));
        ped.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
        path = new Vector3[Loop.Length + 1];
        cum = new float[Loop.Length + 1];
        for (int c = 0; c <= Loop.Length; c++)
        {
            Vector2Int g = Loop[c % Loop.Length];
            path[c] = StageOrigin + new Vector3(g.x, 0f, g.y);
            if (c > 0) cum[c] = cum[c - 1] + Vector3.Distance(path[c - 1], path[c]);
        }
        loopLen = cum[Loop.Length];
        for (int c = 0; c < Loop.Length; c++)
        {
            Vector2Int a = Loop[c], b = Loop[(c + 1) % Loop.Length];
            Vector2Int st = new Vector2Int(System.Math.Sign(b.x - a.x), System.Math.Sign(b.y - a.y));
            for (Vector2Int q = a; q != b; q += st)
            {
                var t = MakeBox(box, railMaterial, stageRoot, "Rail");
                t.localPosition = new Vector3(q.x, 0f, q.y);
            }
        }

        // the cube
        cube = new GameObject("ShopCube").transform;
        cube.SetParent(stageRoot, false);
        cubeVisual = MakeBox(box, null, cube, "Visual");
        cubeMesh = cubeVisual.GetComponent<MeshFilter>();
        cubeRenderer = cubeVisual.GetComponent<MeshRenderer>();
        trail = cube.gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.45f;
        trail.widthMultiplier = 0.6f;
        trail.minVertexDistance = 0.1f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0));
        trail.sharedMaterial = trailMaterial;
        trail.shadowCastingMode = ShadowCastingMode.Off;

        // camera into a full-screen RT
        var camGo = new GameObject("ShopCamera");
        camGo.transform.SetParent(stageRoot, false);
        stageCam = camGo.AddComponent<Camera>();
        stageCam.clearFlags = CameraClearFlags.SolidColor;
        stageCam.backgroundColor = new Color(0.05f, 0.07f, 0.14f);
        stageCam.fieldOfView = 34f;
        stageCam.nearClipPlane = 0.3f;
        stageCam.farClipPlane = 120f;
        stageCam.allowMSAA = false;
        stageCam.cullingMask = ~(1 << MenuDioramaRig.BackdropLayer);
        var data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderShadows = false;
        data.renderPostProcessing = false;
        stageCam.enabled = false;
    }

    static Transform MakeBox(Mesh mesh, Material mat, Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    // Each card's icon: a real render of the skin's cube (once, at start).
    void RenderIcons()
    {
        Mesh box = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var holder = new GameObject("IconRig").transform;
        holder.SetParent(transform, false);
        holder.position = StageOrigin + new Vector3(0f, 200f, 0f);
        var ic = MakeBox(box, null, holder, "IconCube");
        ic.localRotation = Quaternion.Euler(-24f, 38f, -6f);
        var camGo = new GameObject("IconCamera");
        camGo.transform.SetParent(holder, false);
        camGo.transform.localPosition = new Vector3(0f, 0f, -3.4f);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.fieldOfView = 30f;
        cam.cullingMask = ~(1 << MenuDioramaRig.BackdropLayer);
        cam.enabled = false;
        var data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        var r = ic.GetComponent<MeshRenderer>();
        var mf = ic.GetComponent<MeshFilter>();
        foreach (CubeSkin sk in catalog.skins)
        {
            var rt = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32) { name = "Icon " + sk.id };
            mf.sharedMesh = sk.mesh != null ? sk.mesh : box;
            r.sharedMaterial = sk.material != null ? sk.material : railMaterial;
            GameObject ch = null;
            if (sk.characterPrefab != null)
            {
                // a soul character: turned to show its face, a little smaller and lower so the hat / cape fit
                r.enabled = false;
                ch = Instantiate(sk.characterPrefab, holder, false);
                ch.transform.localPosition = new Vector3(0f, -0.1f, 0.55f);
                ch.transform.localRotation = Quaternion.Euler(-14f, 0f, 0f) * Quaternion.Euler(0f, 150f, 0f);
                ch.transform.localScale = Vector3.one * 0.92f;
                var soul = ch.GetComponent<SoulCharacter>();
                if (soul != null) soul.PoseStill();
            }
            cam.targetTexture = rt;
            cam.Render();
            icons.Add(rt);
            if (ch != null) { ch.SetActive(false); Destroy(ch); }
            r.enabled = true;
        }
        cam.targetTexture = null;
        Destroy(holder.gameObject);
    }

    void ShowSkin(CubeSkin sk)
    {
        cubeMesh.sharedMesh = sk.mesh != null ? sk.mesh : Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        cubeRenderer.sharedMaterial = sk.material != null ? sk.material : railMaterial;
        Color head = sk.overrideTrail ? sk.trailHead : sk.uiColor, tail = sk.overrideTrail ? sk.trailTail : new Color(sk.uiColor.r, sk.uiColor.g, sk.uiColor.b, 0f);
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(head, 0f), new GradientColorKey(sk.overrideTrail ? sk.trailMid : sk.uiColor, 0.4f), new GradientColorKey(tail, 1f) },
                  new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = g;
        if (attached != null) Destroy(attached);
        attached = sk.attachedParticles != null ? Instantiate(sk.attachedParticles, cube, false) : null;
        if (character != null) Destroy(character);
        character = null;
        cubeRenderer.enabled = sk.characterPrefab == null;
        if (sk.characterPrefab != null)
        {
            character = Instantiate(sk.characterPrefab, cubeVisual, false);
            var soul = character.GetComponent<SoulCharacter>();
            if (soul != null) soul.viewCamera = stageCam;
        }
    }

    void UpdateStage(float dt)
    {
        stageTime += dt;
        s = Mathf.Repeat(s + 3.2f * dt, loopLen);
        int seg = 0;
        while (seg < Loop.Length - 1 && s >= cum[seg + 1]) seg++;
        Vector3 a = path[seg], b = path[seg + 1];
        Vector3 dir = (b - a).normalized;
        float k = (s - cum[seg]) / (cum[seg + 1] - cum[seg]);
        // a rolling cube: its centre rises over each edge
        float phase = Mathf.Repeat(s, 1f);
        float lift = 0.7071f * Mathf.Cos((phase * 90f - 45f) * Mathf.Deg2Rad) - 0.5f;
        Vector3 axis = Vector3.Cross(Vector3.up, dir);
        roll = Quaternion.AngleAxis(3.2f * dt * 90f, axis) * roll;
        spin = Mathf.MoveTowards(spin, spinBoost, dt * 1400f);
        punch = Mathf.MoveTowards(punch, 0f, dt * 4f);
        float sc = 1f + 0.18f * Mathf.Sin(punch * Mathf.PI) * punch;
        if (character != null)
        {
            // a character stays upright and walks: a little hop per tile, turning into each corner, leaning in a touch
            float hop = Mathf.Abs(Mathf.Sin(s * Mathf.PI)) * 0.14f;
            cube.position = Vector3.Lerp(a, b, k) + Vector3.up * (1f + hop);
            heading = Quaternion.Slerp(heading, Quaternion.LookRotation(dir, Vector3.up), 1f - Mathf.Exp(-dt * 7f));
            cubeVisual.rotation = Quaternion.AngleAxis(stageTime * spin, Vector3.up) * heading * Quaternion.Euler(5f, 0f, 0f);
        }
        else
        {
            cube.position = Vector3.Lerp(a, b, k) + Vector3.up * (1f + lift);
            cubeVisual.rotation = Quaternion.AngleAxis(stageTime * spin, Vector3.up) * roll;
        }
        cubeVisual.localScale = new Vector3(sc, sc, sc);

        // a slow, gentle orbit round the pedestal
        Vector3 focus = StageOrigin + new Vector3(3f, 0.2f, 2f);
        float yaw = 30f + 16f * Mathf.Sin(stageTime * 0.25f);
        Vector3 cp = focus + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 11f, -18f);
        // aimed below the pedestal, so the loop sits in the open upper half above the cards
        stageCam.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(focus + Vector3.down * 0.8f - cp));
    }

    // ------------------------------------------------------------------ UI

    void BuildUI()
    {
        root = MainMenuController.NewRect(menu.Canvas.transform, "Shop");
        MainMenuController.Stretch(root);
        group = root.gameObject.AddComponent<CanvasGroup>();
        Image bg = MainMenuController.NewImage(root, "Backdrop", null, new Color(0.05f, 0.07f, 0.14f, 1f));
        MainMenuController.Stretch(bg.rectTransform);
        stageImage = new GameObject("Stage", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        stageImage.transform.SetParent(root, false);
        MainMenuController.Stretch(stageImage.rectTransform);
        stageImage.raycastTarget = false;
        dimImage = MainMenuController.NewImage(root, "Dim", null, new Color(0f, 0f, 0.02f, 0f));
        MainMenuController.Stretch(dimImage.rectTransform);
        dimImage.raycastTarget = false;
        flashImage = MainMenuController.NewImage(root, "RarityFlash", MenuIcons.Gradient, new Color(1f, 1f, 1f, 0f));
        MainMenuController.Stretch(flashImage.rectTransform);
        flashImage.raycastTarget = false;

        RectTransform safe = MainMenuController.NewRect(root, "SafeArea");
        MainMenuController.Stretch(safe);
        safe.gameObject.AddComponent<SafeArea>();
        float m = menu.margin;

        // top: back (left), coins (right), name + rarity + tagline
        Button back = menu.NewButton(safe, "Back", MenuIcons.Pill, menu.panel, out RectTransform backRt);
        MainMenuController.Place(backRt, new Vector2(0f, 1f), new Vector2(m, -40f), new Vector2(130f, 130f), new Vector2(0f, 1f));
        Image bi = MainMenuController.NewImage(backRt, "Icon", MenuIcons.Back, Color.white);
        MainMenuController.Place(bi.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-4f, 0f), new Vector2(64f, 64f), new Vector2(0.5f, 0.5f));
        bi.raycastTarget = false;
        back.onClick.AddListener(Close);

        Button coinBtn = menu.NewButton(safe, "Coins", MenuIcons.Pill, menu.panel, out RectTransform coinRt);
        coinBtn.transition = Selectable.Transition.None;
        MainMenuController.Place(coinRt, new Vector2(1f, 1f), new Vector2(-m, -50f), new Vector2(300f, 110f), new Vector2(1f, 1f));
        Image ci = MainMenuController.NewImage(coinRt, "Icon", MenuIcons.Coin, Color.white);
        MainMenuController.Place(ci.rectTransform, new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(74f, 74f), new Vector2(0f, 0.5f));
        ci.raycastTarget = false;
        coinIcon = ci.rectTransform;
        coinText = menu.NewText(coinRt, "Count", menu.TitleFont, 56f, Color.white, TextAlignmentOptions.Right);
        MainMenuController.Place(coinText.rectTransform, new Vector2(1f, 0.5f), new Vector2(-28f, 2f), new Vector2(180f, 90f), new Vector2(1f, 0.5f));
        coinText.enableAutoSizing = true; coinText.fontSizeMin = 30f; coinText.fontSizeMax = 56f;

        nameText = menu.NewText(safe, "Name", menu.TitleFont, 80f, Color.white, TextAlignmentOptions.Center);
        MainMenuController.Place(nameText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -210f), new Vector2(980f, 100f), new Vector2(0.5f, 1f));
        rarityText = menu.NewText(safe, "Rarity", menu.BodyFont, 32f, Color.white, TextAlignmentOptions.Center);
        MainMenuController.Place(rarityText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -312f), new Vector2(700f, 44f), new Vector2(0.5f, 1f));
        rarityText.characterSpacing = 10f;
        tagText = menu.NewText(safe, "Tagline", menu.BodyFont, 32f, new Color(1f, 1f, 1f, 0.72f), TextAlignmentOptions.Center);
        MainMenuController.Place(tagText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -360f), new Vector2(900f, 44f), new Vector2(0.5f, 1f));

        // bottom: carousel, arrows, primary button
        Image shelf = MainMenuController.NewImage(root, "Shelf", MenuIcons.Gradient, new Color(0.04f, 0.05f, 0.11f, 1f));
        shelf.rectTransform.anchorMin = Vector2.zero; shelf.rectTransform.anchorMax = new Vector2(1f, 0f);
        shelf.rectTransform.pivot = new Vector2(0.5f, 0f);
        shelf.rectTransform.sizeDelta = new Vector2(0f, 900f);
        shelf.raycastTarget = false;
        shelf.transform.SetSiblingIndex(safe.GetSiblingIndex());

        var vp = new GameObject("Carousel", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
        vp.transform.SetParent(safe, false);
        var vpRt = (RectTransform)vp.transform;
        vpRt.anchorMin = new Vector2(0f, 0f); vpRt.anchorMax = new Vector2(1f, 0f);
        vpRt.pivot = new Vector2(0.5f, 0f);
        vpRt.sizeDelta = new Vector2(0f, CardH + 40f);
        vpRt.anchoredPosition = new Vector2(0f, 250f);
        vp.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        carouselContent = MainMenuController.NewRect(vpRt, "Content");
        carouselContent.anchorMin = carouselContent.anchorMax = new Vector2(0.5f, 0.5f);
        carouselContent.pivot = new Vector2(0f, 0.5f);
        carouselContent.sizeDelta = new Vector2(catalog.skins.Count * (CardW + CardGap), CardH + 40f);
        carousel = vp.AddComponent<ScrollRect>();
        carousel.content = carouselContent;
        carousel.viewport = vpRt;
        carousel.horizontal = true;
        carousel.vertical = false;
        carousel.movementType = ScrollRect.MovementType.Unrestricted;
        carousel.inertia = true;
        carousel.decelerationRate = 0.06f;
        var drag = vp.AddComponent<CarouselDrag>();
        drag.begin = () => { dragging = true; snapping = false; };
        drag.end = () => { dragging = false; SnapTo(NearestCard()); };

        for (int i = 0; i < catalog.skins.Count; i++) cards.Add(MakeCard(i));

        Button left = menu.NewButton(safe, "Prev", MenuIcons.Pill, menu.panel, out RectTransform lRt);
        MainMenuController.Place(lRt, new Vector2(0f, 0f), new Vector2(m, 60f + 75f), new Vector2(130f, 130f), new Vector2(0f, 0.5f));
        Image li = MainMenuController.NewImage(lRt, "Icon", MenuIcons.Back, Color.white); li.raycastTarget = false;
        MainMenuController.Place(li.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-4f, 0f), new Vector2(56f, 56f), new Vector2(0.5f, 0.5f));
        left.onClick.AddListener(() => SnapTo(Mathf.Max(0, selected - 1)));
        Button right = menu.NewButton(safe, "Next", MenuIcons.Pill, menu.panel, out RectTransform rRt);
        MainMenuController.Place(rRt, new Vector2(1f, 0f), new Vector2(-m, 60f + 75f), new Vector2(130f, 130f), new Vector2(1f, 0.5f));
        Image ri = MainMenuController.NewImage(rRt, "Icon", MenuIcons.Back, Color.white); ri.raycastTarget = false;
        MainMenuController.Place(ri.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(4f, 0f), new Vector2(56f, 56f), new Vector2(0.5f, 0.5f));
        ri.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
        right.onClick.AddListener(() => SnapTo(Mathf.Min(cards.Count - 1, selected + 1)));

        Button primary = menu.NewButton(safe, "Primary", MenuIcons.Pill, menu.good, out primaryRt);
        MainMenuController.Place(primaryRt, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(600f, 150f), new Vector2(0.5f, 0f));
        primaryImg = primary.GetComponent<Image>();
        primaryLabel = menu.NewText(primaryRt, "Label", menu.TitleFont, 60f, Color.white, TextAlignmentOptions.Center);
        MainMenuController.Stretch(primaryLabel.rectTransform);
        primaryCoin = MainMenuController.NewImage(primaryRt, "Coin", MenuIcons.Coin, Color.white);
        MainMenuController.Place(primaryCoin.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-150f, 0f), new Vector2(64f, 64f), new Vector2(0.5f, 0.5f));
        primaryCoin.raycastTarget = false;
        primary.onClick.AddListener(OnPrimary);

        flightLayer = MainMenuController.NewRect(root, "Flights");
        MainMenuController.Stretch(flightLayer);
        for (int i = 0; i < FlightCount; i++)
        {
            Image f = MainMenuController.NewImage(flightLayer, "Coin", MenuIcons.Coin, Color.white);
            f.raycastTarget = false;
            f.rectTransform.sizeDelta = new Vector2(58f, 58f);
            f.gameObject.SetActive(false);
            flights[i] = f.rectTransform;
        }
        for (int i = 0; i < BurstCount; i++)
        {
            Image b = MainMenuController.NewImage(flightLayer, "Burst", MenuIcons.Spark, Color.white);
            b.raycastTarget = false;
            b.gameObject.SetActive(false);
            burstImgs[i] = b;
        }
    }

    Card MakeCard(int i)
    {
        CubeSkin sk = catalog.skins[i];
        var c = new Card();
        Image frame = MainMenuController.NewImage(carouselContent, "Card " + sk.id, MenuIcons.Pill, menu.panel);
        c.frame = frame;
        c.rt = frame.rectTransform;
        c.rt.anchorMin = c.rt.anchorMax = new Vector2(0f, 0.5f);
        c.rt.pivot = new Vector2(0.5f, 0.5f);
        c.rt.sizeDelta = new Vector2(CardW, CardH);
        c.rt.anchoredPosition = new Vector2(i * (CardW + CardGap) + CardW * 0.5f, 0f);
        var btn = frame.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        int idx = i;
        btn.onClick.AddListener(() => { if (!dragging) SnapTo(idx); });
        c.ring = MainMenuController.NewImage(c.rt, "Ring", MenuIcons.Ring, sk.RarityColor);
        MainMenuController.Stretch(c.ring.rectTransform);
        c.ring.type = Image.Type.Sliced;
        c.ring.raycastTarget = false;
        var icon = new GameObject("Icon", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        icon.transform.SetParent(c.rt, false);
        icon.texture = icons[i];
        icon.raycastTarget = false;
        MainMenuController.Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(220f, 220f), new Vector2(0.5f, 1f));
        c.name = menu.NewText(c.rt, "Name", menu.BodyFont, 30f, Color.white, TextAlignmentOptions.Center);
        MainMenuController.Place(c.name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 84f), new Vector2(280f, 44f), new Vector2(0.5f, 0f));
        c.name.text = sk.displayName.ToUpperInvariant();
        c.name.enableAutoSizing = true; c.name.fontSizeMin = 20f; c.name.fontSizeMax = 30f;
        c.price = menu.NewText(c.rt, "Price", menu.TitleFont, 38f, Color.white, TextAlignmentOptions.Center);
        MainMenuController.Place(c.price.rectTransform, new Vector2(0.5f, 0f), new Vector2(14f, 28f), new Vector2(260f, 50f), new Vector2(0.5f, 0f));
        c.coin = MainMenuController.NewImage(c.rt, "Coin", MenuIcons.Coin, Color.white);
        MainMenuController.Place(c.coin.rectTransform, new Vector2(0.5f, 0f), new Vector2(-62f, 34f), new Vector2(40f, 40f), new Vector2(0.5f, 0f));
        c.coin.raycastTarget = false;
        RefreshCard(i);
        return c;
    }

    void RefreshCard(int i)
    {
        if (i < 0 || i >= catalog.skins.Count) return;
        CubeSkin sk = catalog.skins[i];
        Card c = i < cards.Count ? cards[i] : null;
        if (c == null) return;
        bool owned = SaveSystem.Owns(sk.id), equipped = SaveSystem.EquippedSkin == sk.id;
        c.coin.gameObject.SetActive(!owned);
        if (equipped) { c.price.text = "EQUIPPED"; c.price.color = menu.good; c.price.fontSize = 30f; c.price.rectTransform.anchoredPosition = new Vector2(0f, 30f); }
        else if (owned) { c.price.text = "OWNED"; c.price.color = new Color(1f, 1f, 1f, 0.75f); c.price.fontSize = 30f; c.price.rectTransform.anchoredPosition = new Vector2(0f, 30f); }
        else { c.price.SetText("{0}", sk.price); c.price.color = Color.white; c.price.fontSize = 38f; c.price.rectTransform.anchoredPosition = new Vector2(18f, 28f); }
    }

    void RefreshPrimary()
    {
        if (selected < 0) return;
        CubeSkin sk = catalog.skins[selected];
        bool owned = SaveSystem.Owns(sk.id), equipped = SaveSystem.EquippedSkin == sk.id;
        Color ink = new Color(0.13f, 0.08f, 0.02f);
        primaryCoin.gameObject.SetActive(false);
        if (equipped) { primaryImg.color = new Color(1f, 1f, 1f, 0.14f); primaryLabel.text = "EQUIPPED"; primaryLabel.color = new Color(1f, 1f, 1f, 0.7f); }
        else if (owned) { primaryImg.color = Color.white; primaryLabel.text = "EQUIP"; primaryLabel.color = ink; }
        else if (SaveSystem.Coins >= sk.price)
        {
            primaryImg.color = menu.good; primaryLabel.color = ink;
            primaryLabel.SetText("BUY  {0}", sk.price);
            primaryLabel.margin = new Vector4(70f, 0f, 0f, 0f);
            primaryCoin.gameObject.SetActive(true);
            return;
        }
        else
        {
            primaryImg.color = new Color(1f, 1f, 1f, 0.1f);
            primaryLabel.color = new Color(1f, 1f, 1f, 0.6f);
            primaryLabel.SetText("NEED {0} MORE", sk.price - SaveSystem.Coins);
        }
        primaryLabel.margin = Vector4.zero;
    }

    void Select(int i)
    {
        if (i == selected) return;
        selected = i;
        CubeSkin sk = catalog.skins[i];
        nameText.text = sk.displayName.ToUpperInvariant();
        rarityText.text = sk.rarity.ToString().ToUpperInvariant();
        rarityText.color = sk.RarityColor;
        tagText.text = sk.tagline;
        ShowSkin(sk);
        RefreshPrimary();
        MenuFeedback.Selection();
    }

    int NearestCard()
    {
        float x = -carouselContent.anchoredPosition.x;
        return Mathf.Clamp(Mathf.RoundToInt((x - CardW * 0.5f) / (CardW + CardGap)), 0, cards.Count - 1);
    }

    float ContentXFor(int i) => -(i * (CardW + CardGap) + CardW * 0.5f);

    void SnapTo(int i)
    {
        snapping = true;
        snapTarget = ContentXFor(i);
        carousel.StopMovement();
        Select(i);
    }

    // ------------------------------------------------------------------ open / close

    public void Open()
    {
        if (open || menu.Launching) return;
        open = true;
        root.gameObject.SetActive(true);
        root.SetAsLastSibling();
        EnsureStageRT();
        stageCam.enabled = true;
        menu.Suspended = true;
        int eq = catalog.IndexOf(SaveSystem.EquippedSkin);
        selected = -1;
        carouselContent.anchoredPosition = new Vector2(ContentXFor(eq), 0f);
        SnapTo(eq);
        for (int i = 0; i < cards.Count; i++) RefreshCard(i);
        shownCoins = SaveSystem.Coins;
        StartCoroutine(Slide(true));
    }

    public void Close()
    {
        if (!open || busy) return;
        MenuFeedback.Selection();
        StartCoroutine(Slide(false));
    }

    IEnumerator Slide(bool inward)
    {
        float w = ((RectTransform)menu.Canvas.transform).rect.width;
        for (float t = 0f; t < slideSeconds; t += Time.unscaledDeltaTime)
        {
            float k = Smooth(t / slideSeconds);
            float x = inward ? Mathf.Lerp(w, 0f, k) : Mathf.Lerp(0f, w, k);
            root.anchoredPosition = new Vector2(x, 0f);
            group.alpha = inward ? k : 1f - k;
            yield return null;
        }
        root.anchoredPosition = new Vector2(inward ? 0f : w, 0f);
        group.alpha = inward ? 1f : 0f;
        if (!inward)
        {
            open = false;
            stageCam.enabled = false;
            menu.Suspended = false;
            root.gameObject.SetActive(false);
        }
    }

    void EnsureStageRT()
    {
        Rect r = ((RectTransform)menu.Canvas.transform).rect;
        int w = Mathf.RoundToInt(r.width * 0.75f), h = Mathf.RoundToInt(r.height * 0.75f);
        if (stageRT != null && stageRT.width == w && stageRT.height == h) return;
        if (stageRT != null) { stageRT.Release(); Destroy(stageRT); }
        stageRT = new RenderTexture(Mathf.Max(64, w), Mathf.Max(64, h), 16, RenderTextureFormat.ARGB32) { name = "ShopStageRT" };
        stageCam.targetTexture = stageRT;
        stageImage.texture = stageRT;
    }

    // ------------------------------------------------------------------ per frame

    void Update()
    {
        if (!open) return;
        float dt = Time.unscaledDeltaTime;
        UpdateStage(dt);
        if (snapping && !dragging)
        {
            Vector2 p = carouselContent.anchoredPosition;
            p.x = Mathf.Lerp(p.x, snapTarget, 1f - Mathf.Exp(-dt * 14f));
            if (Mathf.Abs(p.x - snapTarget) < 0.5f) { p.x = snapTarget; snapping = false; }
            carouselContent.anchoredPosition = p;
        }
        else if (dragging || carousel.velocity.sqrMagnitude > 1f)
        {
            int n = NearestCard();
            if (n != selected) Select(n);
        }
        // the centred card is lifted a little
        float cx = -carouselContent.anchoredPosition.x;
        for (int i = 0; i < cards.Count; i++)
        {
            float d = Mathf.Abs((i * (CardW + CardGap) + CardW * 0.5f) - cx) / (CardW + CardGap);
            float k = 1f - Mathf.Clamp01(d);
            float sc = Mathf.Lerp(0.86f, 1f, k);
            cards[i].rt.localScale = new Vector3(sc, sc, 1f);
            cards[i].ring.color = WithAlpha(catalog.skins[i].RarityColor, Mathf.Lerp(0.25f, 1f, k));
        }

        // coins: the number glides to the wallet (down while a purchase flies in)
        float target = SaveSystem.Coins;
        if (!busy) shownCoins = Mathf.MoveTowards(shownCoins, target, Mathf.Max(40f, Mathf.Abs(target - shownCoins) * 5f) * dt);
        int shown = Mathf.RoundToInt(shownCoins);
        if (shown != lastCoins) { lastCoins = shown; coinText.SetText("{0}", shown); }

        // denied tap: a red pulse and a shake on the button
        if (denyT > 0f)
        {
            denyT = Mathf.Max(0f, denyT - dt);
            float k = denyT / 0.45f;
            primaryImg.color = Color.Lerp(new Color(1f, 1f, 1f, 0.1f), WithAlpha(menu.danger, 0.85f), Mathf.Sin(k * Mathf.PI));
            primaryRt.anchoredPosition = new Vector2(Mathf.Sin(denyT * 70f) * 16f * k, 60f);
        }
        if (reveal >= 0f)
        {
            reveal += dt;
            float k = Mathf.Clamp01(reveal / 1.1f);
            flashImage.color = WithAlpha(rarityFlash, (1f - k) * 0.85f);
            if (k >= 1f) reveal = -1f;
        }
    }

    // ------------------------------------------------------------------ buy / equip

    void OnPrimary()
    {
        if (busy || selected < 0) return;
        CubeSkin sk = catalog.skins[selected];
        if (SaveSystem.EquippedSkin == sk.id) return;
        if (SaveSystem.Owns(sk.id))
        {
            SaveSystem.Equip(sk.id);
            punch = 1f;
            MenuFeedback.Light();
            for (int i = 0; i < cards.Count; i++) RefreshCard(i);
            RefreshPrimary();
            ApplyEquippedEverywhere();
            return;
        }
        if (SaveSystem.Coins < sk.price)
        {
            denyT = 0.45f;
            MenuFeedback.Light();
            return;
        }
        StartCoroutine(Purchase(sk));
    }

    IEnumerator Purchase(CubeSkin sk)
    {
        busy = true;
        float from = SaveSystem.Coins;
        if (!SaveSystem.TrySpend(sk.price)) { busy = false; yield break; }
        float to = SaveSystem.Coins;
        MenuFeedback.Selection();

        // coins fly from the counter into the cube; the number counts down as they leave
        Vector2 start = flightLayer.InverseTransformPoint(coinIcon.position);
        Vector2 end = CubeOnScreen();
        const float flight = 0.55f, stagger = 0.04f, anticipation = 1.05f;
        float total = stagger * FlightCount + flight;
        for (float t = 0f; t < Mathf.Max(total, anticipation); t += Time.unscaledDeltaTime)
        {
            for (int i = 0; i < FlightCount; i++)
            {
                float k = (t - i * stagger) / flight;
                RectTransform f = flights[i];
                bool live = k > 0f && k < 1f;
                if (f.gameObject.activeSelf != live) f.gameObject.SetActive(live);
                if (!live) continue;
                float e = k * k * (3f - 2f * k);
                Vector2 mid = (start + end) * 0.5f + new Vector2((i % 2 == 0 ? 1f : -1f) * 140f, 220f);
                f.anchoredPosition = Vector2.Lerp(Vector2.Lerp(start, mid, e), Vector2.Lerp(mid, end, e), e);
                float sc = Mathf.Lerp(1f, 0.55f, e);
                f.localScale = new Vector3(sc, sc, 1f);
            }
            shownCoins = Mathf.Lerp(from, to, Smooth(t / total));
            // anticipation: the cube spins up, the light dims
            float a = Smooth(t / anticipation);
            spinBoost = Mathf.Lerp(0f, 1080f, a);
            dimImage.color = new Color(0f, 0f, 0.02f, 0.5f * a);
            end = CubeOnScreen();
            yield return null;
        }
        for (int i = 0; i < FlightCount; i++) flights[i].gameObject.SetActive(false);
        shownCoins = to;

        // reveal
        SaveSystem.GrantSkin(sk.id);
        spinBoost = 0f;
        punch = 1f;
        rarityFlash = sk.RarityColor;
        reveal = 0f;
        MenuFeedback.Medium();
        StartCoroutine(Burst(CubeOnScreen(), sk.RarityColor));
        for (float t = 0f; t < 0.4f; t += Time.unscaledDeltaTime)
        {
            dimImage.color = new Color(0f, 0f, 0.02f, 0.5f * (1f - t / 0.4f));
            yield return null;
        }
        dimImage.color = new Color(0f, 0f, 0.02f, 0f);
        RefreshCard(selected);
        RefreshPrimary();
        busy = false;
    }

    IEnumerator Burst(Vector2 at, Color c)
    {
        for (int i = 0; i < BurstCount; i++)
        {
            Image b = burstImgs[i];
            b.gameObject.SetActive(true);
            b.rectTransform.anchoredPosition = at;
            float sz = i % 3 == 0 ? 26f : 16f;
            b.rectTransform.sizeDelta = new Vector2(sz, sz);
            b.color = i % 4 == 0 ? Color.white : c;
            float ang = i * (360f / BurstCount) * Mathf.Deg2Rad + Random.Range(-0.15f, 0.15f);
            burstVel[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(420f, 900f);
        }
        for (float t = 0f; t < 0.9f; t += Time.unscaledDeltaTime)
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < BurstCount; i++)
            {
                burstVel[i] *= 1f - 3.2f * dt;
                burstVel[i].y -= 500f * dt;
                RectTransform r = burstImgs[i].rectTransform;
                r.anchoredPosition += burstVel[i] * dt;
                r.localRotation = Quaternion.Euler(0f, 0f, t * (i % 2 == 0 ? 400f : -320f));
                Color col = burstImgs[i].color; col.a = 1f - Smooth(t / 0.9f); burstImgs[i].color = col;
            }
            yield return null;
        }
        for (int i = 0; i < BurstCount; i++) burstImgs[i].gameObject.SetActive(false);
    }

    // The cube's spot on the shop canvas (through the stage RawImage).
    Vector2 CubeOnScreen()
    {
        Vector3 v = stageCam.WorldToViewportPoint(cube.position);
        Rect r = stageImage.rectTransform.rect;
        Vector3 local = new Vector3(r.xMin + v.x * r.width, r.yMin + v.y * r.height, 0f);
        return flightLayer.InverseTransformPoint(stageImage.rectTransform.TransformPoint(local));
    }

    void ApplyEquippedEverywhere()
    {
        CubeSkin sk = catalog.Equipped;
        if (sk == null) return;
        if (rig != null) rig.ApplyGhostSkin(sk.mesh, sk.keepSceneLook ? null : sk.material, null);
        int ix = catalog.IndexOf(sk.id);
        if (ix >= 0 && ix < icons.Count) menu.SetShopIconTexture(icons[ix]);
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    void OnDestroy()
    {
        if (stageRT != null) { stageRT.Release(); Destroy(stageRT); }
        foreach (RenderTexture rt in icons) if (rt != null) { rt.Release(); Destroy(rt); }
    }
}

/// <summary>Tells the shop when the carousel drag starts and ends (for snapping).</summary>
public class CarouselDrag : MonoBehaviour, IBeginDragHandler, IEndDragHandler
{
    public System.Action begin, end;
    public void OnBeginDrag(PointerEventData e) => begin?.Invoke();
    public void OnEndDrag(PointerEventData e) => end?.Invoke();
}
