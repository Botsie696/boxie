using System.IO;
using System.Text;
using UnityEngine;

// TEMPORARY: steers the runner and records camera behaviour. Deleted after tuning.
public class TmpCamRecorder : MonoBehaviour
{
    public string outPath;
    CubeRailRunner runner;
    CameraParentRotator rig;
    DynamicPathGenerator gen;
    Camera cam;
    readonly StringBuilder sb = new StringBuilder("t,state,zone,ortho,yaw,vx,vy,speed,over,pan,camDist,runnerX,runnerY,runnerZ\n");
    float nextSample, nextSteer;
    bool written;

    void Start()
    {
        runner = FindFirstObjectByType<CubeRailRunner>();
        rig = FindFirstObjectByType<CameraParentRotator>();
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        cam = Camera.main;
    }

    void Update()
    {
        if (!runner.IsGameOver && !runner.isFlying && runner.currentCube != null && Time.time >= nextSteer)
        {
            var corners = gen.GetUpcomingCorners(1);
            var leg = gen.PlayerSegment;
            if (corners.Count > 0 && leg != null)
            {
                Transform c = runner.currentCube;
                Vector3 have = runner.currentSideIndex switch { 1 => c.right, 2 => -c.up, 3 => -c.right, _ => c.up };
                Vector3 want = corners[0].outgoingDirection.normalized;
                float d = Vector3.Dot(have, want);
                if (d <= 0.9f)
                {
                    nextSteer = Time.time + 0.2f;
                    // a half turn is two quarter turns; each tick re-evaluates, so the second follows the first
                    if (d < -0.9f) runner.SendMessage("ChangeSide", 1);
                    else runner.SendMessage("ChangeSide", Vector3.Dot(Quaternion.AngleAxis(-90f, c.forward) * have, want) > 0.9f ? 1 : -1);
                }
            }
        }

        if (Time.time >= nextSample && !written)
        {
            nextSample = Time.time + 0.1f;
            Vector3 v = cam.WorldToViewportPoint(runner.transform.position);
            sb.Append(Time.time.ToString("F2")).Append(',').Append(rig.CurrentState).Append(',').Append(rig.CurrentZoneIndex).Append(',')
              .Append(cam.orthographicSize.ToString("F2")).Append(',').Append(cam.transform.eulerAngles.y.ToString("F1")).Append(',')
              .Append(v.x.ToString("F3")).Append(',').Append(v.y.ToString("F3")).Append(',').Append(runner.forwardSpeed.ToString("F1")).Append(',')
              .Append(runner.IsGameOver ? 1 : 0).Append(',').Append(rig.CurrentPanMagnitude.ToString("F2")).Append(',')
              .Append(Vector3.Distance(cam.transform.position, runner.transform.position).ToString("F1")).Append(',')
              .Append(runner.transform.position.x.ToString("F1")).Append(',').Append(runner.transform.position.y.ToString("F1")).Append(',').Append(runner.transform.position.z.ToString("F1")).Append('\n');
        }

        if (runner.IsGameOver && !written) { written = true; File.WriteAllText(outPath, sb.ToString()); }
    }

    void OnDestroy() { if (!written && outPath != null) File.WriteAllText(outPath, sb.ToString()); }
}
