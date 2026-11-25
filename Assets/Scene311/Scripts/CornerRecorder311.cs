#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// DEV ONLY (editor + development builds; the whole file compiles away in release). Scene 3.11: one CSV row per corner
/// the runner meets - when the corner came into view, every input on the leg, the earliest and latest valid input,
/// the margin, and what happened (made / rescued / shield / death + cause).
///
/// Times are seconds relative to landing on the leg (negative = before landing). Columns:
///   visible      the corner's cube was fully drawn (PathDepthShading) - usually before landing
///   earliest     the first moment an input for this corner is played for it: landing (+ landing lock); a tap up to
///                preCornerBuffer before landing while already on the turn face is buffered for it
///   latest       the corner deadline (the commit point minus cornerDeadline); latestRescue adds the rescue window
///   lastInput    the last tap given on the leg; margin = latest - lastInput (negative = late, a rescue or a death)
/// Written to Application.persistentDataPath/CornerRecorder311/corners_*.csv on every death, pause and quit.
/// Added by Scene311Systems. No per-frame allocation: rows are built only when a leg ends.
/// </summary>
public class CornerRecorder311 : MonoBehaviour
{
    public static string Folder => Path.Combine(Application.persistentDataPath, "CornerRecorder311");

    DynamicPathGenerator gen;
    CubeRailRunner runner;
    PathDepthShading shading;
    readonly StringBuilder rows = new StringBuilder(64 * 1024);
    readonly Dictionary<int, float> visibleAt = new Dictionary<int, float>(64);
    readonly float[] taps = new float[16];
    int tapCount, run, shieldOnLeg;
    bool rescuedOnLeg, wasOver, started;
    DynamicPathGenerator.Segment leg;
    float landedAt;
    string stamp, path;

    public int Rows { get; private set; }

    void Start()
    {
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        runner = FindFirstObjectByType<CubeRailRunner>();
        shading = gen != null ? gen.GetComponent<PathDepthShading>() : null;
        stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        path = Path.Combine(Folder, "corners_" + stamp + ".csv");
        rows.AppendLine("run,level,serial,cyclePos,phase,kind,tapsNeeded,speed,cubes,visible,earliest,latest,latestRescue,firstInput,lastInput,inputs,margin,result,rescue,shield,deathCause");
        if (runner != null)
        {
            runner.TapTaken += OnTap;
            runner.Rescued += OnRescued;
            runner.ShieldSaved += OnShield;
        }
    }

    void OnDestroy()
    {
        if (runner != null) { runner.TapTaken -= OnTap; runner.Rescued -= OnRescued; runner.ShieldSaved -= OnShield; }
        Flush();
    }

    void OnApplicationPause(bool paused) { if (paused) Flush(); }
    void OnApplicationQuit() => Flush();

    void OnTap(int dir)
    {
        if (tapCount < taps.Length) taps[tapCount++] = Time.time;
        else { System.Array.Copy(taps, 1, taps, 0, taps.Length - 1); taps[taps.Length - 1] = Time.time; }
    }

    void OnRescued(CubeRailRunner.RescueInfo info) => rescuedOnLeg = true;
    void OnShield(bool obstacle) => shieldOnLeg++;

    void Update()
    {
        if (gen == null || runner == null) return;
        bool over = runner.IsGameOver;
        if (over && !wasOver && leg != null) { WriteRow(leg, Time.time, "death", runner.LastDeathCause); leg = null; Flush(); }
        if (!over && wasOver) { run++; visibleAt.Clear(); tapCount = 0; }
        wasOver = over;
        if (over || !runner.IsRunning) return;

        DynamicPathGenerator.Segment cur = gen.PlayerSegment;
        if (cur != leg)
        {
            // The leg just left: its corner was made (a rescue, if one fired on it).
            if (leg != null && started) WriteRow(leg, runner.LastLandingTime, rescuedOnLeg ? "rescued" : "made", "");
            leg = cur;
            started = true;
            landedAt = runner.LastLandingTime;
            // taps given just before landing (the cross-corner buffer) belong to this leg
            int keep = 0;
            for (int i = 0; i < tapCount; i++) if (taps[i] >= landedAt - Prof(p => p.preCornerBuffer, 0.15f)) taps[keep++] = taps[i];
            tapCount = keep;
            rescuedOnLeg = false;
            shieldOnLeg = 0;
        }
        TrackVisibility();
    }

    // First moment each upcoming corner cube is fully drawn (the next three legs).
    void TrackVisibility()
    {
        if (shading == null) return;
        int idx = -1;
        for (int i = 0; i < 64; i++)
        {
            var s = gen.SegmentAt(i);
            if (s == null) break;
            if (s == leg) { idx = i; break; }
        }
        if (idx < 0) return;
        for (int k = idx; k <= idx + 3; k++)
        {
            var s = gen.SegmentAt(k);
            if (s == null || s.cubes.Count == 0 || visibleAt.ContainsKey(s.serial)) continue;
            GameObject end = s.cubes[s.cubes.Count - 1];
            if (end != null && shading.ShownAmount(end) >= 0.99f) visibleAt[s.serial] = Time.time;
        }
    }

    float Prof(System.Func<DifficultyProfile, float> f, float fallback)
    {
        var p = gen != null ? gen.difficultyProfile : null;
        return p != null ? f(p) : fallback;
    }

    static string Kind(DynamicPathGenerator.Segment s)
    {
        if (s.portalRole == 1) return "door";
        if (s.portalRole == 2) return "runwayOut";
        if (s.portalRole == 3) return "runway";
        if (s.slowMo) return "slowmo";
        if (s.boost) return "boost";
        if (s.glide) return "glide";
        if (s.decoyAtEnd) return "decoy";
        return s.taps >= 2 ? "corner2" : s.taps == 1 ? "corner1" : "corner0";
    }

    void WriteRow(DynamicPathGenerator.Segment s, float endAt, string result, string cause)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        DifficultyProfile P = gen.difficultyProfile;
        float lock_ = P != null ? P.landingLock : 0.1f;
        float deadline = P != null ? P.cornerDeadline : 0.03f;
        float rescue = P != null ? P.RescueWindowAt(s.serial) : 0f;
        // The corner: where the next leg was landed on (made / rescued: minus the rescue lateness is unknown, so the
        // nominal crossing time is used), or the exact miss for a death.
        float cornerAt = result == "death" && runner.LastMissTime > endAt - 2f ? runner.LastMissTime : landedAt + s.seconds;
        float latest = cornerAt - deadline - landedAt;
        float first = tapCount > 0 ? taps[0] - landedAt : float.NaN;
        float last = tapCount > 0 ? taps[tapCount - 1] - landedAt : float.NaN;
        float vis = visibleAt.TryGetValue(s.serial, out float v) ? v - landedAt : float.NaN;
        visibleAt.Remove(s.serial);
        int level = s.cycle + 1;
        rows.Append(run).Append(',').Append(level).Append(',').Append(s.serial).Append(',').Append(s.cyclePos).Append(',')
            .Append(s.phase).Append(',').Append(Kind(s)).Append(',').Append(s.taps).Append(',')
            .Append(s.speed.ToString("F2", ci)).Append(',').Append(s.cubes.Count).Append(',')
            .Append(F(vis, ci)).Append(',').Append(F(lock_, ci)).Append(',').Append(F(latest, ci)).Append(',').Append(F(latest + rescue, ci)).Append(',')
            .Append(F(first, ci)).Append(',').Append(F(last, ci)).Append(',').Append(tapCount).Append(',')
            .Append(F(tapCount > 0 ? latest - last : float.NaN, ci)).Append(',')
            .Append(shieldOnLeg > 0 && result != "death" ? "shield" : result).Append(',')
            .Append(rescuedOnLeg ? 1 : 0).Append(',').Append(shieldOnLeg).Append(',').Append(cause).Append('\n');
        Rows++;
        tapCount = 0;
    }

    static string F(float x, System.Globalization.CultureInfo ci) => float.IsNaN(x) ? "" : x.ToString("F3", ci);

    void Flush()
    {
        if (Rows == 0) return;
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(path, rows.ToString());
        }
        catch (System.Exception e) { Debug.LogWarning("[CornerRecorder311] could not write: " + e.Message); }
    }
}
#endif
