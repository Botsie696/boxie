using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BOOST SECTIONS. Every few points of score the path lays a run of short legs that zig-zag
/// between the same two headings - left, right, left, right on the flat, or up, across, up,
/// across on a staircase. Each of those turns is onto the heading of two legs back, which is the
/// runner's HALF turn: the far face, reached with one double tap. So a boost is a quick burst of
/// double taps. The speed does not change; RunnerBoostFX just dresses the runner while it lasts.
///
/// A boost is planned whole before its first leg is laid, and only if every leg of it keeps
/// pathClearance away from the older track; when it does not fit, the next leg tries again.
/// Uses its own System.Random, so the path's UnityEngine.Random sequence up to the first boost
/// is the one it always was.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Boost Sections")]
    [Tooltip("Every few points, lay a run of short zig-zag legs that are each taken with a double tap (the half turn to the far face). The runner keeps its speed; RunnerBoostFX plays on it while it is on those legs.")]
    public bool boostSections = true;
    [Tooltip("Fewest points of score (path legs) between two boosts. The gap is drawn fresh between these two every time.")]
    [Range(2, 60)] public int minLegsBetweenBoosts = 12;
    [Tooltip("Most points of score (path legs) between two boosts.")]
    [Range(2, 60)] public int maxLegsBetweenBoosts = 16;
    [Tooltip("Legs in one boost.")]
    [Range(2, 12)] public int boostLegCount = 5;
    [Tooltip("Cubes in each middle leg of a boost. Every middle leg is this long (the first and last legs are longer, see the length multipliers).")]
    [Range(2, 30)] public int boostLegCubes = 12;
    [Tooltip("The runner moves this many times faster while on a zig-zag boost; the speed eases up as it enters and back down as it leaves.")]
    [Range(1f, 2f)] public float boostSpeedMultiplier = 1.2f;
    [Tooltip("The FIRST leg of a boost is this many times as long as an ordinary leg (rolled from the ordinary leg lengths at that speed), so there is time to get ready for the zig-zag.")]
    [Range(1f, 4f)] public float boostFirstLegLengthMultiplier = 2f;
    [Tooltip("Cameras zoom out by this factor while the runner is on a boost, and ease back afterwards.")]
    [Range(1f, 2f)] public float boostCameraZoomOut = 1.3f;
    [Tooltip("Seconds the camera takes to pull back as a boost starts.")]
    [Range(0.05f, 2f)] public float boostCameraZoomInSeconds = 0.5f;
    [Tooltip("Seconds the camera takes to settle back once the boost ends.")]
    [Range(0.05f, 2f)] public float boostCameraZoomOutSeconds = 0.25f;
    [Tooltip("Put a coin on the apex of every turn of a boost (in place of the usual coin runs), so the pickup sound lands on each double tap.")]
    public bool boostApexCoins = true;
    [Tooltip("Chance a boost zig-zags left/right on the flat; otherwise it is an up/down staircase. While the turn odds in force have up and down both at 0, boosts are always left/right.")]
    [Range(0f, 1f)] public float boostSidewaysChance = 0.5f;
    [Tooltip("A boost leg is never shorter than the runner crosses in this many seconds, so a boost stays possible once the speed has climbed. At the opening speeds the cube counts above are used as they are. 0 = always use the cube counts.")]
    [Range(0f, 1.5f)] public float boostMinLegSeconds = 0.4f;
    [Tooltip("The LAST leg of a boost is this many times as long as an ordinary leg (rolled from the ordinary leg lengths at that speed), so the end of the boost reads clearly.")]
    [Range(1f, 4f)] public float boostLastLegLengthMultiplier = 2f;
    [Tooltip("Print each boost as it is laid.")]
    public bool logBoosts = false;

    private System.Random boostRng;
    private int nextBoostSerial;
    private readonly List<Vector3> boostPlanDirs = new List<Vector3>();
    private readonly List<int> boostPlanLengths = new List<int>();
    private int boostPlanIndex;
    private int boostSerialCounter;

    /// <summary>True while the runner is on a leg of a boost.</summary>
    public bool PlayerOnBoostLeg
    {
        get
        {
            Segment s = PlayerSegment;
            return s != null && s.boost && player != null && player.currentCube != null &&
                   player.currentCube != firstCube;
        }
    }

    /// <summary>True on a zig-zag boost leg only (not the runway of a zone door, which is also lit like a boost).</summary>
    public bool PlayerOnZigZagBoost => PlayerOnBoostLeg && PlayerSegment.portalRole == 0;

    /// <summary>Which boost the runner is in (changes once per boost), and which leg of it (0-based).</summary>
    public int PlayerBoostId => PlayerSegment != null && PlayerSegment.boost ? PlayerSegment.boostId : -1;
    public int PlayerBoostLeg => PlayerSegment != null && PlayerSegment.boost ? PlayerSegment.boostLeg : -1;

    void ResetBoost()
    {
        boostRng ??= new System.Random();
        boostPlanDirs.Clear();
        boostPlanLengths.Clear();
        boostPlanIndex = 0;
        nextBoostSerial = RollBoostGap();
    }

    int RollBoostGap()
    {
        int lo = Mathf.Max(1, Mathf.Min(minLegsBetweenBoosts, maxLegsBetweenBoosts));
        int hi = Mathf.Max(lo, Mathf.Max(minLegsBetweenBoosts, maxLegsBetweenBoosts));
        return boostRng.Next(lo, hi + 1);
    }

    void EnsureBoostFX()
    {
        if (boostSections && player != null && player.GetComponent<RunnerBoostFX>() == null)
            player.gameObject.AddComponent<RunnerBoostFX>().pathGen = this;
    }

    /// <summary>
    /// Called by AppendSegment for every leg after the first. When a boost is due (or under way)
    /// hands back that leg's heading and length and marks the segment; otherwise false and the
    /// ordinary turn choice runs.
    /// </summary>
    bool TakeBoostLeg(Vector3 prevDir, float arrivalSpeed, out Vector3 dir, out int len, out int legOfBoost)
    {
        dir = Vector3.zero;
        len = 0;
        legOfBoost = -1;

        if (!boostSections)
        {
            boostPlanDirs.Clear();
            return false;
        }

        if (boostPlanIndex >= boostPlanDirs.Count)
        {
            int legs = boostLegCount;
            bool peak = false;
            // DifficultyProfile waves: boosts come from the cycle schedule, not the 12-16 leg timer.
            if (Waves) { if (!WaveBoostDue(out legs, out peak)) return false; }
            else if (segmentSerial < nextBoostSerial) return false;
            if (logBoosts) Debug.Log($"[Boost] due at leg {segmentSerial}: {legs} legs, peak {peak}");
            if (!PlanBoost(prevDir, arrivalSpeed, legs))   // no room here; the next leg tries again
            {
                if (logBoosts) Debug.Log($"[Boost] no room at leg {segmentSerial}");
                return false;
            }
            if (Waves) OnWaveBoostPlanned(peak);
        }

        legOfBoost = boostPlanIndex;
        dir = boostPlanDirs[boostPlanIndex];
        len = boostPlanLengths[boostPlanIndex];
        boostPlanIndex++;

        if (Mathf.Abs(dir.y) < 0.9f) lastHorizontalDirection = dir;

        if (boostPlanIndex >= boostPlanDirs.Count)
        {
            OnBoostLastLeg311();   // 3.11: B4 exit - the turn after this leg is a half turn
            boostPlanDirs.Clear();
            boostPlanLengths.Clear();
            boostPlanIndex = 0;
            nextBoostSerial = segmentSerial + 1 + RollBoostGap();
            if (Waves) OnBoostFinished();
        }
        return true;
    }

    bool PlanBoost(Vector3 prevDir, float arrivalSpeed, int legCount)
    {
        int count = Mathf.Max(2, legCount);

        // Every middle leg is boostLegCubes, lengthened only where the runner would cross them
        // faster than boostMinLegSeconds.
        int floorBySpeed = boostMinLegSeconds > 0f
            ? Mathf.CeilToInt(boostMinLegSeconds * Mathf.Max(0.01f, arrivalSpeed) / Mathf.Max(0.01f, cubeSize))
            : 0;
        int middle = Mathf.Max(Mathf.Max(2, boostLegCubes), floorBySpeed);
        // DifficultyProfile: a middle leg is a half turn (2 rolls) at BOOST speed, so it is floored at the
        // 2-tap boost time (boostWindowScale x T_human, never under boostMin2Tap) - shorter than an ordinary
        // leg, which is what makes it feel fast, never shorter than a human can do. Two cubes of jitter.
        if (TB)
        {
            // Speed of the boost's LAST leg (the fastest) against T_human of its first (the most generous
            // is the largest, so this is the stricter pairing for every leg in between).
            float vb = LegSpeed(segmentSerial + count - 1, true);
            middle = CubesForSeconds(difficultyProfile.MinLegSeconds(2, segmentSerial, true), vb);
        }
        var lengths = new int[count];
        for (int i = 0; i < count; i++) lengths[i] = TB ? middle + boostRng.Next(0, 2) : middle;

        // The first and last legs: an ordinary leg's length at this speed, times a multiplier, so
        // there is time to get ready and the end reads clearly. Drawn on the boost's own random,
        // so the path's UnityEngine.Random sequence is not moved.
        Vector2Int ordinary = GetSegmentLengthRange(segmentSerial);
        int firstLen = boostRng.Next(ordinary.x, ordinary.y + 1);
        const float endLegScale = 0.75f;   // start/end legs trimmed to 75%, whatever the scene's multiplier says
        lengths[0] = Mathf.Max(middle, Mathf.RoundToInt(firstLen * Mathf.Max(1f, boostFirstLegLengthMultiplier) * endLegScale));
        int ordinaryLen = boostRng.Next(ordinary.x, ordinary.y + 1);
        lengths[count - 1] = Mathf.Max(middle,
            Mathf.RoundToInt(ordinaryLen * Mathf.Max(1f, boostLastLegLengthMultiplier) * endLegScale));
        ShapeBoost311(lengths, middle);   // 3.11 boost modes: B3 broken rhythm, B4 exit (no-op elsewhere)

        // Up/down only where the odds in force let the path climb or dive at all.
        bool upOk = upWeight > 0, downOk = downWeight > 0;
        bool sidewaysFirst = (!upOk && !downOk) || boostRng.NextDouble() < boostSidewaysChance;

        for (int pass = 0; pass < 2; pass++)
        {
            bool sideways = pass == 0 ? sidewaysFirst : !sidewaysFirst;
            if (!sideways && !upOk && !downOk) continue;

            foreach (var pair in BoostPairs(prevDir, sideways, upOk, downOk))
            {
                if (!BoostFits(prevDir, pair.a, pair.b, lengths)) continue;

                boostPlanDirs.Clear();
                boostPlanLengths.Clear();
                for (int i = 0; i < count; i++)
                {
                    boostPlanDirs.Add(i % 2 == 0 ? pair.a : pair.b);
                    boostPlanLengths.Add(lengths[i]);
                }
                boostPlanIndex = 0;
                boostSerialCounter++;

                if (logBoosts)
                    Debug.Log($"[DynamicPathGenerator] boost at score {segmentSerial}: " +
                              $"{(sideways ? "left/right" : "up/down")} {pair.a} / {pair.b}, legs {string.Join(",", lengths)}");
                return true;
            }
        }
        return false;
    }

    static readonly Vector3[] AxisDirs =
    {
        Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.up, Vector3.down
    };

    static bool IsVertical(Vector3 d) => Mathf.Abs(d.y) > 0.9f;
    static bool Perpendicular(Vector3 a, Vector3 b) => Mathf.Abs(Vector3.Dot(a, b)) < 0.1f;

    /// <summary>
    /// The two headings a boost alternates between, A first. A leaves the leg before at a right
    /// angle; B is at a right angle to A. Sideways: both flat. Up/down: one of them vertical.
    /// Pairs that carry straight on in the heading the path already had come first, shuffled.
    /// </summary>
    List<(Vector3 a, Vector3 b)> BoostPairs(Vector3 prevDir, bool sideways, bool upOk, bool downOk)
    {
        var preferred = new List<(Vector3, Vector3)>();
        var others = new List<(Vector3, Vector3)>();

        Vector3 heading = lastHorizontalDirection;

        foreach (Vector3 a in AxisDirs)
        {
            if (!Perpendicular(a, prevDir)) continue;
            if (!VerticalAllowed(a, upOk, downOk)) continue;
            if (!IsVertical(a) && heading.sqrMagnitude > 0.01f && Vector3.Dot(a, heading) < -0.9f) continue;

            Vector3 flatAfterA = IsVertical(a) ? heading : a;

            foreach (Vector3 b in AxisDirs)
            {
                if (!Perpendicular(a, b)) continue;
                if (!VerticalAllowed(b, upOk, downOk)) continue;

                bool bothFlat = !IsVertical(a) && !IsVertical(b);
                if (sideways != bothFlat) continue;

                // Never reverse the last flat heading (see GetNextDirection).
                if (!IsVertical(b) && flatAfterA.sqrMagnitude > 0.01f && Vector3.Dot(b, flatAfterA) < -0.9f) continue;

                bool carriesOn = Vector3.Dot(b, prevDir) > 0.9f;
                (carriesOn ? preferred : others).Add((a, b));
            }
        }

        Shuffle(preferred);
        Shuffle(others);
        preferred.AddRange(others);
        return preferred;
    }

    static bool VerticalAllowed(Vector3 d, bool upOk, bool downOk)
    {
        if (d.y > 0.9f) return upOk;
        if (d.y < -0.9f) return downOk;
        return true;
    }

    void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = boostRng.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// Walks the boost exactly as AppendSegment will lay it (diagonal riser and all) and checks
    /// every leg keeps pathClearance from the track - the old track, and the boost's own earlier
    /// legs - with the leg just before each one exempt, as in LegIsClear.
    /// </summary>
    bool BoostFits(Vector3 prevDir, Vector3 a, Vector3 b, int[] lengths)
    {
        int c = Mathf.Max(1, pathClearance);
        int lastSerial = segmentSerial - 1;
        var planned = new Dictionary<Vector3Int, int>();

        Vector3 pos = lastPosition;
        Vector3 before = prevDir;

        for (int k = 0; k < lengths.Length; k++)
        {
            Vector3 dir = k % 2 == 0 ? a : b;
            pos += before * cubeSize;   // the riser

            for (int i = 0; i <= lengths[k]; i++)
            {
                Vector3Int cell = CellOf(pos + dir * (i * cubeSize));
                for (int dx = -c; dx <= c; dx++)
                    for (int dy = -c; dy <= c; dy++)
                        for (int dz = -c; dz <= c; dz++)
                        {
                            Vector3Int n = cell + new Vector3Int(dx, dy, dz);
                            if (occupiedCells.TryGetValue(n, out int owner) && !(k == 0 && owner == lastSerial))
                                return false;
                            if (planned.TryGetValue(n, out int leg) && leg < k - 1)
                                return false;
                        }
            }

            for (int i = 0; i <= lengths[k]; i++)
                planned[CellOf(pos + dir * (i * cubeSize))] = k;

            pos += dir * (lengths[k] * cubeSize);
            before = dir;
        }
        return true;
    }
}
