using UnityEngine;

/// <summary>
/// MainScene prop camera 3.8: Boxi (MainCharacter/BoxiCharacter) is the runner. Purely visual - the cube's physics, timing,
/// input and collider are untouched; Boxi only rides on the runner's visual rig and acts out what the game does.
///
///   MainPlayer (root: Rigidbody, CubeRailRunner)
///    -> VisualWrapper -> PivotContainer      (RunnerVisualRig: corner drift, the roll onto a new face)
///        -> BoxiRunner (this, at the cube's bottom face = the rail)          moved here at runtime
///         -> Yaw        smoothed facing (corners turn her fluidly, idle she looks back at the player)
///          -> Motion    hop / squash / lean / run bounce
///           -> BoxiCharacter (Animator + BoxiFace + BoxiFurPhysics + BoxiBlinker), auto-fitted to the cube's size
///
/// Animation layering:
///   - Animator (BoxiRunner38.controller): Run_InPlace_v2 with its speed tied to the run speed (fur / tuft / bow / lids),
///     Idle before the run, Jump_Air (fur flared) while hopping, Land on a new leg, Smash on a crash, additive Blink
///   - code, on top: the ball's run bounce + contact squash + forward lean (the v2 clip keeps the body still), the side-switch
///     hop (lift over the edge, stretch, ball-up, spring squash on landing, follow-through lean, a twirl on a double-tap flip),
///     the turn bank, coin / close-call reactions, the face (determined squint at speed, surprise, joy)
///   - BoxiFurPhysics springs everything after that, with the real run velocity as airflow
/// The hop always lasts exactly as long as the game's roll (0.10 s step / 0.12 s flip): nothing is slower than the cube was.
/// </summary>
[DefaultExecutionOrder(950)]   // after RunnerVisualRig (0), before BoxiFace (9000) / BoxiFurPhysics (10000)
[DisallowMultipleComponent]
public class BoxiRunner : MonoBehaviour
{
    [Header("Parts (built by Tools > Boxi Runner)")]
    public Transform yaw;
    public Transform motion;
    public Transform model;
    public Animator animator;
    public Boxi.BoxiFace face;
    public Boxi.BoxiFurPhysics fur;
    public Light glowLight;

    [Header("Size")]
    [Tooltip("Boxi's widest extent, fur tips included, in cube units (the cube is 1). Measured in the game camera: 1.15 covered " +
             "only 43% of the cube's on-screen area (the iso cube shows three faces, the fluffy ball reads small); 1.75 matches the " +
             "cube's screen area 1:1 - the same visual weight the game was tuned with.")]
    [Range(0.6f, 2.4f)] public float fitSize = 1.75f;
    [Tooltip("How far the lowest fur tips sink into the rail (cube units), so she sits on it rather than floats.")]
    [Range(0f, 0.3f)] public float sink = 0.1f;

    [Header("Run")]
    [Tooltip("Run_InPlace_v2 playback rate at the slowest / fastest run speed.")]
    public Vector2 runAnimSpeed = new Vector2(1.1f, 2.2f);
    [Tooltip("Bounces per second at the slowest / fastest run speed.")]
    public Vector2 bounceRate = new Vector2(3.6f, 5.4f);
    [Range(0f, 0.15f)] public float bounceHeight = 0.05f;
    [Range(0f, 0.15f)] public float bounceSquash = 0.06f;
    [Tooltip("Her own weight flattening her on the floor (share of height). Leo-style: heavy, grounded, never floaty.")]
    [Range(0f, 0.25f)] public float restWeight = 0f;
    [Tooltip("Forward / back rock with each roll (degrees).")]
    [Range(0f, 10f)] public float rockDegrees = 0f;
    [Tooltip("How much of a true roll the fur shows (1 = fur turns over exactly as a ball of her size would; the face stays).")]
    [Range(0f, 1f)] public float furRollShare = 0.35f;
    [Tooltip("How far each fur tuft is turned by the rolling wave (degrees).")]
    [Range(0f, 40f)] public float furRollAngle = 0f;
    [Tooltip("Forward lean (degrees) at the slowest / fastest run speed.")]
    public Vector2 forwardLean = new Vector2(3f, 8f);

    [Header("Side switch (hop)")]
    [Range(0f, 0.8f)] public float hopHeight = 0.32f;
    [Range(0f, 0.8f)] public float flipHopHeight = 0.42f;
    [Range(0f, 0.4f)] public float hopStretch = 0.16f;
    [Range(0f, 0.5f)] public float landSquash = 0.24f;
    [Tooltip("Degrees she keeps rolling past the face after landing, then springs back.")]
    [Range(0f, 30f)] public float rollFollowThrough = 13f;
    [Tooltip("A full twirl on a double-tap flip (seconds; 0 = off). Visual only - she faces forward again right after.")]
    [Range(0f, 0.6f)] public float flipTwirl = 0.3f;

    [Header("Facing")]
    [Tooltip("Seconds for her to face a new leg after a corner (critically damped).")]
    [Range(0.01f, 0.3f)] public float turnTime = 0.07f;
    [Tooltip("Bank into a turn, degrees per (degree / second) of turning.")]
    [Range(0f, 0.1f)] public float bank = 0.025f;
    [Tooltip("Before the run, she turns this far toward the camera to look at the player.")]
    [Range(0f, 180f)] public float idleLookBack = 150f;

    [Header("Glow")]
    [ColorUsage(false, true)] public Color glowColor = new Color(0.82f, 0.5f, 1f);
    [Range(0f, 6f)] public float lightIntensity = 2.2f;
    [Range(0.5f, 5f)] public float lightRange = 2.4f;
    [Tooltip("How much a coin / close call / landing brightens her glow.")]
    [Range(0f, 1f)] public float pulseOnCoin = 0.6f;

    [Header("Air on the fur")]
    [Tooltip("BoxiFurPhysics.windStrength: how far the run's airflow sweeps the fur / tuft / bow back.")]
    [Range(0f, 1f)] public float furWind = 0.12f;
    [Tooltip("Extra airflow on top of the real motion, as a share of the run speed (0 = only the real motion).")]
    [Range(0f, 1f)] public float furAirflow = 0f;

    static readonly int PulseId = Shader.PropertyToID("_Pulse");
    static readonly int GlowId = Shader.PropertyToID("_GlowColor");
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
    MaterialPropertyBlock mpb;
    readonly System.Collections.Generic.List<Renderer> hidden = new System.Collections.Generic.List<Renderer>();
    bool attached, fitted, shown = true, wasRunning, wasOver;
    int soulCheckFrames = 120;

    Vector3 prevUp, prevFwd;
    float yawOffset, yawVel, bankAngle, lookBack, lookBackVel;

    // hop
    float hopT = -1f, hopDur = 0.1f, hopLift, hopSign, twirlT = -1f;
    // springs (value, velocity)
    float squash, squashV, roll, rollV, pop, popV, glance, glanceV;
    float bouncePhase, pulse, faceT = -1f, faceLen;
    enum Mood { None, Happy, Surprised, Excited }
    Mood mood;
    float glanceTarget, glanceUntil;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        mine = GetComponentsInChildren<Renderer>(true);
        // The fur feels the real motion (corners, hops, rolls, run bounce) but rides along with the steady run speed;
        // the run itself only shows as airflow sweeping it back.
        if (fur != null) { fur.teleportDistance = 3f; fur.useTransformVelocity = true; fur.carryWithBody = true; fur.windStrength = furWind; }
    }

    void Start()
    {
        runner = GetComponentInParent<CubeRailRunner>();
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        coins = FindFirstObjectByType<CoinCollector>();
        if (coins != null) coins.CoinCollected += OnCoin;
        if (gen != null) gen.CloseCallAwarded += OnCloseCall;
        if (runner != null) { runner.Landed += OnLanded; runner.Rescued += OnRescued; }
        if (glowLight != null) { glowLight.color = glowColor; glowLight.range = lightRange; glowLight.shadows = LightShadows.None; }
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

    // Ride the visual rig's roll (not CubeMesh: that one also pitches), sitting on the cube's bottom face = the rail.
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

    // Scale to the cube, feet (lowest fur) on the rail, face along +Z. Measured once from the animated pose.
    void Fit()
    {
        if (fitted || model == null) return;
        fitted = true;
        if (animator != null) animator.Update(0f);
        Transform glasses = FindDeep(model, "Glasses"), upper = FindDeep(model, "Body_Upper");
        if (glasses != null && upper != null)
        {
            Vector3 f = motion.InverseTransformDirection(glasses.position - upper.position);
            f.y = 0f;
            if (f.sqrMagnitude > 1e-6f) model.localRotation = Quaternion.FromToRotation(f.normalized, Vector3.forward) * model.localRotation;
        }
        var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null) return;
        model.localPosition = Vector3.zero;
        var baked = new Mesh();
        smr.BakeMesh(baked, false);
        Vector3 mn = Vector3.one * 1e6f, mx = -mn;
        foreach (Vector3 v in baked.vertices)
        {
            Vector3 p = motion.InverseTransformPoint(smr.transform.TransformPoint(v));
            mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
        }
        Destroy(baked);
        Vector3 size = mx - mn;
        float k = fitSize / Mathf.Max(0.001f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
        model.localScale *= k;
        Vector3 centre = (mn + mx) * 0.5f;
        model.localPosition = new Vector3(-centre.x * k, -mn.y * k - sink, -centre.z * k);
        if (glowLight != null) glowLight.transform.localPosition = new Vector3(0f, size.y * k * 0.5f, 0f);
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
        pulse = Mathf.Max(pulse, pulseOnCoin);
        popV += 2.2f;
        if (mood != Mood.Surprised) Feel(Mood.Happy, 0.35f);
    }

    void OnCloseCall(int a, int b, int c)
    {
        pulse = 1f;
        Feel(Mood.Surprised, 0.4f);
        Glance(0.45f);
    }

    void OnRescued(CubeRailRunner.RescueInfo info)
    {
        Feel(Mood.Surprised, 0.3f);
        squashV -= 3f;
    }

    // A new leg (corner): a little land on it.
    void OnLanded()
    {
        if (runner == null || !runner.IsRunning || hopT >= 0f) return;
        squashV -= 1.4f;
        if (animator != null) animator.CrossFadeInFixedTime(LandState, 0.04f);
    }

    void Feel(Mood m, float seconds) { mood = m; faceT = 0f; faceLen = seconds; }

    // A quick look back over the shoulder at the player (camera D is behind her).
    void Glance(float seconds) { glanceTarget = 40f; glanceUntil = Time.time + seconds; }

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

        DetectTurns(dt);
        UpdateAnimator(running, over, speed01);

        // --- springs
        Spring(ref squash, ref squashV, 0f, 520f, 20f, dt);
        Spring(ref roll, ref rollV, 0f, 300f, 15f, dt);
        Spring(ref pop, ref popV, 0f, 380f, 16f, dt);
        pulse = Mathf.MoveTowards(pulse, 0f, dt * 2.2f);
        if (Time.time > glanceUntil) glanceTarget = 0f;
        Spring(ref glance, ref glanceV, glanceTarget, 260f, 30f, dt);

        // --- grounded roll (Leo's Fortune): she never floats - her weight flattens her bottom on the floor, she barely
        // leaves it, rocks forward/back with each roll, and her fur turns over the top like a rolling ball's
        float bounce = 0f, bounceSq = 0f, lean = 0f;
        float weight = restWeight * (hopT >= 0f ? 0.2f : 1f);   // the floor holds her up; mid-hop she rounds out
        if (running)
        {
            bouncePhase += dt * Mathf.Lerp(bounceRate.x, bounceRate.y, speed01);
            float s = Mathf.Sin(bouncePhase * Mathf.PI * 2f);
            bounce = Mathf.Abs(s) * bounceHeight;
            bounceSq = (1f - Mathf.Abs(s)) * (1f - Mathf.Abs(s)) * bounceSquash;   // pressed into the floor at contact
            lean = Mathf.Lerp(forwardLean.x, forwardLean.y, speed01) + rockDegrees * Mathf.Sin(bouncePhase * Mathf.PI * 2f + 1.2f);
            rollPhase += runner.CurrentMoveSpeed * dt / Mathf.Max(0.1f, fitSize * 0.5f) * Mathf.Rad2Deg * furRollShare;
        }
        else if (!over) { bouncePhase = 0f; weight = restWeight * (1f + 0.15f * Mathf.Sin(Time.time * 2.2f)); }   // breathing, settled
        RollFur();

        // --- hop (side switch): lift over the edge, stretch on take-off, ball up mid-air
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
            twirl = 360f * (1f - (1f - u) * (1f - u) * (1f - u)) * hopSign;   // fast out, eases into forward
            if (u >= 1f) twirlT = -1f;
        }

        // --- idle: look back at the player before the run starts
        float lookTarget = 0f;
        if (runner.IsWaitingToStart && Camera.main != null)
        {
            Vector3 toCam = Vector3.ProjectOnPlane(Camera.main.transform.position - transform.position, transform.up);
            if (toCam.sqrMagnitude > 0.01f)
                lookTarget = Mathf.Clamp(Vector3.SignedAngle(transform.forward, toCam, transform.up), -idleLookBack, idleLookBack);
        }
        Spring(ref lookBack, ref lookBackVel, lookTarget, running ? 900f : 60f, running ? 60f : 14f, dt);

        // --- assemble
        float glanceSide = Camera.main != null && Vector3.Dot(Camera.main.transform.position - transform.position, transform.right) < 0f ? -1f : 1f;
        yaw.localRotation = Quaternion.Euler(0f, yawOffset + lookBack + twirl + glance * glanceSide, 0f) * Quaternion.Euler(0f, 0f, bankAngle);
        float sy = 1f + stretch - squash - bounceSq - weight + pop * 0.6f;
        float sxz = 1f - stretch * 0.55f + squash * 0.7f + bounceSq * 0.6f + weight * 0.6f + pop * 0.6f;
        motion.localScale = new Vector3(sxz, Mathf.Max(0.4f, sy), sxz);
        motion.localPosition = new Vector3(0f, lift + bounce, 0f);
        motion.localRotation = Quaternion.Euler(lean, 0f, roll);

        UpdateFace(dt, speed01, over);
        UpdateGlow(running);
        if (fur != null) fur.externalVelocity = running ? runner.transform.forward * (runner.CurrentMoveSpeed * furAirflow) : Vector3.zero;

        wasRunning = running;
        wasOver = over;
    }

    float rollPhase;
    Transform[] furBones;
    Transform furCentre;

    // The roll travels over her coat: each tuft tips forward as it passes over the top and back underneath, like the
    // surface of a rolling ball, while the face (eyes, glasses) stays put. Runs after the Animator and before
    // BoxiFurPhysics, so the springs swing on top of it.
    void RollFur()
    {
        if (model == null || furRollAngle <= 0f) return;
        if (furBones == null)
        {
            var list = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Fur_")) list.Add(t);
                if (t.name == "Body_Upper") furCentre = t;
            }
            furBones = list.ToArray();
        }
        if (furCentre == null) return;
        Vector3 right = motion.right, up = motion.up, fwd = motion.forward;
        Vector3 c = furCentre.position;
        float ph = rollPhase * Mathf.Deg2Rad;
        foreach (Transform b in furBones)
        {
            Vector3 d = b.position - c;
            float az = Mathf.Atan2(Vector3.Dot(d, fwd), Vector3.Dot(d, up));   // angle round the roll axis
            float a = furRollAngle * Mathf.Sin(ph - az * 2f);
            b.rotation = Quaternion.AngleAxis(a, right) * b.rotation;
        }
    }

    static void Spring(ref float x, ref float v, float target, float k, float c, float dt)
    {
        // semi-implicit, sub-stepped so a 30 fps frame stays stable
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
            // a side switch: sign = which way she rolled about the heading
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
        // critically damped turn into the new heading
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
        squash = Mathf.Min(squash, 0f);      // a tap during the landing squash: take off straight away
        squashV = 0f;
        if (flip && flipTwirl > 0f) twirlT = 0f;
        if (animator != null) animator.CrossFadeInFixedTime(HopState, 0.03f);
        Feel(Mood.Excited, hopDur + 0.12f);
        pulse = Mathf.Max(pulse, 0.25f);
    }

    void Land()
    {
        hopT = -1f;
        squashV -= landSquash * 26f;                  // spring squash with one soft overshoot
        rollV += -hopSign * rollFollowThrough * 22f;  // keeps rolling a touch, springs back
        if (animator != null && runner.IsRunning) animator.CrossFadeInFixedTime(RunState, 0.06f);
    }

    void UpdateAnimator(bool running, bool over, float speed01)
    {
        if (animator == null) return;
        animator.SetFloat(RunSpeedId, Mathf.Lerp(runAnimSpeed.x, runAnimSpeed.y, speed01));
        if (over && !wasOver) { animator.CrossFadeInFixedTime(SmashState, 0.05f); hopT = -1f; twirlT = -1f; }
        else if (running && !wasRunning) { animator.CrossFadeInFixedTime(RunState, 0.08f); popV += 2.5f; }
        else if (!running && !over && (wasRunning || wasOver)) { animator.CrossFadeInFixedTime(IdleState, 0.15f); ResetMotion(); }
    }

    void ResetMotion()
    {
        hopT = twirlT = -1f;
        squash = squashV = roll = rollV = pop = popV = glance = glanceV = 0f;
        yawOffset = yawVel = bankAngle = 0f;
        if (fur != null) fur.ResetSimulation();
    }

    void UpdateFace(float dt, float speed01, bool over)
    {
        if (face == null) return;
        float lidOpen = Mathf.Lerp(0f, -0.3f, speed01), browAngry = 0.4f * speed01, browRaise = 0f;
        if (faceT >= 0f)
        {
            faceT += dt;
            float k = Mathf.Clamp01(faceT / Mathf.Max(0.01f, faceLen));
            float env = Mathf.Sin(Mathf.Min(1f, k * 4f) * Mathf.PI * 0.5f) * (1f - k * k);
            switch (mood)
            {
                case Mood.Happy: lidOpen = Mathf.Lerp(lidOpen, -0.6f, env); browRaise = 0.5f * env; browAngry = Mathf.Lerp(browAngry, -0.2f, env); break;
                case Mood.Surprised: lidOpen = Mathf.Lerp(lidOpen, 1f, env); browRaise = env; browAngry = Mathf.Lerp(browAngry, -0.6f, env); break;
                case Mood.Excited: lidOpen = Mathf.Lerp(lidOpen, 0.5f, env); browRaise = 0.4f * env; break;
            }
            if (k >= 1f) { faceT = -1f; mood = Mood.None; }
        }
        if (over) { lidOpen = 1f; browRaise = 1f; browAngry = -0.8f; }
        float rate = 1f - Mathf.Exp(-dt * 30f);
        face.lidOpen = Mathf.Lerp(face.lidOpen, lidOpen, rate);
        face.browAngry = Mathf.Lerp(face.browAngry, browAngry, rate);
        face.browRaise = Mathf.Lerp(face.browRaise, browRaise, rate);
        // eyes: down the leg, and toward the next one as the corner comes
        face.lookX = 0f;
        face.lookY = Mathf.Lerp(face.lookY, runner.IsRunning && !over ? -0.05f : 0f, rate);
    }

    void UpdateGlow(bool running)
    {
        float p = Mathf.Clamp01(pulse);
        foreach (Renderer r in mine)
        {
            if (r == null || r is not SkinnedMeshRenderer) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetFloat(PulseId, p);
            mpb.SetColor(GlowId, glowColor);
            r.SetPropertyBlock(mpb);
        }
        if (glowLight != null)
            glowLight.intensity = lightIntensity * (1f + p * 0.8f) * (shown ? 1f : 0f);
    }

    // The cube itself (and 3.7's eye block / cape, a shop skin) never draws in 3.8.
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

    // Boxi is drawn whenever the game would draw the cube (the death shatter and the restart rewind hide it).
    void Mirror()
    {
        bool show = cubeRenderer == null || cubeRenderer.enabled;
        if (show == shown) return;
        shown = show;
        foreach (Renderer r in mine) if (r != null) r.enabled = show;
        if (show) ResetMotion();
    }
}
