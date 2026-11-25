using UnityEngine;

/// <summary>
/// The look of a boost (DynamicPathGeneratorBoost.cs) on the runner: speed lines streaming past
/// it, a glow around it, and a shockwave ring as the boost starts and a small one at each of its
/// turns. Pure dressing - the speed is never touched.
///
/// Added to the runner by DynamicPathGenerator at start. Everything is built at runtime: one
/// camera-facing quad and two particle systems, additive, no textures, no lights and no colliders
/// (CubeRailRunner's forward probe hits any collider on the Default layer, triggers included).
/// Particle colours go in as vertex colours, so they are linearised here (the project is Linear).
/// The particle materials are loaded from Resources/BoostFX so their shader ships in a build.
/// </summary>
public class RunnerBoostFX : MonoBehaviour
{
    public DynamicPathGenerator pathGen;

    [Header("Colour")]
    [Tooltip("Colour of the speed lines and rings.")]
    public Color streakColor = new Color(0.55f, 0.95f, 1f, 1f);
    [Tooltip("Colour of the glow round the runner. HDR: above 1 blooms where bloom is on.")]
    [ColorUsage(true, true)] public Color glowColor = new Color(0.35f, 0.85f, 1f, 1f);

    [Header("Speed Lines")]
    [Tooltip("Speed lines per second at full boost.")]
    [Range(0f, 200f)] public float streakRate = 90f;
    [Tooltip("How fast the lines stream back past the runner, in runner sizes per second.")]
    [Range(5f, 80f)] public float streakSpeed = 30f;
    [Tooltip("Radius of the ring the lines stream along, in runner sizes.")]
    [Range(0.5f, 4f)] public float streakRadius = 1.5f;
    [Tooltip("Thickness of one line, in runner sizes.")]
    [Range(0.02f, 0.5f)] public float streakWidth = 0.2f;
    [Tooltip("Length of one line, as a multiple of its travel per second.")]
    [Range(0.01f, 0.2f)] public float streakLength = 0.05f;

    [Header("Glow")]
    [Tooltip("Size of the glow round the runner, in runner sizes.")]
    [Range(0.5f, 8f)] public float glowSize = 3.2f;
    [Tooltip("Strength of the glow at full boost.")]
    [Range(0f, 2f)] public float glowStrength = 1f;
    [Tooltip("How much the glow breathes while the boost lasts (0 = steady).")]
    [Range(0f, 1f)] public float glowPulse = 0.25f;
    [Tooltip("Breaths per second.")]
    [Range(0.5f, 12f)] public float glowPulseRate = 6f;

    [Header("Rings")]
    [Tooltip("Size the ring grows to as a boost starts, in runner sizes.")]
    [Range(1f, 20f)] public float startRingSize = 9f;
    [Tooltip("Size of the small ring at each turn of a boost, in runner sizes. 0 = none.")]
    [Range(0f, 10f)] public float turnRingSize = 4f;

    [Header("Timing")]
    [Tooltip("Seconds to fade the look in when a boost starts.")]
    [Range(0.01f, 1f)] public float fadeIn = 0.12f;
    [Tooltip("Seconds to fade it out after the boost's last leg.")]
    [Range(0.01f, 2f)] public float fadeOut = 0.45f;

    private CubeRailRunner runner;
    private Transform root;
    private Transform glowQuad;
    private Material glowMaterial;
    private ParticleSystem streaks;
    private ParticleSystem rings;
    private Material streakMaterial, ringMaterial;
    private float amount;              // 0 = off, 1 = full boost
    private float punch;               // extra glow size right after a start or a turn, decays
    private int lastBoostId = -1;
    private int lastBoostLeg = -1;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int FadeId = Shader.PropertyToID("_Fade");

    // Power-diamond autopilot (RunnerPowerFX) borrows the whole look: speed lines, glow, rings.
    [System.NonSerialized] public bool externalOn;
    [System.NonSerialized] public float externalAmount = 1f;
    [System.NonSerialized] public Color externalColor = new Color(0.7f, 1f, 1f, 1f);
    [System.NonSerialized] public float externalFlicker;   // flickers per second, 0 = steady

    /// <summary>A ring burst at a point, for RunnerPowerFX (turns, handover).</summary>
    public void PowerRing(float size, float life, float strength)
    {
        if (rings != null) Ring(size, life, strength);
    }

    /// <summary>0 when no boost is showing, 1 at full boost.</summary>
    public float Amount => amount;

    void Start()
    {
        runner = GetComponent<CubeRailRunner>();
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        Build();
        SetVisible(false);
    }

    void OnDestroy()
    {
        if (glowMaterial != null) Destroy(glowMaterial);
        if (streakMaterial != null) Destroy(streakMaterial);
        if (ringMaterial != null) Destroy(ringMaterial);
        if (root != null) Destroy(root.gameObject);
    }

    void LateUpdate()
    {
        if (root == null || pathGen == null) return;

        bool live = runner == null || !runner.IsGameOver;
        bool onBoost = live && (pathGen.PlayerOnBoostLeg || externalOn);
        int id = pathGen.PlayerBoostId;
        int leg = pathGen.PlayerBoostLeg;

        if (onBoost && id != lastBoostId)
        {
            Ring(startRingSize, 0.45f, 1f);
            punch = 1f;
        }
        else if (onBoost && leg != lastBoostLeg && turnRingSize > 0f)
        {
            Ring(turnRingSize, 0.3f, 0.7f);
            punch = Mathf.Max(punch, 0.6f);
        }
        if (onBoost) { lastBoostId = id; lastBoostLeg = leg; }

        float target = onBoost ? (externalOn ? externalAmount : 1f) : 0f;
        float rate = 1f / Mathf.Max(0.01f, onBoost ? fadeIn : fadeOut);
        amount = Mathf.MoveTowards(amount, target, rate * Time.deltaTime);
        punch = Mathf.MoveTowards(punch, 0f, Time.deltaTime * 3f);

        if (amount <= 0.001f)
        {
            SetVisible(false);
            return;
        }
        SetVisible(true);

        float scale = Mathf.Max(0.01f, transform.lossyScale.x);

        // Speed lines: the emission rate follows the fade, so the lines thin out rather than blink.
        var emission = streaks.emission;
        emission.rateOverTime = streakRate * amount;

        // Glow: faces the camera, grows in with the boost and swells a little at each turn.
        Camera cam = Camera.main;
        if (cam != null) glowQuad.rotation = cam.transform.rotation;
        float breathe = 1f + glowPulse * 0.5f * Mathf.Sin(Time.time * glowPulseRate * Mathf.PI * 2f);
        // Pulled toward the camera so the cube the runner rides on does not hide half of it.
        Vector3 towardCamera = cam != null ? -cam.transform.forward : Vector3.up;
        glowQuad.position = transform.position + towardCamera * (glowSize * scale);
        glowQuad.localScale = Vector3.one * (glowSize * scale * (0.7f + 0.3f * amount + 0.5f * punch));
        float flick = externalOn && externalFlicker > 0f ? (Mathf.Sin(Time.unscaledTime * externalFlicker * Mathf.PI * 2f) > 0f ? 1f : 0.2f) : 1f;
        glowMaterial.SetFloat(FadeId, Mathf.Clamp01(glowStrength * amount * breathe * flick + 0.3f * punch));
        glowMaterial.SetColor(ColorId, externalOn ? externalColor : glowColor);
    }

    void SetVisible(bool on)
    {
        if (glowQuad.gameObject.activeSelf != on) glowQuad.gameObject.SetActive(on);
        if (on && !streaks.isPlaying) streaks.Play();
        if (!on && streaks.isPlaying) streaks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void Ring(float size, float life, float strength)
    {
        float scale = Mathf.Max(0.01f, transform.lossyScale.x);
        rings.Emit(new ParticleSystem.EmitParams
        {
            position = transform.position,
            applyShapeToPosition = false,
            startSize = size * scale,
            startLifetime = life,
            startColor = Linear(new Color(streakColor.r, streakColor.g, streakColor.b, strength)),
            velocity = Vector3.zero
        }, 1);
    }

    // ------------------------------------------------------------------
    // Build
    // ------------------------------------------------------------------

    void Build()
    {
        root = new GameObject("BoostFX").transform;
        root.SetParent(transform, false);

        Material sparkSource = Resources.Load<Material>("BoostFX/BoostStreak");
        Shader sparkShader = sparkSource != null ? sparkSource.shader : Shader.Find("Runner/TrailFX Spark");
        if (sparkShader == null) sparkShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        streakMaterial = sparkSource != null ? new Material(sparkSource) : new Material(sparkShader);
        streakMaterial.name = "BoostStreak (runtime)";
        SetIfPresent(streakMaterial, "_Shape", 0f);
        SetIfPresent(streakMaterial, "_RayStrength", 0f);
        SetIfPresent(streakMaterial, "_Intensity", 2.4f);
        SetIfPresent(streakMaterial, "_CameraPull", 1.2f);

        Material ringSource = Resources.Load<Material>("BoostFX/BoostRing");
        ringMaterial = ringSource != null ? new Material(ringSource) : new Material(sparkShader);
        ringMaterial.name = "BoostRing (runtime)";
        SetIfPresent(ringMaterial, "_Shape", 1f);
        SetIfPresent(ringMaterial, "_RingWidth", 0.07f);
        SetIfPresent(ringMaterial, "_Intensity", 2f);
        SetIfPresent(ringMaterial, "_CameraPull", 1.2f);

        BuildGlow();
        BuildStreaks();
        BuildRings();
    }

    void BuildGlow()
    {
        var go = new GameObject("BoostGlow");
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        Shader glow = Shader.Find("Custom/SoftGlow");
        glowMaterial = new Material(glow != null ? glow : Shader.Find("Universal Render Pipeline/Particles/Unlit"))
        {
            name = "BoostGlow (runtime)"
        };
        glowMaterial.SetFloat("_Falloff", 2.2f);
        glowMaterial.SetFloat("_Core", 0.05f);
        mr.sharedMaterial = glowMaterial;
        glowQuad = go.transform;
    }

    void BuildStreaks()
    {
        var go = new GameObject("BoostStreaks");
        go.transform.SetParent(root, false);
        // The lines are born ahead of the runner and stream back past it.
        go.transform.localPosition = new Vector3(0f, 0f, 2.5f);
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        streaks = go.AddComponent<ParticleSystem>();
        streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = streaks.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(streakSpeed * 0.8f, streakSpeed * 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(streakWidth * 0.6f, streakWidth);
        Color a = Linear(streakColor), b = Linear(Color.Lerp(streakColor, Color.white, 0.6f));
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.maxParticles = 80;

        var emission = streaks.emission;
        emission.rateOverTime = 0f;

        var shape = streaks.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 0f;
        shape.radius = streakRadius;
        shape.radiusThickness = 0.45f;   // from the outer part of the disc only, clear of the runner

        var col = streaks.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.lengthScale = 2f;
        r.velocityScale = streakLength;
        r.sharedMaterial = streakMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    void BuildRings()
    {
        var go = new GameObject("BoostRings");
        go.transform.SetParent(root, false);

        rings = go.AddComponent<ParticleSystem>();
        rings.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = rings.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startSpeed = 0f;
        main.maxParticles = 8;

        var emission = rings.emission;
        emission.enabled = false;
        var shape = rings.shape;
        shape.enabled = false;

        // Grows out from a point and fades as it goes.
        var size = rings.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.15f, 0f, 3f), new Keyframe(1f, 1f, 0.2f, 0f)));

        var col = rings.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = ringMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    static void SetIfPresent(Material m, string prop, float v)
    {
        if (m.HasProperty(prop)) m.SetFloat(prop, v);
    }

    static Color Linear(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
}
