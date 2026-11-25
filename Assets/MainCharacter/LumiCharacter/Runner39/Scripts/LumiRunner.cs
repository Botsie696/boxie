using UnityEngine;

/// <summary>
/// MainScene prop camera 3.9: Lumi (MainCharacter/LumiCharacter), a fluffy amber cube girl, is the runner. Purely visual -
/// the cube's physics, timing, input and collider are untouched; she rides the runner's visual rig and acts out the game.
///
///   MainPlayer (root: Rigidbody, CubeRailRunner)
///    -> VisualWrapper -> PivotContainer      (RunnerVisualRig: corner drift, the roll onto a new face)
///        -> LumiRunner (this, on the cube's bottom face = the rail)           moved here at runtime
///         -> Yaw        smoothed facing on corners, glances, idle look at the player
///          -> Motion    trot / hop / squash / waddle / lean (scales from the rail, so she never sinks or floats)
///           -> Lumi    (Animator + LumiFace + LumiFurPhysics + LumiBlinker), body fitted to the cube's width
///
/// Read like the 3.7 cube: her solid body is exactly one rail wide (fur tips past it), she is lit by her own camera-locked
/// light rig in Lumi/RunnerLit (only she is lit by it, in every checkpoint), and her eyes are bigger and open.
///
/// Soul (on top of her clips - Run_InPlace_v2 keeps the body still, so the body language is code):
///   - a quick trot: a little hop per step, pressed into the rail on contact, a side-to-side waddle, leaning into speed
///   - side switch: a jump over the rail edge timed exactly to the game's roll (0.10 s / 0.12 s flip), stretch on take-off,
///     squash and follow-through on landing, a twirl on a double-tap flip; the eyes go wide for it
///   - eyes read the road: down the leg, then toward the next leg as the corner comes; eye contact with the player now and
///     then when the camera is in front, a glance over her shoulder when it is behind (straights only)
///   - moods: determined at speed, a happy squint + pop on coins, surprised on a close call / rescue, a wince on a crash
///   - before the run: turned to the player, breathing, little happy hops
/// </summary>
[DefaultExecutionOrder(950)]   // after RunnerVisualRig (0), before LumiFace (9000) / LumiFurPhysics (10000)
[DisallowMultipleComponent]
public class LumiRunner : MonoBehaviour
{
    [Header("Parts (built by Tools > Lumi Runner)")]
    public Transform yaw;
    public Transform motion;
    public Transform model;
    public Animator animator;
    public Lumi.LumiFace face;
    public Lumi.LumiFurPhysics fur;
    [HideInInspector] public Light glowLight;   // unused (no light on Lumi); kept so old prefabs load

    [Header("Size")]
    [Tooltip("Width of her solid body (fur excluded) in cube units. 1 = exactly the 3.7 cube / the rail; the fur fluffs past it.")]
    [Range(0.6f, 1.5f)] public float bodyWidth = 1.1f;
    [Tooltip("Height of her solid body (fur and tuft excluded) in cube units - she fills the cube's cell like the 3.7 cube did.")]
    [Range(0.6f, 1.5f)] public float bodyHeight = 1.05f;

    [Header("Eyes")]
    [Tooltip("Eye size (eyeballs + lids). At game distance the face is a few dozen pixels: bigger eyes carry the expression.")]
    [Range(1f, 1.6f)] public float eyeScale = 1.25f;
    [Tooltip("Resting lid opening (+ = wide awake). Her clips rest half-lidded, which reads bored at a distance.")]
    [Range(-0.5f, 1f)] public float eyesOpen = 0.45f;
    [Tooltip("Before a corner the eyes turn toward the next leg (seconds ahead).")]
    [Range(0.1f, 1.5f)] public float lookAheadSeconds = 0.5f;

    [Header("Trot")]
    [Tooltip("Run_InPlace_v2 playback rate at the slowest / fastest run speed (her fur, tuft and lids).")]
    public Vector2 runAnimSpeed = new Vector2(1.1f, 2.2f);
    [Tooltip("Steps per second at the slowest / fastest run speed.")]
    public Vector2 stepRate = new Vector2(3.4f, 5.2f);
    [Range(0f, 0.15f)] public float stepHeight = 0.035f;
    [Range(0f, 0.15f)] public float stepSquash = 0.05f;
    [Tooltip("Side-to-side waddle per step (degrees).")]
    [Range(0f, 12f)] public float waddle = 4f;
    [Tooltip("Forward lean (degrees) at the slowest / fastest run speed.")]
    public Vector2 forwardLean = new Vector2(2f, 6f);

    [Header("Side switch (hop)")]
    [Range(0f, 0.8f)] public float hopHeight = 0.32f;
    [Range(0f, 0.8f)] public float flipHopHeight = 0.42f;
    [Range(0f, 0.4f)] public float hopStretch = 0.14f;
    [Range(0f, 0.5f)] public float landSquash = 0.22f;
    [Range(0f, 30f)] public float rollFollowThrough = 10f;
    [Tooltip("A full twirl on a double-tap flip (seconds; 0 = off). Visual only - she faces forward again right after.")]
    [Range(0f, 0.6f)] public float flipTwirl = 0.3f;

    [Header("Facing")]
    [Range(0.01f, 0.3f)] public float turnTime = 0.07f;
    [Range(0f, 0.1f)] public float bank = 0.025f;
    [Tooltip("Before the run, she turns this far toward the camera to look at the player.")]
    [Range(0f, 180f)] public float idleLookBack = 150f;

    [Header("Glances")]
    [Tooltip("Seconds between looks at the player (eye contact when the camera is in front, over the shoulder when behind).")]
    public Vector2 glanceEvery = new Vector2(3.5f, 6.5f);
    [Range(0f, 90f)] public float shoulderGlance = 65f;

    [Header("Fur")]
    [Range(0f, 1f)] public float furWind = 0.12f;

    static readonly int HopState = Animator.StringToHash("Hop");
    static readonly int RunState = Animator.StringToHash("Run");
    static readonly int IdleState = Animator.StringToHash("Idle");
    static readonly int LandState = Animator.StringToHash("Land");
    static readonly int SmashState = Animator.StringToHash("Smash");
    static readonly int RunSpeedId = Animator.StringToHash("RunSpeed");

    CubeRailRunner runner;
    RunnerVisualRig rig;
    DynamicPathGenerator gen;
    CoinCollector coins;
    MeshRenderer cubeRenderer;
    Renderer[] mine;
    readonly System.Collections.Generic.List<Renderer> hidden = new System.Collections.Generic.List<Renderer>();
    bool attached, fitted, shown = true, wasRunning, wasOver;
    int soulCheckFrames = 120;

    Transform[] eyeParts;
    Vector3[] eyePartScale;

    Vector3 prevUp, prevFwd;
    float yawOffset, yawVel, bankAngle, lookBack, lookBackVel;

    float hopT = -1f, hopDur = 0.1f, hopLift, hopSign, twirlT = -1f;
    float squash, squashV, roll, rollV, pop, popV, glance, glanceV;
    float stepPhase, idleHopT = -1f, nextIdleHop = 2f;
    float faceT = -1f, faceLen;
    enum Mood { None, Happy, Surprised, Excited, Hurt }
    Mood mood;
    float glanceTarget, glanceUntil, nextGlance, eyeContactUntil;

    void Awake()
    {
        mine = GetComponentsInChildren<Renderer>(true);
        // The fur feels the real motion (corners, hops, rolls, trot) but rides along with the steady run speed;
        // the run itself only shows as airflow sweeping it back.
        if (fur != null) { fur.teleportDistance = 3f; fur.useTransformVelocity = true; fur.carryWithBody = true; fur.windStrength = furWind; }
        nextGlance = Random.Range(glanceEvery.x, glanceEvery.y);
        if (model != null)
        {
            var parts = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Eye_") || t.name.StartsWith("Lid_")) parts.Add(t);
            eyeParts = parts.ToArray();
            eyePartScale = new Vector3[eyeParts.Length];
            for (int i = 0; i < eyeParts.Length; i++) eyePartScale[i] = eyeParts[i].localScale;
        }
    }

    void Start()
    {
        runner = GetComponentInParent<CubeRailRunner>();
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        coins = FindFirstObjectByType<CoinCollector>();
        if (coins != null) coins.CoinCollected += OnCoin;
        if (gen != null) gen.CloseCallAwarded += OnCloseCall;
        if (runner != null) { runner.Landed += OnLanded; runner.Rescued += OnRescued; }
        Fit();
    }

    void OnDestroy()
    {
        foreach (Renderer r in hidden) if (r != null) r.forceRenderingOff = false;
        if (coins != null) coins.CoinCollected -= OnCoin;
        if (gen != null) gen.CloseCallAwarded -= OnCloseCall;
        if (runner != null) { runner.Landed -= OnLanded; runner.Rescued -= OnRescued; }
    }

    // ------------------------------------------------------------------ set-up

    // Ride the visual rig's roll (not CubeMesh: that one also pitches), standing on the cube's bottom face = the rail.
    void Attach()
    {
        if (runner == null) return;
        Transform pivot = FindDeep(runner.transform, "PivotContainer");
        if (pivot == null) return;
        attached = true;
        rig = runner.GetComponent<RunnerVisualRig>();
        float unit = runner.UnitSize > 0f ? runner.UnitSize / Mathf.Max(0.0001f, runner.transform.lossyScale.y) : 1f;
        transform.SetParent(pivot, false);
        transform.localPosition = new Vector3(0f, -0.5f * unit, 0f);
        transform.localRotation = Quaternion.identity;
        foreach (Transform t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = runner.gameObject.layer;

        Transform cubeMesh = FindDeep(runner.transform, "CubeMesh");
        if (cubeMesh != null) cubeRenderer = cubeMesh.GetComponent<MeshRenderer>();
        foreach (MeshRenderer r in runner.GetComponentsInChildren<MeshRenderer>(true))
            if (!r.transform.IsChildOf(transform)) hidden.Add(r);
        prevUp = runner.transform.up;
        prevFwd = runner.transform.forward;
    }

    // Face along +Z, scale so the solid body (the Skin part, fur excluded) is bodyWidth wide, pivot (= her ground contact)
    // on the rail. Measured once from the animated pose.
    void Fit()
    {
        if (fitted || model == null) return;
        fitted = true;
        if (animator != null) animator.Update(0f);
        Transform front = FindDeep(model, "Face_Front"), upper = FindDeep(model, "Body_Upper");
        if (front == null) front = FindDeep(model, "Eye_L");
        if (front != null && upper != null)
        {
            Vector3 f = motion.InverseTransformDirection(front.position - upper.position);
            f.y = 0f;
            if (f.sqrMagnitude > 1e-6f) model.localRotation = Quaternion.FromToRotation(f.normalized, Vector3.forward) * model.localRotation;
        }
        var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null) return;
        model.localPosition = Vector3.zero;
        var baked = new Mesh();
        smr.BakeMesh(baked, false);
        int body = 0;
        var mats = smr.sharedMaterials;
        for (int s = 0; s < mats.Length && s < baked.subMeshCount; s++)
            if (mats[s] != null && mats[s].name.Contains("Skin")) { body = s; break; }
        Vector3[] verts = baked.vertices;
        Vector3 mn = Vector3.one * 1e6f, mx = -mn;
        foreach (int i in baked.GetIndices(body))
        {
            Vector3 p = motion.InverseTransformPoint(smr.transform.TransformPoint(verts[i]));
            mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
        }
        Destroy(baked);
        // width and height fitted separately: the body fills the cube's cell (she is a little squatter than her model)
        float k = bodyWidth / Mathf.Max(0.001f, Mathf.Max(mx.x - mn.x, mx.z - mn.z));
        float ky = bodyHeight / Mathf.Max(0.001f, mx.y);   // from the ground contact to the top of the body
        model.localScale = Vector3.Scale(model.localScale, new Vector3(k, ky, k));
        Vector3 centre = (mn + mx) * 0.5f;
        model.localPosition = new Vector3(-centre.x * k, 0f, -centre.z * k);   // her pivot is the ground contact
        if (fur != null) fur.ResetSimulation();
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { Transform f = FindDeep(c, name); if (f != null) return f; }
        return null;
    }

    // ------------------------------------------------------------------ game events

    void OnCoin(int n, Vector3 at)
    {
        popV += 2.4f;
        if (mood != Mood.Surprised) Feel(Mood.Happy, 0.4f);
    }

    void OnCloseCall(int a, int b, int c)
    {
        Feel(Mood.Surprised, 0.45f);
        Glance(0.45f);
    }

    void OnRescued(CubeRailRunner.RescueInfo info)
    {
        Feel(Mood.Surprised, 0.35f);
        squashV -= 3f;
    }

    // A new leg (corner): settle onto it.
    void OnLanded()
    {
        if (runner == null || !runner.IsRunning || hopT >= 0f) return;
        squashV -= 1.4f;
        if (animator != null) animator.CrossFadeInFixedTime(LandState, 0.04f);
    }

    void Feel(Mood m, float seconds) { mood = m; faceT = 0f; faceLen = seconds; }

    void Glance(float seconds) { glanceTarget = shoulderGlance; glanceUntil = Time.time + seconds; }

    // ------------------------------------------------------------------ per frame

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (!attached) { Attach(); if (!attached) return; }
        if (dt <= 0f) return;

        HideOthers();
        Mirror();

        bool running = runner.IsRunning;
        bool over = runner.IsGameOver;
        float speed01 = running ? Mathf.InverseLerp(14f, 30f, runner.CurrentMoveSpeed) : 0f;
        Camera cam = Camera.main;

        DetectTurns(dt);
        UpdateAnimator(running, over, speed01);
        UpdateGlances(dt, running, cam);

        Spring(ref squash, ref squashV, 0f, 520f, 20f, dt);
        Spring(ref roll, ref rollV, 0f, 300f, 15f, dt);
        Spring(ref pop, ref popV, 0f, 380f, 16f, dt);
        if (Time.time > glanceUntil) glanceTarget = 0f;
        Spring(ref glance, ref glanceV, glanceTarget, 220f, 26f, dt);

        // --- trot: one little hop per step, pressed into the rail at contact, waddling side to side
        float step = 0f, stepSq = 0f, lean = 0f, wad = 0f;
        if (running && hopT < 0f)
        {
            stepPhase += dt * Mathf.Lerp(stepRate.x, stepRate.y, speed01);
            float s = Mathf.Sin(stepPhase * Mathf.PI * 2f);
            float up = Mathf.Abs(s);
            step = up * up * stepHeight;                          // light in the air, quick off the ground
            stepSq = (1f - up) * (1f - up) * stepSquash;         // pressed down at contact
            wad = waddle * Mathf.Sin(stepPhase * Mathf.PI);      // one side per step
            lean = Mathf.Lerp(forwardLean.x, forwardLean.y, speed01);
        }
        else if (!running && !over)
        {
            // before the run: breathing and a little happy hop now and then
            stepPhase = 0f;
            stepSq = 0.02f * (0.5f + 0.5f * Mathf.Sin(Time.time * 2.4f));
            if (idleHopT < 0f && (nextIdleHop -= dt) <= 0f) { idleHopT = 0f; nextIdleHop = Random.Range(2.2f, 4.5f); }
            if (idleHopT >= 0f)
            {
                idleHopT += dt;
                float u = Mathf.Clamp01(idleHopT / 0.32f);
                step = 0.18f * Mathf.Sin(u * Mathf.PI);
                if (u >= 1f) { idleHopT = -1f; squashV -= 2.2f; }
            }
        }

        // --- hop (side switch): over the edge, stretch on take-off
        float lift = 0f, stretch = 0f;
        if (hopT >= 0f)
        {
            hopT += dt;
            float u = Mathf.Clamp01(hopT / hopDur);
            lift = hopLift * Mathf.Sin(u * Mathf.PI);
            stretch = hopStretch * Mathf.Sin(Mathf.Min(1f, u * 2f) * Mathf.PI) * (1f - u * 0.5f);
            if (u >= 1f) Land();
        }
        float twirl = 0f;
        if (twirlT >= 0f)
        {
            twirlT += dt;
            float u = Mathf.Clamp01(twirlT / Mathf.Max(0.01f, flipTwirl));
            twirl = 360f * (1f - (1f - u) * (1f - u) * (1f - u)) * hopSign;
            if (u >= 1f) twirlT = -1f;
        }

        // --- idle: turned to the player before the run starts
        float lookTarget = 0f;
        if (runner.IsWaitingToStart && cam != null)
        {
            Vector3 toCam = Vector3.ProjectOnPlane(cam.transform.position - transform.position, transform.up);
            if (toCam.sqrMagnitude > 0.01f)
                lookTarget = Mathf.Clamp(Vector3.SignedAngle(transform.forward, toCam, transform.up), -idleLookBack, idleLookBack);
        }
        Spring(ref lookBack, ref lookBackVel, lookTarget, running ? 900f : 60f, running ? 60f : 14f, dt);

        // --- assemble (Motion scales about the rail: she never sinks or floats)
        float glanceSide = cam != null && Vector3.Dot(cam.transform.position - transform.position, transform.right) < 0f ? -1f : 1f;
        yaw.localRotation = Quaternion.Euler(0f, yawOffset + lookBack + twirl + glance * glanceSide, 0f) * Quaternion.Euler(0f, 0f, bankAngle);
        float sy = 1f + stretch - squash - stepSq + pop * 0.5f;
        float sxz = 1f - stretch * 0.5f + squash * 0.6f + stepSq * 0.6f + pop * 0.5f;
        motion.localScale = new Vector3(sxz, Mathf.Max(0.5f, sy), sxz);
        motion.localPosition = new Vector3(0f, lift + step, 0f);
        motion.localRotation = Quaternion.Euler(lean, 0f, roll + wad);

        UpdateFace(dt, speed01, over, cam);
        if (fur != null) fur.grounded = hopT < 0f && step < 0.02f;   // fur may hang below her only while she is off the rail

        wasRunning = running;
        wasOver = over;
    }

    // Look at the player now and then: eye contact when the camera is in front of her, a glance over the shoulder when
    // it is behind (only on a straight, never near a corner, so the run always reads).
    void UpdateGlances(float dt, bool running, Camera cam)
    {
        if (!running || cam == null) return;
        if ((nextGlance -= dt) > 0f) return;
        nextGlance = Random.Range(glanceEvery.x, glanceEvery.y);
        Vector3 toCam = cam.transform.position - transform.position;
        bool camInFront = Vector3.Dot(toCam, runner.transform.forward) > 0f;
        if (camInFront) eyeContactUntil = Time.time + 0.7f;
        else if (TimeToCorner > 0.9f && hopT < 0f) Glance(0.5f);
    }

    /// <summary>Seconds until the runner reaches the end of the current leg (99 when unknown). From the path itself -
    /// CubeRailRunner.SecondsToCorner is only filled in with the fair-corner difficulty profile.</summary>
    public float TimeToCorner
    {
        get
        {
            if (runner == null || gen == null) return 99f;
            var seg = gen.PlayerSegment;
            if (seg == null || seg.direction.sqrMagnitude < 0.01f) return 99f;
            float dist = Vector3.Dot(seg.endPosition - runner.transform.position, seg.direction.normalized) + 0.5f * gen.cubeSize;
            return Mathf.Max(0f, dist) / Mathf.Max(1f, runner.CurrentMoveSpeed);
        }
    }

    static void Spring(ref float x, ref float v, float target, float k, float c, float dt)
    {
        int n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 8);
        float h = dt / n;
        for (int i = 0; i < n; i++) { v += ((target - x) * k - v * c) * h; x += v * h; }
    }

    // The root takes a new orientation the instant the game rolls or turns; read what happened from it.
    void DetectTurns(float dt)
    {
        Transform r = runner.transform;
        Vector3 up = r.up, fwd = r.forward;
        float upAngle = Vector3.Angle(prevUp, up);
        if (upAngle > 45f && runner.IsRunning && runner.IsRevolving)
        {
            float signed = Vector3.SignedAngle(prevUp, up, fwd);
            BeginHop(upAngle > 135f, signed >= 0f ? 1f : -1f);
        }
        else if (Vector3.Dot(prevUp, up) > 0.9f)
        {
            float d = Vector3.SignedAngle(Vector3.ProjectOnPlane(prevFwd, up), fwd, up);
            if (Mathf.Abs(d) > 1f) yawOffset -= d;   // she keeps facing the old way and turns into the new leg
        }
        prevUp = up;
        prevFwd = fwd;

        if (!runner.IsRunning && !runner.IsGameOver) { yawOffset = 0f; yawVel = 0f; }
        float before = yawOffset;
        float w = 2f / Mathf.Max(0.01f, turnTime);
        int n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 8);
        float h = dt / n;
        for (int i = 0; i < n; i++) { yawVel += (-w * w * yawOffset - 2f * w * yawVel) * h; yawOffset += yawVel * h; }
        float turnRate = (yawOffset - before) / Mathf.Max(1e-4f, dt);
        bankAngle = Mathf.Lerp(bankAngle, Mathf.Clamp(turnRate * bank, -20f, 20f), 1f - Mathf.Exp(-dt * 25f));
    }

    void BeginHop(bool flip, float sign)
    {
        hopDur = flip ? 0.12f : Mathf.Max(0.04f, runner.LiveRollTime);
        if (rig != null) hopDur *= rig.jumpTimeScale;
        hopLift = flip ? flipHopHeight : hopHeight;
        hopSign = sign;
        hopT = 0f;
        squash = Mathf.Min(squash, 0f);
        squashV = 0f;
        glanceTarget = 0f;
        if (flip && flipTwirl > 0f) twirlT = 0f;
        if (animator != null) animator.CrossFadeInFixedTime(HopState, 0.03f);
        Feel(Mood.Excited, hopDur + 0.15f);
    }

    void Land()
    {
        hopT = -1f;
        squashV -= landSquash * 26f;
        rollV += -hopSign * rollFollowThrough * 22f;
        stepPhase = 0f;
        if (animator != null && runner.IsRunning) animator.CrossFadeInFixedTime(RunState, 0.06f);
    }

    void UpdateAnimator(bool running, bool over, float speed01)
    {
        if (animator == null) return;
        animator.SetFloat(RunSpeedId, Mathf.Lerp(runAnimSpeed.x, runAnimSpeed.y, speed01));
        if (over && !wasOver) { animator.CrossFadeInFixedTime(SmashState, 0.05f); hopT = -1f; twirlT = -1f; Feel(Mood.Hurt, 999f); }
        else if (running && !wasRunning) { animator.CrossFadeInFixedTime(RunState, 0.08f); popV += 2.5f; }
        else if (!running && !over && (wasRunning || wasOver)) { animator.CrossFadeInFixedTime(IdleState, 0.15f); ResetMotion(); }
    }

    void ResetMotion()
    {
        hopT = twirlT = idleHopT = -1f;
        squash = squashV = roll = rollV = pop = popV = glance = glanceV = 0f;
        yawOffset = yawVel = bankAngle = 0f;
        mood = Mood.None; faceT = -1f;
        if (fur != null) fur.ResetSimulation();
    }

    void UpdateFace(float dt, float speed01, bool over, Camera cam)
    {
        // bigger eyes, every frame after the Animator (her clips key the eye / lid scale)
        if (eyeParts != null)
            for (int i = 0; i < eyeParts.Length; i++)
                if (eyeParts[i] != null) eyeParts[i].localScale = eyePartScale[i] * eyeScale;

        if (face == null) return;
        // rest: wide awake; at speed a touch narrower with the brows set (determined)
        float lidOpen = Mathf.Lerp(eyesOpen, eyesOpen * 0.4f, speed01), browAngry = 0.35f * speed01, browRaise = 0.15f * (1f - speed01);
        if (faceT >= 0f)
        {
            faceT += dt;
            float k = Mathf.Clamp01(faceT / Mathf.Max(0.01f, faceLen));
            float env = Mathf.Sin(Mathf.Min(1f, k * 4f) * Mathf.PI * 0.5f) * (1f - k * k);
            switch (mood)
            {
                case Mood.Happy: lidOpen = Mathf.Lerp(lidOpen, -0.45f, env); browRaise = Mathf.Lerp(browRaise, 0.7f, env); browAngry = Mathf.Lerp(browAngry, -0.3f, env); break;
                case Mood.Surprised: lidOpen = Mathf.Lerp(lidOpen, 1f, env); browRaise = Mathf.Lerp(browRaise, 1f, env); browAngry = Mathf.Lerp(browAngry, -0.6f, env); break;
                case Mood.Excited: lidOpen = Mathf.Lerp(lidOpen, 0.9f, env); browRaise = Mathf.Lerp(browRaise, 0.6f, env); browAngry = Mathf.Lerp(browAngry, 0f, env); break;
            }
            if (k >= 1f && mood != Mood.Hurt) { faceT = -1f; mood = Mood.None; }
        }
        if (over) { lidOpen = 1f; browRaise = 1f; browAngry = -0.8f; }
        float rate = 1f - Mathf.Exp(-dt * 30f);
        face.lidOpen = Mathf.Lerp(face.lidOpen, lidOpen, rate);
        face.browAngry = Mathf.Lerp(face.browAngry, browAngry, rate);
        face.browRaise = Mathf.Lerp(face.browRaise, browRaise, rate);

        // eyes: eye contact with the player, or down the leg and toward the next one as the corner comes
        bool contact = cam != null && (Time.time < eyeContactUntil || runner.IsWaitingToStart || glance > 20f);
        face.lookTarget = contact ? cam.transform : null;
        float lx = 0f;
        if (!contact && runner.IsRunning && gen != null)
        {
            var next = gen.SegmentAt(gen.PlayerSegmentIndex + 1);
            float t = TimeToCorner;
            if (next != null && t < lookAheadSeconds)
            {
                Vector3 nd = Vector3.ProjectOnPlane(next.direction, runner.transform.up);
                float ang = Vector3.SignedAngle(runner.transform.forward, nd, runner.transform.up);
                lx = Mathf.Clamp(ang / 90f, -1f, 1f) * (1f - Mathf.Clamp01(t / lookAheadSeconds));
            }
        }
        face.lookX = Mathf.Lerp(face.lookX, lx, rate);
        face.lookY = Mathf.Lerp(face.lookY, 0f, rate);
    }

    // The cube itself (and 3.7's eye block / cape, a shop skin) never draws in 3.9.
    void HideOthers()
    {
        foreach (Renderer r in hidden) if (r != null && !r.forceRenderingOff) r.forceRenderingOff = true;
        if (soulCheckFrames > 0)
        {
            soulCheckFrames--;
            foreach (SoulCharacter s in runner.GetComponentsInChildren<SoulCharacter>(true)) s.gameObject.SetActive(false);
            foreach (PlayerTotem t in runner.GetComponentsInChildren<PlayerTotem>(true)) t.gameObject.SetActive(false);
            foreach (MeshRenderer r in runner.GetComponentsInChildren<MeshRenderer>(true))
                if (!r.transform.IsChildOf(transform) && !hidden.Contains(r)) hidden.Add(r);
        }
    }

    // Lumi is drawn whenever the game would draw the cube (the death shatter and the restart rewind hide it).
    void Mirror()
    {
        bool show = cubeRenderer == null || cubeRenderer.enabled;
        if (show == shown) return;
        shown = show;
        foreach (Renderer r in mine) if (r != null) r.enabled = show;
        if (show) ResetMotion();
    }
}
