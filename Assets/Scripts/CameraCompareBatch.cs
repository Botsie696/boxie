#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

/// <summary>
/// Dev-only: plays a list of camera-mode runs back to back in one Play session (AutoPlayer on, runner restarted
/// between runs) and appends each CameraFrameTxt summary to summaryPath. Added from tooling, never saved in a scene.
/// </summary>
public class CameraCompareBatch : MonoBehaviour
{
    public struct Run
    {
        public CameraParentRotator.CameraMode mode; public float forceSpeed; public int score; public float seconds; public string label; public string frames;
        public Run(CameraParentRotator.CameraMode m, float f, int s, float sec, string l, string fr) { mode = m; forceSpeed = f; score = s; seconds = sec; label = l; frames = fr; }
    }

    public Run[] runs;
    public string summaryPath;
    public float timeScale = 3f;
    public bool Done;
    public int Index;

    IEnumerator Start()
    {
        var rig = FindFirstObjectByType<CameraParentRotator>();
        var runner = FindFirstObjectByType<CubeRailRunner>();
        if (!AutoPlayHotkey.IsOn) AutoPlayHotkey.Toggle();
        Time.timeScale = timeScale;
        for (Index = 0; Index < runs.Length; Index++)
        {
            Run r = runs[Index];
            rig.Mode = r.mode;
            if (Index == 0 && !runner.IsGameOver && !runner.IsRunning) runner.BeginRun();
            else
            {
                runner.Restart();
                yield return null;
                while (runner.IsRebuilding || !runner.IsRunning) yield return null;
            }
            yield return null;
            var lg = rig.gameObject.AddComponent<CameraFrameTxt>();
            lg.targetScore = r.score; lg.maxGameSeconds = r.seconds; lg.forceSpeed = r.forceSpeed; lg.label = r.label;
            lg.writeFrames = !string.IsNullOrEmpty(r.frames); lg.path = r.frames ?? ""; lg.summaryPath = summaryPath;
            while (!lg.Done) yield return null;
            Destroy(lg);
            yield return new WaitForSecondsRealtime(0.5f);
            Time.timeScale = timeScale;
        }
        Done = true;
        System.IO.File.AppendAllText(summaryPath, "BATCH DONE\n");
    }
}
#endif
