using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Dev-only (never part of a scene): a HumanSimPlayer soak for `gameSeconds` of game time. Every run's path is laid from
// seed + run index (DynamicPathGenerator.SeedRuns), so two scenes see the same paths in the same order. Runs are ended at
// scoreCap (test runs stay under 50). Power diamonds off. Writes the scores to `path` and stops Play.
public class LeftRightSoak : MonoBehaviour
{
    public int seed = 777;
    public float gameSeconds = 300f;
    public int scoreCap = 49;
    public HumanSimPlayer.Model model = HumanSimPlayer.Model.Average;
    public string path = "";
    public bool Done;

    CubeRailRunner runner; DynamicPathGenerator gen; HumanSimPlayer sim;
    float elapsed; int run; bool sawOver, started; int capped;
    readonly List<int> scores = new List<int>();
    readonly Dictionary<string, int> causes = new Dictionary<string, int>();
    readonly StringBuilder deaths = new StringBuilder();

    void Start()
    {
        runner = FindFirstObjectByType<CubeRailRunner>();
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        gen.powerDiamonds = false;
        gen.SeedRuns(seed);
        if (string.IsNullOrEmpty(path)) path = Application.dataPath + "/leftRightSoak.txt";
    }

    void Update()
    {
        if (Done || runner == null) return;
        if (!started)
        {
            if (runner.IsRunning || runner.IsWaitingToStart) { runner.Restart(); started = true; }
            return;
        }
        if (sim == null)
        {
            if (!runner.IsRunning) return;
            sim = gameObject.AddComponent<HumanSimPlayer>();
            sim.model = model; sim.seed = seed; sim.runBatch = true; sim.batchRuns = 100000; sim.batchTimeScale = 1f;
        }
        elapsed += Time.deltaTime;
        if (runner.IsGameOver)
        {
            if (!sawOver)
            {
                sawOver = true;
                int s = gen.Score;
                scores.Add(s); if (s >= scoreCap) capped++;
                string c = s >= scoreCap ? "cap" : runner.LastDeathCause;
                causes[c] = causes.TryGetValue(c, out int n) ? n + 1 : 1;
                deaths.Append(s).Append(':').Append(c).Append(' ');
                run++;
                gen.SeedRuns(seed + run);   // the sim restarts 1.2 s later on this seed
            }
        }
        else sawOver = false;
        if (runner.IsRunning && gen.Score >= scoreCap) runner.HitObstacle();
        if (elapsed >= gameSeconds) Finish();
    }

    void Finish()
    {
        Done = true;
        float mean = 0f; foreach (int s in scores) mean += s; mean = scores.Count > 0 ? mean / scores.Count : 0f;
        var sorted = new List<int>(scores); sorted.Sort();
        var sb = new StringBuilder();
        sb.AppendLine("scene " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " soak: HumanSimPlayer " + model + ", seed " + seed
            + ", " + gameSeconds + " s game time, cap " + scoreCap + ", toggles A/B/C/D = " + runner.sideWallRunnerRule + "/" + runner.keepSpinOnSideWall + "/" + runner.settleRederiveClearFaces + "/" + runner.useDrawnViewForMapping);
        sb.AppendLine("finished runs " + scores.Count + ", mean score " + mean.ToString("0.00") + ", median " + (sorted.Count > 0 ? sorted[sorted.Count / 2] : 0) + ", runs reaching the cap " + capped
            + ", rescues " + (sim != null ? sim.rescues : 0) + ", HEADING rule reached " + runner.HeadingRuleHits + ", settle re-derives " + runner.SettleRederives + " (changed meaning " + runner.SettleRederiveFlips + ")");
        sb.Append("end causes:"); foreach (var kv in causes) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value); sb.AppendLine();
        sb.AppendLine("score:cause per run: " + deaths);
        System.IO.File.WriteAllText(path, sb.ToString());
        Debug.Log("[LeftRightSoak] " + sb);
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
