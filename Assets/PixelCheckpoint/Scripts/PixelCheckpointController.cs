using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Pixel checkpoint "Jellyfish Grotto" (MainScene prop camera 3.9 checkpoint 4, and the whole run of MainScene prop camera
/// Pixel): everything the pixel-art cave look needs besides the look asset itself (Look_Pixel: colours, sky, fog, sun, post).
///
///   Where        _PixLook (x: the look in force is the pixel look, y: the look being wiped in is, z: always) - every pixel
///                shader (PixelCommon.hlsl) switches along the look wipe with it
///   Palette      moss, mortar, cave stone, mushrooms - constant globals; the leg texture (PixelLeg.hlsl) and the rocks
///                (PixelRockColor) read them
///   Pixel art    while the pixel look is on screen the camera draws with PixelCP_Renderer (the toon ink pass + the
///                pixel-art pass: the reference's palette with an ordered dither). During the look wipe the pass also cuts
///                the full-resolution frame into cells (only on the pixel side of the edge); once the pixel look is fully in,
///                the camera renders at pixelRenderScale with point upscaling instead - real pixels at a fraction of the cost
///                (the pass then only snaps the colours), and the pipeline's own render scale comes back when it leaves
///   Backdrop     the cave behind everything: ceiling, pillars, vines, far rock masses, rising jellyfish, plankton
///   Fireflies    blue square motes drifting round the runner (a world-space particle system that follows it)
///   Rocks        while in the pixel checkpoint the background generator lays low pillars plus floating
///                rocks, restored after it; cells are only rebuilt where the camera cannot see them
/// Purely visual: it never touches gameplay, the camera's framing or any asset.
/// </summary>
[DefaultExecutionOrder(970)]   // after LookTransitionDirector, DepthCueController and ToonCheckpointController
public class PixelCheckpointController : MonoBehaviour
{
    [System.Serializable]
    public class RockLayout
    {
        [Range(0f, 1f)] public float wallChance = 0.85f;
        [Range(1, 3)] public int wallsPerCell = 2;
        public Vector2 wallWidth = new Vector2(5f, 12f);
        public Vector2 wallTopRange = new Vector2(-12f, 10f);
        public float wallClearance = 6f;
        [Range(0f, 1f)] public float floatingChance = 0.7f;
        [Range(0, 4)] public int floatingPerCell = 2;
        public Vector2 floatingWidth = new Vector2(4f, 10f);
        public Vector2 floatingHeightRange = new Vector2(-12f, 8f);
        public float floatingClearance = 5f;
        [Range(0, 6)] public int debrisPerCell = 2;
        public float futureReach = 80f;
        public float futureDrop = 24f;

        public static RockLayout From(PathBackgroundGenerator g) => new RockLayout
        {
            wallChance = g.wallChance, wallsPerCell = g.wallsPerCell, wallWidth = g.wallWidth, wallTopRange = g.wallTopRange,
            wallClearance = g.wallClearance, floatingChance = g.floatingChance, floatingPerCell = g.floatingPerCell,
            floatingWidth = g.floatingWidth, floatingHeightRange = g.floatingHeightRange, floatingClearance = g.floatingClearance,
            debrisPerCell = g.debrisPerCell, futureReach = g.futureReach, futureDrop = g.futureDrop,
        };

        public void ApplyTo(PathBackgroundGenerator g)
        {
            g.wallChance = wallChance; g.wallsPerCell = wallsPerCell; g.wallWidth = wallWidth; g.wallTopRange = wallTopRange;
            g.wallClearance = wallClearance; g.floatingChance = floatingChance; g.floatingPerCell = floatingPerCell;
            g.floatingWidth = floatingWidth; g.floatingHeightRange = floatingHeightRange; g.floatingClearance = floatingClearance;
            g.debrisPerCell = debrisPerCell; g.futureReach = futureReach; g.futureDrop = futureDrop;
        }
    }

    [Header("The pixel look")]
    [Tooltip("Look_Pixel. The pixel checkpoint is wherever this look is in force (or being wiped in).")]
    public AtmosphereLook pixelLook;
    [Tooltip("The whole run is pixel (MainScene prop camera Pixel).")]
    public bool alwaysPixel;

    [Header("References (found when empty)")]
    public LookTransitionDirector director;
    public PathBackgroundGenerator background;
    public DynamicPathGenerator pathGen;
    public CubeRailRunner runner;
    public CameraParentRotator rig;
    public ToonCheckpointController toon;

    [Header("Leg moss (sRGB): glowing jade - apart from the blue cave, opposite the coral sides, unlike the yellow runner")]
    public Color moss = new Color32(0x3F, 0xD0, 0x8A, 0xFF);
    public Color mossLight = new Color32(0x8A, 0xF5, 0xB8, 0xFF);
    public Color mossDark = new Color32(0x1E, 0x9E, 0x6A, 0xFF);
    public Color mossGlow = new Color32(0xD0, 0xFF, 0xE0, 0xFF);

    [Header("Cave moss (sRGB): the reference's blue moss on the rocks, the trail and the fireflies")]
    public Color caveMoss = new Color32(0x35, 0xB0, 0xEC, 0xFF);
    public Color caveMossLight = new Color32(0x6F, 0xE6, 0xF7, 0xFF);
    public Color caveMossDark = new Color32(0x1E, 0x6E, 0xC8, 0xFF);
    public Color caveMossGlow = new Color32(0xB4, 0xFC, 0xFF, 0xFF);
    [Range(0f, 1f)] public float mossAmount = 1f;
    [Tooltip("Share of the top's moss texels that glow.")]
    [Range(0f, 0.3f)] public float glowChance = 0.07f;
    [Tooltip("Longest drip over a leg's side, in texels.")]
    [Range(2f, 8f)] public float longestDrip = 6f;

    [Header("Leg stone (the hue comes from the look's face colours)")]
    public Color mortar = new Color32(0x3A, 0x14, 0x40, 0xFF);
    [Tooltip("Texels per cube on the legs (the Minecraft grid).")]
    [Range(4f, 16f)] public float legTexels = 6f;

    [Header("Cave rocks (sRGB)")]
    public Color rockLit = new Color32(0x3B, 0x47, 0xCC, 0xFF);
    public Color rockMid = new Color32(0x2C, 0x31, 0xA1, 0xFF);
    public Color rockShade = new Color32(0x1B, 0x1B, 0x7D, 0xFF);
    public Color rockDeep = new Color32(0x10, 0x03, 0x5F, 0xFF);
    [Range(0f, 1f)] public float rockDither = 0.8f;
    [Tooltip("Rock texels per unit.")]
    [Range(0.5f, 8f)] public float rockTexels = 2.5f;
    [Tooltip("How far up a rock face must point to grow moss (normal y).")]
    [Range(0f, 1f)] public float rockMossThreshold = 0.62f;
    [Range(0f, 0.3f)] public float rockDripChance = 0.025f;
    public Color shroom = new Color32(0xE0, 0x69, 0x81, 0xFF);
    public Color shroomLight = new Color32(0xF4, 0xA0, 0x7A, 0xFF);
    [Range(0f, 1f)] public float shroomChance = 0.22f;
    [Range(0f, 1f)] public float shroomGlow = 0.65f;

    [Header("Rock light (camera relative)")]
    public bool litRight = true;
    public float lightUp = 1f, lightSide = 0.7f, lightBack = 0.45f;

    [Header("Pixel-art pass")]
    [Tooltip("Renderer index of PixelCP_Renderer on the pipeline assets. -1 = no pixel pass.")]
    public int pixelRendererIndex = 3;
    [Tooltip("The camera's own renderer index, put back when the pixel look leaves the screen.")]
    public int baseRendererIndex = 1;
    [Tooltip("Pixel size: the camera renders at this share of the screen's resolution while the pixel look is fully in force (point upscaled), and the wipe's full-resolution cells match it.")]
    [Range(0.1f, 1f)] public float pixelRenderScale = 0.3f;
    [Tooltip("Use the low-resolution render (fast) once the look is fully in. Off = the full-resolution pass all the time.")]
    public bool useRenderScale = true;
    [Range(0f, 1f)] public float paletteMix = 0.8f;
    [Range(0f, 1f)] public float paletteDither = 0.5f;
    [Tooltip("The reference's measured palette (sRGB), plus the runner's yellow, the danger red and the coral of the legs.")]
    public Color[] palette = DefaultPalette();

    [Header("Guide line over the next leg (DynamicPathGenerator.guideTrailColor)")]
    public Color guideColor = new Color(0.55f, 0.97f, 1f, 0.9f);

    [Header("Backdrop")]
    public Material backdropMaterial;
    [Tooltip("Backdrop drift per degree of camera yaw (screen widths).")]
    public float yawParallax = 0.004f;
    [Tooltip("Backdrop slide per unit the camera travels (screen heights); nearer layers move up to ~1.2x this.")]
    public float travelParallax = 0.006f;

    [Header("Fireflies")]
    public Material moteMaterial;
    [Range(0, 400)] public int moteCount = 160;
    public float moteRate = 22f;
    public Vector3 moteBox = new Vector3(44f, 26f, 44f);
    public Vector2 moteSize = new Vector2(0.14f, 0.34f);

    [Header("Rocks while in the pixel checkpoint")]
    public bool restyleRockLayout = true;
    public RockLayout pixelRocks = new RockLayout();
    [Range(0f, 0.9f)] public float pixelVeil = 0.12f;
    [Range(0f, 1f)] public float pixelHazeMax = 0.45f;
    public float fogEaseSeconds = 1.5f;

    static readonly int LookId = Shader.PropertyToID("_PixLook");
    static readonly int MossId = Shader.PropertyToID("_PixMoss");
    static readonly int MossLightId = Shader.PropertyToID("_PixMossLight");
    static readonly int MossDarkId = Shader.PropertyToID("_PixMossDark");
    static readonly int MossGlowId = Shader.PropertyToID("_PixMossGlow");
    static readonly int MortarId = Shader.PropertyToID("_PixMortar");
    static readonly int RockLitId = Shader.PropertyToID("_PixRockLit");
    static readonly int CaveMossId = Shader.PropertyToID("_PixCaveMoss");
    static readonly int CaveMossLightId = Shader.PropertyToID("_PixCaveMossLight");
    static readonly int CaveMossDarkId = Shader.PropertyToID("_PixCaveMossDark");
    static readonly int CaveMossGlowId = Shader.PropertyToID("_PixCaveMossGlow");
    static readonly int RockMidId = Shader.PropertyToID("_PixRockMid");
    static readonly int RockShadeId = Shader.PropertyToID("_PixRockShade");
    static readonly int RockDeepId = Shader.PropertyToID("_PixRockDeep");
    static readonly int ShroomId = Shader.PropertyToID("_PixShroom");
    static readonly int ShroomLightId = Shader.PropertyToID("_PixShroomLight");
    static readonly int LightDirId = Shader.PropertyToID("_PixLightDir");
    static readonly int ParamsId = Shader.PropertyToID("_PixParams");
    static readonly int BackdropId = Shader.PropertyToID("_PixBackdrop");
    static readonly int PaletteId = Shader.PropertyToID("_PixPalette");
    static readonly int PostId = Shader.PropertyToID("_PixPost");

    readonly Vector4[] paletteArray = new Vector4[32];
    RockLayout originalRocks;
    bool rocksPixel;
    bool pixOnScreen, rendererPix;
    Camera cam;
    UniversalAdditionalCameraData camData;
    GameObject backdrop, fireflies;
    MeshRenderer backdropRenderer;
    ParticleSystem moteSystem;
    UniversalRenderPipelineAsset pipeline;
    float sceneRenderScale = 1f;
    UpscalingFilterSelection sceneUpscaling;
    bool scaled;
    int paletteCount;
    ChasmFog chasm;
    float sceneVeil, sceneHazeMax, fogBlend = -1f;
    bool fogCaptured;
    Color sceneGuideColor;
    bool guideCaptured, guidePix;

    /// <summary>True while the pixel look is in force or being wiped in or out (for tooling).</summary>
    public bool PixelOnScreen => pixOnScreen;
    public bool PixelRockLayout => rocksPixel;
    /// <summary>True while the camera renders at pixelRenderScale (the pixel look fully in force).</summary>
    public bool LowResActive => scaled;

    public static Color[] DefaultPalette()
    {
        string[] hex =
        {
            // measured (k-means, 20 colours of the reference)
            "030113", "01012C", "0A0448", "10035F", "1B1B7D", "2F11DA", "2C31A1", "3B47CC",
            "454FED", "4367D9", "4676ED", "4B92EA", "50C8EF", "7DF0F9", "E4E1E5", "CD6B7D",
            // the legs' jade moss
            "1E9E6A", "3FD08A", "8AF5B8", "D0FFE0",
            // the reference's mushrooms, the legs' coral, the cave moss, the runner's yellow, danger red
            "E06981", "DD8C64", "F4A07A", "A8485C", "6A2448", "3A1440", "35B0EC", "1E6EC8", "FFD34A", "B8862E",
            "FFF4C8", "E0304E",
        };
        var c = new Color[hex.Length];
        for (int i = 0; i < hex.Length; i++) ColorUtility.TryParseHtmlString("#" + hex[i], out c[i]);
        return c;
    }

    void Awake()
    {
        if (director == null) director = FindFirstObjectByType<LookTransitionDirector>();
        if (background == null) background = FindFirstObjectByType<PathBackgroundGenerator>();
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (runner == null) runner = FindFirstObjectByType<CubeRailRunner>();
        if (rig == null) rig = FindFirstObjectByType<CameraParentRotator>();
        if (toon == null) toon = FindFirstObjectByType<ToonCheckpointController>();
        PushPalette();
        Shader.SetGlobalVector(LookId, Vector4.zero);
    }

    void Start()
    {
        cam = rig != null && rig.boomCamera != null ? rig.boomCamera : Camera.main;
        if (cam != null) camData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (background != null) originalRocks = RockLayout.From(background);
        BuildBackdrop();
        BuildFireflies();
    }

    void OnDisable()
    {
        Shader.SetGlobalVector(LookId, Vector4.zero);
        SetLowRes(false);
        if (camData != null && rendererPix) camData.SetRenderer(baseRendererIndex);
        rendererPix = false;
        if (rocksPixel && background != null && originalRocks != null) originalRocks.ApplyTo(background);
        rocksPixel = false;
        if (guidePix && pathGen != null) pathGen.guideTrailColor = sceneGuideColor;
        guidePix = false;
        if (fogCaptured && chasm != null) { chasm.veil = sceneVeil; chasm.hazeMax = sceneHazeMax; }
    }

    void OnDestroy()
    {
        if (backdrop != null) { Destroy(backdrop.GetComponent<MeshFilter>().sharedMesh); Destroy(backdrop); }
        if (fireflies != null) Destroy(fireflies);
    }

    void OnValidate()
    {
        if (isActiveAndEnabled) PushPalette();
    }

    bool IsPixel(AtmosphereLook l) =>
        l != null && pixelLook != null && (l == pixelLook || (!string.IsNullOrEmpty(pixelLook.sourceScene) && l.sourceScene == pixelLook.sourceScene));

    void LateUpdate()
    {
        AtmosphereLook now = director != null ? director.CurrentLook : null;
        AtmosphereLook next = director != null ? director.NextLook : null;
        bool a = IsPixel(now), b = IsPixel(next);
        pixOnScreen = alwaysPixel || a || b;
        bool pixZone = alwaysPixel || (next != null ? b : a);
        Shader.SetGlobalVector(LookId, new Vector4(a ? 1f : 0f, b ? 1f : 0f, alwaysPixel ? 1f : 0f, 0f));

        PushPalette();
        PushLight();
        UpdateRenderer();
        SetLowRes(useRenderScale && pixelRenderScale < 0.999f && (alwaysPixel || (a && next == null)));
        PushPost();
        UpdateGuide();
        UpdateRocks(pixZone);
        UpdateBackdrop();
        UpdateFireflies();
        UpdateFog();
    }

    // ------------------------------------------------------------------ globals

    static Vector4 Lin(Color c, float a) { Color l = c.linear; return new Vector4(l.r, l.g, l.b, a); }

    void PushPalette()
    {
        Shader.SetGlobalVector(MossId, Lin(moss, mossAmount));
        Shader.SetGlobalVector(MossLightId, Lin(mossLight, 1f));
        Shader.SetGlobalVector(MossDarkId, Lin(mossDark, longestDrip));
        Shader.SetGlobalVector(MossGlowId, Lin(mossGlow, glowChance));
        Shader.SetGlobalVector(MortarId, Lin(mortar, 1f));
        Shader.SetGlobalVector(RockLitId, Lin(rockLit, 1f));
        Shader.SetGlobalVector(CaveMossId, Lin(caveMoss, 1f));
        Shader.SetGlobalVector(CaveMossLightId, Lin(caveMossLight, 1f));
        Shader.SetGlobalVector(CaveMossDarkId, Lin(caveMossDark, 1f));
        Shader.SetGlobalVector(CaveMossGlowId, Lin(caveMossGlow, 1f));
        Shader.SetGlobalVector(RockMidId, Lin(rockMid, 1f));
        Shader.SetGlobalVector(RockShadeId, Lin(rockShade, 1f));
        Shader.SetGlobalVector(RockDeepId, Lin(rockDeep, rockDither));
        Shader.SetGlobalVector(ShroomId, Lin(shroom, shroomChance));
        Shader.SetGlobalVector(ShroomLightId, Lin(shroomLight, shroomGlow));
        Shader.SetGlobalVector(ParamsId, new Vector4(legTexels, rockMossThreshold, rockDripChance, 0f));
        int n = palette != null ? Mathf.Min(palette.Length, paletteArray.Length) : 0;
        for (int i = 0; i < paletteArray.Length; i++)
        {
            Color c = i < n ? palette[i] : Color.black;   // sRGB on purpose: the pass compares in sRGB
            paletteArray[i] = new Vector4(c.r, c.g, c.b, i < n ? 1f : 0f);
        }
        Shader.SetGlobalVectorArray(PaletteId, paletteArray);
        paletteCount = n;
        PushPost();
    }

    // _PixPost: x palette size, y cell size (pixels at 1920 high), z palette mix, w dither. At low resolution one cell = one
    // rendered pixel (the upscale makes the pixels); at full resolution one cell = 1 / pixelRenderScale screen pixels.
    void PushPost()
    {
        float h = Mathf.Max(1, Screen.height);
        float cellPx = scaled ? 1f : 1f / Mathf.Max(0.05f, pixelRenderScale);
        float renderedH = scaled ? h * pixelRenderScale : h;
        Shader.SetGlobalVector(PostId, new Vector4(paletteCount, cellPx * 1920f / renderedH, paletteMix, paletteDither));
    }

    // The pipeline's render scale + point upscaling while the pixel look is fully in force (restored after, and on disable).
    void SetLowRes(bool on)
    {
        if (on == scaled) return;
        if (on)
        {
            pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) return;
            sceneRenderScale = pipeline.renderScale;
            sceneUpscaling = pipeline.upscalingFilter;
            pipeline.renderScale = pixelRenderScale;
            pipeline.upscalingFilter = UpscalingFilterSelection.Point;
        }
        else if (pipeline != null)
        {
            pipeline.renderScale = sceneRenderScale;
            pipeline.upscalingFilter = sceneUpscaling;
        }
        scaled = on;
    }

    Vector3 FlatRight()
    {
        if (cam == null) return Vector3.right;
        Vector3 r = cam.transform.right; r.y = 0f;
        return r.sqrMagnitude < 1e-4f ? Vector3.right : r.normalized;
    }

    Vector3 FlatForward()
    {
        if (cam == null) return Vector3.forward;
        Vector3 f = cam.transform.forward; f.y = 0f;
        return f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
    }

    void PushLight()
    {
        Vector3 l = (Vector3.up * lightUp + FlatRight() * (litRight ? lightSide : -lightSide) - FlatForward() * lightBack).normalized;
        Shader.SetGlobalVector(LightDirId, new Vector4(l.x, l.y, l.z, rockTexels));
    }

    // ------------------------------------------------------------------ pixel pass

    void UpdateRenderer()
    {
        if (camData == null || pixelRendererIndex < 0) return;
        if (pixOnScreen)
        {
            // Every frame: the toon controller (earlier in the frame) puts its own renderer back when the toon look leaves.
            camData.SetRenderer(pixelRendererIndex);
            rendererPix = true;
        }
        else if (rendererPix)
        {
            bool toonOn = toon != null && toon.isActiveAndEnabled && toon.ToonOnScreen;
            camData.SetRenderer(toonOn ? toon.toonRendererIndex : baseRendererIndex);
            rendererPix = false;
        }
    }

    // ------------------------------------------------------------------ guide line

    void UpdateGuide()
    {
        if (pathGen == null) return;
        if (!guideCaptured) { sceneGuideColor = pathGen.guideTrailColor; guideCaptured = true; }
        if (pixOnScreen)
        {
            pathGen.guideTrailColor = guideColor;
            guidePix = true;
        }
        else if (guidePix)
        {
            bool toonOn = toon != null && toon.isActiveAndEnabled && toon.ToonOnScreen;
            pathGen.guideTrailColor = toonOn ? toon.guideColor : sceneGuideColor;
            guidePix = false;
        }
    }

    // ------------------------------------------------------------------ rocks + fog

    void UpdateRocks(bool pixZone)
    {
        if (!restyleRockLayout || background == null || originalRocks == null) return;
        if (pixZone == rocksPixel) return;
        rocksPixel = pixZone;
        (pixZone ? pixelRocks : originalRocks).ApplyTo(background);
        background.RebuildOutOfSight();
    }

    void UpdateFog()
    {
        if (chasm == null) chasm = ChasmFog.Active;
        if (chasm == null) return;
        if (!fogCaptured) { sceneVeil = chasm.veil; sceneHazeMax = chasm.hazeMax; fogCaptured = true; }
        float target = pixOnScreen ? 1f : 0f;
        if (fogBlend == target && !pixOnScreen) return;               // settled outside: leave the scene's fog alone
        fogBlend = fogBlend < 0f ? target : Mathf.MoveTowards(fogBlend, target, Time.deltaTime / Mathf.Max(0.01f, fogEaseSeconds));
        float k = fogBlend * fogBlend * (3f - 2f * fogBlend);
        chasm.veil = Mathf.Lerp(sceneVeil, pixelVeil, k);
        chasm.hazeMax = Mathf.Lerp(sceneHazeMax, pixelHazeMax, k);
    }

    // ------------------------------------------------------------------ backdrop

    static Mesh Quad(string name, bool horizontal)
    {
        var m = new Mesh { name = name };
        if (horizontal)
            m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
        else
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateNormals();
        return m;
    }

    MeshRenderer MakeRenderer(GameObject go, Mesh mesh, Material mat)
    {
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.enabled = false;
        return r;
    }

    void BuildBackdrop()
    {
        if (backdropMaterial == null || cam == null) return;
        backdrop = new GameObject("PixelBackdrop");
        backdrop.transform.SetParent(cam.transform, false);
        backdrop.transform.localPosition = new Vector3(0f, 0f, Mathf.Min(cam.farClipPlane * 0.5f, 200f));
        Mesh m = Quad("PixelBackdropQuad", false);
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);   // the shader places it on screen: never cull it
        backdropRenderer = MakeRenderer(backdrop, m, backdropMaterial);
    }

    void UpdateBackdrop()
    {
        if (backdropRenderer == null) return;
        backdropRenderer.enabled = pixOnScreen;
        float yaw = cam != null ? cam.transform.eulerAngles.y : 0f;
        Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;
        // Wrapped to keep the floats small (a wrap needs ~160k units of travel).
        float tx = Mathf.Repeat(Vector3.Dot(cp, FlatRight()) * travelParallax, 1000f);
        float ty = Mathf.Repeat(cp.y * travelParallax, 1000f);
        Shader.SetGlobalVector(BackdropId, new Vector4(-yaw * yawParallax, Time.time, tx, ty));
    }

    // ------------------------------------------------------------------ fireflies

    void BuildFireflies()
    {
        if (moteMaterial == null || moteCount <= 0) return;
        fireflies = new GameObject("PixelFireflies");
        fireflies.transform.SetParent(transform, false);
        moteSystem = fireflies.AddComponent<ParticleSystem>();
        moteSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = moteSystem.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.45f);
        main.startSize = new ParticleSystem.MinMaxCurve(moteSize.x, moteSize.y);
        var g = new Gradient();
        g.mode = GradientMode.Fixed;
        g.SetKeys(new[]
        {
            new GradientColorKey(caveMossLight, 0.45f), new GradientColorKey(caveMoss, 0.75f), new GradientColorKey(caveMossGlow, 0.92f),
            new GradientColorKey(shroom, 1f),
        }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        var sc = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
        main.startColor = sc;
        main.maxParticles = moteCount;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var em = moteSystem.emission;
        em.rateOverTime = moteRate;
        var sh = moteSystem.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = moteBox;
        var col = moteSystem.colorOverLifetime;
        col.enabled = true;
        var life = new Gradient();
        life.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.35f, 0.4f),
                    new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0.5f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(life);
        var noise = moteSystem.noise;
        noise.enabled = true;
        noise.strength = 0.4f;
        noise.frequency = 0.25f;
        noise.scrollSpeed = 0.15f;

        var r = fireflies.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = moteMaterial;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        moteSystem.Play();
    }

    void UpdateFireflies()
    {
        if (moteSystem == null) return;
        if (runner != null) fireflies.transform.position = runner.transform.position + Vector3.up * 3f;
        var em = moteSystem.emission;
        if (em.enabled != pixOnScreen) em.enabled = pixOnScreen;
    }
}
