using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// LookTransition (MainScene prop camera 3.2): at the zone doors listed in a <see cref="CheckpointLookSequence"/> the whole
/// atmosphere changes - inside the running scene, no load - into another scene's look (an <see cref="AtmosphereLook"/>),
/// with a slow left-to-right wipe, a camera move behind the runner at the exit door and a small "CHECKPOINT N" label.
///
///   Looks       The scene's look materials are drawn through runtime copies on the LookTransition shaders, which hold
///               two looks at once (global slots A = in force, B = next) and pick per pixel by the wipe. Before the first
///               door both slots hold the scene's own values and the wipe is idle, so it looks exactly as authored.
///   Globals     Sun, ambient, ChasmFog's backdrop and the post-processing cannot be split per pixel. During the wipe the
///               shaders add "next minus current" on the wiped side; afterwards the real values ease to the next look
///               while that difference shrinks to zero, so the picture never jumps and ends exact.
///   Camera      Through CameraParentRotator.CameraOverride: each frame it reads the active mode's own pose (whatever mode -
///               C, D or E - re-sampled live) and blends it with a chase pose behind the runner: A glide in, B hold (wipe),
///               C glide back out into the live pose, so control is handed back without a snap. The mode's own yaw changes
///               are held off while this runs (SuppressYawChanges), taps are dropped (inputSuspended) and the runner eases
///               to no lower than 85% of its speed across A-B, back to 100% across C.
///   Runway      Before the door is laid, DynamicPathGenerator.exitRunwaySecondsOverride asks for an exit runway long enough
///               for A + B + C + D at that speed, so the player always gets D seconds on the normal camera before the
///               first turn. Only the first portalRunwayCubes of it count toward the two-tower shot.
///   Restart     PortalZoneController.OpeningRestored puts the scene's own look back. Doors without an entry behave as before.
///
/// Gameplay (generation, obstacles, scoring, CameraFeel tuning) is not touched; nothing is allocated per frame.
/// </summary>
[DefaultExecutionOrder(-1000)]   // before the generator, the sky backdrop and ChasmFog: the look copies must be in place first
public class LookTransitionDirector : MonoBehaviour
{
    public CheckpointLookSequence sequence;
    [Tooltip("MainScene prop camera 3.6+: build the portal sequence from these checkpoint profiles (the single source of truth for gameplay " +
             "and the menu) instead of the Sequence asset, and apply the set's legsBetweenPortals. Empty = the Sequence asset, exactly as before.")]
    public CheckpointSet checkpoints;
    CheckpointLookSequence runtimeSequence;   // built from checkpoints; owned here
    [Tooltip("Optional: the look the run starts in (and every restart returns to), instead of the look read from the scene's own " +
             "materials. Empty = the scene's own look, exactly as before. MainScene prop camera Toon uses it to start in the toon look.")]
    public AtmosphereLook baseLookOverride;

    [Header("LookTransition shaders")]
    public Shader blockWipeShader;
    public Shader skyWipeShader;

    [Header("The scene's own look materials (only read: runtime copies are drawn)")]
    public Material blockMaterial;
    public Material blockFadeMaterial;
    public Material playerMaterial;
    public Material obstacleMaterial;
    public Material skyMaterial;

    [Header("Scene (found when empty)")]
    public DynamicPathGenerator pathGen;
    public CubeRailRunner runner;
    public CameraParentRotator rig;
    public PortalZoneController zones;
    public PathDepthShading shading;
    public ChasmFog chasm;
    public Light sun;
    public Volume baseVolume;
    public MonumentSkyBackdrop skyBackdrop;
    public RunnerTrailFX trailFx;
    public RunnerBoostFX boostFx;
    public ActiveFaceHighlight faceHighlight;
    public TurnFaceArrows arrows;

    [Header("Chase shot (orthographic)")]
    [Tooltip("Camera pitch behind the runner. Keep it at or above PathBackgroundGenerator.guardPitch (24 here) so every rock stays under the sight-lines and none has to shrink away in view.")]
    [Range(24f, 35.264f)] public float chaseTilt = 26f;
    [Tooltip("Degrees the chase view is turned off the runway's own heading, so the exit tower is off to the side instead of between the camera and the runner (and no face of the track ever sits edge-on).")]
    [Range(15f, 60f)] public float chaseYawOffset = 35f;
    [Tooltip("Where the runner sits below the screen centre in the chase shot, as a share of the half-height.")]
    [Range(0.1f, 0.6f)] public float chaseRunnerBelowCentre = 0.32f;
    [Tooltip("Chase zoom as a share of the two-tower shot's size. Never tighter than the Camera's authored size.")]
    [Range(0.5f, 1f)] public float chaseZoom = 0.75f;
    [Tooltip("The glide into the chase shot starts the moment the runner comes out of the exit door and slows into place (ease-out), instead of easing in from a standstill - which read as the camera hanging for a beat after the teleport. Off = the original ease-in-out.")]
    public bool chaseGlideEaseOut = true;

    [Header("Wipe")]
    [Tooltip("Half-width of the wipe's soft edge, in screen widths.")]
    [Range(0.005f, 0.2f)] public float wipeSoftness = 0.05f;
    [Tooltip("Edge slant: shift per screen height (negative = the top of the edge leads).")]
    [Range(-0.5f, 0.5f)] public float wipeSlant = -0.18f;

    [Header("Label")]
    [Tooltip("Seconds (unscaled) the label stays after the wipe has crossed it, then it fades.")]
    public float labelHold = 1.4f;
    public float labelFade = 0.8f;
    public int labelFontSize = 44;
    [Tooltip("Reference-pixel offset of the label's top below the top of the safe area (the score sits at 50-180).")]
    public float labelTop = 196f;

    [Header("Debug")]
    public bool logTimeline = true;

    // ---- shader ids ----
    static readonly int LTAId = Shader.PropertyToID("_LTA");
    static readonly int LTBId = Shader.PropertyToID("_LTB");
    static readonly int SkyAId = Shader.PropertyToID("_LTSkyA");
    static readonly int SkyBId = Shader.PropertyToID("_LTSkyB");
    static readonly int WipeId = Shader.PropertyToID("_LTWipe");
    static readonly int ChasmTopDId = Shader.PropertyToID("_LTChasmTopD");
    static readonly int ChasmBottomDId = Shader.PropertyToID("_LTChasmBottomD");
    static readonly int ChasmGlowDId = Shader.PropertyToID("_LTChasmGlowD");
    static readonly int ChasmShapeDId = Shader.PropertyToID("_LTChasmShapeD");
    static readonly int SunDId = Shader.PropertyToID("_LTSunD");
    static readonly int AmbDId = Shader.PropertyToID("_LTAmbD");
    static readonly int RoleId = Shader.PropertyToID("_LTRole");

    // ---- looks ----
    readonly Vector4[] slotA = new Vector4[AtmosphereLook.BlockSlots];
    readonly Vector4[] slotB = new Vector4[AtmosphereLook.BlockSlots];
    readonly Vector4[] baseSlots = new Vector4[AtmosphereLook.BlockSlots];
    readonly Vector4[] skyA = new Vector4[AtmosphereLook.SkyStride];
    readonly Vector4[] skyB = new Vector4[AtmosphereLook.SkyStride];
    readonly Vector4[] baseSky = new Vector4[AtmosphereLook.SkyStride];
    AtmosphereLook baseLook;     // the running scene's own look, read at start
    AtmosphereLook fromLook;     // the live values when a transition starts (reused)
    AtmosphereLook current;      // the look in force
    AtmosphereLook next;
    VolumeProfile baseProfile;
    Volume nextVolume;
    readonly System.Collections.Generic.Dictionary<Material, Material> copies = new System.Collections.Generic.Dictionary<Material, Material>();
    Material skyCopy;
    bool ready;

    // ---- the transition in flight ----
    CheckpointLookSequence.Entry entry;
    CheckpointLookSequence.Timing T;
    bool active, cinematic, camActive, keepObstacle, takePlank;
    float t, lastAccentK = -1f;
    float chaseYaw, chaseOrtho;
    DynamicPathGenerator.Segment exitSegment;
    bool turnLogged, onExitSeen;
    /// <summary>Diagnostic: seconds on the normal camera before the first turn in the last transition (-1 = not reached yet).</summary>
    public float LastReadTime { get; private set; } = -1f;

    // ---- accents (cached arrays: no per-frame garbage) ----
    readonly GradientColorKey[] gradKeys = new GradientColorKey[4];
    readonly GradientAlphaKey[] gradAlpha = { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };

    // ---- label ----
    GameObject labelRoot;
    RectMask2D labelMask;
    RectTransform labelMaskRect;
    CanvasGroup labelGroup;
    Text labelText;
    float labelTimer = -1f;   // unscaled seconds since the wipe finished crossing; -1 = not shown

    /// <summary>True while a look change is playing (for tooling).</summary>
    public bool TransitionActive => active;
    /// <summary>Seconds since the teleport of the transition in flight.</summary>
    public float TransitionTime => t;
    /// <summary>True while the cinematic owns the camera.</summary>
    public bool CameraCinematicActive => camActive;
    /// <summary>Name of the look in force.</summary>
    public string CurrentLookName => current != null ? current.sourceScene : "";
    /// <summary>The look in force (a runtime copy for the base look).</summary>
    public AtmosphereLook CurrentLook => current;
    /// <summary>The look a transition in flight is wiping in, or null when none is playing.</summary>
    public AtmosphereLook NextLook => active ? next : null;
    /// <summary>Diagnostic: managed bytes this component allocated in frames where a transition or the cinematic was running, and how many such frames.</summary>
    public long TransitionAllocatedBytes { get; private set; }
    public int TransitionFrames { get; private set; }

    // ------------------------------------------------------------------ setup

    void Awake()
    {
        FindRefs();
        if (checkpoints != null)
        {
            runtimeSequence = checkpoints.BuildRuntimeSequence();
            sequence = runtimeSequence;
            // Before the generator's Start lays the path (this runs at execution order -1000).
            if (pathGen != null && checkpoints.legsBetweenPortals > 0) pathGen.legsBetweenPortals = checkpoints.legsBetweenPortals;
        }
        if (sequence == null || blockWipeShader == null || skyWipeShader == null)
        {
            Debug.LogWarning("[LookTransition] no sequence or shaders assigned - the scene keeps its own look.", this);
            enabled = false;
            return;
        }

        // The scene's own look, read from the scene itself so the stretch before the first door is exactly as authored.
        baseLook = ScriptableObject.CreateInstance<AtmosphereLook>();
        baseLook.name = "Base look (runtime)";
        baseLook.sourceScene = gameObject.scene.name;
        baseLook.Capture(Sources(true));
        ReadAmbient(baseLook);
        if (baseLookOverride != null)
        {
            // Start (and restart) in the given look: a copy, so OnDestroy never destroys the asset.
            Destroy(baseLook);
            baseLook = Instantiate(baseLookOverride);
            baseLook.name = "Base look (override, runtime)";
        }
        baseProfile = baseVolume != null ? baseVolume.sharedProfile : null;
        fromLook = ScriptableObject.CreateInstance<AtmosphereLook>();
        fromLook.name = "From (runtime)";
        current = baseLook;

        for (int r = 0; r < AtmosphereLook.RoleCount; r++)
            System.Array.Copy(baseLook.blockRoles, r * AtmosphereLook.BlockStride, baseSlots, r * AtmosphereLook.BlockStride, AtmosphereLook.BlockStride);
        System.Array.Copy(baseLook.sky, baseSky, AtmosphereLook.SkyStride);
        System.Array.Copy(baseSlots, slotA, slotA.Length);
        System.Array.Copy(baseSlots, slotB, slotB.Length);
        System.Array.Copy(baseSky, skyA, skyA.Length);
        System.Array.Copy(baseSky, skyB, skyB.Length);
        PushSlots();
        SetWipe(-10f);
        ZeroDeltas();

        MakeCopies();
        SwapInCopies();

        if (baseVolume != null)
        {
            var go = new GameObject("LookTransition_NextVolume");
            go.transform.SetParent(transform, false);
            nextVolume = go.AddComponent<Volume>();
            nextVolume.isGlobal = true;
            nextVolume.priority = baseVolume.priority + 1f;
            nextVolume.weight = 0f;
        }
        ready = true;
        if (baseLookOverride != null) PutBaseLookInForce();
    }

    // baseLookOverride: lights, ambient, backdrop, accents and post of the base look, at once (as a restart does).
    void PutBaseLookInForce()
    {
        ApplyAccents(baseLook, baseLook, 1f);
        ApplyLights(baseLook, baseLook, 1f);
        ZeroDeltas();
        if (baseVolume != null && baseLook.volumeProfile != null) baseVolume.sharedProfile = baseLook.volumeProfile;
        baseProfile = baseVolume != null ? baseVolume.sharedProfile : null;
        if (zones != null)
        {
            zones.doorGlowColor = baseLook.doorGlowColor;
            zones.doorStoneColor = baseLook.doorStoneColor;
            zones.sceneAccent = baseLook.sceneAccent;
        }
    }

    void Start()
    {
        if (!ready) return;
        // Anything another script copied off a look material in its own Awake/OnEnable (the sky quad's runtime copy).
        SwapInCopies();
        if (skyBackdrop != null)
            foreach (MeshRenderer mr in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (mr.name == "MonumentSkyQuad" && mr.sharedMaterial != null && mr.sharedMaterial.shader != skyWipeShader)
                    mr.sharedMaterial.shader = skyWipeShader;   // its own runtime copy, never an asset

        if (zones != null) { zones.DoorCrossed += OnDoorCrossed; zones.OpeningRestored += OnOpeningRestored; }
        if (rig != null) rig.CameraOverride += OnCameraOverride;
        BuildLabel();
        if (logTimeline) Debug.Log("[LookTransition] ready: base look '" + baseLook.sourceScene + "', " + sequence.entries.Count + " checkpoint(s).", this);
    }

    void OnDestroy()
    {
        if (zones != null) { zones.DoorCrossed -= OnDoorCrossed; zones.OpeningRestored -= OnOpeningRestored; }
        if (rig != null) { rig.CameraOverride -= OnCameraOverride; rig.SuppressYawChanges = false; }
        if (runner != null) { runner.inputSuspended = false; runner.CinematicSpeedFactor = 1f; }
        if (pathGen != null) { pathGen.exitRunwaySecondsOverride = 0f; pathGen.exitRunwayFrameCubes = 0; }
        Shader.SetGlobalVector(WipeId, Vector4.zero);
        ZeroDeltas();
        foreach (Material m in copies.Values) if (m != null) Destroy(m);
        if (skyCopy != null) Destroy(skyCopy);
        if (baseLook != null) Destroy(baseLook);
        if (fromLook != null) Destroy(fromLook);
        if (labelRoot != null) Destroy(labelRoot);
        if (runtimeSequence != null) Destroy(runtimeSequence);
    }

    void FindRefs()
    {
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (runner == null) runner = FindFirstObjectByType<CubeRailRunner>();
        if (rig == null) rig = FindFirstObjectByType<CameraParentRotator>();
        if (zones == null) zones = FindFirstObjectByType<PortalZoneController>();
        if (shading == null) shading = FindFirstObjectByType<PathDepthShading>();
        if (chasm == null) chasm = FindFirstObjectByType<ChasmFog>();
        if (skyBackdrop == null) skyBackdrop = FindFirstObjectByType<MonumentSkyBackdrop>();
        if (runner != null)
        {
            if (trailFx == null) trailFx = runner.GetComponent<RunnerTrailFX>();
            if (boostFx == null) boostFx = runner.GetComponent<RunnerBoostFX>();
            if (faceHighlight == null) faceHighlight = runner.GetComponent<ActiveFaceHighlight>();
        }
        if (arrows == null) arrows = FindFirstObjectByType<TurnFaceArrows>();
        if (sun == null)
        {
            sun = RenderSettings.sun;
            if (sun == null)
                foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
        }
        if (baseVolume == null)
            foreach (Volume v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if (v.isGlobal && (baseVolume == null || v.priority > baseVolume.priority)) baseVolume = v;
    }

    AtmosphereLook.Sources Sources(bool withMaterials) => new AtmosphereLook.Sources
    {
        block = withMaterials ? blockMaterial : null,
        blockFade = withMaterials ? blockFadeMaterial : null,
        player = withMaterials ? playerMaterial : null,
        obstacle = withMaterials ? obstacleMaterial : null,
        sky = withMaterials ? skyMaterial : null,
        chasm = chasm, sun = sun,
        camera = rig != null && rig.boomCamera != null ? rig.boomCamera : Camera.main,
        volume = baseVolume, shading = shading, zones = zones, trail = trailFx,
        faceHighlight = faceHighlight, arrows = arrows, generator = pathGen,
    };

    static void ReadAmbient(AtmosphereLook l)
    {
        l.ambientMode = (int)RenderSettings.ambientMode;
        l.ambientSky = RenderSettings.ambientSkyColor;
        l.ambientEquator = RenderSettings.ambientEquatorColor;
        l.ambientGround = RenderSettings.ambientGroundColor;
    }

    // Runtime copies of the look materials on the LookTransition shaders. The assets are never written.
    void MakeCopies()
    {
        Material[] roles = { blockMaterial, blockFadeMaterial, playerMaterial, obstacleMaterial };
        for (int r = 0; r < roles.Length; r++)
        {
            Material src = roles[r];
            if (src == null || copies.ContainsKey(src)) continue;
            var m = new Material(src) { name = src.name + " (LookTransition)" };
            int queue = src.renderQueue;
            m.shader = blockWipeShader;
            m.renderQueue = queue;
            m.SetFloat(RoleId, r);
            copies[src] = m;
        }
        if (skyMaterial != null)
        {
            skyCopy = new Material(skyMaterial) { name = skyMaterial.name + " (LookTransition)" };
            int queue = skyMaterial.renderQueue;
            skyCopy.shader = skyWipeShader;
            skyCopy.renderQueue = queue;
            copies[skyMaterial] = skyCopy;
        }
    }

    Material CopyOf(Material m) => m != null && copies.TryGetValue(m, out Material c) ? c : m;

    // Every renderer in the scene (inactive ones too, e.g. the cube the generator clones) and every script field that
    // hands a look material on.
    void SwapInCopies()
    {
        foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material c = CopyOf(mats[i]);
                if (c != mats[i]) { mats[i] = c; changed = true; }
            }
            if (changed) r.sharedMaterials = mats;
        }
        if (pathGen != null) pathGen.obstaclePyramidMaterial = CopyOf(pathGen.obstaclePyramidMaterial);
        if (shading != null) shading.fadeMaterial = CopyOf(shading.fadeMaterial);
        if (zones != null) { zones.blockMaterial = CopyOf(zones.blockMaterial); zones.skyMaterial = CopyOf(zones.skyMaterial); }
        if (skyBackdrop != null) skyBackdrop.skyMaterial = CopyOf(skyBackdrop.skyMaterial);
    }

    // ------------------------------------------------------------------ globals

    void PushSlots()
    {
        Shader.SetGlobalVectorArray(LTAId, slotA);
        Shader.SetGlobalVectorArray(LTBId, slotB);
        Shader.SetGlobalVectorArray(SkyAId, skyA);
        Shader.SetGlobalVectorArray(SkyBId, skyB);
    }

    void SetWipe(float x) => Shader.SetGlobalVector(WipeId, new Vector4(x, wipeSoftness, wipeSlant, 1f));

    static void ZeroDeltas()
    {
        Shader.SetGlobalVector(ChasmTopDId, Vector4.zero);
        Shader.SetGlobalVector(ChasmBottomDId, Vector4.zero);
        Shader.SetGlobalVector(ChasmGlowDId, Vector4.zero);
        Shader.SetGlobalVector(ChasmShapeDId, Vector4.zero);
        Shader.SetGlobalVector(SunDId, Vector4.zero);
        Shader.SetGlobalVector(AmbDId, Vector4.zero);
    }

    static Vector4 V(Color c) => new Vector4(c.r, c.g, c.b, 0f);

    // ------------------------------------------------------------------ per frame

    void Update()
    {
        if (!ready) return;
        bool measure = active || camActive;
        long g0 = measure ? System.GC.GetAllocatedBytesForCurrentThread() : 0L;
        RequestRunway();

        if (active)
        {
            t += Time.deltaTime;
            Tick();
        }
        // A death mid-cinematic: give the camera, taps and speed back at once (the look carries on easing).
        if (camActive && runner != null && (runner.IsGameOver || runner.IsRebuilding)) ReleaseCamera();
        UpdateLabel();
        if (measure)
        {
            TransitionAllocatedBytes += System.GC.GetAllocatedBytesForCurrentThread() - g0;
            TransitionFrames++;
        }
    }

    // The exit runway of the next door is laid as soon as the runway INTO it is: ask for the room the cinematic needs ahead of time.
    void RequestRunway()
    {
        if (pathGen == null) return;
        CheckpointLookSequence.Entry e = sequence.Find(pathGen.PortalsTaken + 1);
        bool lengthen = e != null && e.cameraCinematic;
        pathGen.exitRunwaySecondsOverride = lengthen ? sequence.TimingFor(e).RunwaySecondsAtFullSpeed : 0f;
        pathGen.exitRunwayFrameCubes = lengthen ? pathGen.portalRunwayCubes : 0;
    }

    void OnDoorCrossed(int portal)
    {
        CheckpointLookSequence.Entry e = sequence.Find(portal);
        if (e == null)
        {
            // The controller just put its opening palette back on the path tint and trail: keep the look in force.
            if (current != baseLook) ApplyAccents(current, current, 1f);
            return;
        }
        if (active) Commit();
        Begin(e);
    }

    /// <summary>
    /// Checkpoint start (3.6): the looks of every door up to and including portalsTaken are put in force at once, in order
    /// (each folded over the last exactly as its transition would end), with no wipe, camera move or label. The look the
    /// next crossing wipes away from is then the one a real run has at that door.
    /// </summary>
    public void JumpToPortal(int portalsTaken)
    {
        if (!ready) return;
        for (int p = 1; p <= portalsTaken; p++)
        {
            CheckpointLookSequence.Entry e = sequence.Find(p);
            if (e == null) continue;
            if (active) Commit();
            Begin(e, true);
            Commit();
        }
        HideLabel();
    }

    /// <summary>Checkpoint start (3.6): ask now for the runway the next door (portal) needs, before the generator lays it.</summary>
    public void RequestRunwayFor(int portal)
    {
        if (pathGen == null || sequence == null) return;
        CheckpointLookSequence.Entry e = sequence.Find(portal);
        bool lengthen = e != null && e.cameraCinematic;
        pathGen.exitRunwaySecondsOverride = lengthen ? sequence.TimingFor(e).RunwaySecondsAtFullSpeed : 0f;
        pathGen.exitRunwayFrameCubes = lengthen ? pathGen.portalRunwayCubes : 0;
    }

    void Begin(CheckpointLookSequence.Entry e) => Begin(e, false);

    void Begin(CheckpointLookSequence.Entry e, bool silent)
    {
        entry = e;
        T = sequence.TimingFor(e);
        T.readTime = Mathf.Max(1.2f, T.readTime);
        T.speedFloor = Mathf.Clamp(T.speedFloor, 0.85f, 1f);
        next = e.look;
        keepObstacle = e.keepObstacle;
        takePlank = e.takePlankColor;
        t = 0f;
        lastAccentK = -1f;
        active = true;
        turnLogged = false;
        onExitSeen = false;
        LastReadTime = -1f;
        exitSegment = pathGen != null ? pathGen.PortalExitSegment : null;

        // Slot B: the next look, role by role (the obstacle stays as it is when the entry says so).
        System.Array.Copy(slotA, slotB, slotB.Length);
        for (int r = 0; r < AtmosphereLook.RoleCount; r++)
        {
            if (!next.hasRole[r] || (r == AtmosphereLook.RoleObstacle && keepObstacle)) continue;
            System.Array.Copy(next.blockRoles, r * AtmosphereLook.BlockStride, slotB, r * AtmosphereLook.BlockStride, AtmosphereLook.BlockStride);
        }
        System.Array.Copy(next.hasSky ? next.sky : skyA, skyB, AtmosphereLook.SkyStride);
        PushSlots();

        // Where every eased value starts: what is on screen now.
        fromLook.Capture(Sources(false));
        ReadAmbient(fromLook);
        fromLook.skyTopColor = current.skyTopColor;

        if (nextVolume != null)
        {
            nextVolume.sharedProfile = next.volumeProfile;
            nextVolume.weight = 0f;
        }

        cinematic = !silent && e.cameraCinematic && rig != null && rig.boomCamera != null && runner != null;
        if (cinematic) BeginCamera();
        if (silent) return;
        ShowLabel(e.LabelText);

        if (logTimeline)
            Debug.Log("[LookTransition] portal " + e.portal + " -> '" + next.sourceScene + "' at " + Time.time.ToString("F2")
                      + " s, speed " + (runner != null ? runner.CurrentMoveSpeed.ToString("F1") : "?") + " u/s, exit runway "
                      + (exitSegment != null ? exitSegment.cubes.Count + " cubes" : "?") + ", A/B/C/D " + T.followThrough + "/" + T.reveal + "/" + T.riseBack + "/" + T.readTime, this);
    }

    void Tick()
    {
        float A = T.followThrough, B = T.reveal, C = T.riseBack;

        // ---- the wipe: from the moment the runner goes into the door, across the camera's glide and hold ----
        float u = Mathf.Clamp01((t - T.WipeStart) / Mathf.Max(0.01f, T.WipeLength));
        float edge = wipeSoftness + Mathf.Abs(wipeSlant) * 0.5f + 0.01f;
        float k = Smooth(u);
        SetWipe(Mathf.Lerp(-edge, 1f + edge, k));
        if (labelMaskRect != null && labelTimer < 0f) RevealLabel(Mathf.Lerp(-edge, 1f + edge, k));
        if (u >= 1f && labelTimer < 0f) labelTimer = 0f;

        // ---- small accents (path tint, trail, arrows, planks, doors) change with the wipe ----
        if (k != lastAccentK) { ApplyAccents(fromLook, next, k); lastAccentK = k; }

        // ---- lights, ambient, backdrop: held during the wipe (the shaders add the difference), then eased home ----
        float settleFrom = T.WipeStart + T.WipeLength;
        float s = Smooth((t - settleFrom) / Mathf.Max(0.01f, T.settleEnd - settleFrom));
        ApplyLights(fromLook, next, s);

        // ---- post-processing: one slow fade from the start of the wipe to the end of the settle ----
        if (nextVolume != null && next.volumeProfile != null)
            nextVolume.weight = Smooth((t - T.WipeStart) / Mathf.Max(0.01f, T.settleEnd - T.WipeStart));

        // ---- the runner: eased to the speed floor across A-B, back across C; taps held until the camera is back.
        // Only while the cinematic owns the camera: a death releases it, and nothing here may touch the runner after that. ----
        if (camActive && runner != null && !runner.IsGameOver && !runner.IsRebuilding)
        {
            float f;
            if (t < A + B) f = Mathf.Lerp(1f, T.speedFloor, Smooth(t / Mathf.Max(0.01f, T.speedEaseIn)));
            else f = Mathf.Lerp(T.speedFloor, 1f, Smooth((t - A - B) / Mathf.Max(0.01f, C)));
            runner.CinematicSpeedFactor = f;
        }

        // ---- how long the player really had on the normal camera before the first turn ----
        if (!onExitSeen && exitSegment != null && pathGen != null && pathGen.PlayerSegment == exitSegment) onExitSeen = true;
        if (!turnLogged && onExitSeen && pathGen.PlayerSegment != null && pathGen.PlayerSegment != exitSegment)
        {
            turnLogged = true;
            LastReadTime = t - T.CameraSeconds;
            if (logTimeline)
                Debug.Log("[LookTransition] first turn reached at t=" + t.ToString("F2") + " s; camera back at " + T.CameraSeconds.ToString("F2")
                          + " s -> read time " + (t - T.CameraSeconds).ToString("F2") + " s (wanted " + T.readTime.ToString("F2") + ")", this);
        }

        if (t >= T.settleEnd && !camActive) Commit();
    }

    // Path tint, travelled colour, trail and boost colours (as PortalZoneController derives them), face highlight,
    // turn arrows, plank colour, obstacle glow and the exit door's stone and light.
    void ApplyAccents(AtmosphereLook a, AtmosphereLook b, float k)
    {
        if (shading != null && shading.aheadGradient != null)
        {
            for (int i = 0; i < 4; i++) gradKeys[i] = new GradientColorKey(Color.Lerp(a.aheadKeys[i], b.aheadKeys[i], k), ZonePalette.GradientTimes[i]);
            shading.aheadGradient.SetKeys(gradKeys, gradAlpha);
            shading.travelledColor = Color.Lerp(a.travelledColor, b.travelledColor, k);
        }
        Color accent = Color.Lerp(Accent(a), Accent(b), k);
        Color skyTop = Color.Lerp(a.skyTopColor, b.skyTopColor, k);
        if (trailFx != null)
        {
            trailFx.headColor = Color.Lerp(accent, Color.white, 0.6f);
            trailFx.midColor = accent;
            trailFx.tailColor = Color.Lerp(accent, skyTop, 0.55f);
            trailFx.fastHeadColor = Color.Lerp(accent, Color.white, 0.75f);
            trailFx.fastMidColor = accent;
            trailFx.fastTailColor = Color.Lerp(accent, skyTop, 0.4f);
            trailFx.wispColorA = Color.Lerp(a.wispColorA, b.wispColorA, k);
            trailFx.wispColorB = Color.Lerp(a.wispColorB, b.wispColorB, k);
            trailFx.switchColor = Color.Lerp(a.trailSwitchColor, b.trailSwitchColor, k);
        }
        if (boostFx != null) { boostFx.streakColor = accent; boostFx.glowColor = accent * 1.15f; }
        if (faceHighlight != null)
        {
            faceHighlight.faceColor = Color.Lerp(a.faceColor, b.faceColor, k);
            faceHighlight.switchColor = Color.Lerp(a.faceSwitchColor, b.faceSwitchColor, k);
        }
        if (arrows != null) arrows.arrowColor = Color.Lerp(a.arrowColor, b.arrowColor, k);
        if (pathGen != null)
        {
            if (takePlank) pathGen.bridgePlankColor = Color.Lerp(a.plankColor, b.plankColor, k);
            if (!keepObstacle)
            {
                pathGen.obstacleGlowColor = Color.Lerp(a.obstacleGlowColor, b.obstacleGlowColor, k);
                pathGen.obstacleGlowIntensity = Mathf.Lerp(a.obstacleGlowIntensity, b.obstacleGlowIntensity, k);
                pathGen.obstacleGlowRange = Mathf.Lerp(a.obstacleGlowRange, b.obstacleGlowRange, k);
            }
        }
        if (zones != null && zones.ExitDoor != null)
        {
            zones.ExitDoor.SetColor(Stone(Color.Lerp(a.doorStoneColor, b.doorStoneColor, k)));
            zones.ExitDoor.SetGlowColor(Color.Lerp(a.doorGlowColor, b.doorGlowColor, k));
        }
    }

    static Color Accent(AtmosphereLook l) => l.sceneAccent.a > 0f ? new Color(l.sceneAccent.r, l.sceneAccent.g, l.sceneAccent.b, 1f) : new Color(0.55f, 0.95f, 1f, 1f);
    static Color Stone(Color c) => new Color(c.r, c.g, c.b, 1f);

    // Real sun / ambient / backdrop / camera background at s along the way, and what the wiped side still needs added.
    void ApplyLights(AtmosphereLook a, AtmosphereLook b, float s)
    {
        Color sunNow = Color.Lerp(a.sunColor, b.sunColor, s);
        float sunI = Mathf.Lerp(a.sunIntensity, b.sunIntensity, s);
        if (sun != null) { sun.color = sunNow; sun.intensity = sunI; }
        Color sunTarget = b.sunColor.linear * b.sunIntensity, sunReal = sunNow.linear * sunI;
        Shader.SetGlobalVector(SunDId, V(sunTarget - sunReal));

        Color ambNow = Color.Lerp(a.ambientSky, b.ambientSky, s);
        RenderSettings.ambientSkyColor = ambNow;
        RenderSettings.ambientEquatorColor = Color.Lerp(a.ambientEquator, b.ambientEquator, s);
        RenderSettings.ambientGroundColor = Color.Lerp(a.ambientGround, b.ambientGround, s);
        Shader.SetGlobalVector(AmbDId, V(b.ambientSky.linear - ambNow.linear));

        if (chasm != null && a.hasChasm && b.hasChasm)
        {
            chasm.topColor = Color.Lerp(a.chasmTop, b.chasmTop, s);
            chasm.bottomColor = Color.Lerp(a.chasmBottom, b.chasmBottom, s);
            chasm.glowColor = Color.Lerp(a.chasmGlow, b.chasmGlow, s);
            chasm.glowStrength = Mathf.Lerp(a.chasmGlowStrength, b.chasmGlowStrength, s);
            chasm.glowCentre = Vector2.Lerp(a.chasmGlowCentre, b.chasmGlowCentre, s);
            chasm.glowRadius = Mathf.Lerp(a.chasmGlowRadius, b.chasmGlowRadius, s);
            Shader.SetGlobalVector(ChasmTopDId, V(b.chasmTop.linear - chasm.topColor.linear));
            Shader.SetGlobalVector(ChasmBottomDId, V(b.chasmBottom.linear - chasm.bottomColor.linear));
            Color gd = b.chasmGlow.linear - chasm.glowColor.linear;
            Shader.SetGlobalVector(ChasmGlowDId, new Vector4(gd.r, gd.g, gd.b, b.chasmGlowStrength - chasm.glowStrength));
            Shader.SetGlobalVector(ChasmShapeDId, new Vector4(b.chasmGlowCentre.x - chasm.glowCentre.x, b.chasmGlowCentre.y - chasm.glowCentre.y, b.chasmGlowRadius - chasm.glowRadius, 0f));
        }

        Camera cam = rig != null && rig.boomCamera != null ? rig.boomCamera : Camera.main;
        if (cam != null) cam.backgroundColor = Color.Lerp(a.cameraBackground, b.cameraBackground, s);
    }

    // The new look is fully in: make it the one in force (slot A), idle the wipe. Nothing on screen changes.
    void Commit()
    {
        if (!active) return;
        ApplyAccents(fromLook, next, 1f);
        ApplyLights(fromLook, next, 1f);
        System.Array.Copy(slotB, slotA, slotA.Length);
        System.Array.Copy(skyB, skyA, skyA.Length);
        PushSlots();
        SetWipe(-10f);
        ZeroDeltas();
        if (baseVolume != null && next.volumeProfile != null)
        {
            baseVolume.sharedProfile = next.volumeProfile;
            if (nextVolume != null) nextVolume.weight = 0f;
        }
        if (zones != null)
        {
            zones.doorGlowColor = next.doorGlowColor;
            zones.doorStoneColor = next.doorStoneColor;
            zones.sceneAccent = next.sceneAccent;
        }
        current = next;
        active = false;
        if (cinematic) ReleaseCamera();
        if (logTimeline) Debug.Log("[LookTransition] look '" + current.sourceScene + "' in force at t=" + t.ToString("F2") + " s.", this);
    }

    // A soft restart: the scene's own look, instantly (the path is being rebuilt behind the rewind).
    void OnOpeningRestored()
    {
        ReleaseCamera();   // always: puts yaw changes, door view, taps and the runner's speed back to normal
        active = false;
        System.Array.Copy(baseSlots, slotA, slotA.Length);
        System.Array.Copy(baseSlots, slotB, slotB.Length);
        System.Array.Copy(baseSky, skyA, skyA.Length);
        System.Array.Copy(baseSky, skyB, skyB.Length);
        PushSlots();
        SetWipe(-10f);
        keepObstacle = false; takePlank = true;
        ApplyAccents(baseLook, baseLook, 1f);
        ApplyLights(baseLook, baseLook, 1f);
        ZeroDeltas();
        if (baseVolume != null) baseVolume.sharedProfile = baseProfile;
        if (nextVolume != null) nextVolume.weight = 0f;
        if (zones != null)
        {
            zones.doorGlowColor = baseLook.doorGlowColor;
            zones.doorStoneColor = baseLook.doorStoneColor;
            zones.sceneAccent = baseLook.sceneAccent;
        }
        current = baseLook;
        HideLabel();
        if (logTimeline) Debug.Log("[LookTransition] restart: base look restored.", this);
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    static float Smoother(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
    static float EaseOut(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x; }   // slope 2 at the start, 0 at the end

    // ------------------------------------------------------------------ camera

    void BeginCamera()
    {
        Camera cam = rig.boomCamera;
        float liveYaw = rig.transform.eulerAngles.y;
        Vector3 dir = runner.transform.forward; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f && pathGen != null) dir = pathGen.PortalExitDir;
        dir.y = 0f;
        float heading = dir.sqrMagnitude > 1e-4f ? Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg : liveYaw;
        float y1 = heading + chaseYawOffset, y2 = heading - chaseYawOffset;
        chaseYaw = Mathf.Abs(Mathf.DeltaAngle(liveYaw, y1)) <= Mathf.Abs(Mathf.DeltaAngle(liveYaw, y2)) ? y1 : y2;
        float live = cam.orthographicSize;
        chaseOrtho = Mathf.Min(live, Mathf.Max(rig.AuthoredOrthoSize, live * chaseZoom));

        camActive = true;
        rig.SuppressYawChanges = true;
        if (zones != null) zones.freezeDoorView = true;
        runner.inputSuspended = true;
    }

    void ReleaseCamera()
    {
        camActive = false;
        if (rig != null) rig.SuppressYawChanges = false;
        if (zones != null) zones.freezeDoorView = false;
        if (runner != null) { runner.inputSuspended = false; runner.CinematicSpeedFactor = 1f; }
    }

    // Called by the rig at the end of its LateUpdate, after the active mode wrote this frame's pose: that pose is the live
    // target. Blend it with the chase pose by the phase weight and write the result.
    void OnCameraOverride(CameraParentRotator r)
    {
        if (!camActive) return;
        long g0 = System.GC.GetAllocatedBytesForCurrentThread();
        WriteCinematicPose(r);
        TransitionAllocatedBytes += System.GC.GetAllocatedBytesForCurrentThread() - g0;
    }

    void WriteCinematicPose(CameraParentRotator r)
    {
        float A = T.followThrough, B = T.reveal, C = T.riseBack;
        float w;
        if (t < A) w = chaseGlideEaseOut ? EaseOut(t / Mathf.Max(0.01f, A)) : Smoother(t / Mathf.Max(0.01f, A));
        else if (t < A + B) w = 1f;
        else if (t < A + B + C) w = 1f - Smoother((t - A - B) / Mathf.Max(0.01f, C));
        else { ReleaseCamera(); return; }   // the mode's own pose is already on screen this frame

        Transform rt = r.transform;
        Camera cam = r.boomCamera;
        Transform ct = cam.transform;
        float liveYaw = rt.eulerAngles.y;
        float liveTilt = ct.localEulerAngles.x;
        if (liveTilt > 180f) liveTilt -= 360f;
        float liveOrtho = cam.orthographicSize;
        Vector3 liveFocus = r.ViewFocus;
        float boom = Vector3.Distance(liveFocus, rt.position);
        if (boom < 1f) boom = r.boomDistance;

        float yaw = liveYaw + Mathf.DeltaAngle(liveYaw, chaseYaw) * w;
        float tilt = Mathf.Lerp(liveTilt, chaseTilt, w);
        float ortho = Mathf.Lerp(liveOrtho, chaseOrtho, w);
        Quaternion q = Quaternion.Euler(tilt, yaw, 0f);
        Vector3 fwd = q * Vector3.forward, right = q * Vector3.right, up = q * Vector3.up;

        // The runner's place on screen (in half-heights from the centre) is what blends, not the focus point: in the
        // live shot it is wherever the mode has it, in the chase shot centred low. Every frame in between therefore
        // has the runner on the straight screen path between two on-screen spots - it can never swing out of frame
        // while the camera orbits it. Depth along the view only decides clipping, and blends with the rest.
        Vector3 run = runner.transform.position;
        Quaternion qLive = Quaternion.Euler(liveTilt, liveYaw, 0f);
        Vector3 dl = run - liveFocus;
        float liveX = Vector3.Dot(dl, qLive * Vector3.right) / Mathf.Max(0.01f, liveOrtho);
        float liveY = Vector3.Dot(dl, qLive * Vector3.up) / Mathf.Max(0.01f, liveOrtho);
        float liveDepth = Vector3.Dot(-dl, qLive * Vector3.forward);
        float sx = Mathf.Lerp(liveX, 0f, w);
        float sy = Mathf.Lerp(liveY, -chaseRunnerBelowCentre, w);
        float depth = Mathf.Lerp(liveDepth, 0f, w);
        Vector3 focus = run - (right * sx + up * sy) * ortho + fwd * depth;

        rt.SetPositionAndRotation(focus - fwd * boom, Quaternion.Euler(0f, yaw, 0f));
        ct.localRotation = Quaternion.Euler(tilt, 0f, 0f);
        ct.localPosition = Vector3.Lerp(ct.localPosition, Vector3.zero, w);
        cam.orthographicSize = ortho;
    }

    // ------------------------------------------------------------------ label

    void BuildLabel()
    {
        labelRoot = new GameObject("LookTransition_Label");
        labelRoot.transform.SetParent(transform, false);
        var canvas = labelRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 101;
        var scaler = labelRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        // No GraphicRaycaster: the label can never take a touch.

        var safe = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeArea));
        safe.transform.SetParent(labelRoot.transform, false);
        labelGroup = safe.AddComponent<CanvasGroup>();
        labelGroup.interactable = false;
        labelGroup.blocksRaycasts = false;
        labelGroup.alpha = 0f;

        var mask = new GameObject("Reveal", typeof(RectTransform));
        mask.transform.SetParent(safe.transform, false);
        labelMaskRect = (RectTransform)mask.transform;
        labelMaskRect.anchorMin = new Vector2(0f, 1f);
        labelMaskRect.anchorMax = new Vector2(1f, 1f);
        labelMaskRect.pivot = new Vector2(0.5f, 1f);
        labelMaskRect.anchoredPosition = new Vector2(0f, -labelTop);
        labelMaskRect.sizeDelta = new Vector2(0f, labelFontSize * 1.8f);
        labelMask = mask.AddComponent<RectMask2D>();

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(mask.transform, false);
        labelText = textGo.AddComponent<Text>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        labelText.font = font;
        labelText.fontSize = labelFontSize;
        labelText.fontStyle = FontStyle.Bold;
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.color = new Color(1f, 0.96f, 0.98f, 0.92f);
        labelText.raycastTarget = false;
        labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
        var trt = labelText.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var shadow = textGo.AddComponent<Shadow>();
        shadow.effectDistance = new Vector2(2f, -2f);
        shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
        labelRoot.SetActive(false);
    }

    void ShowLabel(string text)
    {
        if (labelRoot == null) return;
        labelText.text = text;
        labelRoot.SetActive(true);
        labelGroup.alpha = 1f;
        labelTimer = -1f;
        RevealLabel(-1f);
    }

    // The label is uncovered by the same edge as the world: everything left of the wipe shows.
    void RevealLabel(float wipeX)
    {
        float w = labelMaskRect.rect.width;
        labelMask.padding = new Vector4(0f, 0f, Mathf.Clamp01(1f - wipeX) * w, 0f);
        labelMask.softness = new Vector2Int(Mathf.RoundToInt(wipeSoftness * w), 0);
    }

    void UpdateLabel()
    {
        if (labelRoot == null || !labelRoot.activeSelf || labelTimer < 0f) return;
        labelTimer += Time.unscaledDeltaTime;
        float fade = Mathf.Clamp01((labelTimer - labelHold) / Mathf.Max(0.01f, labelFade));
        labelGroup.alpha = 1f - Smooth(fade);
        if (fade >= 1f) HideLabel();
    }

    void HideLabel()
    {
        if (labelRoot == null) return;
        labelRoot.SetActive(false);
        labelTimer = -1f;
    }
}
