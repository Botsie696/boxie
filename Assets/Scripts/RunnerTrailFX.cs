using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The runner's trail for TrialScene2: turns the TrailRenderer on the runner into a glowing ribbon in the
/// chasm's cool palette and adds gameplay feedback around it:
///  - speed: the ribbon widens, brightens and shifts from aqua to violet as the runner speeds up
///  - two wisps braiding around the runner, and motes left hanging in the air behind them
///  - bursts on a side switch, a turn and a coin pickup (sparks, a ring, a flash down the ribbon)
///  - an ember flicker with sputtering sparks while off the path, and a last burst when the run ends
/// Every part has its own switch. Turning the component off puts the original trail back.
///
/// PathDepthShading sets the ribbon's length (TrailRenderer.time) every frame; this never touches it.
/// Uses its own System.Random so the path generator's UnityEngine.Random sequence is unchanged.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CubeRailRunner))]
public class RunnerTrailFX : MonoBehaviour
{
    [Header("Materials")]
    [Tooltip("Runner/TrailFX Ribbon - the main trail and the wisps.")]
    public Material ribbonMaterial;
    [Tooltip("Runner/TrailFX Spark with Shape = Spark - motes and burst sparks.")]
    public Material sparkMaterial;
    [Tooltip("Runner/TrailFX Spark with Shape = Ring - the shockwave rings.")]
    public Material ringMaterial;

    [Header("Ribbon")]
    [Tooltip("Restyle the runner's own trail. Off = the original trail; the other effects keep running.")]
    public bool restyleTrail = true;
    [Range(0.1f, 2f)] public float ribbonWidth = 1.1f;
    [Range(0f, 4f)] public float ribbonIntensity = 0.95f;
    [Tooltip("How much the ribbon shimmers.")]
    [Range(0f, 1f)] public float shimmer = 0.55f;
    [ColorUsage(false, true)] public Color headColor = new Color(0.75f, 1f, 1f);
    [ColorUsage(false, true)] public Color midColor = new Color(0.2f, 0.78f, 1f);
    [ColorUsage(false, true)] public Color tailColor = new Color(0.38f, 0.28f, 1f);

    [Header("Speed")]
    [Tooltip("The speed the ribbon colours above belong to.")]
    public float calmSpeed = 4f;
    [Tooltip("The speed the fast colours below are fully reached at.")]
    public float fastSpeed = 17f;
    [Range(0f, 1f)] public float fastWidthBoost = 0.3f;
    [Range(0f, 2f)] public float fastIntensityBoost = 0.3f;
    [ColorUsage(false, true)] public Color fastHeadColor = new Color(1f, 0.78f, 1f);
    [ColorUsage(false, true)] public Color fastMidColor = new Color(0.72f, 0.35f, 1f);
    [ColorUsage(false, true)] public Color fastTailColor = new Color(0.18f, 0.3f, 1f);

    [Header("Twin Wisps")]
    public bool wisps = true;
    [Tooltip("Seconds of wisp trail (never longer than the ribbon).")]
    [Range(0.1f, 1.5f)] public float wispLength = 0.6f;
    [Range(0.02f, 0.5f)] public float wispWidth = 0.22f;
    [Tooltip("How far out to the sides the wisps swing.")]
    [Range(0.2f, 1.5f)] public float wispSpread = 0.75f;
    [Tooltip("How far the braid sits above the face the runner is on.")]
    [Range(0f, 1f)] public float wispLift = 0.25f;
    [Tooltip("Units of track per full turn of the braid.")]
    [Range(1f, 12f)] public float wispTwistLength = 3.5f;
    [ColorUsage(false, true)] public Color wispColorA = new Color(0.45f, 1f, 0.9f);
    [ColorUsage(false, true)] public Color wispColorB = new Color(0.8f, 0.55f, 1f);

    [Header("Motes")]
    public bool motes = true;
    [Tooltip("Motes left behind per unit travelled.")]
    [Range(0f, 10f)] public float motesPerUnit = 2.5f;
    [Range(0.05f, 0.6f)] public float moteSize = 0.35f;

    [Header("Gameplay Bursts")]
    public bool sideSwitchBurst = true;
    public bool turnBurst = true;
    [Tooltip("Round shockwave rings on the bursts. Off in MainScene prop camera 3.7 (sparks and flashes stay).")]
    public bool rings = true;
    public bool coinBurst = true;
    [Tooltip("Ember flicker and sputtering sparks while off the path, before the run ends.")]
    public bool offPathWarning = true;
    public bool gameOverBurst = true;
    [Range(0f, 2f)] public float burstStrength = 1f;
    [ColorUsage(false, true)] public Color switchColor = new Color(0.7f, 1f, 1f);
    [ColorUsage(false, true)] public Color coinColor = new Color(1f, 0.78f, 0.3f);
    [ColorUsage(false, true)] public Color dangerColor = new Color(1f, 0.4f, 0.12f);

    [Header("Trail Light")]
    [Tooltip("A small point light just off the face the runner is on. Only surfaces facing it catch it, so the ridden face glows and the other sides stay dark.")]
    public bool castLight = true;
    [Range(0f, 5f)] public float lightIntensity = 0.9f;
    [Tooltip("Light reach, in cubes.")]
    [Range(0.5f, 6f)] public float lightRange = 2.6f;
    [Tooltip("How far off the face the light floats, in cubes.")]
    [Range(0.05f, 1.5f)] public float lightLift = 0.45f;
    [Tooltip("How far behind the runner the light sits, in cubes, so it washes over the trail.")]
    [Range(0f, 2f)] public float lightBehind = 0.4f;
    [Tooltip("Extra intensity for a moment after a side switch or turn.")]
    [Range(0f, 8f)] public float switchLightBoost = 3f;

    static readonly int HeadColorId = Shader.PropertyToID("_HeadColor");
    static readonly int MidColorId = Shader.PropertyToID("_MidColor");
    static readonly int TailColorId = Shader.PropertyToID("_TailColor");
    static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int FlowSpeedId = Shader.PropertyToID("_FlowSpeed");
    static readonly int FlowStrengthId = Shader.PropertyToID("_FlowStrength");
    static readonly int FlashId = Shader.PropertyToID("_Flash");
    static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

    private CubeRailRunner runner;
    private TrailRenderer trail;
    private CoinCollector coins;
    private MaterialPropertyBlock block;
    private readonly System.Random rng = new System.Random();

    // The trail as the scene has it, put back when restyling stops.
    private bool trailStyled;
    private Material originalMaterial;
    private Gradient originalGradient;
    private AnimationCurve originalWidthCurve;
    private float originalWidthMultiplier;
    private int originalCornerVertices;
    private int originalCapVertices;
    private LineTextureMode originalTextureMode;

    private Transform fxRoot;
    private TrailRenderer wispA, wispB;
    private ParticleSystem moteSystem, sparkSystem, ringSystem;
    private Light trailLight;
    private float lightFlash;

    private Transform lastCube;
    private Vector3 lastCubeForward;
    private int lastSide = -1;
    private bool wasGameOver;
    private float flash;
    private Color flashColor;
    private float widthPulse;
    private float danger;
    private float wispPhase;
    private float sputterTimer;
    private float nextCoinLookup;

    void Awake() { EnsureRefs(); }

    // Awake does not run again after a script recompile in Play mode, and the private fields it fills
    // come back null - so anything that needs them makes sure they are there first.
    void EnsureRefs()
    {
        if (runner == null) runner = GetComponent<CubeRailRunner>();
        if (trail == null) trail = GetComponent<TrailRenderer>();
        if (block == null) block = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        EnsureRefs();
        if (ribbonMaterial == null || sparkMaterial == null || ringMaterial == null)
        {
            Debug.LogWarning("RunnerTrailFX: assign the ribbon, spark and ring materials (Assets/Materials/RunnerTrail).", this);
            enabled = false;
            return;
        }

        if (fxRoot == null) BuildEffects();
        fxRoot.gameObject.SetActive(true);
        moteSystem.gameObject.SetActive(true);
        moteSystem.Play();
        sparkSystem.Play();
        ringSystem.Play();

        if (restyleTrail) ApplyTrailStyle();

        lastCube = null;
        lastSide = -1;
        wasGameOver = runner.IsGameOver;
        nextCoinLookup = 0f;
        PlaceWisps();
        wispA.Clear();
        wispB.Clear();
    }

    void OnDisable()
    {
        RestoreTrail();
        SetCoins(null);
        if (fxRoot != null) fxRoot.gameObject.SetActive(false);
        if (moteSystem != null) moteSystem.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (fxRoot != null) Destroy(fxRoot.gameObject);
    }

    void LateUpdate()
    {
        EnsureRefs();
        if (fxRoot == null || wispA == null || moteSystem == null) { OnEnable(); if (!enabled) return; }
        if (restyleTrail != trailStyled)
        {
            if (restyleTrail) ApplyTrailStyle();
            else RestoreTrail();
        }
        if (coins == null && Time.time >= nextCoinLookup)
        {
            nextCoinLookup = Time.time + 0.5f;
            SetCoins(GetComponent<CoinCollector>());
        }

        float dt = Time.deltaTime;
        bool over = runner.IsGameOver;
        bool offPath = runner.isFlying && !over;
        float speed01 = Mathf.InverseLerp(calmSpeed, fastSpeed, runner.forwardSpeed);

        DetectEvents(over);

        danger = Mathf.MoveTowards(danger, offPathWarning && (offPath || over) ? 1f : 0f, dt * (offPath ? 2.5f : 6f));
        flash = Mathf.MoveTowards(flash, 0f, dt * 3.5f);
        widthPulse = Mathf.MoveTowards(widthPulse, 0f, dt * 2.5f);

        Color head = Color.Lerp(headColor, fastHeadColor, speed01);
        Color mid = Color.Lerp(midColor, fastMidColor, speed01);
        Color tail = Color.Lerp(tailColor, fastTailColor, speed01);
        if (powerTintAmount > 0f)
        {
            head = Color.Lerp(head, powerTint * 1.2f, powerTintAmount);
            mid = Color.Lerp(mid, powerTint, powerTintAmount);
            tail = Color.Lerp(tail, powerTint * 0.55f, powerTintAmount);
        }
        Color core = Color.white;
        float intensity = ribbonIntensity * (1f + fastIntensityBoost * speed01) * externalIntensity;
        if (danger > 0f)
        {
            head = Color.Lerp(head, Color.Lerp(dangerColor, Color.white, 0.45f), danger);
            mid = Color.Lerp(mid, dangerColor, danger);
            tail = Color.Lerp(tail, dangerColor * new Color(0.7f, 0.25f, 0.3f), danger);
            core = Color.Lerp(core, new Color(1f, 0.82f, 0.62f), danger);
            float flicker = over ? 0.8f : 0.72f + 0.28f * Mathf.Sin(Time.time * 38f);
            intensity *= Mathf.Lerp(1f, flicker, danger);
        }

        if (trailStyled)
        {
            trail.widthMultiplier = ribbonWidth * (1f + fastWidthBoost * speed01 + widthPulse);
            SetRibbonBlock(trail, head, mid, tail, core, intensity, 1.2f + 2.2f * speed01, shimmer);
        }

        UpdateWisps(dt, speed01, over, tail, intensity);
        UpdateMotes(speed01, over, head, mid);
        UpdateLight(dt, over, Color.Lerp(head, mid, 0.5f));

        if (offPath && offPathWarning)
        {
            sputterTimer -= dt;
            if (sputterTimer <= 0f)
            {
                sputterTimer = 0.05f;
                EmitSparks(transform.position, 2, dangerColor, 1.8f, 0.4f);
            }
        }
    }

    // --- events -------------------------------------------------------------------------------

    void DetectEvents(bool over)
    {
        Transform cube = runner.currentCube;
        int side = runner.currentSideIndex;
        if (cube != lastCube)
        {
            if (lastCube != null && cube != null && Vector3.Angle(cube.forward, lastCubeForward) > 30f) OnTurn();
            lastCube = cube;
            if (cube != null) lastCubeForward = cube.forward;
            lastSide = side;
        }
        else if (side != lastSide)
        {
            if (lastSide >= 0) OnSideSwitch();
            lastSide = side;
        }

        if (over && !wasGameOver) OnGameOver();
        wasGameOver = over;
    }

    void OnSideSwitch()
    {
        if (!sideSwitchBurst) return;
        Vector3 pos = transform.position;
        Flash(switchColor, 1f);
        lightFlash = 1f;
        widthPulse = Mathf.Max(widthPulse, 0.45f * burstStrength);
        EmitSparks(pos, Count(20), switchColor, 3.8f, 0.5f);
        EmitRing(pos, 3.8f, switchColor, 0.45f);
    }

    void OnTurn()
    {
        if (!turnBurst) return;
        Vector3 pos = transform.position;
        float speed01 = Mathf.InverseLerp(calmSpeed, fastSpeed, runner.forwardSpeed);
        Color c = Color.Lerp(headColor, fastMidColor, speed01);
        Flash(c, 1f);
        lightFlash = Mathf.Max(lightFlash, 0.7f);
        widthPulse = Mathf.Max(widthPulse, 0.4f * burstStrength);
        EmitSparks(pos, Count(22), c, 4.5f, 0.5f);
        EmitSparks(pos, Count(8), Color.white, 2.5f, 0.35f);
        EmitRing(pos, 4.5f, c, 0.45f);
    }

    void OnCoin(int total, Vector3 coinPosition)
    {
        if (!coinBurst || !isActiveAndEnabled) return;
        Flash(coinColor, 0.55f);
        EmitSparks(coinPosition, Count(10), coinColor, 2.8f, 0.4f);
        EmitRing(coinPosition, 2.2f, coinColor, 0.3f);
    }

    [Tooltip("Multiplies the ribbon brightness. 1 = as tuned. CloseCallFX steps it with the wave phase when a DifficultyProfile is on.")]
    [Range(0.5f, 2f)] public float externalIntensity = 1f;

    /// <summary>Power-diamond autopilot (RunnerPowerFX): blends the ribbon toward this colour. 0 = untouched.</summary>
    [System.NonSerialized] public Color powerTint = Color.white;
    [System.NonSerialized] public float powerTintAmount;

    /// <summary>MainScene prop camera 3.7 DepthCueController: scales the decoration (point light, motes, wisps) down as the run speeds up. 1 = untouched.</summary>
    [System.NonSerialized] public float decorScale = 1f;

    /// <summary>Close call: a hot white-gold shockwave and a spray of sparks (CloseCallFX).</summary>
    public void CloseCallBurst(Color color)
    {
        if (!isActiveAndEnabled || fxRoot == null) return;
        Vector3 pos = transform.position;
        Flash(color, 1.4f);
        lightFlash = 1.4f;
        widthPulse = Mathf.Max(widthPulse, 0.9f * burstStrength);
        EmitRing(pos, 9f, color, 0.5f);
        EmitRing(pos, 5f, Color.white, 0.3f);
        EmitSparks(pos, Count(40), color, 7f, 0.6f);
        EmitSparks(pos, Count(16), Color.white, 4f, 0.45f);
    }

    /// <summary>Small flash and ring, e.g. a boost cleared or a clean corner.</summary>
    public void SmallBurst(Color color, float size)
    {
        if (!isActiveAndEnabled || fxRoot == null) return;
        Vector3 pos = transform.position;
        Flash(color, 0.7f);
        EmitRing(pos, size, color, 0.35f);
        EmitSparks(pos, Count(12), color, 3.5f, 0.4f);
    }

    void OnGameOver()
    {
        wispA.emitting = false;
        wispB.emitting = false;
        if (!gameOverBurst) return;
        Vector3 pos = transform.position;
        Flash(dangerColor, 1.2f);
        EmitSparks(pos, Count(36), dangerColor, 5.5f, 0.6f);
        EmitSparks(pos, Count(14), new Color(1f, 0.85f, 0.6f), 3f, 0.45f);
        EmitRing(pos, 7f, dangerColor, 0.6f);
    }

    void Flash(Color color, float amount)
    {
        amount *= burstStrength;
        if (amount < flash) return;
        flash = amount;
        flashColor = color;
    }

    int Count(int n) => Mathf.RoundToInt(n * burstStrength);

    void SetCoins(CoinCollector collector)
    {
        if (coins != null) coins.CoinCollected -= OnCoin;
        coins = collector;
        if (coins != null) coins.CoinCollected += OnCoin;
    }

    // --- ribbon and wisps ---------------------------------------------------------------------

    void ApplyTrailStyle()
    {
        if (trail == null || trailStyled) return;
        originalMaterial = trail.sharedMaterial;
        originalGradient = trail.colorGradient;
        originalWidthCurve = trail.widthCurve;
        originalWidthMultiplier = trail.widthMultiplier;
        originalCornerVertices = trail.numCornerVertices;
        originalCapVertices = trail.numCapVertices;
        originalTextureMode = trail.textureMode;

        trail.sharedMaterial = ribbonMaterial;
        trail.colorGradient = WhiteGradient();
        trail.widthCurve = SmoothCurve(new Keyframe(0f, 0.8f), new Keyframe(0.05f, 1f), new Keyframe(0.4f, 0.55f), new Keyframe(1f, 0f));
        trail.numCornerVertices = 3;
        trail.numCapVertices = 3;
        trail.textureMode = LineTextureMode.Stretch;
        trailStyled = true;
    }

    void RestoreTrail()
    {
        if (!trailStyled || trail == null) return;
        trail.sharedMaterial = originalMaterial;
        trail.colorGradient = originalGradient;
        trail.widthCurve = originalWidthCurve;
        trail.widthMultiplier = originalWidthMultiplier;
        trail.numCornerVertices = originalCornerVertices;
        trail.numCapVertices = originalCapVertices;
        trail.textureMode = originalTextureMode;
        trail.SetPropertyBlock(null);
        trailStyled = false;
    }

    void UpdateWisps(float dt, float speed01, bool over, Color ribbonTail, float intensity)
    {
        bool on = wisps && !over;
        wispA.emitting = on;
        wispB.emitting = on;
        float time = trail != null ? Mathf.Min(wispLength, trail.time) : wispLength;
        wispA.time = time;
        wispB.time = time;

        wispPhase += runner.forwardSpeed * dt / wispTwistLength * (2f * Mathf.PI);
        if (wispPhase > 2f * Mathf.PI) wispPhase -= 2f * Mathf.PI;
        if (!over) PlaceWisps();

        float width = wispWidth * (1f + 0.4f * speed01) * decorScale;
        wispA.widthMultiplier = width;
        wispB.widthMultiplier = width;
        SetWispBlock(wispA, wispColorA, ribbonTail, intensity);
        SetWispBlock(wispB, wispColorB, ribbonTail, intensity);
    }

    // Two points mirrored through a line just above the runner, circling it as it moves, so their trails braid.
    void PlaceWisps()
    {
        Transform t = transform;
        Vector3 center = t.position + t.up * wispLift;
        Vector3 offset = t.right * (Mathf.Sin(wispPhase) * wispSpread) + t.up * (Mathf.Cos(wispPhase) * wispSpread * 0.55f);
        wispA.transform.position = center + offset;
        wispB.transform.position = center - offset;
    }

    void SetWispBlock(TrailRenderer wisp, Color color, Color ribbonTail, float intensity)
    {
        Color tail = Color.Lerp(color, ribbonTail, 0.6f);
        if (danger > 0f) color = Color.Lerp(color, dangerColor, danger);
        SetRibbonBlock(wisp, Color.Lerp(color, Color.white, 0.3f), color, tail, color * 0.7f, intensity * 0.9f, 2f, 0.4f);
    }

    void SetRibbonBlock(Renderer r, Color head, Color mid, Color tail, Color core, float intensity, float flowSpeed, float flowStrength)
    {
        block.Clear();
        block.SetVector(HeadColorId, Linear(head));
        block.SetVector(MidColorId, Linear(mid));
        block.SetVector(TailColorId, Linear(tail));
        block.SetVector(CoreColorId, Linear(core));
        block.SetFloat(IntensityId, intensity);
        block.SetFloat(FlowSpeedId, flowSpeed);
        block.SetFloat(FlowStrengthId, flowStrength);
        block.SetFloat(FlashId, flash);
        block.SetVector(FlashColorId, Linear(flashColor));
        r.SetPropertyBlock(block);
    }

    // The runner's up is the normal of the face it rides, so the light sits just off that face.
    void UpdateLight(float dt, bool over, Color color)
    {
        lightFlash = Mathf.MoveTowards(lightFlash, 0f, dt * 2.2f);
        bool on = castLight && !over && !runner.isFlying;
        if (trailLight.enabled != on) trailLight.enabled = on;
        if (!on) return;

        Transform t = transform;
        float cube = Mathf.Max(0.01f, t.lossyScale.y);
        trailLight.transform.position = t.position + t.up * (lightLift * cube) - t.forward * (lightBehind * cube);
        trailLight.range = lightRange * cube;
        trailLight.color = Color.Lerp(color, switchColor, lightFlash);
        trailLight.intensity = lightIntensity * (1f + switchLightBoost * lightFlash * burstStrength) * (1f - danger * 0.5f) * decorScale;
    }

    // --- particles ----------------------------------------------------------------------------

    void UpdateMotes(float speed01, bool over, Color head, Color mid)
    {
        var emission = moteSystem.emission;
        emission.rateOverDistance = motes && !over ? motesPerUnit * (0.6f + 0.8f * speed01) * decorScale : 0f;
        var main = moteSystem.main;
        main.startColor = new ParticleSystem.MinMaxGradient(Linear(mid), Linear(Color.Lerp(head, Color.white, 0.3f)));
        main.startSize = new ParticleSystem.MinMaxCurve(moteSize * 0.45f, moteSize);
    }

    void EmitSparks(Vector3 position, int count, Color color, float speed, float size)
    {
        if (count <= 0) return;
        var p = new ParticleSystem.EmitParams { startColor = Linear(color) };
        Vector3 behind = -transform.forward * (runner.forwardSpeed * 0.15f);
        for (int i = 0; i < count; i++)
        {
            p.position = position;
            p.velocity = RandomDirection() * (speed * Range(0.45f, 1f)) + behind;
            p.startSize = size * Range(0.5f, 1f);
            p.startLifetime = Range(0.35f, 0.75f);
            sparkSystem.Emit(p, 1);
        }
    }

    void EmitRing(Vector3 position, float size, Color color, float lifetime)
    {
        if (!rings) return;
        var p = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = Vector3.zero,
            startColor = Linear(color),
            startSize = size * Mathf.Lerp(1f, burstStrength, 0.5f),
            startLifetime = lifetime,
        };
        ringSystem.Emit(p, 1);
    }

    // --- building -----------------------------------------------------------------------------

    void BuildEffects()
    {
        fxRoot = new GameObject("RunnerTrailFX").transform;
        wispA = MakeWisp("Wisp A");
        wispB = MakeWisp("Wisp B");

        var lightGo = new GameObject("Trail Light");
        lightGo.transform.SetParent(fxRoot, false);
        trailLight = lightGo.AddComponent<Light>();
        trailLight.type = LightType.Point;
        trailLight.shadows = LightShadows.None;
        trailLight.renderMode = LightRenderMode.ForcePixel;
        trailLight.enabled = false;

        // The motes ride on the runner so they emit from where it is this frame, not last frame.
        moteSystem = MakeSystem("RunnerTrailFX Motes", transform, sparkMaterial, 200);
        var main = moteSystem.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.45f);
        main.gravityModifier = -0.06f;
        var shape = moteSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.45f;
        var color = moteSystem.colorOverLifetime;
        color.enabled = true;
        color.color = FadeGradient(0.15f, 0.6f);
        var size = moteSystem.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, SmoothCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

        sparkSystem = MakeSystem("Sparks", fxRoot, sparkMaterial, 400);
        color = sparkSystem.colorOverLifetime;
        color.enabled = true;
        color.color = FadeGradient(0f, 0.5f);
        size = sparkSystem.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, SmoothCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
        var drag = sparkSystem.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.limit = 100f;
        drag.drag = 3f;

        ringSystem = MakeSystem("Rings", fxRoot, ringMaterial, 16);
        color = ringSystem.colorOverLifetime;
        color.enabled = true;
        color.color = FadeGradient(0f, 0.2f);
        size = ringSystem.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.2f, 0f, 2.4f), new Keyframe(1f, 1f, 0.2f, 0f)));
    }

    TrailRenderer MakeWisp(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(fxRoot, false);
        var t = go.AddComponent<TrailRenderer>();
        t.sharedMaterial = ribbonMaterial;
        t.colorGradient = WhiteGradient();
        t.widthCurve = SmoothCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        t.numCornerVertices = 2;
        t.numCapVertices = 2;
        t.textureMode = LineTextureMode.Stretch;
        t.alignment = LineAlignment.View;
        t.minVertexDistance = 0.05f;
        t.shadowCastingMode = ShadowCastingMode.Off;
        t.receiveShadows = false;
        t.lightProbeUsage = LightProbeUsage.Off;
        t.reflectionProbeUsage = ReflectionProbeUsage.Off;
        t.time = wispLength;
        return t;
    }

    static ParticleSystem MakeSystem(string name, Transform parent, Material material, int maxParticles)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxParticles;
        main.startSpeed = 0f;
        main.startColor = Color.white;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;
        var shape = ps.shape;
        shape.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = material;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return ps;
    }

    // --- helpers ------------------------------------------------------------------------------

    static Gradient WhiteGradient()
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    // White, fading in over the first fadeIn of the lifetime and out from fadeOutFrom to the end.
    static Gradient FadeGradient(float fadeIn, float fadeOutFrom)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  fadeIn > 0f
                      ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, fadeOutFrom), new GradientAlphaKey(0f, 1f) }
                      : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, fadeOutFrom), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    static AnimationCurve SmoothCurve(params Keyframe[] keys)
    {
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
        return curve;
    }

    // Colours go to the GPU as they are (vectors and particle vertex colours), so convert them here.
    static Color Linear(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

    float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);

    Vector3 RandomDirection()
    {
        float z = Range(-1f, 1f);
        float a = Range(0f, 2f * Mathf.PI);
        float r = Mathf.Sqrt(1f - z * z);
        return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z);
    }
}
