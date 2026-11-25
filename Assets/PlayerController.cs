using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public class CubeRailRunner : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Live speed. DynamicPathGenerator writes it through SetLegSpeed every time a leg is reached, so anything typed here is overwritten at run time.")]
    public float forwardSpeed = 14f;
    [Tooltip("Mirror of Hard Max Speed (x speed scale), kept for the camera's speed zoom. Set through SetLegSpeed.")]
    public float maxSpeed = 42f;
    public float sideSwitchSpeed = 50f; // Significantly increased

    [Header("Speed Curve (by leg reached)")]
    [Tooltip("Speed on leg 0, in world units per second.")]
    [SerializeField] private float startSpeed = 14f;
    [Tooltip("The human ceiling. No leg, however deep, is ever faster than this.")]
    [SerializeField] private float hardMaxSpeed = 42f;
    [Tooltip("Scales the start speed and the ceiling: 0.85 = 85% of the authored 14 / 42 u/s.")]
    [SerializeField] private float overallSpeedMultiplier = 0.85f;
    [Tooltip("Scales how fast speed is gained per leg, on its own: 0.95 = 95% of the authored curve's gains.")]
    [SerializeField] private float rampRateMultiplier = 0.95f;
    [Tooltip("How far along the way from Start Speed to Hard Max Speed the runner is (0..1), against leg / Curve Legs. Hook: +0.3 u/s per leg to leg 25. Grind: +0.2 per leg to leg 60. Limit: flattening to leg 100.")]
    [SerializeField] private AnimationCurve speedCurve = DefaultSpeedCurve();
    [Tooltip("The leg count the curve's horizontal axis spans (1.0 on the curve = this leg).")]
    [SerializeField] private float curveLegs = 100f;
    [Tooltip("Past Curve Legs the speed closes on Hard Max Speed exponentially. This is the e-folding length in legs: bigger = a slower creep. It approaches the ceiling and never reaches it.")]
    [SerializeField] private float tailLegs = 150f;
    [Tooltip("Multiplies every speed above. DynamicPathGenerator sets it to its sizeMultiplier so a leg takes as long to cross when the world is built bigger.")]
    public float speedScale = 1f;

    // Normalised over (speed - start) / (hardMax - start) for start 14 / max 42:
    // 0 -> 0, leg 25 -> 7.5/28, leg 60 -> 14.5/28, leg 100 -> 19/28 (33 u/s). Tangents are per 1.0 of x (= 100 legs).
    static AnimationCurve DefaultSpeedCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f,    0f,      0f,      1.0714f),
            new Keyframe(0.25f, 0.2679f, 1.0714f, 0.7143f),
            new Keyframe(0.60f, 0.5179f, 0.7143f, 0.7143f),
            new Keyframe(1.00f, 0.6786f, 0.2143f, 0.2143f));
    }

    /// <summary>The forward speed for a given leg count. Pure: same leg, same speed.</summary>
    public float SpeedForLeg(int leg)
    {
        leg = Mathf.Max(0, leg);
        // DifficultyProfile: hold for the opening legs, then an exponential close on vCap (no steps).
        if (profile != null && profile.useProfileSpeedCurve && !profile.steppedSpeed)
            return profile.SpeedForLeg(leg) * Mathf.Max(0.01f, speedScale);
        float sm = Mathf.Max(0.01f, overallSpeedMultiplier);
        float eStart = startSpeed * sm, eMax = hardMaxSpeed * sm;
        float span = Mathf.Max(0f, eMax - eStart);
        // Gains come out at rampRateMultiplier x the authored curve's, against a start and ceiling scaled by sm.
        float k = rampRateMultiplier / sm;
        float span01 = Mathf.Clamp(speedCurve.Evaluate(1f) * k, 0f, 0.99f);
        float n;
        if (leg <= curveLegs)
            n = Mathf.Clamp01(speedCurve.Evaluate(leg / Mathf.Max(1f, curveLegs)) * k);
        else
            n = span01 + (1f - span01) * (1f - Mathf.Exp(-(leg - curveLegs) / Mathf.Max(1f, tailLegs)));
        return (eStart + span * n) * Mathf.Max(0.01f, speedScale);
    }

    /// <summary>Sets forwardSpeed (and the mirrored cap) for the leg the runner has just reached.</summary>
    public void SetLegSpeed(int leg)
    {
        float target = SpeedForLeg(leg);
        maxSpeed = hardMaxSpeed * Mathf.Max(0.01f, overallSpeedMultiplier) * Mathf.Max(0.01f, speedScale);
        if (profile != null && profile.useProfileSpeedCurve && !profile.steppedSpeed)
            maxSpeed = profile.vCap * Mathf.Max(0.01f, speedScale);

        // DifficultyProfile: a new leg's speed is eased in over speedEaseSeconds so it never jolts (once running).
        if (profile != null && profile.speedEaseSeconds > 0f && runState == RunState.Playing && target > forwardSpeed)
        {
            speedEaseFrom = forwardSpeed;
            speedEaseTarget = target;
            speedEaseT = 0f;
            return;
        }
        speedEaseT = -1f;
        forwardSpeed = target;
    }

    // ---- DifficultyProfile (fair input, exact corners, close-call rescue). Everything here is inert while
    // profile is null, which it is in every scene whose DynamicPathGenerator has no profile assigned. ----
    private DifficultyProfile profile;
    private float speedEaseFrom, speedEaseTarget, speedEaseT = -1f;
    public DifficultyProfile Profile => profile;
    bool FairInput => profile != null && profile.fairInput;
    bool FairCorners => profile != null && profile.fairCorners;
    bool FlipRemapOn => flipRemapsControls && !(FairInput && profile.disableFlipRemap);

    /// <summary>Called by DynamicPathGenerator.Start when its scene has a DifficultyProfile.</summary>
    public void ApplyProfile(DifficultyProfile p, DynamicPathGenerator gen)
    {
        profile = p;
        if (gen != null) pathGen = gen;
        SetLegSpeed(0);
        startForwardSpeed = forwardSpeed;
    }

    /// <summary>Live mechanical timings, for the time-budget floors and the human sim.</summary>
    public float LiveRollTime => StepRevolveDuration;
    public float LiveRollCooldown => StepCooldown;
    public float LiveLandingLock => FairInput ? profile.landingLock : 0.1f;

    /// <summary>A rescue happened: what kind, how late the input was (s), how far past the corner (cubes).</summary>
    public struct RescueInfo { public bool plank; public float lateSeconds; public float overshootCubes; public bool onBoost; public Vector3 position; }
    public event System.Action<RescueInfo> Rescued;
    /// <summary>A screen tap (-1 / +1) was taken in (after the tutorial filter). For telemetry and the human sim.</summary>
    public event System.Action<int> TapTaken;
    /// <summary>The runner landed on a new leg (any route).</summary>
    public event System.Action Landed;
    public float LastLandingTime { get; private set; } = -10f;
    public float LastViewChangeTime { get; private set; } = -10f;
    public float LastRemapChangeTime { get; private set; } = -10f;
    public float LastExecutedTapTime { get; private set; } = -10f;
    public float LastDroppedTapTime { get; private set; } = -10f;
    /// <summary>Why the last run ended: corner, plank, decoy, portal, obstacle or offtrack.</summary>
    public string LastDeathCause { get; private set; } = "";
    /// <summary>Seconds by which the last corner was missed when it ended in a fall (for NEAR_LATE).</summary>
    public float LastMissTime { get; private set; } = -10f;
    public int TapsThisLeg { get; private set; }
    public float FirstTapAfterLanding { get; private set; } = -1f;
    public int RescuesThisRun { get; private set; }
    public int CornersMadeThisRun { get; private set; }
    public LayerMask groundLayer;

    [Header("Input")]
    [Tooltip("Left/right (keys, taps and swipes) mean left/right on the SCREEN, whichever way the runner faces the camera. Off = the old fixed roll direction.")]
    public bool cameraRelativeControls = true;
    [Tooltip("Opt-in (default off, so other scenes are unchanged). Maps left/right by dotting the cube's face normals against the camera's right axis instead of the swing projection, and turns off the double-tap 180 flip: two quick taps are two normal 90 degree rolls.")]
    public bool screenAxisControls = false;
    [Tooltip("Opt-in (default off, so other scenes are unchanged). Once the runner has settled on the top<->bottom opposite of the face it landed the leg on, for flipRemapDelay seconds, left/right swap meaning. Going back to the landing face swaps them back. Sideways (left/right face) flips never swap.")]
    public bool flipRemapsControls = false;
    public float flipRemapDelay = 0.2f;
    [Tooltip("Opt-in (default off; on only in MainScene prop camera 3.2). Needs screenAxisControls + flipRemapsControls. On a left/right FACE, where the screen cannot say which of the two rolls (up or down) is left or right, then after flipRemapDelay (scaled by Time.timeScale) settled there, LEFT/RIGHT mean the runner's own left/right-hand roll. Back on a top/bottom face the screen mapping is re-derived.")]
    public bool sideFaceRemap = false;

    // Legacy left/right fixes (screenAxisControls path). Each is opt-in, default off; all four are on only in MainScene prop camera 3.4.
    [Tooltip("Opt-in (default off; on only in MainScene prop camera 3.4). On a LEFT/RIGHT face where both rolls land at the same screen x, RIGHT = the runner's own clockwise roll (raw +1) from the moment of landing, instead of the HEADING rule for the first flipRemapDelay. The HEADING rule never runs (logged if anything reaches it).")]
    public bool sideWallRunnerRule = false;
    [Tooltip("Opt-in (default off; on only in MainScene prop camera 3.4). The side-face remap never overwrites a direction already used on this leg: it only sets RIGHT = clockwise while nothing has been pressed and the leg's sign is still undecided. Leaving the side wall keeps the sign (settleRederiveClearFaces re-derives it once settled).")]
    public bool keepSpinOnSideWall = false;
    [Tooltip("Opt-in (default off; on only in MainScene prop camera 3.4). After flipRemapDelay on a face with nothing queued or rolling, if the screen clearly separates the two rolls there, the leg's sign is cleared so the next press re-decides it with the screen-x rule (BOTTOM after a flip, faces of vertical legs). Presses faster than flipRemapDelay keep spinning the same way.")]
    public bool settleRederiveClearFaces = false;
    [Tooltip("Opt-in (default off; on only in MainScene prop camera 3.4). Left/right are decided against the camera as it is drawn at that physics step, not the camera's settled (end-of-swing) view. With settleRederiveClearFaces, a swing that ends while the runner is settled re-derives clear faces.")]
    public bool useDrawnViewForMapping = false;

    public KeyCode leftKey = KeyCode.A;
    public KeyCode rightKey = KeyCode.D;
    public KeyCode altLeftKey = KeyCode.LeftArrow;
    public KeyCode altRightKey = KeyCode.RightArrow;

    private Rigidbody rb;
    public Transform currentCube;
    public int currentSideIndex = 0; // 0=Top, 1=Right, 2=Bottom, 3=Left
    public bool isFlying = false;
    private float switchCooldown = 0f;
    private readonly Queue<int> inputQueue = new Queue<int>();
    // When each queued input was given (Time.time). Kept beside inputQueue; other scripts that clear inputQueue
    // by reflection are caught by the count check in Enqueue / DequeueInput.
    private readonly Queue<float> inputTimes = new Queue<float>();
    private int enqueuedCount, dequeuedCount;   // serials, so a touch can find its own tap in the queue
    private const int MaxQueuedInputs = 5;
    private DynamicPathGenerator pathGen; // for the corner grace (which face opens toward the next leg)

    // Touch / swipe
    private const float SwipeThreshold = 50f;
    private const float FlickThreshold = 40f; // px; flick fires as soon as the finger travels this far
    private Vector2 touchStartPos;
    private bool touchActive = false;
    private bool flickFired = false;

    // Queue codes: -1 = left, 1 = right, FlipCode = flip to opposite face, DoubleTapCode = flip undoing the first tap
    private const int FlipCode = 2;
    private const int DoubleTapCode = 3;
    private const int RawRollCode = 10; // +/-10 = roll right/left in the runner's frame, no screen mapping (QueueRoll)

    // Double-tap: same direction pressed twice within this window flips to the opposite face
    private const float DoubleTapWindow = 0.5f; // doubled from 0.25: more grace between the two taps
    private int lastTapDir = 0;
    private float lastTapTime = -10f;
    private bool lastTapExecuted = true;
    private int sideBeforeLastTap = 0;
    private bool FlickMode => !forceTapMode && ControlSettings.Mode == ControlMode.Flick; // TUTORIAL ADDITION (forceTapMode)

    // ---- TUTORIAL ADDITION: hooks used by TutorialManager. All are inert (null / false) outside the tutorial. ----
    /// <summary>Called with the screen direction (-1 left, +1 right) of every tap. Return false to swallow it.</summary>
    public System.Func<int, bool> tutorialInputFilter;
    /// <summary>The tutorial teaches taps, so it forces tap mode whatever the saved control setting is.</summary>
    public bool forceTapMode;
    /// <summary>Falling off never ends the run (the tutorial resets the runner itself).</summary>
    public bool suppressGameOver;
    /// <summary>The tutorial reads the touches itself (every finger, same tap rules) and feeds them in through SimulateTap.</summary>
    [System.NonSerialized] public bool tutorialOwnsTouch;
    /// <summary>Seconds the runner has been off the path (0 while on it).</summary>
    public float FlyingSeconds => isFlying ? flyingTimer : 0f;

    /// <summary>The face (0 top, 1 right, 2 bottom, 3 left) of the current cube that points most toward worldDir.</summary>
    public int SideFacing(Vector3 worldDir)
    {
        if (currentCube == null) return currentSideIndex;
        Vector3 l = currentCube.InverseTransformDirection(worldDir);
        if (Mathf.Abs(l.y) > Mathf.Abs(l.x)) return l.y > 0 ? 0 : 2;
        return l.x > 0 ? 1 : 3;
    }

    /// <summary>Feeds a screen tap (-1 left, +1 right) through the normal tap path (filter, double-tap detection, queue). For tests and tools.</summary>
    public void SimulateTap(int dir) => EnqueueTap(dir);

    /// <summary>World normal of a face of the current cube.</summary>
    public Vector3 SideNormal(int side) => GetFaceNormalWorld(((side % 4) + 4) % 4);

    /// <summary>
    /// The on-screen tap (-1 left, +1 right) that rolls the runner ONE face onto targetSide, exactly as a real
    /// tap would (same screen mapping as ChangeSide). 0 = already there, or the face is not one roll away.
    /// </summary>
    public int TapDirectionToReach(int targetSide)
    {
        if (currentCube == null || targetSide == currentSideIndex) return 0;
        mapPeek = MapFixesOn;   // Legacy fixes: a look, never a commit of the leg's sign
        try
        {
            for (int d = -1; d <= 1; d += 2)
            {
                Vector3 fwd = currentCube.forward;
                Vector3 n = GetFaceNormalWorld(currentSideIndex);
                int raw = ScreenRelativeDir(d, fwd, n);
                Vector3 local = currentCube.InverseTransformDirection(Quaternion.AngleAxis(-90f * raw, fwd) * n);
                int side = Mathf.Abs(local.y) > Mathf.Abs(local.x) ? (local.y > 0 ? 0 : 2) : (local.x > 0 ? 1 : 3);
                if (side == targetSide) return d;
            }
            return 0;
        }
        finally { mapPeek = false; }
    }

    /// <summary>
    /// Legacy screenAxisControls mapping, for tooling: the face a LEFT (-1) / RIGHT (+1) press would land on if it were played
    /// now, without deciding anything (rule = "held" or the rule that would decide it). Not for ScreenProjected.
    /// </summary>
    public int PeekPressFace(int screenDir, out string rule)
    {
        rule = "";
        if (currentCube == null) return currentSideIndex;
        int s = legRollSign;
        if (s != 0) rule = sideRemapActive ? "side-remap(held)" : "held";
        else { s = DecideLegSign(currentSideIndex, false, out MapRule r); rule = "would-decide:" + r; }
        return SideAfterRaw(currentSideIndex, screenDir > 0 ? s : -s);
    }

    /// <summary>Puts the runner back to a clean "just placed" state after the generator has repositioned it.</summary>
    public void ResetForTutorial()
    {
        ClearInputs();
        isRevolving = false;
        if (visualRig != null) visualRig.ResetVisual();
        flyingTimer = 0f;
        switchCooldown = 0f;
        lastTapDir = 0;
        lastTapExecuted = true;
        hasLegView = false;
        // A missed corner latches the fall (fallLatched forces isFlying every step) and may leave a rescue or plank
        // window open; after a rewind none of that is true any more, and the corner cache must be rebuilt.
        missPending = plankPending = rescuedThisLeg = fallLatched = false;
        cornerFor = null;
        cornerOk = false;
        zCarry = 0f;
        earlyWasOff = false;
        earlyTurnDist = -1f;
        flyCause = "offtrack";
        cameraRig = null;
        if (rb == null) rb = GetComponent<Rigidbody>();
        rb.isKinematic = false;
        if (runState == RunState.GameOver) runState = RunState.Playing;
    }
    // ---- end TUTORIAL ADDITION ----

    // Fail: off the path for this long, then the runner stops and the run is over
    // (short: the fall itself is played by GameOverSequence, so the run ends as the runner leaves the path)
    public const float FailFlyingSeconds = 0.15f;
    private float flyingTimer = 0f;
    public bool IsGameOver => runState == RunState.GameOver;
    private bool isLost => IsGameOver;

    public bool IsWaitingToStart => runState == RunState.WaitingToStart;
    /// <summary>True from the tap that restarts a run until the new path is up and the runner is off again.</summary>
    public bool IsRebuilding => runState == RunState.Rebuilding;

    private float startForwardSpeed;
    private const float CameraRewindSeconds = 0.4f;

    /// <summary>Soft restart, no scene reload: the old path sinks, a new one rises, the runner and camera go back to the start.</summary>
    public void Restart()
    {
        if (runState == RunState.Rebuilding) return;
        StartCoroutine(SoftResetRoutine());
    }

    System.Collections.IEnumerator SoftResetRoutine()
    {
        runState = RunState.Rebuilding;
        ClearInputs();
        ResetFairState();
        var gen = FindFirstObjectByType<DynamicPathGenerator>();

        // The camera is still frozen on the fall while the old path drops away.
        if (gen != null) yield return gen.SinkPath();
        var zones = FindFirstObjectByType<PortalZoneController>();
        if (zones != null) zones.ResetToOpening();   // back to the scene's default look

        var seq = GetComponent<GameOverSequence>();

        // Runner: back to a clean "just placed" state (the generator then seats it on the first cube).
        // It stays hidden (GameOverSequence still holds it) until the camera has rewound.
        isRevolving = false;
        if (visualRig != null) visualRig.ResetVisual();
        flyingTimer = 0f;
        switchCooldown = 0f;
        lastTapDir = 0;
        lastTapExecuted = true;
        hasLegView = false;
        cameraRig = null;
        isFlying = false;
        forwardSpeed = startForwardSpeed;
        speedEaseT = -1f;
        boostSpeedFactor = 1f;
        var coinUI = GetComponent<CoinCounterUI>();
        if (coinUI != null) coinUI.ResetCount();
        else { var cc = GetComponent<CoinCollector>(); if (cc != null) cc.currentScore = 0; }
        rb.useGravity = false;
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        float rise = 0f;
        if (gen != null)
        {
            gen.SoftReset();
            rise = gen.RiseSeconds * 0.8f;
        }

        // Rewind: let the rig work out the opening shot, then glide the camera there from where it froze.
        var rig = FindFirstObjectByType<CameraParentRotator>();
        Camera cam = rig != null && rig.boomCamera != null ? rig.boomCamera : Camera.main;
        float rewound = 0f;
        if (rig != null && cam != null)
        {
            Transform ct = cam.transform;
            Vector3 fromPos = ct.position;
            Quaternion fromRot = ct.rotation;
            float fromSize = cam.orthographicSize;
            rig.SnapToPlayerZone();
            Vector3 toPos = ct.position;
            Quaternion toRot = ct.rotation;
            float toSize = cam.orthographicSize;
            ct.SetPositionAndRotation(fromPos, fromRot);
            cam.orthographicSize = fromSize;

            for (float t = 0f; t < CameraRewindSeconds; t += Time.deltaTime)
            {
                float k = 1f - t / CameraRewindSeconds;
                float e = 1f - k * k * k; // ease out
                ct.SetPositionAndRotation(Vector3.LerpUnclamped(fromPos, toPos, e), Quaternion.SlerpUnclamped(fromRot, toRot, e));
                cam.orthographicSize = Mathf.LerpUnclamped(fromSize, toSize, e);
                yield return null;
                rewound += Time.deltaTime;
            }
            ct.SetPositionAndRotation(toPos, toRot);
            cam.orthographicSize = toSize;
        }

        // Camera is home: the runner drops in and the rig takes over again.
        if (seq != null) { seq.Release(); seq.UnfreezeCamera(); }
        foreach (var tr in GetComponentsInChildren<TrailRenderer>()) tr.Clear();

        // Off once the path is nearly up.
        if (rise > rewound) yield return new WaitForSeconds(rise - rewound);
        ClearInputs();
        ResetFairState();
        runState = RunState.Playing;
    }

    public void BeginRun() { if (runState == RunState.WaitingToStart) runState = RunState.Playing; }

    // View that left/right map against on the current leg (see UpdateLegView).
    private CameraParentRotator cameraRig;
    private Quaternion legView = Quaternion.identity;
    private Vector3 legHeading;
    private int legStartSide;          // face the runner was on when it landed this leg
    private bool remapFlipped;         // flipRemapsControls: left/right currently swapped
    private float remapSettledTime;    // seconds spent on the opposite (or back on the start) face
    private bool hasLegView;
    /// <summary>The view left/right are mapped against on the current leg (for tooling).</summary>
    public Quaternion ControlView => legView;

    // Child-hierarchy visuals (RunnerVisualRig): the roll, vault, flip squash and cornering are all played on the
    // children so the root's Rigidbody, collider and raycasts never see them.
    private RunnerVisualRig visualRig;
    public float CurrentMoveSpeed => MoveSpeed;
    public bool IsRunning => runState == RunState.Playing;
    public float UnitSize => WorldScale;

    private bool isRevolving = false;
    /// <summary>True while a face-switch roll is in flight (mid-arc: no face is squarely underfoot).</summary>
    public bool IsRevolving => isRevolving;
    private Quaternion revolveFrom;
    private Quaternion revolveTo;
    private float revolveTimer = 0f;
    private float revolveDuration = 0.15f;
    private int revolveFromSide;
    private float revolveArc; // degrees about the cube's forward axis from revolveFromSide to currentSideIndex
    // Quicker side switching: a plain roll now takes 0.10s (flip 0.12s) and the next input is accepted sooner.
    private const float StepRevolveDuration = 0.10f;
    private const float FlipRevolveDuration = 0.12f;
    private const float StepCooldown = 0.08f;
    private const float FlipCooldown = 0.10f;

    private enum RunState { WaitingToStart, Playing, GameOver, Rebuilding }
    private RunState runState = RunState.WaitingToStart;

    private const float PlayerHalfSize = 0.5f;
    private const float Gap = 0.05f;

    // 1 for a normal-sized runner; 2 when the runner and the path are built double size. The probe
    // distances above are authored for scale 1 and have to grow with it.
    float WorldScale => Mathf.Max(0.01f, transform.lossyScale.x);

    void Awake()
    {
        SetLegSpeed(0); // the generator re-applies this per leg; this covers the frames before it exists
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.freezeRotation = true;

        // Sphere collider handles cube edges much better than a box
        SphereCollider sc = GetComponent<SphereCollider>();
        sc.radius = 0.4f;

        // Zero friction material
        PhysicsMaterial mat = new PhysicsMaterial("ZeroFriction");
        mat.staticFriction = 0;
        mat.dynamicFriction = 0;
        mat.frictionCombine = PhysicsMaterialCombine.Minimum;
        sc.sharedMaterial = mat;

        visualRig = GetComponent<RunnerVisualRig>();
        if (visualRig == null) visualRig = gameObject.AddComponent<RunnerVisualRig>();

        if (GetComponent<RunnerHUD>() == null) gameObject.AddComponent<RunnerHUD>();
        if (GetComponent<GameOverSequence>() == null) gameObject.AddComponent<GameOverSequence>(); // builds its shard pool + chime in Start
    }

    void Start()
    {
        if (groundLayer.value == 0) groundLayer = 1; // Default
        InitRollMapping();

        // The runner's position is driven entirely by MoveAndSnap, so it never needs to bump into
        // the path. Colliding made a quick roll (e.g. a half turn) snag on the cube edge it swings
        // round, arriving at a corner still off the face - and missing the turn at speed.
        rb.excludeLayers |= groundLayer;

        // Initial find
        FindAndAttachToCube(-transform.up, 5f * WorldScale);
        StartGame();
    }

    void StartGame()
    {
        startForwardSpeed = forwardSpeed;
        runState = RunState.WaitingToStart;
    }

    void GameOver(Vector3? wallMomentum = null)
    {
        // The shield (RunnerShieldFX) may cancel a death and put the runner back on the route.
        if (SaveHook != null && runState == RunState.Playing && SaveHook(wallMomentum.HasValue)) return;
        // Hand-off to physics: no more input or grid movement, gravity turned on.
        // Falling off the edge keeps the forward velocity so the runner sails off in an arc.
        // A wall hit instead stops the runner dead right where it hit - no bounce-back, no sailing
        // through - and hands its impact speed to GameOverSequence so the shatter's rock shards
        // keep flying the direction it was going, like a real collision.
        runState = RunState.GameOver;
        ClearInputs();
        LastDeathCause = DeathCause(wallMomentum.HasValue);
        rb.isKinematic = false;
        if (wallMomentum.HasValue)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        else
        {
            Vector3 v = rb.linearVelocity;
            rb.linearVelocity = v.sqrMagnitude > 0.01f ? v : transform.forward * forwardSpeed;
        }
        rb.useGravity = true;
        var seq = GetComponent<GameOverSequence>();
        if (seq != null) seq.Begin(wallMomentum);
    }

    /// <summary>Called by PathObstacleGuard when the runner touches a live obstacle. Stops the
    /// runner dead against it (instead of the usual fly-off-the-edge window) and shatters it in
    /// place, with the shard burst carrying the runner's impact speed forward, so it reads as a
    /// real collision instead of passing through the obstacle.</summary>
    public void HitObstacle()
    {
        if (runState != RunState.Playing || isFlying || PowerAuto) return;   // autopilot: invulnerable
        isFlying = true;
        GameOver(wallMomentum: transform.forward * forwardSpeed);
    }

    /// <summary>Triangle gate (DynamicPathGeneratorGate.cs, on only in MainScene prop camera 3.6): a roll that only just
    /// cleared a gate's bars. Pays and plays exactly like a rescue. Nothing else calls it.</summary>
    public void ReportGateCloseCall(Vector3 pos)
    {
        if (runState != RunState.Playing) return;
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        RescuesThisRun++;
        Haptics.MediumImpact();
        if (pathGen != null && profile != null && profile.rescueBonusPoints > 0) pathGen.AwardCloseCall();
        Rescued?.Invoke(new RescueInfo { plank = false, lateSeconds = 0f, overshootCubes = 0f, onBoost = false, position = pos });
    }

    // Boost sections run faster: forwardSpeed itself (ramp, resets, generator) is left alone; this factor eases
    // up on entering a zig-zag boost and back to 1 on leaving it, and is applied where the runner is moved.
    private float boostSpeedFactor = 1f;
    private DynamicPathGenerator boostGen;
    private float MoveSpeed => forwardSpeed * boostSpeedFactor * PowerSpeedFactor * CinematicSpeedFactor * SlowMoSpeedFactor;
    /// <summary>Slow-motion section (3.11, SlowMoDirector): the runner itself slows on its tiny legs, on top of the time scale. 1 everywhere else.</summary>
    [System.NonSerialized] public float SlowMoSpeedFactor = 1f;

    // ---- LookTransition hooks (MainScene prop camera 3.2 only; runtime, default = no change) ----
    /// <summary>Extra speed factor while a portal cinematic plays (never below 0.85 there). 1 everywhere else.</summary>
    [System.NonSerialized] public float CinematicSpeedFactor = 1f;
    /// <summary>True while a portal cinematic owns the camera: every tap, swipe and queued roll is dropped (never applied with the wrong mapping).</summary>
    [System.NonSerialized] public bool inputSuspended;

    // ---- Pause hooks (MainScene prop camera 3.6 PauseController; null / false everywhere else) ----
    /// <summary>A touch that BEGINS where this returns true is ignored for its whole life (press, swipe, release, double tap): the pause button owns it.</summary>
    [System.NonSerialized] public System.Func<Vector2, bool> touchBlocker;
    /// <summary>True while the pause screen is up: no touch or key reaches the runner, and fingers already down are forgotten.</summary>
    [System.NonSerialized] public bool pauseInput;

    // Power diamond autopilot (RunnerPowerFX): speed factor on top of everything, input ignored, runner invulnerable.
    public float PowerSpeedFactor = 1f;
    public bool PowerAuto;
    private bool powerPushing;
    /// <summary>A tap or flick that was ignored because autopilot is on (the code is -1/1 for a side tap).</summary>
    public event System.Action<int> AutoInputIgnored;
    public int QueuedInputCount => inputQueue.Count;

    // ---- Shield save (RunnerShieldFX) ----
    /// <summary>Called when a death is about to start; true = the death was cancelled (the argument says it was an obstacle hit).</summary>
    public System.Func<bool, bool> SaveHook;
    [System.NonSerialized] public int PlankFallSide = -1;
    [System.NonSerialized] public float PlankFallTime = -10f;

    /// <summary>
    /// Puts the runner back on the route after a death was cancelled. Obstacle: stays where it is. A bridge plank or
    /// portal door met mid-leg: stays on that cube, on the plank's face (or the top). Anything else (a missed corner,
    /// a decoy dead end): onto the turn face of the first cube of the next leg, as the close-call rescue does.
    /// Speed and progress are untouched; landing rules and the control mapping refresh like a normal landing.
    /// </summary>
    /// <summary>Raised when the shield cancelled a death (argument: it was an obstacle hit). Listeners: 3.11 only.</summary>
    public event System.Action<bool> ShieldSaved;

    public bool ShieldRecover(bool obstacle)
    {
        if (runState != RunState.Playing) return false;
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (!obstacle && !RecoverOntoRoute()) return false;
        ShieldSaved?.Invoke(obstacle);

        isFlying = false;
        flyingTimer = 0f;
        fallLatched = false;
        missPending = false;
        plankPending = false;
        isRevolving = false;
        flyCause = "offtrack";
        ClearInputs();
        switchCooldown = 0.1f;
        LastLandingTime = Time.fixedTime;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        return true;
    }

    bool RecoverOntoRoute()
    {
        if (pathGen == null || currentCube == null) return false;
        DynamicPathGenerator.Segment seg = pathGen.PlayerSegment;
        if (seg == null) return false;
        int idx = seg.cubes.IndexOf(currentCube.gameObject);
        bool plank = PlankFallSide >= 0 && Time.time - PlankFallTime < 0.6f;

        if (idx >= 0 && (plank || idx < seg.cubes.Count - 2))
        {
            currentSideIndex = plank ? PlankFallSide : 0;
            PlankFallSide = -1;
            float lr = 0.5f + (PlayerHalfSize + Gap);
            Vector3 lp = Vector3.zero, up = Vector3.up;
            switch (currentSideIndex)
            {
                case 0: lp = new Vector3(0, lr, 0); up = Vector3.up; break;
                case 1: lp = new Vector3(lr, 0, 0); up = Vector3.right; break;
                case 2: lp = new Vector3(0, -lr, 0); up = Vector3.down; break;
                default: lp = new Vector3(-lr, 0, 0); up = Vector3.left; break;
            }
            lp.z = currentCube.InverseTransformPoint(rb.position).z;
            Vector3 p = currentCube.TransformPoint(lp);
            Quaternion r = Quaternion.LookRotation(currentCube.forward, currentCube.TransformDirection(up));
            transform.SetPositionAndRotation(p, r);
            rb.position = p; rb.rotation = r;
            Physics.SyncTransforms();
            return true;
        }

        Transform next; Vector3 inD, outD;
        if (!pathGen.TryGetCornerAfter(currentCube, out next, out inD, out outD, out _, out _))
        {
            DynamicPathGenerator.Segment target = pathGen.SegmentBySerial(seg.serial + 1);
            if (target == null || target.cubes.Count == 0 || target.cubes[0] == null) return false;
            next = target.cubes[0].transform;
            inD = seg.direction;
            outD = target.direction;
        }
        float s = next.lossyScale.x;
        Vector3 n = -inD.normalized;
        SetCube(next, n);
        Vector3 pos = next.position + n * ((0.5f + PlayerHalfSize + Gap) * s) + outD.normalized * (0.05f * s);
        Quaternion rot = Quaternion.LookRotation(outD.normalized, n);
        transform.SetPositionAndRotation(pos, rot);
        rb.position = pos; rb.rotation = rot;
        Physics.SyncTransforms();
        zCarry = 0f;
        if (visualRig != null) visualRig.BeginBlend(inD, outD, 0f, 0.12f);
        CornersMadeThisRun++;
        return true;
    }
    /// <summary>Autopilot's own presses: a roll (+-1, raw, in the runner's frame) or a flip (flip = true).</summary>
    public void PowerPress(int rollDir, bool flip)
    {
        powerPushing = true;
        if (flip) QueueFlip(); else QueueRoll(rollDir);
        powerPushing = false;
    }

    void UpdateBoostSpeed()
    {
        float goal = 1f;
        if (runState == RunState.Playing)
        {
            if (boostGen == null) boostGen = FindFirstObjectByType<DynamicPathGenerator>();
            if (boostGen != null && boostGen.PlayerOnZigZagBoost) goal = Mathf.Max(1f, boostGen.boostSpeedMultiplier);
        }
        float rate = Mathf.Abs(Mathf.Max(1f, boostGen != null ? boostGen.boostSpeedMultiplier : 1f) - 1f) / 0.25f;
        boostSpeedFactor = Mathf.MoveTowards(boostSpeedFactor, goal, Mathf.Max(0.01f, rate) * Time.deltaTime);
    }

    void Update()
    {
        UpdateBoostSpeed();

        // Flying timer — always track regardless of runState
        if (isFlying)
            flyingTimer += Time.deltaTime;
        else
            flyingTimer = 0f;

        if (runState == RunState.Playing && isFlying && flyingTimer >= FailFlyingSeconds && !suppressGameOver) // TUTORIAL ADDITION (suppressGameOver)
            GameOver();

        // R key restarts only when lost
        if (Input.GetKeyDown(KeyCode.R) && isLost)
            Restart();

        if (pauseInput) { touchActive = false; for (int i = 0; i < fingers.Length; i++) fingers[i].active = false; return; }   // Pause hook (3.6)

        // Touch input (swipe). DifficultyProfile: fire on touch-down, every finger (tap mode only).
        if (tutorialOwnsTouch) { } // TUTORIAL ADDITION: TutorialManager reads every finger and calls SimulateTap
        else if (FairInput && !FlickMode) HandleTouchInputFair();
        else HandleTouchInput();

        UpdateLegView();

        if (runState != RunState.Playing) return;

        // Always enqueue input — never drop a press during cooldown
        if (Input.GetKeyDown(leftKey) || Input.GetKeyDown(altLeftKey))
            EnqueueTap(-1);
        else if (Input.GetKeyDown(rightKey) || Input.GetKeyDown(altRightKey))
            EnqueueTap(1);
        else if (FlickMode && (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.S) ||
                               Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow)))
            Enqueue(FlipCode);
    }

    // Runs in FixedUpdate, in step with the movement it drives. Done once per rendered frame
    // instead, a frame hitch held a queued press back until the physics had already simulated
    // the whole hitch - at speed, long enough to carry the runner past the corner it was for.
    void ProcessInputQueue()
    {
        if (switchCooldown > 0)
            switchCooldown -= Time.fixedDeltaTime;

        if (currentCube == null || inputQueue.Count == 0) return;

        // The runner may have landed on a new leg this step; map left/right against that leg's view.
        UpdateLegView();

        // Flips jump the cooldown so a double-tap / flick is instant; single steps wait for it
        int next = inputQueue.Peek();
        bool isFlip = next == FlipCode || next == DoubleTapCode;
        if (isFlip || switchCooldown <= 0)
        {
            DequeueInput();
            ExecuteCode(next);
        }
    }

    void ExecuteCode(int next)
    {
        if (next == FlipCode) FlipToOpposite(currentSideIndex);
        else if (next == DoubleTapCode) FlipToOpposite(lastTapExecuted ? sideBeforeLastTap : currentSideIndex);
        else if (next == RawRollCode || next == -RawRollCode) ChangeSide(next / RawRollCode, false);
        else
        {
            sideBeforeLastTap = currentSideIndex;
            lastTapExecuted = true;
            ChangeSide(next);
        }
    }

    int DequeueInput()
    {
        while (inputTimes.Count > inputQueue.Count) inputTimes.Dequeue();   // inputQueue was cleared from outside
        while (inputViews.Count > inputQueue.Count) inputViews.Dequeue();
        int code = inputQueue.Dequeue();
        LastExecutedTapTime = inputTimes.Count > 0 ? inputTimes.Dequeue() : Time.time;
        lastDequeuedView = inputViews.Count > 0 ? inputViews.Dequeue() : default;   // roll mapping: the view this press was made under
        dequeuedCount++;
        return code;
    }

    void ClearInputs()
    {
        dequeuedCount += inputQueue.Count;
        inputQueue.Clear();
        inputTimes.Clear();
        inputViews.Clear();
        nextLegBuffer.Clear();
        nextLegViews.Clear();
    }

    /// <summary>
    /// Queues a roll in the runner's own frame (+1 / -1 as ChangeSide applies it), skipping the
    /// screen-relative mapping. For the auto-play bot, which works out the roll it needs from the
    /// path itself, so it must not depend on which camera view the controls are mapped against.
    /// </summary>
    public void QueueRoll(int rollDir)
    {
        if (runState == RunState.Playing && rollDir != 0) Enqueue(rollDir > 0 ? RawRollCode : -RawRollCode);
    }

    /// <summary>Queues a flip to the opposite face (the double-tap move) - one quick move instead of two rolls.</summary>
    public void QueueFlip()
    {
        if (runState == RunState.Playing) Enqueue(FlipCode);
    }

    void Enqueue(int code) => Enqueue(code, Time.time);

    void Enqueue(int code, float at) => Enqueue(code, at, ProjectedOn ? CaptureView(code == 1 || code == -1 ? code : 0) : default);

    void Enqueue(int code, float at, in PressView view)
    {
        if (inputSuspended) { LastDroppedTapTime = Time.time; return; }   // LookTransition cinematic (off everywhere else)
        if (PowerAuto && !powerPushing) { AutoInputIgnored?.Invoke(code); return; }
        if (inputQueue.Count == 0) { inputTimes.Clear(); inputViews.Clear(); }
        if (inputQueue.Count >= MaxQueuedInputs) { LastDroppedTapTime = Time.time; return; }
        if (settleRederiveClearFaces) settleTimer = 0f;   // a press is coming: not settled
        inputQueue.Enqueue(code);
        inputTimes.Enqueue(at);
        inputViews.Enqueue(view);
        enqueuedCount++;
        if (ProjectedOn) NoteEnqueuedForCheck(code);
    }

    // A left/right press. In tap mode, the same direction twice in quick succession becomes a flip.
    void EnqueueTap(int dir)
    {
        if (tutorialInputFilter != null && !tutorialInputFilter(dir)) return; // TUTORIAL ADDITION: swallow wrong inputs
        if (PowerAuto) { AutoInputIgnored?.Invoke(dir); return; }
        TapsThisLeg++;
        if (FirstTapAfterLanding < 0f) FirstTapAfterLanding = Time.time - LastLandingTime;
        TapTaken?.Invoke(dir);

        // DifficultyProfile: a tap a moment before landing on the next leg is FOR that leg - kept and played at
        // landing, never lost and never rolling the runner off the face it needs for this corner.
        if (FairCorners && TapIsForNextLeg())
        {
            nextLegBuffer.Add(new Vector2(dir, Time.time));
            nextLegViews.Add(ProjectedOn ? CaptureView(dir) : default);   // keeps the view it was pressed under; mapped on the new leg
            lastTapDir = 0;
            return;
        }

        bool doubleTap = FairInput
            ? profile.doubleTapFlip && dir == lastTapDir && lastTapCube == currentCube && Time.unscaledTime - lastTapTime <= profile.doubleTapWindow
            : !screenAxisControls && dir == lastTapDir && Time.unscaledTime - lastTapTime <= DoubleTapWindow;
        if (doubleTap && !FlickMode)
        {
            // In fair mode only the first tap (if still waiting) is replaced: anything queued before it stays.
            if (FairInput) RemoveLastQueued();
            else ClearInputs();
            Enqueue(DoubleTapCode, Time.time, ProjectedOn ? CaptureView(dir) : default);
            lastTapDir = 0; // a third tap starts a fresh sequence
            return;
        }

        lastTapDir = dir;
        lastTapTime = Time.unscaledTime;
        lastTapCube = currentCube;
        lastTapExecuted = false;
        Enqueue(dir);
    }

    private Transform lastTapCube;

    // The first tap of a double tap, if it has not been played yet, makes way for the flip.
    void RemoveLastQueued()
    {
        if (inputQueue.Count == 0 || lastTapExecuted) return;
        int n = inputQueue.Count;
        int[] codes = inputQueue.ToArray();
        float[] times = inputTimes.ToArray();
        PressView[] views = inputViews.ToArray();
        inputQueue.Clear();
        inputTimes.Clear();
        inputViews.Clear();
        for (int i = 0; i < n - 1; i++) { inputQueue.Enqueue(codes[i]); inputTimes.Enqueue(i < times.Length ? times[i] : Time.time); inputViews.Enqueue(i < views.Length ? views[i] : default); }
        enqueuedCount--;
    }

    // ------------------------------------------------------------------ DifficultyProfile: touch

    struct Finger { public bool active, converted; public Vector2 start; public int dir; public int serial; }
    private readonly Finger[] fingers = new Finger[10];

    // Every finger that touches down is its own tap, fired at once (no waiting for the lift). A finger that
    // then travels swipePixels becomes a swipe - but only if its roll has not started yet.
    void HandleTouchInputFair()
    {
        int count = Input.touchCount;
        for (int i = 0; i < count; i++)
        {
            Touch t = Input.GetTouch(i);
            if (!profile.multiTouch && i > 0) break;
            int id = Mathf.Clamp(t.fingerId, 0, fingers.Length - 1);
            switch (t.phase)
            {
                case TouchPhase.Began:
                    if (touchBlocker != null && touchBlocker(t.position)) { fingers[id] = new Finger { active = false }; break; }   // Pause hook (3.6)
                    fingers[id] = new Finger { active = true, start = t.position, serial = -1 };
                    if (runState != RunState.Playing) break;
                    int dir = t.position.x > Screen.width * 0.5f ? 1 : -1;
                    fingers[id].dir = dir;
                    int before = enqueuedCount;
                    if (profile.fireOnTouchDown) EnqueueTap(dir);
                    if (enqueuedCount > before) fingers[id].serial = enqueuedCount - 1;
                    break;

                case TouchPhase.Moved:
                    if (!fingers[id].active || fingers[id].converted || runState != RunState.Playing) break;
                    Vector2 d = t.position - fingers[id].start;
                    if (d.magnitude < profile.swipePixels) break;
                    fingers[id].converted = true;
                    int code = Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? (d.x > 0f ? 1 : -1) : (profile.swipeFlip ? FlipCode : 0);
                    if (code != 0 && code != fingers[id].dir) ReplaceQueued(fingers[id].serial, code);
                    break;

                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    if (fingers[id].active && !profile.fireOnTouchDown && runState == RunState.Playing && !fingers[id].converted)
                        EnqueueTap(t.position.x > Screen.width * 0.5f ? 1 : -1);
                    fingers[id].active = false;
                    break;
            }
        }
    }

    // Swaps a still-waiting queued input (by its serial) for another code. Does nothing once it has played.
    void ReplaceQueued(int serial, int code)
    {
        if (serial < dequeuedCount || serial >= enqueuedCount) return;
        int idx = serial - dequeuedCount;
        if (idx >= inputQueue.Count) return;
        int[] codes = inputQueue.ToArray();
        float[] times = inputTimes.ToArray();
        PressView[] views = inputViews.ToArray();
        codes[idx] = code;
        if (ProjectedOn && idx < views.Length) views[idx].dir = code == 1 || code == -1 ? code : 0;   // the swipe's own direction; same view
        inputQueue.Clear();
        inputTimes.Clear();
        inputViews.Clear();
        for (int i = 0; i < codes.Length; i++) { inputQueue.Enqueue(codes[i]); inputTimes.Enqueue(i < times.Length ? times[i] : Time.time); inputViews.Enqueue(i < views.Length ? views[i] : default); }
    }

    void HandleTouchInput()
    {
        if (Input.touchCount == 0) return;

        Touch touch = Input.GetTouch(0);

        switch (touch.phase)
        {
            case TouchPhase.Began:
                if (touchBlocker != null && touchBlocker(touch.position)) { touchActive = false; break; }   // Pause hook (3.6)
                touchStartPos = touch.position;
                touchActive = true;
                flickFired = false;
                break;

            case TouchPhase.Moved:
                // Flick mode: fire the instant the finger has travelled far enough (no need to lift)
                if (!FlickMode || !touchActive || flickFired || runState != RunState.Playing) break;
                Vector2 d = touch.position - touchStartPos;
                if (d.magnitude < FlickThreshold) break;
                flickFired = true;
                if (Mathf.Abs(d.x) >= Mathf.Abs(d.y)) Enqueue(d.x > 0 ? 1 : -1);
                else Enqueue(FlipCode); // up or down
                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                if (!touchActive) break;
                touchActive = false;
                if (FlickMode) break; // flicks are handled while moving; plain taps do nothing

                float deltaX = touch.position.x - touchStartPos.x;

                if (runState == RunState.Playing)
                {
                    // Swipe left/right, or tap the left/right half of the screen
                    int dir = Mathf.Abs(deltaX) >= SwipeThreshold
                        ? (deltaX > 0 ? 1 : -1)
                        : (touch.position.x > Screen.width * 0.5f ? 1 : -1);
                    EnqueueTap(dir);
                }
                break;
        }
    }

    void FixedUpdate()
    {
        if (runState != RunState.Playing) return;

        if (speedEaseT >= 0f)
        {
            speedEaseT += Time.fixedDeltaTime;
            float k = Mathf.Clamp01(speedEaseT / Mathf.Max(0.01f, profile != null ? profile.speedEaseSeconds : 0.3f));
            forwardSpeed = Mathf.Lerp(speedEaseFrom, speedEaseTarget, k * k * (3f - 2f * k));
            if (k >= 1f) speedEaseT = -1f;
        }

        UpdateFlipRemap();
        UpdateSettleRederive();
        ProcessInputQueue();
        if (plankPending) StepPlankMiss();

        // 1. Transition Check
        if (!(FairCorners && FairCornerStep())) CheckForTransitions();
        if (fallLatched) isFlying = true;   // a failed rescue falls, whatever is tapped after

        // 2. Movement Logic
        if (isFlying)
        {
            rb.linearVelocity = transform.forward * MoveSpeed;
        }
        else
        {
            MoveAndSnap();
        }
    }

    void CheckForTransitions()
    {
        // 1. Check ahead for next cube. Probes from the physics position (with interpolation on,
        // transform.position lags it and only moves once per rendered frame) and reaches at least
        // one physics step ahead, so a fast runner or a low frame rate cannot skip over a corner.
        Vector3 probeFrom = rb.position;
        Vector3 probeDir = rb.rotation * Vector3.forward;
        float probeLength = Mathf.Max(1.2f * WorldScale, MoveSpeed * Time.fixedDeltaTime + 0.5f * WorldScale);
        if (Physics.Raycast(probeFrom, probeDir, out RaycastHit hit, probeLength, groundLayer))
        {
            // DifficultyProfile: the real next leg is taken by the exact corner check (FairCornerStep), not by
            // whichever physics step the probe first reaches it on. Anything else it hits (a decoy) is taken here.
            if (hit.transform != currentCube && !(FairCorners && cornerOk && hit.transform == cornerNext))
            {
                SetCube(hit.transform, hit.normal);
                isFlying = false;
                switchCooldown = FairInput ? profile.landingLock : 0.1f;
                return;
            }
        }

        // 2. Grounding check (Only if not in cooldown to allow side switching, and not mid-roll:
        // on the arc round the cube's edge there is no face straight under the runner)
        if (!isFlying && !isRevolving && switchCooldown <= 0)
        {
            Vector3 inward = -GetFaceNormalWorld(currentSideIndex);

            // Raycast slightly from outside the player to ensure it hits the face even at the very edge
            Vector3 rayStart = rb.position - inward * (0.1f * WorldScale);

            bool overGround = Physics.Raycast(rayStart, inward, 2f * WorldScale, groundLayer) ||
                             Physics.Raycast(rayStart + probeDir * (0.4f * WorldScale), inward, 2f * WorldScale, groundLayer);

            if (!overGround)
            {
                if (FairCorners) { isFlying = true; flyCause = "offtrack"; }
                else if (!TryCornerGrace()) isFlying = true;
            }
        }
        else if (isFlying && switchCooldown <= 0 && !isRevolving && !FairCorners)
        {
            // Just left the end of a leg: a press still waiting in the queue gets one more chance.
            TryCornerGrace();
        }
    }

    // ================================================================== DifficultyProfile: exact corners + rescue

    // The corner at the end of the current leg, cached per leg.
    private Transform cornerFor, cornerNext;
    private bool cornerOk;
    private Vector3 cornerIn, cornerOut, cornerLast;
    private int cornerInner;
    private DynamicPathGenerator.Segment cornerLeg;

    // A corner missed with a correct input maybe on its way (rescue window open).
    private bool missPending, rescuedThisLeg, fallLatched;
    private float missTime;
    private int missMinRolls, missMoves;
    private float zCarry;                        // overshoot carried onto the new leg by the next MoveAndSnap
    private string flyCause = "offtrack";

    // Same for a bridge plank.
    private bool plankPending;
    private int plankPendingId = int.MinValue, plankRescuedId = int.MinValue, plankFailedId = int.MinValue;
    private int plankSide, plankMinRolls, plankMoves;
    private float plankMissTime;

    // Taps kept for the next leg (x = screen dir, y = when).
    private readonly List<Vector2> nextLegBuffer = new List<Vector2>(4);
    private readonly List<PressView> nextLegViews = new List<PressView>(4);   // roll mapping: the view each buffered tap was made under

    /// <summary>True while a corner or plank miss is waiting out its rescue window.</summary>
    public bool RescuePending => missPending || plankPending;
    /// <summary>BridgePlankGuard asks: should a wrong-face plank be reported to ReportPlankMiss instead of ending the run?</summary>
    public bool HandlesPlankMiss => FairCorners && profile.rescuePlanks && runState == RunState.Playing;

    void ResetFairState()
    {
        missPending = plankPending = rescuedThisLeg = fallLatched = false;
        plankPendingId = plankRescuedId = plankFailedId = int.MinValue;
        cornerFor = null;
        cornerOk = false;
        zCarry = 0f;
        nextLegBuffer.Clear();
        flyCause = "offtrack";
        LastDeathCause = "";
        nextLegViews.Clear();
        ResetRollMapping();
        RescuesThisRun = 0;
        CornersMadeThisRun = 0;
        LastMissTime = -10f;
        speedEaseT = -1f;
    }

    void RefreshCorner()
    {
        if (cornerFor == currentCube && cornerOk) return;
        if (cornerFor != currentCube) { earlyWasOff = false; earlyTurnDist = -1f; }
        cornerFor = currentCube;
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        cornerOk = pathGen != null && pathGen.TryGetCornerAfter(currentCube, out cornerNext, out cornerIn, out cornerOut, out cornerLast, out cornerLeg);
        if (cornerOk) cornerInner = SideFacing(cornerOut);
    }

    float CommitA => profile.cornerCommitOffset * (cornerNext != null ? cornerNext.lossyScale.x : WorldScale);

    // The close-call rescue for the leg the runner is on (scene 3.11 sets it per level; everywhere else the profile's fields).
    int RescueLeg => pathGen != null && pathGen.PlayerSegment != null ? pathGen.PlayerSegment.serial : -1;
    float RescueWindowNow => profile.RescueWindowAt(RescueLeg);
    float RescueReachNow => profile.RescueReachAt(RescueLeg);
    float AlongCorner => Vector3.Dot(rb.position - cornerLast, cornerIn);

    /// <summary>Seconds until the corner is decided (big if there is no corner on this leg).</summary>
    public float SecondsToCorner
    {
        get
        {
            if (!FairCorners || !cornerOk || cornerFor != currentCube) return 99f;
            return Mathf.Max(0f, CommitA - AlongCorner) / Mathf.Max(0.01f, MoveSpeed);
        }
    }

    // True when the runner is on (or already rolling onto) this corner's face and the corner is close: the
    // tap is meant for the next leg.
    bool TapIsForNextLeg()
    {
        if (runState != RunState.Playing || missPending || currentCube == null) return false;
        RefreshCorner();
        if (!cornerOk) return false;
        if (PredictSide(out _) != cornerInner) return false;     // not on the face yet: the tap is for this corner
        return SecondsToCorner < profile.preCornerBuffer;
    }

    // The face the runner ends on once everything queued has played, and how many face-steps that takes.
    int PredictSide(out int moves)
    {
        int side = currentSideIndex;
        moves = 0;
        int baseForDouble = lastTapExecuted ? sideBeforeLastTap : currentSideIndex;
        // Roll mapping (ScreenProjected): each queued press is mapped under ITS OWN view, on a copy of the live roll state,
        // exactly as ExecuteCode will do it when the press is played.
        RollState sim = default;
        Queue<PressView>.Enumerator views = default;
        bool projected = ProjectedOn;
        if (projected) { sim = GetRollState(); views = inputViews.GetEnumerator(); }
        // Legacy fixes: the first queued press decides the sign from the face it is played on, later ones keep it - simulated
        // on a copy, never committed.
        bool fixes = !projected && LegacyFixesMapping;
        if (fixes) UpdateLegView();
        int simSign = legRollSign;
        foreach (int code in inputQueue)
        {
            PressView pv = default;
            if (projected && views.MoveNext()) pv = views.Current;
            int to;
            if (code == FlipCode) to = (side + 2) % 4;
            else if (code == DoubleTapCode) to = (baseForDouble + 2) % 4;
            else if (code == RawRollCode || code == -RawRollCode) to = SideAfterRollFrom(side, code / RawRollCode, false);
            else if (projected) { baseForDouble = side; to = SideAfterProjected(side, code, pv, ref sim); }
            else if (fixes)
            {
                baseForDouble = side;
                if (simSign == 0) simSign = DecideLegSign(side, false, out _);
                to = SideAfterRaw(side, code > 0 ? simSign : -simSign);
            }
            else { baseForDouble = side; to = SideAfterRollFrom(side, code, true); }
            moves += DynamicPathGenerator.Rolls(side, to);
            side = to;
        }
        return side;
    }

    // SideAfterRoll from any face (for predicting a queued sequence).
    int SideAfterRollFrom(int fromSide, int dir, bool screenRelative)
    {
        Vector3 moveForward = currentCube.forward;
        Vector3 n = GetFaceNormalWorld(fromSide);
        if (screenRelative) { mapPeek = MapFixesOn; try { dir = ScreenRelativeDir(dir, moveForward, n); } finally { mapPeek = false; } }
        if (dir == 0) return fromSide;
        Vector3 l = currentCube.InverseTransformDirection(Quaternion.AngleAxis(-90f * dir, moveForward) * n);
        if (Mathf.Abs(l.y) > Mathf.Abs(l.x)) return l.y > 0 ? 0 : 2;
        return l.x > 0 ? 1 : 3;
    }

    // Returns true when it has dealt with this step (the old probe / grounding pass is then skipped).
    bool FairCornerStep()
    {
        if (currentCube == null) return false;
        if (missPending) return StepCornerMiss();

        RefreshCorner();
        if (!cornerOk || isFlying) return false;
        float over = AlongCorner - CommitA;

        // Early-turn bonus: remember how far before the corner the runner arrived on the turn face (only if it
        // had to turn on this leg).
        if (currentSideIndex != cornerInner) { earlyWasOff = true; earlyTurnDist = -1f; }
        else if (earlyWasOff && earlyTurnDist < 0f) earlyTurnDist = Mathf.Max(0f, -over);

        if (over < 0f) return false;

        // The corner is decided now, at its exact point (sub-step: the overshoot is carried onto the next leg).
        if (currentSideIndex == cornerInner)
        {
            bool early = earlyWasOff && earlyTurnDist >= 0f && profile.earlyTurnBonusCubes > 0f
                && earlyTurnDist <= profile.earlyTurnBonusCubes * (cornerNext != null ? cornerNext.lossyScale.x : WorldScale);
            bool boost = pathGen != null && pathGen.PlayerOnZigZagBoost;
            Vector3 pos = transform.position;
            LandCorner(over, false);
            if (early) EarlyTurnBonus(boost, pos);
            return true;
        }

        // A correct input already given but held back by the roll cooldown counts as on time.
        int predicted = PredictSide(out int moves);
        if (inputQueue.Count > 0 && predicted == cornerInner && moves == DynamicPathGenerator.Rolls(currentSideIndex, cornerInner))
        {
            while (inputQueue.Count > 0) ExecuteCode(DequeueInput());
            if (currentSideIndex == cornerInner) { LandCorner(over, false); return true; }
        }

        // Missed. A rescue may still come within rescueWindow (one per leg).
        missTime = Time.fixedTime - over / Mathf.Max(0.01f, MoveSpeed);
        LastMissTime = missTime;
        if (rescuedThisLeg || RescueWindowNow <= 0f)
        {
            fallLatched = true;
            isFlying = true;
            flyCause = "corner";
            return true;
        }
        missPending = true;
        missMinRolls = DynamicPathGenerator.Rolls(currentSideIndex, cornerInner);
        missMoves = 0;
        return true;
    }

    // While the rescue window is open the runner glides on past the end of the leg; the fall timer and the
    // ground probe are held off. A rescue needs: on the turn face, nothing more queued, the fewest face-steps
    // from where it was at the miss, and every input given inside the window.
    bool StepCornerMiss()
    {
        float elapsed = Time.fixedTime - missTime;
        float over = AlongCorner - CommitA;
        float window = RescueWindowNow;

        if (currentSideIndex == cornerInner && inputQueue.Count == 0)
        {
            if (missMoves == missMinRolls && LastExecutedTapTime <= missTime + window + 0.0001f)
            {
                Rescue(over);
                return true;
            }
            FailMiss();      // over-rotated, or came round the long way
            return true;
        }
        if (elapsed > window + Time.fixedDeltaTime || over > RescueReachNow * cornerNext.lossyScale.x)
        {
            FailMiss();
            return true;
        }
        isFlying = false;
        return true;
    }

    void FailMiss()
    {
        missPending = false;
        fallLatched = true;
        isFlying = true;
        flyCause = "corner";
    }

    // Takes the corner: the runner goes onto the next leg's landing face exactly as a normal landing does.
    // A normal landing carries its sub-step overshoot through MoveAndSnap; a rescue is placed outright and
    // the visual whips round the corner edge from where it was (RunnerVisualRig.BeginBlend).
    void LandCorner(float over, bool rescued)
    {
        Transform next = cornerNext;
        Vector3 inDir = cornerIn, outDir = cornerOut;
        float s = next.lossyScale.x;

        missPending = false;
        SetCube(next, -inDir);
        isFlying = false;
        switchCooldown = FairInput ? profile.landingLock : 0.1f;
        LastLandingTime = Time.fixedTime - over / Mathf.Max(0.01f, MoveSpeed);
        CornersMadeThisRun++;
        if (pathGen != null) pathGen.NoteCornerLanded();

        if (rescued)
        {
            Vector3 n = -inDir;
            Vector3 pos = next.position + n * ((0.5f + PlayerHalfSize + Gap) * s) + outDir * (0.05f * s + over);
            Quaternion rot = Quaternion.LookRotation(outDir, n);
            transform.SetPositionAndRotation(pos, rot);
            rb.position = pos;
            rb.rotation = rot;
            Physics.SyncTransforms();
            zCarry = 0f;
            if (visualRig != null) visualRig.BeginBlend(inDir, outDir, over, profile.rescueRollTime);
        }
        else zCarry = over;

        FlushNextLegBuffer();
    }

    private bool earlyWasOff;
    private float earlyTurnDist = -1f;

    // A turn made inside the arrow zone (the last few cubes before the corner) scores the same +1 and FX as a rescue.
    void EarlyTurnBonus(bool boost, Vector3 pos)
    {
        RescuesThisRun++;
        Haptics.MediumImpact();
        if (pathGen != null && profile.rescueBonusPoints > 0) pathGen.AwardCloseCall();
        Rescued?.Invoke(new RescueInfo { plank = false, lateSeconds = 0f, overshootCubes = 0f, onBoost = boost, position = pos });
    }

    void Rescue(float over)
    {
        float late = Mathf.Max(0f, LastExecutedTapTime - missTime);
        bool boost = pathGen != null && pathGen.PlayerOnZigZagBoost;
        LandCorner(over, true);    // lands on a new leg, so the next leg can be rescued again (never twice on one leg)
        RescuesThisRun++;
        Haptics.MediumImpact();
        if (pathGen != null && profile.rescueBonusPoints > 0) pathGen.AwardCloseCall();
        Rescued?.Invoke(new RescueInfo { plank = false, lateSeconds = late, overshootCubes = over / Mathf.Max(0.01f, cornerNext != null ? cornerNext.lossyScale.x : 1f), onBoost = boost, position = transform.position });
    }

    void FlushNextLegBuffer()
    {
        for (int i = 0; i < nextLegBuffer.Count; i++)
        {
            lastTapDir = (int)nextLegBuffer[i].x;
            lastTapTime = Time.unscaledTime;
            lastTapCube = currentCube;
            lastTapExecuted = false;
            Enqueue((int)nextLegBuffer[i].x, nextLegBuffer[i].y, i < nextLegViews.Count ? nextLegViews[i] : default);
        }
        nextLegBuffer.Clear();
        nextLegViews.Clear();
    }

    /// <summary>
    /// BridgePlankGuard: the runner is over plank `id` on the wrong face. Opens the rescue window (once per
    /// plank); if a correct roll onto `side` lands inside it, it was a close call, otherwise the run ends.
    /// </summary>
    public void ReportPlankMiss(int id, int side, float distInto)
    {
        if (PowerAuto) return;   // autopilot: invulnerable
        PlankFallSide = side; PlankFallTime = Time.time;
        if (runState != RunState.Playing) return;
        if (plankPending && plankPendingId == id) return;
        if (id == plankRescuedId || id == plankFailedId || plankPending || RescueWindowNow <= 0f)
        {
            plankFailedId = id;
            fallLatched = true;
            isFlying = true;
            flyCause = "plank";
            return;
        }
        plankPending = true;
        plankPendingId = id;
        plankSide = side;
        plankMissTime = Time.time - Mathf.Max(0f, distInto) / Mathf.Max(0.01f, MoveSpeed);
        LastMissTime = plankMissTime;
        plankMinRolls = DynamicPathGenerator.Rolls(currentSideIndex, side);
        plankMoves = 0;
    }

    void StepPlankMiss()
    {
        float elapsed = Time.fixedTime - plankMissTime;
        if (currentSideIndex == plankSide && inputQueue.Count == 0)
        {
            plankPending = false;
            if (plankMoves == plankMinRolls && LastExecutedTapTime <= plankMissTime + RescueWindowNow + 0.0001f)
            {
                plankRescuedId = plankPendingId;
                rescuedThisLeg = true;     // one rescue per leg: this leg's corner gets none now
                RescuesThisRun++;
                Haptics.MediumImpact();
                if (pathGen != null && profile.rescueBonusPoints > 0) pathGen.AwardCloseCall();
                Rescued?.Invoke(new RescueInfo { plank = true, lateSeconds = Mathf.Max(0f, LastExecutedTapTime - plankMissTime), overshootCubes = elapsed * MoveSpeed / WorldScale, onBoost = false, position = transform.position });
            }
            else { plankFailedId = plankPendingId; fallLatched = true; isFlying = true; flyCause = "plank"; }
            return;
        }
        if (elapsed > RescueWindowNow + Time.fixedDeltaTime)
        {
            plankPending = false;
            plankFailedId = plankPendingId;
            fallLatched = true;
            isFlying = true;
            flyCause = "plank";
        }
    }

    string DeathCause(bool wall)
    {
        if (wall) return "obstacle";
        if (currentCube != null && currentCube.name.StartsWith("DecoyCube")) return "decoy";
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (pathGen != null && pathGen.PortalEntrySegment != null && pathGen.PlayerSegment == pathGen.PortalEntrySegment) return "portal";
        if (flyCause == "plank") return "plank";
        if (flyCause == "corner") return "corner";
        // Without a profile there is no exact corner check: off the end of a leg is still a corner.
        if (!FairCorners && pathGen != null && pathGen.PlayerSegment != null && currentCube != null &&
            Vector3.Dot(transform.position - pathGen.PlayerSegment.endPosition, currentCube.forward) > -0.6f) return "corner";
        return flyCause;
    }

    // Corner grace: the runner is about to leave the end of its leg with a press still waiting in the
    // queue (held back by the step cooldown). If that press lands on exactly the face that opens
    // toward the next leg (the "inner" face, as the corner arrows show), run it now instead of falling.
    // A press toward any other face is left alone, so a wrong-way turn is never rescued.
    bool TryCornerGrace()
    {
        if (currentCube == null || inputQueue.Count == 0) return false;
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (pathGen == null) return false;

        var corners = pathGen.GetUpcomingCorners(1);
        if (corners.Count == 0) return false;
        if (Vector3.Dot(currentCube.forward, corners[0].incomingDirection.normalized) < 0.9f) return false;

        Vector3 want = corners[0].outgoingDirection.normalized;
        int inner = -1;
        for (int s = 0; s < 4; s++)
            if (Vector3.Dot(GetFaceNormalWorld(s), want) > 0.9f) inner = s;
        if (inner < 0 || inner == currentSideIndex) return false;

        int next = inputQueue.Peek();
        int predicted;
        if (next == FlipCode) predicted = (currentSideIndex + 2) % 4;
        else if (next == DoubleTapCode) predicted = ((lastTapExecuted ? sideBeforeLastTap : currentSideIndex) + 2) % 4;
        else predicted = SideAfterRoll(next == RawRollCode || next == -RawRollCode ? next / RawRollCode : next,
                                       !(next == RawRollCode || next == -RawRollCode));
        if (predicted != inner) return false;

        // Run it now, skipping the cooldown.
        DequeueInput();
        if (next == FlipCode) FlipToOpposite(currentSideIndex);
        else if (next == DoubleTapCode) FlipToOpposite(lastTapExecuted ? sideBeforeLastTap : currentSideIndex);
        else if (next == RawRollCode || next == -RawRollCode) ChangeSide(next / RawRollCode, false);
        else { sideBeforeLastTap = currentSideIndex; lastTapExecuted = true; ChangeSide(next); }
        return true;
    }

    // The face a roll would land on (currentSideIndex if it does nothing); same maths as ChangeSide.
    int SideAfterRoll(int dir, bool screenRelative)
    {
        if (screenRelative && ProjectedOn)
        {
            // The head of the queue is the press being asked about: map it under its own view, on a copy of the state.
            RollState sim = GetRollState();
            return SideAfterProjected(currentSideIndex, dir, inputViews.Count > 0 ? inputViews.Peek() : CaptureView(dir), ref sim);
        }
        Vector3 moveForward = currentCube.forward;
        Vector3 currentNormal = GetFaceNormalWorld(currentSideIndex);
        if (screenRelative) { mapPeek = MapFixesOn; try { dir = ScreenRelativeDir(dir, moveForward, currentNormal); } finally { mapPeek = false; } }
        if (dir == 0) return currentSideIndex;
        Vector3 localNormal = currentCube.InverseTransformDirection(Quaternion.AngleAxis(-90f * dir, moveForward) * currentNormal);
        if (Mathf.Abs(localNormal.y) > Mathf.Abs(localNormal.x)) return localNormal.y > 0 ? 0 : 2;
        return localNormal.x > 0 ? 1 : 3;
    }

    // The view left/right are read against. It is only looked at again at the moment the runner
    // lands on the next leg (a new heading), and only replaced if the camera has actually turned
    // since - so a camera swing never flips the controls part way along a leg, and landing on a
    // new leg under an unchanged camera changes nothing. If the camera is mid-swing at that moment,
    // the swing's end orientation is used.
    void UpdateLegView()
    {
        if (currentCube == null) return;
        Vector3 heading = currentCube.forward;
        if (hasLegView && Vector3.Angle(heading, legHeading) < 30f) return;
        legHeading = heading;
        legStartSide = currentSideIndex;
        remapFlipped = false;
        remapSettledTime = 0f;
        legRollSign = 0; // a new leg: left/right are worked out afresh, then held for the whole leg
        sideRemapActive = false; sideSettleFace = -1;
        pressPlayedThisLeg = false; settleTimer = 0f; settleFace = -1;

        if (cameraRig == null) cameraRig = FindFirstObjectByType<CameraParentRotator>();
        Camera cam = Camera.main;
        Quaternion view;
        if (cameraRig != null) view = cameraRig.SettledViewRotation;
        else if (cam != null) view = cam.transform.rotation;
        else return;

        if (!hasLegView || Quaternion.Angle(view, legView) > ViewChangeDegrees)
        {
            if (hasLegView) LastViewChangeTime = Time.time;
            legView = view;
        }
        hasLegView = true;
    }

    // Camera turns smaller than this on landing a new leg leave the controls as they were.
    private const float ViewChangeDegrees = 10f;

    // Turns a left/right press into the roll direction that reads as left/right ON SCREEN.
    // Rolling "dir" is a fixed rotation about the runner's heading, so on its own it looks
    // left or right depending on which way the runner faces the camera. This finds which way
    // the top of the runner would swing on screen for dir = +1 and flips it when that is left.
    int ScreenRelativeDir(int dir, Vector3 moveForward, Vector3 currentNormal)
    {
        // ScreenProjected: a read-only look (live view, copy of the roll state) for callers that only ask "where would this go"
        // (TapDirectionToReach and so on). Real presses are mapped by ProjectedCommitDir from ChangeSide.
        if (ProjectedOn) return ProjectedPeekDir(dir, currentNormal);
        if (!cameraRelativeControls || !hasLegView) return dir;
        if (screenAxisControls)
        {
            int r = DotProductRollDir(dir);
            if (r != 0) return r;
        }

        // Always measured from the path's top face, never the face currently under the runner:
        // the on-screen swing of a roll changes as the runner goes round the path, which made a
        // repeated "right" flip to the left. Now the mapping is fixed for the whole leg.
        Vector3 refNormal = currentCube != null ? currentCube.up : currentNormal;
        Vector3 rolled = Quaternion.AngleAxis(-90f, moveForward) * refNormal;
        Vector3 swing = Vector3.ProjectOnPlane(rolled - refNormal, refNormal); // world direction the face swings for +1

        // The view locked in when the runner came onto this leg, not the live camera.
        Vector3 camRight = legView * Vector3.right, camUp = legView * Vector3.up;
        Vector2 swingScreen = new Vector2(Vector3.Dot(swing, camRight), Vector3.Dot(swing, camUp));
        Vector2 heading = new Vector2(Vector3.Dot(moveForward, camRight), Vector3.Dot(moveForward, camUp));

        float sign;
        if (Mathf.Abs(swingScreen.x) > 0.3f * swingScreen.magnitude)
        {
            // The swing has a clear left/right on screen: follow it.
            sign = Mathf.Sign(swingScreen.x);
        }
        else
        {
            // The swing is straight up/down the screen (runner crossing the view sideways),
            // so left/right has no screen meaning; use the runner's own right-hand side.
            Vector2 runnerRight = new Vector2(heading.y, -heading.x);
            sign = Mathf.Sign(Vector2.Dot(swingScreen, runnerRight));
        }
        if (sign == 0f) sign = 1f;

        if (remapFlipped) sign = -sign;
        return sign > 0f ? dir : -dir;
    }

    public bool RemapFlipped => remapFlipped;
    public int LegStartSide => legStartSide;
    public int CurrentSideIndex => currentSideIndex;

    // flipRemapsControls: after the runner has sat on the top<->bottom opposite of its landing face for
    // flipRemapDelay real seconds (counted from the flip itself), left/right swap (and swap back if it returns).
    void UpdateFlipRemap()
    {
        if (currentCube == null) return;
        if (ProjectedOn) return;   // ScreenProjected projects the runner's real position, so no left/right swap is needed
        // Independent of FlipRemapOn: a DifficultyProfile with fairInput + disableFlipRemap turns the top/bottom remap off
        // (prop camera 3.2 uses one), and this side-face rule must still run.
        if (sideFaceRemap && screenAxisControls && UpdateSideFaceRemap()) return;
        if (!FlipRemapOn) return;
        bool wantFlipped = (legStartSide == 0 || legStartSide == 2) && currentSideIndex == (legStartSide + 2) % 4;
        // Only a top/bottom face can change the mapping. On a side face (mid-roll round the cube) it is held, so a
        // left tap that has just rolled to a side face and another left tap keep going the same way.
        if (currentSideIndex == 1 || currentSideIndex == 3 || wantFlipped == remapFlipped) { remapSettledTime = 0f; return; }
        remapSettledTime += Time.fixedUnscaledDeltaTime; // real seconds, so slow-mo (DebugTimeScale) doesn't stretch it
        // The delay is real seconds at normal speed and shrinks with Time.timeScale, so slow-mo (P hotkey) doesn't make it feel long.
        if (remapSettledTime >= flipRemapDelay * Mathf.Min(1f, Time.timeScale)) {
            remapFlipped = wantFlipped;
            LastRemapChangeTime = Time.time;
            remapSettledTime = 0f;
            legRollSign = 0; // screenAxisControls path: re-derive left/right from the face the runner is on now
        }
    }

    // sideFaceRemap. On a left/right face the two possible rolls go to the top and bottom faces, which sit at the same
    // screen-x, so the screen cannot say which one is "left"/"right" (DotProductRollDir then falls back on the heading,
    // which can disagree with the runner's own sides). After flipRemapDelay (shrunk by Time.timeScale, like the top/bottom
    // remap) settled on such a face, if the screen really cannot tell the two rolls apart, RIGHT is set to the runner's own
    // right-hand roll (raw +1) and LEFT to its left-hand roll. Back on a top/bottom face the screen mapping is re-derived.
    // Returns true while the runner is on a side face (the caller then skips the top/bottom remap, which holds there anyway).
    private bool sideRemapActive;
    private float sideSettleTime;
    private int sideSettleFace = -1;

    bool UpdateSideFaceRemap()
    {
        if (currentSideIndex != 1 && currentSideIndex != 3)
        {
            sideSettleFace = -1;
            if (sideRemapActive)
            {
                sideRemapActive = false;
                // keepSpinOnSideWall: rolling off the wall keeps the direction (settleRederiveClearFaces re-derives once settled)
                if (!keepSpinOnSideWall) { legRollSign = 0; LastRemapChangeTime = Time.time; }
            }
            return false;
        }
        if (currentSideIndex != sideSettleFace) { sideSettleFace = currentSideIndex; sideSettleTime = 0f; }
        if (sideRemapActive) return true;
        sideSettleTime += Time.fixedUnscaledDeltaTime;   // real seconds, shrunk by timeScale below
        if (sideSettleTime < flipRemapDelay * Mathf.Min(1f, Time.timeScale)) return true;

        // Does the screen separate the two rolls? Where a +1 roll lands, left/right on screen (a -1 roll lands opposite).
        Vector3 fwd = currentCube.forward;
        Vector3 camRight = MappingView() * Vector3.right;
        Vector3 n = GetFaceNormalWorld(currentSideIndex);
        float xPlus = Vector3.Dot(Quaternion.AngleAxis(-90f, fwd) * n, camRight);
        if (Mathf.Abs(xPlus) > ScreenDecisiveX) return true;   // the screen does say: keep its answer
        // keepSpinOnSideWall: a direction the player has already used on this leg is never overwritten
        if (keepSpinOnSideWall && (pressPlayedThisLeg || legRollSign != 0)) return true;

        sideRemapActive = true;
        if (legRollSign != 1) { legRollSign = 1; LastRemapChangeTime = Time.time; }
        return true;
    }

    // Which raw roll (+1 / -1) a RIGHT press means on the current leg. 0 = not decided yet. Decided by the first
    // press after the runner lands on a leg, then held - every later press on the leg keeps that direction, whatever
    // face the runner rolls onto. UpdateLegView clears it when the runner lands on the next leg.
    private int legRollSign;

    // "Right" means the PLAYER'S right: the side of its own heading as it appears on screen (the heading turned
    // 90 degrees clockwise, in the view locked when the runner landed on this leg). The first press picks whichever
    // of the two rolls lands the runner further along it, from the face it is on at that moment.
    // Returns the raw roll (+1/-1) for a screen press (-1 left, +1 right).
    // Screen-x of a roll's landing face above which the screen decides left/right (the side-face rule uses the same limit).
    private const float ScreenDecisiveX = 0.15f;

    int DotProductRollDir(int screenDir)
    {
        if (currentCube == null) return 0;

        if (legRollSign == 0)
        {
            // Legacy fixes: a look (TapDirectionToReach, PredictSide, corner grace) answers without deciding the leg's sign.
            if (mapPeek && MapFixesOn)
            {
                int s = DecideLegSign(currentSideIndex, false, out _);
                return screenDir > 0 ? s : -s;
            }
            legRollSign = DecideLegSign(currentSideIndex, true, out _);
        }

        return screenDir > 0 ? legRollSign : -legRollSign;
    }

    public enum MapRule { ScreenX, Heading, SideWall, ScreenXSettledView, HeadingReached }

    // The first-press decision of DotProductRollDir, from `face`, without storing it. With every fix toggle off this is
    // exactly the original maths (legView, screen-x when sideFaceRemap, else HEADING).
    int DecideLegSign(int face, bool commit, out MapRule rule)
    {
        Quaternion view = MappingView();
        Vector3 camRight = view * Vector3.right, camUp = view * Vector3.up;
        Vector3 fwd = currentCube.forward;

        // The face a +1 roll lands on; a -1 roll lands on the opposite face, so its score is the negative.
        Vector3 dest = Quaternion.AngleAxis(-90f, fwd) * GetFaceNormalWorld(face);
        // (sideFaceRemap scenes only - prop camera 3.2.) Where the screen clearly separates the two rolls (one lands screen-right, the other screen-left), RIGHT is
        // the roll that lands toward screen-right - even when the runner is heading toward the camera, where its own
        // right-hand side is screen-left. Only when both land at the same screen x does the heading decide.
        float screenX = Vector3.Dot(dest, camRight);
        if (sideFaceRemap && Mathf.Abs(screenX) > ScreenDecisiveX) { rule = MapRule.ScreenX; return screenX > 0f ? 1 : -1; }

        if (sideWallRunnerRule)
        {
            // Side wall, both rolls stacked: RIGHT = the runner's own clockwise roll, whenever the press comes.
            if (face == 1 || face == 3) { rule = MapRule.SideWall; return 1; }
            // TOP/BOTTOM stacked can only happen under a drawn mid-swing view: the swing's end view decides if it can.
            if (useDrawnViewForMapping)
            {
                float x2 = Vector3.Dot(dest, legView * Vector3.right);
                if (Mathf.Abs(x2) > ScreenDecisiveX) { rule = MapRule.ScreenXSettledView; return x2 > 0f ? 1 : -1; }
            }
            rule = MapRule.HeadingReached;
            if (commit)
            {
                HeadingRuleHits++;
                Debug.LogWarning($"[RollMap] HEADING rule reached with sideWallRunnerRule on (face {face}, screenX {screenX:0.00}) - used the clockwise roll instead");
            }
            return 1;
        }

        float hx = Vector3.Dot(fwd, camRight), hy = Vector3.Dot(fwd, camUp);
        Vector3 playerRight = camRight * hy - camUp * hx;
        rule = MapRule.Heading;
        return Vector3.Dot(dest, playerRight) >= 0f ? 1 : -1;
    }

    // ---- Legacy fixes (sideWallRunnerRule / keepSpinOnSideWall / settleRederiveClearFaces / useDrawnViewForMapping) ----
    bool MapFixesOn => sideWallRunnerRule || keepSpinOnSideWall || settleRederiveClearFaces || useDrawnViewForMapping;
    bool LegacyFixesMapping => MapFixesOn && screenAxisControls && cameraRelativeControls && hasLegView && !remapFlipped;
    private bool mapPeek;              // set around look-only mapping calls
    private bool pressPlayedThisLeg;   // a screen press has been played on this leg (keepSpinOnSideWall)
    private float settleTimer;         // settleRederiveClearFaces: real seconds with nothing queued / rolling on this face
    private int settleFace = -1;
    private Camera drawnCam;

    /// <summary>Times the HEADING rule was reached with sideWallRunnerRule on (should stay 0).</summary>
    public int HeadingRuleHits { get; private set; }
    /// <summary>settleRederiveClearFaces: sign clears, and how many of them changed what LEFT/RIGHT mean.</summary>
    public int SettleRederives { get; private set; }
    public int SettleRederiveFlips { get; private set; }
    public int LegRollSign => legRollSign;
    public bool SideRemapActive => sideRemapActive;
    public bool PressPlayedThisLeg => pressPlayedThisLeg;
    /// <summary>The camera rotation left/right are decided against right now (drawn camera with useDrawnViewForMapping, else the leg's settled view).</summary>
    public Quaternion MappingViewRotation => MappingView();

    Quaternion MappingView()
    {
        if (useDrawnViewForMapping)
        {
            if (drawnCam == null)
            {
                if (cameraRig == null) cameraRig = FindFirstObjectByType<CameraParentRotator>();
                drawnCam = cameraRig != null && cameraRig.boomCamera != null ? cameraRig.boomCamera : Camera.main;
            }
            if (drawnCam != null) return drawnCam.transform.rotation;
        }
        return legView;
    }

    int SideAfterRaw(int fromSide, int raw)
    {
        Vector3 fwd = currentCube.forward;
        Vector3 l = currentCube.InverseTransformDirection(Quaternion.AngleAxis(-90f * raw, fwd) * GetFaceNormalWorld(fromSide));
        if (Mathf.Abs(l.y) > Mathf.Abs(l.x)) return l.y > 0 ? 0 : 2;
        return l.x > 0 ? 1 : 3;
    }

    // settleRederiveClearFaces: once the runner has sat on a face for flipRemapDelay (real seconds, shrunk by timeScale) with
    // nothing queued, rolling or buffered, and the screen clearly separates the two rolls from that face, the leg's sign is
    // cleared - the next press re-decides it with the screen-x rule against the (drawn, with useDrawnViewForMapping) camera.
    // Checked every step, so a camera swing that ends while the runner is settled counts too. Stacked side walls keep the sign.
    void UpdateSettleRederive()
    {
        if (!settleRederiveClearFaces || currentCube == null || ProjectedOn || !screenAxisControls || !hasLegView) return;
        if (currentSideIndex != settleFace) { settleFace = currentSideIndex; settleTimer = 0f; }
        if (inputQueue.Count > 0 || isRevolving || nextLegBuffer.Count > 0) { settleTimer = 0f; return; }
        settleTimer += Time.fixedUnscaledDeltaTime;
        if (settleTimer < flipRemapDelay * Mathf.Min(1f, Time.timeScale) || legRollSign == 0) return;

        Vector3 dest = Quaternion.AngleAxis(-90f, currentCube.forward) * GetFaceNormalWorld(currentSideIndex);
        float screenX = Vector3.Dot(dest, MappingView() * Vector3.right);
        if (Mathf.Abs(screenX) <= ScreenDecisiveX) return;   // stacked: keep the held sign
        int want = screenX > 0f ? 1 : -1;
        SettleRederives++;
        if (want != legRollSign) { SettleRederiveFlips++; LastRemapChangeTime = Time.time; }
        legRollSign = 0;
        if (sideRemapActive) sideRemapActive = false;
    }

    void ChangeSide(int dir) => ChangeSide(dir, true);

    void ChangeSide(int dir, bool screenRelative)
    {
        if (currentCube == null) return;

        Vector3 moveForward = currentCube.forward;
        Vector3 currentNormal = GetFaceNormalWorld(currentSideIndex);
        if (screenRelative && ProjectedOn) dir = ProjectedCommitDir(dir);   // ScreenProjected: mapped under the view the press was made under
        else if (screenRelative) { dir = ScreenRelativeDir(dir, moveForward, currentNormal); pressPlayedThisLeg = true; }
        if (dir == 0) return; // dot-product mapping: already on the screen-left/right face

        Vector3 rotatedNormal = Quaternion.AngleAxis(-90f * dir, moveForward) * currentNormal;
        Vector3 localNormal = currentCube.InverseTransformDirection(rotatedNormal);

        int oldSide = currentSideIndex;
        if (Mathf.Abs(localNormal.y) > Mathf.Abs(localNormal.x))
            currentSideIndex = (localNormal.y > 0) ? 0 : 2;
        else
            currentSideIndex = (localNormal.x > 0) ? 1 : 3;

        if (oldSide != currentSideIndex)
            BeginRevolve(oldSide, StepCooldown, StepRevolveDuration);
        if (screenRelative && ProjectedOn) AfterProjectedRoll(oldSide);
    }

    // Jump straight to the face opposite baseSide (used by double-tap and flick up/down)
    void FlipToOpposite(int baseSide)
    {
        if (currentCube == null) return;
        int oldSide = currentSideIndex;
        currentSideIndex = (baseSide + 2) % 4;
        if (ProjectedOn) flipArcOverride = ProjectedFlipArc(oldSide);   // the flip spins the way the same-side roll would
        BeginRevolve(oldSide, FlipCooldown, FlipRevolveDuration, true);

        // Camera juice; the rig ignores this unless its flipImpactEnabled is on (only the prop camera scene).
        if (cameraRig == null) cameraRig = FindFirstObjectByType<CameraParentRotator>();
        if (cameraRig != null) cameraRig.TriggerFlipImpact();
    }

    static Vector3 SideLocalUp(int side) => side switch
    {
        1 => Vector3.right,
        2 => Vector3.down,
        3 => Vector3.left,
        _ => Vector3.up
    };

    void BeginRevolve(int fromSide, float cooldown, float duration, bool flip = false)
    {
        // The position swings round the cube on an arc from the old face to the new one (see MoveAndSnap).
        revolveFromSide = fromSide;
        revolveArc = Vector3.SignedAngle(SideLocalUp(fromSide), SideLocalUp(currentSideIndex), Vector3.forward);
        if (Mathf.Abs(revolveArc) > 179f) revolveArc = 180f;
        if (flip && flipArcOverride != 0f) revolveArc = flipArcOverride;
        flipArcOverride = 0f;

        Haptics.LightImpact();
        switchCooldown = cooldown;
        isFlying = fallLatched;   // (fallLatched only ever set by the DifficultyProfile rescue: a failed rescue keeps falling)

        // DifficultyProfile rescue bookkeeping: face-steps made since the miss, and the quick rescue roll.
        if (missPending || plankPending)
        {
            int steps = DynamicPathGenerator.Rolls(fromSide, currentSideIndex);
            if (missPending) missMoves += steps;
            if (plankPending) plankMoves += steps;
            duration = Mathf.Min(duration, profile.rescueRollTime);
        }

        // Start revolving rotation to new side orientation
        Vector3 newUp = currentSideIndex switch
        {
            0 => Vector3.up,
            1 => Vector3.right,
            2 => Vector3.down,
            3 => Vector3.left,
            _ => Vector3.up
        };
        Vector3 targetWorldUp = currentCube.TransformDirection(newUp);
        revolveFrom = rb.rotation;
        revolveTo = Quaternion.LookRotation(currentCube.forward, targetWorldUp);
        // The root takes the new face orientation at once (no slerp); the visual rig plays the roll on the children.
        transform.rotation = revolveTo;
        rb.rotation = revolveTo;
        if (visualRig != null) visualRig.BeginVault(revolveArc, duration, flip);
        revolveTimer = 0f;
        revolveDuration = duration;
        isRevolving = true;
    }

    void SetCube(Transform newCube, Vector3 normal)
    {
        Transform prevCube = currentCube;
        currentCube = newCube;
        Vector3 localNormal = currentCube.InverseTransformDirection(normal);

        // Determine closest side to hit normal
        if (Mathf.Abs(localNormal.y) > Mathf.Abs(localNormal.x))
            currentSideIndex = (localNormal.y > 0) ? 0 : 2;
        else
            currentSideIndex = (localNormal.x > 0) ? 1 : 3;

        isFlying = false;
        isRevolving = false; // a roll's arc is in the old cube's frame; the new face is settled directly
        if (prevCube != newCube)
        {
            LastLandingTime = Time.fixedTime;
            TapsThisLeg = 0;
            FirstTapAfterLanding = -1f;
            rescuedThisLeg = false;
            fallLatched = false;
            missPending = false;
            Landed?.Invoke();
        }
        if (visualRig != null && runState == RunState.Playing && prevCube != null && prevCube != newCube
            && Vector3.Angle(prevCube.forward, newCube.forward) > 30f)
            visualRig.BeginCorner(Vector3.SignedAngle(prevCube.forward, newCube.forward, GetFaceNormalWorld(currentSideIndex)) > 0f ? 1 : -1);
        Debug.Log($"Attached to {newCube.name}, side {currentSideIndex}");
    }

    void FindAndAttachToCube(Vector3 direction, float distance)
    {
        if (Physics.Raycast(transform.position, direction, out RaycastHit hit, distance, groundLayer))
        {
            SetCube(hit.transform, hit.normal);
        }
    }

    void MoveAndSnap()
    {
        if (currentCube == null) return;

        float localRadius = 0.5f + (PlayerHalfSize + Gap);
        Vector3 targetLocalPos = Vector3.zero;
        Vector3 upVector = Vector3.up;

        switch (currentSideIndex)
        {
            case 0: targetLocalPos = new Vector3(0, localRadius, 0); upVector = Vector3.up; break;
            case 1: targetLocalPos = new Vector3(localRadius, 0, 0); upVector = Vector3.right; break;
            case 2: targetLocalPos = new Vector3(0, -localRadius, 0); upVector = Vector3.down; break;
            case 3: targetLocalPos = new Vector3(-localRadius, 0, 0); upVector = Vector3.left; break;
        }

        // While rolling, the target swings round the cube on an arc instead of cutting straight
        // across: the runner does not collide with the path, so a straight line would pass
        // through the cube's edge (or, for a flip, right through the cube).
        if (isRevolving)
        {
            // The position finishes its swing a little before the spin, so the runner is over the
            // new face (and can see a corner cube ahead) as soon as possible.
            float tp = Mathf.Clamp01((revolveTimer + Time.fixedDeltaTime) / (revolveDuration * 0.6f));
            targetLocalPos = Quaternion.AngleAxis(revolveArc * tp, Vector3.forward) * SideLocalUp(revolveFromSide) * localRadius;
        }

        // Maintain local Z (forward progress)
        targetLocalPos.z = currentCube.InverseTransformPoint(rb.position).z;
        // DifficultyProfile exact corners: the distance run past the corner point this step goes onto the new leg.
        if (zCarry > 0f) { targetLocalPos.z += zCarry / Mathf.Max(0.01f, currentCube.lossyScale.z); zCarry = 0f; }

        Vector3 targetWorldPos = currentCube.TransformPoint(targetLocalPos);
        Vector3 targetWorldUp = currentCube.TransformDirection(upVector);

        // Movement components: Forward + Snap
        Vector3 forwardVel = currentCube.forward * MoveSpeed;
        Vector3 snapVel = (targetWorldPos - rb.position) * sideSwitchSpeed;

        rb.linearVelocity = forwardVel + snapVel;

        // Rotation: revolve smoothly when switching sides, otherwise align normally
        Quaternion targetRot = Quaternion.LookRotation(currentCube.forward, targetWorldUp);
        if (isRevolving)
        {
            revolveTimer += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(revolveTimer / revolveDuration);
            rb.MoveRotation(revolveTo);
            if (t >= 1f) isRevolving = false;
        }
        else
        {
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, 50f * Time.fixedDeltaTime));
        }
        }

    Vector3 GetFaceNormalWorld(int side)
    {
        if (currentCube == null) return transform.up;
        switch (side)
        {
            case 0: return currentCube.up;
            case 1: return currentCube.right;
            case 2: return -currentCube.up;
            case 3: return -currentCube.right;
            default: return currentCube.up;
        }
    }

    // =====================================================================================================
    // Roll mapping (RollMappingMode.ScreenProjected, MainScene prop camera 3.3). Legacy leaves every path above untouched.
    // Kept inside this class (not a partial in another file): Unity binds this script asset to the class by file.
    // =====================================================================================================

    [Header("Roll mapping (prop camera 3.3 only - Legacy everywhere else)")]
    [Tooltip("Legacy = the original left/right maths, untouched. ScreenProjected = Right is the roll that lands further RIGHT on screen (projected with the camera as it was when the key / tap was pressed); when both land above each other, stackedRule decides.")]
    public RollMappingMode rollMapping = RollMappingMode.Legacy;
    [Tooltip("ScreenProjected: the two roll results must differ in screen x by at least this (in cube widths) for 'further right' to decide.")]
    public float clearThreshold = 0.35f;
    [Tooltip("ScreenProjected: what Right means when both roll results are stacked on screen. RightIsUp = the one higher on screen. Clockwise = clockwise round the track's on-screen cross-section.")]
    public StackedRule stackedRule = StackedRule.RightIsUp;
    [Tooltip("ScreenProjected: presses closer together than this (unscaled s), or made while a roll is still playing, keep the same rotation direction. 'Right, Right, Right' goes round the track the same way.")]
    public float burstWindow = 0.35f;
    [Tooltip("ScreenProjected: two small chevrons on the two roll results, a faint glow on the matching screen half; yellow when it is the face the coming corner needs. Fades with correct play, always shown when stacked.")]
    public bool showRollPreview = false;
    [Tooltip("Log every mapped press (leg, cube, side, candidates, dA/dB, clear/stacked, rule, burst/re-derive, chosen face) and draw gizmos at the projected candidates.")]
    public bool debugRollMapping = false;
    [Tooltip("Count (and log) presses whose predicted face (PredictSide at enqueue) differs from what actually happened.")]
    public bool checkRollPredictions = false;

    /// <summary>Presses whose prediction was compared, and how many differed (checkRollPredictions).</summary>
    public int RollPredictionChecks { get; private set; }
    public int RollPredictionMismatches { get; private set; }
    /// <summary>Mapped presses, and how many used the stacked rule (stats for camera tuning).</summary>
    public int RollMappedPresses { get; private set; }
    public int RollStackedPresses { get; private set; }

    bool ProjectedOn => rollMapping == RollMappingMode.ScreenProjected;

    private readonly Queue<PressView> inputViews = new Queue<PressView>();   // beside inputQueue / inputTimes
    private PressView lastDequeuedView;
    private float flipArcOverride;
    private float rollLastPress = -100f;
    private Quaternion rollDeriveRot = Quaternion.identity;
    private Camera viewCam;
    private RollInfo lastRollInfo;
    private bool haveRollInfo;
    private bool checkArmed;
    private int checkPredicted;
    private int checkSerial = -1;
    private Transform checkCube;
    private RollPreview rollPreview;

    void InitRollMapping()
    {
        if (!ProjectedOn) return;
        if (showRollPreview && rollPreview == null)
        {
            var go = new GameObject("RollPreview");
            rollPreview = go.AddComponent<RollPreview>();
            rollPreview.runner = this;
        }
    }

    void ResetRollMapping()
    {
        legRollSign = 0;
        rollLastPress = -100f;
        lastDequeuedView = default;
        inputViews.Clear();
        checkArmed = false;
    }

    // UpdateLegView first: a press queued right after a landing is predicted before Update has noticed the new heading; the
    // new leg must reset the held sign here too, or the prediction would use the old leg's sign and the real press would not.
    RollState GetRollState() { UpdateLegView(); return new RollState { sign = legRollSign, lastPress = rollLastPress, rot = rollDeriveRot }; }
    void SetRollState(in RollState s) { legRollSign = s.sign; rollLastPress = s.lastPress; rollDeriveRot = s.rot; }

    PressView CaptureView(int dir = 0)
    {
        if (viewCam == null) viewCam = Camera.main;
        var v = new PressView { time = Time.unscaledTime, dir = dir, rolling = isRevolving || switchCooldown > 0f };
        if (viewCam == null) { v.rot = Quaternion.identity; v.isOrtho = true; v.ortho = 1f; return v; }
        Transform t = viewCam.transform;
        v.valid = true;
        v.pos = t.position;
        v.rot = t.rotation;
        v.fov = viewCam.fieldOfView;
        v.isOrtho = viewCam.orthographic;
        v.ortho = viewCam.orthographicSize;
        v.aspect = viewCam.aspect;
        v.screen = new Vector2(Screen.width, Screen.height);
        return v;
    }

    /// <summary>The raw roll (+1 / -1) a screen press (-1 left, +1 right) gives from face fromSide under view v, with the held state st (updated).</summary>
    int ProjectedRaw(int screenDir, int fromSide, in PressView v, ref RollState st, out RollInfo info)
    {
        Transform cube = currentCube;
        Vector3 fwd = cube.forward;
        float cubeSize = cube.lossyScale.x;
        Vector3 n0 = GetFaceNormalWorld(fromSide);

        bool rightIsA = RollProjection.RightIsPlusOne(v, cube.position, cubeSize, fwd, n0, clearThreshold, stackedRule, out info);
        int derived = rightIsA ? 1 : -1;

        RollWhy why;
        int sign;
        if (st.sign == 0) { sign = derived; why = RollWhy.Derived; }
        else
        {
            float gap = v.time - st.lastPress;
            bool camChanged = Quaternion.Angle(v.rot, st.rot) > 10f;
            if (!camChanged && gap <= burstWindow) { sign = st.sign; why = RollWhy.Burst; }
            else if (!camChanged && v.rolling) { sign = st.sign; why = RollWhy.Rolling; }
            else
            {
                why = camChanged ? RollWhy.Camera : RollWhy.Gap;
                sign = derived;
                // Jitter right at the clear threshold must not flip a sign that was already in force.
                if (Mathf.Abs(Mathf.Abs(info.dxDiff) - clearThreshold) <= 0.05f) { sign = st.sign; why = RollWhy.Hysteresis; }
            }
        }
        if (why != RollWhy.Burst && why != RollWhy.Rolling) st.rot = v.rot;
        st.sign = sign;
        st.lastPress = v.time;
        info.why = why;
        info.sign = sign;
        return screenDir > 0 ? sign : -sign;
    }

    int FaceAfterRoll(int fromSide, int raw)
    {
        if (raw == 0) return fromSide;
        Vector3 l = currentCube.InverseTransformDirection(Quaternion.AngleAxis(-90f * raw, currentCube.forward) * GetFaceNormalWorld(fromSide));
        if (Mathf.Abs(l.y) > Mathf.Abs(l.x)) return l.y > 0 ? 0 : 2;
        return l.x > 0 ? 1 : 3;
    }

    /// <summary>Where a queued press goes (state st is a copy the caller owns). Used by PredictSide and the corner grace.</summary>
    int SideAfterProjected(int fromSide, int dir, in PressView pv, ref RollState st)
    {
        PressView v = pv.valid ? pv : CaptureView(dir);
        int raw = ProjectedRaw(dir, fromSide, v, ref st, out _);
        return FaceAfterRoll(fromSide, raw);
    }

    /// <summary>Real execution of a press (ChangeSide): maps it under the view it was made under and commits the held state.</summary>
    int ProjectedCommitDir(int dir)
    {
        PressView v = lastDequeuedView.valid ? lastDequeuedView : CaptureView(dir);
        RollState st = GetRollState();
        int raw = ProjectedRaw(dir, currentSideIndex, v, ref st, out RollInfo info);
        SetRollState(st);
        lastRollInfo = info;
        haveRollInfo = true;
        RollMappedPresses++;
        if (!info.clear) { RollStackedPresses++; if (checkRollPredictions) NoteStacked(v); }
        if (debugRollMapping) LogRollMapping(dir, raw, info, v);
        return raw;
    }

    /// <summary>Stacked presses by camera yaw / on-screen heading / face (filled while checkRollPredictions is on), to see which camera and leg combinations hit the stacked rule.</summary>
    public readonly Dictionary<string, int> RollStackedCombos = new Dictionary<string, int>();

    void NoteStacked(in PressView v)
    {
        Vector3 c = currentCube.position;
        Vector2 a = RollProjection.ScreenOf(v, c), b = RollProjection.ScreenOf(v, c + currentCube.forward);
        Vector2 h = b - a;
        string head = (Mathf.Abs(h.x) < 0.002f ? "" : h.x > 0f ? "right" : "left") + (Mathf.Abs(h.y) < 0.002f ? "" : h.y > 0f ? "up" : "down");
        head = head == "" ? "none" : head.Replace("rightup", "up-right").Replace("leftup", "up-left").Replace("rightdown", "down-right").Replace("leftdown", "down-left");
        string key = "yaw " + Mathf.RoundToInt(v.rot.eulerAngles.y) + " heading " + head + " on " + FaceNames[currentSideIndex & 3];
        RollStackedCombos.TryGetValue(key, out int n);
        RollStackedCombos[key] = n + 1;
    }

    /// <summary>Read-only: where would a press right now go (live camera, a copy of the held state)? For TapDirectionToReach and the like.</summary>
    int ProjectedPeekDir(int dir, Vector3 currentNormal)
    {
        PressView v = CaptureView(dir);
        RollState st = GetRollState();
        return ProjectedRaw(dir, SideFacing(currentNormal), v, ref st, out _);
    }

    /// <summary>Spin of a flip (+/-180) so it turns the way the same-side roll would.</summary>
    float ProjectedFlipArc(int fromSide)
    {
        if (!lastDequeuedView.valid || lastDequeuedView.dir == 0) return 0f;
        PressView v = lastDequeuedView;
        RollState st = GetRollState();
        int raw = ProjectedRaw(v.dir, fromSide, v, ref st, out RollInfo info);
        SetRollState(st);
        lastRollInfo = info;
        haveRollInfo = true;
        if (debugRollMapping) LogRollMapping(v.dir, raw, info, v);
        return raw > 0 ? -180f : 180f;   // a +1 roll arcs -90, a -1 roll +90
    }

    // ---- prediction check (checkRollPredictions): PredictSide at enqueue vs what the press then did ----
    void NoteEnqueuedForCheck(int code)
    {
        if (!checkRollPredictions || (code != 1 && code != -1) || currentCube == null) return;
        if (inputQueue.Count != 1) return;   // only a press that is the whole queue has a clean prediction
        checkPredicted = PredictSide(out _);
        checkCube = currentCube;
        checkSerial = enqueuedCount - 1;   // this very press (a flip / double-tap may replace it, then nothing is compared)
        checkArmed = true;
    }

    void AfterProjectedRoll(int oldSide)
    {
        if (!checkArmed) return;
        checkArmed = false;
        if (checkSerial != dequeuedCount - 1) return;   // a different press than the one predicted
        if (currentCube != checkCube) return;   // landed on another leg meanwhile: not comparable
        RollPredictionChecks++;
        if (currentSideIndex != checkPredicted)
        {
            RollPredictionMismatches++;
            Debug.LogWarning("[RollMap] prediction mismatch: predicted face " + checkPredicted + " but went " + oldSide + " -> " + currentSideIndex + " on " + currentCube.name);
        }
    }

    static readonly string[] FaceNames = { "Top", "Right", "Bottom", "Left" };

    void LogRollMapping(int dir, int raw, in RollInfo info, in PressView v)
    {
        int leg = pathGen != null && pathGen.PlayerSegment != null ? pathGen.PlayerSegment.serial : -1;
        int sA = FaceAfterRoll(currentSideIndex, 1), sB = FaceAfterRoll(currentSideIndex, -1);
        int rightFace = info.sign > 0 ? sA : sB, leftFace = info.sign > 0 ? sB : sA;
        Debug.Log("[RollMap] leg " + leg + " cube " + (currentCube != null ? currentCube.name : "-") + " side " + FaceNames[currentSideIndex & 3]
            + " | candidates +1->" + FaceNames[sA & 3] + " -1->" + FaceNames[sB & 3]
            + " | dA=(" + info.dA.x.ToString("F2") + "," + info.dA.y.ToString("F2") + ") dB=(" + info.dB.x.ToString("F2") + "," + info.dB.y.ToString("F2") + ") dxDiff=" + info.dxDiff.ToString("F2")
            + " | " + (info.clear ? "CLEAR (further right)" : "STACKED rule=" + info.rule)
            + " | " + info.why + " | Right->" + FaceNames[rightFace & 3] + " Left->" + FaceNames[leftFace & 3]
            + " | press " + (dir > 0 ? "RIGHT" : "LEFT") + " -> raw " + raw + " -> " + FaceNames[FaceAfterRoll(currentSideIndex, raw) & 3]
            + " | cam yaw " + v.rot.eulerAngles.y.ToString("F0"));
    }

    /// <summary>For the preview: where Right and Left would go right now, on a copy of the state, with the live camera.</summary>
    public bool PreviewCandidates(out int rightFace, out int leftFace, out bool stacked)
    {
        rightFace = leftFace = 0; stacked = false;
        if (!ProjectedOn || currentCube == null || IsGameOver) return false;
        PressView v = CaptureView(1);
        RollState st = GetRollState();
        int rRaw = ProjectedRaw(1, currentSideIndex, v, ref st, out RollInfo info);
        rightFace = FaceAfterRoll(currentSideIndex, rRaw);
        leftFace = FaceAfterRoll(currentSideIndex, -rRaw);
        stacked = !info.clear;
        return true;
    }

    /// <summary>Corner the runner is heading for: the face it needs, and whether the corner is close.</summary>
    public bool PreviewNeededFace(out int face)
    {
        face = -1;
        if (!cornerOk || currentCube == null || cornerFor != currentCube) return false;
        face = cornerInner;
        return SecondsToCorner < 3f;
    }

    public Vector3 PreviewFacePoint(int side, float lift)
    {
        Vector3 fwd = currentCube.forward;
        float half = currentCube.lossyScale.x * 0.5f;
        Vector3 along = fwd * Vector3.Dot(transform.position - currentCube.position, fwd);
        return currentCube.position + along + GetFaceNormalWorld(side) * (half + lift);
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!debugRollMapping || !ProjectedOn || !haveRollInfo) return;
        // The last mapped press: the two projected candidate points; the RIGHT one is labelled R.
        bool aIsRight = lastRollInfo.sign > 0;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(aIsRight ? lastRollInfo.pointA : lastRollInfo.pointB, 0.25f);
        UnityEditor.Handles.Label((aIsRight ? lastRollInfo.pointA : lastRollInfo.pointB) + Vector3.up * 0.4f, "R");
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(aIsRight ? lastRollInfo.pointB : lastRollInfo.pointA, 0.25f);
        UnityEditor.Handles.Label((aIsRight ? lastRollInfo.pointB : lastRollInfo.pointA) + Vector3.up * 0.4f, "L");
    }
#endif
}