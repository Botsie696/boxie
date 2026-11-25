#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using UnityEngine;

/// <summary>
/// Dev-only recorder for comparing camera modes (C / A / B). Add it to any object in Play mode; it is
/// never part of a scene. Per frame it measures where the runner is on screen, how much of the route
/// ahead is visible (P1 = to the next corner, P2 = first half of the leg after it, P3 = the rest of that
/// leg), how the camera moves, and (for A) when it holds / glides. Allocation-free after Awake.
/// Summary() returns the table row.
/// </summary>
[DefaultExecutionOrder(2000)]
public class CameraModeRecorder : MonoBehaviour
{
    public int targetScore = 120;
    public bool Done { get; private set; }
    public string DoneReason = "";

    CameraParentRotator rig;
    DynamicPathGenerator gen;
    CubeRailRunner runner;
    Camera cam;
    CameraFeelProfile P;

    // totals
    double time, stillTime, inSafeTime, inHardTime, p1Time, p2Time, p3Time, p2TimeLowS, timeLowS, p2TimeHighS, timeHighS, stillLow, stillHigh;
    double holdTime, glideTime, emergTime;
    int frames, steps, maxStepFrame;
    float maxStep, maxZoomRate, maxCamSpeed, sMax, speedMax, speedSum;
    int zoomOver4;
    readonly int[] runnerSpeedHist = new int[1200];   // 0.005 half-screens/s per bin, to 6
    long runnerSpeedN;

    // glides
    bool inGlide; float glideStart; float glideMaxCamSpeed; float glideRunnerSpeedSum; int glideFrames; float lastGlideEnd = -10f;
    readonly float[] glideDur = new float[1024]; int glideN;
    float worstPeakRatio, minGlideDur = 99f, minGapAfterGlide = 99f;
    int lastGlideCount;

    // reversals
    const int RingN = 16;
    readonly Vector2 [] ringP = new Vector2[RingN];
    int ringHead, ringCount; float ringTimer, reversalCooldown; int reversals;

    float prevDt = 0.01f; Vector3 prevCamPos, prevStepVec; float prevOrtho, prevSx, prevSy; bool havePrev;
    string startMode = "";
    public string Mode => startMode;

    void Start()
    {
        rig = FindFirstObjectByType<CameraParentRotator>();
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        runner = FindFirstObjectByType<CubeRailRunner>();
        cam = rig != null ? rig.boomCamera : Camera.main;
        P = rig != null ? rig.FeelProfile : null;
        if (P == null) P = ScriptableObject.CreateInstance<CameraFeelProfile>();
    }

    void LateUpdate()
    {
        if (Done || rig == null || gen == null || runner == null || cam == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (startMode == "") startMode = rig.ActiveMode.ToString();

        if (gen.DisplayScore >= targetScore) { Finish("reached score " + gen.DisplayScore); return; }
        if (runner.IsGameOver) { Finish("game over at score " + gen.DisplayScore); return; }

        float speed = runner.forwardSpeed;
        float s = P.SpeedFraction(speed);
        bool low = s <= 0.5f;
        frames++; time += dt; speedSum += speed; speedMax = Mathf.Max(speedMax, speed); sMax = Mathf.Max(sMax, s);
        if (low) timeLowS += dt; else timeHighS += dt;

        // ---- runner on screen (half-screens, centre 0, +up) ----
        Vector3 vp = cam.WorldToViewportPoint(runner.transform.position);
        float sx = (vp.x - 0.5f) * 2f, sy = (vp.y - 0.5f) * 2f;
        float top = 1f - 2f * P.hudTopReserve;
        bool safe = Mathf.Abs(sx) <= P.runnerSafeX && sy >= P.runnerSafeYMin && sy <= Mathf.Min(P.runnerSafeYMax, top);
        bool hardOk = Mathf.Abs(sx) <= P.hardFrameLimit && Mathf.Abs(sy) <= P.hardFrameLimit && sy <= top;
        if (safe) inSafeTime += dt;
        if (hardOk) inHardTime += dt;

        // ---- route visibility ----
        gen.BuildRoute(runner.transform.position);
        int n = gen.RouteCount, cc = gen.RouteCornerCount;
        bool v1 = true, v2 = true, v3 = true;
        if (cc > 0)
        {
            int c0 = gen.RouteCornerIdx[0];
            int c1 = cc > 1 ? gen.RouteCornerIdx[1] : n;
            int e2 = Mathf.Min(n - 1, c0 + Mathf.CeilToInt((c1 - c0) * 0.5f));
            int e3 = Mathf.Min(n - 1, c1);
            for (int i = 1; i <= e3; i++)
            {
                bool vis = Visible(gen.RoutePts[i], top);
                if (vis) continue;
                if (i <= c0) v1 = false; else if (i <= e2) v2 = false; else v3 = false;
            }
        }
        if (v1) p1Time += dt;
        if (v1 && v2) { p2Time += dt; if (low) p2TimeLowS += dt; else p2TimeHighS += dt; }
        if (v1 && v2 && v3) p3Time += dt;

        // ---- camera motion in the screen plane ----
        // the point at the middle of the screen at the runner's depth: unlike the camera position it does not swing when the yaw turns
        Vector3 camPos = cam.transform.position, camFwd = cam.transform.forward;
        Vector3 cp = camPos + camFwd * Vector3.Dot(runner.transform.position - camPos, camFwd);
        Vector3 dpos = cp - prevCamPos;
        Vector3 planar = Vector3.ProjectOnPlane(dpos, cam.transform.forward);
        float ortho = cam.orthographicSize;
        if (havePrev)
        {
            float step = planar.magnitude;
            float camSpeed = step / dt;
            float zoomRate = Mathf.Abs(ortho - prevOrtho) / dt;
            maxCamSpeed = Mathf.Max(maxCamSpeed, camSpeed);
            maxZoomRate = Mathf.Max(maxZoomRate, zoomRate);
            if (zoomRate > 4f) zoomOver4++;
            maxStepFrame = 0;
            float jump = (planar - prevStepVec * (dt / Mathf.Max(0.0001f, prevDt))).magnitude;   // departure from the previous velocity (dt-normalised)
            maxStep = Mathf.Max(maxStep, jump);
            if (jump > 0.5f) steps++;
            bool still = camSpeed < 0.05f && zoomRate < 0.02f;
            if (still) { stillTime += dt; if (low) stillLow += dt; else stillHigh += dt; }

            float rv = Mathf.Sqrt((sx - prevSx) * (sx - prevSx) + (sy - prevSy) * (sy - prevSy)) / dt;
            int bin = Mathf.Min(runnerSpeedHist.Length - 1, (int)(rv / 0.005f));
            runnerSpeedHist[bin]++; runnerSpeedN++;

            // glide bookkeeping (state names come from the rig)
            string st = rig.ModeStateName;
            bool gl = st.StartsWith("GLIDE");
            if (st.StartsWith("HOLD")) holdTime += dt; else if (gl) glideTime += dt;
            if (st.Contains("EMERGENCY")) emergTime += dt;
            if (gl && !inGlide)
            {
                inGlide = true; glideStart = (float)time; glideMaxCamSpeed = 0f; glideRunnerSpeedSum = 0f; glideFrames = 0;
                if ((float)time - lastGlideEnd < minGapAfterGlide) minGapAfterGlide = (float)time - lastGlideEnd;
            }
            if (inGlide)
            {
                glideMaxCamSpeed = Mathf.Max(glideMaxCamSpeed, camSpeed); glideRunnerSpeedSum += speed; glideFrames++;
                if (!gl)
                {
                    inGlide = false; lastGlideEnd = (float)time;
                    float d = (float)time - glideStart;
                    if (glideN < glideDur.Length) glideDur[glideN++] = d;
                    minGlideDur = Mathf.Min(minGlideDur, d);
                    float ratio = glideMaxCamSpeed / Mathf.Max(0.1f, glideRunnerSpeedSum / Mathf.Max(1, glideFrames));
                    worstPeakRatio = Mathf.Max(worstPeakRatio, ratio);
                }
            }

            // reversals: out by > 1.5 u, then more than half of it back within 1.5 s (sampled at 10 Hz)
            ringTimer += dt; reversalCooldown -= dt;
            if (ringTimer >= 0.1f)
            {
                ringTimer = 0f;
                Vector2 now = new Vector2(Vector3.Dot(cp, cam.transform.right), Vector3.Dot(cp, cam.transform.up));
                ringP[ringHead] = now; ringHead = (ringHead + 1) % RingN; ringCount = Mathf.Min(RingN, ringCount + 1);
                if (reversalCooldown <= 0f && ringCount >= 4)
                {
                    int newest = (ringHead + RingN - 1) % RingN;
                    for (int a = 0; a < ringCount - 2 && reversalCooldown <= 0f; a++)
                    {
                        Vector2 pa = ringP[(newest - (ringCount - 1) + a + RingN * 2) % RingN];
                        for (int b = a + 1; b < ringCount - 1; b++)
                        {
                            Vector2 pb = ringP[(newest - (ringCount - 1) + b + RingN * 2) % RingN];
                            Vector2 ab = pb - pa, bn = now - pb;
                            if (ab.magnitude > 1.5f && Vector2.Dot(ab, bn) < 0f && bn.magnitude > 0.5f * ab.magnitude) { reversals++; reversalCooldown = 1.5f; break; }
                        }
                    }
                }
            }
        }
        prevDt = dt; prevCamPos = cp; prevStepVec = planar; prevOrtho = ortho; prevSx = sx; prevSy = sy; havePrev = true;
    }

    bool Visible(Vector3 w, float top)
    {
        Vector3 v = cam.WorldToViewportPoint(w);
        float x = (v.x - 0.5f) * 2f, y = (v.y - 0.5f) * 2f;
        return Mathf.Abs(x) <= 1f && y >= -1f && y <= top;
    }

    void Finish(string reason) { Done = true; DoneReason = reason; }

    float Percentile(float q)
    {
        if (runnerSpeedN == 0) return 0f;
        long target = (long)(runnerSpeedN * q), acc = 0;
        for (int i = 0; i < runnerSpeedHist.Length; i++) { acc += runnerSpeedHist[i]; if (acc >= target) return (i + 1) * 0.005f; }
        return 6f;
    }

    float GlidePct(float q)
    {
        if (glideN == 0) return 0f;
        var tmp = new float[glideN]; System.Array.Copy(glideDur, tmp, glideN); System.Array.Sort(tmp);
        return tmp[Mathf.Min(glideN - 1, (int)(glideN * q))];
    }

    static string Pct(double a, double b) => b > 0.001 ? (100.0 * a / b).ToString("F1") : "n/a";

    /// <summary>One block of metrics for this run.</summary>
    public string Summary()
    {
        double min = time / 60.0;
        var sb = new StringBuilder();
        sb.AppendLine("MODE " + startMode + " | " + DoneReason + " | game time " + time.ToString("F1") + " s | frames " + frames + " | speed avg " + (speedSum / Mathf.Max(1, frames)).ToString("F1") + " max " + speedMax.ToString("F1") + " (s max " + sMax.ToString("F2") + ")");
        sb.AppendLine("  runner in safe box %      " + Pct(inSafeTime, time) + "   | inside hard box & under HUD %  " + Pct(inHardTime, time));
        sb.AppendLine("  P1 fully visible %        " + Pct(p1Time, time));
        sb.AppendLine("  P1+P2 fully visible %     " + Pct(p2Time, time) + "   (s<=0.5: " + Pct(p2TimeLowS, timeLowS) + " | s>0.5: " + Pct(p2TimeHighS, timeHighS) + ")");
        sb.AppendLine("  P1+P2+P3 fully visible %  " + Pct(p3Time, time));
        sb.AppendLine("  camera still %            " + Pct(stillTime, time) + "   (s<=0.5: " + Pct(stillLow, timeLowS) + " | s>0.5: " + Pct(stillHigh, timeHighS) + ")");
        sb.AppendLine("  A: hold % " + Pct(holdTime, time) + " | glide % " + Pct(glideTime, time) + " | emergency % " + Pct(emergTime, time) + " | glides " + glideN + " (" + (glideN / Mathf.Max(0.01f, (float)min)).ToString("F1") + "/min) dur p50 " + GlidePct(0.5f).ToString("F2") + " p90 " + GlidePct(0.9f).ToString("F2") + " min " + (glideN > 0 ? minGlideDur.ToString("F2") : "n/a") + " | worst peak cam speed / runner speed " + worstPeakRatio.ToString("F2") + " | min gap after a glide before the next " + (minGapAfterGlide < 90f ? minGapAfterGlide.ToString("F2") : "n/a"));
        sb.AppendLine("  reversals/min " + (reversals / Mathf.Max(0.01f, (float)min)).ToString("F2") + " (" + reversals + ")"
                      + " | runner screen speed p50 " + Percentile(0.5f).ToString("F2") + " p95 " + Percentile(0.95f).ToString("F2") + " p99 " + Percentile(0.99f).ToString("F2") + " half-screens/s");
        sb.AppendLine("  zoom rate max " + maxZoomRate.ToString("F2") + " u/s (frames > 4 u/s: " + zoomOver4 + ") | camera speed max " + maxCamSpeed.ToString("F1") + " u/s | position steps > 0.5 u: " + steps + " (max " + maxStep.ToString("F2") + " u)"
                      + " | emergencies " + rig.ModeEmergencyCount + " (" + (rig.ModeEmergencyCount / Mathf.Max(0.01f, (float)min)).ToString("F2") + "/min)"
                      + (rig.ActiveMode == CameraParentRotator.CameraMode.B_Follow ? " | B slide active " + (rig.FollowSlideActiveShare * 100f).ToString("F1") + "% of frames" : ""));
        return sb.ToString();
    }
}
#endif
