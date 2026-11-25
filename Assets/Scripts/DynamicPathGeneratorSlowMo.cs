using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SLOW-MOTION SECTIONS (MainScene prop camera 3.11 + Test Obstacles - off unless slowMoSections), the third special
/// mode next to boosts and obstacle legs. A zig-zag of TINY legs (5-7 cubes, half turns like a boost) taken while
/// everything runs slow: the time scale drops (TimeScaleMixer.Source.SlowMo, so hit-stop / shield / pause stack) AND
/// the runner itself slows (CubeRailRunner.SlowMoSpeedFactor). Then one REGULAR-length exit leg, along which both ease
/// back to normal, so the player is up to speed again before the next ordinary turn. SlowMoDirector drives both.
///
/// Why the runner must slow too: the mechanics of a turn (landing lock, two rolls, cooldown) run in GAME time, so a
/// lower time scale stretches them exactly as much as the leg - at full speed a 6-cube leg is shorter than the two
/// rolls alone. A tiny leg must last DifficultyProfile.SlowMoLegSeconds(scale) game seconds = the mechanics + the
/// human's reaction budget shrunk by the time scale (it is real time); the runner is slowed until the SHORTEST tiny leg
/// does. A harder level = a higher time scale (less real time per leg), never a leg under what the mechanics allow.
///
/// Rules (all numbers in the DifficultyProfile, "Slow motion"): from level slowMoFromLevel (1 = Start) one section per
/// level, slowMoLegs tiny legs at the first level and +slowMoLegsPerLevel on every level after (capped), at a random
/// place at least 10 legs in and clear of the door, never beside a boost or an obstacle leg. ENDLESS run (MainUi
/// infinity): every slowMoEndlessEvery legs, walking the same ladder. TEST MODE (Test Obstacles): one right at the
/// start, then again after every full round of obstacle designs.
///
/// Uses the boost's planner pieces (BoostPairs / BoostFits) and its own System.Random.
/// </summary>
public partial class DynamicPathGenerator
{
    public partial class Segment
    {
        public bool slowMo;           // a leg of a slow-motion section (its tiny legs and its exit leg)
        public bool slowMoExit;       // ... its regular-length exit leg, along which everything eases back to normal
        public float slowMoScale;     // the time scale of the section
        public float slowMoSpeed;     // the runner's speed factor on its tiny legs
        public int slowMoLeg;         // which leg of it, from 0
    }

    [Header("Slow-Motion Sections (3.11) - numbers in the DifficultyProfile")]
    [Tooltip("Lay a zig-zag of tiny legs now and then and slow everything down while the runner is on them. Needs a DifficultyProfile.")]
    public bool slowMoSections = false;
    [Tooltip("Endless run: legs between two sections.")]
    public Vector2Int slowMoEndlessEvery = new Vector2Int(40, 55);
    [Tooltip("Testing: > 0 forces the section's level (1-based) instead of the real one.")]
    public int slowMoLevelOverride = -1;
    [Tooltip("Print each section as it is planned.")]
    public bool logSlowMo = false;
    [Tooltip("3.11: the very first level (Start) has no boost and no obstacle leg - the player is still learning to read the path. Endless runs are exempt.")]
    public bool noSpecialsInFirstLevel = false;

    bool NoSpecialsHere => noSpecialsInFirstLevel && cycleIndex == 0 && endlessLevel == 0;

    System.Random slowMoRng;
    readonly List<Vector3> slowMoPlanDirs = new List<Vector3>();
    readonly List<int> slowMoPlanLengths = new List<int>();
    int slowMoPlanIndex, slowMoPlanCycle = -1, slowMoAtSerial;
    float slowMoPlanScale = 1f, slowMoPlanSpeed = 1f;
    int slowMoEndlessCount;
    int slowMoEndSerial = -100;   // last leg of the latest section (an obstacle leg keeps 4 legs away from it)
    int slowMoLaidLeg;            // the leg of the section just taken (read by AppendSegment)
    bool slowMoLaidExit;
    int slowMoSectionLegs;        // legs in the section being laid (its camera shot holds them all)

    /// <summary>True while the runner is on a slow-motion leg (tiny or exit).</summary>
    public bool PlayerOnSlowMo
    {
        get { Segment s = PlayerSegment; return s != null && s.slowMo && player != null && player.currentCube != null && player.currentCube != firstCube; }
    }

    /// <summary>The slow-motion leg right after the one the runner is on (so the slowdown can ease in before it), if any.</summary>
    public bool NextLegSlowMo(out Segment next)
    {
        next = null;
        int i = currentPlayerSegmentIdx + 1;
        if (i < 0 || i >= allSegments.Count) return false;
        next = allSegments[i];
        return next != null && next.slowMo;
    }

    void ResetSlowMo()
    {
        slowMoRng ??= new System.Random();
        slowMoPlanDirs.Clear();
        slowMoPlanLengths.Clear();
        slowMoPlanIndex = 0;
        slowMoPlanCycle = -1;
        slowMoEndlessCount = 0;
        slowMoEndSerial = -100;
        if (slowMoSections && GetComponent<SlowMoDirector>() == null)
            gameObject.AddComponent<SlowMoDirector>().pathGen = this;
        if (slowMoSections && player != null && player.GetComponent<RunnerSlowMoFX>() == null)
            player.gameObject.AddComponent<RunnerSlowMoFX>().pathGen = this;
    }

    // 1-based level the section belongs to (the ladder its leg count and time scale walk).
    int SlowMoLevel()
    {
        if (slowMoLevelOverride > 0) return slowMoLevelOverride;
        if (obstacleTestMode) return difficultyProfile.slowMoFromLevel;
        if (endlessLevel > 0) return Mathf.Min(10, difficultyProfile.slowMoFromLevel + slowMoEndlessCount);
        return cycleIndex + 1;
    }

    static int RollRange(System.Random rng, Vector2Int r) => rng.Next(Mathf.Min(r.x, r.y), Mathf.Max(r.x, r.y) + 1);

    /// <summary>Called by AppendSegment (after the obstacle leg, before boosts) for every leg after the first.</summary>
    bool TakeSlowMoLeg(Vector3 prevDir, out Vector3 dir, out int len)
    {
        dir = Vector3.zero;
        len = 0;
        if (!slowMoSections || tutorialActive || difficultyProfile == null) return false;
        DifficultyProfile P = difficultyProfile;
        slowMoRng ??= new System.Random();

        if (slowMoPlanIndex >= slowMoPlanDirs.Count)
        {
            int level = SlowMoLevel();
            if (level < P.slowMoFromLevel) return false;

            // 3.11 plans each level's specials together (DynamicPathGeneratorSpecials.cs): due when its slot comes up.
            int plannedLegs = -1;
            if (SpecialsOn && !SpecialDue(SpecialSlowMo, out plannedLegs)) return false;

            // When the next section is due: once per level; every 40-55 legs in an endless run; at once in test mode.
            if (SpecialsOn) slowMoAtSerial = 0;
            else if (obstacleTestMode)
            {
                if (slowMoPlanCycle != -3) { slowMoPlanCycle = -3; slowMoAtSerial = 1; }
            }
            else if (endlessLevel > 0)
            {
                if (slowMoPlanCycle != -2) { slowMoPlanCycle = -2; slowMoAtSerial = segmentSerial + RollRange(slowMoRng, slowMoEndlessEvery); }
            }
            else if (slowMoPlanCycle != cycleIndex)
            {
                slowMoPlanCycle = cycleIndex;
                int levelLen = Mathf.Max(30, nextPortalSerial - cycleStartSerial);
                int lo = cycleStartSerial + 10, hi = Mathf.Max(lo + 1, cycleStartSerial + levelLen - 14 - P.slowMoLegCap / 2);
                slowMoAtSerial = slowMoRng.Next(lo, hi + 1);
            }
            if (segmentSerial < slowMoAtSerial) return false;
            if (portalHold || portalExitPending) return false;
            if (boostPlanIndex < boostPlanDirs.Count) return false;                        // let a boost finish first
            if (Mathf.Abs(prevDir.y) > 0.9f) return false;
            if (segmentSerial < lastHeavyEndSerial + 5) return false;                       // not right after a boost
            for (int i = allSegments.Count - 1; i >= 0 && i >= allSegments.Count - 4; i--)  // ... nor an obstacle leg
                if (allSegments[i].portalRole == 3) return false;
            if (portalZones && segmentSerial + 8 >= nextPortalSerial) { slowMoAtSerial = int.MaxValue; return false; }

            int legs = plannedLegs;
            if (legs <= 0)
            {
                legs = RollRange(slowMoRng, P.slowMoLegs);
                for (int l = P.slowMoFromLevel; l < level; l++) legs += RollRange(slowMoRng, P.slowMoLegsPerLevel);
            }
            legs = Mathf.Min(legs, P.slowMoLegCap);
            if (portalZones)   // the whole section (and its exit leg) must be laid before the door's runway takes over
            {
                legs = Mathf.Min(legs, nextPortalSerial - segmentSerial - 9);
                if (legs < 3) { slowMoAtSerial = int.MaxValue; return false; }
            }
            float d = Mathf.Clamp01((level - P.slowMoFromLevel) / Mathf.Max(1f, 10f - P.slowMoFromLevel));
            float scale = Mathf.Lerp(P.slowMoTimeScale.x, P.slowMoTimeScale.y, d);

            if (!PlanSlowMo(prevDir, legs, scale))
            {
                slowMoAtSerial = segmentSerial + 1;   // no room here: the next leg tries again
                return false;
            }
            if (SpecialsOn) { MarkSpecialDone(); slowMoFinaleActive = slowMoFinaleNext; slowMoFinaleNext = false; }
            else if (obstacleTestMode) slowMoAtSerial = segmentSerial + legs + 2 * TestSequence.Length + 4;   // after a full round of designs
            else if (endlessLevel > 0) { slowMoEndlessCount++; slowMoPlanCycle = -1; }                 // re-arm: the next one in 40-55 legs
            else slowMoAtSerial = int.MaxValue;                                                       // done for this level
            if (logSlowMo) Debug.Log($"[SlowMo] level {level}: {legs} tiny legs ({string.Join(",", slowMoPlanLengths)}) at time x{scale:0.00}, runner x{slowMoPlanSpeed:0.00}, from leg {segmentSerial}");
        }

        dir = slowMoPlanDirs[slowMoPlanIndex];
        len = slowMoPlanLengths[slowMoPlanIndex];
        if (Mathf.Abs(dir.y) < 0.9f) lastHorizontalDirection = dir;
        return true;
    }

    void OnSlowMoLegTaken()
    {
        slowMoLaidLeg = slowMoPlanIndex;
        slowMoLaidExit = slowMoPlanIndex == slowMoPlanDirs.Count - 1;
        slowMoPlanIndex++;
        if (slowMoPlanIndex >= slowMoPlanDirs.Count)
        {
            slowMoPlanDirs.Clear();
            slowMoPlanLengths.Clear();
            slowMoPlanIndex = 0;
            slowMoEndSerial = segmentSerial;
            lastHeavyEndSerial = segmentSerial;
            breatherFrom = segmentSerial + 1;
            breatherUntil = segmentSerial + 3;
            if (!Waves) nextBoostSerial = Mathf.Max(nextBoostSerial, segmentSerial + 4);
            OnSlowMoFinished311();   // 3.11: the L5 finale's short-leg burst starts here
        }
    }

    // `tiny` tiny legs + one regular exit leg, zig-zagging between two headings (each turn a half turn, like a boost).
    bool PlanSlowMo(Vector3 prevDir, int tiny, float scale)
    {
        DifficultyProfile P = difficultyProfile;
        int count = Mathf.Max(2, tiny) + 1;
        boostRng ??= new System.Random();

        var lengths = new int[count];
        float speed = 1f;
        for (int i = 0; i < count - 1; i++)
        {
            lengths[i] = Mathf.Max(3, RollRange(slowMoRng, P.slowMoLegCubes));
            // Slow the runner until this leg lasts what a 2-roll turn needs at this time scale.
            float v = Mathf.Max(0.5f, LegSpeed(segmentSerial + i, false));
            // (Test Obstacles runs at leg 1, where the onboarding reaction budget is generous: judge it as the level would.)
            int humanLeg = obstacleTestMode ? NominalCheckpointLeg(Mathf.Max(0, SlowMoLevel() - 1)) + 10 : segmentSerial + i;
            float need = P.SlowMoLegSeconds(scale, humanLeg);
            float have = SecondsForCubes(lengths[i], v);
            speed = Mathf.Min(speed, have / Mathf.Max(0.01f, need));
        }
        speed = Mathf.Clamp(speed * P.slowMoSpeedBoost, 0.15f, 1f);   // a bit faster than the floor: it stays hard
        // The exit leg: an ordinary leg at full speed (everything eases back to normal along it).
        Vector2Int ordinary = GetSegmentLengthRange(segmentSerial + count - 1);
        lengths[count - 1] = slowMoRng.Next(ordinary.x, ordinary.y + 1);

        // Any direction each leg - left, right, up, down - a right angle off the leg before, never back along the last
        // flat heading (the camera's rule, as GetNextDirection), and never near any other leg (old track or its own).
        var dirs = new Vector3[count];
        for (int attempt = 0; attempt < 60; attempt++)
        {
            Vector3 before = prevDir, flat = lastHorizontalDirection;
            bool ok = true;
            for (int i = 0; i < count && ok; i++)
            {
                var options = new List<Vector3>();
                foreach (Vector3 d in AxisDirs)
                {
                    if (!Perpendicular(d, before)) continue;
                    if (!IsVertical(d) && flat.sqrMagnitude > 0.01f && Vector3.Dot(d, flat) < -0.9f) continue;
                    if (i == count - 1 && IsVertical(d)) continue;   // the exit leg runs flat, as an ordinary leg
                    options.Add(d);
                }
                if (options.Count == 0) { ok = false; break; }
                dirs[i] = options[slowMoRng.Next(0, options.Count)];
                before = dirs[i];
                if (!IsVertical(dirs[i])) flat = dirs[i];
            }
            if (!ok || !SlowMoFits(prevDir, dirs, lengths)) continue;

            slowMoPlanDirs.Clear();
            slowMoPlanLengths.Clear();
            for (int i = 0; i < count; i++) { slowMoPlanDirs.Add(dirs[i]); slowMoPlanLengths.Add(lengths[i]); }
            slowMoPlanIndex = 0;
            slowMoPlanScale = scale;
            slowMoPlanSpeed = speed;
            slowMoSectionLegs = count;
            return true;
        }
        return false;
    }

    // BoostFits for any list of headings: walks the section exactly as AppendSegment will lay it (diagonal riser and all)
    // and checks every leg keeps pathClearance from the old track and from its own legs (the leg just before exempt).
    bool SlowMoFits(Vector3 prevDir, Vector3[] dirs, int[] lengths)
    {
        int c = Mathf.Max(1, pathClearance);
        int lastSerial = segmentSerial - 1;
        var planned = new Dictionary<Vector3Int, int>();
        Vector3 pos = lastPosition, before = prevDir;
        for (int k = 0; k < lengths.Length; k++)
        {
            Vector3 dir = dirs[k];
            pos += before * cubeSize;   // the riser
            for (int i = 0; i <= lengths[k]; i++)
            {
                Vector3Int cell = CellOf(pos + dir * (i * cubeSize));
                for (int dx = -c; dx <= c; dx++)
                    for (int dy = -c; dy <= c; dy++)
                        for (int dz = -c; dz <= c; dz++)
                        {
                            Vector3Int n = cell + new Vector3Int(dx, dy, dz);
                            if (occupiedCells.TryGetValue(n, out int owner) && !(k == 0 && owner == lastSerial)) return false;
                            if (planned.TryGetValue(n, out int leg) && leg < k - 1) return false;
                        }
            }
            for (int i = 0; i <= lengths[k]; i++) planned[CellOf(pos + dir * (i * cubeSize))] = k;
            pos += dir * (lengths[k] * cubeSize);
            before = dir;
        }
        return true;
    }
}

/// <summary>
/// Drives a slow-motion section (DynamicPathGeneratorSlowMo.cs): eases the time scale (TimeScaleMixer.Source.SlowMo)
/// and the runner's speed (CubeRailRunner.SlowMoSpeedFactor) down just before the first tiny leg, holds them on the
/// tiny legs, and eases both back to 1 along the regular-length exit leg (by the runner's place on it), so the player
/// is at full speed again before the next ordinary turn. Easing in uses UNSCALED time. Added by ResetSlowMo.
/// </summary>
[DefaultExecutionOrder(-20)]
public class SlowMoDirector : MonoBehaviour
{
    public DynamicPathGenerator pathGen;
    [Tooltip("Real seconds to ease into the slowdown (it starts about this long before the first tiny leg).")]
    [Range(0.05f, 1.5f)] public float easeInSeconds = 0.35f;
    [Tooltip("Share of the exit leg over which time and speed come back to normal.")]
    [Range(0.2f, 1f)] public float exitEaseShare = 0.7f;

    float scale = 1f, speed = 1f;

    void Update()
    {
        if (pathGen == null) return;
        CubeRailRunner p = pathGen.player;
        float tScale = 1f, tSpeed = 1f;
        bool follow = false;   // exit leg: set straight from the runner's place, no smoothing
        if (p != null && !p.IsGameOver)
        {
            DynamicPathGenerator.Segment s = pathGen.PlayerSegment;
            if (pathGen.PlayerOnSlowMo && s != null)
            {
                if (s.slowMoExit)
                {
                    float len = Mathf.Max(1f, s.cubes.Count * pathGen.cubeSize);
                    float k = Mathf.SmoothStep(0f, 1f, Vector3.Dot(p.transform.position - s.startPosition, s.direction) / (len * exitEaseShare));
                    tScale = Mathf.Lerp(s.slowMoScale, 1f, k);
                    tSpeed = Mathf.Lerp(s.slowMoSpeed, 1f, k);
                    follow = true;
                }
                else { tScale = s.slowMoScale; tSpeed = s.slowMoSpeed; }
            }
            else if (s != null && pathGen.NextLegSlowMo(out DynamicPathGenerator.Segment next))
            {
                // Ease in over the last stretch of the leg before the section.
                float v = Mathf.Max(0.5f, p.CurrentMoveSpeed);
                float left = Vector3.Dot(s.endPosition - p.transform.position, s.direction) / v;
                if (left < easeInSeconds * 1.5f) { tScale = next.slowMoScale; tSpeed = next.slowMoSpeed; }
            }
        }
        else { scale = speed = 1f; follow = true; }   // a fresh run never starts slow

        if (follow) { scale = tScale; speed = tSpeed; }   // continuous: at the exit leg's start it equals the tiny legs' values
        else
        {
            float step = Time.unscaledDeltaTime / Mathf.Max(0.05f, easeInSeconds);
            scale = Mathf.MoveTowards(scale, tScale, step);
            speed = Mathf.MoveTowards(speed, tSpeed, step);
        }
        TimeScaleMixer.Set(TimeScaleMixer.Source.SlowMo, scale);
        if (p != null) p.SlowMoSpeedFactor = speed;
    }

    void OnDisable()
    {
        TimeScaleMixer.Set(TimeScaleMixer.Source.SlowMo, 1f);
        if (pathGen != null && pathGen.player != null) pathGen.player.SlowMoSpeedFactor = 1f;
    }
}
