using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Scene 3.11 > Build Difficulty Asset: writes the 3.11 production-pass numbers (the "Average" tier) into
/// Assets/Difficulty/Scene3_11_CheckpointDifficulty.asset, switching it to Scene311DifficultyProfile first if it is still
/// a Scene310DifficultyProfile (same file, same GUID, so the scene's reference holds). Re-run after editing the tables.
/// Tools > Scene 3.11 > Print Base Table: the per-level table to DifficultyAudit_3_11/base_table.md.
/// </summary>
public static class Scene311DifficultyBuilder
{
    const string AssetPath = "Assets/Difficulty/Scene3_11_CheckpointDifficulty.asset";
    const string ScriptPath = "Assets/Scene311/Scripts/Scene311DifficultyProfile.cs";

    // DesignKind order: Totem, Pair, Corner, Fence, Drifter, Twin, CornerSlider.
    // Counts are for the WAVES; a finale signature special (L3/L8 boost, L5/L7/L9 runway) comes on top, so
    // L3 2 boosts = 1 + the finale one, L8 3 = 2 + 1, L5 2 runways = 1 + 1, L7 3 = 2 + 1, L9 3 = 2 + 1.
    struct Row
    {
        public string name; public int len; public float v0, v1; public int hold; public float t0, t1;
        public float build, peak; public int cap; public float decoy; public float rescue, reach;
        public FinaleSignature311 sig; public int waves, finale;
        public int boosts, newestMode; public int runways; public Vector2Int stations; public int[] designs; public float gap;
        public Vector2 slider; public int maxSliders; public int[] finaleDesigns; public int slowMos; public float power;
        public float coinScale; public int powerFrom;
    }

    static readonly Row[] Rows =
    {
        new Row { name = "Start",     len = 45,  v0 = 17f,   v1 = 23f,   hold = 3, t0 = 0.72f, t1 = 0.55f, build = 1.6f, peak = 3.2f, cap = 5, decoy = 0f,    rescue = 0.09f, reach = 3.5f,
                  sig = FinaleSignature311.TwoRollPairs,     waves = 2, finale = 20, boosts = 0, newestMode = -1, runways = 0, stations = new Vector2Int(5, 5), designs = new int[7], gap = 1.2f,
                  slider = Vector2.zero, maxSliders = 0, finaleDesigns = new int[0], slowMos = 0, power = 1f, coinScale = 1f, powerFrom = -1 },
        new Row { name = "Crimson",   len = 60,  v0 = 20f,   v1 = 24.5f, hold = 0, t0 = 0.52f, t1 = 0.46f, build = 2.3f, peak = 3.9f, cap = 5, decoy = 0.30f, rescue = 0.08f, reach = 3.0f,
                  sig = FinaleSignature311.Staircase,        waves = 2, finale = 20, boosts = 1, newestMode = 0, runways = 1, stations = new Vector2Int(5, 5), designs = new[] { 6, 4, 0, 0, 0, 0, 0 }, gap = 1.20f,
                  slider = Vector2.zero, maxSliders = 0, finaleDesigns = new int[0], slowMos = 0, power = 1f, coinScale = 1f, powerFrom = -1 },
        new Row { name = "Toon",      len = 70,  v0 = 23f,   v1 = 27.5f, hold = 0, t0 = 0.50f, t1 = 0.45f, build = 1.8f, peak = 3.4f, cap = 5, decoy = 0.10f, rescue = 0.07f, reach = 3.0f,
                  sig = FinaleSignature311.LongBoostExit,    waves = 2, finale = 20, boosts = 1, newestMode = 1, runways = 1, stations = new Vector2Int(6, 6), designs = new[] { 3, 4, 0, 3, 0, 0, 0 }, gap = 1.15f,
                  slider = Vector2.zero, maxSliders = 0, finaleDesigns = new int[0], slowMos = 1, power = 1f, coinScale = 1f, powerFrom = -1 },
        new Row { name = "Pixel",     len = 75,  v0 = 23f,   v1 = 25.5f, hold = 0, t0 = 0.47f, t1 = 0.42f, build = 2.8f, peak = 4.1f, cap = 6, decoy = 0.55f, rescue = 0.06f, reach = 2.5f,
                  sig = FinaleSignature311.DecoyGauntlet,    waves = 3, finale = 25, boosts = 1, newestMode = 1, runways = 2, stations = new Vector2Int(6, 6), designs = new[] { 2, 3, 4, 2, 1, 0, 0 }, gap = 1.15f,
                  slider = new Vector2(0.22f, 0.32f), maxSliders = 1, finaleDesigns = new int[0], slowMos = 1, power = 0.7f, coinScale = 1f, powerFrom = -1 },
        new Row { name = "Storybook", len = 80,  v0 = 23.5f, v1 = 27f,   hold = 0, t0 = 0.45f, t1 = 0.41f, build = 2.7f, peak = 4.3f, cap = 6, decoy = 0.30f, rescue = 0.05f, reach = 2.0f,
                  sig = FinaleSignature311.RunwaySlowBurst,  waves = 3, finale = 28, boosts = 2, newestMode = 2, runways = 1, stations = new Vector2Int(7, 7), designs = new[] { 0, 1, 3, 4, 2, 0, 0 }, gap = 1.10f,
                  slider = new Vector2(0.25f, 0.35f), maxSliders = 99, finaleDesigns = new[] { 3, 4 }, slowMos = 0, power = 0.7f, coinScale = 1f, powerFrom = -1 },
        new Row { name = "Blossom",   len = 65,  v0 = 24f,   v1 = 26.5f, hold = 0, t0 = 0.47f, t1 = 0.43f, build = 1.7f, peak = 3.2f, cap = 5, decoy = 0.10f, rescue = 0.06f, reach = 2.5f,
                  sig = FinaleSignature311.Mild,             waves = 3, finale = 25, boosts = 2, newestMode = 2, runways = 1, stations = new Vector2Int(6, 6), designs = new[] { 0, 2, 3, 2, 1, 3, 0 }, gap = 1.15f,
                  slider = new Vector2(0.25f, 0.32f), maxSliders = 99, finaleDesigns = new int[0], slowMos = 1, power = 0.7f, coinScale = 2f, powerFrom = 10 },
        new Row { name = "Brick Meadow", len = 85, v0 = 24.5f, v1 = 28f, hold = 0, t0 = 0.44f, t1 = 0.40f, build = 2.8f, peak = 4.3f, cap = 6, decoy = 0.20f, rescue = 0.04f, reach = 1.5f,
                  sig = FinaleSignature311.SliderRunwayBurst, waves = 3, finale = 28, boosts = 2, newestMode = 3, runways = 2, stations = new Vector2Int(7, 7), designs = new[] { 0, 2, 3, 2, 1, 2, 3 }, gap = 1.10f,
                  slider = new Vector2(0.30f, 0.42f), maxSliders = 99, finaleDesigns = new[] { 6 }, slowMos = 1, power = 0.5f, coinScale = 1f, powerFrom = -1 },
        new Row { name = "Sketchbook", len = 90, v0 = 25.5f, v1 = 29.5f, hold = 0, t0 = 0.44f, t1 = 0.40f, build = 2.2f, peak = 3.8f, cap = 5, decoy = 0.20f, rescue = 0.04f, reach = 1.5f,
                  sig = FinaleSignature311.LongBoostDecoys,  waves = 3, finale = 30, boosts = 2, newestMode = 3, runways = 2, stations = new Vector2Int(8, 8), designs = new[] { 1, 2, 3, 2, 2, 2, 3 }, gap = 1.05f,
                  slider = new Vector2(0.30f, 0.42f), maxSliders = 99, finaleDesigns = new int[0], slowMos = 1, power = 0.5f, coinScale = 1f, powerFrom = -1 },
        new Row { name = "Rogue Planet", len = 100, v0 = 26f, v1 = 29.5f, hold = 0, t0 = 0.43f, t1 = 0.40f, build = 3.0f, peak = 4.6f, cap = 6, decoy = 0.40f, rescue = 0.03f, reach = 1.5f,
                  sig = FinaleSignature311.MixAll,           waves = 3, finale = 30, boosts = 3, newestMode = 3, runways = 2, stations = new Vector2Int(8, 9), designs = new[] { 1, 2, 3, 2, 2, 2, 3 }, gap = 1.05f,
                  slider = new Vector2(0.30f, 0.45f), maxSliders = 99, finaleDesigns = new int[0], slowMos = 0, power = 0.5f, coinScale = 1f, powerFrom = -1 },
    };

    [MenuItem("Tools/Scene 3.11/Build Difficulty Asset")]
    public static void Build()
    {
        EnsureClass();
        var p = AssetDatabase.LoadAssetAtPath<Scene311DifficultyProfile>(AssetPath);
        if (p == null) { Debug.LogError("[Scene311Builder] could not load " + AssetPath + " as Scene311DifficultyProfile."); return; }
        Undo.RecordObject(p, "Build 3.11 difficulty");

        var portal = new int[Rows.Length];
        int acc = 0;
        for (int i = 0; i < Rows.Length; i++) { acc += Rows[i].len; portal[i] = acc; }
        p.portalLegs = portal;
        p.portalLegsRepeat = 60;
        p.adaptiveEasing = false;          // the 3.11 skill model replaces the 3.10 death-count easing
        p.teachOpeningDecoy = false;
        p.globalFloor1 = 0.40f;
        p.globalFloor2 = 0.47f;
        p.speedCeiling = 31f;
        p.endlessIdentity = new[] { 1, 2, 3, 4, 6, 7, 8 };   // freefall rotates L2 L3 L4 L5 L7 L8 L9
        p.endlessSpeedStart = 26.5f;
        p.endlessSpeedEnd = 30.5f;
        p.endlessSpeedPerCycle = 0.3f;
        p.endlessBudgetPerCycle = 0.1f;
        p.endlessPeakCap = 4.8f;
        p.endlessGlideEvery = 11;
        p.slowMoSpeedBoost = 1f;           // slow-motion tiny legs exactly at the fairness floor (3.10 default 1.2 ran them 20% under)

        while (p.levels.Count < Rows.Length) p.levels.Add(new LevelTuning310());
        if (p.levels.Count > Rows.Length) p.levels.RemoveRange(Rows.Length, p.levels.Count - Rows.Length);
        p.levels311 = new List<LevelExtra311>();

        for (int i = 0; i < Rows.Length; i++)
        {
            Row r = Rows[i];
            LevelTuning310 t = p.levels[i];
            t.name = r.name;
            t.length = r.len;
            t.speedStart = r.v0; t.speedEnd = r.v1; t.speedHoldLegs = r.hold;
            t.speedCurve = 1.4f;                         // ease-in: gentle start, steeper finish
            t.blendLegs = i == 0 ? 0 : 5;                // 5-leg smoothstep down from the last level's end speed
            t.tHuman1 = new Vector2(r.t0, r.t1);
            t.tHuman2 = new Vector2(r.t0 + p.twoRollAdd, r.t1 + p.twoRollAdd);
            t.buildAvg = r.build; t.peakBudget = r.peak; t.legCostCap = r.cap;
            t.decoyChance = r.decoy;
            t.forcedFamily = -1;
            t.runwayLegs = new int[0];                   // the 3.11 specials plan places runways
            t.coinScale = r.coinScale;
            t.powerUpFromLeg = r.powerFrom;
            WaveShape s = t.shape;
            s.releaseEnd = p.releaseLegs;
            s.firstHazardLeg = p.releaseLegs + 1;
            s.buildEnd = r.len - 3; s.peakEnd = r.len - 2;
            s.onboardingBoost = Vector2Int.zero;
            s.peakKind = PeakKind.None; s.peakBoostLegs = 0;
            s.buildBoosts = new Vector2Int[0];
            s.clusterBridgeLegs = new int[0];
            s.glideLegs = new int[0];                    // the 3.11 plan places glides
            s.glideLengthScale = 1.35f;
            s.featured = 5;                              // no featured hazard: the plan's own ramps
            s.families = false;
            t.shape = s;
            t.intent = Intent(r);

            p.levels311.Add(new LevelExtra311
            {
                waves = r.waves, finaleLegs = r.finale, signature = r.sig,
                rescueWindow = r.rescue, rescueReachCubes = r.reach,
                boosts = r.boosts, newestBoostMode = r.newestMode,
                runways = r.runways, stations = r.stations, designWeights = r.designs, gapFactor = r.gap, gapJitter = 0.25f,
                sliderSpeed = r.slider, maxSliders = r.maxSliders, finaleDesigns = r.finaleDesigns,
                slowMos = r.slowMos, powerFrequency = r.power,
            });
        }
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        Debug.Log("[Scene311Builder] built " + AssetPath + " (" + Rows.Length + " levels, portals " + string.Join(",", portal) + ")");
    }

    static string Intent(Row r)
    {
        switch (r.sig)
        {
            case FinaleSignature311.TwoRollPairs: return "Learn to read. No specials. Finale: the first 2-roll pairs.";
            case FinaleSignature311.Staircase: return "Rhythm. One classic boost, one runway. Finale: a staircase of short alternating legs.";
            case FinaleSignature311.LongBoostExit: return "Speed. Long boosts unlocked. Finale: a long boost straight into a 2-roll exit.";
            case FinaleSignature311.DecoyGauntlet: return "Reading forks. Finale: decoy gauntlet, every decoy followed by a 2-roll.";
            case FinaleSignature311.RunwaySlowBurst: return "First exam. Broken-rhythm boosts. Finale: runway (Fence + Drifter) -> slow motion -> short-leg burst.";
            case FinaleSignature311.Mild: return "Breather level. Coins x2, a guaranteed power-up, Twin sliders introduced, mild finale.";
            case FinaleSignature311.SliderRunwayBurst: return "Boost exits unlocked. Finale: CornerSlider runway into a short-leg burst.";
            case FinaleSignature311.LongBoostDecoys: return "Finale: a long boost (x1.5) into decoys.";
            default: return "Final exam. Finale: 30 legs mixing every hazard, one double spike.";
        }
    }

    // Same file, same GUID: only the script reference changes, so every field it already had is kept.
    static void EnsureClass()
    {
        var existing = AssetDatabase.LoadMainAssetAtPath(AssetPath);
        if (existing is Scene311DifficultyProfile) return;
        string guid = AssetDatabase.AssetPathToGUID(ScriptPath);
        if (string.IsNullOrEmpty(guid)) { Debug.LogError("[Scene311Builder] no GUID for " + ScriptPath); return; }
        string full = Path.Combine(Path.GetDirectoryName(Application.dataPath), AssetPath);
        string text = File.ReadAllText(full);
        int at = text.IndexOf("m_Script: {fileID: 11500000, guid: ", System.StringComparison.Ordinal);
        if (at < 0) { Debug.LogError("[Scene311Builder] no m_Script line in " + AssetPath); return; }
        int end = text.IndexOf(", type: 3}", at, System.StringComparison.Ordinal);
        text = text.Substring(0, at) + "m_Script: {fileID: 11500000, guid: " + guid + text.Substring(end);
        text = text.Replace("m_EditorClassIdentifier: Assembly-CSharp::Scene310DifficultyProfile", "m_EditorClassIdentifier: Assembly-CSharp::Scene311DifficultyProfile");
        File.WriteAllText(full, text);
        AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
        Debug.Log("[Scene311Builder] " + AssetPath + " is now a Scene311DifficultyProfile.");
    }

    [MenuItem("Tools/Scene 3.11/Print Base Table")]
    public static void PrintTable()
    {
        var p = AssetDatabase.LoadAssetAtPath<Scene311DifficultyProfile>(AssetPath);
        if (p == null) return;
        var sb = new StringBuilder();
        sb.AppendLine("| L | name | legs | door | speed | T_human 1-roll | 2-roll | build/peak, cap | decoy | rescue | boosts (newest) | runways x stations | gap | sliders | slow-mo | power | waves | finale | signature |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        for (int i = 0; i < p.levels.Count; i++)
        {
            LevelTuning310 t = p.levels[i];
            LevelExtra311 x = p.levels311[i];
            sb.AppendLine($"| {i + 1} | {t.name} | {t.length} | {p.portalLegs[i]} | {t.speedStart}->{t.speedEnd} | {t.tHuman1.x:0.00}->{t.tHuman1.y:0.00} | {t.tHuman2.x:0.00}->{t.tHuman2.y:0.00} | {t.buildAvg}/{t.peakBudget}, {t.legCostCap} | {t.decoyChance:0.00} | {x.rescueWindow:0.00}s/{x.rescueReachCubes} | {x.boosts} (B{x.newestBoostMode + 1}) | {x.runways} x {x.stations.x}-{x.stations.y} | x{x.gapFactor:0.00} | {(x.sliderSpeed.y > 0 ? $"{x.sliderSpeed.x * 100:0}-{x.sliderSpeed.y * 100:0}%" : "-")} | {x.slowMos} | x{x.powerFrequency} | {x.waves} | {x.finaleLegs} | {x.signature} |");
        }
        sb.AppendLine($"| FF | Freefall | {p.portalLegsRepeat}/cycle | +{p.portalLegsRepeat} | {p.endlessSpeedStart}->{p.endlessSpeedEnd} +{p.endlessSpeedPerCycle}/cycle (cap {p.speedCeiling}) | {p.freefallTHuman1.x:0.00}->{p.freefallTHuman1.y:0.00}, {p.freefallTHumanPerCycle}/cycle (floor {p.absoluteFloor1}) | +{p.twoRollAdd} | identity +{p.endlessBudgetPerCycle}/cycle (peak cap {p.endlessPeakCap}) | identity | {p.freefallRescue}s | {p.freefallBoosts} | {p.freefallRunways} | identity | identity | {p.freefallSlowMos} | identity | {p.freefallWaves} | {p.freefallFinale} | identity |");
        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "DifficultyAudit_3_11");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "base_table.md"), sb.ToString());
        Debug.Log("[Scene311Builder] table -> DifficultyAudit_3_11/base_table.md");
    }
}
