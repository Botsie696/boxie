using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// THE POWER DIAMOND. A glowing diamond sits on one face of a cube close to the end of a leg: either
/// on the face the runner has to be on to take the next turn, or on the opposite face (a detour that
/// has to be paid for with a swipe back). Collecting it starts an autopilot (RunnerPowerFX): the runner
/// speeds up, steers itself, cannot be hurt and sucks in every coin near it, then slows down and hands
/// control back on a fresh, plain leg.
///
/// This partial places the diamond, draws it (an outline quad + halo + a thin glowing line on its face
/// leading toward it), notices the pickup, keeps the track plain around it, and flies the magnet coins.
/// Everything is data until the diamond's cube is drawn, nothing has a collider (the runner's forward
/// probe raycasts the Default layer), and there is only ever one diamond, so there is no pooling.
///
/// Off unless powerDiamonds is ticked, which only the MainScene prop camera scenes do.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Power Diamond")]
    [Tooltip("Lay a power diamond now and then; collecting it starts an autopilot. Off = nothing here runs.")]
    public bool powerDiamonds = false;
    [Tooltip("DEBUG, first diamond only: the leg (0 = the first) whose end holds the first diamond. -1 = roll it like every other one. Leg 4 in the 1-based count is 3.")]
    public int powerDebugFirstLeg = -1;
    [Tooltip("Fewest legs between the end of one autopilot hand-over and the next diamond.")]
    [Range(4, 80)] public int powerMinGapLegs = 24;
    [Tooltip("Most legs between two diamonds. The gap is rolled between the two.")]
    [Range(4, 80)] public int powerMaxGapLegs = 34;
    [Tooltip("Chance a diamond is laid on an eligible leg once the gap has passed.")]
    [Range(0f, 1f)] public float powerChance = 0.7f;
    [Tooltip("Chance it sits on the face OPPOSITE the one the next turn needs (a detour) rather than on the turn face.")]
    [Range(0f, 1f)] public float powerWrongFaceChance = 0.5f;
    [Tooltip("Width of the diamond, in cubes.")]
    [Range(0.5f, 4f)] public float powerSize = 1.5f;
    [Tooltip("Colour of the diamond, the aura, the trail and the HUD while the power lasts. HDR.")]
    [ColorUsage(true, true)] public Color powerColor = new Color(0.55f, 0.95f, 1f, 1f);

    [Header("Power Diamond - Autopilot")]
    [Tooltip("Legs the autopilot drives after the leg the diamond is on (the rest of that leg is driven too).")]
    [Range(1, 12)] public int powerAutoLegs = 8;
    [Tooltip("The handover leg starts at least this many seconds ahead of the pickup, at autopilot speed, measured along the route, and is the first safe leg after that.")]
    [Range(1f, 8f)] public float powerMinAutoSeconds = 3f;
    [Tooltip("Speed during the autopilot, as a multiple of the normal speed.")]
    [Range(1f, 2f)] public float powerSpeedMultiplier = 1.45f;
    [Tooltip("Seconds to ramp up to that speed.")]
    [Range(0.05f, 2f)] public float powerRampSeconds = 0.4f;
    [Tooltip("Seconds before the end of the autopilot over which the speed eases back to normal (done as the hand-over leg starts).")]
    [Range(0.3f, 2.5f)] public float powerSlowSeconds = 0.9f;
    [Tooltip("Seconds before the end the countdown (3-2-1 pips, flicker) runs.")]
    [Range(0.6f, 2.5f)] public float powerCountdownSeconds = 1.2f;
    [Tooltip("Camera pull-back during the autopilot, one smooth glide (reuses the boost zoom).")]
    [Range(1f, 1.5f)] public float powerCameraZoom = 1.10f;
    [Tooltip("Coins within this many cubes of the runner fly into it, whatever face they are on.")]
    [Range(1f, 12f)] public float powerMagnetRadius = 4.5f;
    [Tooltip("The hand-over leg is at least this many cubes long (a generous one).")]
    [Range(6, 40)] public int powerHandoverMinCubes = 20;
    [Tooltip("Legs after the hand-over leg with no boost, bridge, portal, decoy or obstacle.")]
    [Range(0, 8)] public int powerQuietLegsAfter = 3;
    [Tooltip("Log placement and collection.")]
    public bool logPower = false;

    class PowerGem
    {
        public Segment seg;
        public GameObject cube;
        public Vector3 pos, outward, legDir, guideFrom;
        public int serial;
    }

    private PowerGem powerGem;
    private int powerNextSerial = -1;
    private int powerQuietUntil = -1, powerQuietFrom = int.MaxValue, powerHandoverSerial = -1;
    private System.Random powerRng;
    private RunnerPowerFX powerFx;
    private Transform powerRoot;
    private Mesh powerQuad;
    private Material powerCoreMat, powerGlowMat, powerGuideMat;
    private MeshRenderer powerCore, powerGlow, powerGuide;
    private float powerClock;

    /// <summary>RunnerPowerFX picked this leg as the handover: it and the legs after it stay plain (low hazard budget).</summary>
    public void PowerMarkHandover(int serial)
    {
        powerHandoverSerial = serial;
        powerQuietUntil = Mathf.Max(powerQuietUntil, serial + powerQuietLegsAfter);
    }

    /// <summary>1 normally; the camera reads this and pulls back to it (see CameraParentRotator).</summary>
    [System.NonSerialized] public float PowerZoom = 1f;

    /// <summary>True while the leg about to be laid must stay plain (no boost / bridge / portal / decoy / obstacle).</summary>
    bool PowerQuiet => powerDiamonds && segmentSerial >= powerQuietFrom && segmentSerial <= powerQuietUntil;
    bool PowerHandoverLeg(int serial) => powerDiamonds && serial == powerHandoverSerial;
    bool PowerBlocksObstacle(Segment s) => s != null && ((powerDiamonds && s.serial >= powerQuietFrom && s.serial <= powerQuietUntil) || powerUpLegs.Contains(s.serial));

    /// <summary>How drawn a cube is (0..1), so nothing is lit before its cube has grown in.</summary>
    public float CubeShown(GameObject c)
    {
        if (c == null) return 0f;
        if (shadingRef == null) shadingRef = GetComponent<PathDepthShading>();
        return shadingRef != null && shadingRef.isActiveAndEnabled ? shadingRef.ShownAmount(c) : (c.activeInHierarchy ? 1f : 0f);
    }

    public int PlayerSerial { get { Segment s = PlayerSegment; return s != null ? s.serial : -1; } }
    public bool PowerGemPresent => powerGem != null;

    /// <summary>The leg with this number, or null once it has been culled (or is not laid yet).</summary>
    public Segment SegmentBySerial(int serial)
    {
        for (int i = allSegments.Count - 1; i >= 0; i--)
            if (allSegments[i].serial == serial) return allSegments[i];
        return null;
    }

    /// <summary>Fills buf with the cubes the runner has yet to cross, nearest first, across legs.</summary>
    public void CubesAhead(List<GameObject> buf, int max)
    {
        buf.Clear();
        int idx = currentPlayerSegmentIdx;
        if (idx < 0 || idx >= allSegments.Count || player == null || player.currentCube == null) return;
        int at = allSegments[idx].cubes.IndexOf(player.currentCube.gameObject);
        for (int s = idx; s < allSegments.Count && buf.Count < max; s++)
        {
            List<GameObject> cubes = allSegments[s].cubes;
            for (int c = s == idx ? Mathf.Max(0, at) : 0; c < cubes.Count && buf.Count < max; c++)
                if (cubes[c] != null) buf.Add(cubes[c]);
        }
    }

    // ------------------------------------------------------------------
    // Placement
    // ------------------------------------------------------------------

    void ResetPower()
    {
        ClearPowerGem();
        powerRng ??= new System.Random();
        powerGuaranteeCycleDone = -1;
        powerQuietUntil = -1;
        powerQuietFrom = int.MaxValue;
        powerHandoverSerial = -1;
        PowerZoom = 1f;
        powerNextSerial = powerDebugFirstLeg >= 0 ? powerDebugFirstLeg : RollPowerGap();
    }

    int RollPowerGap()
    {
        int lo = Mathf.Min(powerMinGapLegs, powerMaxGapLegs), hi = Mathf.Max(powerMinGapLegs, powerMaxGapLegs);
        return ScalePowerGap311(powerRng.Next(lo, hi + 1));   // 3.11: per level / tier (unchanged elsewhere)
    }

    /// <summary>A leg safe to hand control back on: no boost, portal, decoy, bridge or obstacle.</summary>
    public bool PowerLegSafe(Segment s)
    {
        if (PowerLegHazard(s)) return false;
        foreach (GameObject c in s.cubes) if (c != null && c.GetComponentInChildren<PathObstacleGuard>(true) != null) return false;
        return true;
    }

    static bool PowerLegHazard(Segment s) =>
        s != null && (s.boost || s.portalRole != 0 || s.decoyCubes != null || GetBridgeSpan(s.cubes, out _, out _));

    /// <summary>incoming = the leg that ends at the corner, next = the leg just laid after it.</summary>
    void PlacePowerDiamond(Segment incoming, Segment next)
    {
        if (!powerDiamonds || incoming == null || next == null || powerGem != null) return;
        if (!PowerSystemReady()) return;
        EnsurePowerFx();
        if (powerFx != null && powerFx.Active) return;
        // A profile may guarantee one diamond per level (scene 3.10 "reward" level): due from that leg on, schedule and dice ignored.
        int guaranteePos = difficultyProfile != null ? difficultyProfile.PowerUpGuaranteePos(incoming.cycle) : -1;
        bool guaranteed = guaranteePos >= 0 && powerGuaranteeCycleDone != incoming.cycle && incoming.cyclePos >= guaranteePos;
        if (!guaranteed && incoming.serial < powerNextSerial) return;
        if (!PowerUpSlotFree(incoming)) return;   // shared scheduler rules (spacing, hazards, autopilot legs)
        if (incoming.cubes.Count < 9) return;

        // Build phase only, and never within 3 legs of a boost, bridge or portal (or a decoy).
        for (int back = 0; back <= 3; back++)
        {
            Segment s = SegmentBySerial(incoming.serial - back);
            if (PowerLegHazard(s)) return;
        }
        if (PowerLegHazard(next)) return;

        bool forced = powerDebugFirstLeg >= 0 && incoming.serial == powerDebugFirstLeg && !powerDebugDone;
        if (!forced && !guaranteed && powerRng.NextDouble() >= powerChance) return;

        List<GameObject> cubes = incoming.cubes;
        int index = cubes.Count - 1 - 3;       // a few cubes before the corner cube
        GameObject cube = cubes[index];
        if (cube == null) return;
        Transform t = cube.transform;

        // The face the next turn needs (toward the next heading), or the one opposite.
        Vector3 nextW = next.direction.normalized;
        float dR = Vector3.Dot(t.right, nextW), dU = Vector3.Dot(t.up, nextW);
        Vector3 outward = Mathf.Abs(dR) >= Mathf.Abs(dU) ? (dR >= 0f ? t.right : -t.right) : (dU >= 0f ? t.up : -t.up);
        if (powerRng.NextDouble() < powerWrongFaceChance && !forced) outward = -outward;

        powerGem = new PowerGem
        {
            seg = incoming,
            cube = cube,
            outward = outward,
            legDir = incoming.direction,
            serial = incoming.serial,
            pos = t.position + outward * (cubeSize * 1.15f),
            guideFrom = t.position + outward * (cubeSize * 0.51f) - incoming.direction * (cubeSize * 6f)
        };
        if (forced) powerDebugDone = true;
        if (guaranteed) powerGuaranteeCycleDone = incoming.cycle;
        RegisterPowerUp(incoming, PowerUpType.Autopilot);

        powerQuietFrom = incoming.serial;
        powerHandoverSerial = incoming.serial + powerAutoLegs + 1;
        powerQuietUntil = powerHandoverSerial + powerQuietLegsAfter;
        powerNextSerial = powerHandoverSerial + RollPowerGap();
        if (logPower) Debug.Log($"[Power] diamond on leg {incoming.serial}, cube {index}, face {(Vector3.Dot(outward, nextW) > 0f ? "turn" : "opposite")}, handover leg {powerHandoverSerial}");
    }

    private bool powerDebugDone;
    private int powerGuaranteeCycleDone = -1;   // cycle whose guaranteed diamond (DifficultyProfile.PowerUpGuaranteePos) was placed

    void ClearPowerGem()
    {
        powerGem = null;
        if (powerCore != null) powerCore.enabled = false;
        if (powerGlow != null) powerGlow.enabled = false;
        if (powerGuide != null) powerGuide.enabled = false;
        HidePowerLook();
    }

    void ReleasePowerOn(Segment seg)
    {
        if (powerGem != null && powerGem.seg == seg) ClearPowerGem();
    }

    void DestroyPowerRuntime()
    {
        if (powerCoreMat != null) Destroy(powerCoreMat);
        if (powerGlowMat != null) Destroy(powerGlowMat);
        if (powerGuideMat != null) Destroy(powerGuideMat);
        if (powerQuad != null) Destroy(powerQuad);
        powerCoreMat = powerGlowMat = powerGuideMat = null;
        powerQuad = null;
        DestroyPowerLook();
        foreach (CoinFlight f in coinFlights) if (f.view != null) f.view = null;
        coinFlights.Clear();
    }

    void EnsurePowerFx()
    {
        if (powerFx != null || player == null) return;
        powerFx = player.GetComponent<RunnerPowerFX>();
        if (powerFx == null) powerFx = player.gameObject.AddComponent<RunnerPowerFX>();
        powerFx.gen = this;
    }

    bool PowerSystemReady()
    {
        if (powerQuad != null) return true;
        Shader core = Shader.Find("Custom/PathGem"), glow = Shader.Find("Custom/SoftGlow");
        if (core == null || glow == null) return false;

        powerQuad = BuildQuad();
        powerCoreMat = new Material(core) { name = "PowerDiamond" };
        powerCoreMat.SetFloat(GemThicknessId, 0.11f);
        powerCoreMat.SetFloat(GemGlowId, 0.55f);
        powerCoreMat.SetFloat(GemFillId, 0.25f);
        powerGlowMat = new Material(glow) { name = "PowerDiamondGlow" };
        powerGuideMat = new Material(glow) { name = "PowerDiamondGuide" };
        powerGuideMat.SetFloat("_Falloff", 1.6f);
        powerGuideMat.SetFloat("_Core", 0.35f);

        powerRoot = new GameObject("PowerDiamond").transform;
        powerRoot.SetParent(transform, false);
        powerCore = NewPowerRenderer("Core", powerCoreMat);
        powerGlow = NewPowerRenderer("Glow", powerGlowMat);
        powerGuide = NewPowerRenderer("Guide", powerGuideMat);
        return true;
    }

    MeshRenderer NewPowerRenderer(string name, Material m)
    {
        var go = new GameObject(name);
        go.transform.SetParent(powerRoot, false);
        go.AddComponent<MeshFilter>().sharedMesh = powerQuad;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.enabled = false;
        return r;
    }

    // ------------------------------------------------------------------
    // Per frame
    // ------------------------------------------------------------------

    void UpdatePower()
    {
        if (!powerDiamonds) return;
        float dt = Time.deltaTime;
        powerClock += dt;

        if (powerGem != null) UpdatePowerGem();
        if (coinFlights.Count > 0) UpdateCoinFlights(dt);
        if (powerFx != null && powerFx.Active && powerFx.MagnetOn) PowerMagnet(player.transform.position, powerMagnetRadius * cubeSize);
    }

    void UpdatePowerGem()
    {
        PowerGem g = powerGem;
        if (g.cube == null) { ClearPowerGem(); return; }

        if (shadingRef == null) shadingRef = GetComponent<PathDepthShading>();
        bool shaded = shadingRef != null && shadingRef.isActiveAndEnabled;
        float shown = shaded ? shadingRef.ShownAmount(g.cube) : (g.cube.activeInHierarchy ? 1f : 0f);

        // Pickup: swept from last frame so a fast runner cannot skip it. Only when its cube is drawn.
        if (shown > 0.2f && player != null && player.isActiveAndEnabled && !player.IsGameOver && powerFx != null && !powerFx.Active)
        {
            Vector3 now = player.transform.position;
            Vector3 from = powerHaveLast && (now - powerLastRunner).sqrMagnitude < 9f ? powerLastRunner : now;
            float r = Mathf.Max(coinPickupRadius * 1.6f, cubeSize * 0.75f);
            if (SqrDistanceToSegment(g.pos, from, now) <= r * r)
            {
                Vector3 at = g.pos;
                if (logPower) Debug.Log($"[Power] collected on leg {g.serial}");
                ClearPowerGem();
                powerFx.Begin(at, g.serial);
                powerLastRunner = now; powerHaveLast = true;
                return;
            }
            powerLastRunner = now; powerHaveLast = true;
        }

        if (shown <= 0.01f)
        {
            powerCore.enabled = powerGlow.enabled = powerGuide.enabled = false;
            HidePowerLook();
            return;
        }

        float grow = Mathf.SmoothStep(0f, 1f, shown);
        Camera cam = Camera.main;
        Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
        float parentScale = powerRoot != null ? Mathf.Max(0.0001f, powerRoot.lossyScale.x) : 1f;
        float width = cubeSize * powerSize / parentScale;
        float bob = Mathf.Sin(powerClock * 3f) * 0.08f * cubeSize;
        Vector3 pos = g.pos + g.outward * bob;
        float beat = 0.88f + 0.12f * Mathf.Sin(powerClock * 5f);   // a power-up may pulse; the corner gems may not

        powerCoreMat.SetColor(GemColorId, powerColor);
        Color halo = powerColor; halo.a = 0.9f * beat;
        powerGlowMat.SetColor(GemColorId, halo);
        Color line = powerColor; line.a = 0.8f;
        powerGuideMat.SetColor(GemColorId, line * (0.8f + 0.2f * Mathf.Sin(powerClock * 6f)));

        powerCore.enabled = powerGlow.enabled = powerGuide.enabled = true;
        Transform t = powerCore.transform;
        t.SetPositionAndRotation(pos, face * Quaternion.AngleAxis(0f, Vector3.forward));
        t.localScale = new Vector3(width * grow, width * 1.15f * grow, width);

        Transform q = powerGlow.transform;
        q.SetPositionAndRotation(pos, face);
        float hw = width * 2.4f * grow * beat;
        q.localScale = new Vector3(hw, hw, hw);

        // Solid look: the gem stands on its face (DynamicPathGeneratorPowerLook.cs) in place of the outline,
        // with no round halo or soft guide streak (the square frame on the face marks it instead).
        if (powerSolidLook && PowerLookReady())
        {
            powerCore.enabled = powerGlow.enabled = powerGuide.enabled = false;
            DrawPowerLook(g, grow, bob, beat, parentScale);
        }
        else HidePowerLook();

        // The line lies on the face, from a few cubes back up to the diamond, so the face it is on reads.
        Vector3 a = g.guideFrom, b = g.cube.transform.position + g.outward * (cubeSize * 0.51f);
        Transform gl = powerGuide.transform;
        gl.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(-g.outward, g.legDir));
        gl.localScale = new Vector3(cubeSize * 0.45f * grow / parentScale, (b - a).magnitude * grow / parentScale, 1f);
    }

    private Vector3 powerLastRunner;
    private bool powerHaveLast;

    // ------------------------------------------------------------------
    // Coin magnet: coins leave their cube, curve into the runner, then play the usual pickup there.
    // ------------------------------------------------------------------

    class CoinFlight
    {
        public MeshRenderer view;
        public Vector3 from, side;
        public Quaternion rotation;
        public float scale, t, duration, delay;
    }

    private readonly List<CoinFlight> coinFlights = new List<CoinFlight>();
    private readonly Stack<CoinFlight> spareFlights = new Stack<CoinFlight>();

    void PowerMagnet(Vector3 centre, float radius)
    {
        if (coins.Count == 0 || coinMesh == null) return;
        float r2 = radius * radius;
        float parentScale = coinRoot != null ? Mathf.Max(0.0001f, coinRoot.lossyScale.x) : 1f;
        for (int i = coins.Count - 1; i >= 0; i--)
        {
            PathCoin c = coins[i];
            if (c.cube == null) continue;
            Vector3 pos = CoinPosition(c);
            if ((pos - centre).sqrMagnitude > r2) continue;

            MeshRenderer v = c.view;
            c.view = null;
            if (v == null) v = TakeCoinView();
            float scale = c.viewScale > 0f ? c.viewScale : coinSize * c.sizeScale / coinMeshWidth / parentScale;
            Quaternion rot = c.stand * coinMeshTurn;
            RemoveCoinAt(i);   // hides the contact shadow; the view is already ours

            CoinFlight f = spareFlights.Count > 0 ? spareFlights.Pop() : new CoinFlight();
            f.view = v; f.from = pos; f.rotation = rot; f.scale = scale; f.t = 0f;
            f.delay = Random.value * 0.12f;
            f.duration = 0.26f + Random.value * 0.08f;
            f.side = Random.onUnitSphere;
            v.enabled = true;
            coinFlights.Add(f);
        }
    }

    void UpdateCoinFlights(float dt)
    {
        Vector3 target = player != null ? player.transform.position : Vector3.zero;
        for (int i = coinFlights.Count - 1; i >= 0; i--)
        {
            CoinFlight f = coinFlights[i];
            if (f.delay > 0f) { f.delay -= dt; continue; }
            f.t += dt / f.duration;
            float k = Mathf.Clamp01(f.t);
            float e = k * k * (3f - 2f * k);

            // A curved path: a quadratic Bezier through a control point lifted and thrown sideways.
            Vector3 ctrl = (f.from + target) * 0.5f + Vector3.up * (cubeSize * 1.4f) + Vector3.Cross(f.side, Vector3.up).normalized * (cubeSize * 0.9f);
            Vector3 p = (1f - e) * (1f - e) * f.from + 2f * (1f - e) * e * ctrl + e * e * target;
            Transform t = f.view.transform;
            t.SetPositionAndRotation(p, Quaternion.AngleAxis(720f * k, Vector3.up) * f.rotation);
            float s = f.scale * Mathf.Lerp(1f, 0.7f, e);
            t.localScale = new Vector3(s, s, s);

            if (f.t < 1f) continue;

            // Arrived: the regular pickup animation plays on the runner, and the coin is counted.
            CoinBurst b = spareBursts.Count > 0 ? spareBursts.Pop() : new CoinBurst();
            b.view = f.view;
            b.from = target;
            b.up = Vector3.up;
            b.rotation = f.view.transform.rotation;
            b.scale = f.scale * 0.7f;
            b.t = 0f;
            coinBursts.Add(b);
            if (coinCollector != null) coinCollector.Collect(target);

            f.view = null;
            spareFlights.Push(f);
            int last = coinFlights.Count - 1;
            coinFlights[i] = coinFlights[last];
            coinFlights.RemoveAt(last);
        }
    }
}
