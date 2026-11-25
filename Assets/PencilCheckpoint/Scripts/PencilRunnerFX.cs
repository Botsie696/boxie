using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Pencil checkpoint: the runner's trail effects while the pencil look is in force (sits next to
/// PencilCheckpointController; the shader PencilCheckpoint/RunnerFX only draws where the look is on screen).
///   Front trail  the leg ahead of the runner being sketched just before it gets there: construction lines, a dashed
///                guide, fading hatching and a scribbling pencil tip, laid flat on the face the runner rides and
///                stopping at the end of the leg
///   Flecks       graphite specks and curls of pencil shaving shed off the face behind the runner, more on a side
///                switch and a landing
///   Streaks      short hand-drawn speed lines peeling back past the runner, more the faster it goes
/// The glow / face ribbon trail is untouched (PixelFaceTrail's PencilRibbon already turns it to graphite).
/// Uses its own System.Random so the path generator's UnityEngine.Random sequence is unchanged.
/// </summary>
[DisallowMultipleComponent]
public class PencilRunnerFX : MonoBehaviour
{
    public PencilCheckpointController controller;
    public CubeRailRunner runner;

    [Header("Materials (PencilCheckpoint/RunnerFX, Mode Front / Fleck / Streak)")]
    public Material frontMaterial;
    public Material fleckMaterial;
    public Material streakMaterial;

    [Header("Front trail")]
    public bool frontTrail = true;
    [Tooltip("How far ahead of the runner the sketch reaches, in cubes (stops at the end of the leg).")]
    [Range(1f, 8f)] public float frontLength = 3.5f;
    [Tooltip("Gap between the runner and where the sketch starts, in cubes.")]
    [Range(0f, 1.5f)] public float frontGap = 0.45f;
    [Tooltip("How far off the face the sketch floats (world units).")]
    [Range(0.002f, 0.1f)] public float lift = 0.025f;
    [Tooltip("Seconds to fade the sketch in / out (look on and off, rolls, flights).")]
    [Range(0.02f, 1f)] public float frontFade = 0.15f;

    [Header("Flecks")]
    public bool flecks = true;
    [Tooltip("Flecks shed per cube travelled.")]
    [Range(0f, 8f)] public float flecksPerUnit = 3f;
    public Vector2 fleckSize = new Vector2(0.08f, 0.2f);
    public Vector2 fleckLife = new Vector2(0.45f, 0.9f);
    [Range(0, 40)] public int switchBurst = 12;
    [Range(0, 60)] public int landingBurst = 18;

    [Header("Speed streaks")]
    public bool streaks = true;
    [Tooltip("Streaks per cube travelled at calm speed...")]
    [Range(0f, 4f)] public float streaksPerUnit = 0.9f;
    [Tooltip("...and at fast speed.")]
    [Range(0f, 6f)] public float fastStreaksPerUnit = 1.6f;
    public float calmSpeed = 8f, fastSpeed = 24f;
    [Range(0.01f, 0.1f)] public float streakWidth = 0.055f;

    static readonly int AlphaId = Shader.PropertyToID("_Alpha");

    GameObject frontGo;
    Mesh frontMesh;
    MeshRenderer frontMr;
    MaterialPropertyBlock block;
    readonly Vector3[] fv = new Vector3[4];
    readonly Vector2[] fuv = new Vector2[4];
    readonly Vector2[] fuv1 = new Vector2[4];
    float frontAlpha;

    ParticleSystem fleckSys, streakSys;
    readonly System.Random rng = new System.Random(8128);
    Vector3 lastPos;
    bool hasLast;
    float fleckDebt, streakDebt;

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    CubeRailRunner hooked;   // the runner whose events this listens to

    void OnEnable()
    {
        if (block == null) block = new MaterialPropertyBlock();
        hasLast = false;
        Resolve();
    }

    // The runner can turn up after this (or be replaced on a restart): find it, listen to it, (re)make the effects.
    bool Resolve()
    {
        if (controller == null) controller = GetComponent<PencilCheckpointController>();
        if (runner == null) runner = controller != null && controller.runner != null ? controller.runner : FindFirstObjectByType<CubeRailRunner>();
        if (runner == null) return false;
        if (hooked != runner)
        {
            Unhook();
            runner.TapTaken += OnTap;
            runner.Landed += OnLanded;
            hooked = runner;
            hasLast = false;
        }
        Build();
        return true;
    }

    void Unhook()
    {
        if (hooked != null) { hooked.TapTaken -= OnTap; hooked.Landed -= OnLanded; }
        hooked = null;
    }

    void OnDisable()
    {
        Unhook();
        if (frontGo != null) frontGo.SetActive(false);
        if (fleckSys != null) fleckSys.Clear();
        if (streakSys != null) streakSys.Clear();
        frontAlpha = 0f;
    }

    void OnDestroy()
    {
        if (frontGo != null) Destroy(frontGo);
        if (frontMesh != null) Destroy(frontMesh);
        if (fleckSys != null) Destroy(fleckSys.gameObject);
        if (streakSys != null) Destroy(streakSys.gameObject);
    }

    void Build()
    {
        if (frontGo == null && frontMaterial != null)
        {
            frontGo = new GameObject("PencilFrontTrail");
            frontMesh = new Mesh { name = "PencilFrontTrail" };
            frontMesh.MarkDynamic();
            frontGo.AddComponent<MeshFilter>().sharedMesh = frontMesh;
            frontMr = frontGo.AddComponent<MeshRenderer>();
            frontMr.sharedMaterial = frontMaterial;
            Quiet(frontMr);
        }
        if (frontGo != null) frontGo.SetActive(true);
        if (fleckSys == null && fleckMaterial != null) fleckSys = MakeSystem("PencilFlecks", fleckMaterial, false);
        if (streakSys == null && streakMaterial != null) streakSys = MakeSystem("PencilStreaks", streakMaterial, true);
    }

    static void Quiet(Renderer r)
    {
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    // A world-space system fed only by Emit() from here.
    ParticleSystem MakeSystem(string name, Material mat, bool stretch)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = stretch ? 120 : 300;
        main.gravityModifier = stretch ? 0f : 0.35f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 1f));
        var em = ps.emission; em.enabled = false;
        var shape = ps.shape; shape.enabled = false;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  stretch ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) }
                          : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var size = ps.sizeOverLifetime;
        size.enabled = !stretch;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.4f));
        if (!stretch)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0f;
            limit.dampen = 0.12f;
        }

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        if (stretch) { r.lengthScale = 2.5f; r.velocityScale = 0.06f; }
        r.alignment = ParticleSystemRenderSpace.View;
        Quiet(r);
        ps.Play();
        return ps;
    }

    bool Active => controller == null || controller.PencilOnScreen;

    void LateUpdate()
    {
        if (!Resolve()) return;
        bool on = Active && !runner.IsGameOver;
        bool grounded = on && runner.currentCube != null && !runner.isFlying;
        float scale = runner.currentCube != null ? Mathf.Max(0.01f, runner.currentCube.lossyScale.x) : runner.UnitSize;

        UpdateFront(grounded && frontTrail && !runner.IsRevolving, scale);

        // Distance-fed emission.
        Vector3 pos = runner.transform.position;
        float step = hasLast ? (pos - lastPos).magnitude : 0f;
        if (step > Mathf.Max(1.5f, runner.forwardSpeed * Time.deltaTime * 4f)) step = 0f;   // teleport / rescue snap
        lastPos = pos;
        hasLast = true;
        if (!grounded || !runner.IsRunning) { fleckDebt = streakDebt = 0f; return; }

        float cubes = step / scale;
        Vector3 n = runner.SideNormal(runner.currentSideIndex);
        Vector3 fwd = runner.currentCube.forward;
        Vector3 face = runner.PreviewFacePoint(runner.currentSideIndex, lift);

        if (flecks && fleckSys != null)
        {
            fleckDebt += cubes * flecksPerUnit;
            while (fleckDebt >= 1f) { fleckDebt -= 1f; EmitFleck(face - fwd * R(0.1f, 0.4f) * scale, n, fwd, scale, 0.8f); }
        }
        if (streaks && streakSys != null)
        {
            float fast = Mathf.InverseLerp(calmSpeed, fastSpeed, runner.CurrentMoveSpeed);
            streakDebt += cubes * Mathf.Lerp(streaksPerUnit, fastStreaksPerUnit, fast);
            while (streakDebt >= 1f) { streakDebt -= 1f; EmitStreak(face, n, fwd, scale); }
        }
    }

    void UpdateFront(bool show, float scale)
    {
        if (frontGo == null) return;
        float target = show ? 1f : 0f;
        frontAlpha = Mathf.MoveTowards(frontAlpha, target, Time.deltaTime / Mathf.Max(0.01f, frontFade));
        if (frontAlpha <= 0.001f && !show) { frontMr.enabled = false; return; }
        if (runner.currentCube == null) { frontMr.enabled = false; return; }

        Vector3 fwd = runner.currentCube.forward;
        Vector3 n = runner.SideNormal(runner.currentSideIndex);
        Vector3 side = Vector3.Cross(n, fwd).normalized;
        Vector3 face = runner.PreviewFacePoint(runner.currentSideIndex, lift);

        // Stop at the end of the leg (endPosition is the last cube's centre; its face runs half a cube further).
        float reach = frontLength * scale;
        var seg = controller != null && controller.pathGen != null ? controller.pathGen.PlayerSegment : null;
        if (seg != null && Vector3.Dot(seg.direction.normalized, fwd) > 0.9f)
            reach = Mathf.Min(reach, Vector3.Dot(seg.endPosition - runner.transform.position, fwd) + scale * 0.48f);
        float a = frontGap * scale, b = reach;
        if (b - a < 0.25f * scale) { frontMr.enabled = false; return; }

        frontMr.enabled = true;
        float half = scale * 0.5f;
        Vector3 pa = face + fwd * a, pb = face + fwd * b;
        fv[0] = pa - side * half; fv[1] = pa + side * half;
        fv[2] = pb - side * half; fv[3] = pb + side * half;
        // Along in face units measured in world space, so the strokes stay where they were drawn as the runner nears.
        float ua = Vector3.Dot(pa, fwd) / scale, ub = Vector3.Dot(pb, fwd) / scale;
        fuv[0] = new Vector2(ua, -0.5f); fuv[1] = new Vector2(ua, 0.5f);
        fuv[2] = new Vector2(ub, -0.5f); fuv[3] = new Vector2(ub, 0.5f);
        fuv1[0] = fuv1[1] = Vector2.zero;
        fuv1[2] = fuv1[3] = new Vector2(1f, 0f);

        frontMesh.Clear();
        frontMesh.vertices = fv;
        frontMesh.uv = fuv;
        frontMesh.uv2 = fuv1;
        frontMesh.triangles = Tris;
        frontMesh.RecalculateBounds();

        frontMr.GetPropertyBlock(block);
        block.SetFloat(AlphaId, frontAlpha);
        frontMr.SetPropertyBlock(block);
    }

    static readonly int[] Tris = { 0, 2, 1, 1, 2, 3 };

    void EmitFleck(Vector3 at, Vector3 n, Vector3 fwd, float scale, float kick)
    {
        Vector3 side = Vector3.Cross(n, fwd);
        var ep = new ParticleSystem.EmitParams
        {
            position = at + side * R(-0.35f, 0.35f) * scale,
            velocity = (n * R(0.6f, 1.8f) + side * R(-1f, 1f) - fwd * R(0.2f, 1f)) * kick * scale,
            startSize = R(fleckSize.x, fleckSize.y) * scale,
            startLifetime = R(fleckLife.x, fleckLife.y),
            rotation = R(0f, 360f),
            startColor = new Color(R(0f, 1f), 1f, 1f, 1f),
            applyShapeToPosition = false,
        };
        fleckSys.Emit(ep, 1);
    }

    void EmitStreak(Vector3 face, Vector3 n, Vector3 fwd, float scale)
    {
        Vector3 side = Vector3.Cross(n, fwd);
        float s = R(0f, 1f) < 0.5f ? -1f : 1f;
        var ep = new ParticleSystem.EmitParams
        {
            position = face + side * s * R(0.45f, 0.85f) * scale + n * R(0.1f, 0.9f) * scale + fwd * R(0f, 0.6f) * scale,
            velocity = -fwd * Mathf.Max(3f, runner.CurrentMoveSpeed) * R(0.3f, 0.45f),
            startSize = streakWidth * scale,
            startLifetime = R(0.22f, 0.38f),
            startColor = new Color(R(0f, 1f), 1f, 1f, 1f),
            applyShapeToPosition = false,
        };
        streakSys.Emit(ep, 1);
    }

    void Burst(int count, float kick)
    {
        if (!Active || fleckSys == null || runner.currentCube == null || count <= 0) return;
        float scale = Mathf.Max(0.01f, runner.currentCube.lossyScale.x);
        Vector3 n = runner.SideNormal(runner.currentSideIndex);
        Vector3 fwd = runner.currentCube.forward;
        Vector3 face = runner.PreviewFacePoint(runner.currentSideIndex, lift);
        for (int i = 0; i < count; i++) EmitFleck(face, n, fwd, scale, kick);
        if (streaks && streakSys != null) for (int i = 0; i < count / 4; i++) EmitStreak(face, n, fwd, scale);
    }

    void OnTap(int dir) { if (flecks) Burst(switchBurst, 1.3f); }
    void OnLanded() { if (flecks) Burst(landingBurst, 1.6f); }
}
