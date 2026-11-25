using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Scene 3.10 Difficulty:
///   Build Asset + Assign   writes Assets/Difficulty/Scene3_10_CheckpointDifficulty.asset with every level serialized
///                          explicitly (the base fairness fields are copied from the scene 3.7 asset, which is only READ),
///                          then assigns it on scene 3.10's DynamicPathGenerator. No other scene or asset is touched.
///   Print Table            speed curve, per-leg windows and floor checks for 3.10 -> Library/Scene310_difficulty_table.txt
/// </summary>
public static class Scene310DifficultyBuilder
{
    const string AssetPath = "Assets/Difficulty/Scene3_10_CheckpointDifficulty.asset";
    const string SourcePath = "Assets/Difficulty/Scene3_7_CheckpointDifficulty.asset";   // read only
    const string ScenePath = "Assets/MainScene prop camera 3.10.unity";

    static WaveShape Shape(int release, int build, int peak, int firstHazard, PeakKind kind, int peakBoostLegs,
                           Vector2Int[] buildBoosts, int[] glides, float glideScale, int featured, bool families,
                           Vector2Int onboarding = default)
    {
        return new WaveShape
        {
            releaseEnd = release, buildEnd = build, peakEnd = peak, firstHazardLeg = firstHazard,
            onboardingBoost = onboarding, peakKind = kind, peakBoostLegs = peakBoostLegs,
            buildBoosts = buildBoosts ?? new Vector2Int[0], clusterBridgeLegs = new int[0],
            glideLegs = glides ?? new int[0], glideLengthScale = glideScale, featured = featured, families = families,
        };
    }

    static LevelTuning310 L(string name, string intent, int length, float s0, float s1, int hold, float curve, int blend,
                            Vector2 d0, Vector2 d1, float t1a, float t1b, float b, float p, int cap, WaveShape shape,
                            float decoy, int family = -1, int[] runways = null, float coin = 1f, int power = -1)
    {
        return new LevelTuning310
        {
            name = name, intent = intent, length = length,
            speedStart = s0, speedEnd = s1, speedHoldLegs = hold, speedCurve = curve, blendLegs = blend,
            durStart = d0, durEnd = d1,
            tHuman1 = new Vector2(t1a, t1b),
            tHuman2 = new Vector2(Mathf.Max(0.47f, t1a + 0.09f), Mathf.Max(0.47f, t1b + 0.09f)),
            buildAvg = b, peakBudget = p, legCostCap = cap, shape = shape,
            decoyChance = decoy, forcedFamily = family, runwayLegs = runways ?? new int[0], coinScale = coin, powerUpFromLeg = power,
        };
    }

    static List<LevelTuning310> Levels()
    {
        var none = PeakKind.None;
        return new List<LevelTuning310>
        {
            L("Start", "Learn to read. Hold 15 u/s for 3 legs, then a gentle climb to 21. One opening boost, two glides, no decoys, no runways.",
              37, 17f, 23f, 3, 1.3f, 0, new Vector2(1.00f, 1.30f), new Vector2(0.85f, 1.15f), 0.75f, 0.55f, 1.6f, 3.2f, 5,
              Shape(3, 29, 34, 5, none, 0, null, new[] { 9, 18 }, 1.45f, 5, false, new Vector2Int(11, 5)), 0f, runways: new[] { 21 }),

            L("Crimson", "Rhythm. Tight corners, alternating directions (staircase), decoy forks, three glides.",
              41, 20f, 24f, 0, 1f, 4, new Vector2(0.74f, 0.98f), new Vector2(0.72f, 0.96f), 0.52f, 0.47f, 2.2f, 3.8f, 5,
              Shape(3, 33, 38, 5, none, 0, null, new[] { 9, 18, 27 }, 1.4f, 1, false), 0.30f, family: 0, runways: new[] { 15 }),

            L("Toon", "Sprint. Long clean legs, few hazards, two short boosts. Fast, not hard.",
              29, 23f, 27f, 0, 1f, 4, new Vector2(1.05f, 1.40f), new Vector2(1.00f, 1.30f), 0.50f, 0.47f, 1.4f, 3.0f, 5,
              Shape(3, 24, 26, 8, none, 0, new[] { new Vector2Int(5, 3), new Vector2Int(14, 4) }, new[] { 12 }, 1.3f, 5, false), 0.10f, runways: new[] { 10 }),

            L("Pixel", "Maze. Slow on purpose (19.5 -> 21.5, 6-leg blend), decoy forks, medium legs for 2-roll turns, no boost, four glides.",
              45, 21.5f, 23.5f, 0, 1f, 6, new Vector2(0.95f, 1.25f), new Vector2(0.90f, 1.20f), 0.48f, 0.44f, 2.6f, 4.0f, 6,
              Shape(3, 37, 42, 5, none, 0, null, new[] { 8, 16, 24, 32 }, 1.35f, 1, false), 0.50f, runways: new[] { 18 }),

            L("Storybook", "First exam. Remix families, one guaranteed obstacle runway at ~60%, a peak boost.",
              45, 22f, 26f, 0, 1f, 4, new Vector2(0.72f, 0.98f), new Vector2(0.68f, 0.94f), 0.46f, 0.42f, 2.5f, 4.2f, 6,
              Shape(3, 37, 42, 5, PeakKind.Boost, 5, null, new[] { 7, 17, 27 }, 1.35f, -1, true), 0.25f, runways: new[] { 24 }),

            L("Blossom", "Reward breather. Low budget, doubled coins, a guaranteed power-up, three boosts, no runway. The runner feels powerful.",
              33, 22.5f, 24.5f, 0, 1f, 4, new Vector2(0.85f, 1.15f), new Vector2(0.82f, 1.10f), 0.48f, 0.46f, 1.5f, 2.8f, 4,
              Shape(3, 25, 30, 8, PeakKind.Boost, 4, new[] { new Vector2Int(4, 3), new Vector2Int(11, 3) }, new[] { 8, 16 }, 1.3f, 5, false),
              0f, runways: new[] { 14 }, coin: 2f, power: 10),

            L("Brick Meadow", "Precision. Two obstacle runways (~35% and ~75%) with 2-roll stations, a breather right after each.",
              41, 23f, 25.5f, 0, 1f, 4, new Vector2(0.85f, 1.15f), new Vector2(0.80f, 1.10f), 0.46f, 0.42f, 2.4f, 4.0f, 6,
              Shape(3, 33, 38, 5, none, 0, null, new[] { 20 }, 1.35f, 1, false), 0.15f, runways: new[] { 13, 27 }),

            L("Raspberry", "Sprint II. Toon's recipe, faster.",
              31, 25f, 28.5f, 0, 1f, 4, new Vector2(1.05f, 1.40f), new Vector2(1.00f, 1.30f), 0.48f, 0.45f, 1.6f, 3.2f, 5,
              Shape(3, 26, 28, 7, none, 0, new[] { new Vector2Int(5, 3), new Vector2Int(14, 4) }, new[] { 11 }, 1.3f, 5, false), 0.10f, runways: new[] { 10 }),

            L("Jade Moon", "Gauntlet. Remix + decoys + one runway, two clear breathers (~30% / ~60%), the hardest 8 legs, two easy legs into the portal.",
              49, 24.5f, 27.5f, 0, 1f, 4, new Vector2(0.72f, 0.98f), new Vector2(0.68f, 0.94f), 0.45f, 0.40f, 2.7f, 4.4f, 6,
              Shape(3, 38, 46, 5, none, 0, null, new[] { 7, 13, 20, 26, 32 }, 1.5f, -1, true), 0.40f, runways: new[] { 17 }),
        };
    }

    [MenuItem("Tools/Scene 3.10 Difficulty/Build Asset + Assign")]
    public static void BuildAndAssign()
    {
        var src = AssetDatabase.LoadAssetAtPath<DifficultyProfile>(SourcePath);
        var asset = AssetDatabase.LoadAssetAtPath<Scene310DifficultyProfile>(AssetPath);
        bool isNew = asset == null;
        if (isNew) asset = ScriptableObject.CreateInstance<Scene310DifficultyProfile>();
        if (src != null) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(src), asset);   // base fairness numbers (read only)

        // Own fields are set AFTER the copy: the 3.7 asset has portalLegs / stages of its own.
        asset.portalLegs = new[] { 37, 78, 107, 152, 197, 230, 271, 302, 351 };
        asset.portalLegsRepeat = 40;
        asset.levels = Levels();
        asset.endlessIdentity = new[] { 1, 2, 3, 6, 4 };       // Crimson, Toon, Pixel, Brick Meadow, Storybook
        asset.endlessSpeedStart = 25f;
        asset.endlessSpeedEnd = 28f;
        asset.endlessSpeedPerCycle = 0.4f;
        asset.speedCeiling = 31f;
        asset.endlessBudgetPerCycle = 0.1f;
        asset.endlessPeakCap = 4.7f;
        asset.endlessGlideEvery = 7;
        asset.globalFloor1 = 0.40f;
        asset.globalFloor2 = 0.47f;
        asset.teachOpeningDecoy = false;
        asset.adaptiveEasing = true;
        asset.name = "Scene3_10_CheckpointDifficulty";

        if (isNew) AssetDatabase.CreateAsset(asset, AssetPath);
        else EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();

        // The menu has no generator: let 3.10's checkpoint set show the real portal legs of this schedule.
        var set = AssetDatabase.LoadAssetAtPath<CheckpointSet>("Assets/FlowerCheckpoint/CheckpointSet_3_10.asset");
        if (set != null) { set.scheduleProfile = asset; EditorUtility.SetDirty(set); AssetDatabase.SaveAssets(); }

        // Assign on scene 3.10 only. If that scene is already the open scene, set it live; otherwise the one-line
        // difficultyProfile guid in the scene file is swapped by hand (no scene is opened or saved from here).
        var open = EditorSceneManager.GetActiveScene();
        if (open.path == ScenePath)
        {
            var gen = Object.FindFirstObjectByType<DynamicPathGenerator>();
            if (gen != null)
            {
                gen.difficultyProfile = asset;
                EditorUtility.SetDirty(gen);
                EditorSceneManager.MarkSceneDirty(open);
                EditorSceneManager.SaveScene(open);
            }
        }
        Debug.Log("[Scene310] asset " + (isNew ? "created" : "updated") + " guid " + AssetDatabase.AssetPathToGUID(AssetPath));
    }

    [MenuItem("Tools/Scene 3.10 Difficulty/Print Table")]
    public static void PrintTable()
    {
        var p = AssetDatabase.LoadAssetAtPath<Scene310DifficultyProfile>(AssetPath);
        if (p == null) { Debug.LogError("[Scene310] build the asset first"); return; }
        var sb = new StringBuilder();
        int legs = p.PortalLeg(p.portalLegs.Length + 3, 50);
        int bad = 0;
        float prevEnd = 0f;
        int level = -1, levelStart = 0;
        sb.AppendLine("leg lvl pos  speed  dur(min-max)  T1    T2   floor1 floor2  minCubes1 minCubes2  phase     tags");
        for (int leg = 0; leg < legs; leg++)
        {
            int k = 0;
            while (p.PortalLeg(k + 1, 50) <= leg) k++;
            int start = p.PortalLeg(k, 50);
            int pos = leg - start;
            WaveShape S = p.ShapeForCycle(k);
            float v = p.SpeedForLeg(leg);
            Vector2 d = p.DurationRange(leg);
            float t1 = p.THuman(1, leg, false), t2 = p.THuman(2, leg, false);
            float f1 = p.MinLegSeconds(1, leg, false), f2 = p.MinLegSeconds(2, leg, false);
            string phase = pos <= S.releaseEnd ? "release" : pos <= S.buildEnd ? "build" : pos <= S.peakEnd ? "PEAK" : "cooldown";
            var tags = new StringBuilder();
            if (S.glideLegs != null && System.Array.IndexOf(S.glideLegs, pos) >= 0 && phase == "build") tags.Append("glide ");
            if (k == 0 && pos >= S.onboardingBoost.x && pos < S.onboardingBoost.x + S.onboardingBoost.y) tags.Append("boost ");
            if (S.buildBoosts != null) foreach (Vector2Int b in S.buildBoosts) if (pos >= b.x && pos < b.x + b.y) tags.Append("boost ");
            if (S.peakKind == PeakKind.Boost && phase == "PEAK" && pos >= S.buildEnd + 1 && pos < S.buildEnd + 1 + S.peakBoostLegs) tags.Append("boost ");
            int[] rw = p.RunwayPlan(k);
            if (rw != null && System.Array.IndexOf(rw, pos) >= 0) tags.Append("RUNWAY ");
            if (pos == 0) tags.Append("door-out ");
            if (t1 < p.globalFloor1 - 1e-4f || t2 < p.globalFloor2 - 1e-4f) { tags.Append("BELOW-GLOBAL-FLOOR "); bad++; }
            if (d.x < f1 - 1e-4f) tags.Append("shortest-leg<1roll-floor(lifted) ");
            if (k != level)
            {
                if (level >= 0) sb.AppendLine($"    -- level {level + 1} end speed {prevEnd:F2}");
                level = k; levelStart = start;
                float dip = k > 0 ? (v - prevEnd) / prevEnd * 100f : 0f;
                sb.AppendLine($"== level {k + 1} (legs {start}-{p.PortalLeg(k + 1, 50) - 1}, {p.PortalLeg(k + 1, 50) - start} legs) start speed {v:F2} ({dip:+0.0;-0.0}% vs previous end)");
            }
            prevEnd = v;
            int c1 = Mathf.CeilToInt(f1 * v + 1.1f - 0.0001f), c2 = Mathf.CeilToInt(f2 * v + 1.1f - 0.0001f);
            sb.AppendLine($"{leg,3} {k + 1,2} {pos,3} {v,6:F2}  {d.x:F2}-{d.y:F2}   {t1:F2}  {t2:F2}  {f1:F2}  {f2:F2}    {c1,3}     {c2,3}      {phase,-8}  {tags}");
        }
        sb.AppendLine($"    -- level {level + 1} end speed {prevEnd:F2}");
        sb.AppendLine($"legs below the global floor: {bad}");
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "Scene310_difficulty_table.txt"));
        File.WriteAllText(path, sb.ToString());
        Debug.Log("[Scene310] table -> " + path);
    }
}
