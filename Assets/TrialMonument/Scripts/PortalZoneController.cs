using UnityEngine;

/// <summary>
/// The zone change (TrialMonument2 and every scene with portalZones ticked). DynamicPathGenerator lays
/// the runway into a door AND, right away, the runway out of it too (DynamicPathGeneratorPortal.cs) -
/// this builds the two flat red doors (PortalDoor.cs) on both of them and retints the world to red as
/// the runner crosses.
///
///   Both exist   The entry door is built the moment its runway is laid; the exit door is built the
///              moment ITS runway is laid, which now happens right away too - so both doors, and the
///              short legs they stand on, already exist and can be seen together well before the
///              runner reaches the first one. Each grows in with the track beside it, the same way
///              everything else draws in: no camera pull, no speed lines, no vignette.
///   Through    A runner on the entry door's own face crosses it and is set down on the runway out -
///              already standing, a couple of cubes on - on whichever face was picked for it when it
///              was built. No cut, no screen effect, no pause: this is a normal step, like any other leg.
///   After      BOTH doors stay standing. The old runway (and the entry door on it) is left behind
///              exactly the way any two adjacent legs already coexist during an ordinary run:
///              PathDepthShading's own crossed-leg fade takes the old track transparent as the
///              runner's leg index moves past it, and the door follows the same crossed amount,
///              closing down in step with its track instead of needing its own effect.
///
/// A door's face is oriented toward the camera's own look direction (fixed at the moment it is built),
/// not its own leg's direction - otherwise a leg that happens to point toward/away from the fixed
/// isometric camera would foreshorten its door down to an unreadable sliver.
///
/// The palette is shader globals (MonumentBlock.shader / MonumentSky.shader) plus PathDepthShading's
/// ahead-gradient, so no material asset is touched. Every portal leads to the same red palette - this
/// is a colour change, not a light show.
/// </summary>
[DefaultExecutionOrder(500)] // after the camera rig has written this frame's size, before the sky reads it
public class PortalZoneController : MonoBehaviour
{
    [Header("References")]
    public DynamicPathGenerator pathGen;
    public CubeRailRunner runner;
    public CameraParentRotator cameraRig;
    public PathDepthShading shading;
    [Tooltip("M_MonumentBlock: read for the opening zone's shade colour.")]
    public Material blockMaterial;
    [Tooltip("M_MonumentSky: read for the opening zone's sky colours.")]
    public Material skyMaterial;

    [Header("Tower doors (MainScene prop only - off everywhere else)")]
    [Tooltip("Build each door as the crenellated tower of the Monument Valley 2 reference round one cube: a square body with a round-arched doorway through it, battlements, an onion dome with a finial and steps on one flank, with a recessed void in the doorway. The entry stands on the last cube of a dead-end runway; the exit stands on the first cube of the separate island runway and glows, with a fan of light rays. Both stand on the TOP face. Off = the solid slab.")]
    public bool monumentDoors = false;
    [Tooltip("The exit doorway, its light rays, the pool of light and the motes.")]
    public Color doorGlowColor = new Color(1f, 0.82f, 0.45f, 1f);
    [Tooltip("M_PortalVoid (TrialMonument/PortalVoid): the opaque, depth-writing hole. A copy is made per door.")]
    public Material voidMaterial;
    [Tooltip("Cubes past the entry door's void plane the runner's centre must be before it counts as swallowed. 0.5 = its tail has just cleared the plane; the plane is recessed inside the arch so this is still over the last cube.")]
    [Range(0.4f, 1f)] public float entrySwallowDepth = 0.5f;
    [Tooltip("Cubes BEHIND the exit cube's centre the runner is set down, so it starts wholly behind the exit door's void plane and drives out through it.")]
    [Range(0f, 0.5f)] public float exitBehindCubes = 0.25f;

    [Header("Look scenes (MainScene prop camera 1-3 only - off everywhere else)")]
    [Tooltip("Keep the scene's own palette through every zone door instead of switching the world to red.")]
    public bool keepScenePalette = false;
    [Tooltip("Accent (runner trail, boost FX) of the scene's own palette. Alpha 0 = the pale cyan default.")]
    public Color sceneAccent = new Color(0.55f, 0.95f, 1f, 0f);
    [Tooltip("Stone colour of the zone-door towers. Alpha 0 = the red palette's accent, as before.")]
    public Color doorStoneColor = new Color(1f, 0f, 0f, 0f);

    static readonly int ZoneBlendId = Shader.PropertyToID("_ZoneBlend");
    static readonly int ZoneShadowId = Shader.PropertyToID("_ZoneShadowColor");
    static readonly int ZoneEdgeId = Shader.PropertyToID("_ZoneEdgeColor");
    static readonly int SkyTopId = Shader.PropertyToID("_ZoneSkyTop");
    static readonly int SkyMidId = Shader.PropertyToID("_ZoneSkyMid");
    static readonly int SkyBottomId = Shader.PropertyToID("_ZoneSkyBottom");
    static readonly int SkyGlowId = Shader.PropertyToID("_ZoneSkyGlow");
    static readonly int StarId = Shader.PropertyToID("_ZoneStarColor");
    // Sentinel values only - no sweep/wipe is ever animated any more, this just keeps the shared
    // shaders (read by TrialMonument and everything else) at the "no transition in flight" state.
    static readonly int WipeXId = Shader.PropertyToID("_ZoneWipeX");
    static readonly int GoneShadowId = Shader.PropertyToID("_ZoneGoneShadow");

    // ---- runtime ----
    ZonePalette opening, current, redPalette;
    PortalDoor entryDoor, exitDoor;
    DynamicPathGenerator.Segment builtEntryFor, builtExitFor;
    GameObject entryProbeCube, exitProbeCube;
    int pendingExitSide;
    Transform pendingExitCube;
    bool changing;

    readonly System.Collections.Generic.List<MeshRenderer> runnerMeshes = new System.Collections.Generic.List<MeshRenderer>();
    TrailRenderer runnerTrail;
    RunnerBoostFX boostFx;
    RunnerTrailFX trailFx;
    readonly GradientColorKey[] colorKeys = new GradientColorKey[4];
    readonly GradientAlphaKey[] alphaKeys = { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };

    public int ZoneNumber => (pathGen != null ? pathGen.PortalsTaken : 0) + 1;

    // ---- LookTransition hooks (MainScene prop camera 3.2 only; nothing subscribes anywhere else) ----
    /// <summary>Raised at the end of a crossing, after the runner is set down on the runway out and the palette applied. Argument: doors taken so far (1 = the first).</summary>
    public event System.Action<int> DoorCrossed;
    /// <summary>Raised when a soft restart puts the opening palette back (ResetToOpening).</summary>
    public event System.Action OpeningRestored;
    public PortalDoor ExitDoor => exitDoor;
    public PortalDoor EntryDoor => entryDoor;
    /// <summary>While true the doors keep the view they were last given (their stairs flank and void do not follow the camera). Runtime only, default off.</summary>
    [System.NonSerialized] public bool freezeDoorView;

    // ------------------------------------------------------------------ setup

    void Awake()
    {
        if (pathGen == null) pathGen = GetComponent<DynamicPathGenerator>();
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        // Before the generator's Start builds the path: this scene wants zone doors.
        if (pathGen != null) pathGen.portalZones = true;
    }

    void Start()
    {
        if (runner == null) runner = FindFirstObjectByType<CubeRailRunner>();
        if (cameraRig == null) cameraRig = FindFirstObjectByType<CameraParentRotator>();
        if (shading == null && pathGen != null) shading = pathGen.GetComponent<PathDepthShading>();

        if (runner != null)
        {
            runnerTrail = runner.GetComponentInChildren<TrailRenderer>(true);
            boostFx = runner.GetComponent<RunnerBoostFX>();
            trailFx = runner.GetComponent<RunnerTrailFX>();
            foreach (MeshRenderer r in runner.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name == "Cube") runnerMeshes.Add(r);
            }
        }

        redPalette = ZonePalette.FromHue("Red", 0f, 1.15f);
        opening = CaptureOpeningPalette();

        ApplyPalette(opening);
        Shader.SetGlobalFloat(WipeXId, 2f);
        Shader.SetGlobalFloat(GoneShadowId, 0f);
    }

    void OnDisable()
    {
        // Leave the shared shaders as authored: TrialMonument and everything else read them too.
        Shader.SetGlobalFloat(ZoneBlendId, 0f);
        Shader.SetGlobalFloat(WipeXId, 2f);
        Shader.SetGlobalFloat(GoneShadowId, 0f);
        if (entryDoor != null) Destroy(entryDoor.gameObject);
        if (exitDoor != null) Destroy(exitDoor.gameObject);
    }

    /// <summary>Soft restart: drop any zone change in flight and go back to the opening (scene default) palette.</summary>
    public void ResetToOpening()
    {
        changing = false;
        if (entryDoor != null) { Destroy(entryDoor.gameObject); entryDoor = null; }
        if (exitDoor != null) { Destroy(exitDoor.gameObject); exitDoor = null; }
        builtEntryFor = null;
        builtExitFor = null;
        entryProbeCube = null;
        exitProbeCube = null;
        pendingExitCube = null;
        pendingExitSide = 0;
        if (opening == null) return;
        ApplyPalette(opening);
        Shader.SetGlobalFloat(WipeXId, 2f);
        Shader.SetGlobalFloat(GoneShadowId, 0f);
        OpeningRestored?.Invoke();
    }

    // The scene as authored is zone 1, so the first stretch looks exactly like TrialMonument.
    ZonePalette CaptureOpeningPalette()
    {
        var p = new ZonePalette { name = "Pale Cyan", accent = new Color(0.55f, 0.95f, 1f, 1f) };
        if (sceneAccent.a > 0f) p.accent = new Color(sceneAccent.r, sceneAccent.g, sceneAccent.b, 1f);
        if (shading != null)
        {
            Gradient g = shading.aheadGradient;
            p.n0 = g.Evaluate(ZonePalette.GradientTimes[0]);
            p.n1 = g.Evaluate(ZonePalette.GradientTimes[1]);
            p.n2 = g.Evaluate(ZonePalette.GradientTimes[2]);
            p.n3 = g.Evaluate(ZonePalette.GradientTimes[3]);
            p.travelled = shading.travelledColor;
        }
        p.shadow = blockMaterial != null ? blockMaterial.GetColor("_ShadowColor") : new Color(0.76f, 0.85f, 0.93f);
        p.edge = blockMaterial != null ? blockMaterial.GetColor("_EdgeColor") : Color.white;
        if (skyMaterial != null)
        {
            p.skyTop = skyMaterial.GetColor("_TopColor");
            p.skyMid = skyMaterial.GetColor("_MidColor");
            p.skyBottom = skyMaterial.GetColor("_BottomColor");
            p.skyGlow = skyMaterial.GetColor("_GlowColor");
            p.star = skyMaterial.GetColor("_StarColor");
        }
        return current = p;
    }

    ZonePalette PaletteFor(int zoneIndex) => zoneIndex <= 0 || keepScenePalette ? opening : redPalette;

    Color DoorStone => doorStoneColor.a > 0f ? new Color(doorStoneColor.r, doorStoneColor.g, doorStoneColor.b, 1f) : redPalette.accent;

    // The palette in force: shader globals, the shading gradient and the runner's FX.
    void ApplyPalette(ZonePalette p)
    {
        current = p;
        Shader.SetGlobalFloat(ZoneBlendId, 1f);
        Shader.SetGlobalColor(ZoneShadowId, p.shadow);
        Shader.SetGlobalColor(ZoneEdgeId, p.edge);
        Shader.SetGlobalColor(SkyTopId, p.skyTop);
        Shader.SetGlobalColor(SkyMidId, p.skyMid);
        Shader.SetGlobalColor(SkyBottomId, p.skyBottom);
        Shader.SetGlobalColor(SkyGlowId, p.skyGlow);
        Shader.SetGlobalColor(StarId, p.star);

        if (shading != null)
        {
            colorKeys[0] = new GradientColorKey(p.n0, ZonePalette.GradientTimes[0]);
            colorKeys[1] = new GradientColorKey(p.n1, ZonePalette.GradientTimes[1]);
            colorKeys[2] = new GradientColorKey(p.n2, ZonePalette.GradientTimes[2]);
            colorKeys[3] = new GradientColorKey(p.n3, ZonePalette.GradientTimes[3]);
            shading.aheadGradient.SetKeys(colorKeys, alphaKeys);
            shading.travelledColor = p.travelled;
        }

        if (boostFx != null)
        {
            boostFx.streakColor = p.accent;
            boostFx.glowColor = p.accent * 1.15f;
        }
        if (trailFx != null)
        {
            trailFx.headColor = Color.Lerp(p.accent, Color.white, 0.6f);
            trailFx.midColor = p.accent;
            trailFx.tailColor = Color.Lerp(p.accent, p.skyTop, 0.55f);
            trailFx.fastHeadColor = Color.Lerp(p.accent, Color.white, 0.75f);
            trailFx.fastMidColor = p.accent;
            trailFx.fastTailColor = Color.Lerp(p.accent, p.skyTop, 0.4f);
        }
    }

    // The world-space outward direction of a cube's face: 0=Top,1=Right,2=Bottom,3=Left, matching
    // CubeRailRunner's own currentSideIndex convention (PlayerController.cs).
    static Vector3 SideUpWorld(Transform cube, int side) => side switch
    {
        1 => cube.right,
        2 => -cube.up,
        3 => -cube.right,
        _ => cube.up,
    };

    // The camera's own horizontal look direction, fixed at the moment a door is built: orienting a
    // door's face this way (rather than along its own leg's direction) keeps it reading as a clear
    // wide rectangle no matter which way that particular leg happens to run - a leg pointed toward or
    // away from the fixed isometric camera would otherwise foreshorten the door to a sliver.
    Vector3 BillboardForward(Vector3 sideUp, Vector3 fallback)
    {
        Camera cam = cameraRig != null && cameraRig.boomCamera != null ? cameraRig.boomCamera : Camera.main;
        Vector3 f = cam != null ? cam.transform.forward : fallback;
        f.y = 0f;
        if (f.sqrMagnitude < 0.0001f) f = fallback;
        else f.Normalize();
        // Guard against the (rare) case where this ends up parallel to the face's own up axis, which
        // would make LookRotation degenerate.
        return Mathf.Abs(Vector3.Dot(f, sideUp.normalized)) > 0.98f ? fallback : f;
    }

    // The archways' void hides the runner, which is only right with the camera on the door's front (see
    // PortalDoor.SetView). The generator forces the camera's yaw for the two door zones; this is the
    // guard for when it could not.
    void FeedCameraToDoors()
    {
        Camera cam = cameraRig != null && cameraRig.boomCamera != null ? cameraRig.boomCamera : Camera.main;
        if (cam == null) return;
        Vector3 f = cam.transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.0001f) return;
        f.Normalize();
        if (entryDoor != null) entryDoor.SetView(f);
        if (exitDoor != null) exitDoor.SetView(f);
    }

    // ------------------------------------------------------------------ per frame

    void Update()
    {
        if (pathGen == null || runner == null) return;

        EnsureEntryDoor();
        EnsureExitDoor();
        if (monumentDoors && !freezeDoorView) FeedCameraToDoors();
        if (!changing) CheckCrossing();
        UpdateDoorFade();
    }

    // The door is built the moment its runway is laid, but it only shows as much as the drawn track
    // beside it, so it grows in with the track like everything else and never pops. It stands on
    // whichever face of the beam the runner is currently running on.
    void EnsureEntryDoor()
    {
        DynamicPathGenerator.Segment entry = pathGen.PortalEntrySegment;
        if (entry == null || entry.cubes.Count == 0) return;

        if (entryDoor == null && entry != builtEntryFor)
        {
            float u = pathGen.cubeSize;
            Vector3 dir = pathGen.PortalEntryDir;
            // The archway always stands on the top face (it is a building): the runner must be up there.
            int side = monumentDoors ? 0 : runner.currentSideIndex;
            Transform lastCube = entry.cubes[entry.cubes.Count - 1].transform;
            Vector3 sideUp = SideUpWorld(lastCube, side);
            // Archway: built round the last cube of the dead-end runway, facing the way the runner goes.
            // The slab is turned toward the camera instead.
            Vector3 forward = monumentDoors ? dir : BillboardForward(sideUp, dir);
            entryDoor = PortalDoor.Create(lastCube, lastCube.position, forward, sideUp, side, u, monumentDoors, false, voidMaterial);
            entryDoor.SetColor(DoorStone);
            entryDoor.SetGlowColor(doorGlowColor);
            entryDoor.SetReveal(0f);
            builtEntryFor = entry;
            entryProbeCube = lastCube.gameObject;
            pathGen.PortalEntrySide = side;
        }

        if (entryDoor == null) return;

        // The last cubes of a leg are the faded edge of what is drawn, so the door follows the cube one
        // fade-length back: it is fully there once the track is fully drawn up to it.
        int fade = shading != null ? Mathf.CeilToInt(shading.edgeFadeCubes) : 0;
        GameObject probe = entry.cubes[Mathf.Max(0, entry.cubes.Count - 1 - fade)];
        entryDoor.SetReveal(shading != null ? shading.ShownAmount(probe) : 1f);
    }

    // The exit door is now built just as eagerly, on the runway out - which DynamicPathGenerator lays
    // right away too - so both doors exist and can be seen together well before the runner reaches
    // the first one. Its side is picked once, at build time, and held (PlaceRunner uses the same side
    // later, when the door is actually crossed).
    void EnsureExitDoor()
    {
        DynamicPathGenerator.Segment exit = pathGen.PortalExitSegment;
        if (exit == null || exit.cubes.Count == 0) return;

        if (exitDoor == null && exit != builtExitFor)
        {
            float u = pathGen.cubeSize;
            Transform cube0 = exit.cubes[0].transform;
            // The archway stands upright on top; the slab takes any face, independent of the entry's.
            int side = monumentDoors ? 0 : Random.Range(0, 4);
            Vector3 sideUp = SideUpWorld(cube0, side);
            // Archway: built round cube 0, the start of the island runway, facing the way the runner will
            // go. The runner is set down inside it, behind its void, and drives out.
            Vector3 forward = monumentDoors ? exit.direction : BillboardForward(sideUp, exit.direction);
            exitDoor = PortalDoor.Create(cube0, cube0.position, forward, sideUp, side, u, monumentDoors, true, voidMaterial);
            exitDoor.SetColor(DoorStone);
            exitDoor.SetGlowColor(doorGlowColor);
            exitDoor.SetReveal(0f);
            builtExitFor = exit;
            exitProbeCube = cube0.gameObject;
            pendingExitSide = side;
            pendingExitCube = cube0;
        }

        if (exitDoor == null) return;

        int fade = shading != null ? Mathf.CeilToInt(shading.edgeFadeCubes) : 0;
        GameObject probe = exit.cubes[Mathf.Min(fade, exit.cubes.Count - 1)];
        exitDoor.SetReveal(shading != null ? shading.ShownAmount(probe) : 1f);
    }

    [Tooltip("Also check the door crossing every physics step (3.11). The frame check alone can miss it: a long frame (a hitch at speed, a sped-up test) moves the runner several steps, so it can run off the end of the door's runway and die before the next frame looks. Off = the old frame-only check.")]
    public bool crossEveryPhysicsStep = false;
    DynamicPathGenerator.Segment crossedEntry;

    void FixedUpdate()
    {
        if (crossEveryPhysicsStep && pathGen != null && runner != null && !changing) CheckCrossing();
    }

    void CheckCrossing()
    {
        DynamicPathGenerator.Segment entry = pathGen.PortalEntrySegment;
        bool live = !runner.IsGameOver;
        bool onEntry = live && entryDoor != null && entry != null && pathGen.PlayerSegment == entry;
        if (!onEntry) return;
        if (entry == crossedEntry) return;   // this door was already crossed (the physics-step check can run again before the generator moves on)

        Vector3 dir = pathGen.PortalEntryDir;
        float dist = Vector3.Dot(entryDoor.ThresholdCentre - runner.transform.position, dir);

        // The slab is crossed at its plane. The archway waits until the runner is wholly behind its void
        // (centre entrySwallowDepth cubes past the plane, tail clear of it), so the last thing seen is the
        // runner going into the hole and the next frame it is coming out of the other one.
        float swallow = monumentDoors ? entrySwallowDepth * pathGen.cubeSize : 0f;

        // The door only lets through a runner on its own face: on any other face it misses, flies on
        // and the run is lost.
        if (dist <= -swallow && runner.currentSideIndex == entryDoor.Side)
            CrossDoor();
    }

    // ------------------------------------------------------------------ the crossing

    /// <summary>
    /// Checkpoint start (MainScene prop camera 3.6, DynamicPathGenerator.checkpointStartPortal): the generator has laid only
    /// the runway out of the door; this builds its tower now and plays the very same crossing a runner going through the
    /// entry door gets (counted, set down behind the exit void, palette, DoorCrossed). False if there is no runway out.
    /// Nothing calls it in any other scene.
    /// </summary>
    public bool CrossFromCheckpoint()
    {
        if (pathGen == null || runner == null || changing) return false;
        EnsureExitDoor();
        if (pendingExitCube == null) return false;
        CrossDoor();
        return true;
    }

    void CrossDoor()
    {
        if (changing) return;
        if (pendingExitCube == null)
        {
            Debug.LogWarning("[PortalZoneController] the runway out of the door was not ready when it was crossed.");
            return;
        }
        changing = true;
        crossedEntry = pathGen.PortalEntrySegment;

        Rigidbody rb = runner.GetComponent<Rigidbody>();
        Transform cube0 = pendingExitCube;
        Vector3 sideUp = SideUpWorld(cube0, pendingExitSide);

        pathGen.PortalCrossed();   // releases the hold so ordinary generation carries on past the runway out
        PlaceRunner(rb, cube0, pendingExitSide, sideUp);
        Haptics.LightImpact();

        // Just a colour change - the same call the game already uses to retint the environment,
        // without any sweep/curtain choreography around it.
        ApplyPalette(PaletteFor(pathGen.PortalsTaken));

        changing = false;
        DoorCrossed?.Invoke(pathGen.PortalsTaken);
    }

    // Sets the runner down on the chosen face of the first cube of the runway out, facing along it.
    // Its speed is untouched.
    void PlaceRunner(Rigidbody rb, Transform cube0, int side, Vector3 worldUp)
    {
        runner.enabled = false;
        runner.currentCube = cube0;
        runner.isFlying = false;
        runner.currentSideIndex = side;

        float offset = (0.5f + 0.5f + 0.05f) * pathGen.firstCube.lossyScale.x;
        Vector3 pos = cube0.position + worldUp * offset;
        // Archway: set down a little behind cube 0's centre, wholly behind the exit door's void plane.
        if (monumentDoors) pos -= cube0.forward * (exitBehindCubes * pathGen.cubeSize);
        Quaternion rot = Quaternion.LookRotation(cube0.forward, worldUp);

        rb.isKinematic = false;
        runner.transform.SetPositionAndRotation(pos, rot);
        rb.position = pos;
        rb.rotation = rot;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();

        // Taps made just before the door would otherwise fire on the first frame out.
        try
        {
            var f = typeof(CubeRailRunner).GetField("inputQueue",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var q = f != null ? f.GetValue(runner) as System.Collections.Generic.Queue<int> : null;
            q?.Clear();
        }
        catch { /* only a stale tap survives */ }

        if (runnerTrail != null) runnerTrail.Clear();
        foreach (MeshRenderer r in runnerMeshes) if (r != null) r.enabled = true;
        runner.enabled = true;
    }

    // ------------------------------------------------------------------ leaving the doors behind

    // Both doors stay standing until the leg they're on is genuinely left behind, exactly like any
    // other track: fade with it (PathDepthShading.CrossedAmount), then go.
    void UpdateDoorFade()
    {
        if (shading == null) return;

        if (entryDoor != null)
        {
            float amount = entryProbeCube != null ? shading.CrossedAmount(entryProbeCube) : 0f;
            entryDoor.SetCrossed(amount);
            if (amount >= 0.999f)
            {
                Destroy(entryDoor.gameObject);
                entryDoor = null;
                entryProbeCube = null;
            }
        }

        if (exitDoor != null)
        {
            float amount = exitProbeCube != null ? shading.CrossedAmount(exitProbeCube) : 0f;
            exitDoor.SetCrossed(amount);
            if (amount >= 0.999f)
            {
                Destroy(exitDoor.gameObject);
                exitDoor = null;
                exitProbeCube = null;
            }
        }
    }
}
