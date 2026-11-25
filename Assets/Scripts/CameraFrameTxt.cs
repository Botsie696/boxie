using System.Text;
using UnityEngine;

// Dev-only: per-frame camera / screen-% dump to a txt file until targetScore (or maxGameSeconds), plus a summary
// block (camera speed / accel / dips, runner offset, corner visibility, yaw, flow, gaze and thumb zones, emergencies,
// shot changes) written to summaryPath. forceSpeed > 0 pins the runner's forwardSpeed. Never part of a scene.
[DefaultExecutionOrder(2000)]
public class CameraFrameTxt : MonoBehaviour
{
    public int targetScore = 10;
    public float maxGameSeconds = 0f;     // 0 = no limit
    public float forceSpeed = 0f;         // 0 = the game's own speed
    public string path = "";
    public string summaryPath = "";
    public string label = "";
    public bool writeFrames = true;
    public bool Done;
    public string Summary = "";

    CameraParentRotator rig; DynamicPathGenerator gen; CubeRailRunner runner; Camera cam; CameraFeelProfile P;
    readonly StringBuilder sb = new StringBuilder(1 << 22);
    float t; int f;

    const int Cap = 200000;
    float[] FX, FY, FZ;
    float[] T, PX, PY, PZ, Yaw, Ortho, RX, RY, CX, CY, Flow, Spd;
    bool[] Run, C1v, C2v, Rot, HasC1;
    int n;
    Vector3 integ, prevCp, prevFwd; bool havePrev;
    int emerg0, lastShotState; float[] shotTimes = new float[4096]; int shotN;

    void Start()
    {
        rig = FindFirstObjectByType<CameraParentRotator>();
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        runner = FindFirstObjectByType<CubeRailRunner>();
        cam = rig.boomCamera != null ? rig.boomCamera : Camera.main;
        P = rig.FeelProfile != null ? rig.FeelProfile : ScriptableObject.CreateInstance<CameraFeelProfile>();
        T = new float[Cap]; PX = new float[Cap]; PY = new float[Cap]; PZ = new float[Cap]; Yaw = new float[Cap]; Ortho = new float[Cap];
        RX = new float[Cap]; RY = new float[Cap]; CX = new float[Cap]; CY = new float[Cap]; Flow = new float[Cap]; Spd = new float[Cap];
        FX = new float[Cap]; FY = new float[Cap]; FZ = new float[Cap];
        Run = new bool[Cap]; C1v = new bool[Cap]; C2v = new bool[Cap]; Rot = new bool[Cap]; HasC1 = new bool[Cap];
        emerg0 = rig.ModeEmergencyCount;
        sb.AppendLine("MODE " + rig.ActiveMode + " " + label + " (x,y = % of screen from left/bottom; runner, next corner C1, following corner C2; ok=within 0-100; flow = % screen height/s)");
        sb.AppendLine("frame\tt\tscore\tstate\tcamX\tcamY\tcamZ\tyaw\ttilt\tortho\trunX%\trunY%\tC1X%\tC1Y%\tC2X%\tC2Y%\tC1vis\tC2vis\tflow\toverBudget\temerg\trotating\tspeed\tfocusX\tfocusY\tfocusZ");
    }

    void Update() { if (forceSpeed > 0f && runner != null && !Done) runner.forwardSpeed = forceSpeed; }

    static bool In(Vector3 v) { return v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f; }

    void LateUpdate()
    {
        if (Done || rig == null || gen == null || runner == null) return;
        if (forceSpeed > 0f) runner.forwardSpeed = forceSpeed;
        float dt = Time.deltaTime; if (dt <= 0f) return;
        t += dt; f++;
        if (gen.DisplayScore >= targetScore || runner.IsGameOver || (maxGameSeconds > 0f && t >= maxGameSeconds) || n >= Cap - 1)
        { Finish(runner.IsGameOver ? "game over at score " + gen.DisplayScore : "score " + gen.DisplayScore + " t " + t.ToString("F1")); return; }

        Vector3 r = cam.WorldToViewportPoint(runner.transform.position);
        gen.BuildRoute(runner.transform.position);
        Vector3 c1 = new Vector3(-1, -1, 0), c2 = c1;
        int cc = gen.RouteCornerCount;
        if (cc > 0) c1 = cam.WorldToViewportPoint(gen.RoutePts[gen.RouteCornerIdx[0]]);
        if (cc > 1) c2 = cam.WorldToViewportPoint(gen.RoutePts[gen.RouteCornerIdx[1]]);
        Transform ct = cam.transform; Vector3 p = ct.position; Vector3 e = ct.eulerAngles;
        string st = rig.ModeStateName;
        bool emerg = st.Contains("EMERGENCY") || rig.ModeEmergencyActive && rig.ActiveMode != CameraParentRotator.CameraMode.C_Original;

        // screen-plane path of the point at the middle of the screen at the runner's depth
        Vector3 fwd = ct.forward;
        Vector3 cp = p + fwd * Vector3.Dot(runner.transform.position - p, fwd);
        if (havePrev)
        {
            Vector3 d = Vector3.ProjectOnPlane(cp - prevCp, fwd);
            if (d.magnitude < 20f) integ += d;
        }
        prevCp = cp; prevFwd = fwd; havePrev = true;

        bool trans = st.StartsWith("ZONEDRIFT-TRANS");
        if (trans && lastShotState == 0 && shotN < shotTimes.Length) shotTimes[shotN++] = t;
        lastShotState = trans ? 1 : 0;

        float flow = rig.ModeFlow;
        Vector3 vf = rig.ViewFocus; FX[n] = vf.x; FY[n] = vf.y; FZ[n] = vf.z;
        T[n] = t; PX[n] = integ.x; PY[n] = integ.y; PZ[n] = integ.z; Yaw[n] = e.y; Ortho[n] = cam.orthographicSize;
        RX[n] = (r.x - 0.5f) * 2f; RY[n] = (r.y - 0.5f) * 2f; HasC1[n] = cc > 0;
        CX[n] = cc > 0 ? (c1.x - 0.5f) * 2f : 0f; CY[n] = cc > 0 ? (c1.y - 0.5f) * 2f : 0f;
        C1v[n] = cc == 0 || In(c1); C2v[n] = cc < 2 || In(c2);
        Run[n] = runner.IsRunning && !runner.IsGameOver; Rot[n] = rig.ModeRotating; Flow[n] = flow; Spd[n] = runner.CurrentMoveSpeed;
        n++;

        if (!writeFrames) return;
        sb.Append(f).Append('\t').Append(t.ToString("F3")).Append('\t').Append(gen.DisplayScore).Append('\t').Append(st)
          .Append('\t').Append(p.x.ToString("F2")).Append('\t').Append(p.y.ToString("F2")).Append('\t').Append(p.z.ToString("F2"))
          .Append('\t').Append(e.y.ToString("F2")).Append('\t').Append(e.x.ToString("F2")).Append('\t').Append(cam.orthographicSize.ToString("F2"))
          .Append('\t').Append((r.x * 100f).ToString("F1")).Append('\t').Append((r.y * 100f).ToString("F1"))
          .Append('\t').Append(cc > 0 ? (c1.x * 100f).ToString("F1") : "-").Append('\t').Append(cc > 0 ? (c1.y * 100f).ToString("F1") : "-")
          .Append('\t').Append(cc > 1 ? (c2.x * 100f).ToString("F1") : "-").Append('\t').Append(cc > 1 ? (c2.y * 100f).ToString("F1") : "-")
          .Append('\t').Append(cc > 0 && In(c1) ? 1 : 0).Append('\t').Append(cc > 1 && In(c2) ? 1 : 0)
          .Append('\t').Append(flow.ToString("F1")).Append('\t').Append(rig.ModeOverBudget ? 1 : 0).Append('\t').Append(emerg ? 1 : 0)
          .Append('\t').Append(rig.ModeRotating ? 1 : 0).Append('\t').Append(runner.CurrentMoveSpeed.ToString("F1"))
          .Append('	').Append(vf.x.ToString("F3")).Append('	').Append(vf.y.ToString("F3")).Append('	').Append(vf.z.ToString("F3")).AppendLine();
    }

    void Finish(string reason)
    {
        Done = true;
        Summary = BuildSummary(reason);
        if (writeFrames && path != "") System.IO.File.WriteAllText(path, sb.ToString());
        if (summaryPath != "") System.IO.File.AppendAllText(summaryPath, Summary + "\n");
    }

    // ------------------------------------------------------------------ metrics
    static float Pct(float[] a, int m, float q)
    {
        if (m == 0) return 0f;
        var b = new float[m]; System.Array.Copy(a, b, m); System.Array.Sort(b);
        return b[Mathf.Clamp(Mathf.RoundToInt(q * (m - 1)), 0, m - 1)];
    }

    bool RunAt(float time)
    {
        int lo = 0, hi = n - 1;
        while (hi - lo > 1) { int m = (lo + hi) >> 1; if (T[m] <= time) lo = m; else hi = m; }
        return Run[lo];
    }

    string BuildSummary(string reason)
    {
        float dur = n > 0 ? T[n - 1] : 0f, min = Mathf.Max(1e-3f, dur / 60f);
        // resample the screen-plane path at 50 Hz
        int m = Mathf.Max(0, Mathf.FloorToInt(dur * 50f));
        var rp = new Vector3[m]; var ok = new bool[m];
        int j = 0;
        for (int i = 0; i < m; i++)
        {
            float tt = i / 50f;
            while (j < n - 2 && T[j + 1] < tt) j++;
            float a = T[j], b = T[j + 1 < n ? j + 1 : j];
            float u = b > a ? Mathf.Clamp01((tt - a) / (b - a)) : 0f;
            int k = j + 1 < n ? j + 1 : j;
            rp[i] = Vector3.Lerp(new Vector3(FX[j], FY[j], FZ[j]), new Vector3(FX[k], FY[k], FZ[k]), u);   // 3D look-at point
            ok[i] = Run[j];
        }
        var vel = new Vector3[m]; var vs = new Vector3[m];
        for (int i = 1; i < m; i++) vel[i] = (rp[i] - rp[i - 1]) * 50f;
        for (int i = 0; i < m; i++) { Vector3 s = Vector3.zero; int c = 0; for (int q = i - 2; q <= i + 2; q++) if (q >= 1 && q < m) { s += vel[q]; c++; } vs[i] = c > 0 ? s / c : Vector3.zero; }
        var acc = new Vector3[m]; var accS = new float[m]; var spd = new float[m]; var spdRun = new float[m]; var accRun = new float[m];
        for (int i = 1; i < m; i++) acc[i] = (vs[i] - vs[i - 1]) * 50f;
        int nr = 0, na = 0, slow = 0;
        for (int i = 0; i < m; i++)
        {
            Vector3 s = Vector3.zero; int c = 0; for (int q = i - 2; q <= i + 2; q++) if (q >= 1 && q < m) { s += acc[q]; c++; }
            accS[i] = c > 0 ? (s / c).magnitude : 0f; spd[i] = vs[i].magnitude;
            bool settled = i > 50 && ok[i] && (i + 3 >= m || ok[Mathf.Min(m - 1, i + 3)]) && ok[Mathf.Max(0, i - 3)];
            if (settled) { spdRun[nr++] = spd[i]; accRun[na++] = accS[i]; if (spd[i] < 3f) slow++; }
        }
        int dips = 0, cool = 0;
        for (int i = 25; i < m; i++)
        {
            if (cool > 0) { cool--; continue; }
            if (!ok[i] || !ok[i - 25]) continue;
            float mx = 0f; for (int q = i - 25; q < i; q++) mx = Mathf.Max(mx, spd[q]);
            if (mx > 5f && spd[i] < 0.6f * mx) { dips++; cool = 25; }
        }
        // per-frame screen measures
        var off = new float[n]; var flow = new float[n];
        int c1 = 0, c2 = 0, gaze = 0, thumb = 0, outSafe = 0, over = 0, rn = 0;
        float yawMax = 0f, ambMax = 0f, hcEnd = -1f;
        float tY = -1f + 2f * P.thumbZoneHeight, tL = -1f + 2f * P.thumbZoneWidth, tR = 1f - 2f * P.thumbZoneWidth, g = P.gazeZone;
        float top = 1f - 2f * P.hudTopReserve;
        float flowEma = 0f;
        // rotations: contiguous |yaw rate| > 3 deg/s spans with >= 45 deg total
        float rotStart = -1f, rotDeg = 0f, minRotPer90 = 99f; int rotCount = 0;
        for (int i = 0; i < n; i++)
        {
            float dt = i > 0 ? Mathf.Max(1e-4f, T[i] - T[i - 1]) : 0.016f;
            float dy = i > 0 ? Mathf.Abs(Mathf.DeltaAngle(Yaw[i - 1], Yaw[i])) : 0f;
            float yr = dy / dt;
            // flow from the recording (same formula for every mode)
            if (i > 0)
            {
                float vp = new Vector3(PX[i] - PX[i - 1], PY[i] - PY[i - 1], PZ[i] - PZ[i - 1]).magnitude / dt;
                float o = Mathf.Max(1f, Ortho[i]);
                float rad = Mathf.Max(4f, Mathf.Sqrt(RX[i] * RX[i] * 0.25f + RY[i] * RY[i]) * o);
                float fl = 100f * (vp / (2f * o) + yr * Mathf.Deg2Rad * rad / (2f * o) + Mathf.Abs(Ortho[i] - Ortho[i - 1]) / dt / o);
                flowEma = Mathf.Lerp(flowEma, Mathf.Min(fl, 1000f), 1f - Mathf.Exp(-dt / 0.1f));
            }
            if (!Run[i] || T[i] < 1f) continue;
            float ryaw = yr; // smoothed over 3 frames
            if (i > 2) ryaw = Mathf.Abs(Mathf.DeltaAngle(Yaw[i - 3], Yaw[i])) / Mathf.Max(1e-4f, T[i] - T[i - 3]);
            yawMax = Mathf.Max(yawMax, ryaw);
            if (!Rot[i] && !(i > 3 && Rot[i - 3]) && !(i + 3 < n && Rot[i + 3])) ambMax = Mathf.Max(ambMax, ryaw);
            if (ryaw > 3f) { if (rotStart < 0f) { rotStart = T[i]; rotDeg = 0f; } rotDeg += dy; hcEnd = T[i]; }
            else if (rotStart >= 0f && T[i] - hcEnd > 0.1f)
            {
                if (rotDeg >= 45f) { rotCount++; minRotPer90 = Mathf.Min(minRotPer90, (hcEnd - rotStart) / (rotDeg / 90f)); }
                rotStart = -1f;
            }
            float ox = Mathf.Abs(RX[i]), oy = Mathf.Abs(RY[i]);
            off[rn] = Mathf.Max(ox, oy) * 100f; flow[rn] = flowEma; rn++;
            if (C1v[i]) c1++;
            if (C2v[i]) c2++;
            bool rg = ox <= g && oy <= g, cg = !HasC1[i] || (Mathf.Abs(CX[i]) <= g && Mathf.Abs(CY[i]) <= g);
            if (rg && cg) gaze++;
            bool rt = RY[i] < tY && (RX[i] < tL || RX[i] > tR), ctb = HasC1[i] && CY[i] < tY && (CX[i] < tL || CX[i] > tR) && CY[i] > -1f && Mathf.Abs(CX[i]) <= 1f;
            if (rt || ctb) thumb++;
            if (ox > P.runnerSafeX || RY[i] < P.runnerSafeYMin || RY[i] > Mathf.Min(P.runnerSafeYMax, top)) outSafe++;
            if (flowEma > P.flowMax) over++;
        }
        float minGap = 99f;
        for (int i = 1; i < shotN; i++) minGap = Mathf.Min(minGap, shotTimes[i] - shotTimes[i - 1]);
        float pr(int a) => rn > 0 ? 100f * a / rn : 0f;
        int em = rig.ModeEmergencyCount - emerg0;
        var ob = new StringBuilder();
        ob.Append(label).Append(" | ").Append(rig.ActiveMode).Append(" | ").Append(reason).Append(" | ").Append(dur.ToString("F1")).Append(" s game\n");
        ob.Append("  cam speed p1/50/99 ").Append(Pct(spdRun, nr, 0.01f).ToString("F1")).Append('/').Append(Pct(spdRun, nr, 0.5f).ToString("F1")).Append('/').Append(Pct(spdRun, nr, 0.99f).ToString("F1"))
         .Append(" | <3u/s ").Append((nr > 0 ? 100f * slow / nr : 0f).ToString("F1")).Append('%')
         .Append(" | accel p50/95/99 ").Append(Pct(accRun, na, 0.5f).ToString("F1")).Append('/').Append(Pct(accRun, na, 0.95f).ToString("F1")).Append('/').Append(Pct(accRun, na, 0.99f).ToString("F1"))
         .Append(" | dips/10s ").Append((dips / Mathf.Max(0.1f, dur / 10f)).ToString("F2")).Append('\n');
        ob.Append("  runner offset p95/max ").Append(Pct(off, rn, 0.95f).ToString("F0")).Append('/').Append(Pct(off, rn, 1f).ToString("F0")).Append('%')
         .Append(" | outside safe box ").Append(pr(outSafe).ToString("F2")).Append('%')
         .Append(" | C1 ").Append(pr(c1).ToString("F1")).Append("% C2 ").Append(pr(c2).ToString("F1")).Append('%')
         .Append(" | gaze ").Append(pr(gaze).ToString("F1")).Append("% | thumb ").Append(pr(thumb).ToString("F2")).Append("%\n");
        ob.Append("  yaw rate max ").Append(yawMax.ToString("F1")).Append(" ambient max ").Append(ambMax.ToString("F1")).Append(" deg/s | rotations ").Append(rotCount)
         .Append(" min s/90 ").Append(rotCount > 0 ? minRotPer90.ToString("F2") : "n/a")
         .Append(" | flow p95/max ").Append(Pct(flow, rn, 0.95f).ToString("F0")).Append('/').Append(Pct(flow, rn, 1f).ToString("F0")).Append(" over ").Append(pr(over).ToString("F1")).Append('%')
         .Append(" | emergencies/min ").Append((em / min).ToString("F2")).Append(" (").Append(em).Append(')');
        if (rig.ActiveMode == CameraParentRotator.CameraMode.E_ZoneDrift)
            ob.Append(" | shots/min ").Append((shotN / min).ToString("F1")).Append(" min gap ").Append(shotN > 1 ? minGap.ToString("F2") : "n/a");
        return ob.ToString();
    }
}
