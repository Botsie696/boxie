using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// POWER-UP SCHEDULER and the SHIELD pickup.
///
/// One scheduler decides which power-up (if any) is laid on each new leg. Shared rules: at most one per
/// leg, at least PowerUpSchedulerSettings.minLegsBetween legs between any two, never on a boost, bridge,
/// portal runway, decoy, obstacle or autopilot leg, never in the PEAK phase, every placement passes a
/// fairness check, coins near a pickup are removed. Priority when several are due on one leg:
/// shield, then focus, then the diamond (PlacePowerUps calls them in that order, the first that takes the
/// leg wins and the others retry on the next eligible leg).
///
/// Off unless enableShield is ticked (the MainScene prop camera scenes). All tunables are in the
/// ShieldSettings / PowerUpSchedulerSettings assets (Assets/PowerUps/).
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Power-ups")]
    [Tooltip("Shared placement rules for every power-up (Assets/PowerUps/PowerUpSchedulerSettings.asset). Empty = defaults.")]
    public PowerUpSchedulerSettings schedulerSettings;
    [Tooltip("Lay shield pickups. A shield absorbs one fatal mistake.")]
    public bool enableShield = false;
    [Tooltip("Everything about the shield (Assets/PowerUps/ShieldSettings.asset). Empty = defaults.")]
    public ShieldSettings shieldSettings;

    PowerUpSchedulerSettings Sched => schedulerSettings != null ? schedulerSettings : (schedulerSettings = ScriptableObject.CreateInstance<PowerUpSchedulerSettings>());
    ShieldSettings ShieldCfg => shieldSettings != null ? shieldSettings : (shieldSettings = ScriptableObject.CreateInstance<ShieldSettings>());

    class ShieldPickup
    {
        public Segment seg;
        public GameObject cube;
        public Vector3 pos, outward, legDir, faceCentre;
        public int serial;
    }

    private int lastPowerUpSerial = -100;
    private readonly HashSet<int> powerUpLegs = new HashSet<int>();
    private ShieldPickup shieldGem;
    private PickupVisual shieldView;
    private int shieldNextSerial = -1, shieldWaitLegs;
    private bool shieldRolled, shieldWaitHazard;
    private System.Random shieldRng;
    private RunnerShieldFX shieldFx;
    private float shieldClock;
    private Vector3 shieldLastRunner;
    private bool shieldHaveLast;

    public bool ShieldOnTrack => shieldGem != null;
    public ShieldSettings ShieldConfig => ShieldCfg;

    /// <summary>Test hook: the first leg a shield may be laid on (the settings asset is left alone).</summary>
    public void DebugShieldFrom(int leg) { shieldNextSerial = leg; shieldRolled = true; shieldWaitHazard = false; }

    // ------------------------------------------------------------------ scheduler

    void ResetPowerUps()
    {
        ClearShieldPickup();
        powerUpLegs.Clear();
        lastPowerUpSerial = -100;
        shieldRng ??= new System.Random();
        shieldRolled = false; shieldWaitHazard = false; shieldWaitLegs = 0;
        shieldNextSerial = enableShield ? ShieldCfg.firstLeg : int.MaxValue;
        PowerUpTelemetry.Reset();
    }

    /// <summary>The single call AppendSegment makes: shield first, then the diamond. Focus slots in between later.</summary>
    void PlacePowerUps(Segment incoming, Segment next)
    {
        if (incoming == null || next == null) return;
        PlaceShield(incoming, next);
        PlacePowerDiamond(incoming, next);
    }

    /// <summary>The shared rules: may a pickup go on this leg at all?</summary>
    bool PowerUpSlotFree(Segment incoming)
    {
        PowerUpSchedulerSettings s = Sched;
        if (TutorialLegOf(incoming) >= 0) return false;   // TUTORIAL ADDITION: no pickups on the scripted legs (the autopilot would play the lessons)
        if (incoming.serial - lastPowerUpSerial < s.minLegsBetween) return false;
        if (powerUpLegs.Contains(incoming.serial)) return false;
        if (incoming.boost || incoming.portalRole != 0 || incoming.decoyCubes != null) return false;
        if (GetBridgeSpan(incoming.cubes, out _, out _)) return false;
        if (powerGem != null || shieldGem != null) return false;
        if (incoming.serial >= powerQuietFrom && incoming.serial <= powerQuietUntil) return false;   // autopilot legs
        if (s.avoidPeakPhase && Waves && incoming.phase == WavePhase.Peak) return false;
        return true;
    }

    void RegisterPowerUp(Segment incoming, PowerUpType type)
    {
        lastPowerUpSerial = incoming.serial;
        powerUpLegs.Add(incoming.serial);
        PowerUpTelemetry.Spawned(type, incoming.serial);
        if (Sched.logTelemetry) Debug.Log($"[PowerUps] {type} placed on leg {incoming.serial}");
    }

    /// <summary>The fairness rule: a pickup off the turn face leaves at least the time the rolls back to it need.</summary>
    bool PickupFair(Segment seg, int cubeIndex, int rollsToTurnFace)
    {
        if (rollsToTurnFace <= 0) return true;
        PowerUpSchedulerSettings s = Sched;
        float speed = player != null ? Mathf.Max(1f, player.SpeedForLeg(seg.serial)) : 12f;
        float remaining = (seg.cubes.Count - 1 - cubeIndex) * cubeSize / speed;
        return remaining >= rollsToTurnFace * s.secondsPerTap + s.humanSeconds;
    }

    void ClearPowerUpCoins(Vector3 at)
    {
        float r = Sched.coinClearCubes * cubeSize;
        if (r <= 0f) return;
        for (int i = coins.Count - 1; i >= 0; i--)
            if ((CoinPosition(coins[i]) - at).sqrMagnitude < r * r) RemoveCoinAt(i);
    }

    // ------------------------------------------------------------------ shield placement

    void PlaceShield(Segment incoming, Segment next)
    {
        if (!enableShield || shieldGem != null) return;
        if (incoming.serial < shieldNextSerial) return;
        EnsureShieldSystem();
        if (shieldView == null) return;
        if (shieldFx != null && shieldFx.Held) return;                 // never while one is held
        if (!PowerUpSlotFree(incoming)) return;
        if (incoming.cubes.Count < 8) return;
        ShieldSettings S = ShieldCfg;

        // Challenge: sometimes it waits for the leg right after a bridge or decoy leg.
        if (!shieldRolled) { shieldRolled = true; shieldWaitHazard = shieldRng.NextDouble() < S.afterHazardChance; shieldWaitLegs = 0; }
        if (shieldWaitHazard)
        {
            Segment prev = SegmentBySerial(incoming.serial - 1);
            bool after = prev != null && (prev.decoyCubes != null || GetBridgeSpan(prev.cubes, out _, out _));
            if (!after) { if (++shieldWaitLegs > 12) shieldWaitHazard = false; return; }
        }

        List<GameObject> cubes = incoming.cubes;
        int lo = Mathf.Clamp(Mathf.RoundToInt(cubes.Count * Mathf.Min(S.alongMin, S.alongMax)), 2, cubes.Count - 4);
        int hi = Mathf.Clamp(Mathf.RoundToInt(cubes.Count * Mathf.Max(S.alongMin, S.alongMax)), lo, cubes.Count - 4);
        int index = shieldRng.Next(lo, hi + 1);
        Transform t = cubes[index] != null ? cubes[index].transform : null;
        if (t == null) return;

        // The turn face: toward the next heading (as the coins and the diamond see it).
        Vector3 nextW = next.direction.normalized;
        float dR = Vector3.Dot(t.right, nextW), dU = Vector3.Dot(t.up, nextW);
        Vector3 turn = Mathf.Abs(dR) >= Mathf.Abs(dU) ? (dR >= 0f ? t.right : -t.right) : (dU >= 0f ? t.up : -t.up);
        Vector3 outward = turn;
        bool offTurn = shieldRng.NextDouble() < S.offTurnFaceChance;
        if (offTurn)
        {
            Vector3[] faces = { t.right, -t.right, t.up, -t.up };
            int pick = shieldRng.Next(0, 3), seen = 0;
            foreach (Vector3 f in faces)
            {
                if (Vector3.Dot(f, turn) > 0.85f) continue;
                if (seen++ == pick) { outward = f; break; }
            }
            int rolls = Vector3.Dot(outward, turn) < -0.85f ? 2 : 1;
            // Fairness: not enough time left after it -> move it earlier, else put it on the turn face.
            int tries = index;
            while (tries > 2 && !PickupFair(incoming, tries, rolls)) tries--;
            if (!PickupFair(incoming, tries, rolls)) { outward = turn; }
            else if (tries != index) { index = tries; t = cubes[index].transform; }
        }

        shieldGem = new ShieldPickup
        {
            seg = incoming,
            cube = cubes[index],
            outward = outward,
            legDir = incoming.direction,
            serial = incoming.serial,
            faceCentre = t.position + outward * (cubeSize * 0.5f),
            pos = t.position + outward * (cubeSize * (0.5f + S.hover))
        };
        ClearPowerUpCoins(shieldGem.pos);
        RegisterPowerUp(incoming, PowerUpType.Shield);
        shieldNextSerial = incoming.serial + ScaleShieldGap311(shieldRng.Next(Mathf.Min(S.gapMin, S.gapMax), Mathf.Max(S.gapMin, S.gapMax) + 1));
        shieldRolled = false;
        if (logPower) Debug.Log($"[Shield] on leg {incoming.serial}, cube {index}, {(offTurn ? "off-turn" : "turn")} face; next due leg {shieldNextSerial}");
    }

    void ClearShieldPickup()
    {
        shieldGem = null;
        if (shieldView != null) shieldView.Hide();
    }

    void ReleaseShieldOn(Segment seg)
    {
        if (shieldGem != null && shieldGem.seg == seg) ClearShieldPickup();
    }

    void DestroyShieldRuntime()
    {
        if (shieldView != null) shieldView.Destroy();
        shieldView = null;
    }

    void EnsureShieldSystem()
    {
        if (shieldView == null)
        {
            ShieldSettings S = ShieldCfg;
            Color body = S.bodyColor;
            var cols = new[] { body, S.rimColor, Color.white };   // body, rim, emblem
            shieldView = new PickupVisual(transform, "ShieldPickup", ShieldMeshBuilder.Mesh, cols, S.rimColor, body, true);
        }
        if (shieldFx == null && player != null)
        {
            shieldFx = player.GetComponent<RunnerShieldFX>();
            if (shieldFx == null) shieldFx = player.gameObject.AddComponent<RunnerShieldFX>();
            shieldFx.gen = this;
        }
    }

    // ------------------------------------------------------------------ per frame

    void UpdatePowerUps()
    {
        if (!enableShield || shieldGem == null || shieldView == null) return;
        ShieldPickup g = shieldGem;
        if (g.cube == null) { ClearShieldPickup(); return; }
        ShieldSettings S = ShieldCfg;
        shieldClock += Time.deltaTime;

        float shown = CubeShown(g.cube);

        if (shown > 0.2f && player != null && player.isActiveAndEnabled && !player.IsGameOver && shieldFx != null)
        {
            Vector3 now = player.transform.position;
            Vector3 from = shieldHaveLast && (now - shieldLastRunner).sqrMagnitude < 9f ? shieldLastRunner : now;
            float r = Mathf.Max(S.pickupRadius * cubeSize, 0.3f);
            shieldLastRunner = now; shieldHaveLast = true;
            if (SqrDistanceToSegment(g.pos, from, now) <= r * r)
            {
                Vector3 at = g.pos;
                ClearShieldPickup();
                PowerUpTelemetry.Collected(PowerUpType.Shield);
                shieldFx.Collect(at, g.outward, g.faceCentre);
                return;
            }
        }

        if (shown <= 0.01f) { shieldView.Hide(); return; }

        Camera cam = Camera.main;
        var pose = new PickupVisual.Pose
        {
            pos = g.pos, faceCentre = g.faceCentre, outward = g.outward, legDir = g.legDir,
            grow = Mathf.SmoothStep(0f, 1f, shown), time = shieldClock, cubeSize = cubeSize,
            size = S.size, bubbleRadius = S.bubbleRadius, spinSeconds = S.spinSeconds, bob = S.bob, bobPeriod = S.bobPeriod,
            shimmerPeriod = S.shimmerPeriod, pillarHeight = S.pillarHeight,
            cameraRotation = cam != null ? cam.transform.rotation : Quaternion.identity
        };
        shieldView.Draw(pose);
    }
}
