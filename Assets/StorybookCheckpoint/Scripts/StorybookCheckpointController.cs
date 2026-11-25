using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Storybook checkpoint "Storybook Keep" (MainScene prop camera 3.9 checkpoint 5, and the whole run of MainScene prop camera
/// Storybook): everything the pen-and-watercolour castle look needs besides the look asset (Look_Storybook: colours, sky,
/// fog, sun, post).
///
///   Where      _SbLook (x: the look in force is storybook, y: the look being wiped in is, z: always) - every storybook
///              shader (StorybookCommon.hlsl) switches along the look wipe with it
///   Palette    paper, ink, limestone, plaster, timber, roofs, windows, foliage - constant globals; the leg texture
///              (StorybookLeg.hlsl: every leg a building - roof, walls, windows, some legs bridges), the rocks
///              (StorybookRock.hlsl) and the props read them
///   Pen        while the look is on screen the camera draws with StorybookCP_Renderer (the pixel renderer + the storybook
///              pen pass + the paper pass); the previous renderer comes back when it leaves
///   Backdrop   teal watercolour sky, cumulus banks with pen outlines, far castles, birds
///   Rocks      while in the checkpoint the background generator lays low, wide rock tops below the track (ground, not
///              towers), and every rock top gets dressed: tree clumps, now and then a little house with a tile roof or a
///              round tower with a cone roof and a waving flag. Props are children of the rock, so they fade and retire
///              with it (nothing pops in view); cells are only rebuilt where the camera cannot see them
/// Purely visual: it never touches gameplay, the camera's framing or any asset.
/// </summary>
[DefaultExecutionOrder(975)]   // after PixelCheckpointController / ToonCheckpointController: its renderer wins while on screen
public class StorybookCheckpointController : MonoBehaviour
{
    [Header("The storybook look")]
    [Tooltip("Look_Storybook. The checkpoint is wherever this look is in force (or being wiped in).")]
    public AtmosphereLook storyLook;
    [Tooltip("The whole run is storybook (MainScene prop camera Storybook).")]
    public bool alwaysStory;

    [Header("References (found when empty)")]
    public LookTransitionDirector director;
    public PathBackgroundGenerator background;
    public DynamicPathGenerator pathGen;
    public CubeRailRunner runner;
    public CameraParentRotator rig;
    public ToonCheckpointController toon;
    public PixelCheckpointController pixel;

    [Header("Palette (sRGB) - measured from the reference")]
    public Color paper = new Color32(0xEF, 0xE6, 0xCC, 0xFF);
    public Color ink = new Color32(0x3B, 0x2A, 0x1C, 0xFF);
    [Range(0f, 1f)] public float inkStrength = 0.9f;
    [Range(0f, 1f)] public float washBlotch = 0.8f;
    public Color stone = new Color32(0xE6, 0xDA, 0xBE, 0xFF);
    public Color stoneShade = new Color32(0xA2, 0xA6, 0xA0, 0xFF);
    [Range(0f, 1f)] public float hatching = 0.8f;
    public Color plaster = new Color32(0xF0, 0xE4, 0xC8, 0xFF);
    public Color timber = new Color32(0x5C, 0x3C, 0x26, 0xFF);
    public Color wood = new Color32(0xB4, 0x7C, 0x4A, 0xFF);
    public Color roof = new Color32(0xDB, 0x63, 0x2C, 0xFF);
    public Color roofDark = new Color32(0x9E, 0x3E, 0x1C, 0xFF);
    public Color roofAlt = new Color32(0xBA, 0x50, 0x2E, 0xFF);
    public Color slate = new Color32(0x70, 0x88, 0x90, 0xFF);
    public Color window = new Color32(0x3A, 0x2C, 0x24, 0xFF);
    public Color shutter = new Color32(0xAA, 0x46, 0x2C, 0xFF);
    public Color green = new Color32(0x9C, 0xAC, 0x5C, 0xFF);
    public Color greenDark = new Color32(0x5C, 0x70, 0x44, 0xFF);
    public Color sky = new Color32(0x92, 0xB9, 0xB3, 0xFF);
    public Color flagBlue = new Color32(0xA9, 0xC6, 0xC8, 0xFF);

    [Header("Pen")]
    [Tooltip("Pen width on the legs, in cube units.")]
    [Range(0.004f, 0.06f)] public float penWidth = 0.018f;
    [Tooltip("Hatch lines per cube on the legs.")]
    [Range(4f, 40f)] public float hatchDensity = 18f;
    [Tooltip("Share of the (long enough) legs drawn as stone bridges.")]
    [Range(0f, 1f)] public float bridgeShare = 0.2f;
    [Tooltip("Share of the house legs with a tile roof on top (the rest are battlement walks).")]
    [Range(0f, 1f)] public float roofShare = 0.8f;
    [Tooltip("Rock pen scale (crack / hatch density).")]
    [Range(0.1f, 3f)] public float rockPenScale = 0.8f;

    [Header("Light (camera relative)")]
    public bool litRight = false;
    public float lightUp = 1f, lightSide = 0.75f, lightBack = 0.5f;

    [Header("Renderer")]
    [Tooltip("Renderer index of StorybookCP_Renderer on the pipeline assets. -1 = no pen / paper pass.")]
    public int storyRendererIndex = 4;
    [Tooltip("The camera's own renderer index, put back when the look leaves the screen.")]
    public int baseRendererIndex = 1;

    [Header("Guide line over the next leg (DynamicPathGenerator.guideTrailColor)")]
    public Color guideColor = new Color(1f, 0.85f, 0.35f, 0.9f);

    [Header("Backdrop")]
    public Material backdropMaterial;
    public float yawParallax = 0.003f;
    public float travelParallax = 0.004f;

    [Header("Rocks while in the checkpoint: low, wide tops below the track")]
    public bool restyleRockLayout = true;
    public PixelCheckpointController.RockLayout storyRocks = new PixelCheckpointController.RockLayout
    {
        wallChance = 0.85f, wallsPerCell = 2, wallWidth = new Vector2(20f, 36f), wallTopRange = new Vector2(-24f, -4f),
        wallClearance = 34f, floatingChance = 0f, floatingPerCell = 0, floatingWidth = new Vector2(3f, 6f),
        floatingHeightRange = new Vector2(-20f, -8f), floatingClearance = 6f, debrisPerCell = 0, futureReach = 80f, futureDrop = 24f,
    };
    [Range(0f, 0.9f)] public float storyVeil = 0.1f;
    [Range(0f, 1f)] public float storyHazeMax = 0.4f;
    public float fogEaseSeconds = 1.5f;

    [Header("Props on the rock tops")]
    public Material propMaterial;
    public bool dressRocks = true;
    [Tooltip("The checkpoint's own cliff shape (stepped limestone columns) replaces the scene's rock prefabs while it is on.")]
    public bool ownCliffs = true;
    [Range(0, 8)] public int treesMin = 2, treesMax = 4;
    [Tooltip("Village: houses on every cliff top (min / max).")]
    [Range(0, 16)] public int housesMin = 4, housesMax = 9;
    [Range(0, 4)] public int towersMax = 2;
    public Vector2 treeScale = new Vector2(1.8f, 3.0f);
    public Vector2 houseScale = new Vector2(1.8f, 2.6f);
    public Vector2 towerScale = new Vector2(1.5f, 2.1f);
    [Tooltip("Props sink this far into the rock (world units) so they never hover.")]
    public float sink = 0.25f;

    static readonly int LookId = Shader.PropertyToID("_SbLook");
    static readonly int InkId = Shader.PropertyToID("_SbInk");
    static readonly int PaperId = Shader.PropertyToID("_SbPaper");
    static readonly int StoneId = Shader.PropertyToID("_SbStone");
    static readonly int StoneShadeId = Shader.PropertyToID("_SbStoneShade");
    static readonly int PlasterId = Shader.PropertyToID("_SbPlaster");
    static readonly int TimberId = Shader.PropertyToID("_SbTimber");
    static readonly int WoodId = Shader.PropertyToID("_SbWood");
    static readonly int RoofId = Shader.PropertyToID("_SbRoof");
    static readonly int RoofDarkId = Shader.PropertyToID("_SbRoofDark");
    static readonly int RoofAltId = Shader.PropertyToID("_SbRoofAlt");
    static readonly int SlateId = Shader.PropertyToID("_SbSlate");
    static readonly int WindowId = Shader.PropertyToID("_SbWindow");
    static readonly int ShutterId = Shader.PropertyToID("_SbShutter");
    static readonly int GreenId = Shader.PropertyToID("_SbGreen");
    static readonly int GreenDarkId = Shader.PropertyToID("_SbGreenDark");
    static readonly int SkyId = Shader.PropertyToID("_SbSky");
    static readonly int ParamsId = Shader.PropertyToID("_SbParams");
    static readonly int LightDirId = Shader.PropertyToID("_SbLightDir");
    static readonly int BackdropId = Shader.PropertyToID("_SbBackdrop");

    PixelCheckpointController.RockLayout originalRocks;
    bool rocksStory, sbOnScreen, rendererSb;
    Camera cam;
    UniversalAdditionalCameraData camData;
    GameObject backdrop;
    MeshRenderer backdropRenderer;
    ChasmFog chasm;
    float sceneVeil, sceneHazeMax, fogBlend = -1f;
    bool fogCaptured;
    Color sceneGuideColor;
    bool guideCaptured, guideSb;

    Mesh[] treeMeshes, houseMeshes, towerMeshes;
    GameObject[] cliffTemplates, sceneWallPrefabs;
    GameObject cliffRoot;
    bool cliffsOn;
    readonly Dictionary<string, List<Vector4>> cliffColumns = new Dictionary<string, List<Vector4>>();   // per template: x, z, radius, top
    readonly List<Vector4> columns = new List<Vector4>();
    readonly List<Mesh> madeMeshes = new List<Mesh>();
    class TopMap { public float[] h; public float minX, minZ, sizeX, sizeZ; public int n; }
    readonly Dictionary<string, TopMap> topMaps = new Dictionary<string, TopMap>();
    readonly List<Vector3> scratchVerts = new List<Vector3>();
    readonly List<Vector3> candidates = new List<Vector3>();
    readonly List<Vector3> placed = new List<Vector3>();

    public bool StoryOnScreen => sbOnScreen;
    public bool StoryRockLayout => rocksStory;

    void Awake()
    {
        if (director == null) director = FindFirstObjectByType<LookTransitionDirector>();
        if (background == null) background = FindFirstObjectByType<PathBackgroundGenerator>();
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (runner == null) runner = FindFirstObjectByType<CubeRailRunner>();
        if (rig == null) rig = FindFirstObjectByType<CameraParentRotator>();
        if (toon == null) toon = FindFirstObjectByType<ToonCheckpointController>();
        if (pixel == null) pixel = FindFirstObjectByType<PixelCheckpointController>();
        PushPalette();
        Shader.SetGlobalVector(LookId, Vector4.zero);
    }

    void Start()
    {
        cam = rig != null && rig.boomCamera != null ? rig.boomCamera : Camera.main;
        if (cam != null) camData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (background != null) originalRocks = PixelCheckpointController.RockLayout.From(background);
        BuildBackdrop();
        BuildPropMeshes();
        BuildCliffs();
    }

    void OnDisable()
    {
        Shader.SetGlobalVector(LookId, Vector4.zero);
        if (camData != null && rendererSb) camData.SetRenderer(baseRendererIndex);
        rendererSb = false;
        if (background != null) background.RockTopBuilt -= DressRock;
        if (rocksStory && background != null && originalRocks != null) originalRocks.ApplyTo(background);
        if (cliffsOn && background != null) background.wallPrefabs = sceneWallPrefabs;
        cliffsOn = false;
        rocksStory = false;
        if (guideSb && pathGen != null) pathGen.guideTrailColor = sceneGuideColor;
        guideSb = false;
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

    bool IsStory(AtmosphereLook l) =>
        l != null && storyLook != null && (l == storyLook || (!string.IsNullOrEmpty(storyLook.sourceScene) && l.sourceScene == storyLook.sourceScene));

    void LateUpdate()
    {
        AtmosphereLook now = director != null ? director.CurrentLook : null;
        AtmosphereLook next = director != null ? director.NextLook : null;
        bool a = IsStory(now), b = IsStory(next);
        sbOnScreen = alwaysStory || a || b;
        bool zone = alwaysStory || (next != null ? b : a);
        Shader.SetGlobalVector(LookId, new Vector4(a ? 1f : 0f, b ? 1f : 0f, alwaysStory ? 1f : 0f, 0f));

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
        Shader.SetGlobalVector(InkId, Lin(ink, inkStrength));
        Shader.SetGlobalVector(PaperId, Lin(paper, washBlotch));
        Shader.SetGlobalVector(StoneId, Lin(stone, hatching));
        Shader.SetGlobalVector(StoneShadeId, Lin(stoneShade, 1f));
        Shader.SetGlobalVector(PlasterId, Lin(plaster, 1f));
        Shader.SetGlobalVector(TimberId, Lin(timber, 1f));
        Shader.SetGlobalVector(WoodId, Lin(wood, 1f));
        Shader.SetGlobalVector(RoofId, Lin(roof, 1f));
        Shader.SetGlobalVector(RoofDarkId, Lin(roofDark, 1f));
        Shader.SetGlobalVector(RoofAltId, Lin(roofAlt, 1f));
        Shader.SetGlobalVector(SlateId, Lin(slate, 1f));
        Shader.SetGlobalVector(WindowId, Lin(window, 1f));
        Shader.SetGlobalVector(ShutterId, Lin(shutter, 1f));
        Shader.SetGlobalVector(GreenId, Lin(green, 1f));
        Shader.SetGlobalVector(GreenDarkId, Lin(greenDark, 1f));
        Shader.SetGlobalVector(SkyId, Lin(sky, 1f));
        Shader.SetGlobalVector(ParamsId, new Vector4(penWidth, hatchDensity, bridgeShare, roofShare));
    }

    Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-4f ? Vector3.right : v.normalized;
    }

    void PushLight()
    {
        Vector3 r = cam != null ? Flat(cam.transform.right) : Vector3.right;
        Vector3 f = cam != null ? Flat(cam.transform.forward) : Vector3.forward;
        Vector3 l = (Vector3.up * lightUp + r * (litRight ? lightSide : -lightSide) - f * lightBack).normalized;
        Shader.SetGlobalVector(LightDirId, new Vector4(l.x, l.y, l.z, rockPenScale));
    }

    // ------------------------------------------------------------------ renderer + guide

    void UpdateRenderer()
    {
        if (camData == null || storyRendererIndex < 0) return;
        if (sbOnScreen)
        {
            camData.SetRenderer(storyRendererIndex);   // every frame: the pixel / toon controllers run earlier
            rendererSb = true;
        }
        else if (rendererSb)
        {
            int idx = baseRendererIndex;
            if (pixel != null && pixel.isActiveAndEnabled && pixel.PixelOnScreen) idx = pixel.pixelRendererIndex;
            else if (toon != null && toon.isActiveAndEnabled && toon.ToonOnScreen) idx = toon.toonRendererIndex;
            camData.SetRenderer(idx);
            rendererSb = false;
        }
    }

    void UpdateGuide()
    {
        if (pathGen == null) return;
        if (!guideCaptured) { sceneGuideColor = pathGen.guideTrailColor; guideCaptured = true; }
        if (sbOnScreen)
        {
            pathGen.guideTrailColor = guideColor;
            guideSb = true;
        }
        else if (guideSb)
        {
            if (pixel != null && pixel.isActiveAndEnabled && pixel.PixelOnScreen) pathGen.guideTrailColor = pixel.guideColor;
            else if (toon != null && toon.isActiveAndEnabled && toon.ToonOnScreen) pathGen.guideTrailColor = toon.guideColor;
            else pathGen.guideTrailColor = sceneGuideColor;
            guideSb = false;
        }
    }

    // ------------------------------------------------------------------ rocks + fog

    void UpdateRocks(bool zone)
    {
        if (background == null || originalRocks == null) return;
        if (zone == rocksStory) return;
        rocksStory = zone;
        background.RockTopBuilt -= DressRock;
        if (zone && dressRocks) background.RockTopBuilt += DressRock;
        if (restyleRockLayout)
        {
            // Leaving: the scene's layout, unless the pixel checkpoint (earlier this frame) already put its own.
            if (zone) storyRocks.ApplyTo(background);
            else if (pixel == null || !pixel.PixelRockLayout) originalRocks.ApplyTo(background);
        }
        // The checkpoint's own cliffs instead of the scene's rock prefabs (the shared rocks every other look uses).
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
        float target = sbOnScreen ? 1f : 0f;
        if (fogBlend == target && !sbOnScreen) return;
        fogBlend = fogBlend < 0f ? target : Mathf.MoveTowards(fogBlend, target, Time.deltaTime / Mathf.Max(0.01f, fogEaseSeconds));
        float k = fogBlend * fogBlend * (3f - 2f * fogBlend);
        chasm.veil = Mathf.Lerp(sceneVeil, storyVeil, k);
        chasm.hazeMax = Mathf.Lerp(sceneHazeMax, storyHazeMax, k);
    }

    // ------------------------------------------------------------------ backdrop

    void BuildBackdrop()
    {
        if (backdropMaterial == null || cam == null) return;
        backdrop = new GameObject("StorybookBackdrop");
        backdrop.transform.SetParent(cam.transform, false);
        backdrop.transform.localPosition = new Vector3(0f, 0f, Mathf.Min(cam.farClipPlane * 0.5f, 200f));
        var m = new Mesh { name = "StorybookBackdropQuad" };
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
        backdropRenderer.enabled = sbOnScreen;
        if (!sbOnScreen) return;
        float yaw = cam.transform.eulerAngles.y;
        Vector3 cp = cam.transform.position;
        float tx = Mathf.Repeat(Vector3.Dot(cp, Flat(cam.transform.right)) * travelParallax, 1000f);
        float ty = Mathf.Repeat(cp.y * travelParallax, 1000f);
        Shader.SetGlobalVector(BackdropId, new Vector4(-yaw * yawParallax, Time.time, tx, ty));
    }

    // ------------------------------------------------------------------ props on the rock tops

    /// <summary>Called by PathBackgroundGenerator for every rock top it places while the checkpoint is on.</summary>
    void DressRock(Transform holder, bool wall)
    {
        if (propMaterial == null || treeMeshes == null || holder == null || holder.childCount == 0) return;
        Transform visual = holder.GetChild(0);
        Vector3 hp = holder.position;
        var rng = new System.Random(Mathf.RoundToInt(hp.x * 7.31f) * 73856093 ^ Mathf.RoundToInt(hp.z * 3.17f) * 19349663 ^ Mathf.RoundToInt(hp.y) * 83492791);
        float F(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        if (!TopCandidates(visual)) return;
        placed.Clear();

        // The big piece first (a tower or a house) on the most central plateau point, then the trees round it.
        if (wall)
        {
            // A village: towers first (they need room), then houses packed on every flat step.
            int towers = rng.Next(0, towersMax + 1);
            for (int i = 0; i < towers; i++) Place(holder, towerMeshes[rng.Next(towerMeshes.Length)], F(towerScale.x, towerScale.y), 1.3f, rng);
            int houses = rng.Next(housesMin, housesMax + 1);
            for (int i = 0; i < houses; i++) Place(holder, houseMeshes[rng.Next(houseMeshes.Length)], F(houseScale.x, houseScale.y), 1.25f, rng);
        }
        int trees = wall ? rng.Next(treesMin, treesMax + 1) : rng.Next(0, 2);
        for (int i = 0; i < trees; i++) Place(holder, treeMeshes[rng.Next(treeMeshes.Length)], F(treeScale.x, treeScale.y), 0.9f, rng);
    }

    void Place(Transform holder, Mesh mesh, float scale, float footprint, System.Random rng)
    {
        float gap = footprint * scale;
        for (int tries = 0; tries < 14 && candidates.Count > 0; tries++)
        {
            Vector3 p = candidates[rng.Next(candidates.Count)];
            bool ok = true;
            foreach (Vector3 q in placed)
                if ((new Vector2(p.x - q.x, p.z - q.z)).sqrMagnitude < gap * gap) { ok = false; break; }
            if (!ok) continue;
            placed.Add(p);
            var go = new GameObject(mesh.name);
            go.transform.SetParent(holder, true);
            go.transform.SetPositionAndRotation(p + Vector3.down * sink, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            go.transform.localScale = Vector3.one * scale;   // the holder is at full size when its rock is placed
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = propMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return;
        }
    }

    // Plateau points of the rock's top (world space) into "candidates": the highest band of its top surface.
    bool TopCandidates(Transform visual)
    {
        candidates.Clear();
        // The checkpoint's own cliffs: the column tops are known exactly - a few spots on every top no taller column covers.
        if (cliffColumns.TryGetValue(visual.name, out List<Vector4> cols))
        {
            foreach (Vector4 c in cols)
            {
                for (int s = 0; s < 7; s++)
                {
                    float a = s * 0.898f, rr = s == 0 ? 0f : c.z * 0.5f;
                    float x = c.x + Mathf.Cos(a) * rr, z = c.y + Mathf.Sin(a) * rr;
                    bool covered = false;
                    foreach (Vector4 o in cols)
                        if (o.w > c.w + 1e-3f && (new Vector2(x - o.x, z - o.y)).sqrMagnitude < o.z * o.z * 1.3f) { covered = true; break; }
                    if (!covered) candidates.Add(visual.TransformPoint(new Vector3(x, c.w, z)));
                }
            }
            return candidates.Count > 0;
        }
        TopMap map = GetTopMap(visual);
        if (map == null)
        {
            var rend = visual.GetComponentInChildren<Renderer>();
            if (rend == null) return false;
            Bounds b = rend.bounds;
            candidates.Add(new Vector3(b.center.x, b.max.y - 0.4f, b.center.z));
            return true;
        }
        // Every flat step of the top (all four neighbours at nearly the same height), not only the highest: a village
        // spreads over the terraces. Steps far below the summit are left bare (they sink into the fog).
        float best = float.NegativeInfinity;
        int n = map.n;
        float sy = Mathf.Max(visual.lossyScale.y, 1e-3f);
        for (int z = 1; z < n - 1; z++)
            for (int x = 1; x < n - 1; x++)
            {
                float h = map.h[z * n + x];
                if (float.IsNegativeInfinity(h)) continue;
                float dmax = 0f;
                dmax = Mathf.Max(dmax, Mathf.Abs(map.h[z * n + x - 1] - h));
                dmax = Mathf.Max(dmax, Mathf.Abs(map.h[z * n + x + 1] - h));
                dmax = Mathf.Max(dmax, Mathf.Abs(map.h[(z - 1) * n + x] - h));
                dmax = Mathf.Max(dmax, Mathf.Abs(map.h[(z + 1) * n + x] - h));
                if (dmax * sy > 0.6f) continue;   // a slope or an edge (an empty neighbour reads as infinite)
                Vector3 w = visual.TransformPoint(new Vector3(map.minX + (x + 0.5f) * map.sizeX / n, h, map.minZ + (z + 0.5f) * map.sizeZ / n));
                best = Mathf.Max(best, w.y);
                candidates.Add(w);
            }
        for (int i = candidates.Count - 1; i >= 0; i--)
            if (candidates[i].y < best - 9f) candidates.RemoveAt(i);
        return candidates.Count > 0;
    }

    TopMap GetTopMap(Transform visual)
    {
        string key = visual.name;
        if (topMaps.TryGetValue(key, out TopMap cached)) return cached;
        TopMap map = null;
        var filters = visual.GetComponentsInChildren<MeshFilter>();
        const int n = 16;
        float minX = float.PositiveInfinity, minZ = float.PositiveInfinity, maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
        Matrix4x4 toVis = visual.worldToLocalMatrix;
        bool any = false;
        foreach (MeshFilter mf in filters)
        {
            Mesh m = mf.sharedMesh;
            if (m == null || !m.isReadable) continue;
            m.GetVertices(scratchVerts);
            Matrix4x4 mtx = toVis * mf.transform.localToWorldMatrix;
            foreach (Vector3 v in scratchVerts)
            {
                Vector3 p = mtx.MultiplyPoint3x4(v);
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                any = true;
            }
        }
        if (any)
        {
            map = new TopMap { n = n, minX = minX, minZ = minZ, sizeX = Mathf.Max(maxX - minX, 1e-3f), sizeZ = Mathf.Max(maxZ - minZ, 1e-3f), h = new float[n * n] };
            for (int i = 0; i < map.h.Length; i++) map.h[i] = float.NegativeInfinity;
            foreach (MeshFilter mf in filters)
            {
                Mesh m = mf.sharedMesh;
                if (m == null || !m.isReadable) continue;
                m.GetVertices(scratchVerts);
                Matrix4x4 mtx = toVis * mf.transform.localToWorldMatrix;
                foreach (Vector3 v in scratchVerts)
                {
                    Vector3 p = mtx.MultiplyPoint3x4(v);
                    int x = Mathf.Clamp((int)((p.x - minX) / map.sizeX * n), 0, n - 1);
                    int z = Mathf.Clamp((int)((p.z - minZ) / map.sizeZ * n), 0, n - 1);
                    int i = z * n + x;
                    if (p.y > map.h[i]) map.h[i] = p.y;
                }
            }
        }
        topMaps[key] = map;
        return map;
    }

    // ------------------------------------------------------------------ prop meshes (built once, shared)

    class Kit
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> nr = new List<Vector3>();
        readonly List<Color> c = new List<Color>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();

        int Vert(Vector3 p, Vector3 n, Color col, float kind, float wave)
        {
            col.a = wave;
            v.Add(p); nr.Add(n); c.Add(col); uv.Add(new Vector2(kind, 0f));
            return v.Count - 1;
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 d, Color col, float kind)
        {
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            int i = Vert(a, n, col, kind, 0f), j = Vert(b, n, col, kind, 0f), k = Vert(d, n, col, kind, 0f);
            t.Add(i); t.Add(j); t.Add(k);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color col, float kind)
        {
            Tri(a, b, d, col, kind); Tri(a, d, e, col, kind);
        }

        // Axis box, flat faces (a, b corners: min / max).
        public void Box(Vector3 min, Vector3 max, Color col, float kind)
        {
            Vector3 p000 = min, p100 = new Vector3(max.x, min.y, min.z), p010 = new Vector3(min.x, max.y, min.z), p110 = new Vector3(max.x, max.y, min.z);
            Vector3 p001 = new Vector3(min.x, min.y, max.z), p101 = new Vector3(max.x, min.y, max.z), p011 = new Vector3(min.x, max.y, max.z), p111 = max;
            Quad(p000, p010, p110, p100, col, kind);   // -z
            Quad(p101, p111, p011, p001, col, kind);   // +z
            Quad(p001, p011, p010, p000, col, kind);   // -x
            Quad(p100, p110, p111, p101, col, kind);   // +x
            Quad(p010, p011, p111, p110, col, kind);   // +y
            Quad(p000, p100, p101, p001, col, kind);   // -y
        }

        public void Cylinder(Vector3 b, float r, float h, int seg, Color col, float kind)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r, d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r;
                Quad(b + d0, b + d0 + Vector3.up * h, b + d1 + Vector3.up * h, b + d1, col, kind);
                Tri(b + Vector3.up * h, b + d1 + Vector3.up * h, b + d0 + Vector3.up * h, col, kind);
            }
        }

        public void Cone(Vector3 b, float r, float h, int seg, Color col, float kind)
        {
            Vector3 tip = b + Vector3.up * h;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r, d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r;
                Tri(b + d0, tip, b + d1, col, kind);
                Tri(b + d1, b, b + d0, col, kind);
            }
        }

        // Hip roof over a w (x) by d (z) footprint, eaves at y0.
        public void HipRoof(Vector3 centre, float w, float d, float h, Color col)
        {
            float hx = w * 0.5f, hz = d * 0.5f, ridge = Mathf.Max(hx - hz, 0.05f);
            Vector3 a = centre + new Vector3(-hx, 0, -hz), b = centre + new Vector3(hx, 0, -hz), e = centre + new Vector3(hx, 0, hz), f = centre + new Vector3(-hx, 0, hz);
            Vector3 r0 = centre + new Vector3(-ridge, h, 0), r1 = centre + new Vector3(ridge, h, 0);
            Quad(a, r0, r1, b, col, 1f);
            Quad(e, r1, r0, f, col, 1f);
            Tri(b, r1, e, col, 1f);
            Tri(f, r0, a, col, 1f);
            Quad(a, b, e, f, col * 0.6f, 1f);
        }

        // Gable roof over a w (x) by d (z) footprint: the ridge runs the full width, triangular gable ends.
        public void GableRoof(Vector3 centre, float w, float d, float h, Color col)
        {
            float hx = w * 0.5f, hz = d * 0.5f;
            Vector3 a = centre + new Vector3(-hx, 0, -hz), b = centre + new Vector3(hx, 0, -hz), e = centre + new Vector3(hx, 0, hz), f = centre + new Vector3(-hx, 0, hz);
            Vector3 r0 = centre + new Vector3(-hx, h, 0), r1 = centre + new Vector3(hx, h, 0);
            Quad(a, r0, r1, b, col, 1f);
            Quad(e, r1, r0, f, col, 1f);
            Color gableCol = col * 0.8f;
            Tri(b, r1, e, gableCol, 0f);
            Tri(f, r0, a, gableCol, 0f);
            Quad(a, b, e, f, col * 0.6f, 1f);
        }

        // A smooth low-poly blob (foliage).
        public void Blob(Vector3 centre, float r, float squash, Color col, System.Random rng)
        {
            const int seg = 8, rings = 5;
            int start = v.Count;
            for (int y = 0; y <= rings; y++)
            {
                float phi = Mathf.PI * y / rings;
                for (int x = 0; x <= seg; x++)
                {
                    float th = Mathf.PI * 2f * x / seg;
                    Vector3 n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    float j = (x == seg || y == 0 || y == rings) ? 1f : 0.88f + (float)rng.NextDouble() * 0.24f;
                    Vector3 p = centre + Vector3.Scale(n * r * j, new Vector3(1f, squash, 1f));
                    Vert(p, n, col, 2f, 0f);
                }
            }
            for (int y = 0; y < rings; y++)
                for (int x = 0; x < seg; x++)
                {
                    int i0 = start + y * (seg + 1) + x, i1 = i0 + 1, i2 = i0 + seg + 1, i3 = i2 + 1;
                    t.Add(i0); t.Add(i1); t.Add(i2);
                    t.Add(i1); t.Add(i3); t.Add(i2);
                }
        }

        // A pole with a cloth flag in the object's XY plane; the cloth's vertex alpha is its wave weight.
        public void Flag(Vector3 b, float poleH, float w, float h, Color pole, Color cloth)
        {
            Box(b + new Vector3(-0.025f, 0f, -0.025f), b + new Vector3(0.025f, poleH, 0.025f), pole, 3f);
            const int cols = 4;
            Vector3 n = Vector3.forward;
            int start = v.Count;
            for (int x = 0; x <= cols; x++)
            {
                float k = (float)x / cols;
                float tail = k * 0.25f * h;   // the swallow-tail droop
                Vert(b + new Vector3(k * w, poleH - tail * 0.3f, 0f), n, cloth, 3f, k);
                Vert(b + new Vector3(k * w, poleH - h + tail * 0.3f, 0f), n, cloth, 3f, k);
            }
            for (int x = 0; x < cols; x++)
            {
                int i0 = start + x * 2;
                t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1);
                t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0 + 3);
            }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(nr); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    Mesh Keep(Mesh m) { madeMeshes.Add(m); return m; }

    // The checkpoint's cliffs: chunky limestone mesas of stepped columns (the reference's cliff), unit footprint, base at
    // y = 0. Templates sit under an inactive root (never drawn); the background generator instantiates them like prefabs,
    // with the scene rock's own material so the chasm fog / storybook rock shader applies unchanged.
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
        cliffRoot = new GameObject("StorybookCliffTemplates");
        cliffRoot.SetActive(false);
        cliffRoot.transform.SetParent(transform, false);
        var rng = new System.Random(11);
        cliffTemplates = new GameObject[4];
        for (int k = 0; k < cliffTemplates.Length; k++)
        {
            var kit = new Kit();
            int cols = 7 + k * 2;
            for (int i = 0; i < cols; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float d = i == 0 ? 0f : Mathf.Sqrt((float)rng.NextDouble()) * 0.3f;
                Vector2 c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                float r = Mathf.Lerp(0.16f, 0.3f, (float)rng.NextDouble());
                // Taller toward the middle, in steps: terraces for the village.
                float h = Mathf.Lerp(0.55f, 1f, 1f - d / 0.35f) * (0.85f + 0.15f * (float)rng.NextDouble());
                h = Mathf.Round(h * 6f) / 6f;
                Prism(kit, c, r, h, 5 + rng.Next(3), rng);
                columns.Add(new Vector4(c.x, c.y, r, h));
            }
            cliffColumns["SbCliff" + k + "(Clone)"] = new List<Vector4>(columns);
            columns.Clear();
            var go = new GameObject("SbCliff" + k);
            go.transform.SetParent(cliffRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Keep(kit.Build("SbCliff" + k));
            go.AddComponent<MeshRenderer>().sharedMaterial = rockMat;
            cliffTemplates[k] = go;
        }
    }

    // One column of the cliff: an irregular prism, flat top, slightly wider at the foot.
    static void Prism(Kit kit, Vector2 c, float r, float h, int sides, System.Random rng)
    {
        float phase = (float)rng.NextDouble() * 6.283f;
        var ring = new Vector2[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = phase + i * Mathf.PI * 2f / sides;
            float rr = r * (0.8f + 0.4f * (float)rng.NextDouble());
            ring[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
        }
        Color col = Color.white;
        Vector3 top = new Vector3(c.x, h, c.y);
        for (int i = 0; i < sides; i++)
        {
            Vector2 p0 = ring[i], p1 = ring[(i + 1) % sides];
            Vector2 f0 = c + (p0 - c) * 1.12f, f1 = c + (p1 - c) * 1.12f;
            Vector3 a0 = new Vector3(f0.x, 0f, f0.y), a1 = new Vector3(f1.x, 0f, f1.y);
            Vector3 b0 = new Vector3(p0.x, h, p0.y), b1 = new Vector3(p1.x, h, p1.y);
            kit.Quad(a0, b0, b1, a1, col, 0f);
            kit.Tri(top, b1, b0, col, 0f);
        }
    }

    void BuildPropMeshes()
    {
        if (treeMeshes != null) return;
        var rng = new System.Random(5);
        Color G = green.linear, GD = greenDark.linear, T = timber.linear, S = stone.linear, P = plaster.linear, W = wood.linear;
        Color R = roof.linear, RA = roofAlt.linear, RD = roofDark.linear, WIN = window.linear, SH = shutter.linear;
        Color FB = flagBlue.linear, CR = paper.linear;

        treeMeshes = new Mesh[3];
        for (int k = 0; k < 3; k++)
        {
            var kit = new Kit();
            kit.Cylinder(Vector3.zero, 0.09f, 0.8f, 5, T, 3f);
            int blobs = 3 + k;
            for (int i = 0; i < blobs; i++)
            {
                float a = i * 2.4f + k;
                float rr = i == 0 ? 0f : 0.35f;
                Vector3 c = new Vector3(Mathf.Cos(a) * rr, 0.95f + i * 0.13f + (i == 0 ? 0.2f : 0f), Mathf.Sin(a) * rr);
                Color col = Color.Lerp(G, GD, (float)rng.NextDouble() * 0.5f + (i % 2) * 0.15f);
                kit.Blob(c, i == 0 ? 0.6f : 0.42f + (float)rng.NextDouble() * 0.15f, 0.8f, col, rng);
            }
            treeMeshes[k] = Keep(kit.Build("SbTree" + k));
        }

        // Six village houses: jettied timber, stone cottage, plank house, tall stone townhouse, long barn, plaster cottage.
        houseMeshes = new Mesh[6];
        Color[] walls = { P, S, W, S, W, P };
        Color[] roofs = { R, RA, R, R, RA, slate.linear };
        float[] widths = { 1.5f, 1.3f, 1.5f, 1.0f, 2.2f, 1.2f }, depths = { 1.1f, 1.1f, 1.1f, 1.0f, 1.1f, 1.0f }, heights = { 1.0f, 0.9f, 1.0f, 1.9f, 0.9f, 0.85f };
        bool[] gable = { false, true, false, true, true, false };
        for (int k = 0; k < 6; k++)
        {
            var kit = new Kit();
            float w = widths[k], d = depths[k], h = heights[k];
            kit.Box(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h, d / 2), walls[k], 0f);
            float top = h;
            if (k == 0)
            {
                // Jettied timber upper floor.
                kit.Box(new Vector3(-w / 2 - 0.06f, h, -d / 2 - 0.06f), new Vector3(w / 2 + 0.06f, h + 0.7f, d / 2 + 0.06f), P, 0f);
                kit.Box(new Vector3(-w / 2 - 0.07f, h - 0.03f, -d / 2 - 0.07f), new Vector3(w / 2 + 0.07f, h + 0.04f, d / 2 + 0.07f), T, 3f);
                top = h + 0.7f;
            }
            if (gable[k]) kit.GableRoof(new Vector3(0, top, 0), w + 0.3f, d + 0.3f, k == 3 ? 1.0f : 0.7f, roofs[k]);
            else kit.HipRoof(new Vector3(0, top, 0), w + 0.3f, d + 0.3f, 0.75f, roofs[k]);
            // Windows (dark panes in red-brown frames) on both long sides, a door.
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * (d / 2 + (k == 0 ? 0.07f : 0.01f));
                float wy0 = k == 0 ? h + 0.25f : k == 3 ? 0.35f : h * 0.45f;
                for (int i = -1; i <= 1; i += 2)
                for (int fl = 0; fl < (k == 3 ? 2 : 1); fl++)
                {
                    float x = i * Mathf.Min(0.38f, w * 0.26f) * (k == 4 ? 1.8f : 1f);
                    float wy = wy0 + fl * 0.8f;
                    kit.Box(new Vector3(x - 0.13f, wy - 0.02f, z - 0.02f), new Vector3(x + 0.13f, wy + 0.3f, z + 0.02f), SH, 3f);
                    kit.Box(new Vector3(x - 0.09f, wy + 0.02f, z - 0.03f), new Vector3(x + 0.09f, wy + 0.26f, z + 0.03f), WIN, 3f);
                }
            }
            kit.Box(new Vector3(-0.15f, 0f, -d / 2 - 0.03f), new Vector3(0.15f, 0.55f, -d / 2 + 0.02f), T, 3f);
            kit.Box(new Vector3(0.35f, top + 0.2f, 0.1f), new Vector3(0.55f, top + 0.95f, 0.3f), S, 0f);   // chimney
            if (k == 0 || k == 3) kit.Flag(new Vector3(-0.35f, top + 0.4f, 0f), 0.9f, 0.45f, 0.26f, T, k == 0 ? FB : SH);
            houseMeshes[k] = Keep(kit.Build("SbHouse" + k));
        }

        towerMeshes = new Mesh[2];
        for (int k = 0; k < 2; k++)
        {
            var kit = new Kit();
            float r = k == 0 ? 0.55f : 0.45f, h = k == 0 ? 2.8f : 2.2f;
            kit.Cylinder(Vector3.zero, r, h, 10, S, 0f);
            // Balcony ring / crenellated cap.
            kit.Cylinder(new Vector3(0, h, 0), r + 0.12f, 0.22f, 10, k == 0 ? W : S, k == 0 ? 3f : 0f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + 0.3f;
                Vector3 o = new Vector3(Mathf.Cos(a) * (r + 0.005f), h * (0.35f + 0.15f * i), Mathf.Sin(a) * (r + 0.005f));
                Vector3 tng = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.06f;
                kit.Quad(o - tng, o - tng + Vector3.up * 0.32f, o + tng + Vector3.up * 0.32f, o + tng, WIN, 3f);
            }
            kit.Cone(new Vector3(0, h + 0.22f, 0), r + 0.25f, k == 0 ? 1.5f : 1.2f, 10, k == 0 ? R : RA, 1f);
            kit.Flag(new Vector3(0, h + 0.22f + (k == 0 ? 1.45f : 1.15f), 0), 0.7f, 0.55f, 0.3f, T, k == 0 ? FB : CR);
            towerMeshes[k] = Keep(kit.Build("SbTower" + k));
        }
    }
}
