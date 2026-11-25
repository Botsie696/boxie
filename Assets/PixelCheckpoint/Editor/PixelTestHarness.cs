#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dev test for the pixel checkpoint (Play mode, 3.9 or Pixel): switches to a test save, removes the pause controller (focus
/// loss pauses), adds the AutoPlayer bot, restarts at a checkpoint and takes game-view screenshots at the given seconds,
/// logging "[PixTest]" with the look in force, the runner's screen position and whether it is alive, and at the end the
/// editor frame rate while the low-resolution pixel render was on.
///   PixelTestHarness.Run(4, dir, 4f, 8f, 12f)
/// </summary>
public static class PixelTestHarness
{
    public static void Run(int checkpoint, string dir, params float[] at)
    {
        if (!EditorApplication.isPlaying) { Debug.LogError("[PixTest] enter Play first"); return; }
        Time.timeScale = 1f;
        SaveSystem.UseFile("save_test.json");
        KillPause();
        var runner = Object.FindFirstObjectByType<CubeRailRunner>();
        if (runner.GetComponent<AutoPlayer>() == null) runner.gameObject.AddComponent<AutoPlayer>();
        var crc = Object.FindFirstObjectByType<CheckpointRunController>();
        if (crc != null && checkpoint > 0) crc.RestartAt(checkpoint);
        runner.BeginRun();
        System.IO.Directory.CreateDirectory(dir);
        float t0 = Time.realtimeSinceStartup;
        int k = 0;
        int frame0 = -1;
        float time0 = 0f;
        EditorApplication.CallbackFunction cb = null;
        cb = () =>
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= cb; return; }
            KillPause();
            var r = Object.FindFirstObjectByType<CubeRailRunner>();
            if (r != null && r.IsWaitingToStart) r.BeginRun();
            // Player frames per second (editor - indicative only) from when the pixel look is fully in force.
            var px = Object.FindFirstObjectByType<PixelCheckpointController>();
            if (frame0 < 0 && px != null && px.LowResActive) { frame0 = Time.frameCount; time0 = Time.realtimeSinceStartup; }
            if (k < at.Length && Time.realtimeSinceStartup - t0 > at[k])
            {
                var pc = Object.FindFirstObjectByType<PixelCheckpointController>();
                var d = Object.FindFirstObjectByType<LookTransitionDirector>();
                Vector3 sp = Camera.main.WorldToScreenPoint(r.transform.position);
                Debug.Log("[PixTest] shot " + k + " look=" + d.CurrentLookName + " next=" + (d.NextLook != null ? d.NextLook.sourceScene : "-")
                          + " pixOn=" + (pc != null && pc.PixelOnScreen) + " lowRes=" + (pc != null && pc.LowResActive) + " runnerY=" + r.transform.position.y
                          + " screen=" + (int)sp.x + "," + (Screen.height - (int)sp.y) + " over=" + r.IsGameOver);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "s" + k + ".png"), 1);
                k++;
            }
            if (k >= at.Length)
            {
                EditorApplication.update -= cb;
                float span = Time.realtimeSinceStartup - time0;
                if (frame0 >= 0 && span > 0.5f) Debug.Log("[PixTest] editor fps in the pixel look: " + ((Time.frameCount - frame0) / span).ToString("0.0") + " over " + span.ToString("0.0") + " s");
            }
        };
        EditorApplication.update += cb;
    }

    static void KillPause()
    {
        foreach (var p in Object.FindObjectsByType<PauseController>(FindObjectsSortMode.None)) Object.Destroy(p);
    }
}
#endif
