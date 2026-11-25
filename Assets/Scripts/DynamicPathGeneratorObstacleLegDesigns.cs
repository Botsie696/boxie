using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// OBSTACLE-LEG DESIGNS (MainScene prop camera 3.11 + Test Obstacles - off unless obstacleLegDesigns). Lays each
/// obstacle leg out from a per-level table instead of the 3.10 "two pillars on opposite faces" every time, so each
/// level brings in its own pattern and gets a little harder. Everything is built from the SAME totem pillar the 3.10
/// / 3.11 obstacle legs use (obstacleLegPillarBlocks, coloured per stage by LegStageColors), and every pillar stays on
/// its face - the player only ever has to read "which faces have a pillar" and roll to a free one:
///   Totem         one pillar (one face blocked)
///   Pair          two pillars on opposite faces
///   Corner        two pillars on NEIGHBOURING faces (an L): take one of the other two
///   Fence         a short row of pillars on one face, 3 cubes apart (stay off that face for longer)
///   Drifter       one pillar SLIDING toward the runner along the rail
///   Twin          two opposite pillars sliding together
///   CornerSlider  an L of two pillars sliding together
///
/// Fair by construction: sliding pillars are a function of the runner's PROGRESS along the leg (HazardPattern) and all
/// slide at one ratio of the runner's speed, live only in the back part of the leg and never cross anything; the gap
/// to the next station is the reaction floor (DifficultyProfile.MinLegSeconds) for the rolls needed between the faces
/// the two leave open; nothing blocks three faces; the lead and the tail of the leg stay clear.
///
/// TEST MODE (scene "Test Obstacles", ObstacleTestScene): every other leg is a ~10 s obstacle leg showing ONE design
/// (the next one each time, TestSequence), labelled for the on-screen bar (Segment.designLabel / designHint).
///
/// Uses the obstacle leg's own System.Random; off = the 3.10 random sequence is untouched.
/// </summary>
public partial class DynamicPathGenerator
{
    public enum DesignKind { Totem, Pair, Corner, Fence, Drifter, Twin, CornerSlider }

    public partial class Segment
    {
        public string designLabel;   // obstacle leg: what is on it (test bar / logs)
        public string designHint;    // ... and how to get through
    }

    [Header("Obstacle Leg Designs (3.11)")]
    [Tooltip("Lay each obstacle leg out from the per-level design table (pillars, pairs, L corners, fences, sliding pillars) instead of the 3.10 pillar pairs.")]
    public bool obstacleLegDesigns = false;
    [Tooltip("Sliding pillars: their speed as a share of the runner's, from the easiest to the hardest level.")]
    public Vector2 obstacleDesignApproach = new Vector2(0.22f, 0.42f);
    [Tooltip("Share of the way down the leg (between the first and the last station that fits) where sliding pillars take over from the standing ones.")]
    public Vector2 obstacleDesignMarchFrom = new Vector2(0.5f, 0.68f);
    [Tooltip("First table row (portals crossed) that may have sliding pillars.")]
    [Range(1, 10)] public int obstacleDesignMarchLevel = 3;
    [Tooltip("Testing: -1 = by level, otherwise use this table row (portals crossed).")]
    public int obstacleDesignLevelOverride = -1;
    [Tooltip("Testing: -1 = by table, otherwise every station is this DesignKind.")]
    public int obstacleDesignForceKind = -1;
    [Tooltip("ENDLESS run (MainUi's infinity button, endlessLevel > 0): one obstacle leg every this many legs, showing level 2's design, then 3's ... 10's, then random designs for good.")]
    public Vector2Int endlessObstacleEveryLegs = new Vector2Int(50, 60);
    [Tooltip("TEST MODE (scene Test Obstacles): every other leg is an obstacle leg showing the next design of TestSequence.")]
    public bool obstacleTestMode = false;
    [Tooltip("Test mode: seconds each obstacle leg lasts.")]
    [Range(4f, 30f)] public float obstacleTestLegSeconds = 10f;
    [Tooltip("Test mode: start the sequence at this entry (0 = the first).")]
    public int obstacleTestStartIndex = 0;
    [Tooltip("Coins on the obstacle legs: a short run before each station on the face that is safe there (and a coin beside the station), and beside each sliding pillar where it meets the runner - always on a face nothing blocks.")]
    public bool obstacleLegCoins = false;

    int endlessDesignCount;   // obstacle legs laid so far in this endless run
    int obstacleTestCounter;  // test mode: obstacle legs laid so far
    bool EndlessDesigns => obstacleLegDesigns && endlessLevel > 0;

    const float FenceSpacing = 3f;   // cubes between two pillars of a fence

    struct DesignEntry { public DesignKind kind; public int weight; public DesignEntry(DesignKind k, int w) { kind = k; weight = w; } }

    /// <summary>Level names by portals crossed (level number shown to the player = this + 1).</summary>
    public static readonly string[] DesignLevelNames = { "Start", "Crimson", "Toon", "Pixel", "Storybook", "Flower", "Voxel", "Pencil", "Space", "Spice Route" };

    // One row per level (portals crossed). The first STANDING entry is the level's signature (its first station), the
    // first SLIDING entry the signature of its sliding part.
    static readonly DesignEntry[][] DesignTable =
    {
        /* 0 Start: none */ null,
        /* 1 Crimson     */ new[] { new DesignEntry(DesignKind.Totem, 6), new DesignEntry(DesignKind.Pair, 2) },
        /* 2 Toon        */ new[] { new DesignEntry(DesignKind.Pair, 4), new DesignEntry(DesignKind.Totem, 2), new DesignEntry(DesignKind.Fence, 2) },
        /* 3 Pixel       */ new[] { new DesignEntry(DesignKind.Corner, 4), new DesignEntry(DesignKind.Pair, 2), new DesignEntry(DesignKind.Totem, 2), new DesignEntry(DesignKind.Drifter, 1) },
        /* 4 Storybook   */ new[] { new DesignEntry(DesignKind.Fence, 4), new DesignEntry(DesignKind.Corner, 2), new DesignEntry(DesignKind.Pair, 2), new DesignEntry(DesignKind.Drifter, 2) },
        /* 5 Flower      */ new[] { new DesignEntry(DesignKind.Corner, 3), new DesignEntry(DesignKind.Fence, 2), new DesignEntry(DesignKind.Pair, 2), new DesignEntry(DesignKind.Twin, 3), new DesignEntry(DesignKind.Drifter, 1) },
        /* 6 Voxel       */ new[] { new DesignEntry(DesignKind.Corner, 3), new DesignEntry(DesignKind.Pair, 2), new DesignEntry(DesignKind.CornerSlider, 3), new DesignEntry(DesignKind.Twin, 2) },
        /* 7 Pencil      */ new[] { new DesignEntry(DesignKind.Fence, 3), new DesignEntry(DesignKind.Corner, 3), new DesignEntry(DesignKind.Pair, 2), new DesignEntry(DesignKind.Twin, 2), new DesignEntry(DesignKind.CornerSlider, 2), new DesignEntry(DesignKind.Drifter, 1) },
        /* 8 Space       */ new[] { new DesignEntry(DesignKind.Corner, 3), new DesignEntry(DesignKind.Pair, 2), new DesignEntry(DesignKind.Fence, 2), new DesignEntry(DesignKind.CornerSlider, 3), new DesignEntry(DesignKind.Twin, 3) },
        /* 9 Spice Route */ new[] { new DesignEntry(DesignKind.Corner, 2), new DesignEntry(DesignKind.Pair, 2), new DesignEntry(DesignKind.Fence, 2), new DesignEntry(DesignKind.Totem, 1), new DesignEntry(DesignKind.Twin, 2), new DesignEntry(DesignKind.CornerSlider, 2), new DesignEntry(DesignKind.Drifter, 2) },
    };

    struct TestEntry
    {
        public int kind;      // DesignKind, -1 = the level's own mix
        public int level;     // table row (portals crossed)
        public TestEntry(int k, int l) { kind = k; level = l; }
    }

    // Test Obstacles: one design per obstacle leg, in the order the levels bring them in, then the last level's mix.
    static readonly TestEntry[] TestSequence =
    {
        new TestEntry((int)DesignKind.Totem, 1),
        new TestEntry((int)DesignKind.Pair, 2),
        new TestEntry((int)DesignKind.Corner, 3),
        new TestEntry((int)DesignKind.Drifter, 3),
        new TestEntry((int)DesignKind.Fence, 4),
        new TestEntry((int)DesignKind.Twin, 5),
        new TestEntry((int)DesignKind.CornerSlider, 6),
        new TestEntry(-1, 9),
    };

    public static string DesignTitle(DesignKind k)
    {
        switch (k)
        {
            case DesignKind.Totem: return "PILLAR";
            case DesignKind.Pair: return "PILLAR PAIR";
            case DesignKind.Corner: return "L CORNER";
            case DesignKind.Fence: return "PILLAR FENCE";
            case DesignKind.Drifter: return "SLIDING PILLAR";
            case DesignKind.Twin: return "TWIN SLIDERS";
            default: return "SLIDING L";
        }
    }

    public static string DesignHint(DesignKind k)
    {
        switch (k)
        {
            case DesignKind.Totem: return "one face blocked - be on any other face";
            case DesignKind.Pair: return "two opposite faces blocked - take one of the other two";
            case DesignKind.Corner: return "two neighbouring faces blocked - take one of the other two";
            case DesignKind.Fence: return "a row of pillars on one face - stay off it until the row ends";
            case DesignKind.Drifter: return "a pillar slides toward you on one face - leave that face";
            case DesignKind.Twin: return "two pillars slide toward you on opposite faces - take a side between them";
            default: return "an L of two pillars slides toward you - take one of the other two faces";
        }
    }

    // Portals crossed when leg `serial` is laid (the same count LegStageColors uses).
    int DesignLevelOf(int serial)
    {
        if (obstacleDesignLevelOverride >= 0) return obstacleDesignLevelOverride;
        // Endless run: the theme never changes, so the DESIGNS walk through every level's instead (rows 1..9), then
        // the random mix (any level >= the table length = the last row's mix at full difficulty).
        if (EndlessDesigns) return endlessDesignCount < DesignTable.Length - 1 ? endlessDesignCount + 1 : DesignTable.Length;
        int level = 0, end = CheckpointLeg(1);
        for (int k = 1; k < 64 && serial >= end; k++) { level = k; end = CheckpointLeg(k + 1); }
        return level;
    }

    static bool CanSlide(DesignKind k) => k == DesignKind.Drifter || k == DesignKind.Twin || k == DesignKind.CornerSlider;

    // The faces a station blocks when its first pillar is on face `face`.
    static int DesignFaces(DesignKind k, int face)
    {
        int a = 1 << face;
        switch (k)
        {
            case DesignKind.Pair:
            case DesignKind.Twin: return a | 1 << ((face + 2) % 4);
            case DesignKind.Corner:
            case DesignKind.CornerSlider: return a | 1 << ((face + 1) % 4);
            default: return a;
        }
    }

    DesignKind PickDesign(DesignEntry[] row, bool slide, bool signature)
    {
        if (signature) foreach (DesignEntry e in row) if (CanSlide(e.kind) == slide) return e.kind;
        int total = 0;
        foreach (DesignEntry e in row) if (CanSlide(e.kind) == slide) total += e.weight;
        if (total <= 0) return slide ? DesignKind.Drifter : DesignKind.Totem;
        int roll = obstacleLegRng.Next(0, total);
        foreach (DesignEntry e in row)
        {
            if (CanSlide(e.kind) != slide) continue;
            roll -= e.weight;
            if (roll < 0) return e.kind;
        }
        return row[0].kind;
    }

    float DesignGapSeconds(int rolls, int serial, float d, bool slide)
    {
        float floor = TB ? difficultyProfile.MinLegSeconds(rolls, serial, false)
                         : obstacleLegMinGapSeconds + rolls * obstacleLegSecondsPerRoll;
        float s = floor * Mathf.Lerp(1.3f, 1.02f, d) + (slide ? 0.15f : 0f);
        s = Mathf.Max(obstacleLegMinGapSeconds, s);
        return s * (1f + (float)obstacleLegRng.NextDouble() * obstacleLegGapJitter * 0.6f);
    }

    float Rand(float lo, float hi) => Mathf.Lerp(lo, hi, (float)obstacleLegRng.NextDouble());

    // Half a pillar's thickness along the leg, in cubes (what BuildPillarBody will make).
    float PillarHalfThickness() => HasPillarBlocks() ? obstacleLegPillarBlockSize * 0.5f : obstacleLegPoleThickness * 0.5f;

    struct PlacedDesign { public HazardPattern pat; public float meet; }

    // Two stations must never overlap anywhere the runner could see it (progress 0 .. past both meets).
    static bool Crosses(HazardPattern a, float meetA, HazardPattern b, float meetB)
    {
        float end = Mathf.Max(meetA, meetB) + 4f;
        float clear = a.halfDepth + b.halfDepth + 0.6f;
        for (float p = 0f; p <= end; p += 1f)
            if (Mathf.Abs(a.ZAt(p) - b.ZAt(p)) < clear) return true;
        return false;
    }

    /// <summary>Called first thing by PlaceObstacleLegPoles: true = the designs laid this leg, false = 3.10 pillars.</summary>
    bool PlaceObstacleLegDesigns(Segment seg)
    {
        List<GameObject> cubes = seg.cubes;
        int n = cubes.Count;
        if (n < 12) return false;

        int level, forcedKind = obstacleDesignForceKind;
        if (obstacleTestMode)
        {
            int idx = Mathf.Abs(obstacleTestStartIndex + obstacleTestCounter) % TestSequence.Length;
            TestEntry te = TestSequence[idx];
            obstacleTestCounter++;
            level = te.level;
            if (forcedKind < 0) forcedKind = te.kind;
            string where = "L" + (level + 1) + " " + DesignLevelNames[Mathf.Clamp(level, 0, DesignLevelNames.Length - 1)];
            seg.designLabel = (idx + 1) + "/" + TestSequence.Length + "  " + (te.kind < 0 ? where.ToUpper() + " MIX" : DesignTitle((DesignKind)te.kind)) + "  -  " + where;
            seg.designHint = te.kind < 0 ? "every obstacle, mixed" : DesignHint((DesignKind)te.kind);
        }
        else level = DesignLevelOf(seg.serial);

        DesignEntry[] row = level <= 0 ? DesignTable[1] : level >= DesignTable.Length ? DesignTable[DesignTable.Length - 1] : DesignTable[level];
        DesignEntry[] row311 = obstacleTestMode ? null : DesignRow311(seg);   // 3.11: the level's (or the finale's) designs
        bool is311 = row311 != null;
        if (is311) row = row311;
        int target311 = is311 ? runwayStationsNext : 0;
        float d = level >= DesignTable.Length ? 1f : Mathf.Clamp01((level - 1) / 8f);
        if (!obstacleTestMode)
        {
            seg.designLabel = level < DesignTable.Length ? "L" + (level + 1) + " " + DesignLevelNames[Mathf.Clamp(level, 0, 9)] + ": " + DesignTitle(row[0].kind) : "RANDOM MIX";
            seg.designHint = level < DesignTable.Length ? DesignHint(row[0].kind) : "";
        }

        float v = Mathf.Max(0.5f, LegSpeed(seg.serial, false));
        float cps = v / Mathf.Max(0.01f, cubeSize);

        float lead = obstacleLegLeadSeconds;
        if (TB) lead = Mathf.Max(lead, difficultyProfile.MinLegSeconds(2, seg.serial, false));
        float tail = obstacleLegTailSeconds;
        if (TB) tail = Mathf.Max(tail, obstacleLegGlideOutSeconds + difficultyProfile.MinLegSeconds(2, seg.serial, false));
        int first = Mathf.Max(3, Mathf.CeilToInt(lead * cps));
        int lastAllowed = n - 1 - Mathf.CeilToInt(tail * cps);

        if (obstacleLayer < 0) obstacleLayer = ResolveObstacleLayer();
        if (obstacleShading == null) obstacleShading = GetComponent<PathDepthShading>();

        float r = Mathf.Lerp(Mathf.Min(obstacleDesignApproach.x, obstacleDesignApproach.y), Mathf.Max(obstacleDesignApproach.x, obstacleDesignApproach.y), d);
        bool forcedSlide = forcedKind >= 0 && CanSlide((DesignKind)forcedKind);
        bool canSlide = forcedKind >= 0 ? forcedSlide : level >= obstacleDesignMarchLevel;
        bool slideOnly311 = false;
        if (is311)
        {
            Vector2 sp = Spec311At(seg.cycle).sliderSpeed;
            bool rowSlides = false, rowStands = false;
            foreach (DesignEntry e in row) { if (CanSlide(e.kind)) rowSlides = true; else rowStands = true; }
            canSlide = forcedKind < 0 && rowSlides && sp.y > 0f;
            if (canSlide) r = Rand(Mathf.Min(sp.x, sp.y), Mathf.Max(sp.x, sp.y));
            slideOnly311 = canSlide && !rowStands;   // a slider-only row (a finale signature) slides from the first station
        }
        int lastSlide = Mathf.Min(lastAllowed, Mathf.FloorToInt((n - 2f) / (1f + r)));
        float marchFrom = !canSlide ? float.MaxValue
            : forcedSlide || slideOnly311 ? first
            : Mathf.Lerp(first, lastSlide, Rand(Mathf.Min(obstacleDesignMarchFrom.x, obstacleDesignMarchFrom.y), Mathf.Max(obstacleDesignMarchFrom.x, obstacleDesignMarchFrom.y)));
        float pillarHalf = PillarHalfThickness();
        int fencePillars = d < 0.5f ? 3 : 4;
        float fenceSpan = (fencePillars - 1) * FenceSpacing * 0.5f;

        var placed = new List<PlacedDesign>();
        var laid = new List<LaidStation>();
        var log = logObstacleLegs ? new System.Text.StringBuilder() : null;
        int prevMask = 0, stations = 0;
        float lastEnd = -1f, lastPole = -1f;
        bool slideSigDone = false;
        int slidersPlaced311 = 0;

        for (int guard = 0; guard < 60; guard++)
        {
            if (target311 > 0 && stations >= target311) break;   // 3.11: the level's station count
            float earliest = stations == 0 ? first + Rand(0f, 0.6f) * cps : lastEnd + obstacleLegMinGapSeconds * cps;
            bool slide = canSlide && earliest >= marchFrom && (!is311 || slidersPlaced311 < Spec311At(seg.cycle).maxSliders);
            if (slide && Mathf.CeilToInt((1f + r) * earliest) > n - 2) break;

            HazardPattern chosen = null;
            DesignKind chosenKind = DesignKind.Totem;
            int chosenFaces = 0, chosenFace = 0;
            float chosenMeet = 0f, chosenSpan = 0f;
            for (int attempt = 0; attempt < 10 && chosen == null; attempt++)
            {
                DesignKind kind = forcedKind >= 0 ? (DesignKind)Mathf.Clamp(forcedKind, 0, 6)
                                : PickDesign(row, slide, slide ? !slideSigDone : stations == 0);
                if (slide != CanSlide(kind)) { if (forcedKind >= 0) break; continue; }
                int face = obstacleLegRng.Next(0, 4);
                int faces = DesignFaces(kind, face);
                float span = kind == DesignKind.Fence ? fenceSpan : 0f;

                // Measured from where the last station ENDS to where this one STARTS (a fence is long).
                int rolls = RollsNeeded(prevMask, faces);
                float gapS = is311 ? DesignGap311(seg, rolls, slide) : DesignGapSeconds(rolls, seg.serial, d, slide);
                float meet = stations == 0 ? earliest + span
                           : Mathf.Max(earliest, lastEnd + gapS * cps) + span;
                meet = Mathf.Round(meet);
                if (slide ? Mathf.CeilToInt((1f + r) * meet) > n - 2 || meet > lastSlide : meet + span > lastAllowed) { attempt = 99; break; }

                var pat = new HazardPattern
                {
                    zStart = slide ? (1f + r) * meet : meet,
                    approach = slide ? r : 0f,
                    halfDepth = pillarHalf + span,
                    faces = faces,
                };
                bool clash = false;
                foreach (PlacedDesign pd in placed) if (Crosses(pat, meet, pd.pat, pd.meet)) { clash = true; break; }
                if (clash) continue;
                chosen = pat; chosenKind = kind; chosenFaces = faces; chosenFace = face; chosenMeet = meet; chosenSpan = span;
            }
            if (chosen == null) break;

            if (CanSlide(chosenKind)) { BuildSlider(seg, chosen); slideSigDone = true; slidersPlaced311++; }
            else
            {
                // Standing pillars: the very 3.10 poles (PathObstacleGuard + ObstacleLegPoleGuard; the bot reads them).
                int count = chosenKind == DesignKind.Fence ? fencePillars : 1;
                for (int k = 0; k < count; k++)
                {
                    int ci = Mathf.Clamp(Mathf.RoundToInt(chosenMeet - chosenSpan + k * FenceSpacing), 0, n - 1);
                    if (cubes[ci] == null) continue;
                    for (int s = 0; s < 4; s++)
                        if ((chosenFaces & (1 << s)) != 0) PlacePole(cubes[ci].transform, s, false);
                }
            }

            seg.designMask311 |= 1 << (int)chosenKind;   // 3.11 skill-model tag
            placed.Add(new PlacedDesign { pat = chosen, meet = chosenMeet });
            laid.Add(new LaidStation { meet = chosenMeet, span = chosenSpan, faces = chosenFaces, slide = CanSlide(chosenKind) });
            log?.Append(chosenMeet).Append(':').Append(chosenKind).Append(chosen.approach > 0f ? "~" : "").Append('/').Append(chosenFaces).Append(' ');
            prevMask = chosenFaces;
            lastEnd = chosenMeet + chosenSpan;
            lastPole = lastEnd;
            stations++;
        }

        seg.obstacleGlideInCubes = Mathf.Max(2f, Mathf.Min(first - 1f, obstacleLegLeadSeconds * 0.8f * cps));
        float outCubes = Mathf.Max(2f, obstacleLegGlideOutSeconds * cps);
        seg.obstacleGlideOutCubes = outCubes;
        seg.obstacleGlideOutFrom = Mathf.Min((lastPole >= 0f ? lastPole : first) + 1.5f, n - 1 - outCubes);
        if (EndlessDesigns && !obstacleTestMode) endlessDesignCount++;
        if (obstacleLegCoins && spawnCoins) PlaceObstacleLegCoins(seg, laid, first);

        if (logObstacleLegs)
            Debug.Log($"[ObstacleLeg311] leg {seg.serial} level {level} d {d:0.00} '{seg.designLabel}': {n} cubes at {v:0.0} u/s, {stations} stations (meet:kind/faces, ~ = slides) {log}");
        return true;
    }

    // A group of totem pillars (one per face of the pattern) that slides toward the runner. Same pillar as the standing
    // ones; every level's colour comes from LegStageColors (pillars and root are named "ObstaclePole").
    struct LaidStation { public float meet, span; public int faces; public bool slide; }

    static Vector3 FaceNormal(Transform cube, int face) =>
        face == 0 ? cube.up : face == 1 ? cube.right : face == 2 ? -cube.up : -cube.right;

    // Coins that show the way through: before each standing station a short run on a face that is free there (one the
    // runner can already be on after the station before, when there is one), carrying on beside the station itself;
    // at each sliding pillar a few coins at the meeting point on a face it does not slide along. Never on a blocked face.
    void PlaceObstacleLegCoins(Segment seg, List<LaidStation> laid, int first)
    {
        List<GameObject> cubes = seg.cubes;
        int n = cubes.Count;
        int prevFree = 15;   // before the first station every face is open
        float prevEnd = first - 4f;
        int run = 0;
        foreach (LaidStation st in laid)
        {
            int free = ~st.faces & 15;
            int both = free & prevFree;
            int pick = both != 0 ? both : free;
            var options = new List<int>();
            for (int s = 0; s < 4; s++) if ((pick & (1 << s)) != 0) options.Add(s);
            if (options.Count == 0) continue;
            int face = options[obstacleLegRng.Next(0, options.Count)];

            float from, to;
            if (st.slide) { from = st.meet - 2f; to = st.meet + 2f; }                                  // where it passes the runner
            else { from = Mathf.Max(prevEnd + 3f, st.meet - st.span - 7f); to = st.meet + st.span; }  // the run in + beside it
            for (float z = Mathf.Ceil(from); z <= to; z += coinSpacing)
            {
                int ci = Mathf.RoundToInt(z);
                if (ci < 1 || ci >= n - 1 || cubes[ci] == null) continue;
                Transform ct = cubes[ci].transform;
                PlaceSingleCoin(seg, ct, FaceNormal(ct, face), run++);
            }
            prevFree = free;
            prevEnd = st.meet + st.span;
        }
    }

    void BuildSlider(Segment seg, HazardPattern pat)
    {
        int n = seg.cubes.Count;
        Transform parent = seg.cubes[Mathf.Clamp(n / 2, 0, n - 1)].transform;

        var root = new GameObject("ObstaclePole") { layer = obstacleLayer };
        root.transform.SetParent(parent, false);
        for (int s = 0; s < 4; s++)
        {
            if ((pat.faces & (1 << s)) == 0) continue;
            var holder = new GameObject("Face" + s) { layer = obstacleLayer };
            holder.transform.SetParent(root.transform, false);
            holder.transform.localRotation = Quaternion.AngleAxis(-90f * s, Vector3.forward);   // local +y = out of face s
            var pillar = new GameObject("ObstaclePole") { layer = obstacleLayer };
            pillar.transform.SetParent(holder.transform, false);
            pillar.transform.localPosition = new Vector3(0f, cubeSize * 0.5f, 0f);
            BuildPillarBody(pillar.transform);
        }

        var guard = root.AddComponent<PathObstacleGuard>();
        guard.player = player;
        guard.blockedSide = -1;   // the bot reads ObstacleLegHazard instead
        guard.enabled = false;    // never fades / scales itself; it is only what the runner's shield shatters

        var hz = root.AddComponent<ObstacleLegHazard>();
        hz.player = player;
        hz.pathGen = this;
        hz.seg = seg;
        hz.pattern = pat;
        hz.shieldGuard = guard;
    }
}
