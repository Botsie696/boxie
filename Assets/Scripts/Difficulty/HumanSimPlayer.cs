using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// A simulated HUMAN player, for tuning the difficulty (DifficultyProfile). Two ways to use it:
///
/// 1. Live (press K in the editor, or add the component): plays the real game through CubeRailRunner.SimulateTap,
///    exactly like screen taps (same mapping, same buffer, same queue). It only reacts to a target once it is
///    on screen (inside the drawn track) and once the runner has landed on that leg, then taps after
///      reaction  Normal(mean, sd)   (skilled 0.25 / 0.04 s, average 0.42 / 0.07 s)
///    + contact   0.09 s, only when taps fire on finger lift (the old input)
///    + noise     Normal(0, sd)      (skilled 0.035 s, average 0.05 s)
///    with at least 0.15 s between its own taps, and picks the wrong direction 1% of the time (3% within 1 s of
///    a camera turn or a control re-map). A wrong roll is seen after a second reaction and corrected if time allows.
///    With runBatch it plays batchRuns runs back to back (restarting itself) at batchTimeScale.
///
/// 2. Batch (HumanSimPlayer.RunBatch, editor tooling): lays seeded runs with DynamicPathGenerator.DryLay and plays
///    the same human model against the same mechanical rules analytically - hundreds of runs in seconds - then
///    prints the survival curve, median run, per-leg-type failure rates, death causes, NEAR_LATE / LOST_TAP /
///    MAP_CHANGE shares, rescue rate and floor violations.
/// </summary>
public class HumanSimPlayer : MonoBehaviour
{
    public enum Model { Skilled, Average }

    [System.Serializable]
    public struct Human
    {
        public float rtMean, rtSd, noiseSd, gapMin, gapMean, gapSd, wrongRate, wrongRateNearChange, correctionRtScale;
        public float lateChance, lateExtra;   // a late input now and then (+lateExtra s); 0 = never (the old model)
        public static Human For(Model m) => m == Model.Skilled
            ? new Human { rtMean = 0.25f, rtSd = 0.04f, noiseSd = 0.035f, gapMin = 0.15f, gapMean = 0.16f, gapSd = 0.025f, wrongRate = 0.01f, wrongRateNearChange = 0.03f, correctionRtScale = 1f }
            : new Human { rtMean = 0.42f, rtSd = 0.07f, noiseSd = 0.05f, gapMin = 0.15f, gapMean = 0.16f, gapSd = 0.025f, wrongRate = 0.01f, wrongRateNearChange = 0.03f, correctionRtScale = 1f };
    }

    [Header("Model")]
    public Model model = Model.Average;
    public int seed = 1;
    [Header("Batch (live)")]
    public bool runBatch = false;
    public int batchRuns = 10;
    [Range(1f, 4f)] public float batchTimeScale = 4f;
    [Header("Results")]
    public List<int> runScores = new List<int>();
    public int rescues;

    CubeRailRunner runner;
    DynamicPathGenerator gen;
    PathDepthShading shading;
    System.Random rng;
    Human h;

    // the current decision
    int targetFace = -1;
    float targetDeadlineDist;      // cubes along the leg the target must be met by
    bool deciding;
    float fireAt, secondAt;
    int fireDir, secondDir, pendingTaps;
    Transform plannedOnCube;
    float lastOwnTap = -10f;
    float blockedUntilDist = -1f;  // on a plank: no taps until the runner is past it

    void Start()
    {
        runner = FindFirstObjectByType<CubeRailRunner>();
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        shading = gen != null ? gen.GetComponent<PathDepthShading>() : null;
        rng = new System.Random(seed);
        h = Human.For(model);
        if (runner != null) runner.Rescued += _ => rescues++;
        if (runBatch) Time.timeScale = batchTimeScale;
    }

    float Gauss(float mean, float sd)
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return mean + sd * (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Sin(2.0 * System.Math.PI * u2));
    }

    bool FireOnLift => runner.Profile == null || !runner.Profile.fairInput || !runner.Profile.fireOnTouchDown;

    float restartAt = -1f;
    void Update()
    {
        if (runner == null || gen == null) return;
        if (runBatch && Time.timeScale < batchTimeScale - 0.01f && Time.timeScale > 0.5f) Time.timeScale = batchTimeScale;

        if (runner.IsWaitingToStart) { runner.BeginRun(); return; }
        if (runner.IsGameOver)
        {
            if (restartAt < 0f)
            {
                runScores.Add(gen.Score);
                restartAt = Time.unscaledTime + 1.2f;
                if (runBatch && runScores.Count >= batchRuns) { Report(); runBatch = false; }
            }
            else if (runBatch && Time.unscaledTime >= restartAt) { restartAt = -1f; deciding = false; pendingTaps = 0; runner.Restart(); }   // pendingTaps too: a death mid-plan left it > 0 and Think() never planned again
            return;
        }
        restartAt = -1f;
        if (!runner.IsRunning || runner.currentCube == null) return;

        if (plannedOnCube != runner.currentCube) { deciding = false; pendingTaps = 0; plannedOnCube = runner.currentCube; blockedUntilDist = -1f; }
        Think();
        Act();
    }

    // Which face is needed next on this leg, and by how far along it. Planks first, then the corner.
    bool NextTarget(out int face, out float dist, out float afterDist)
    {
        face = -1; dist = 0f; afterDist = -1f;
        var seg = gen.PlayerSegment;
        Transform cube = runner.currentCube;
        if (seg == null || cube == null) return false;
        float along = Vector3.Dot(runner.transform.position - cube.position, cube.forward) / gen.cubeSize;   // cubes from cube 0
        foreach (var p in seg.planks)
        {
            if (along > p.y + 0.5f) continue;                          // already past this plank
            if (along >= p.x - 0.5f) { face = p.z; dist = along; afterDist = p.y + 0.5f; return true; }  // on it: hold
            face = p.z; dist = p.x - 0.5f; return true;
        }
        if (seg.portalRole == 1) { face = gen.PortalEntrySide; dist = seg.cubes.Count - 1; return true; }
        if (seg.turnSide >= 0) { face = seg.turnSide; dist = seg.cubes.Count - 1.05f; return true; }
        var corners = gen.GetUpcomingCorners(1);
        if (corners.Count == 0) return false;
        face = runner.SideFacing(corners[0].outgoingDirection);
        dist = seg.cubes.Count - 1.05f;
        return true;
    }

    void Think()
    {
        if (deciding || pendingTaps > 0) return;
        if (Time.time < nextLookAt) return;   // look again only once the last tap has played (it waits for the next physics step)
        if (!NextTarget(out int face, out float dist, out float afterDist)) return;
        Transform cube = runner.currentCube;
        float along = Vector3.Dot(runner.transform.position - cube.position, cube.forward) / gen.cubeSize;

        // On a plank: no rolling until past it; the next target is planned for right after.
        if (afterDist > 0f && runner.currentSideIndex == face)
        {
            blockedUntilDist = afterDist;
            return;
        }
        if (runner.currentSideIndex == face) return;

        // Only once the target is on screen (inside the drawn track).
        float visible = shading != null && shading.enabled ? shading.VisibleAheadCubes : 24f;
        if (dist - along > visible) return;

        int rolls = DynamicPathGenerator.Rolls(runner.currentSideIndex, face);
        int dir = runner.TapDirectionToReach(rolls == 1 ? face : (runner.currentSideIndex + 1) % 4);
        if (dir == 0) dir = 1;
        bool near = Time.time - runner.LastViewChangeTime <= 1f || Time.time - runner.LastRemapChangeTime <= 1f;
        bool wrong = rng.NextDouble() < (near ? h.wrongRateNearChange : h.wrongRate);

        float speed = Mathf.Max(0.01f, runner.CurrentMoveSpeed);
        float ready = Time.time;
        if (blockedUntilDist > 0f && along < blockedUntilDist)
        {
            // Anticipated: the move is ready; tap as the runner clears the plank, with a small safety margin.
            ready = Time.time + (blockedUntilDist - along) * gen.cubeSize / speed + Mathf.Abs(Gauss(0f, h.noiseSd)) + h.noiseSd;
            fireAt = ready;
        }
        else fireAt = ready + Mathf.Max(0.12f, Gauss(h.rtMean, h.rtSd)) + Gauss(0f, h.noiseSd);
        if (FireOnLift) fireAt += 0.09f;
        fireAt = Mathf.Max(fireAt, lastOwnTap + h.gapMin);

        planFace = rolls == 1 ? face : (runner.currentSideIndex + 1) % 4;
        planRolls = rolls;
        planWrong = wrong;
        fireDir = wrong && rolls == 1 ? -dir : dir;
        pendingTaps = rolls;
        secondDir = wrong && rolls == 2 ? -dir : dir;
        deciding = true;
    }

    void Act()
    {
        if (!deciding) return;
        if (pendingTaps == 2 && Time.time >= fireAt)
        {
            LookAgainAtPress();
            runner.SimulateTap(fireDir);
            lastOwnTap = Time.time;
            secondAt = Time.time + Mathf.Max(h.gapMin, Gauss(h.gapMean, h.gapSd));
            pendingTaps = 1;
            fireDir = secondDir;
            fireAt = secondAt;
            return;
        }
        if (pendingTaps == 1 && Time.time >= fireAt)
        {
            if (planRolls == 1) LookAgainAtPress();
            runner.SimulateTap(fireDir);
            lastOwnTap = Time.time;
            pendingTaps = 0;
            // Look again once the roll has played: a wrong roll is corrected after a fresh reaction.
            deciding = false;
            blockedUntilDist = -1f;
            nextLookAt = Time.time + 0.1f;
        }
    }
    float nextLookAt;

    // RollMappingMode.ScreenProjected (prop camera 3.3 only): where a press goes depends on the screen at the moment of the
    // press (camera, burst timing), so a human looks at the markers again just before tapping. Legacy: nothing changes.
    int planFace, planRolls;
    bool planWrong;
    void LookAgainAtPress()
    {
        if (runner.rollMapping != RollMappingMode.ScreenProjected) return;
        int d = runner.TapDirectionToReach(planFace);
        if (d == 0) return;
        fireDir = planWrong && planRolls == 1 ? -d : d;
        secondDir = planWrong && planRolls == 2 ? -d : d;
    }

    void Report()
    {
        var s = new List<int>(runScores);
        s.Sort();
        Debug.Log($"[HumanSim live {model}] runs={s.Count} median={(s.Count > 0 ? s[s.Count / 2] : 0)} scores={string.Join(",", runScores)} rescues={rescues}");
    }

    // ============================================================================ analytic batch

    /// <summary>Mechanical rules the analytic model plays against.</summary>
    public struct Mech
    {
        public float contact, landingLock, deadline, cooldown, rescue, legOffsetCubes, jitter, portalGrace;
        public bool flipRemap;
        public static Mech Old() => new Mech { contact = 0.09f, landingLock = 0.12f, deadline = 0.06f, cooldown = 0.08f, rescue = 0f, legOffsetCubes = 1.75f, jitter = 0.02f, portalGrace = 0.15f, flipRemap = true };
        public static Mech New(DifficultyProfile p) => new Mech
        {
            contact = p.fairInput && p.fireOnTouchDown ? 0f : 0.09f,
            landingLock = (p.fairInput ? p.landingLock : 0.1f) + 0.02f,
            deadline = p.fairCorners ? 0.01f : 0.06f,
            cooldown = 0.08f,
            rescue = p.fairCorners ? p.rescueWindow : 0f,
            legOffsetCubes = p.fairCorners ? 1.1f : 1.75f,
            jitter = p.fairCorners ? 0.01f : 0.02f,
            portalGrace = 0.15f,
            flipRemap = !(p.fairInput && p.disableFlipRemap),
        };
    }

    public class BatchResult
    {
        public string label;
        public int runs;
        public List<int> scores = new List<int>();
        public Dictionary<string, int> legsByType = new Dictionary<string, int>(), deathsByType = new Dictionary<string, int>();
        public Dictionary<string, int> causes = new Dictionary<string, int>();
        public int nearLate, lostTap, mapChange, rescues, corners, floorViolations, legsChecked;
    }

    static string TypeOf(DynamicPathGenerator.LegFacts f)
    {
        if (f.portalRole != 0) return "portal";
        if (f.boost) return "boost";
        if (f.plankCount > 1) return "multiplank";
        if (f.plankCount == 1) return "bridge";
        if (f.decoyAtEnd) return "decoy";
        if (f.obstacle) return "obstacle";
        return "ordinary";
    }

    static float G(System.Random r, float mean, float sd)
    {
        double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
        return mean + sd * (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Sin(2.0 * System.Math.PI * u2));
    }

    // Plays one leg. Returns null if survived, else the death cause. rescued / nearLate are set on the way.
    public static string PlayLeg(DynamicPathGenerator.LegFacts f, Human h, Mech m, System.Random r, float visibleCubes, float cubeSize,
                          ref bool rescued, ref bool nearLate, ref bool mapChange)
    {
        float v = Mathf.Max(0.01f, f.speed);
        float T(float cubes) => cubes * cubeSize / v;
        float legEnd = T(Mathf.Max(0.5f, f.cubes - m.legOffsetCubes));   // when the corner is decided, from landing
        bool near = f.cameraTurn;
        mapChange = near;

        // The targets, in order: (time the face must be held from, face, time the hold ends; <0 = corner/door)
        var targets = new List<(float at, int face, float until)>(4);
        if (f.plankCount > 0)
        {
            Vector3Int[] ps = { f.p0, f.p1, f.p2 };
            for (int i = 0; i < f.plankCount && i < 3; i++)
                targets.Add((T(ps[i].x - 0.55f), ps[i].z, T(ps[i].y + 0.45f)));
        }
        int turnFace = f.portalRole == 1 ? 0 : f.turnSide;
        targets.Add((legEnd, turnFace, -1f));

        int side = f.landingSide;
        float lastExec = -1f;       // when the last roll played
        float humanFree = 0f;       // earliest the human can tap again
        float holdEnd = -1f;        // previous plank: no rolls before this
        bool firstDecision = true;
        bool remapPending = false;

        foreach (var tg in targets)
        {
            int need = DynamicPathGenerator.Rolls(side, tg.face);
            bool isCorner = tg.until < 0f;
            float deadline = tg.at - (isCorner ? m.deadline : 0.01f);
            float grace = isCorner ? (f.portalRole == 1 ? m.portalGrace : m.rescue) : m.rescue;

            // Visible from: the target within the drawn track.
            float visibleFrom = Mathf.Max(0f, tg.at - T(visibleCubes));
            int attempts = 0;
            while (need > 0 && attempts++ < 3)
            {
                bool anticipated = holdEnd > 0f;
                float tap1;
                if (anticipated) tap1 = holdEnd + Mathf.Abs(G(r, 0f, h.noiseSd)) + h.noiseSd;   // tap as the plank is cleared
                else
                {
                    float rt = Mathf.Max(h.lateChance > 0f || h.rtMean < 0.12f ? 0.03f : 0.12f, G(r, h.rtMean, h.rtSd)) * (firstDecision ? 1f : h.correctionRtScale);
                    if (h.lateChance > 0f && r.NextDouble() < h.lateChance) rt += h.lateExtra;
                    tap1 = Mathf.Max(visibleFrom, humanFree) + rt + G(r, 0f, h.noiseSd);
                }
                tap1 += m.contact;
                tap1 = Mathf.Max(tap1, humanFree + (lastExec >= 0f ? 0f : 0f));
                if (anticipated && tap1 - m.contact < holdEnd) return "plank";   // rolled off the plank too early

                bool q = near || remapPending;
                bool wrong = r.NextDouble() < (q ? h.wrongRateNearChange : h.wrongRate) * (isCorner && f.decoyAtEnd ? 2f : 1f);
                float exec1 = Mathf.Max(tap1 + (float)r.NextDouble() * m.jitter, m.landingLock, lastExec >= 0f ? lastExec + m.cooldown : 0f);
                float execLast = exec1;
                int sideAfter;
                if (need == 1)
                {
                    sideAfter = wrong ? (tg.face + 2) % 4 : tg.face;
                    humanFree = tap1 + h.gapMin;
                }
                else
                {
                    float tap2 = tap1 + Mathf.Max(h.gapMin, G(r, h.gapMean, h.gapSd));
                    execLast = Mathf.Max(tap2 + (float)r.NextDouble() * m.jitter, exec1 + m.cooldown);
                    sideAfter = wrong ? side : tg.face;            // inconsistent directions: back where it started
                    humanFree = tap2 + h.gapMin;
                }
                lastExec = execLast;
                firstDecision = false;
                holdEnd = -1f;

                if (!wrong)
                {
                    if (execLast <= deadline) { side = tg.face; need = 0; break; }
                    if (execLast <= deadline + grace + 0.0001f && !(isCorner && f.decoyAtEnd && false))
                    {
                        if (f.portalRole != 1) rescued = true;
                        side = tg.face; need = 0; break;
                    }
                    if (execLast <= deadline + 0.12f) nearLate = true;
                    return isCorner ? (f.portalRole == 1 ? "portal" : (f.boost ? "boost" : "corner")) : "plank";
                }

                // Wrong: seen once the roll has played, then corrected.
                side = sideAfter;
                need = DynamicPathGenerator.Rolls(side, tg.face);
                humanFree = Mathf.Max(humanFree, execLast + 0.1f);
                if (execLast > deadline) return isCorner ? (f.decoyAtEnd && r.NextDouble() < 0.5 ? "decoy" : "corner") : "plank";
            }
            if (need > 0) return isCorner ? (f.decoyAtEnd ? "decoy" : "corner") : "plank";

            // A flip that puts the runner on the far side of its landing top/bottom face re-maps the controls 0.3 s later (old input).
            if (m.flipRemap && (f.landingSide == 0 || f.landingSide == 2) && side == (f.landingSide + 2) % 4) remapPending = true;
            if (!isCorner) { holdEnd = tg.until; humanFree = Mathf.Max(humanFree, tg.at); }
        }
        return null;
    }

    /// <summary>Plays `runs` simulated runs over the leg corpora (one corpus per seed, re-used round-robin).</summary>
    public static BatchResult Simulate(string label, List<List<DynamicPathGenerator.LegFacts>> corpora, Model model, Mech mech,
                                       int runs, int seed, float visibleCubes, float cubeSize)
    {
        var res = new BatchResult { label = label, runs = runs };
        var h = Human.For(model);
        var r = new System.Random(seed);
        foreach (var c in corpora)
            foreach (var f in c) { res.legsChecked++; if (f.floorViolation) res.floorViolations++; }

        for (int run = 0; run < runs; run++)
        {
            var legs = corpora[run % corpora.Count];
            int score = legs.Count;
            for (int i = 0; i < legs.Count; i++)
            {
                var f = legs[i];
                string type = TypeOf(f);
                res.legsByType[type] = (res.legsByType.TryGetValue(type, out int n) ? n : 0) + 1;
                bool rescued = false, near = false, map = false;
                string death = PlayLeg(f, h, mech, r, visibleCubes, cubeSize, ref rescued, ref near, ref map);
                res.corners++;
                if (rescued) res.rescues++;
                if (death == null) continue;
                score = f.serial;
                res.deathsByType[type] = (res.deathsByType.TryGetValue(type, out int d) ? d : 0) + 1;
                res.causes[death] = (res.causes.TryGetValue(death, out int k) ? k : 0) + 1;
                if (near) res.nearLate++;
                if (map) res.mapChange++;
                break;
            }
            res.scores.Add(score);
        }
        return res;
    }

    public static string Format(BatchResult r, int corpusLegs)
    {
        var sb = new StringBuilder();
        var s = new List<int>(r.scores);
        s.Sort();
        int deaths = 0;
        foreach (var kv in r.causes) deaths += kv.Value;
        float Alive(int leg) { int a = 0; foreach (int x in r.scores) if (x >= leg) a++; return 100f * a / Mathf.Max(1, r.scores.Count); }
        sb.AppendLine($"== {r.label}: {r.runs} runs (corpus {corpusLegs} legs; a run that survives all of it counts as {corpusLegs}) ==");
        sb.Append("alive at leg ");
        foreach (int l in new[] { 10, 30, 60, 100, 150, 200, 300 }) sb.Append($"{l}:{Alive(l):0}%  ");
        sb.AppendLine();
        sb.AppendLine($"median run {s[s.Count / 2]} legs | p25 {s[s.Count / 4]} | p75 {s[Mathf.Min(s.Count - 1, s.Count * 3 / 4)]} | ended before 30: {100f - Alive(30):0}% | passed 150: {Alive(150):0}% | passed 300: {Alive(300):0}%");
        sb.Append("per-leg failure p: ");
        float ordP = 0f;
        foreach (var kv in r.legsByType)
        {
            int d = r.deathsByType.TryGetValue(kv.Key, out int x) ? x : 0;
            float p = (float)d / Mathf.Max(1, kv.Value);
            if (kv.Key == "ordinary") ordP = p;
            sb.Append($"{kv.Key} {100f * p:0.00}% ({d}/{kv.Value})  ");
        }
        sb.AppendLine();
        if (ordP > 0f)
        {
            sb.Append("ratio to ordinary: ");
            foreach (var kv in r.legsByType)
            {
                int d = r.deathsByType.TryGetValue(kv.Key, out int x) ? x : 0;
                sb.Append($"{kv.Key} x{((float)d / Mathf.Max(1, kv.Value)) / ordP:0.0}  ");
            }
            sb.AppendLine();
        }
        sb.Append("death causes: ");
        foreach (var kv in r.causes) sb.Append($"{kv.Key} {100f * kv.Value / Mathf.Max(1, deaths):0}%  ");
        sb.AppendLine();
        sb.AppendLine($"NEAR_LATE {100f * r.nearLate / Mathf.Max(1, deaths):0.0}% | LOST_TAP {100f * r.lostTap / Mathf.Max(1, deaths):0.0}% | MAP_CHANGE {100f * r.mapChange / Mathf.Max(1, deaths):0.0}% of deaths | rescues {100f * r.rescues / Mathf.Max(1, r.corners):0.00}% of legs | floor violations {r.floorViolations} in {r.legsChecked} laid legs");
        return sb.ToString();
    }

    /// <summary>
    /// Editor tooling: lays `corpora` seeded runs of `corpusLegs` legs with the scene's generator (as it is
    /// configured right now), then simulates `runs` runs for each model. Returns the report text.
    /// </summary>
    public static string RunBatch(string label, Mech mech, int corpora, int corpusLegs, int runs, int seed,
                                  out List<List<DynamicPathGenerator.LegFacts>> corpus)
    {
        var gen = FindFirstObjectByType<DynamicPathGenerator>();
        corpus = new List<List<DynamicPathGenerator.LegFacts>>();
        if (gen == null) return "no generator";
        for (int i = 0; i < corpora; i++) corpus.Add(gen.DryLay(corpusLegs, seed + 1000 * i));
        var shading = gen.GetComponent<PathDepthShading>();
        float visible = shading != null && shading.enabled ? shading.VisibleAheadCubes : 24f;
        var sb = new StringBuilder();
        foreach (Model m in new[] { Model.Average, Model.Skilled })
            sb.AppendLine(Format(Simulate($"{label} / {m}", corpus, m, mech, runs, seed + 7, visible, gen.cubeSize), corpusLegs));
        return sb.ToString();
    }
}

#if UNITY_EDITOR
/// <summary>Editor-only: K in Play mode hands the runner to a HumanSimPlayer (average model), K again takes it back.</summary>
public class HumanSimHotkey : MonoBehaviour
{
    HumanSimPlayer sim;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var go = new GameObject("HumanSimHotkey (editor only)");
        DontDestroyOnLoad(go);
        go.AddComponent<HumanSimHotkey>();
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.K)) return;
        if (sim != null) { Destroy(sim); sim = null; Debug.Log("[HumanSim] OFF"); return; }
        var runner = FindFirstObjectByType<CubeRailRunner>();
        if (runner == null) return;
        sim = runner.gameObject.AddComponent<HumanSimPlayer>();
        Debug.Log("[HumanSim] ON (average human model) - K to stop");
    }
}
#endif
