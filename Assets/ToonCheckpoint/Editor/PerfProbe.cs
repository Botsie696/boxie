#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Dev only: bot run from a checkpoint, logs frame time / batches / object counts every few seconds (Library/perf_probe.txt).</summary>
public static class PerfProbe
{
    static int frame, cp, count;
    static float t0, lastSample, accMs, maxMs; static int accFrames;
    static bool started;
    static readonly List<string> log = new List<string>();
    static string path => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "perf_probe.txt"));

    public static void Run(int checkpoint)
    {
        cp = checkpoint; frame = 0; started = false; log.Clear(); accMs = 0; maxMs = 0; accFrames = 0;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        frame++;
        foreach (var p in Object.FindObjectsByType<PauseController>(FindObjectsSortMode.None)) Object.Destroy(p);
        if (Time.timeScale < 0.99f && Time.timeScale > 0f) Time.timeScale = 1f;
        if (frame < 90) return;
        var ctl = Object.FindFirstObjectByType<CheckpointRunController>();
        var runner = Object.FindFirstObjectByType<CubeRailRunner>();
        var gen = Object.FindFirstObjectByType<DynamicPathGenerator>();
        if (ctl == null || runner == null || gen == null) return;
        if (!started)
        {
            SaveSystem.UseFile("save_test.json");
            ctl.RestartAt(cp);
            started = true; t0 = Time.realtimeSinceStartup; lastSample = t0;
            return;
        }
        if (runner.GetComponent<AutoPlayer>() == null) runner.gameObject.AddComponent<AutoPlayer>().report = false;
        if (runner.IsGameOver) { log.Add("game over score " + gen.Score); runner.Restart(); }
        float dt = Time.unscaledDeltaTime * 1000f; accMs += dt; accFrames++; maxMs = Mathf.Max(maxMs, dt);
        if (Time.realtimeSinceStartup - lastSample >= 5f)
        {
            lastSample = Time.realtimeSinceStartup;
            int rend = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length;
            int go = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
            log.Add($"t {lastSample - t0:F0}s score {gen.Score} avg {accMs / Mathf.Max(1, accFrames):F1}ms max {maxMs:F0}ms batches {UnityEditor.UnityStats.batches} setpass {UnityEditor.UnityStats.setPassCalls} tris {UnityEditor.UnityStats.triangles / 1000}k shadowCasters {UnityEditor.UnityStats.shadowCasters} renderers {rend} transforms {go} mono {(System.GC.GetTotalMemory(false) >> 20)}MB");
            var grp = new Dictionary<string, int>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) { string k = r.name.Split((char)40)[0].Trim(); if (k.StartsWith("Cliff_")) k = "Cliff*"; var rt = r.transform; while (rt.parent != null && rt.parent.parent != null) rt = rt.parent; k += "<" + rt.name.Split((char)32)[0]; grp.TryGetValue(k, out int gc); grp[k] = gc + 1; }
            var top = new List<KeyValuePair<string, int>>(grp); top.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new System.Text.StringBuilder("   top:"); for (int i = 0; i < Mathf.Min(8, top.Count); i++) sb.Append(" ").Append(top[i].Key).Append("=").Append(top[i].Value);
            log.Add(sb.ToString());
            accMs = 0; maxMs = 0; accFrames = 0;
            File.WriteAllLines(path, log);
        }
        if (Time.realtimeSinceStartup - t0 > 100f) { log.Add("done"); File.WriteAllLines(path, log); EditorApplication.update -= Tick; runner.HitObstacle(); }
    }
}
#endif
