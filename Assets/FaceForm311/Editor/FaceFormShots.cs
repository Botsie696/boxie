#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dev test for scene 3.11 (Play mode): for each checkpoint restarts there with the bot, freezes after a few seconds and
/// takes the game view twice - form shading ON, then OFF (FaceFormController disabled) - into dir as cpN_on.png / cpN_off.png.
///   FaceFormShots.Run(dir, 3, 4, 5, 6, 7, 8, 9, 10)
/// </summary>
public static class FaceFormShots
{
    public static void Run(string dir, params int[] cps)
    {
        if (!EditorApplication.isPlaying) { Debug.LogError("[FaceShots] enter Play first"); return; }
        Time.timeScale = 1f;
        SaveSystem.UseFile("save_test.json");
        System.IO.Directory.CreateDirectory(dir);
        var runner = Object.FindFirstObjectByType<CubeRailRunner>();
        if (runner.GetComponent<AutoPlayer>() == null) runner.gameObject.AddComponent<AutoPlayer>();
        var crc = Object.FindFirstObjectByType<CheckpointRunController>();
        var ff = Object.FindFirstObjectByType<FaceFormController>();
        var queue = new Queue<int>(cps);
        int phase = 0, wait = 0, cur = 0;
        float t0 = 0f;
        EditorApplication.CallbackFunction cb = null;
        cb = () =>
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= cb; return; }
            foreach (var p in Object.FindObjectsByType<PauseController>(FindObjectsSortMode.None)) Object.Destroy(p);
            var r = Object.FindFirstObjectByType<CubeRailRunner>();
            if (r != null && r.IsWaitingToStart) r.BeginRun();
            switch (phase)
            {
                case 0:
                    if (queue.Count == 0) { Debug.Log("[FaceShots] done"); EditorApplication.update -= cb; return; }
                    cur = queue.Dequeue();
                    Time.timeScale = 1f;
                    crc.RestartAt(cur);
                    r.BeginRun();
                    t0 = Time.realtimeSinceStartup;
                    phase = 1;
                    break;
                case 1:
                    if (Time.realtimeSinceStartup - t0 > 6f)
                    {
                        Time.timeScale = 0f;
                        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "cp" + cur + "_on.png"), 1);
                        wait = 0; phase = 2;
                    }
                    break;
                case 2:
                    if (++wait > 4) { ff.enabled = false; wait = 0; phase = 3; }
                    break;
                case 3:
                    if (++wait > 4)
                    {
                        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "cp" + cur + "_off.png"), 1);
                        wait = 0; phase = 4;
                    }
                    break;
                case 4:
                    if (++wait > 4)
                    {
                        ff.enabled = true;
                        Debug.Log("[FaceShots] cp " + cur + " alive=" + !r.IsGameOver);
                        phase = 0;
                    }
                    break;
            }
        };
        EditorApplication.update += cb;
    }
}
#endif
