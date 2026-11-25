using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Per-leg and per-death logs for tuning (DifficultyProfile.telemetry, or add it by hand). Writes CSV files to
/// Application.persistentDataPath/RunTelemetry (no network, nothing leaves the device). Off in release builds
/// unless writeInBuilds is ticked.
///
/// legs_*.csv   one row per leg left: index, type, speed, cubes, seconds on it, taps needed / made, landing to
///              first tap, slack at the corner (corner time minus the last played tap), hazard cost, camera
///              turned in the last 1.0 s, controls re-mapped in the last 1.0 s.
/// deaths_*.csv one row per fall: cause, the leg's data, and the flags
///              NEAR_LATE  a tap came within 120 ms AFTER the corner deadline
///              LOST_TAP   a tap given before the miss was never played (dropped)
///              MAP_CHANGE the camera turned or the controls re-mapped within 1.0 s before the fall
/// rescues_*.csv one row per close call: leg type, overshoot (cubes), lateness (ms).
/// </summary>
public class RunTelemetry : MonoBehaviour
{
    [Tooltip("Write the CSV files. Off = only the in-memory counters below.")]
    public bool writeFiles = true;
    [Tooltip("Also write in player builds (off: editor and development builds only).")]
    public bool writeInBuilds = false;

    [Header("Counters (this session)")]
    public int legs;
    public int deaths;
    public int rescues;
    public int nearLate, lostTap, mapChange;

    DynamicPathGenerator gen;
    CubeRailRunner runner;
    CameraParentRotator rig;
    StringBuilder legRows = new StringBuilder(), deathRows = new StringBuilder(), rescueRows = new StringBuilder();
    string stamp;
    readonly List<float> tapTimes = new List<float>(16);
    DynamicPathGenerator.Segment onLeg;
    float legStart, lastYaw, lastYawChange = -10f;
    int tapsOnLeg;
    float firstTap = -1f;
    bool wasOver;

    public static string Folder => Path.Combine(Application.persistentDataPath, "RunTelemetry");

    void Start()
    {
        gen = GetComponent<DynamicPathGenerator>() ?? FindFirstObjectByType<DynamicPathGenerator>();
        runner = gen != null ? gen.player : FindFirstObjectByType<CubeRailRunner>();
        rig = FindFirstObjectByType<CameraParentRotator>();
        stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        legRows.AppendLine("run,leg,type,speed,cubes,seconds,tapsNeeded,tapsMade,firstTapAfterLanding,slack,cost,cameraTurned1s,mapChanged1s,rescued");
        deathRows.AppendLine("run,leg,cause,type,speed,cubes,tapsNeeded,tapsMade,secondsOnLeg,lastTapAfterDeadline,NEAR_LATE,LOST_TAP,MAP_CHANGE");
        rescueRows.AppendLine("run,leg,kind,type,overshootCubes,lateMs,onBoost");
        if (runner != null)
        {
            runner.TapTaken += OnTap;
            runner.Landed += OnLanded;
            runner.Rescued += OnRescued;
        }
        if (rig != null) lastYaw = rig.CurrentYaw;
    }

    void OnDestroy()
    {
        if (runner != null) { runner.TapTaken -= OnTap; runner.Landed -= OnLanded; runner.Rescued -= OnRescued; }
        Flush();
    }

    void OnApplicationPause(bool paused) { if (paused) Flush(); }

    int run;
    bool rescuedOnLeg;

    void OnTap(int dir)
    {
        tapTimes.Add(Time.time);
        if (tapTimes.Count > 16) tapTimes.RemoveAt(0);
        tapsOnLeg++;
        if (firstTap < 0f) firstTap = Time.time - legStart;
    }

    void OnRescued(CubeRailRunner.RescueInfo info)
    {
        rescues++;
        rescuedOnLeg = true;
        rescueRows.AppendLine($"{run},{(gen != null ? gen.Score : 0)},{(info.plank ? "plank" : "corner")},{TypeOf(onLeg)},{info.overshootCubes:0.00},{info.lateSeconds * 1000f:0},{(info.onBoost ? 1 : 0)}");
    }

    // The leg just left is written when the next one is landed on.
    void OnLanded()
    {
        if (gen == null || runner == null) return;
        if (onLeg != null && onLeg != gen.PlayerSegment) WriteLeg(onLeg, runner.LastLandingTime);
        onLeg = null;    // picked up in Update once the generator has moved on
        legStart = runner.LastLandingTime;
        tapsOnLeg = 0;
        firstTap = -1f;
        rescuedOnLeg = false;
    }

    void Update()
    {
        if (gen == null || runner == null) return;
        if (rig != null && Mathf.Abs(Mathf.DeltaAngle(rig.CurrentYaw, lastYaw)) > 0.5f) { lastYawChange = Time.time; lastYaw = rig.CurrentYaw; }
        if (onLeg == null && runner.IsRunning) onLeg = gen.PlayerSegment;

        bool over = runner.IsGameOver;
        if (over && !wasOver) WriteDeath();
        if (!over && wasOver) run++;
        wasOver = over;
    }

    bool CameraTurned(float at) => at - lastYawChange <= 1f;
    bool MapChanged(float at) => at - runner.LastViewChangeTime <= 1f || at - runner.LastRemapChangeTime <= 1f;

    void WriteLeg(DynamicPathGenerator.Segment s, float leftAt)
    {
        legs++;
        float slack = leftAt - runner.LastExecutedTapTime;
        legRows.AppendLine($"{run},{s.serial},{TypeOf(s)},{s.speed:0.00},{s.cubes.Count},{leftAt - legStart:0.000},{s.taps},{tapsOnLeg}," +
                           $"{firstTap:0.000},{(tapsOnLeg > 0 ? slack : -1f):0.000},{s.cost},{(CameraTurned(leftAt) ? 1 : 0)},{(MapChanged(leftAt) ? 1 : 0)},{(rescuedOnLeg ? 1 : 0)}");
    }

    void WriteDeath()
    {
        deaths++;
        float now = Time.time;
        // The deadline: the exact miss when the profile's corner check made it, else the moment it left the track.
        float deadline = runner.LastMissTime > now - 1f ? runner.LastMissTime : now - CubeRailRunner.FailFlyingSeconds;
        float lateTap = float.NaN;
        bool near = false, lost = false;
        foreach (float t in tapTimes)
        {
            if (t > deadline && t <= deadline + 0.12f) { near = true; lateTap = t - deadline; }
            if (t <= deadline && t > now - 0.3f && t > runner.LastExecutedTapTime + 0.001f) lost = true;
        }
        if (runner.LastDroppedTapTime > now - 0.3f) lost = true;
        bool map = CameraTurned(now) || MapChanged(now);
        if (near) nearLate++;
        if (lost) lostTap++;
        if (map) mapChange++;

        var s = onLeg ?? gen.PlayerSegment;
        deathRows.AppendLine($"{run},{gen.Score},{runner.LastDeathCause},{TypeOf(s)},{(s != null ? s.speed : 0f):0.00},{(s != null ? s.cubes.Count : 0)}," +
                             $"{(s != null ? s.taps : -1)},{tapsOnLeg},{now - legStart:0.000},{(float.IsNaN(lateTap) ? -1f : lateTap):0.000},{(near ? 1 : 0)},{(lost ? 1 : 0)},{(map ? 1 : 0)}");
        Flush();
    }

    public static string TypeOf(DynamicPathGenerator.Segment s)
    {
        if (s == null) return "unknown";
        if (s.portalRole != 0) return "portal";
        if (s.boost) return "boost";
        if (s.plankCount > 1) return "multiplank";
        if (s.plankCount == 1) return "bridge";
        if (s.decoyAtEnd) return "decoy";
        if (s.obstacle) return "obstacle";
        return "ordinary";
    }

    void Flush()
    {
        if (!writeFiles || (!Application.isEditor && !Debug.isDebugBuild && !writeInBuilds)) return;
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, $"legs_{stamp}.csv"), legRows.ToString());
            File.WriteAllText(Path.Combine(Folder, $"deaths_{stamp}.csv"), deathRows.ToString());
            File.WriteAllText(Path.Combine(Folder, $"rescues_{stamp}.csv"), rescueRows.ToString());
        }
        catch (System.Exception e) { Debug.LogWarning("[RunTelemetry] could not write: " + e.Message); }
    }
}
