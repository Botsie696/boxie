using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Scene 3.11 analytic bot simulation (editor, Play mode; called from Tools > Scene 3.11 > Sim or the MCP). Lays seeded
/// tracks with the real generator (DryLaySeeded) at several adaptive tiers, then plays them with HumanSimPlayer's per-leg
/// human model (reaction from landing, motor noise, tap gaps, wrong-direction rate, 3% late input +0.12 s) against the
/// real mechanics and each level's own rescue window. Additions for 3.11:
///   - slow-motion tiny legs are played in real time (game time / time scale; the runner is slowed too)
///   - obstacle runways: every station after the first is a mini corner (0/1/2 rolls 20/60/20%) with its gap =
///     the rolls' own floor x the level's gap factor (+ jitter), as the generator spaces them
///   - the expert-planner reads the track ahead: no reaction, only motor jitter (sd .03) and tap throughput
/// Results: DifficultyAudit_3_11/phase6/*.md
/// </summary>
public static class Sim311
{
    public struct BotProfile { public string name; public float rt, sd; public bool planner; }

    public static readonly BotProfile[] Profiles =
    {
        new BotProfile { name = "Struggling", rt = 0.45f, sd = 0.09f },
        new BotProfile { name = "Below",      rt = 0.38f, sd = 0.08f },
        new BotProfile { name = "Average",    rt = 0.32f, sd = 0.07f },
        new BotProfile { name = "Above",      rt = 0.26f, sd = 0.06f },
        new BotProfile { name = "Expert-pl",  rt = 0.06f, sd = 0.03f, planner = true },
    };

    public static readonly int[] TargetAverage = { 85, 70, 55, 40, 30, 55, 20, 15, 10 };
    public static readonly int[] TargetPlanner = { 98, 92, 85, 70, 60, 80, 45, 40, 30 };

    public static HumanSimPlayer.Human HumanFor(BotProfile p, float rtScale = 1f)
    {
        if (p.planner)
            return new HumanSimPlayer.Human { rtMean = p.rt, rtSd = p.sd, noiseSd = 0.01f, gapMin = 0.12f, gapMean = 0.13f, gapSd = 0.02f,
                                              wrongRate = 0.003f, wrongRateNearChange = 0.01f, correctionRtScale = 1f, lateChance = 0.03f, lateExtra = 0.12f };
        return new HumanSimPlayer.Human { rtMean = p.rt * rtScale, rtSd = p.sd * rtScale, noiseSd = 0.02f, gapMin = 0.15f, gapMean = 0.16f, gapSd = 0.025f,
                                          wrongRate = 0.01f, wrongRateNearChange = 0.03f, correctionRtScale = 1f, lateChance = 0.03f, lateExtra = 0.12f };
    }

    // ---------------------------------------------------------------------- corpora

    public class Leg
    {
        public DynamicPathGenerator.LegFacts f;
        public string part;
        public float slack;
        public int stations;
        public float gapFactor;
        public bool sliders;
        public int mask;
    }

    public class Bucket
    {
        public float tier, speed;
        public TierMods311 mods;
        public Dictionary<int, List<List<Leg>>> levels = new Dictionary<int, List<List<Leg>>>();   // level -> corpora
        public Dictionary<int, float> rescue = new Dictionary<int, float>();
    }

    static DynamicPathGenerator gen;
    static Scene311DifficultyProfile P;
    static SkillTuning311 tuning;

    public static List<Bucket> BuildCorpora(int seeds, int legs, float[] tiers, bool speedVariants)
    {
        gen = Object.FindFirstObjectByType<DynamicPathGenerator>();
        P = gen.difficultyProfile as Scene311DifficultyProfile;
        tuning = SkillProfile311.Instance != null ? SkillProfile311.Instance.tuning : ScriptableObject.CreateInstance<SkillTuning311>();
        bool vf = P.validateFloors; P.validateFloors = false;
        var list = new List<Bucket>();
        try
        {
            Adaptive311.Enabled = true;
            foreach (float t in tiers)
            {
                list.Add(Lay(t, 1f, seeds, legs));
                if (speedVariants && t <= -1f) list.Add(Lay(t, -1f, seeds, legs));   // the mercy speed (x0.94 at Struggling)
            }
        }
        finally { P.validateFloors = vf; Adaptive311.Reset(); }
        return list;
    }

    static Bucket Lay(float tier, float speedFlag, int seeds, int legs)
    {
        var model = new SkillModel311(tuning, SkillSave311.New(tuning.startRating));
        model.S.runs = tuning.collectOnlyRuns;
        model.S.rating = tuning.startRating + tier * tuning.pointsPerTier;
        TierMods311 m = model.ModsFor(1, out _, out _);
        if (speedFlag < 0f) m.speed = tuning.RowAt(tier).speed; else m.speed = 1f;
        Adaptive311.Apply(m);
        var b = new Bucket { tier = tier, speed = m.speed, mods = m };
        for (int s = 0; s < seeds; s++)
        {
            var facts = gen.DryLaySeeded(legs, 5000 + s * 97 + Mathf.RoundToInt(tier * 10f) * 13 + (speedFlag < 0f ? 7 : 0));
            List<Leg> cur = null; int curLevel = -1;
            foreach (var f in facts)
            {
                int level = f.cycle + 1;
                LevelSpec311 spec = P.Spec311(f.cycle);
                if (level != curLevel)
                {
                    curLevel = level;
                    if (!b.levels.TryGetValue(level, out var lst)) b.levels[level] = lst = new List<List<Leg>>();
                    cur = new List<Leg>(110);
                    lst.Add(cur);
                    if (spec != null) b.rescue[level] = spec.rescueWindow;
                }
                LegPlan311 pl = spec != null ? spec.PlanAt(f.cyclePos) : default;
                string part = f.portalRole == 1 ? "door" : f.cyclePos == 0 ? "runwayOut" : pl.release ? "release" : pl.exhale ? "exhale"
                            : pl.finale ? "finale" : f.glide ? "glide" : pl.phase == WavePhase.Peak ? "peak" : "build";
                float floor = P.MinLegSeconds(f.taps, f.serial, f.boost);
                var L = new Leg { f = f, part = part, slack = f.seconds - floor, mask = Mask(f) };
                if (f.portalRole == 3 && spec != null)
                {
                    L.stations = Mathf.RoundToInt((spec.stations.x + spec.stations.y) * 0.5f);
                    L.gapFactor = spec.gapFactor;
                    L.sliders = spec.sliderSpeed.y > 0f;
                }
                cur.Add(L);
            }
        }
        return b;
    }

    static int Mask(DynamicPathGenerator.LegFacts f)
    {
        if (f.portalRole == 3) return 1 << (int)Feature311.RunwayTotem;
        if (f.slowMo) return 1 << (int)Feature311.SlowMo;
        if (f.boost) return 1 << ((int)Feature311.BoostClassic + Mathf.Clamp(f.boostMode, 0, 3));
        int m = 0;
        if (f.decoyAtEnd) m |= 1 << (int)Feature311.Decoy;
        if (f.taps >= 2) m |= 1 << (int)Feature311.Corner2; else if (f.taps == 1) m |= 1 << (int)Feature311.Corner1;
        return m;
    }

    // ---------------------------------------------------------------------- one attempt at one level

    public struct Attempt { public bool clear; public int deathIndex; public string deathPart; public int rescues; public float seconds; public int legs; }

    static readonly List<float> legResults = new List<float>(128);

    public static Attempt PlayLevel(List<Leg> legs, HumanSimPlayer.Human h, HumanSimPlayer.Mech mech, System.Random r, float cubeSize, SkillModel311 learn)
    {
        var a = new Attempt { deathIndex = -1 };
        for (int i = 0; i < legs.Count; i++)
        {
            Leg L = legs[i];
            DynamicPathGenerator.LegFacts f = L.f;
            if (f.slowMo && f.slowMoSpeed > 0f) f.speed = f.speed * f.slowMoSpeed * Mathf.Max(0.1f, f.slowMoScale);   // played in real time
            bool rescued = false, near = false, map = false;
            string death = f.portalRole == 2 ? null : HumanSimPlayer.PlayLeg(f, h, mech, r, 92f, cubeSize, ref rescued, ref near, ref map);
            if (death == null && L.stations > 1) death = PlayStations(L, h, mech, r, f.serial);
            a.seconds += f.seconds / (f.slowMo ? Mathf.Max(0.1f, f.slowMoScale) : 1f);
            a.legs++;
            if (rescued) a.rescues++;
            float result = death != null ? 0f : rescued ? 0.6f : 1f;
            if (learn != null && f.portalRole != 2) learn.OnLeg(L.mask, Mathf.Max(0, f.cost), L.slack, result);
            if (death != null) { a.deathIndex = i; a.deathPart = L.part; return a; }
        }
        a.clear = true;
        return a;
    }

    // Each station after the first: a face change in the station's gap (0/1/2 rolls 20/60/20%).
    static string PlayStations(Leg L, HumanSimPlayer.Human h, HumanSimPlayer.Mech mech, System.Random r, int serial)
    {
        for (int s = 1; s < L.stations; s++)
        {
            double u = r.NextDouble();
            int taps = u < 0.2 ? 0 : u < 0.8 ? 1 : 2;
            if (taps == 0) continue;
            float gap = P.MinLegSeconds(taps, serial, false) * L.gapFactor * (1f + (float)r.NextDouble() * 0.25f) + (L.sliders && s * 2 >= L.stations ? 0.15f : 0f);
            var mini = new DynamicPathGenerator.LegFacts
            {
                serial = serial, taps = taps, landingSide = 0, turnSide = taps == 2 ? 2 : 1, speed = L.f.speed,
                cubes = Mathf.CeilToInt(gap * L.f.speed + 1.1f), seconds = gap,
            };
            bool rescued = false, near = false, map = false;
            var m = mech; m.rescue = 0f;   // a pillar has no rescue
            string d = HumanSimPlayer.PlayLeg(mini, h, m, r, 92f, 1f, ref rescued, ref near, ref map);
            if (d != null) return "runway";
        }
        return null;
    }

    static HumanSimPlayer.Mech MechFor(Bucket b, int level)
    {
        var m = HumanSimPlayer.Mech.New(P);
        if (b.rescue.TryGetValue(level, out float w)) m.rescue = w;
        return m;
    }

    static Bucket Nearest(List<Bucket> bs, float tier, bool slow)
    {
        Bucket best = bs[0]; float bd = float.MaxValue;
        foreach (var b in bs)
        {
            bool bslow = b.speed < 0.999f;
            float d = Mathf.Abs(b.tier - tier) + (bslow != slow ? 10f : 0f);
            if (d < bd) { bd = d; best = b; }
        }
        return best;
    }

    // ---------------------------------------------------------------------- A: base clear rates (adaptive off)

    public static string BaseClearRates(List<Bucket> bs, int runs)
    {
        Bucket b = Nearest(bs, 0f, false);
        var sb = new StringBuilder();
        sb.AppendLine("## Bot clear rates, adaptive OFF (Average tier), " + runs + " attempts per checkpoint per profile");
        sb.AppendLine("| Profile | L1 | L2 | L3 | L4 | L5 | L6 | L7 | L8 | L9 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        var deathParts = new Dictionary<string, Dictionary<string, int>>();
        var spikes = new StringBuilder();
        float cs = gen.cubeSize;
        foreach (var p in Profiles)
        {
            var h = HumanFor(p);
            var row = new StringBuilder("| " + p.name + (p.planner ? "" : $" {p.rt:0.00}/{p.sd:0.00}") + " |");
            var parts = new Dictionary<string, int>();
            for (int level = 1; level <= 9; level++)
            {
                if (!b.levels.TryGetValue(level, out var corpora) || corpora.Count == 0) { row.Append(" - |"); continue; }
                var r = new System.Random(100 + level * 31 + p.name.Length * 7);
                var mech = MechFor(b, level);
                int clears = 0; var perPos = new Dictionary<int, int>();
                for (int i = 0; i < runs; i++)
                {
                    var legs = corpora[i % corpora.Count];
                    Attempt a = PlayLevel(legs, h, mech, r, cs, null);
                    if (a.clear) clears++;
                    else
                    {
                        parts[a.deathPart] = (parts.TryGetValue(a.deathPart, out int n) ? n : 0) + 1;
                        int pos = legs[a.deathIndex].f.cyclePos;
                        perPos[pos] = (perPos.TryGetValue(pos, out int q) ? q : 0) + 1;
                    }
                }
                int target = p.planner ? TargetPlanner[level - 1] : p.name == "Average" ? TargetAverage[level - 1] : -1;
                float pct = 100f * clears / runs;
                row.Append($" {pct:0}%{(target >= 0 ? $" ({target})" : "")} |");
                if (perPos.Count > 0 && (p.name == "Average" || p.planner))
                {
                    int deaths = runs - clears, maxPos = 0, maxN = 0;
                    foreach (var kv in perPos) if (kv.Value > maxN) { maxN = kv.Value; maxPos = kv.Key; }
                    float mean = deaths / Mathf.Max(1f, corpora[0].Count);
                    float ratio = maxN / Mathf.Max(0.01f, mean);
                    spikes.AppendLine($"- {p.name} L{level}: worst leg pos {maxPos} {maxN} deaths = {ratio:0.0}x the mean ({mean:0.00}/leg){(ratio > 2f ? "  **> 2x**" : "")}");
                }
            }
            sb.AppendLine(row.ToString());
            deathParts[p.name] = parts;
        }
        sb.AppendLine();
        sb.AppendLine("(target in brackets: Average and Expert-planner base targets)");
        sb.AppendLine();
        sb.AppendLine("### Where the bots die (all levels, adaptive off)");
        sb.AppendLine("| Profile | release | build | peak | glide | finale | exhale | door | runwayOut |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        string[] keys = { "release", "build", "peak", "glide", "finale", "exhale", "door", "runwayOut" };
        foreach (var kv in deathParts)
        {
            int tot = 0; foreach (var v in kv.Value.Values) tot += v;
            var row = new StringBuilder("| " + kv.Key + " |");
            foreach (string k in keys) row.Append($" {(kv.Value.TryGetValue(k, out int n) ? 100f * n / Mathf.Max(1, tot) : 0f):0}% |");
            sb.AppendLine(row.ToString());
        }
        sb.AppendLine();
        sb.AppendLine("### Death concentration per leg (no leg should exceed 2x its level's mean)");
        sb.Append(spikes);
        return sb.ToString();
    }

    // ---------------------------------------------------------------------- B: adaptive attempts-to-clear

    public static string AdaptiveAttempts(List<Bucket> bs, int players)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Adaptive ON: attempts from each checkpoint before its first clear (median of " + players + " simulated players)");
        sb.AppendLine("| Profile | L1 | L2 | L3 | L4 | L5 | L6 | L7 | L8 | L9 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        var tgt = new StringBuilder("| target |");
        for (int l = 1; l <= 9; l++) { var t = tuning.TargetFor(l); tgt.Append($" {t.x}-{t.y} |"); }
        sb.AppendLine(tgt.ToString());
        foreach (var p in Profiles)
        {
            var all = new List<int>[10];
            for (int l = 1; l <= 9; l++) all[l] = new List<int>();
            for (int pl = 0; pl < players; pl++)
            {
                var model = new SkillModel311(tuning, SkillSave311.New(tuning.startRating));
                var r = new System.Random(4000 + pl * 17 + p.name.Length);
                var h = HumanFor(p);
                for (int level = 1; level <= 9; level++)
                {
                    int tries = 0;
                    while (tries < 400)
                    {
                        tries++;
                        model.NoteRunStart();
                        model.NoteAttempt(level);
                        TierMods311 m = model.ModsFor(level, out float tier, out _);
                        Bucket b = Nearest(bs, tier, m.speed < 0.999f);
                        var corpora = b.levels[level];
                        Attempt a = PlayLevel(corpora[r.Next(0, corpora.Count)], h, MechFor(b, level), r, gen.cubeSize, model);
                        if (a.clear) { model.NoteClear(level); break; }
                        model.NoteDeath(level);
                    }
                    all[level].Add(tries);
                }
            }
            var row = new StringBuilder("| " + p.name + " |");
            for (int l = 1; l <= 9; l++) { all[l].Sort(); row.Append($" {all[l][all[l].Count / 2]} |"); }
            sb.AppendLine(row.ToString());
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------------- C: multi-week

    public static string MultiWeek(List<Bucket> bs, int players, int maxDays)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Multi-week, adaptive ON: day each level is first cleared (median of {players} players; 10-15 min a day; reaction -1% per 10 attempts)");
        sb.AppendLine("| Profile | L1 | L2 | L3 | L4 | L5 | L6 | L7 | L8 | L9 = Freefall | target |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        string[] targets = { "<= 35", "18-28", "14-21", "10-14", "7-10" };
        int pi = 0;
        foreach (var p in Profiles)
        {
            var days = new List<int>[10];
            for (int l = 1; l <= 9; l++) days[l] = new List<int>();
            for (int pl = 0; pl < players; pl++)
            {
                var model = new SkillModel311(tuning, SkillSave311.New(tuning.startRating));
                var r = new System.Random(9000 + pl * 31 + p.name.Length * 3);
                int level = 1, attempts = 0;
                var cleared = new int[10];
                for (int day = 1; day <= maxDays && level <= 9; day++)
                {
                    float budget = 600f + (float)r.NextDouble() * 300f;
                    while (budget > 0f && level <= 9)
                    {
                        float improve = Mathf.Pow(0.99f, attempts / 10);
                        var h = HumanFor(p, improve);
                        if (p.planner) h.rtSd = p.sd;   // the planner's jitter does not improve
                        model.NoteRunStart();
                        model.NoteAttempt(level);
                        TierMods311 m = model.ModsFor(level, out float tier, out _);
                        Bucket b = Nearest(bs, tier, m.speed < 0.999f);
                        var corpora = b.levels[level];
                        Attempt a = PlayLevel(corpora[r.Next(0, corpora.Count)], h, MechFor(b, level), r, gen.cubeSize, model);
                        attempts++;
                        budget -= a.seconds + 6f;
                        if (a.clear) { model.NoteClear(level); cleared[level] = day; level++; }
                        else model.NoteDeath(level);
                    }
                }
                for (int l = 1; l <= 9; l++) days[l].Add(cleared[l] > 0 ? cleared[l] : maxDays + 1);
            }
            var row = new StringBuilder("| " + p.name + " |");
            for (int l = 1; l <= 9; l++)
            {
                days[l].Sort();
                int med = days[l][days[l].Count / 2];
                row.Append(med > maxDays ? $" >{maxDays} |" : $" {med} |");
            }
            row.Append(" " + targets[pi++] + " |");
            sb.AppendLine(row.ToString());
        }
        return sb.ToString();
    }

    public static string Dir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "DifficultyAudit_3_11", "phase6");

    public static void Write(string name, string text)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, name), text);
    }
}
