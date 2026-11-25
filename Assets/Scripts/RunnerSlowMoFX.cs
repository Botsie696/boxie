using UnityEngine;

/// <summary>
/// The look of a slow-motion section (DynamicPathGeneratorSlowMo.cs) on the runner - the counterpart of RunnerBoostFX's
/// speed lines: a "time bubble". A soft violet glow that breathes slowly, motes of dust hanging almost still in the air
/// round the runner (they drift, they do not stream), a wide slow ripple ring as the section starts and a smaller one at
/// each tiny leg. Its strength follows the slowdown itself (TimeScaleMixer.Source.SlowMo), so it fades in with the ease-in
/// and away along the exit leg as everything comes back up to speed. Particles run on scaled time: they slow down too.
///
/// Added to the runner by ResetSlowMo. Built at runtime like RunnerBoostFX (same Resources/BoostFX materials, no
/// colliders - the runner's forward probe hits any collider).
/// </summary>
public class RunnerSlowMoFX : MonoBehaviour
{
    public DynamicPathGenerator pathGen;

    [Tooltip("Colour of the motes and rings.")]
    public Color moteColor = new Color(0.78f, 0.62f, 1f, 1f);
    [Tooltip("Colour of the glow round the runner. HDR: above 1 blooms where bloom is on.")]
    [ColorUsage(true, true)] public Color glowColor = new Color(0.55f, 0.35f, 1f, 1f);
    [Tooltip("Motes per second at full slowdown.")]
    [Range(0f, 80f)] public float moteRate = 26f;
    [Tooltip("Radius of the cloud of motes, in runner sizes.")]
    [Range(0.5f, 6f)] public float moteRadius = 2.6f;
    [Tooltip("Size of the glow, in runner sizes.")]
    [Range(0.5f, 8f)] public float glowSize = 3.8f;
    [Tooltip("Size of the ring as a section starts / at each tiny leg, in runner sizes.")]
    public Vector2 ringSize = new Vector2(12f, 5f);

    CubeRailRunner runner;
    Transform root, glowQuad;
    Material glowMaterial, moteMaterial, ringMaterial;
    ParticleSystem motes, rings;
    float amount;
    int lastSerial = -1;
    bool wasSlow;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int FadeId = Shader.PropertyToID("_Fade");

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
        if (moteMaterial != null) Destroy(moteMaterial);
        if (ringMaterial != null) Destroy(ringMaterial);
        if (root != null) Destroy(root.gameObject);
    }

    void LateUpdate()
    {
        if (root == null || pathGen == null) return;
        bool live = runner == null || !runner.IsGameOver;
        // 1 at the section's full slowdown (time scale 0.85 or lower), 0 at normal speed.
        float slow = TimeScaleMixer.Get(TimeScaleMixer.Source.SlowMo);
        amount = live ? Mathf.Clamp01((1f - slow) / 0.15f) : 0f;

        DynamicPathGenerator.Segment s = pathGen.PlayerSegment;
        bool onSlow = live && pathGen.PlayerOnSlowMo && s != null && !s.slowMoExit;
        if (onSlow && s.serial != lastSerial)
        {
            Ring(wasSlow ? ringSize.y : ringSize.x, wasSlow ? 0.9f : 1.6f, wasSlow ? 0.6f : 1f);   // a big one to start, small ones per tiny leg
            lastSerial = s.serial;
        }
        wasSlow = onSlow;

        if (amount <= 0.001f) { SetVisible(false); return; }
        SetVisible(true);

        float scale = Mathf.Max(0.01f, transform.lossyScale.x);
        var emission = motes.emission;
        emission.rateOverTime = moteRate * amount;

        Camera cam = Camera.main;
        if (cam != null) glowQuad.rotation = cam.transform.rotation;
        Vector3 towardCamera = cam != null ? -cam.transform.forward : Vector3.up;
        glowQuad.position = transform.position + towardCamera * (glowSize * scale);
        float breathe = 1f + 0.18f * Mathf.Sin(Time.unscaledTime * 1.6f * Mathf.PI * 2f);
        glowQuad.localScale = Vector3.one * (glowSize * scale * (0.75f + 0.25f * amount) * breathe);
        glowMaterial.SetFloat(FadeId, Mathf.Clamp01(0.85f * amount));
        glowMaterial.SetColor(ColorId, glowColor);
    }

    void SetVisible(bool on)
    {
        if (glowQuad.gameObject.activeSelf != on) glowQuad.gameObject.SetActive(on);
        if (on && !motes.isPlaying) motes.Play();
        if (!on && motes.isPlaying) motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void Ring(float size, float life, float strength)
    {
        if (rings == null) return;
        float scale = Mathf.Max(0.01f, transform.lossyScale.x);
        rings.Emit(new ParticleSystem.EmitParams
        {
            position = transform.position,
            applyShapeToPosition = false,
            startSize = size * scale,
            startLifetime = life,
            startColor = Linear(new Color(moteColor.r, moteColor.g, moteColor.b, strength)),
            velocity = Vector3.zero
        }, 1);
    }

    void Build()
    {
        root = new GameObject("SlowMoFX").transform;
        root.SetParent(transform, false);

        Material sparkSource = Resources.Load<Material>("BoostFX/BoostStreak");
        Shader sparkShader = sparkSource != null ? sparkSource.shader : Shader.Find("Runner/TrailFX Spark");
        if (sparkShader == null) sparkShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        moteMaterial = sparkSource != null ? new Material(sparkSource) : new Material(sparkShader);
        moteMaterial.name = "SlowMoMote (runtime)";
        SetIfPresent(moteMaterial, "_Shape", 0f);
        SetIfPresent(moteMaterial, "_RayStrength", 0f);
        SetIfPresent(moteMaterial, "_Intensity", 2f);
        SetIfPresent(moteMaterial, "_CameraPull", 1.2f);

        Material ringSource = Resources.Load<Material>("BoostFX/BoostRing");
        ringMaterial = ringSource != null ? new Material(ringSource) : new Material(sparkShader);
        ringMaterial.name = "SlowMoRing (runtime)";
        SetIfPresent(ringMaterial, "_Shape", 1f);
        SetIfPresent(ringMaterial, "_RingWidth", 0.04f);
        SetIfPresent(ringMaterial, "_Intensity", 1.6f);
        SetIfPresent(ringMaterial, "_CameraPull", 1.2f);

        // Glow
        var g = new GameObject("SlowMoGlow");
        g.transform.SetParent(root, false);
        g.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var gr = g.AddComponent<MeshRenderer>();
        gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        gr.receiveShadows = false;
        Shader glow = Shader.Find("Custom/SoftGlow");
        glowMaterial = new Material(glow != null ? glow : Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "SlowMoGlow (runtime)" };
        glowMaterial.SetFloat("_Falloff", 1.6f);
        glowMaterial.SetFloat("_Core", 0.02f);
        gr.sharedMaterial = glowMaterial;
        glowQuad = g.transform;

        // Motes: born in a sphere round the runner, left behind in the world, barely drifting.
        var m = new GameObject("SlowMoMotes");
        m.transform.SetParent(root, false);
        motes = m.AddComponent<ParticleSystem>();
        motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = motes.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        main.startColor = new ParticleSystem.MinMaxGradient(Linear(moteColor), Linear(Color.Lerp(moteColor, Color.white, 0.7f)));
        main.maxParticles = 120;
        var em = motes.emission;
        em.rateOverTime = 0f;
        var sh = motes.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = moteRadius;
        sh.radiusThickness = 0.6f;
        var col = motes.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var mr = m.GetComponent<ParticleSystemRenderer>();
        mr.renderMode = ParticleSystemRenderMode.Billboard;
        mr.sharedMaterial = moteMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        // Rings: a slow ripple out from the runner.
        var rgo = new GameObject("SlowMoRings");
        rgo.transform.SetParent(root, false);
        rings = rgo.AddComponent<ParticleSystem>();
        rings.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var rm = rings.main;
        rm.loop = false;
        rm.playOnAwake = false;
        rm.simulationSpace = ParticleSystemSimulationSpace.World;
        rm.scalingMode = ParticleSystemScalingMode.Shape;
        rm.startSpeed = 0f;
        rm.maxParticles = 8;
        var re = rings.emission;
        re.enabled = false;
        var rs = rings.shape;
        rs.enabled = false;
        var size = rings.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.1f, 0f, 2f), new Keyframe(1f, 1f, 0.4f, 0f)));
        var rc = rings.colorOverLifetime;
        rc.enabled = true;
        var rg = new Gradient();
        rg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                   new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
        rc.color = rg;
        var rr = rgo.GetComponent<ParticleSystemRenderer>();
        rr.renderMode = ParticleSystemRenderMode.Billboard;
        rr.sharedMaterial = ringMaterial;
        rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rr.receiveShadows = false;
    }

    static void SetIfPresent(Material m, string prop, float v)
    {
        if (m.HasProperty(prop)) m.SetFloat(prop, v);
    }

    static Color Linear(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
}
