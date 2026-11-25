#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dev only (never in a scene): drives a short bot run in Play mode for the toon checkpoint work and takes screenshots.
/// Call <see cref="Run"/> from a RunCommand once Play mode is on. It waits for the scene to settle, switches the save to a
/// test file, removes the pause controller (focus loss pauses the game), starts the run, turns the AutoPlayer bot on and
/// captures a screenshot at each requested time (seconds after the run starts) or score. The run is stopped by killing the
/// runner once <see cref="scoreCap"/> is reached, or when the last shot is taken. Results: <see cref="LogPath"/>.
/// </summary>
public static class ToonTestHarness
{
    public static string OutDir = "";
    public static string LogPath => Path.Combine(OutDir, "harness_log.txt");
    public static int scoreCap = 58;
    public static int superSize = 2;

    static readonly List<float> shotTimes = new List<float>();
    static readonly List<int> shotScores = new List<int>();
    static readonly List<string> log = new List<string>();
    static int frame;
    static float runStart = -1f;
    static bool started, saveSwitched, done;
    static string prefix = "shot";
    static int waitFrames = 60;
    public static System.Action<int> OnPortal;
    static int lastPortals = -1;
    static float nextStat;
    static int statSamples, statSum, statMax, shrinkInView;
    static readonly Dictionary<Transform, float> lastScale = new Dictionary<Transform, float>();
    static readonly List<Renderer> rockRenderers = new List<Renderer>();

    /// <summary>Rocks on screen (screen rectangle overlaps the view, top above the fog) and rocks seen shrinking.</summary>
    static void RockStats(CubeRailRunner runner)
    {
        var bg = Object.FindFirstObjectByType<PathBackgroundGenerator>();
        Camera cam = Camera.main;
        if (bg == null || cam == null) return;
        Transform root = bg.transform.Find("PathBackground");
        if (root == null) return;
        int onScreen = 0;
        float floorY = runner.transform.position.y - 45f;
        foreach (Transform cell in root)
            foreach (Transform piece in cell)
            {
                piece.GetComponentsInChildren(false, rockRenderers);
                bool seen = false;
                foreach (var r in rockRenderers)
                {
                    Bounds b = r.bounds;
                    if (b.max.y < floorY) continue;
                    Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, 0), hi = new Vector3(float.MinValue, float.MinValue, 0);
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector3 v = cam.WorldToViewportPoint(c);
                        lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v);
                    }
                    if (hi.x > 0 && lo.x < 1 && hi.y > 0 && lo.y < 1) { seen = true; break; }
                }
                if (seen)
                {
                    onScreen++;
                    float sc = piece.localScale.x;
                    if (lastScale.TryGetValue(piece, out float prev) && sc < prev - 1e-4f) shrinkInView++;
                }
                lastScale[piece] = piece.localScale.x;
            }
        statSamples++; statSum += onScreen; statMax = Mathf.Max(statMax, onScreen);
    }
    public static bool Done => done;

    /// <summary>times: seconds after the run starts; scores: also shoot when the score first reaches these.</summary>
    public static void Run(string outDir, string shotPrefix, float[] times, int[] scores, int cap = 58)
    {
        Stop();
        OutDir = outDir;
        Directory.CreateDirectory(OutDir);
        prefix = shotPrefix;
        shotTimes.Clear(); if (times != null) shotTimes.AddRange(times);
        shotScores.Clear(); if (scores != null) shotScores.AddRange(scores);
        scoreCap = cap;
        log.Clear();
        frame = 0; runStart = -1f; started = false; saveSwitched = false; done = false; lastPortals = -1;
        statSamples = 0; statSum = 0; statMax = 0; shrinkInView = 0; lastScale.Clear(); nextStat = 0f;
        if (EditorApplication.isPlaying) { SaveSystem.UseFile("save_test.json"); saveSwitched = true; }   // never touch the real save
        EditorApplication.update += Tick;
    }

    public static void Stop()
    {
        EditorApplication.update -= Tick;
    }

    static void Note(string s)
    {
        log.Add(Time.realtimeSinceStartup.ToString("F1") + " " + s);
        try { File.WriteAllLines(LogPath, log); } catch { }
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Note("play stopped"); done = true; Stop(); return; }
        frame++;
        // Focus loss pauses 3.6+ runs: keep the pause controller out of the way.
        foreach (var p in Object.FindObjectsByType<PauseController>(FindObjectsSortMode.None)) Object.Destroy(p);
        if (Time.timeScale < 0.99f && Time.timeScale > 0f && !started) Time.timeScale = 1f;

        if (frame < waitFrames) return;
        if (!saveSwitched) { SaveSystem.UseFile("save_test.json"); saveSwitched = true; Note("save -> save_test.json"); }

        var runner = Object.FindFirstObjectByType<CubeRailRunner>();
        var gen = Object.FindFirstObjectByType<DynamicPathGenerator>();
        if (runner == null || gen == null) return;

        if (!started)
        {
            if (runner.IsGameOver) { runner.Restart(); return; }
            EnsureBot(runner);
            runner.BeginRun();
            started = true;
            runStart = Time.time;
            Note("run started");
            return;
        }

        EnsureBot(runner);
        float t = Time.time - runStart;
        if (Time.time >= nextStat) { nextStat = Time.time + 0.25f; RockStats(runner); }
        if (gen.PortalsTaken != lastPortals) { lastPortals = gen.PortalsTaken; Note("portals " + lastPortals + " score " + gen.Score); OnPortal?.Invoke(lastPortals); }

        for (int i = shotTimes.Count - 1; i >= 0; i--)
            if (t >= shotTimes[i]) { Shoot(prefix + "_t" + shotTimes[i].ToString("0.#"), gen); shotTimes.RemoveAt(i); }
        for (int i = shotScores.Count - 1; i >= 0; i--)
            if (gen.Score >= shotScores[i]) { Shoot(prefix + "_s" + shotScores[i], gen); shotScores.RemoveAt(i); }

        if (runner.IsGameOver) { Note("game over at score " + gen.Score + " t " + t.ToString("F1")); runner.Restart(); runStart = Time.time; started = false; frame = waitFrames; }

        if (gen.Score >= scoreCap || (shotTimes.Count == 0 && shotScores.Count == 0))
        {
            Note("finished at score " + gen.Score + " t " + t.ToString("F1") + " portals " + gen.PortalsTaken);
            Note("rocks on screen avg " + (statSamples > 0 ? (float)statSum / statSamples : 0f).ToString("F1") + " max " + statMax + " shrinks in view " + shrinkInView + " samples " + statSamples);
            var bot = runner.GetComponent<AutoPlayer>();
            if (bot != null) Object.Destroy(bot);
            runner.HitObstacle();
            done = true;
            Stop();
        }
    }

    static void EnsureBot(CubeRailRunner runner)
    {
        if (runner.GetComponent<AutoPlayer>() == null) runner.gameObject.AddComponent<AutoPlayer>().report = false;
    }

    static void Shoot(string name, DynamicPathGenerator gen)
    {
        string path = Path.Combine(OutDir, name + ".png");
        ScreenCapture.CaptureScreenshot(path, superSize);
        Note("shot " + name + " score " + gen.Score + " portals " + gen.PortalsTaken);
    }
}
#endif
