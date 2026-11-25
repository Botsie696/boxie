using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The runner's side of the power diamond (DynamicPathGeneratorPower.cs): the pickup burst, the
/// autopilot that steers and speeds the runner up, the countdown, the slow-down and the hand-over.
///
/// Auto-added to the runner by the generator when powerDiamonds is on. Everything is built at
/// runtime and nothing here has a collider. It borrows RunnerBoostFX for the aura, speed lines and
/// rings and RunnerTrailFX for the trail colour and bursts, when those are on the runner.
///
/// Autopilot: the runner is invulnerable (CubeRailRunner.PowerAuto), player taps only make a ripple,
/// and the same closed-loop steering the AutoPlayer bot uses puts it on the right face for each turn.
/// It ends when the runner lands on the leg after the last driven one, at normal speed.
/// </summary>
public class RunnerPowerFX : MonoBehaviour
{
    public DynamicPathGenerator gen;

    [Header("Pickup")]
    [Tooltip("Seconds the game freezes at the pickup.")]
    [Range(0f, 0.12f)] public float hitStopSeconds = 0.05f;
    [Tooltip("Sparkle motes that fly from the pickup to the AUTO meter.")]
    [Range(0, 12)] public int moteCount = 7;
    [Tooltip("Shards that burst out of the diamond.")]
    [Range(0, 40)] public int shardCount = 16;

    [Header("Hand-over")]
    [Tooltip("A tap this long before control returns still counts for the hand-over leg.")]
    [Range(0f, 0.6f)] public float inputBufferSeconds = 0.3f;
    [Tooltip("Time scale for a moment at the hand-over (catch your breath). 1 = off.")]
    [Range(0.6f, 1f)] public float handoverTimeDip = 0.85f;
    [Range(0.05f, 1f)] public float handoverDipSeconds = 0.3f;

    [Header("Runway lights")]
    [Range(0, 16)] public int runwayLights = 12;

    public bool Active { get; private set; }
    public bool MagnetOn => Active && !pickupFrozen;

    private CubeRailRunner player;
    private RunnerBoostFX boostFx;
    private RunnerTrailFX trailFx;
    private Color col = new Color(0.55f, 0.95f, 1f, 1f);

    private int endSerial;
    private float activeTime, rampT, factor = 1f, initialRemain = 1f, remainSec = 99f;
    private float tintAmount;
    private bool pickupFrozen;
    private float hitStopEnd, prevScale = 1f, dipEnd;
    private int lastSerial = -1;
    private float lastTapTime = -10f;
    private int lastTapCode;

    // steering (the same closed loop as AutoPlayer)
    private Vector3 pendingCorner;
    private bool hasPending;

    // ---- HUD ----
    private RectTransform hudRoot, meterRoot, meterFill, hint;
    private Image[] pips = new Image[3];
    private Image[] edges = new Image[4];
    private Text autoLabel;
    private Image meterFillImage;
    private readonly List<Mote> motes = new List<Mote>();
    private readonly List<Image> ripples = new List<Image>();
    private readonly List<float> rippleT = new List<float>();
    private float flash, breathT = 1f, meterOutT = -1f, hintT = 1f, meterShown, arrivedFrac;
    private int hintDir;
    private Sprite spGradient, spRing, spDot;

    class Mote { public Image img; public Vector2 from, ctrl, to; public float t, delay; }

    // ---- world FX ----
    private ParticleSystem shards;
    private Material shardMat;
    private Transform flashQuad;
    private Material flashMat;
    private float flashQuadT = 1f;
    private Vector3 flashQuadPos;
    private readonly List<Transform> lights = new List<Transform>();
    private readonly List<Material> lightMats = new List<Material>();
    private readonly List<GameObject> cubeBuf = new List<GameObject>();
    private Material lightMat;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int FadeId = Shader.PropertyToID("_Fade");

    void Start()
    {
        player = GetComponent<CubeRailRunner>();
        boostFx = GetComponent<RunnerBoostFX>();
        trailFx = GetComponent<RunnerTrailFX>();
        if (player != null) player.AutoInputIgnored += OnTapIgnored;
    }

    void OnDestroy()
    {
        if (player != null) { player.AutoInputIgnored -= OnTapIgnored; player.PowerAuto = false; player.PowerSpeedFactor = 1f; }
        if (hitStopEnd > 0f) TimeScaleMixer.Set(TimeScaleMixer.Source.HitStop, 1f);
        if (dipEnd > 0f) TimeScaleMixer.Set(TimeScaleMixer.Source.PowerDip, 1f);
        if (gen != null) gen.PowerZoom = 1f;
        if (hudRoot != null) Destroy(hudRoot.GetComponentInParent<Canvas>().gameObject);
        if (flashMat != null) Destroy(flashMat);
        if (shardMat != null) Destroy(shardMat);
        foreach (Material m in lightMats) if (m != null) Destroy(m);
    }

    // ------------------------------------------------------------------
    // Begin / End
    // ------------------------------------------------------------------

    public void Begin(Vector3 at, int diamondSerial)
    {
        if (Active || player == null || gen == null) return;
        if (boostFx == null) boostFx = GetComponent<RunnerBoostFX>();
        if (trailFx == null) trailFx = GetComponent<RunnerTrailFX>();
        col = gen.powerColor;
        EnsureWorldFx();
        EnsureHud();

        Active = true;
        endSerial = diamondSerial + gen.powerAutoLegs;   // fallback until the handover leg is laid
        handoverPicked = false;
        TryPickHandover(gen.PlayerSerial);
        SetEdgeColor(new Color(0.8f, 1f, 1f));
        activeTime = 0f; rampT = 0f; factor = 1f; initialRemain = 1f; remainSec = 99f;
        lastSerial = gen.PlayerSerial;
        hasPending = false;
        player.PowerAuto = true;

        // Hit-stop: a brief freeze with everything else going off around it.
        if (hitStopSeconds > 0f)
        {
            TimeScaleMixer.Set(TimeScaleMixer.Source.HitStop, 0f);
            pickupFrozen = true;
            hitStopEnd = Time.unscaledTime + hitStopSeconds;
        }

        Haptics.MediumImpact();
        Haptics.LightImpact();
        flash = 1f;
        Shatter(at);
        StartMotes(at);
        if (trailFx != null) trailFx.SmallBurst(col, 7f);
        if (boostFx != null)
        {
            boostFx.externalOn = true;
            boostFx.externalColor = col;
            boostFx.externalAmount = 1f;
            boostFx.externalFlicker = 0f;
            boostFx.PowerRing(12f, 0.5f, 1f);
        }
        if (autoLabel != null) autoLabel.text = "";
        meterRoot.gameObject.SetActive(true);
        meterOutT = -1f; meterRoot.anchoredPosition = Vector2.zero; meterRoot.localRotation = Quaternion.identity;
        meterShown = 0f; arrivedFrac = 0f;
    }

    void End(bool handOver)
    {
        Active = false;
        player.PowerAuto = false;
        player.PowerSpeedFactor = 1f;
        gen.PowerZoom = 1f;
        if (boostFx != null) { boostFx.externalOn = false; boostFx.externalFlicker = 0f; }
        if (handOver) meterOutT = 0f; else meterRoot.gameObject.SetActive(false);
        foreach (Transform l in lights) l.gameObject.SetActive(false);
        if (!handOver) return;

        // Wordless handover: ring + sparks, an edge-glow breath, chevron burst, haptic, tap hint, buffered tap.
        Haptics.MediumImpact();
        Vector3 p = transform.position;
        EmitShards(p, Mathf.Max(8, shardCount / 2), 4f, 0.6f);
        if (boostFx != null) boostFx.PowerRing(10f, 0.55f, 0.9f);
        if (trailFx != null) trailFx.SmallBurst(col, 6f);
        breathT = 0f;
        SetEdgeColor(new Color(1f, 0.85f, 0.3f));   // guidance yellow
        var arrows = FindFirstObjectByType<TurnFaceArrows>();
        if (arrows != null) arrows.Burst(1f);
        ShowTapHint();
        if (handoverTimeDip < 1f)
        {
            TimeScaleMixer.Set(TimeScaleMixer.Source.PowerDip, handoverTimeDip);
            dipEnd = Time.unscaledTime + handoverDipSeconds;
        }
        if (Time.time - lastTapTime <= inputBufferSeconds && Mathf.Abs(lastTapCode) == 1) player.SimulateTap(lastTapCode);
    }

    void OnTapIgnored(int code)
    {
        lastTapTime = Time.time;
        lastTapCode = code;
        Ripple(Input.touchCount > 0 ? (Vector2)Input.GetTouch(0).position : (Vector2)Input.mousePosition);
    }

    // ------------------------------------------------------------------
    // Update
    // ------------------------------------------------------------------

    void Update()
    {
        if (hitStopEnd > 0f && Time.unscaledTime >= hitStopEnd)
        {
            TimeScaleMixer.Set(TimeScaleMixer.Source.HitStop, 1f);
            hitStopEnd = 0f;
            pickupFrozen = false;
        }
        if (dipEnd > 0f && Time.unscaledTime >= dipEnd)
        {
            TimeScaleMixer.Set(TimeScaleMixer.Source.PowerDip, 1f);
            dipEnd = 0f;
        }

        float dt = Time.unscaledDeltaTime;
        if (Active && !pickupFrozen) TickAuto(Time.deltaTime);
        if (hudRoot != null) UpdateHud(dt);
        UpdateWorldFx(dt);
    }

    void TickAuto(float dt)
    {
        if (player.IsGameOver) { End(false); return; }
        int ps = gen.PlayerSerial;
        if (ps > endSerial) { End(true); return; }
        activeTime += dt;

        if (!handoverPicked) TryPickHandover(ps);
        // Speed must be normal 0.1 s BEFORE the handover leg starts.
        remainSec = Mathf.Max(0f, RemainingSeconds(ps) - 0.1f);
        if (activeTime < 0.6f) initialRemain = Mathf.Max(initialRemain, remainSec);

        // Speed: ease up (ease-in) at the start, ease down so it is back to 1 as the last driven leg ends.
        rampT = Mathf.Min(1f, rampT + dt / Mathf.Max(0.05f, gen.powerRampSeconds));
        float mult = gen.powerSpeedMultiplier;
        float up = 1f + (mult - 1f) * rampT * rampT;
        float s = Mathf.Clamp01(remainSec / Mathf.Max(0.3f, gen.powerSlowSeconds));
        float down = 1f + (mult - 1f) * (s * s * (3f - 2f * s));
        factor = Mathf.Min(up, down);
        player.PowerSpeedFactor = factor;

        gen.PowerZoom = Mathf.Max(1f, gen.powerCameraZoom);

        // Countdown: pips blink out 3-2-1, the aura flickers faster, the speed lines thin.
        float cd = Mathf.Max(0.6f, gen.powerCountdownSeconds);
        bool counting = remainSec <= cd;
        float k = counting ? 1f - remainSec / cd : 0f;
        if (boostFx != null)
        {
            boostFx.externalFlicker = counting ? Mathf.Lerp(5f, 16f, k) : 0f;
            boostFx.externalAmount = counting ? Mathf.Lerp(1f, 0.3f, k) : 1f;
        }

        // A turn passed: a small ring and a spark arc, "corner whoosh".
        if (ps != lastSerial)
        {
            lastSerial = ps;
            if (boostFx != null) boostFx.PowerRing(5f, 0.3f, 0.8f);
            if (trailFx != null) trailFx.SmallBurst(col, 4f);
        }

        Steer();
        UpdateRunway();
    }

    bool handoverPicked;

    // The first safe leg whose START is at least minAutoSeconds ahead (at autopilot speed) along the route.
    void TryPickHandover(int ps)
    {
        DynamicPathGenerator.Segment cur = gen.SegmentBySerial(ps);
        if (cur == null) return;
        float need = gen.powerMinAutoSeconds * Mathf.Max(1f, player.forwardSpeed) * gen.powerSpeedMultiplier;
        float dist = Mathf.Max(0f, Vector3.Dot(cur.endPosition - transform.position, cur.direction));
        for (int s = ps + 1; s <= ps + 40; s++)
        {
            DynamicPathGenerator.Segment seg = gen.SegmentBySerial(s);
            if (seg == null) return;   // not laid yet: try again next frame
            float startDist = dist + gen.cubeSize;
            if (startDist >= need && gen.PowerLegSafe(seg))
            {
                handoverPicked = true;
                endSerial = s - 1;
                gen.PowerMarkHandover(s);
                return;
            }
            dist = startDist + Vector3.Distance(seg.startPosition, seg.endPosition);
        }
    }

    // Seconds to the end of the last driven leg, summed over the legs still to cross.
    float RemainingSeconds(int ps)
    {
        float dist = 0f;
        for (int s = ps; s <= endSerial; s++)
        {
            DynamicPathGenerator.Segment seg = gen.SegmentBySerial(s);
            if (seg == null) { dist += 40f * gen.cubeSize; continue; }
            if (s == ps) dist += Mathf.Max(0f, Vector3.Dot(seg.endPosition - transform.position, seg.direction));
            else dist += Vector3.Distance(seg.startPosition, seg.endPosition) + gen.cubeSize;
        }
        return dist / Mathf.Max(1f, player.forwardSpeed * Mathf.Max(1f, factor));
    }

    // ------------------------------------------------------------------
    // Steering: stand on the face that points along the outgoing leg before each corner.
    // ------------------------------------------------------------------

    void Steer()
    {
        Transform cube = player.currentCube;
        if (cube == null || player.isFlying || player.IsGameOver) return;

        var corners = gen.GetUpcomingCorners(1);
        if (corners.Count == 0) { hasPending = false; return; }
        DynamicPathGenerator.PathCorner corner = corners[0];
        if (Vector3.Dot(cube.forward, corner.incomingDirection.normalized) < 0.9f) return;

        float along = Vector3.Dot(corner.position - transform.position, cube.forward);
        float seconds = Mathf.Max(0f, along) / Mathf.Max(0.01f, player.forwardSpeed * factor);
        if (!hasPending || (corner.position - pendingCorner).sqrMagnitude > 0.0001f)
        {
            if (seconds > 1.6f) return;
            pendingCorner = corner.position;
            hasPending = true;
        }
        if (player.QueuedInputCount > 0) return;

        int presses = PressesToInnerFace(cube, corner.outgoingDirection.normalized, out int dir);
        if (presses == 0) return;
        player.PowerPress(dir, presses == 2);
    }

    int PressesToInnerFace(Transform cube, Vector3 want, out int direction)
    {
        direction = 1;
        Vector3 axis = cube.forward;
        Vector3 have = player.SideNormal(player.currentSideIndex);
        if (Vector3.Dot(have, want) > 0.9f) return 0;
        if (Vector3.Dot(have, want) < -0.9f) return 2;
        if (Vector3.Dot(Quaternion.AngleAxis(-90f, axis) * have, want) > 0.9f) { direction = 1; return 1; }
        direction = -1;
        return 1;
    }

    void ShowTapHint()
    {
        hintDir = 0;
        var corners = gen.GetUpcomingCorners(1);
        if (corners.Count > 0 && player.currentCube != null)
        {
            int side = player.SideFacing(corners[0].outgoingDirection);
            if (side != player.currentSideIndex) hintDir = player.TapDirectionToReach(side);
        }
        hintT = hintDir != 0 ? 0f : 1f;
    }

    // ------------------------------------------------------------------
    // Runway lights: a chase of glows over the cubes ahead.
    // ------------------------------------------------------------------

    void UpdateRunway()
    {
        if (runwayLights <= 0 || lightMat == null) return;
        gen.CubesAhead(cubeBuf, runwayLights);
        Camera cam = Camera.main;
        Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
        Vector3 want = player.SideNormal(player.currentSideIndex);
        float fade = Mathf.Clamp01(remainSec / Mathf.Max(0.3f, gen.powerCountdownSeconds));   // the chase thins out at the end
        float rate = Mathf.Lerp(7f, 15f, fade);
        float size = gen.cubeSize * 1.9f;

        for (int i = 0; i < lights.Count; i++)
        {
            if (i >= cubeBuf.Count || gen.CubeShown(cubeBuf[i]) < 0.9f) { lights[i].gameObject.SetActive(false); continue; }
            Transform ct = cubeBuf[i].transform;
            Vector3 n = ct.up; float best = Vector3.Dot(n, want);
            foreach (Vector3 a in new[] { -ct.up, ct.right, -ct.right })
            { float d = Vector3.Dot(a, want); if (d > best) { best = d; n = a; } }

            float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * rate - i * 0.85f);
            float a2 = wave * (1f - i / (float)lights.Count) * fade;
            lights[i].gameObject.SetActive(a2 > 0.02f);
            lights[i].SetPositionAndRotation(ct.position + n * (gen.cubeSize * 0.62f), face);
            lights[i].localScale = Vector3.one * size;
            lightMats[i].SetColor(ColorId, col);
            lightMats[i].SetFloat(FadeId, a2 * 0.9f);
        }
    }

    // ------------------------------------------------------------------
    // World FX
    // ------------------------------------------------------------------

    void EnsureWorldFx()
    {
        if (shards != null) return;
        var go = new GameObject("PowerShards");
        go.transform.SetParent(transform, false);
        shards = go.AddComponent<ParticleSystem>();
        shards.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = shards.main;
        main.loop = false; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;
        main.startSpeed = 0f;
        var em = shards.emission; em.enabled = false;
        var sh = shards.shape; sh.enabled = false;
        var colOverLife = shards.colorOverLifetime;
        colOverLife.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
        colOverLife.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.lengthScale = 2f; r.velocityScale = 0.06f;
        Material src = Resources.Load<Material>("BoostFX/BoostStreak");
        Shader sh2 = src != null ? src.shader : Shader.Find("Runner/TrailFX Spark");
        if (sh2 == null) sh2 = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        shardMat = src != null ? new Material(src) : new Material(sh2);
        if (shardMat.HasProperty("_Shape")) shardMat.SetFloat("_Shape", 0f);
        if (shardMat.HasProperty("_RayStrength")) shardMat.SetFloat("_RayStrength", 0f);
        if (shardMat.HasProperty("_Intensity")) shardMat.SetFloat("_Intensity", 2.6f);
        if (shardMat.HasProperty("_CameraPull")) shardMat.SetFloat("_CameraPull", 1.2f);
        r.sharedMaterial = shardMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        Shader glow = Shader.Find("Custom/SoftGlow");
        Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        if (glow != null)
        {
            flashMat = new Material(glow) { name = "PowerFlash" };
            flashMat.SetFloat("_Falloff", 1.4f); flashMat.SetFloat("_Core", 0.1f);
            flashQuad = NewQuad("PowerFlash", quad, flashMat);
            flashQuad.gameObject.SetActive(false);

            lightMat = new Material(glow) { name = "PowerRunway" };
            for (int i = 0; i < runwayLights; i++)
            {
                var m = new Material(lightMat) { name = "PowerRunway" + i };
                m.SetFloat("_Falloff", 2.2f); m.SetFloat("_Core", 0.15f);
                lightMats.Add(m);
                Transform q = NewQuad("RunwayLight", quad, m);
                q.gameObject.SetActive(false);
                lights.Add(q);
            }
        }
    }

    Transform NewQuad(string name, Mesh mesh, Material m)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform.parent, false);   // not under the runner: it must not roll with it
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    static Color Lin(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

    void EmitShards(Vector3 at, int n, float speed, float life)
    {
        if (shards == null) return;
        for (int i = 0; i < n; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            Color c = Color.Lerp(col, Color.white, Random.value * 0.7f);
            shards.Emit(new ParticleSystem.EmitParams
            {
                position = at,
                velocity = dir * speed * (0.6f + Random.value * 0.8f),
                startLifetime = life * (0.7f + Random.value * 0.6f),
                startSize = (0.14f + Random.value * 0.2f) * Mathf.Max(0.01f, gen.cubeSize),
                startColor = Lin(c)
            }, 1);
        }
    }

    void Shatter(Vector3 at)
    {
        EmitShards(at, shardCount, 9f * gen.cubeSize, 0.55f);
        if (boostFx != null) boostFx.PowerRing(9f, 0.45f, 1f);
        flashQuadT = 0f;
        flashQuadPos = at;
    }

    void UpdateWorldFx(float dt)
    {
        // The star flash at the pickup: swells and fades in about 0.15 s.
        if (flashQuad != null)
        {
            if (flashQuadT < 1f)
            {
                flashQuadT += dt / 0.2f;
                Camera cam = Camera.main;
                float k = Mathf.Clamp01(flashQuadT);
                flashQuad.gameObject.SetActive(k < 1f);
                flashQuad.SetPositionAndRotation(flashQuadPos, cam != null ? cam.transform.rotation : Quaternion.identity);
                flashQuad.localScale = Vector3.one * gen.cubeSize * Mathf.Lerp(1f, 6f, 1f - (1f - k) * (1f - k));
                flashMat.SetColor(ColorId, Color.Lerp(col, Color.white, 0.5f));
                flashMat.SetFloat(FadeId, 1f - k);
            }
            else if (flashQuad.gameObject.activeSelf) flashQuad.gameObject.SetActive(false);
        }

        // Trail colour: blends in while it lasts and back over about 0.3 s after.
        if (trailFx != null)
        {
            float goal = Active ? 1f : 0f;
            tintAmount = Mathf.MoveTowards(tintAmount, goal, dt / (Active ? 0.15f : 0.3f));
            trailFx.powerTint = col;
            trailFx.powerTintAmount = tintAmount;
        }
    }

    // ------------------------------------------------------------------
    // HUD
    // ------------------------------------------------------------------

    void EnsureHud()
    {
        if (hudRoot != null) return;
        spGradient = MakeSprite(2, 64, (x, y) => { float a = 1f - y / 63f; return a * a; });
        spRing = MakeSprite(64, 64, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 31.5f;
            return Mathf.Clamp01(1f - Mathf.Abs(d - 0.82f) / 0.14f);
        });
        spDot = MakeSprite(32, 32, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 15.5f;
            return Mathf.Clamp01(1.4f - d * 1.4f);
        });

        var canvasGO = new GameObject("PowerHUDCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        hudRoot = new GameObject("Root", typeof(RectTransform)).GetComponent<RectTransform>();
        hudRoot.SetParent(canvasGO.transform, false);
        hudRoot.anchorMin = Vector2.zero; hudRoot.anchorMax = Vector2.one;
        hudRoot.offsetMin = hudRoot.offsetMax = Vector2.zero;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Screen-edge flash: four stepped strips.
        BuildEdges();

        // The AUTO meter, top centre.
        meterRoot = new GameObject("AutoMeter", typeof(RectTransform)).GetComponent<RectTransform>();
        meterRoot.SetParent(hudRoot, false);
        meterRoot.anchorMin = meterRoot.anchorMax = new Vector2(0.5f, 0.2f);   // bottom centre, 20% up from the bottom
        meterRoot.pivot = new Vector2(0.5f, 0.5f);
        meterRoot.anchoredPosition = Vector2.zero;
        meterRoot.sizeDelta = new Vector2(460f, 56f);
        var bg = NewImage("Back", meterRoot, null);
        Stretch(bg.rectTransform);
        bg.color = new Color(0.02f, 0.08f, 0.12f, 0.6f);
        var fillBG = NewImage("Fill", meterRoot, null);
        meterFill = fillBG.rectTransform;
        meterFill.anchorMin = new Vector2(0, 0); meterFill.anchorMax = new Vector2(0, 1); meterFill.pivot = new Vector2(0, 0.5f);
        meterFill.anchoredPosition = new Vector2(4, 0); meterFill.sizeDelta = new Vector2(0, -8);
        meterFillImage = fillBG;
        autoLabel = NewText("Label", meterRoot, font, 34, TextAnchor.MiddleLeft);
        autoLabel.rectTransform.anchoredPosition = new Vector2(18, 0);
        autoLabel.text = "";
        for (int i = 0; i < 3; i++)
        {
            pips[i] = NewImage("Pip" + i, meterRoot, spDot);
            var rt = pips[i].rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(30, 30);
            rt.anchoredPosition = new Vector2(-30 - i * 40, 0);
        }
        meterRoot.gameObject.SetActive(false);


        var hi = NewImage("TapHint", hudRoot, spRing);
        hint = hi.rectTransform;
        hint.sizeDelta = new Vector2(220, 220);
        hint.gameObject.SetActive(false);
    }

    void BuildEdges()
    {
        // left, right, top, bottom: stepped plain rects fading inward.
        for (int i = 0; i < 4; i++)
        {
            RectTransform holder = new GameObject("Edge" + i, typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            holder.SetParent(hudRoot, false);
            Stretch(holder);
            holder.GetComponent<CanvasGroup>().blocksRaycasts = false;
            const int steps = 14;
            for (int s = 0; s < steps; s++)
            {
                var img = NewImage("s" + s, holder, null);
                img.color = new Color(0.8f, 1f, 1f, 0.5f * Mathf.Pow(1f - s / (float)steps, 2.2f));
                var rt = img.rectTransform;
                float w = 9f;
                switch (i)
                {
                    case 0: rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 0.5f); rt.anchoredPosition = new Vector2(s * w, 0); rt.sizeDelta = new Vector2(w, 0); break;
                    case 1: rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 0.5f); rt.anchoredPosition = new Vector2(-s * w, 0); rt.sizeDelta = new Vector2(w, 0); break;
                    case 2: rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1); rt.anchoredPosition = new Vector2(0, -s * w); rt.sizeDelta = new Vector2(0, w); break;
                    default: rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0); rt.anchoredPosition = new Vector2(0, s * w); rt.sizeDelta = new Vector2(0, w); break;
                }
            }
            edges[i] = holder.gameObject.AddComponent<Image>();
            edges[i].color = Color.clear;
            edges[i].raycastTarget = false;
        }
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static Image NewImage(string name, Transform parent, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    static Text NewText(string name, Transform parent, Font font, int size, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = font; t.fontSize = size; t.fontStyle = FontStyle.Bold; t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = Color.white; t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor == TextAnchor.MiddleLeft ? new Vector2(0, 0.5f) : new Vector2(0.5f, 0.5f);
        rt.pivot = anchor == TextAnchor.MiddleLeft ? new Vector2(0, 0.5f) : new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(300, 60);
        return t;
    }

    static Sprite MakeSprite(int w, int h, System.Func<int, int, float> alpha)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x, y)) * 255f));
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    Vector2 ScreenToHud(Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(hudRoot, screen, null, out Vector2 local);
        return local;
    }

    void SetEdgeColor(Color c)
    {
        foreach (Image e in edges)
            if (e != null) foreach (Image b in e.GetComponentsInChildren<Image>()) if (b != e) b.color = new Color(c.r, c.g, c.b, b.color.a);
    }

    void StartMotes(Vector3 world)
    {
        Camera cam = Camera.main;
        if (cam == null || hudRoot == null) return;
        Vector2 from = ScreenToHud(cam.WorldToScreenPoint(world));
        Vector2 to = new Vector2(0f, hudRoot.rect.height * 0.5f - 170f - 28f);
        for (int i = 0; i < moteCount; i++)
        {
            Image img = NewImage("Mote", hudRoot, spDot);
            img.rectTransform.sizeDelta = new Vector2(34, 34);
            img.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
            img.color = Color.Lerp(col, Color.white, 0.5f);
            Vector2 mid = (from + to) * 0.5f + new Vector2(Random.Range(-260f, 260f), Random.Range(-60f, 160f));
            motes.Add(new Mote { img = img, from = from, ctrl = mid, to = to + new Vector2(Random.Range(-120f, 120f), 0f), t = 0f, delay = i * 0.04f });
        }
    }

    void Ripple(Vector2 screen)
    {
        if (hudRoot == null) return;
        Image img = NewImage("Ripple", hudRoot, spRing);
        img.rectTransform.anchoredPosition = ScreenToHud(screen);
        img.color = new Color(1f, 1f, 1f, 0.5f);
        ripples.Add(img); rippleT.Add(0f);
    }

    void UpdateHud(float dt)
    {
        // Edge flash: white-cyan, gone in about 0.25 s.
        flash = Mathf.MoveTowards(flash, 0f, dt / 0.25f);
        // Motes arc to the meter; the meter fills as they land.
        int total = Mathf.Max(1, moteCount);
        for (int i = motes.Count - 1; i >= 0; i--)
        {
            Mote m = motes[i];
            if (m.delay > 0f) { m.delay -= dt; continue; }
            m.t += dt / 0.5f;
            float k = Mathf.Clamp01(m.t);
            float e = k * k;
            Vector2 p = (1 - e) * (1 - e) * m.from + 2 * (1 - e) * e * m.ctrl + e * e * m.to;
            m.img.rectTransform.anchoredPosition = p;
            m.img.rectTransform.localScale = Vector3.one * (1f - 0.4f * k);
            if (m.t >= 1f) { Destroy(m.img.gameObject); motes.RemoveAt(i); arrivedFrac += 1f / total; }
        }
        if (moteCount == 0) arrivedFrac = 1f;

        if (Active)
        {
            float frac = Mathf.Clamp01(remainSec / Mathf.Max(0.01f, initialRemain));
            float shown = Mathf.Min(frac, Mathf.Clamp01(arrivedFrac));
            meterShown = Mathf.MoveTowards(meterShown, shown, dt * 3f);
            meterFill.sizeDelta = new Vector2(meterShown * (meterRoot.sizeDelta.x - 8f), meterFill.sizeDelta.y);
            bool counting = remainSec <= gen.powerCountdownSeconds;
            // The ring/bar warns with a bright, pulsing edge as the last moments drain.
            float pulse = counting ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 22f) : 0.85f;
            meterFillImage.color = Color.Lerp(col, Color.white, counting ? pulse : 0.2f);
            int lit = counting ? Mathf.Clamp(Mathf.CeilToInt(remainSec / (gen.powerCountdownSeconds / 3f)), 0, 3) : 3;
            for (int i = 0; i < 3; i++) pips[i].color = i < lit ? Color.white : new Color(1f, 1f, 1f, 0.15f);
        }

        // Handover edge-glow breath: in 0.15 s, out 0.35 s, up to 45%.
        float breath = 0f;
        if (breathT < 1f)
        {
            breathT += dt / 0.5f;
            float t = breathT * 0.5f / 0.15f;
            breath = breathT * 0.5f < 0.15f ? Mathf.Clamp01(t) : Mathf.Clamp01(1f - (breathT * 0.5f - 0.15f) / 0.35f);
        }
        foreach (Image e in edges) if (e != null) e.GetComponent<CanvasGroup>().alpha = Mathf.Max(flash * 0.8f, breath * 0.9f);

        // AUTO meter at the handover: a two-frame crack, then it slides out (0.2 s).
        if (meterOutT >= 0f)
        {
            meterOutT += dt;
            bool crack = meterOutT < 0.1f;
            meterRoot.localRotation = Quaternion.Euler(0, 0, crack ? (Mathf.Repeat(meterOutT, 0.05f) < 0.025f ? 4f : -4f) : 0f);
            meterFillImage.color = crack ? new Color(0.1f, 0.1f, 0.12f, 1f) : meterFillImage.color;
            float k = Mathf.Clamp01((meterOutT - 0.1f) / 0.2f);
            meterRoot.anchoredPosition = new Vector2(0f, -k * k * 260f);
            if (meterOutT >= 0.3f) { meterRoot.gameObject.SetActive(false); meterOutT = -1f; meterRoot.anchoredPosition = Vector2.zero; meterRoot.localRotation = Quaternion.identity; }
        }

        // Tap hint: a pulsing ring on the side to tap.
        if (hintT < 1f)
        {
            hintT += dt / 1.4f;
            hint.gameObject.SetActive(hintT < 1f);
            hint.anchoredPosition = new Vector2(hintDir * hudRoot.rect.width * 0.25f, -hudRoot.rect.height * 0.18f);
            float ph = Mathf.Repeat(hintT * 3f, 1f);
            hint.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.3f, ph);
            hint.GetComponent<Image>().color = new Color(col.r, col.g, col.b, (1f - ph) * 0.9f);
        }

        for (int i = ripples.Count - 1; i >= 0; i--)
        {
            rippleT[i] += dt / 0.35f;
            float k = rippleT[i];
            if (k >= 1f) { Destroy(ripples[i].gameObject); ripples.RemoveAt(i); rippleT.RemoveAt(i); continue; }
            ripples[i].rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(40f, 150f, k);
            ripples[i].color = new Color(1f, 1f, 1f, 0.5f * (1f - k));
        }
    }
}
