using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// MainUi: the live worlds behind the checkpoint bands. Each checkpoint shows the first legs a real run lays there
/// (MenuSliceData, baked from 3.6 with the real DynamicPathGenerator and a fixed seed; checkpoints 2+ start at the exit
/// tower, built by the real PortalDoor), drawn with the gameplay block material on the LookTransition wipe shader and
/// tinted like PathDepthShading tints the track. No runner: each band's camera is the gameplay camera (E framing, the baked
/// pitch / yaw / orthographic size) pulled back pullBack x, gliding along the legs as it would follow an invisible runner,
/// fading round at the end of its glide. The legs rise into place once, the way a fresh path rises in gameplay.
///
/// Fog, sky, sun, ambient and post are global, so each camera gets its checkpoint's values in
/// RenderPipelineManager.beginCameraRendering and the menu's own values go back in endCameraRendering. At most
/// maxCamerasPerFrame band cameras draw in one frame (the stalest first). Nothing is allocated per frame.
/// </summary>
[DefaultExecutionOrder(-100)]
public class MenuDioramaRig : MonoBehaviour
{
    [System.Serializable]
    public struct FogSettings
    {
        public float fogStart, fogSolid, billowHeight, billowSize, billowSpeed;
        public Vector2 windDirection;
        public float hazeStart, hazeEnd, hazeMax, screenFogSolid, screenFogTop, screenFogStrength, veil;
        public float houseFogShare, houseFogAtHouse, houseFogDepth;
        public static FogSettings Default => new FogSettings
        {
            fogStart = 10, fogSolid = 170, billowHeight = 10, billowSize = 26, billowSpeed = 1.2f, windDirection = new Vector2(1, 0.4f),
            hazeStart = 6, hazeEnd = 150, hazeMax = 0.85f, screenFogSolid = 0.06f, screenFogTop = 0.28f, screenFogStrength = 0.85f, veil = 0.35f,
            houseFogShare = 0.5f, houseFogAtHouse = 0.35f, houseFogDepth = 30,
        };
    }

    [Header("Content")]
    public CheckpointSet checkpoints;
    [Tooltip("The first legs of every checkpoint, baked from 3.6 (MenuSliceBaker).")]
    public MenuSliceData slices;
    [Tooltip("Gameplay path-cube material (FlatFaceBlock family); drawn through a runtime copy on the wipe shader.")]
    public Material blockMaterial;
    public Material playerMaterial;
    public Shader blockWipeShader;
    [Tooltip("Gameplay sky material (RadialSky) and its LookTransition wipe shader: a sky quad behind every band camera.")]
    public Material skyMaterial;
    public Shader skyWipeShader;
    [Tooltip("ChasmFog backdrop shader (the LookTransition wipe copy): the coloured background behind every band.")]
    public Shader backdropShader;
    [Tooltip("PortalZoneController.voidMaterial: the exit tower's doorway.")]
    public Material voidMaterial;
    public GameObject coinPrefab;     // unused now (no runner, no pickups); kept so older data still loads
    public Material coinMaterial;
    public Material trailMaterial;
    public Material particleMaterial;
    public FogSettings fog = FogSettings.Default;

    [Header("Scene")]
    public Light sun;
    public Volume volume;

    [Header("Camera")]
    [Tooltip("How much wider than gameplay each band frames its legs (1.3-1.6 reads as a scene with sky and fog depth).")]
    [Range(1f, 2.5f)] public float pullBack = 1.45f;
    [Tooltip("Glide speed along the legs, world units per second (each band varies it a little).")]
    public float glideSpeed = 4f;
    [Tooltip("Longest stretch a band glides before it fades round to the start (exit runways are long).")]
    public float glideLength = 9999f;
    [Tooltip("Gameplay reference height the baked orthographic size covers.")]
    public float gameplayReferenceHeight = 1920f;

    [Header("Rendering")]
    [Tooltip("Band render target size at the 1080 x 1920 reference (the band rect including its slanted ends). Set by the menu.")]
    public Vector2 bandReferencePixels = new Vector2(1080, 386);
    [Range(0.25f, 1f)] public float renderScale = 0.5f;
    public bool postProcessing = true;
    [Range(1, 8)] public int maxCamerasPerFrame = 4;
    [Tooltip("World distance between dioramas, so no camera ever sees another.")]
    public float spacing = 2000f;
    [Tooltip("Seconds the legs take to rise into place when first shown (the gameplay rise).")]
    public float riseSeconds = 0.8f;
    public float riseDrop = 5f;

    // ---- shader ids ----
    static readonly int LTAId = Shader.PropertyToID("_LTA"), LTBId = Shader.PropertyToID("_LTB");
    static readonly int SkyAId = Shader.PropertyToID("_LTSkyA"), SkyBId = Shader.PropertyToID("_LTSkyB");
    static readonly int WipeId = Shader.PropertyToID("_LTWipe"), RoleId = Shader.PropertyToID("_LTRole");
    static readonly int TopId = Shader.PropertyToID("_ChasmFogTop"), BottomId = Shader.PropertyToID("_ChasmFogBottom");
    static readonly int GlowId = Shader.PropertyToID("_ChasmFogGlow"), GlowShapeId = Shader.PropertyToID("_ChasmFogGlowShape");
    static readonly int HeightId = Shader.PropertyToID("_ChasmFogHeight"), DepthId = Shader.PropertyToID("_ChasmFogDepth");
    static readonly int WindId = Shader.PropertyToID("_ChasmFogWind"), ScreenId = Shader.PropertyToID("_ChasmFogScreen");
    static readonly int ViewId = Shader.PropertyToID("_ChasmFogView"), ViewPosId = Shader.PropertyToID("_ChasmFogViewPos");
    static readonly int HouseId = Shader.PropertyToID("_ChasmFogHouse");
    static readonly int FogRefId = Shader.PropertyToID("_PropFogRefY");
    static readonly int RunnerPId = Shader.PropertyToID("_PropRunnerP"), RunnerNId = Shader.PropertyToID("_PropRunnerN");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");
    static readonly string[] DeltaNames = { "_LTChasmTopD", "_LTChasmBottomD", "_LTChasmGlowD", "_LTChasmShapeD", "_LTSunD", "_LTAmbD" };
    static readonly float[] GradientTimes = { 0f, 0.33f, 0.66f, 1f };
    const float RunnerLift = 1.05f;
    /// <summary>Layer of the full-screen fog backdrop: only band cameras draw it.</summary>
    public const int BackdropLayer = 30;

    public class Diorama
    {
        public int checkpoint;
        public CheckpointProfile profile;
        public AtmosphereLook look;
        public Transform root, follow;
        public Vector3 origin;
        public Camera cam;
        public RenderTexture rt;
        public Material skyMat;
        public PortalDoor tower;
        public readonly Vector4[] blockSlots = new Vector4[AtmosphereLook.BlockSlots];
        public readonly Vector4[] skySlots = new Vector4[AtmosphereLook.SkyStride];
        public Vector3[] route;
        public float[] cum;
        public float glideEnd, s, speed, ortho;
        public Quaternion camRot;
        public Vector3 focus;
        public float fade = 1f;          // 0..1, multiplies the band (the glide's loop fades through it)
        public int loopState;             // 0 gliding, 1 fading out, 2 fading in
        public float rise = -1f;          // -1 = not started; 0..1 rising
        public int renderEvery, lastRendered = -1000;
        public bool fullRate;
    }

    readonly List<Diorama> dioramas = new List<Diorama>();
    readonly Dictionary<int, Diorama> byCheckpoint = new Dictionary<int, Diorama>();
    Material blockCopy;
    GameObject backdrop;
    Material backdropMat;
    Vector2 wind;
    MaterialPropertyBlock mpb;

    Color homeSunColor; float homeSunIntensity; Quaternion homeSunRot;
    Color homeAmbSky, homeAmbEq, homeAmbGround;
    VolumeProfile homeProfile;
    bool built;

    public IReadOnlyList<Diorama> Dioramas => dioramas;

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
    }

    void OnDestroy()
    {
        foreach (Diorama d in dioramas) { if (d.rt != null) { d.rt.Release(); Destroy(d.rt); } if (d.skyMat != null) Destroy(d.skyMat); }
        if (blockCopy != null) Destroy(blockCopy);
        if (backdropMat != null) Destroy(backdropMat);
    }

    // ------------------------------------------------------------------ build

    public void Build()
    {
        if (built || checkpoints == null) return;
        built = true;
        mpb = new MaterialPropertyBlock();

        if (sun != null) { homeSunColor = sun.color; homeSunIntensity = sun.intensity; homeSunRot = sun.transform.rotation; }
        homeAmbSky = RenderSettings.ambientSkyColor; homeAmbEq = RenderSettings.ambientEquatorColor; homeAmbGround = RenderSettings.ambientGroundColor;
        homeProfile = volume != null ? volume.sharedProfile : null;

        if (blockMaterial != null && blockWipeShader != null)
        {
            blockCopy = new Material(blockMaterial) { name = "MenuBlock (wipe)" };
            int q = blockMaterial.renderQueue; blockCopy.shader = blockWipeShader; blockCopy.renderQueue = q; blockCopy.SetFloat(RoleId, 0);
        }

        Shader.SetGlobalVector(WipeId, new Vector4(-10f, 0.05f, -0.18f, 1f));
        foreach (string n in DeltaNames) Shader.SetGlobalVector(n, Vector4.zero);
        Shader.SetGlobalFloat("_ZoneWipeX", 2f);
        Shader.SetGlobalFloat("_ZoneGoneShadow", 0f);
        Shader.SetGlobalVector(RunnerPId, Vector4.zero);
        Shader.SetGlobalVector(RunnerNId, Vector4.zero);

        BuildBackdrop();

        // every checkpoint has its baked legs; a placeholder (no look yet) borrows the latest real look
        AtmosphereLook lastLook = null;
        int slot = 0;
        for (int i = 0; i < checkpoints.checkpoints.Count; i++)
        {
            CheckpointProfile p = checkpoints.checkpoints[i];
            if (p == null) continue;
            if (p.look != null) lastLook = p.look;
            AtmosphereLook look = p.look != null ? p.look : lastLook;
            MenuSliceData.Slice slice = slices != null ? slices.Get(p.index) : null;
            if (look == null || slice == null) continue;
            byCheckpoint[p.index] = Make(p, look, slice, slot++);
        }
    }

    void BuildBackdrop()
    {
        if (backdropShader == null) return;
        backdropMat = new Material(backdropShader) { name = "MenuChasmBackdrop" };
        var mesh = new Mesh { name = "MenuChasmBackdrop" };
        mesh.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(-1f, 3f, 0f), new Vector3(3f, -1f, 0f) };
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        backdrop = new GameObject("MenuChasmBackdrop");
        backdrop.layer = BackdropLayer;   // band cameras only (the shop and icon cameras leave it out)
        backdrop.transform.SetParent(transform, false);
        backdrop.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = backdrop.AddComponent<MeshRenderer>();
        r.sharedMaterial = backdropMat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    static float Hash(int n, int salt) { uint h = (uint)(n * 73856093) ^ (uint)(salt * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xffff) / 65535f; }

    Diorama Make(CheckpointProfile p, AtmosphereLook look, MenuSliceData.Slice slice, int slot)
    {
        var d = new Diorama { checkpoint = p.index, profile = p, look = look };
        d.origin = new Vector3((slot + 1) * spacing, 0f, 0f);
        d.root = new GameObject("Diorama CP" + p.index).transform;
        d.root.SetParent(transform, false);
        d.root.position = d.origin + Vector3.down * riseDrop;   // rises when first shown
        PackLook(look, d);

        // ---- the baked legs, tinted along the route like PathDepthShading's ahead gradient ----
        Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        int count = slice.cubePos.Count;
        Transform firstCube = null;
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Cube");
            go.transform.SetParent(d.root, false);
            go.transform.localPosition = slice.cubePos[i];
            go.transform.localRotation = slice.cubeRot[i];
            go.transform.localScale = slice.cubeScale;
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = blockCopy != null ? blockCopy : blockMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Color tint = Ahead(look, Mathf.Clamp01((float)i / Mathf.Max(1, count - 1)));
            mpb.Clear();
            mpb.SetColor(BaseColorId, tint);
            mpb.SetColor(ColorId, tint);
            mr.SetPropertyBlock(mpb);
            if (i == 0) firstCube = go.transform;
        }

        // ---- the exit tower (checkpoints 2+): the real PortalDoor, in the look's stone and glow ----
        if (slice.exitTower && firstCube != null)
        {
            Vector3 fwd = slice.towerForward.sqrMagnitude > 0.01f ? slice.towerForward.normalized : Vector3.forward;
            d.tower = PortalDoor.Create(firstCube, firstCube.position, fwd, Vector3.up, 0, slice.cubeScale.x, true, true, voidMaterial);
            Color stone = look.doorStoneColor; stone.a = 1f;
            d.tower.SetColor(stone);
            d.tower.SetGlowColor(look.doorGlowColor);
            d.tower.SetReveal(1f);
        }

        // ---- the glide route (runner path), capped ----
        int n = slice.route.Count;
        d.route = new Vector3[Mathf.Max(2, n)];
        d.cum = new float[d.route.Length];
        for (int i = 0; i < d.route.Length; i++)
        {
            d.route[i] = d.origin + (i < n ? slice.route[i] : slice.route[n - 1] + Vector3.forward) + Vector3.up * RunnerLift;
            if (i > 0) d.cum[i] = d.cum[i - 1] + Vector3.Distance(d.route[i - 1], d.route[i]);
        }
        d.glideEnd = Mathf.Min(d.cum[d.cum.Length - 1], glideLength);
        d.s = 0f;   // every glide starts at the run start / the exit tower
        d.speed = glideSpeed * Mathf.Lerp(0.8f, 1.2f, Hash(p.index, 2));
        d.focus = PointAt(d, d.s);
        d.follow = new GameObject("Follow").transform;
        d.follow.SetParent(d.root, false);

        // ---- ambient motes follow the camera's focus ----
        GameObject motes = p.ambientParticlePrefab != null ? Instantiate(p.ambientParticlePrefab, d.follow) : null;
        if (motes == null)
        {
            motes = new GameObject("Motes");
            motes.transform.SetParent(d.follow, false);
            MenuParticles.Configure(motes.AddComponent<ParticleSystem>(), MenuParticles.ColorFor(look), particleMaterial);
        }
        motes.transform.localPosition = Vector3.up * 3f;

        // ---- the camera: gameplay framing (baked E pose), pulled back, each band a little different ----
        var camGo = new GameObject("BandCamera");
        camGo.transform.SetParent(d.root, false);
        d.cam = camGo.AddComponent<Camera>();
        d.cam.clearFlags = CameraClearFlags.SolidColor;
        d.cam.backgroundColor = look.cameraBackground;
        d.cam.orthographic = true;
        float band = bandReferencePixels.y / Mathf.Max(1f, gameplayReferenceHeight);
        d.ortho = slice.camOrtho * band * pullBack * Mathf.Lerp(0.92f, 1.1f, Hash(p.index, 3));
        d.cam.orthographicSize = d.ortho;
        d.cam.nearClipPlane = 1f;
        d.cam.farClipPlane = 260f;
        d.cam.allowMSAA = false;
        d.cam.allowHDR = false;
        d.cam.useOcclusionCulling = false;
        Vector3 e = slice.camEuler;
        d.camRot = Quaternion.Euler(e.x + Mathf.Lerp(-3f, 3f, Hash(p.index, 4)), e.y + Mathf.Lerp(-9f, 9f, Hash(p.index, 5)), 0f);
        var data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderShadows = false;
        data.renderPostProcessing = postProcessing;
        data.antialiasing = AntialiasingMode.None;
        data.requiresDepthTexture = false;
        data.requiresColorTexture = false;
        int w = Mathf.Max(64, Mathf.RoundToInt(bandReferencePixels.x * renderScale));
        int h = Mathf.Max(32, Mathf.RoundToInt(bandReferencePixels.y * renderScale));
        d.rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32) { name = "BandRT CP" + p.index, antiAliasing = 1, useMipMap = false };
        d.rt.Create();
        d.cam.targetTexture = d.rt;
        d.cam.enabled = false;
        AddSky(d);
        PlaceCamera(d);
        if (d.tower != null) { Vector3 f = d.camRot * Vector3.forward; f.y = 0f; d.tower.SetView(f.normalized); }

        dioramas.Add(d);
        return d;
    }

    static Color Ahead(AtmosphereLook l, float t)
    {
        if (l.aheadKeys == null || l.aheadKeys.Length < 4) return Color.white;
        for (int i = 0; i < 3; i++)
            if (t <= GradientTimes[i + 1]) return Color.Lerp(l.aheadKeys[i], l.aheadKeys[i + 1], (t - GradientTimes[i]) / (GradientTimes[i + 1] - GradientTimes[i]));
        return l.aheadKeys[3];
    }

    // The gameplay sky (MonumentSkyBackdrop): a quad just inside the far plane, on the wipe shader (reads _LTSkyA per camera).
    void AddSky(Diorama d)
    {
        if (skyMaterial == null || skyWipeShader == null) return;
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "SkyQuad";
        Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(d.cam.transform, false);
        float dist = d.cam.farClipPlane - 2f;
        float aspect = (float)d.rt.width / d.rt.height;
        float h = d.cam.orthographic ? d.cam.orthographicSize * 2f : 2f * dist * Mathf.Tan(d.cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        q.transform.localPosition = new Vector3(0f, 0f, dist);
        q.transform.localScale = new Vector3(h * aspect * 1.04f, h * 1.04f, 1f);
        var m = new Material(skyMaterial) { name = "MenuSky CP" + d.checkpoint };
        int queue = skyMaterial.renderQueue; m.shader = skyWipeShader; m.renderQueue = queue;
        m.SetFloat("_Aspect", aspect);
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        d.skyMat = m;
    }

    static void PackLook(AtmosphereLook look, Diorama d)
    {
        System.Array.Clear(d.blockSlots, 0, d.blockSlots.Length);
        if (look.blockRoles != null) System.Array.Copy(look.blockRoles, d.blockSlots, Mathf.Min(look.blockRoles.Length, d.blockSlots.Length));
        if (look.sky != null) System.Array.Copy(look.sky, d.skySlots, Mathf.Min(look.sky.Length, d.skySlots.Length));
    }

    // ------------------------------------------------------------------ queries / control

    public RenderTexture TextureFor(int checkpoint) => DioramaFor(checkpoint)?.rt;
    public Diorama DioramaFor(int checkpoint) => byCheckpoint.TryGetValue(checkpoint, out Diorama d) ? d : null;
    /// <summary>The band's loop fade (1 = fully shown).</summary>
    public float FadeFor(int checkpoint) { Diorama d = DioramaFor(checkpoint); return d != null ? d.fade : 1f; }

    public void ClearRequests()
    {
        for (int i = 0; i < dioramas.Count; i++) { dioramas[i].renderEvery = 0; dioramas[i].fullRate = false; }
    }

    /// <summary>Render this checkpoint's world about every n-th frame (fullRate = every frame, e.g. the launch).</summary>
    public void Request(int checkpoint, int every, bool fullRate = false)
    {
        Diorama d = DioramaFor(checkpoint);
        if (d == null) return;
        if (d.renderEvery == 0 || every < d.renderEvery) d.renderEvery = Mathf.Max(1, every);
        d.fullRate |= fullRate;
        if (d.rise < 0f) d.rise = 0f;   // first shown: its legs rise
    }

    /// <summary>The menu has no runner any more; kept so the shop can call it.</summary>
    public void ApplyGhostSkin(Mesh mesh, Material material, Material trail) { }

    // ------------------------------------------------------------------ per frame

    void LateUpdate()
    {
        if (!built) return;
        long g0 = System.GC.GetAllocatedBytesForCurrentThread();
        Tick();
        MainMenuController.DevAllocBytes += System.GC.GetAllocatedBytesForCurrentThread() - g0;
    }

    void Tick()
    {
        float dt = Time.unscaledDeltaTime;
        Vector2 wd = fog.windDirection.sqrMagnitude > 1e-6f ? fog.windDirection.normalized : Vector2.right;
        wind += wd * (fog.billowSpeed * dt);
        int frame = Time.frameCount;

        for (int i = 0; i < dioramas.Count; i++)
        {
            Diorama d = dioramas[i];
            d.cam.enabled = false;
            if (d.renderEvery <= 0) continue;
            if (d.rise >= 0f && d.rise < 1f)
            {
                d.rise = Mathf.Min(1f, d.rise + dt / Mathf.Max(0.05f, riseSeconds));
                float k = 1f - d.rise;
                d.root.position = d.origin + Vector3.down * (riseDrop * k * k * k);   // ease out, as RiseFromVoid
            }
            Glide(d, dt);
            PlaceCamera(d);
        }

        // at most maxCamerasPerFrame draw this frame: full-rate ones first, then the stalest that are due
        int budget = maxCamerasPerFrame;
        for (int i = 0; i < dioramas.Count && budget > 0; i++)
        {
            Diorama d = dioramas[i];
            if (d.renderEvery > 0 && d.fullRate) { d.cam.enabled = true; d.lastRendered = frame; budget--; }
        }
        while (budget > 0)
        {
            Diorama pick = null;
            for (int i = 0; i < dioramas.Count; i++)
            {
                Diorama d = dioramas[i];
                if (d.renderEvery <= 0 || d.cam.enabled || frame - d.lastRendered < d.renderEvery) continue;
                if (pick == null || d.lastRendered < pick.lastRendered) pick = d;
            }
            if (pick == null) break;
            pick.cam.enabled = true;
            pick.lastRendered = frame;
            budget--;
        }
    }

    // The focus follows the runner's route at a calm pace; at the end of the glide the band fades round to the start.
    void Glide(Diorama d, float dt)
    {
        const float fadeSeconds = 0.45f;
        // a long first leg (the runway out of a door) is crossed briskly in its middle, so all the legs get seen
        float l1 = d.cum.Length > 1 ? d.cum[1] : 0f, boost = 1f;
        if (l1 > 40f) boost += 2.5f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 24f, d.s)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(l1 - 24f, l1 - 12f, d.s)));
        d.s += d.speed * boost * dt;
        if (d.loopState == 0)
        {
            if (d.s >= d.glideEnd) d.loopState = 1;
        }
        else if (d.loopState == 1)
        {
            d.fade = Mathf.Max(0f, d.fade - dt / fadeSeconds);
            if (d.fade <= 0f) { d.s = 0f; d.focus = PointAt(d, 0f); d.loopState = 2; }
        }
        else
        {
            d.fade = Mathf.Min(1f, d.fade + dt / fadeSeconds);
            if (d.fade >= 1f) d.loopState = 0;
        }
        Vector3 target = PointAt(d, Mathf.Min(d.s, d.cum[d.cum.Length - 1]));
        d.focus = Vector3.Lerp(d.focus, target, 1f - Mathf.Exp(-dt * 2.5f));   // the rig's soft follow, no snaps
        d.follow.position = d.focus;
    }

    static Vector3 PointAt(Diorama d, float s)
    {
        int n = d.route.Length;
        if (s <= 0f) return d.route[0];
        for (int i = 1; i < n; i++)
            if (s <= d.cum[i]) return Vector3.Lerp(d.route[i - 1], d.route[i], (s - d.cum[i - 1]) / Mathf.Max(0.0001f, d.cum[i] - d.cum[i - 1]));
        return d.route[n - 1];
    }

    void PlaceCamera(Diorama d)
    {
        Vector3 fwd = d.camRot * Vector3.forward;
        d.cam.transform.SetPositionAndRotation(d.focus - fwd * 90f, d.camRot);
    }

    // ------------------------------------------------------------------ per-camera look

    Diorama Owner(Camera c)
    {
        for (int i = 0; i < dioramas.Count; i++) if (dioramas[i].cam == c) return dioramas[i];
        return null;
    }

    void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
    {
        Diorama d = Owner(c);
        if (d == null) return;
        AtmosphereLook l = d.look;
        Shader.SetGlobalVectorArray(LTAId, d.blockSlots);
        Shader.SetGlobalVectorArray(LTBId, d.blockSlots);
        Shader.SetGlobalVectorArray(SkyAId, d.skySlots);
        Shader.SetGlobalVectorArray(SkyBId, d.skySlots);

        Color top = l.chasmTop.linear, bottom = l.chasmBottom.linear, glow = l.chasmGlow.linear;
        Shader.SetGlobalVector(TopId, new Vector4(top.r, top.g, top.b, 1f));
        Shader.SetGlobalVector(BottomId, new Vector4(bottom.r, bottom.g, bottom.b, 1f));
        Shader.SetGlobalVector(GlowId, new Vector4(glow.r, glow.g, glow.b, l.chasmGlowStrength));
        Shader.SetGlobalVector(GlowShapeId, new Vector4(l.chasmGlowCentre.x, l.chasmGlowCentre.y, l.chasmGlowRadius, 0f));
        float floorY = d.origin.y - 0.5f;
        Shader.SetGlobalVector(HeightId, new Vector4(floorY - fog.fogStart, floorY - fog.fogSolid, fog.billowHeight, 1f / Mathf.Max(fog.billowSize, 0.5f)));
        Shader.SetGlobalVector(WindId, new Vector4(wind.x, wind.y, 0f, 0f));
        Shader.SetGlobalVector(ScreenId, new Vector4(fog.screenFogSolid, fog.screenFogTop, fog.screenFogStrength, fog.veil));
        Shader.SetGlobalVector(HouseId, new Vector4(fog.houseFogShare, fog.houseFogAtHouse, 1f / Mathf.Max(fog.houseFogDepth, 0.01f), 0f));
        Vector3 fwd = c.transform.forward, pos = c.transform.position;
        Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
        if (flat.sqrMagnitude > 1e-4f) fwd = flat.normalized;
        float focusDepth = Vector3.Dot(d.focus - pos, fwd);
        Shader.SetGlobalVector(DepthId, new Vector4(focusDepth + fog.hazeStart, focusDepth + fog.hazeEnd, fog.hazeMax, 0f));
        Shader.SetGlobalVector(ViewId, new Vector4(fwd.x, fwd.y, fwd.z, l.hasChasm ? 1f : 0f));
        Shader.SetGlobalVector(ViewPosId, new Vector4(pos.x, pos.y, pos.z, 0f));
        Shader.SetGlobalFloat(FogRefId, d.focus.y);

        if (sun != null)
        {
            sun.color = l.sunColor;
            sun.intensity = l.sunIntensity;
            if (d.profile.applySunRotation) sun.transform.rotation = Quaternion.Euler(l.sunEuler);
        }
        RenderSettings.ambientSkyColor = l.ambientSky;
        RenderSettings.ambientEquatorColor = l.ambientEquator;
        RenderSettings.ambientGroundColor = l.ambientGround;
        if (volume != null && l.volumeProfile != null) volume.sharedProfile = l.volumeProfile;
    }

    void OnEndCamera(ScriptableRenderContext ctx, Camera c)
    {
        if (Owner(c) == null) return;
        if (sun != null) { sun.color = homeSunColor; sun.intensity = homeSunIntensity; sun.transform.rotation = homeSunRot; }
        RenderSettings.ambientSkyColor = homeAmbSky;
        RenderSettings.ambientEquatorColor = homeAmbEq;
        RenderSettings.ambientGroundColor = homeAmbGround;
        if (volume != null) volume.sharedProfile = homeProfile;
    }
}

/// <summary>Ambient motes for the menu dioramas: small squares (no round glows), drifting up slowly.</summary>
public static class MenuParticles
{
    public static Color ColorFor(AtmosphereLook l)
    {
        Color c = l.doorGlowColor.maxColorComponent > 0.05f ? l.doorGlowColor : l.chasmGlow;
        c.a = 0.75f;
        return c;
    }

    public static void Configure(ParticleSystem ps, Color color, Material material)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.duration = 6f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 0.5f);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 60;
        main.useUnscaledTime = true;
        main.prewarm = true;
        var em = ps.emission;
        em.rateOverTime = 9f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(18f, 6f, 16f);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.y = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (material != null) r.sharedMaterial = material;
        ps.Play();
    }
}
