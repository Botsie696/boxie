using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Space checkpoint "Rogue Planet" (MainScene prop camera 3.10 checkpoint 9, right after Sketchbook, and the whole run of
/// MainScene prop camera Space): everything the oil-painted deep-space look needs besides the look asset (Look_Space:
/// colours, sky, fog, sun, post). References: a thick-impasto purple/blue spaceship over a planet, a Saturn oil painting
/// over an orange sunset, launching shuttles in teal / violet / amber smoke, a pale red planet with mesas.
///
///   Where      _SpLook (x: the look in force is space, y: the look being wiped in is, z: always) - every space shader
///              (SpaceCommon.hlsl) switches along the look wipe with it
///   Palette    leg top / side / underside colours, hull cream, engine orange, console cyan / magenta, window light,
///              the Martian strata - constant globals read by the leg texture (SpaceLeg.hlsl: deck, portholes, glass
///              corridors, engine bays, whole ships), the rocks (SpaceRock.hlsl) and the trail (PixelFaceTrail)
///   Paint      while the look is on screen the camera draws with SpaceCP_Renderer: the oil pass (Kuwahara dabs +
///              impasto light); the sketchbook's passes ride along only while the sketchbook is on screen too
///   Backdrop   SpaceBackdrop: the painted sunset skybox with a ringed planet, a cropped giant, moons, far shuttle
///              launches in billowing smoke (far, small, behind everything) and a row of mesas
///   Rocks      the checkpoint's own far Martian mesas, buttes and spires: placed far out and low, hazed to the
///              sunset (no floating rocks, no debris, nothing near the legs)
/// Purely visual: it never touches gameplay, the camera's framing or any asset (the renderer features it switches are
/// put back on disable).
/// </summary>
[DefaultExecutionOrder(1010)]   // after the pencil / voxel / flower / storybook / pixel / toon controllers: its renderer wins while on screen
public class SpaceCheckpointController : MonoBehaviour
{
    [Header("The space look")]
    [Tooltip("Look_Space. The checkpoint is wherever this look is in force (or being wiped in).")]
    public AtmosphereLook spaceLook;
    [Tooltip("The whole run is space (MainScene prop camera Space).")]
    public bool alwaysSpace;
    [Tooltip("Portal whose runway out starts the space level. 3.10: 8.")]
    public int spacePortal = 8;

    [Header("References (found when empty)")]
    public LookTransitionDirector director;
    public PathBackgroundGenerator background;
    public DynamicPathGenerator pathGen;
    public CubeRailRunner runner;
    public CameraParentRotator rig;
    public PencilCheckpointController pencil;
    public FlowerCheckpointController flower;
    public StorybookCheckpointController story;
    public PixelCheckpointController pixel;
    public ToonCheckpointController toon;

    [Header("Palette (sRGB) - measured from the references")]
    [Tooltip("Leg tops: the deck, pale periwinkle hull plate (lit / shade).")]
    public Color top = new Color32(0x9A, 0xA8, 0xF2, 0xFF);
    public Color topDark = new Color32(0x56, 0x62, 0xC4, 0xFF);
    [Range(0f, 1f)] public float brush = 0.85f;
    [Tooltip("Leg sides: royal blue (lit) / deep indigo (shade).")]
    public Color side = new Color32(0x3A, 0x5C, 0xE0, 0xFF);
    public Color sideDark = new Color32(0x1C, 0x22, 0x84, 0xFF);
    [Tooltip("Leg undersides: hot coral-orange (lit) / plum (shade).")]
    public Color bottom = new Color32(0xE8, 0x62, 0x3C, 0xFF);
    public Color bottomDark = new Color32(0x8A, 0x2A, 0x58, 0xFF);
    public Color accent = new Color32(0xEC, 0x74, 0x40, 0xFF);
    public Color glow = new Color32(0x5F, 0xE8, 0xFF, 0xFF);
    public Color window = new Color32(0xFF, 0xC8, 0x78, 0xFF);
    public Color cream = new Color32(0xEF, 0xE6, 0xCD, 0xFF);
    public Color magenta = new Color32(0xD0, 0x40, 0xA8, 0xFF);
    [Range(0.4f, 1.6f)] public float windowGlow = 1.0f;
    [Header("Martian rock (sRGB)")]
    public Color rockLit = new Color32(0xE0, 0x7A, 0x52, 0xFF);
    public Color rockShade = new Color32(0x5C, 0x2C, 0x58, 0xFF);
    public Color rockBand0 = new Color32(0xC2, 0x58, 0x3E, 0xFF);
    public Color rockBand1 = new Color32(0xF2, 0xB0, 0x78, 0xFF);
    public Color rockDust = new Color32(0xEE, 0xC8, 0xB0, 0xFF);

    [Header("Light (camera relative)")]
    public bool litRight = false;
    public float lightUp = 1f, lightSide = 0.8f, lightBack = 0.45f;
    [Range(0.1f, 3f)] public float rockBrushScale = 0.7f;

    [Header("Renderer")]
    [Tooltip("Renderer index of SpaceCP_Renderer on the pipeline assets. -1 = no paint pass.")]
    public int spaceRendererIndex = 7;
    [Tooltip("The camera's own renderer index, put back when the look leaves the screen.")]
    public int baseRendererIndex = 1;
    [Tooltip("SpaceCP_Renderer: its sketchbook features are only switched on while the sketchbook is on screen too.")]
    public UniversalRendererData spaceRenderer;

    [Header("Guide line over the next leg (DynamicPathGenerator.guideTrailColor)")]
    public Color guideColor = new Color(1f, 0.93f, 0.8f, 0.9f);

    [Header("Backdrop")]
    public Material backdropMaterial;
    public float yawParallax = 0.003f;
    public float travelParallax = 0.004f;

    [Header("Far Martian rocks while in the checkpoint")]
    public bool restyleRockLayout = true;
    public PixelCheckpointController.RockLayout spaceRocks = new PixelCheckpointController.RockLayout
    {
        wallChance = 0.9f, wallsPerCell = 2, wallWidth = new Vector2(16f, 34f), wallTopRange = new Vector2(-36f, -8f),
        wallClearance = 38f, floatingChance = 0f, floatingPerCell = 0, floatingWidth = new Vector2(3f, 6f),
        floatingHeightRange = new Vector2(-20f, -8f), floatingClearance = 6f, debrisPerCell = 0, futureReach = 80f, futureDrop = 26f,
    };
    [Range(0f, 0.9f)] public float spaceVeil = 0.06f;
    [Range(0f, 1f)] public float spaceHazeMax = 0.5f;
    public float fogEaseSeconds = 1.5f;
    public bool ownCliffs = true;

    static readonly int LookId = Shader.PropertyToID("_SpLook");
    static readonly int TopId = Shader.PropertyToID("_SpTop");
    static readonly int TopDarkId = Shader.PropertyToID("_SpTopDark");
    static readonly int SideId = Shader.PropertyToID("_SpSide");
    static readonly int SideDarkId = Shader.PropertyToID("_SpSideDark");
    static readonly int BottomId = Shader.PropertyToID("_SpBottom");
    static readonly int BottomDarkId = Shader.PropertyToID("_SpBottomDark");
    static readonly int AccentId = Shader.PropertyToID("_SpAccent");
    static readonly int GlowId = Shader.PropertyToID("_SpGlow");
    static readonly int WindowId = Shader.PropertyToID("_SpWindow");
    static readonly int CreamId = Shader.PropertyToID("_SpCream");
    static readonly int MagentaId = Shader.PropertyToID("_SpMagenta");
    static readonly int RockLitId = Shader.PropertyToID("_SpRockLit");
    static readonly int RockShadeId = Shader.PropertyToID("_SpRockShade");
    static readonly int RockBand0Id = Shader.PropertyToID("_SpRockBand0");
    static readonly int RockBand1Id = Shader.PropertyToID("_SpRockBand1");
    static readonly int RockDustId = Shader.PropertyToID("_SpRockDust");
    static readonly int ParamsId = Shader.PropertyToID("_SpParams");
    static readonly int LightDirId = Shader.PropertyToID("_SpLightDir");
    static readonly int BackdropId = Shader.PropertyToID("_SpBackdrop");
    const string LinesFeature = "PencilLines", PaperFeature = "PencilPaper";

    PixelCheckpointController.RockLayout originalRocks;
    bool rocksSp, spOnScreen, rendererSp;
    Camera cam;
    UniversalAdditionalCameraData camData;
    GameObject backdrop;
    MeshRenderer backdropRenderer;
    ChasmFog chasm;
    float sceneVeil, sceneHazeMax, fogBlend = -1f, entryVeil, entryHaze;
    bool fogCaptured;
    Color sceneGuideColor;
    bool guideCaptured, guideSp;
    ScriptableRendererFeature linesFeature, paperFeature;
    bool sketchFeaturesOn = true;

    GameObject[] cliffTemplates, sceneWallPrefabs;
    GameObject cliffRoot;
    bool cliffsOn;
    readonly List<Mesh> madeMeshes = new List<Mesh>();

    public bool SpaceOnScreen => spOnScreen;
    public bool SpaceRockLayout => rocksSp;

    void Awake()
    {
        if (director == null) director = FindFirstObjectByType<LookTransitionDirector>();
        if (background == null) background = FindFirstObjectByType<PathBackgroundGenerator>();
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (runner == null) runner = FindFirstObjectByType<CubeRailRunner>();
        if (rig == null) rig = FindFirstObjectByType<CameraParentRotator>();
        if (pencil == null) pencil = FindFirstObjectByType<PencilCheckpointController>();
        if (flower == null) flower = FindFirstObjectByType<FlowerCheckpointController>();
        if (story == null) story = FindFirstObjectByType<StorybookCheckpointController>();
        if (pixel == null) pixel = FindFirstObjectByType<PixelCheckpointController>();
        if (toon == null) toon = FindFirstObjectByType<ToonCheckpointController>();
        PushPalette();
        Shader.SetGlobalVector(LookId, Vector4.zero);
        if (spaceRenderer != null)
            foreach (ScriptableRendererFeature f in spaceRenderer.rendererFeatures)
            {
                if (f == null) continue;
                if (f.name == LinesFeature) linesFeature = f;
                else if (f.name == PaperFeature) paperFeature = f;
            }
        SetSketchFeatures(false);
    }

    void Start()
    {
        cam = rig != null && rig.boomCamera != null ? rig.boomCamera : Camera.main;
        if (cam != null) camData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (background != null) originalRocks = PixelCheckpointController.RockLayout.From(background);
        BuildBackdrop();
        BuildCliffs();
    }

    void OnDisable()
    {
        Shader.SetGlobalVector(LookId, Vector4.zero);
        if (camData != null && rendererSp) camData.SetRenderer(baseRendererIndex);
        rendererSp = false;
        SetSketchFeatures(false);
        if (rocksSp && background != null && originalRocks != null) originalRocks.ApplyTo(background);
        if (cliffsOn && background != null) background.wallPrefabs = sceneWallPrefabs;
        cliffsOn = false;
        rocksSp = false;
        if (guideSp && pathGen != null) pathGen.guideTrailColor = sceneGuideColor;
        guideSp = false;
        if (fogCaptured && chasm != null) { chasm.veil = sceneVeil; chasm.hazeMax = sceneHazeMax; }
    }

    void OnDestroy()
    {
        if (backdrop != null) { Destroy(backdrop.GetComponent<MeshFilter>().sharedMesh); Destroy(backdrop); }
        foreach (Mesh m in madeMeshes) if (m != null) Destroy(m);
        if (cliffRoot != null) Destroy(cliffRoot);
    }

    void OnValidate()
    {
        if (isActiveAndEnabled) PushPalette();
    }

    bool IsSpace(AtmosphereLook l) =>
        l != null && spaceLook != null && (l == spaceLook || (!string.IsNullOrEmpty(spaceLook.sourceScene) && l.sourceScene == spaceLook.sourceScene));

    void LateUpdate()
    {
        AtmosphereLook now = director != null ? director.CurrentLook : null;
        AtmosphereLook next = director != null ? director.NextLook : null;
        bool a = IsSpace(now), b = IsSpace(next);
        spOnScreen = alwaysSpace || a || b;
        bool zone = alwaysSpace || (next != null ? b : a);
        Shader.SetGlobalVector(LookId, new Vector4(a ? 1f : 0f, b ? 1f : 0f, alwaysSpace ? 1f : 0f, 0f));

        PushPalette();
        PushLight();
        UpdateRenderer();
        UpdateGuide();
        UpdateRocks(zone);
        UpdateBackdrop();
        UpdateFog();
    }

    // ------------------------------------------------------------------ globals

    static Vector4 Lin(Color c, float a) { Color l = c.linear; return new Vector4(l.r, l.g, l.b, a); }

    void PushPalette()
    {
        Shader.SetGlobalVector(TopId, Lin(top, brush));
        Shader.SetGlobalVector(TopDarkId, Lin(topDark, 1f));
        Shader.SetGlobalVector(SideId, Lin(side, 1f));
        Shader.SetGlobalVector(SideDarkId, Lin(sideDark, 1f));
        Shader.SetGlobalVector(BottomId, Lin(bottom, 1f));
        Shader.SetGlobalVector(BottomDarkId, Lin(bottomDark, 1f));
        Shader.SetGlobalVector(AccentId, Lin(accent, 1f));
        Shader.SetGlobalVector(GlowId, Lin(glow, 1f));
        Shader.SetGlobalVector(WindowId, Lin(window, 1f));
        Shader.SetGlobalVector(CreamId, Lin(cream, 1f));
        Shader.SetGlobalVector(MagentaId, Lin(magenta, 1f));
        Shader.SetGlobalVector(RockLitId, Lin(rockLit, 1f));
        Shader.SetGlobalVector(RockShadeId, Lin(rockShade, 1f));
        Shader.SetGlobalVector(RockBand0Id, Lin(rockBand0, 1f));
        Shader.SetGlobalVector(RockBand1Id, Lin(rockBand1, 1f));
        Shader.SetGlobalVector(RockDustId, Lin(rockDust, 1f));
        Shader.SetGlobalVector(ParamsId, new Vector4(0.25f, windowGlow, 1f, 0f));
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-4f ? Vector3.right : v.normalized;
    }

    void PushLight()
    {
        Vector3 r = cam != null ? Flat(cam.transform.right) : Vector3.right;
        Vector3 f = cam != null ? Flat(cam.transform.forward) : Vector3.forward;
        Vector3 l = (Vector3.up * lightUp + r * (litRight ? lightSide : -lightSide) - f * lightBack).normalized;
        Shader.SetGlobalVector(LightDirId, new Vector4(l.x, l.y, l.z, rockBrushScale));
    }

    // ------------------------------------------------------------------ renderer + guide

    void SetSketchFeatures(bool on)
    {
        if (sketchFeaturesOn == on) return;
        sketchFeaturesOn = on;
        if (linesFeature != null) linesFeature.SetActive(on);
        if (paperFeature != null) paperFeature.SetActive(on);
    }

    bool PencilVisible => pencil != null && pencil.isActiveAndEnabled && pencil.PencilOnScreen;

    void UpdateRenderer()
    {
        if (camData == null || spaceRendererIndex < 0) return;
        if (spOnScreen)
        {
            camData.SetRenderer(spaceRendererIndex);   // every frame: the other checkpoint controllers run earlier
            rendererSp = true;
            SetSketchFeatures(PencilVisible);
        }
        else if (rendererSp)
        {
            camData.SetRenderer(baseRendererIndex);
            rendererSp = false;
            SetSketchFeatures(false);
        }
    }

    void UpdateGuide()
    {
        if (pathGen == null) return;
        if (!guideCaptured) { sceneGuideColor = pathGen.guideTrailColor; guideCaptured = true; }
        if (spOnScreen)
        {
            pathGen.guideTrailColor = guideColor;
            guideSp = true;
        }
        else if (guideSp)
        {
            if (PencilVisible) pathGen.guideTrailColor = pencil.guideColor;
            else pathGen.guideTrailColor = sceneGuideColor;
            guideSp = false;
        }
    }

    // ------------------------------------------------------------------ rocks + fog

    void UpdateRocks(bool zone)
    {
        if (background == null || originalRocks == null) return;
        if (zone == rocksSp) return;
        rocksSp = zone;
        if (restyleRockLayout)
        {
            // Leaving: the scene's layout, unless an earlier-running checkpoint already put its own this frame.
            if (zone) spaceRocks.ApplyTo(background);
            else if ((pencil == null || !pencil.PencilRockLayout) && (flower == null || !flower.FlowerRockLayout)
                     && (story == null || !story.StoryRockLayout) && (pixel == null || !pixel.PixelRockLayout))
                originalRocks.ApplyTo(background);
        }
        if (ownCliffs && cliffTemplates != null)
        {
            if (zone) { sceneWallPrefabs = background.wallPrefabs; background.wallPrefabs = cliffTemplates; cliffsOn = true; }
            else if (cliffsOn) { background.wallPrefabs = sceneWallPrefabs; cliffsOn = false; }
        }
        background.RebuildOutOfSight();
    }

    void UpdateFog()
    {
        if (chasm == null) chasm = ChasmFog.Active;
        if (chasm == null) return;
        if (!fogCaptured) { sceneVeil = chasm.veil; sceneHazeMax = chasm.hazeMax; fogCaptured = true; }
        float target = spOnScreen ? 1f : 0f;
        if (fogBlend == target && !spOnScreen) return;
        if (fogBlend <= 0f && spOnScreen) { entryVeil = chasm.veil; entryHaze = chasm.hazeMax; }
        fogBlend = fogBlend < 0f ? target : Mathf.MoveTowards(fogBlend, target, Time.deltaTime / Mathf.Max(0.01f, fogEaseSeconds));
        float k = fogBlend * fogBlend * (3f - 2f * fogBlend);
        if (spOnScreen)
        {
            chasm.veil = Mathf.Lerp(entryVeil, spaceVeil, k);
            chasm.hazeMax = Mathf.Lerp(entryHaze, spaceHazeMax, k);
        }
        else
        {
            chasm.veil = Mathf.Lerp(sceneVeil, spaceVeil, k);
            chasm.hazeMax = Mathf.Lerp(sceneHazeMax, spaceHazeMax, k);
        }
    }

    // ------------------------------------------------------------------ backdrop

    void BuildBackdrop()
    {
        if (backdropMaterial == null || cam == null) return;
        backdrop = new GameObject("SpaceBackdrop");
        backdrop.transform.SetParent(cam.transform, false);
        backdrop.transform.localPosition = new Vector3(0f, 0f, Mathf.Min(cam.farClipPlane * 0.5f, 200f));
        var m = new Mesh { name = "SpaceBackdropQuad" };
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        backdrop.AddComponent<MeshFilter>().sharedMesh = m;
        backdropRenderer = backdrop.AddComponent<MeshRenderer>();
        backdropRenderer.sharedMaterial = backdropMaterial;
        backdropRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        backdropRenderer.receiveShadows = false;
        backdropRenderer.enabled = false;
    }

    void UpdateBackdrop()
    {
        if (backdropRenderer == null) return;
        backdropRenderer.enabled = spOnScreen;
        if (!spOnScreen) return;
        float yaw = cam.transform.eulerAngles.y;
        Vector3 cp = cam.transform.position;
        float tx = Mathf.Repeat(Vector3.Dot(cp, Flat(cam.transform.right)) * travelParallax, 1000f);
        float ty = Mathf.Repeat(cp.y * travelParallax, 1000f);
        Shader.SetGlobalVector(BackdropId, new Vector4(-yaw * yawParallax, Time.time, tx, ty));
    }

    // ------------------------------------------------------------------ far Martian rocks

    class Kit
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> nr = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();

        public void Tri(Vector3 a, Vector3 b, Vector3 d)
        {
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(d);
            nr.Add(n); nr.Add(n); nr.Add(n);
            uv.Add(Vector2.zero); uv.Add(Vector2.zero); uv.Add(Vector2.zero);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e) { Tri(a, b, d); Tri(a, d, e); }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(nr); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    Mesh Keep(Mesh m) { madeMeshes.Add(m); return m; }

    // A jagged ring of "sides" points round c at height y, radius r (per-corner jag from the array).
    static Vector3[] Ring(Vector2 c, float y, float r, float phase, float[] jag, float squashZ = 1f)
    {
        int n = jag.Length;
        var p = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float a = phase + i * Mathf.PI * 2f / n;
            float rr = r * jag[i];
            p[i] = new Vector3(c.x + Mathf.Cos(a) * rr, y, c.y + Mathf.Sin(a) * rr * squashZ);
        }
        return p;
    }

    static void Join(Kit kit, Vector3[] lo, Vector3[] hi)
    {
        int n = lo.Length;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            kit.Quad(lo[i], hi[i], hi[j], lo[j]);
        }
    }

    static void Cap(Kit kit, Vector3[] ring, float y, Vector2 c)
    {
        int n = ring.Length;
        var ctr = new Vector3(c.x, y, c.y);
        for (int i = 0; i < n; i++) kit.Tri(ctr, ring[(i + 1) % n], ring[i]);
    }

    // One Martian mesa: a scree skirt flaring out at the foot, then stepped cliff tiers (a steep wall, a ledge, a
    // narrower wall ...) under a flat cap. base y = 0. Returns the top height.
    static float Mesa(Kit kit, Vector2 c, float r, float h, int tiers, int sides, System.Random rng, float taper = 0.8f)
    {
        var jag = new float[sides];
        for (int i = 0; i < sides; i++) jag[i] = 0.82f + 0.36f * (float)rng.NextDouble();
        float phase = (float)rng.NextDouble() * 6.283f;
        float skirtH = h * 0.14f;
        Vector3[] foot = Ring(c, 0f, r * 1.28f, phase, jag);
        Vector3[] prev = Ring(c, skirtH, r * 1.02f, phase, jag);
        Join(kit, foot, prev);                      // the scree slope
        float y = skirtH;
        float rad = r;
        float tierH = (h - skirtH) / tiers;
        for (int k = 0; k < tiers; k++)
        {
            float wall = tierH * 0.82f;
            Vector3[] topRing = Ring(c, y + wall, rad * 0.97f, phase, jag);
            Join(kit, prev, topRing);               // the cliff wall (slightly battered)
            y += wall;
            if (k < tiers - 1)
            {
                float next = rad * taper;
                Vector3[] ledgeIn = Ring(c, y + tierH * 0.18f, next, phase, jag);   // a sloping ledge back to the next wall
                Join(kit, topRing, ledgeIn);
                prev = ledgeIn;
                y += tierH * 0.18f;
                rad = next;
            }
            else
            {
                prev = topRing;
            }
        }
        Cap(kit, prev, y, c);
        return y;
    }

    // The checkpoint's cliffs: clusters of wide Martian mesas with a spire or two and low buttes (unit footprint, base at
    // y = 0, about 2.2 high). The generator stacks them downward and hazes them to the sunset, so they read as a far,
    // wide, layered plateau country. Templates sit under an inactive root (never drawn); the scene rock's own material is
    // used so the chasm fog / space rock shader applies unchanged.
    void BuildCliffs()
    {
        if (!ownCliffs || background == null || background.wallPrefabs == null || background.wallPrefabs.Length == 0) return;
        Material rockMat = null;
        foreach (GameObject p in background.wallPrefabs)
        {
            var r = p != null ? p.GetComponentInChildren<Renderer>(true) : null;
            if (r != null && r.sharedMaterial != null) { rockMat = r.sharedMaterial; break; }
        }
        if (rockMat == null) return;
        cliffRoot = new GameObject("SpaceMesaTemplates");
        cliffRoot.SetActive(false);
        cliffRoot.transform.SetParent(transform, false);
        var rng = new System.Random(41);
        cliffTemplates = new GameObject[4];
        for (int k = 0; k < cliffTemplates.Length; k++)
        {
            var kit = new Kit();
            // The main mesa.
            Mesa(kit, Vector2.zero, Mathf.Lerp(0.3f, 0.38f, (float)rng.NextDouble()), Mathf.Lerp(1.5f, 2.0f, (float)rng.NextDouble()), 2 + k % 2, 9, rng);
            // A spire and a low butte beside it (different every template).
            int extras = 2 + (k & 1);
            for (int i = 0; i < extras; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float d = Mathf.Lerp(0.34f, 0.5f, (float)rng.NextDouble());
                Vector2 c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                bool spire = i == 0;
                float r = spire ? Mathf.Lerp(0.07f, 0.11f, (float)rng.NextDouble()) : Mathf.Lerp(0.12f, 0.2f, (float)rng.NextDouble());
                float h = spire ? Mathf.Lerp(1.6f, 2.2f, (float)rng.NextDouble()) : Mathf.Lerp(0.6f, 1.1f, (float)rng.NextDouble());
                Mesa(kit, c, r, h, spire ? 1 : 2, spire ? 6 : 8, rng, 0.7f);
            }
            var go = new GameObject("SpMesa" + k);
            go.transform.SetParent(cliffRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Keep(kit.Build("SpMesa" + k));
            go.AddComponent<MeshRenderer>().sharedMaterial = rockMat;
            cliffTemplates[k] = go;
        }
    }
}
