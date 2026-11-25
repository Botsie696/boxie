using System.Collections.Generic;
using UnityEngine;
using PromptKind = TutorialUIController.PromptKind;

/// <summary>
/// Just-in-time hints in the real game, one time each (a PlayerPrefs flag per type): the first boost zig-zag, decoy
/// fork, zone door (portal), bridge plank, red pyramid and power-up pickup the player meets. From hintLead seconds
/// before the decision point - and only if the runner is not already on the right face - time eases to hintTimeScale,
/// the right face gets a glowing strip, the roll arc lights and the matching screen half glows. Normal game rules
/// stay: no dimmer, no input filter, no rewind. Normal time returns on the right move or at the decision point.
///
/// Never two within hintMinGapSeconds; never during autopilot or while the tutorial runs; timeScale and
/// fixedDeltaTime are always restored. Off unless enableFirstEncounterHints is ticked (TutorialManager's boot
/// ticks it in the tutorial scene and in any scene with "Tutorial In This Scene").
/// </summary>
public class FirstEncounterHints : MonoBehaviour
{
    [Tooltip("Show the one-time hints the first time each new thing comes up.")]
    public bool enableFirstEncounterHints = false;

    public enum HintType { BoostZigZag, DecoyFork, ZoneDoor, BridgePlank, RedPyramid, PowerUp, Shield }
    const string PrefPrefix = "FirstHint_";

    CubeRailRunner runner;
    DynamicPathGenerator gen;
    TutorialStyle st;
    TutorialUIController ui;
    TutorialRollArc arc;
    TutorialWorldFX fx;

    bool active, restoring, changedTime;
    HintType activeType;
    int targetFace, side;
    PromptKind kind;
    Vector3 decisionPoint, legDir;
    DynamicPathGenerator.Segment activeLeg;
    float lastHintEnd = -100f;
    float baseFixedDelta = -1f;
    float scaleNow = 1f;

    // Per-leg scan cache (planks, pyramids): rescanned only when the runner reaches a new leg.
    DynamicPathGenerator.Segment scannedLeg;
    readonly List<(Vector3 pos, int face, HintType type)> legThings = new List<(Vector3, int, HintType)>();

    // Read once at start (no string building per frame); written through as each hint is shown.
    readonly bool[] seen = new bool[System.Enum.GetValues(typeof(HintType)).Length];
    bool Seen(HintType t) => seen[(int)t];
    void MarkSeen(HintType t)
    {
        seen[(int)t] = true;
        PlayerPrefs.SetInt(PrefPrefix + t, 1);
        PlayerPrefs.Save();
    }

    void Start()
    {
        runner = FindFirstObjectByType<CubeRailRunner>();
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        st = TutorialStyle.Active;
        for (int i = 0; i < seen.Length; i++) seen[i] = PlayerPrefs.GetInt(PrefPrefix + (HintType)i, 0) == 1;
    }

    void EnsureVisuals()
    {
        if (ui != null) return;
        ui = TutorialUIController.Build();
        ui.gameObject.name = "FirstEncounterHintCanvas";
        arc = gameObject.GetComponent<TutorialRollArc>();
        if (arc == null) { arc = gameObject.AddComponent<TutorialRollArc>(); arc.Init(runner); }
        fx = gameObject.GetComponent<TutorialWorldFX>();
        if (fx == null) { fx = gameObject.AddComponent<TutorialWorldFX>(); fx.Init(); }
    }

    void OnDisable() { RestoreTime(); }
    void OnDestroy() { RestoreTime(); }

    void RestoreTime()
    {
        if (!changedTime) return;
        Time.timeScale = 1f;
        if (baseFixedDelta > 0f) Time.fixedDeltaTime = baseFixedDelta;
        changedTime = false;
        scaleNow = 1f;
    }

    void Update()
    {
        if (runner == null || gen == null) return;
        if (!enableFirstEncounterHints || TutorialManager.Running) { if (active) End(false); TickRestore(); return; }

        if (active) { TickActive(); return; }
        TickRestore();
        if (restoring) return;
        if (!runner.IsRunning || runner.PowerAuto || runner.IsGameOver) return;
        if (Time.unscaledTime - lastHintEnd < st.hintMinGapSeconds) return;
        Detect();
    }

    // ------------------------------------------------------------------ what is coming up

    void Detect()
    {
        DynamicPathGenerator.Segment seg = gen.PlayerSegment;
        if (seg == null || runner.currentCube == null) return;
        int cur = runner.currentSideIndex;

        // Zone door: the runner must be on the door's face at the end of the runway.
        if (!Seen(HintType.ZoneDoor) && gen.PortalAhead && seg == gen.PortalEntrySegment)
        {
            if (TryStart(HintType.ZoneDoor, gen.PortalEntrySide, gen.PortalEntryEnd, seg)) return;
        }

        // Corner-type: the turn at the end of this leg leads into a boost, or has a decoy branch.
        DynamicPathGenerator.Segment next = gen.SegmentAfter(seg);
        if (next != null && next.cubes.Count > 0)
        {
            Vector3 corner = LastCubePos(seg);
            int face = runner.SideFacing(next.direction);
            if (!Seen(HintType.BoostZigZag) && next.boost && next.boostLeg == 0 && !seg.boost)
                if (TryStart(HintType.BoostZigZag, face, corner, seg)) return;
            if (!Seen(HintType.DecoyFork) && next.decoyCubes != null)
                if (TryStart(HintType.DecoyFork, face, corner, seg)) return;
        }

        // Planks and pyramids on this leg.
        if (seg != scannedLeg) ScanLeg(seg);
        foreach (var thing in legThings)
        {
            if (Seen(thing.type)) continue;
            if (Vector3.Dot(thing.pos - runner.transform.position, seg.direction) <= 0f) continue;
            if (TryStart(thing.type, thing.face, thing.pos, seg)) return;
        }

        // Power-up pickups: be on their face.
        if (gen.HintPowerUpTarget(out Vector3 pPos, out Vector3 pOut, out DynamicPathGenerator.Segment pSeg, out bool shield) && pSeg == seg)
        {
            HintType t = shield ? HintType.Shield : HintType.PowerUp;
            if (!Seen(t) && Vector3.Dot(pPos - runner.transform.position, seg.direction) > 0f)
                TryStart(t, runner.SideFacing(pOut), pPos, seg);
        }
    }

    void ScanLeg(DynamicPathGenerator.Segment seg)
    {
        scannedLeg = seg;
        legThings.Clear();
        foreach (GameObject c in seg.cubes)
        {
            if (c == null) continue;
            var plank = c.GetComponent<BridgePlankGuard>();
            if (plank != null) { legThings.Add((c.transform.position, plank.allowedSide, HintType.BridgePlank)); break; }
        }
        if (Seen(HintType.RedPyramid)) return;
        // Pyramids: any obstacle standing on this leg's line (a scan once per leg, not per frame).
        foreach (PathObstacleGuard g in FindObjectsByType<PathObstacleGuard>(FindObjectsSortMode.None))
        {
            Vector3 d = g.transform.position - seg.startPosition;
            Vector3 off = d - seg.direction * Vector3.Dot(d, seg.direction);
            if (off.magnitude > gen.cubeSize * 1.5f) continue;
            float along = Vector3.Dot(d, seg.direction);
            if (along < -gen.cubeSize || along > (seg.cubes.Count + 1) * gen.cubeSize) continue;
            legThings.Add((seg.startPosition + seg.direction * along, -1 - g.blockedSide, HintType.RedPyramid));   // face < 0: "anything but"
        }
    }

    // Starts a hint if it is needed (the runner is not on the right face) and the decision point is close enough.
    bool TryStart(HintType type, int face, Vector3 point, DynamicPathGenerator.Segment seg)
    {
        float speed = runner.forwardSpeed;
        if (speed <= 0.01f) return false;
        float secs = Vector3.Dot(point - runner.transform.position, seg.direction.normalized) / speed;
        if (secs <= 0f || secs > st.hintLead) return false;

        int cur = runner.currentSideIndex;
        if (face < 0)
        {
            // A pyramid: off the blocked face. On it already -> roll to whichever neighbour is in view.
            int blocked = -1 - face;
            if (cur != blocked) return false;
            face = VisibleNeighbour(cur);
        }
        if (face < 0 || face == cur) return false;

        EnsureVisuals();
        int taps = (cur + 2) % 4 == face ? 2 : 1;
        bool flip = runner.Profile != null && runner.Profile.fairInput ? runner.Profile.doubleTapFlip : !runner.screenAxisControls;
        kind = taps == 1 ? PromptKind.Tap : (flip ? PromptKind.DoubleTap : PromptKind.TwoTaps);
        side = taps == 1 ? runner.TapDirectionToReach(face) : VisibleSide();
        if (side == 0) side = 1;

        active = true;
        activeType = type;
        targetFace = face;
        decisionPoint = point;
        legDir = seg.direction.normalized;
        activeLeg = seg;
        MarkSeen(type);

        if (baseFixedDelta <= 0f) baseFixedDelta = Time.fixedDeltaTime;
        ui.ShowPrompt(side, kind, false, true, false);   // the matching half glows; no dimmer, no thumb
        TutorialRollArc.Kind ak = kind == PromptKind.Tap ? TutorialRollArc.Kind.Roll90 : kind == PromptKind.DoubleTap ? TutorialRollArc.Kind.Flip180 : TutorialRollArc.Kind.TwoRolls;
        arc.Show(side, ak, cur, MidFace(side));
        Debug.Log("[FirstEncounterHints] " + type + ": face " + cur + " -> " + face + ", " + kind + " on the " + (side < 0 ? "left" : "right"));
        return true;
    }

    void TickActive()
    {
        float dt = Time.unscaledDeltaTime;
        // Ease into the slower time.
        scaleNow = Mathf.MoveTowards(scaleNow, st.hintTimeScale, (1f - st.hintTimeScale) / Mathf.Max(0.02f, st.hintEaseInSeconds) * dt);
        ApplyTime();

        bool right = runner.currentSideIndex == targetFace;
        float secs = Vector3.Dot(decisionPoint - runner.transform.position, legDir) / Mathf.Max(0.01f, runner.forwardSpeed);
        bool over = runner.IsGameOver || runner.PowerAuto || gen.PlayerSegment != activeLeg || secs <= 0f;
        if (right || over) { End(right); return; }

        // The right face: a glowing strip from the runner to the decision point.
        Transform cube = runner.currentCube;
        if (cube != null && fx != null)
        {
            float cs = cube.lossyScale.x;
            Vector3 n = runner.SideNormal(targetFace);
            Vector3 along = cube.position + legDir * Vector3.Dot(runner.transform.position - cube.position, legDir);
            float len = Mathf.Max(0f, Vector3.Dot(decisionPoint - along, legDir));
            fx.Strip(along + n * (cs * 0.52f), n, legDir, cs * st.chevrons.laneWidth, len, 1.6f * TutorialUIController.Pulse(kind), st.Guidance);
        }
    }

    void End(bool correct)
    {
        active = false;
        restoring = true;
        lastHintEnd = Time.unscaledTime;
        if (ui != null) ui.Hide();
        if (arc != null) { if (correct) arc.Sweep(side); arc.HideAll(); }
        if (correct && fx != null) fx.Sparks(runner.transform.position, runner.currentCube != null ? runner.currentCube.lossyScale.x : 1f);
        TutorialTelemetry.HintOutcome(activeType.ToString(), correct);
    }

    // Back to normal time over hintEaseOutSeconds (unscaled).
    void TickRestore()
    {
        if (!restoring) return;
        scaleNow = Mathf.MoveTowards(scaleNow, 1f, (1f - st.hintTimeScale) / Mathf.Max(0.02f, st.hintEaseOutSeconds) * Time.unscaledDeltaTime);
        ApplyTime();
        if (scaleNow >= 1f) { restoring = false; RestoreTime(); }
    }

    void ApplyTime()
    {
        if (baseFixedDelta <= 0f) baseFixedDelta = Time.fixedDeltaTime;
        Time.timeScale = scaleNow;
        Time.fixedDeltaTime = baseFixedDelta * scaleNow;
        changedTime = true;
    }

    // ------------------------------------------------------------------ helpers

    int MidFace(int s)
    {
        int c = runner.currentSideIndex;
        for (int k = 1; k <= 3; k += 2)
        {
            int n = (c + k) % 4;
            if (runner.TapDirectionToReach(n) == s) return n;
        }
        return -1;
    }

    int VisibleNeighbour(int cur)
    {
        Camera cam = Camera.main;
        Vector3 toCam = cam != null ? -cam.transform.forward : Vector3.up;
        int a = (cur + 1) % 4, b = (cur + 3) % 4;
        return Vector3.Dot(runner.SideNormal(a), toCam) >= Vector3.Dot(runner.SideNormal(b), toCam) ? a : b;
    }

    int VisibleSide()
    {
        Camera cam = Camera.main;
        Vector3 toCam = cam != null ? -cam.transform.forward : Vector3.up;
        int ml = MidFace(-1), mr = MidFace(1);
        float vl = ml >= 0 ? Vector3.Dot(runner.SideNormal(ml), toCam) : -1f;
        float vr = mr >= 0 ? Vector3.Dot(runner.SideNormal(mr), toCam) : -1f;
        return vl >= vr ? -1 : 1;
    }

    static Vector3 LastCubePos(DynamicPathGenerator.Segment seg)
    {
        GameObject last = seg.cubes.Count > 0 ? seg.cubes[seg.cubes.Count - 1] : null;
        return last != null ? last.transform.position : seg.endPosition;
    }
}
