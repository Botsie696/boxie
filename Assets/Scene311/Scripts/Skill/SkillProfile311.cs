using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// SCENE 3.11 SKILL MODEL in the game (added by Scene311Systems). Hidden from the player. Watches every leg and run
/// (first-tap reaction, tap margin before the corner, success / rescue / shield / death per feature, attempts and
/// clears per checkpoint, retries, session length), keeps the Elo-style ratings in SkillModel311, saves them as JSON
/// (persistentDataPath/skill311.json, versioned) and hands the tier to the difficulty profile through Adaptive311 -
/// ONLY at run start (and before a restart) and when a door's runway is laid (the level after it), never mid-leg.
/// Freefall ignores it (Scene311DifficultyProfile). Dev overlay in development builds (tap the top-left corner 3x / F2).
/// </summary>
[DefaultExecutionOrder(-800)]
public class SkillProfile311 : MonoBehaviour
{
    public SkillTuning311 tuning;

    public static SkillProfile311 Instance { get; private set; }
    /// <summary>Bot tests: nothing is recorded or saved (the player's profile is left alone).</summary>
    public static bool RecordingOff;
    public SkillModel311 Model { get; private set; }
    /// <summary>The checkpoint whose level the current modifiers were made for, and how.</summary>
    public int AppliedCheckpoint { get; private set; }
    public float AppliedTier { get; private set; }
    public int AppliedMercy { get; private set; }

    public event System.Action<int> CheckpointCleared;   // checkpoint (1-based) whose door was crossed
    public event System.Action<int> RunDied;             // checkpoint died in
    public event System.Action RunStarted;
    public event System.Action<DynamicPathGenerator.Segment, float> LegDone;   // leg, result (1 / rescue / 0)

    static string FilePath => Path.Combine(Application.persistentDataPath, "skill311.json");

    DynamicPathGenerator gen;
    CubeRailRunner runner;
    PortalZoneController zones;
    CheckpointRunController crc;
    DynamicPathGenerator.Segment leg;
    float legLanded, firstTap = -1f, lastTap = -1f;
    bool rescued, shielded, wasOver, wasRunning, portalAhead, dirty;
    float sessionStart;

    /// <summary>Scene311Systems hands the tuning over right after adding this (Awake has already run by then).</summary>
    public void Init(SkillTuning311 t)
    {
        if (Model != null) return;
        tuning = t != null ? t : (tuning != null ? tuning : ScriptableObject.CreateInstance<SkillTuning311>());
        Model = new SkillModel311(tuning, Load());
        Model.S.sessions++;
        sessionStart = Time.unscaledTime;
    }

    void Awake()
    {
        Instance = this;
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        runner = FindFirstObjectByType<CubeRailRunner>();
        zones = FindFirstObjectByType<PortalZoneController>();
        crc = FindFirstObjectByType<CheckpointRunController>();
    }

    void Start()
    {
        Init(tuning);
        if (runner != null)
        {
            runner.TapTaken += OnTap;
            runner.Rescued += OnRescued;
            runner.ShieldSaved += OnShield;
        }
        if (zones != null) zones.DoorCrossed += OnDoorCrossed;
        // The very first path is built after every Start: lay it with this player's tier for the starting checkpoint.
        ApplyFor(crc != null ? crc.StartCheckpoint : 1);
    }

    void OnDestroy()
    {
        if (runner != null) { runner.TapTaken -= OnTap; runner.Rescued -= OnRescued; runner.ShieldSaved -= OnShield; }
        if (zones != null) zones.DoorCrossed -= OnDoorCrossed;
        Save();
        if (Instance == this) { Instance = null; Adaptive311.Reset(); }
    }

    void OnApplicationPause(bool paused) { if (paused) Save(); }
    void OnApplicationQuit() => Save();

    int CurrentCheckpoint => gen != null ? gen.PortalsTaken + 1 : 1;

    /// <summary>Hands the tier for `checkpoint` to the profile. Run start / restart / a door ahead only.</summary>
    public void ApplyFor(int checkpoint)
    {
        TierMods311 m = Model.ModsFor(checkpoint, out float tier, out int mercy);
        Adaptive311.Apply(m);
        AppliedCheckpoint = checkpoint;
        AppliedTier = tier;
        AppliedMercy = mercy;
        overlayDirty = true;
    }

    // ---------------------------------------------------------------------- signals

    void OnTap(int dir)
    {
        float now = Time.time;
        if (firstTap < 0f) { firstTap = now; Model.NoteReaction(now - legLanded); }
        lastTap = now;
    }

    void OnRescued(CubeRailRunner.RescueInfo info) { rescued = true; Model.S.rescues++; Model.S.closeCalls++; dirty = true; }
    void OnShield(bool obstacle) { shielded = true; Model.S.shieldSaves++; dirty = true; }

    void OnDoorCrossed(int portal)
    {
        if (RecordingOff) return;
        // portal k = the door at the end of checkpoint k's level
        // A run started at checkpoint c comes out of door c - 1: that crossing is the start, not a clear.
        if (crc != null && portal + 1 == crc.StartCheckpoint) return;
        Model.NoteClear(portal);
        Model.NoteAttempt(portal + 1);
        CheckpointCleared?.Invoke(portal);
        dirty = true;
        Save();
    }

    void Update()
    {
        if (gen == null || runner == null || RecordingOff) return;
        bool over = runner.IsGameOver;
        bool running = runner.IsRunning;

        if (running && !wasRunning && !over)
        {
            // A run (or a restart) has begun at its checkpoint.
            Model.NoteRunStart();
            if (Model.S.runs > 1) Model.S.retries++;
            Model.NoteAttempt(crc != null ? crc.StartCheckpoint : 1);
            RunStarted?.Invoke();
            leg = null;
            dirty = true;
        }
        wasRunning = running;

        if (over && !wasOver)
        {
            int cp = CurrentCheckpoint;
            if (leg != null) EndLeg(leg, 0f);
            leg = null;
            Model.NoteDeath(cp);
            RunDied?.Invoke(cp);
            // The restart goes back to this checkpoint: lay it with the tier as it now stands (mercy may have moved).
            ApplyFor(crc != null ? Mathf.Max(cp, 1) : 1);
            Save();
        }
        wasOver = over;
        if (over || !running) return;

        // A door's runway is laid: every leg of this level is down, so the tier for the next one goes in now.
        bool ahead = gen.PortalAhead;
        if (ahead && !portalAhead) ApplyFor(CurrentCheckpoint + 1);
        portalAhead = ahead;

        DynamicPathGenerator.Segment cur = gen.PlayerSegment;
        if (cur != leg)
        {
            if (leg != null) EndLeg(leg, shielded ? 0f : rescued ? 0.6f : 1f);
            leg = cur;
            legLanded = runner.LastLandingTime;
            firstTap = lastTap = -1f;
            rescued = shielded = false;
        }
        if (dirty && Time.frameCount % 120 == 0) Save();
        UpdateOverlay();
    }

    void EndLeg(DynamicPathGenerator.Segment s, float result)
    {
        if (s.portalRole == 2 || s.cubes.Count == 0) return;   // the runway out of a door asks nothing
        if (s.taps > 0 && lastTap > 0f)
        {
            // margin: the corner deadline minus the last tap (seconds; < 0 = late)
            float deadline = legLanded + s.seconds - (gen.difficultyProfile != null ? gen.difficultyProfile.cornerDeadline : 0.03f);
            Model.NoteMargin(deadline - lastTap);
        }
        float slack = s.floorSeconds > 0f ? s.seconds - s.floorSeconds : 0.3f;
        Model.OnLeg(FeatureMask(s), Mathf.Max(0, s.cost), slack, result);
        LegDone?.Invoke(s, result);
        dirty = true;
    }

    public static int FeatureMask(DynamicPathGenerator.Segment s)
    {
        int m = 0;
        if (s.portalRole == 3)
        {
            for (int k = 0; k < 7; k++) if ((s.designMask311 & (1 << k)) != 0) m |= 1 << ((int)Feature311.RunwayTotem + k);
            return m;
        }
        if (s.slowMo) return 1 << (int)Feature311.SlowMo;
        if (s.boost && s.portalRole == 0) return 1 << ((int)Feature311.BoostClassic + Mathf.Clamp(s.boostMode311, 0, 3));
        if (s.decoyAtEnd) m |= 1 << (int)Feature311.Decoy;
        if (s.taps >= 2) m |= 1 << (int)Feature311.Corner2;
        else if (s.taps == 1) m |= 1 << (int)Feature311.Corner1;
        return m;
    }

    // ---------------------------------------------------------------------- save

    SkillSave311 Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonUtility.FromJson<SkillSave311>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (s != null) return s;
            }
        }
        catch (System.Exception e) { Debug.LogWarning("[SkillProfile311] could not read the save: " + e.Message); }
        return SkillSave311.New(tuning.startRating);
    }

    public void Save()
    {
        if (Model == null || RecordingOff) return;
        Model.S.sessionSeconds += Time.unscaledTime - sessionStart;
        sessionStart = Time.unscaledTime;
        try
        {
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(Model.S), Encoding.UTF8);
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
            dirty = false;
        }
        catch (System.Exception e) { Debug.LogWarning("[SkillProfile311] could not save: " + e.Message); }
    }

    /// <summary>Dev menu: wipe the skill profile.</summary>
    public void ResetProfile()
    {
        var fresh = SkillSave311.New(tuning.startRating);
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(fresh), Model.S);
        ApplyFor(CurrentCheckpoint);
        Save();
    }

    /// <summary>Dev menu: force a tier (-2..2) and mercy step on the next apply (a fake rating and attempt count).</summary>
    public void ForceTier(float tier, int mercy)
    {
        Model.S.rating = tuning.startRating + tier * tuning.pointsPerTier;
        Model.S.runs = Mathf.Max(Model.S.runs, tuning.collectOnlyRuns);
        int cp = Mathf.Clamp(CurrentCheckpoint, 1, SkillSave311.MaxCheckpoints);
        int high = tuning.TargetFor(cp).y;
        Model.S.attempts[cp] = mercy <= 0 ? 0 : high + 1 + Mathf.CeilToInt((mercy - 1) * tuning.mercyStepShare * high);
        ApplyFor(cp);
        Save();
    }

    // ---------------------------------------------------------------------- dev overlay

    bool overlayOn, overlayDirty = true;
    string overlayText = "";
    float overlayAt;
    int cornerTaps;
    float cornerTapAt;
    GUIStyle style;

    void UpdateOverlay()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!tuning.devOverlay) return;
        if (Input.GetKeyDown(KeyCode.F2)) overlayOn = !overlayOn;
        if (Input.GetMouseButtonDown(0) && Input.mousePosition.x < Screen.width * 0.12f && Input.mousePosition.y > Screen.height * 0.88f)
        {
            cornerTaps = Time.unscaledTime - cornerTapAt < 0.5f ? cornerTaps + 1 : 1;
            cornerTapAt = Time.unscaledTime;
            if (cornerTaps >= 3) { overlayOn = !overlayOn; cornerTaps = 0; }
        }
        if (overlayOn && (overlayDirty || Time.unscaledTime - overlayAt > 0.5f)) BuildOverlay();
#endif
    }

    void BuildOverlay()
    {
        overlayAt = Time.unscaledTime;
        overlayDirty = false;
        var S = Model.S;
        int cp = CurrentCheckpoint;
        Vector2Int target = tuning.TargetFor(cp);
        var sb = new StringBuilder(512);
        sb.Append("SKILL  rating ").Append(S.rating.ToString("0")).Append("  tier ").Append(Model.Tier.ToString("+0.00;-0.00"))
          .Append(" (").Append(SkillTuning311.TierName(Model.Tier)).Append(")  runs ").Append(S.runs).Append('\n');
        sb.Append("applied CP").Append(AppliedCheckpoint).Append(": tier ").Append(AppliedTier.ToString("+0.00;-0.00")).Append(" mercy ").Append(AppliedMercy)
          .Append("  push ").Append(S.push[Mathf.Clamp(cp, 0, SkillSave311.MaxCheckpoints)].ToString("0.0")).Append('\n');
        sb.Append("CP").Append(cp).Append(" attempts ").Append(S.attempts[Mathf.Clamp(cp, 0, SkillSave311.MaxCheckpoints)])
          .Append(" / target ").Append(target.x).Append('-').Append(target.y).Append("  mercy step ").Append(Model.MercyStep(cp)).Append('\n');
        sb.Append("reaction ").Append(S.reactionEma.ToString("0.000")).Append("s  margin ").Append(S.marginEma.ToString("0.000"))
          .Append("s  rescues ").Append(S.rescues).Append("  shields ").Append(S.shieldSaves).Append('\n');
        for (int f = 0; f < (int)Feature311.Count; f++)
        {
            if (S.featureLegs[f] == 0) continue;
            sb.Append(((Feature311)f).ToString()).Append(' ').Append(Model.FeatureTier((Feature311)f).ToString("+0.0;-0.0"))
              .Append(" (").Append(S.featureLegs[f]).Append(")  ");
            if (f % 3 == 2) sb.Append('\n');
        }
        TierMods311 m = Adaptive311.Mods;
        sb.Append("\nmods: budget x").Append(m.budget.ToString("0.00")).Append(" T+").Append(m.tHumanAdd.ToString("0.00"))
          .Append(" rescue+").Append(m.rescueAdd.ToString("0.00")).Append(" stations ").Append(m.stationsAdd)
          .Append(" boost x").Append(m.boostLength.ToString("0.00")).Append(" decoys x").Append(m.decoys.ToString("0.00"))
          .Append(" release+").Append(m.releaseAdd).Append(" shield x").Append(m.shieldFreq.ToString("0.00")).Append(" speed x").Append(m.speed.ToString("0.00"));
        if (m.weakDecoy || m.weakBoost || m.weakRunway || m.weakSlowMo || m.weakTwoRoll)
            sb.Append("\nweak: ").Append(m.weakDecoy ? "decoy " : "").Append(m.weakBoost ? "boost " : "").Append(m.weakRunway ? "runway " : "")
              .Append(m.weakSlowMo ? "slowmo " : "").Append(m.weakTwoRoll ? "2-roll" : "");
        overlayText = sb.ToString();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void OnGUI()
    {
        if (!overlayOn || !tuning.devOverlay) return;
        if (style == null) style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = Mathf.Max(12, Screen.height / 70), wordWrap = true };
        GUI.Box(new Rect(8, 8, Screen.width * 0.6f, Screen.height * 0.3f), overlayText, style);
    }
#endif
}
