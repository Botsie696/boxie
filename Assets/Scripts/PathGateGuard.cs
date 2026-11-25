using UnityEngine;

/// <summary>
/// One live triangle gate (DynamicPathGeneratorGate.cs). Pooled: the generator hands it out with Spawn and takes it
/// back when its cube is culled or the track behind fades.
///
///   Drawn      Like PathObstacleGuard: the visual grows with its cube's ShownAmount and fades with CrossedAmount, so
///              it is never seen popping in or out. The colliders sit on unscaled children and are only on once the
///              gate is fully grown in (a half-drawn gate never kills).
///   Hit        The base bars beside the rail are triggers on the Obstacle layer. Unlike PathObstacleGuard, a hit counts
///              mid-roll too: the runner's sphere follows the real roll arc, so a roll toward a blocked face meets the
///              bar. Ends the run through CubeRailRunner.HitObstacle (so the shield saves it the same way); if the shield
///              saved it, the gate breaks so the runner is not hit again by the same bar.
///   Close call A roll back onto the landing face that finished less than closeCallSeconds before the frame, or a roll
///              off it started less than closeCallSeconds after the frame: CubeRailRunner.ReportGateCloseCall.
///   Coin       The special coin in the opening: a swept distance test against the runner, like the ordinary coins.
///   Glow       A slow pulse, and a short flash when the runner goes through cleanly.
/// </summary>
[DefaultExecutionOrder(60)] // after PathDepthShading (50) has set this frame's ShownAmount/CrossedAmount
public class PathGateGuard : MonoBehaviour
{
    static readonly int GlowId = Shader.PropertyToID("_Glow");

    [System.NonSerialized] public DynamicPathGenerator.Segment segment;
    /// <summary>The face (0 up, 1 right, 2 down, 3 left, CubeRailRunner.currentSideIndex's convention) the gate stands on.</summary>
    [System.NonSerialized] public int landingSide;

    DynamicPathGenerator gen;
    CubeRailRunner player;
    Rigidbody playerRb;
    GameObject hostCube;
    PathDepthShading shading;

    Transform visual, coin;
    MeshRenderer frameRenderer, coinRenderer;
    Collider[] cols;
    MaterialPropertyBlock mpb;

    float halfDepth;        // world units, along the leg
    public float HalfDepth => halfDepth;

    // Cube style (DynamicPathGeneratorGate.NewCubeGate): bars drawn exactly like the leg cube the gate stands on.
    MeshRenderer[] cubeParts;
    Renderer host;
    public bool CubeStyle => cubeParts != null;
    public void SetCubeParts(MeshRenderer[] parts) => cubeParts = parts;
    int coinValue;
    float pickupRadius;
    float closeCallSeconds, pulseSpeed, pulseDepth, baseGlow, passFlash;

    bool collidersOn, broken, hitHere, coinTaken, closeCallPaid, entered, passed;
    float lastOffTime = -10f, passTime = -10f, flashT = -1f, coinBurstT = -1f, breakT = -1f;
    float prevAlong;
    bool havePrev;
    Vector3 lastRunnerPos;
    bool haveRunnerPos;

    /// <summary>Builds the pooled object's parts once (called by the generator right after AddComponent).</summary>
    public void Build(Transform visual, MeshRenderer frameRenderer, Transform coin, MeshRenderer coinRenderer, Collider[] cols)
    {
        this.visual = visual;
        this.frameRenderer = frameRenderer;
        this.coin = coin;
        this.coinRenderer = coinRenderer;
        this.cols = cols;
        mpb = new MaterialPropertyBlock();
    }

    public void Spawn(DynamicPathGenerator gen, CubeRailRunner player, DynamicPathGenerator.Segment segment, GameObject hostCube,
                      PathDepthShading shading, int landingSide, float halfDepth, int coinValue, float pickupRadius,
                      float closeCallSeconds, float baseGlow, float pulseSpeed, float pulseDepth, float passFlash)
    {
        this.gen = gen;
        this.player = player;
        playerRb = player != null ? player.GetComponent<Rigidbody>() : null;
        this.segment = segment;
        this.hostCube = hostCube;
        this.shading = shading;
        this.landingSide = landingSide;
        this.halfDepth = halfDepth;
        this.coinValue = coinValue;
        this.pickupRadius = pickupRadius;
        this.closeCallSeconds = closeCallSeconds;
        this.baseGlow = baseGlow;
        this.pulseSpeed = pulseSpeed;
        this.pulseDepth = pulseDepth;
        this.passFlash = passFlash;

        broken = hitHere = coinTaken = closeCallPaid = entered = passed = false;
        lastOffTime = passTime = -10f;
        flashT = coinBurstT = breakT = -1f;
        havePrev = haveRunnerPos = false;
        coin.gameObject.SetActive(coinValue > 0);
        coin.localScale = Vector3.one;
        SetColliders(false);
        visual.localScale = Vector3.zero;
        gameObject.SetActive(true);
    }

    void SetColliders(bool on)
    {
        collidersOn = on;
        foreach (Collider c in cols) if (c != null) c.enabled = on;
    }

    void LateUpdate()
    {
        if (hostCube == null) { gen.ReleaseGate(this); return; }   // its cube was culled (or the path rebuilt)

        float shown = shading != null ? shading.ShownAmount(hostCube) : 1f;
        float crossed = shading != null ? shading.CrossedAmount(hostCube) : 0f;
        shown *= 1f - Mathf.SmoothStep(0f, 1f, crossed);
        if (passed && shown <= 0.01f) { gen.ReleaseGate(this); return; }

        float scale = Mathf.SmoothStep(0f, 1f, shown);
        if (breakT >= 0f)
        {
            // Shield save: the gate folds away fast instead of being hit again.
            breakT += Time.deltaTime / 0.25f;
            scale *= Mathf.Clamp01(1f - breakT);
            if (breakT >= 1f) { gen.ReleaseGate(this); return; }
        }
        visual.localScale = new Vector3(scale, scale, scale);

        bool wantCols = !broken && shown > 0.98f && crossed < 0.01f;
        if (wantCols != collidersOn) SetColliders(wantCols);

        TrackRunner();
        UpdateCoin();
        if (cubeParts != null) MatchHost();
        else UpdateGlow();
    }

    // Where the runner is along the leg, relative to the frame, and how the landing face was held through it.
    void TrackRunner()
    {
        if (player == null || player.IsGameOver || gen.PlayerSegment != segment) { havePrev = false; return; }

        Vector3 fwd = transform.forward;
        float along = Vector3.Dot(player.transform.position - transform.position, fwd);
        float runnerHalf = 0.5f * gen.cubeSize;
        float front = -halfDepth - runnerHalf, back = halfDepth + runnerHalf;

        bool off = player.currentSideIndex != landingSide || player.IsRevolving;
        if (off) lastOffTime = Time.time;

        if (havePrev)
        {
            if (!entered && prevAlong < front && along >= front)
            {
                entered = true;
                // Got back onto the landing face only just in time.
                if (!off && Time.time - lastOffTime < closeCallSeconds) CloseCall();
            }
            if (!passed && prevAlong <= back && along > back)
            {
                passed = true;
                passTime = Time.time;
                if (!hitHere && !broken) flashT = 0f;
            }
        }
        // Left the landing face the instant the frame was behind.
        if (passed && off && Time.time - passTime < closeCallSeconds) CloseCall();

        prevAlong = along;
        havePrev = true;
    }

    void CloseCall()
    {
        if (closeCallPaid || hitHere || broken || player == null) return;
        closeCallPaid = true;
        player.ReportGateCloseCall(player.transform.position);
    }

    void UpdateCoin()
    {
        if (coinValue <= 0 || !coin.gameObject.activeSelf) return;

        if (coinBurstT >= 0f)
        {
            // Pickup: a quick swell and spin, then gone (no halo, no ring).
            coinBurstT += Time.deltaTime / 0.3f;
            float s = 1f + coinBurstT * 0.8f;
            coin.localScale = new Vector3(s, s, s) * Mathf.Clamp01(1f - coinBurstT * coinBurstT);
            coin.localRotation = Quaternion.AngleAxis(Time.time * 900f, Vector3.up);
            if (coinBurstT >= 1f) coin.gameObject.SetActive(false);
            return;
        }

        coin.localRotation = Quaternion.AngleAxis(Time.time * gen.GateCoinSpin, Vector3.up);

        if (player == null || player.IsGameOver || coinTaken || !player.isActiveAndEnabled) return;
        Vector3 runnerPos = player.transform.position;
        Vector3 from = haveRunnerPos && (runnerPos - lastRunnerPos).sqrMagnitude < 9f ? lastRunnerPos : runnerPos;
        lastRunnerPos = runnerPos;
        haveRunnerPos = true;
        if (visual.localScale.x < 0.5f) return;

        Vector3 c = coin.position;
        Vector3 ab = runnerPos - from;
        float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(c - from, ab) / ab.sqrMagnitude) : 0f;
        if ((from + ab * t - c).sqrMagnitude > pickupRadius * pickupRadius) return;

        coinTaken = true;
        coinBurstT = 0f;
        if (gen.Coins != null) gen.Coins.Collect(c, coinValue);
    }

    void UpdateGlow()
    {
        float g = baseGlow * (1f - pulseDepth * 0.5f + pulseDepth * 0.5f * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f));
        if (flashT >= 0f)
        {
            flashT += Time.deltaTime / 0.35f;
            g += passFlash * Mathf.Clamp01(1f - flashT);
            if (flashT >= 1f) flashT = -1f;
        }
        mpb.SetFloat(GlowId, g);
        frameRenderer.SetPropertyBlock(mpb);
    }

    // Copies the leg cube's current material (opaque / fade swap) and property block (tint, glow) onto the bars.
    void MatchHost()
    {
        if (host == null || !host.enabled)
        {
            host = null;
            foreach (Renderer r in hostCube.GetComponentsInChildren<Renderer>())
                if (r.enabled && r.GetComponent<MeshFilter>() != null) { host = r; break; }
            if (host == null) return;
        }
        Material m = host.sharedMaterial;
        host.GetPropertyBlock(mpb);
        foreach (MeshRenderer p in cubeParts)
        {
            if (p.sharedMaterial != m) p.sharedMaterial = m;
            p.SetPropertyBlock(mpb);
        }
    }

    void OnTriggerEnter(Collider other) => Hit(other);
    void OnTriggerStay(Collider other) => Hit(other);

    void Hit(Collider other)
    {
        if (!collidersOn || broken || player == null) return;
        if (playerRb == null || other.attachedRigidbody != playerRb) return;
        if (player.isFlying || player.IsGameOver || !player.IsRunning) return;

        hitHere = true;
        PathObstacleGuard.LastHit = null;   // the shield must not destroy some older pyramid for this
        player.HitObstacle();
        if (!player.IsGameOver)
        {
            // The shield took it (or autopilot is on): break the gate so the same bar cannot hit twice.
            broken = true;
            SetColliders(false);
            breakT = 0f;
        }
    }

    /// <summary>Called by the generator when the gate goes back to the pool.</summary>
    public void Clear()
    {
        segment = null;
        hostCube = null;
        host = null;
        SetColliders(false);
        gameObject.SetActive(false);
    }
}
