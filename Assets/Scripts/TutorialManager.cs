using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Segment = DynamicPathGenerator.Segment;
using PromptKind = TutorialUIController.PromptKind;

/// <summary>Opacity of the game's own HUD (score, coin counter). 1 everywhere except during the tutorial.</summary>
public static class TutorialHud
{
    public static float Alpha = 1f;
}

/// <summary>
/// One-time tutorial for the cube-rail runner, kept apart from the endless game: a state machine that watches the
/// runner and drives the hooks marked "TUTORIAL ADDITION". It never loads a scene. No words on screen: the screen
/// halves, a ghost thumb, a world-space roll arc, a ghost runner and the scene's own chevrons do the teaching.
///
///   L0  Grip and halves. Standing still: tap left, tap right, then each once more with lighter hints. Then go.
///   L1  First corner, guided: slow motion, wrong half dimmed, the right half lit, thumb + arc + ghost runner,
///       enhanced chevrons. Only the right tap is taken.
///   L2  See what happens: the same hints, every tap taken. A miss plays out, then rewinds and retries as L1.
///   L3  Hints fade: one corner with arc + thumb + ghost in real time, one with only the enhanced chevrons + lane.
///   L4  The normal chevrons only. A miss rewinds and retries with L3's first hints.
///   L5  No tap needed: hands off, the hold cue glows, taps are held back.
///   L6  The opposite face: the flip (double tap) or two taps, whichever the scene plays. Guided, then real time.
///   L7  The obstacle: blocks on three faces, only the opposite one open.
///   L8  Hand-over: TutorialCompleted is saved, the random generator carries on from the end of the scripted legs,
///       a ring and a wave of light go out, the HUD fades in and the speed ramps from 70% to full.
///
/// Misses never show a fail screen: they play out briefly, then the runner, camera and coins rewind to before the
/// corner and the lesson runs again with more help. The camera never turns during the tutorial (every zone is
/// pinned to one yaw).
///
/// Boots itself in the scene named SceneName (and, as before, MainScene), or in any scene whose
/// DynamicPathGenerator has "Tutorial In This Scene" ticked - nothing to add to the scene. Skips everything if
/// PlayerPrefs "TutorialCompleted" is 1.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    public const string SceneName = "TutorialTrialMonument2";
    public const string PrefKey = "TutorialCompleted";
    /// <summary>Scenes that run the tutorial by name (as they always have); any other scene needs the checkbox.</summary>
    static readonly string[] BootScenes = { SceneName, "MainScene" };

    [Header("Pace")]
    [Tooltip("Runner speed during the tutorial, as a share of the scene's normal starting speed.")]
    [Range(0.3f, 1f)] public float tutorialSpeedFactor = 0.7f;
    [Tooltip("Seconds to ramp from tutorial speed to full speed after the hand-off.")]
    public float rampSeconds = 4f;
    [Tooltip("Seconds for the score / coin counter to fade in after the hand-off.")]
    public float hudFadeSeconds = 1f;

    [Header("Obstacle (L7)")]
    [Tooltip("Index (from the start of the leg) of the cube carrying the obstacle.")]
    public int obstacleCubeIndex = 9;

    public enum Lesson { L0_Halves, L1_Guided, L2_SeeWhatHappens, L3_HintsFade1, L3_HintsFade2, L4_NormalCue, L5_NoTap, L6_FlipGuided, L6_FlipReal, L7_Obstacle }
    enum Hint { Guided, GuidedNoFilter, Assist, ChevronOnly, Normal, NoTap, FlipGuided, FlipReal, Obstacle }
    enum Filter { Open, Prompt, BlockAll, Silent }
    enum Stage { L0, Lessons }
    enum L0Phase { Prompt, Pause, Outro }

    // The lesson at the end of each scripted leg; the last leg is the obstacle (L7).
    static readonly Lesson[] CornerLessons =
    {
        Lesson.L1_Guided, Lesson.L2_SeeWhatHappens, Lesson.L3_HintsFade1, Lesson.L3_HintsFade2,
        Lesson.L4_NormalCue, Lesson.L5_NoTap, Lesson.L6_FlipGuided, Lesson.L6_FlipReal
    };
    static readonly int[] L0Sides = { -1, 1, -1, 1 };
    const float SwipePixels = 50f;   // CubeRailRunner.SwipeThreshold

    CubeRailRunner runner;
    DynamicPathGenerator gen;
    CameraParentRotator rig;
    TurnFaceArrows arrows;
    TutorialUIController ui;
    TutorialRollArc arc;
    TutorialGhostRunner ghost;
    TutorialWorldFX fx;
    TutorialRewind rewind;
    TutorialStyle st;

    float fullSpeed, tutorialSpeed;
    float tutScale = 1f;
    // Guided lessons: the world stands still just before the corner until the right tap comes (never a miss loop).
    bool holding;
    const float GuidedHoldSeconds = 0.3f;
    // A run restarted from the pause screen rebuilds the path under the tutorial: it starts the lessons over.
    bool rebuilding;
    bool booted, done, teachFlip;
    Stage stage = Stage.L0;
    bool l0Done;
    int lastLogLeg = -2;

    // ---- L0
    int l0Step;
    L0Phase l0Phase;
    bool l0PromptShown;
    float l0T;

    // ---- the corner in hand
    int cornerLeg = -2;
    Hint hint;
    bool prompted, succeeded, handsOffShown;
    bool slow;
    Filter filter = Filter.Open;
    int promptSide, targetFace, tapsWanted, tapsAccepted;
    PromptKind kind;
    float promptStart;
    int flipAlternate = 1;
    readonly Hint[] retryHint = new Hint[32];
    readonly bool[] hasRetry = new bool[32];
    readonly bool[] failedOnLeg = new bool[32];
    readonly int[] missesOnLeg = new int[32];
    Transform failCube;

    // ---- input
    struct Finger { public bool active; public Vector2 start; }
    readonly Finger[] fingers = new Finger[10];
    float lastOpenTapTime = -10f, lastAcceptedTime = -10f;
    int lastOpenTapDir, lastAcceptedDir;

    // ---- miss + rewind
    bool failing, flashLane;
    float failT, failCornerTime;
    int failLeg;

    // ---- obstacle (L7)
    bool obstacleBuilt;
    int safeSide;
    Segment obstacleLeg;
    int obstacleIdx;
    Vector3 obstacleCentre, obstacleLegDir;
    Transform obstacleHost;
    readonly List<Transform> obstacleBlocks = new List<Transform>();
    readonly List<Renderer> obstacleRenderers = new List<Renderer>();
    Vector3 obstacleBaseScale;
    bool obstacleFading;
    float blockPulseT = -1f;

    // ---- after the hand-over
    float handoverTime = -1f;
    bool deathWatch, wasGameOver;

    /// <summary>True while a tutorial is in charge of the scene (other systems stay quiet).</summary>
    public static bool Running { get; private set; }

    // ------------------------------------------------------------------ boot

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnLoad()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Spawn(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => Spawn(s);

    static void Spawn(Scene s)
    {
        if (FindFirstObjectByType<TutorialManager>() != null) return;
        var g = FindFirstObjectByType<DynamicPathGenerator>();
        bool checkbox = g != null && g.tutorialInThisScene;
        bool byName = System.Array.IndexOf(BootScenes, s.name) >= 0;
        if (!byName && !checkbox) return;

        var go = new GameObject("TutorialManager");
        // The tutorial scene itself and checkbox scenes also get the "?" replay and the first-encounter hints.
        if (checkbox || s.name == SceneName)
        {
            go.AddComponent<TutorialReplayButton>();
            go.AddComponent<FirstEncounterHints>().enableFirstEncounterHints = true;
        }
        go.AddComponent<TutorialManager>();
    }

    public static bool Completed => PlayerPrefs.GetInt(PrefKey, 0) == 1;

    /// <summary>
    /// Set by the "?" (or any replay button): the next scene load plays the tutorial once, WITHOUT touching the
    /// saved TutorialCompleted flag, so Level 1 from the menu never shows it again.
    /// </summary>
    public static bool ReplayOnce;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { ReplayOnce = false; }

    // Awake runs before the generator's Start, so flagging it here makes its very first GenerateNewPath lay the
    // scripted opening, and the camera's first plan already uses the locked yaw.
    void Awake()
    {
        bool replay = ReplayOnce;
        ReplayOnce = false;
        if (Completed && !replay) { done = true; return; }

        gen = FindFirstObjectByType<DynamicPathGenerator>();
        runner = FindFirstObjectByType<CubeRailRunner>();
        if (gen == null || runner == null) { done = true; return; }
        // A start from a later checkpoint (menu) is not the place for the first-run lessons; they wait for a run start.
        if (gen.checkpointStartPortal > 0) { done = true; return; }

        st = TutorialStyle.Active;
        rig = FindFirstObjectByType<CameraParentRotator>();
        gen.tutorialActive = true;
        Running = true;
        TutorialHud.Alpha = 0f;
        TutorialTelemetry.Reset();
        // Quitting (or being killed) mid-tutorial counts as having seen it: marked at the start, never cleared.
        if (PlayerPrefs.GetInt(PrefKey, 0) != 1) { PlayerPrefs.SetInt(PrefKey, 1); PlayerPrefs.Save(); }

        // One camera yaw for the whole tutorial: the opening heading, turned by the rig's isometric offset (the leg
        // runs away up the screen). The generator pins every zone to it and plans the left/right lessons against it.
        Vector3 h = gen.firstCube != null ? gen.firstCube.forward : Vector3.forward;
        h.y = 0f;
        if (h.sqrMagnitude < 0.01f) h = Vector3.forward;
        float headingYaw = Mathf.Round(Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg / 90f) * 90f;
        gen.tutorialViewYaw = Mathf.Repeat(headingYaw + (rig != null ? rig.isoYawOffset : 45f), 360f);
        gen.tutorialViewTilt = rig != null ? rig.isometricTiltX : 35.264f;
    }

    IEnumerator Start()
    {
        if (done) { Destroy(this); yield break; }

        arrows = gen.GetComponent<TurnFaceArrows>();
        TutorialStyle.GuidanceOverride = arrows != null ? arrows.arrowColor : (Color?)null;

        // Always a fresh prompt canvas (an old hand-built one, if any, is switched off).
        foreach (var old in FindObjectsByType<TutorialUIController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            old.gameObject.SetActive(false);
        ui = TutorialUIController.Build();
        arc = gameObject.AddComponent<TutorialRollArc>();
        arc.Init(runner);
        ghost = gameObject.AddComponent<TutorialGhostRunner>();
        ghost.Init(runner);
        fx = gameObject.AddComponent<TutorialWorldFX>();
        fx.Init();
        rewind = gameObject.AddComponent<TutorialRewind>();
        rewind.Init(runner, rig);

        yield return null;   // let every Start (generator, runner, camera) finish

        fullSpeed = runner.forwardSpeed;
        tutorialSpeed = fullSpeed * tutorialSpeedFactor;

        // Control scheme of the game this hands over to (this scene): the tutorial teaches exactly that.
        DifficultyProfile p = runner.Profile;
        bool fair = p != null && p.fairInput;
        teachFlip = fair ? p.doubleTapFlip : !runner.screenAxisControls;
        bool onDown = fair && p.fireOnTouchDown;
        Debug.Log("[Tutorial] control scheme: tap the left/right half to roll; the opposite face = " +
                  (teachFlip ? "double tap the same half (flip)" : "two taps on the same half") +
                  "; taps fire on " + (onDown ? "touch-down" : "lift") +
                  "; camera-relative " + runner.cameraRelativeControls + ", screen-axis " + runner.screenAxisControls +
                  ", flip remap " + runner.flipRemapsControls + ", profile " + (p != null ? p.name : "none") +
                  ", saved mode " + ControlSettings.Mode + " (tap mode is forced while the tutorial runs).");

        runner.forceTapMode = true;
        runner.suppressGameOver = true;
        runner.tutorialInputFilter = FilterInput;
        runner.tutorialOwnsTouch = true;
        if (arrows != null)
        {
            arrows.tutorialMode = true;
            arrows.tutorialSettings = st.chevrons;
            arrows.tutorialBoost = 0f;
        }

        if (gen.TutorialSegment(0) == null) gen.BeginTutorialPath();
        StartAttempt(false);
        booted = true;
    }

    void OnDestroy()
    {
        RestoreTime();
        if (!done && booted)
        {
            TutorialHud.Alpha = 1f;
            Running = false;
        }
        if (Running && !done) Running = false;
        TutorialStyle.GuidanceOverride = null;
        DestroyObstacle();
    }

    void OnDisable() { if (!done) RestoreTime(); }

    // ------------------------------------------------------------------ attempts

    void StartAttempt(bool regenerate)
    {
        slow = false;
        RestoreTime();
        ui.Hide();
        arc.HideAll();
        ghost.Hide();
        DestroyObstacle();
        obstacleBuilt = false;
        obstacleLeg = null;
        cornerLeg = -2;
        filter = Filter.Open;
        failing = false;
        prompted = succeeded = handsOffShown = false;
        ui.HandsOff(false);

        if (regenerate)
        {
            gen.BeginTutorialPath();          // rebuilds the path and re-places the runner on firstCube
            runner.ResetForTutorial();
            if (rig != null) rig.SnapToPlayerZone();
            rewind.Clear();
        }

        runner.BeginRun();
        if (l0Done)
        {
            stage = Stage.Lessons;
            runner.forwardSpeed = tutorialSpeed;
            ui.SetRest(false, false);
        }
        else
        {
            stage = Stage.L0;
            l0Step = 0;
            l0Phase = L0Phase.Prompt;
            l0PromptShown = false;
            l0T = 0f;
            runner.forwardSpeed = 0f;
            ui.SetRest(true, true);
            TutorialTelemetry.LessonStart(Lesson.L0_Halves.ToString());
        }
    }

    void FullRebuild(string why)
    {
        Debug.Log("[Tutorial] rebuild: " + why);
        StartAttempt(true);
    }

    void OnRunRestarting()
    {
        rebuilding = true;
        rewind.Cancel();
        RestoreTime();
        failing = false;
        prompted = false;
        filter = Filter.Silent;
        ui.Hide();
        ui.HandsOff(false);
        ui.SetRewind(false);
        arc.HideAll();
        ghost.Hide();
        DestroyObstacle();
        if (arrows != null) { arrows.tutorialBoost = 0f; arrows.tutorialLaneFlash = 0f; }
        Log("[Tutorial] run restarted from the pause screen");
    }

    void OnRunRestarted()
    {
        rebuilding = false;
        rewind.Clear();
        lastLogLeg = -2;
        StartAttempt(false);
    }

    // ------------------------------------------------------------------ per frame

    void Update()
    {
        if (!booted) return;
        if (done) { TickAfterHandoff(); return; }

        // Restart from the pause screen: the runner rebuilds the path (scripted again, the generator is still in
        // tutorial mode); once it is back up the lessons start over on the new path.
        if (runner.IsRebuilding)
        {
            if (!rebuilding) OnRunRestarting();
            return;
        }
        if (rebuilding) OnRunRestarted();

        EaseTime();
        gen.TutorialForceZoneYaws();
        if (GamePaused) { ForgetFingers(); return; }
        ReadTouches();

        if (rewind.Playing) return;
        holding = false;   // TickCorner / TickObstacle set it again while a guided prompt waits at the corner
        if (failing) { TickFail(); return; }
        if (stage == Stage.L0) { TickL0(); return; }

        if (runner.FlyingSeconds > CubeRailRunner.FailFlyingSeconds) { BeginFail("missed the corner"); return; }

        Segment seg = gen.PlayerSegment;
        int leg = gen.TutorialLegOf(seg);
        if (leg != lastLogLeg) { OnLegChanged(lastLogLeg, leg); lastLogLeg = leg; }
        if (leg < 0) return;

        if (leg < gen.TutorialLegCount - 1) TickCorner(seg, leg);
        else TickObstacle(seg, leg);
    }

    void LateUpdate()
    {
        UpdateObstacleScale();
    }

    // Slow motion eases down (a breath, not a pause) and snaps back up; the miss plays at failTimeScale; the
    // rewind and the guided hold freeze the world. The factor goes through TimeScaleMixer (its own source), so the
    // pause screen and the game's other time effects still stack on top instead of being overwritten every frame.
    void EaseTime()
    {
        float target = rewind.Playing || holding ? 0f : failing ? st.failTimeScale : slow ? st.slowMoScale : 1f;
        float now = tutScale;
        if (target <= 0f) now = 0f;
        else if (target < now)
            now = Mathf.Max(target, now - (1f - st.slowMoScale) / Mathf.Max(0.01f, st.slowMoEaseSeconds) * Time.unscaledDeltaTime);
        else now = target;
        tutScale = now;
        TimeScaleMixer.Set(TimeScaleMixer.Source.Tutorial, now);
    }

    void RestoreTime()
    {
        tutScale = 1f;
        holding = false;
        TimeScaleMixer.Set(TimeScaleMixer.Source.Tutorial, 1f);
    }

    // The pause screen (MainScene prop camera 3.6+) is up: the tutorial stands still and reads no fingers.
    bool GamePaused => TimeScaleMixer.Get(TimeScaleMixer.Source.Pause) <= 0.0001f || runner.pauseInput;

    // ------------------------------------------------------------------ input

    // The tutorial reads every finger itself (CubeRailRunner.tutorialOwnsTouch) with the game's own rule - a tap
    // fires on lift (on touch-down with a fair-input DifficultyProfile), a swipe past 50 px goes its way - and feeds
    // it through SimulateTap, so the filter below sees exactly the taps the game would.
    void ReadTouches()
    {
        DifficultyProfile p = runner.Profile;
        bool onDown = p != null && p.fairInput && p.fireOnTouchDown;
        int n = Input.touchCount;
        for (int i = 0; i < n; i++)
        {
            Touch t = Input.GetTouch(i);
            int id = Mathf.Clamp(t.fingerId, 0, fingers.Length - 1);
            switch (t.phase)
            {
                case TouchPhase.Began:
                    if (Blocked(t.position)) { fingers[id].active = false; break; }   // the pause button owns it
                    fingers[id].active = true;
                    fingers[id].start = t.position;
                    ui.TouchRipple(t.position);
                    if (onDown) Fire(t.position.x > Screen.width * 0.5f ? 1 : -1);
                    break;
                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    if (fingers[id].active && !onDown) Fire(DirOf(fingers[id].start, t.position));
                    fingers[id].active = false;
                    break;
            }
        }
#if UNITY_EDITOR
        // Editor convenience: the mouse plays the part of one finger.
        if (n == 0)
        {
            if (Input.GetMouseButtonDown(0) && !Blocked(Input.mousePosition))
            {
                fingers[0].active = true;
                fingers[0].start = Input.mousePosition;
                ui.TouchRipple(Input.mousePosition);
                if (onDown) Fire(Input.mousePosition.x > Screen.width * 0.5f ? 1 : -1);
            }
            else if (Input.GetMouseButtonUp(0) && fingers[0].active)
            {
                if (!onDown) Fire(DirOf(fingers[0].start, Input.mousePosition));
                fingers[0].active = false;
            }
        }
#endif
    }

    bool Blocked(Vector2 pos) => runner.touchBlocker != null && runner.touchBlocker(pos);

    void ForgetFingers()
    {
        for (int i = 0; i < fingers.Length; i++) fingers[i].active = false;
    }

    static int DirOf(Vector2 start, Vector2 end)
    {
        float dx = end.x - start.x;
        if (Mathf.Abs(dx) >= SwipePixels) return dx > 0f ? 1 : -1;
        return end.x > Screen.width * 0.5f ? 1 : -1;
    }

    void Fire(int dir)
    {
        if (runner.IsRunning) runner.SimulateTap(dir);
    }

    // CubeRailRunner asks about every tap (touch, keys, SimulateTap). Return false to swallow it.
    bool FilterInput(int dir)
    {
        if (done) return true;
        if (failing || rewind.Playing) return false;
        float now = Time.unscaledTime;
        switch (filter)
        {
            case Filter.Silent:
                return false;

            case Filter.BlockAll:
                ui.WrongHalf(dir);
                TutorialTelemetry.WrongTap(CurrentLessonName());
                return false;

            case Filter.Prompt:
                if (dir != promptSide)
                {
                    // A tap on the other half right as the prompt appears was not an answer to it.
                    if (now - promptStart >= st.promptGraceSeconds)
                    {
                        ui.WrongHalf(dir);
                        TutorialTelemetry.WrongTap(CurrentLessonName());
                    }
                    return false;
                }
                if (tapsAccepted >= tapsWanted) return false;
                // A second single tap on the same half this soon would read as a double tap (a flip).
                if (kind == PromptKind.Tap && dir == lastAcceptedDir && now - lastAcceptedTime < 0.55f) return false;
                tapsAccepted++;
                lastAcceptedDir = dir;
                lastAcceptedTime = now;
                OnPromptTap(dir);
                return true;

            default:   // Open
                lastOpenTapTime = now;
                lastOpenTapDir = dir;
                if (prompted && !succeeded && dir == promptSide) OnPromptTap(dir);
                return true;
        }
    }

    // The real tap on the prompted half: the arc sweeps round with the roll.
    void OnPromptTap(int dir)
    {
        arc.Sweep(dir);
    }

    // ------------------------------------------------------------------ helpers

    // The face a tap on `side` rolls the runner onto, or -1.
    int MidFace(int side)
    {
        int c = runner.currentSideIndex;
        for (int k = 1; k <= 3; k += 2)
        {
            int n = (c + k) % 4;
            if (runner.TapDirectionToReach(n) == side) return n;
        }
        return -1;
    }

    // The half to prompt for the opposite face: the one whose FIRST roll passes over a face the camera can see (so
    // the player sees the move); if both (or neither) can be seen, alternate between lessons.
    int FlipSide()
    {
        Camera cam = Camera.main;
        Vector3 toCam = cam != null ? -cam.transform.forward : Vector3.up;
        int ml = MidFace(-1), mr = MidFace(1);
        bool vl = ml >= 0 && Vector3.Dot(runner.SideNormal(ml), toCam) > 0.05f;
        bool vr = mr >= 0 && Vector3.Dot(runner.SideNormal(mr), toCam) > 0.05f;
        if (vl != vr) return vl ? -1 : 1;
        flipAlternate = -flipAlternate;
        return flipAlternate;
    }

    float SecondsTo(Vector3 point, Vector3 dir)
    {
        float speed = runner.forwardSpeed;
        if (speed <= 0.01f) return float.PositiveInfinity;
        float dist = Vector3.Dot(point - runner.transform.position, dir.normalized);
        return dist / speed;
    }

    static Vector3 LastCubePos(Segment seg)
    {
        GameObject last = seg.cubes.Count > 0 ? seg.cubes[seg.cubes.Count - 1] : null;
        return last != null ? last.transform.position : seg.endPosition;
    }

    float CubeScale => runner.currentCube != null ? runner.currentCube.lossyScale.x : gen.cubeSize;

    static Lesson LessonOfLeg(int leg) => leg >= 0 && leg < CornerLessons.Length ? CornerLessons[leg] : Lesson.L7_Obstacle;

    string CurrentLessonName() => stage == Stage.L0 ? Lesson.L0_Halves.ToString() : LessonOfLeg(cornerLeg).ToString();

    static Hint DefaultHint(Lesson l) => l switch
    {
        Lesson.L1_Guided => Hint.Guided,
        Lesson.L2_SeeWhatHappens => Hint.GuidedNoFilter,
        Lesson.L3_HintsFade1 => Hint.Assist,
        Lesson.L3_HintsFade2 => Hint.ChevronOnly,
        Lesson.L4_NormalCue => Hint.Normal,
        Lesson.L5_NoTap => Hint.NoTap,
        Lesson.L6_FlipGuided => Hint.FlipGuided,
        Lesson.L6_FlipReal => Hint.FlipReal,
        _ => Hint.Obstacle
    };

    // The help a corner gets after a miss there.
    static Hint RetryHint(Lesson l) => l switch
    {
        Lesson.L4_NormalCue => Hint.Assist,        // L4 repeats with L3's first hints
        Lesson.L5_NoTap => Hint.Assist,
        Lesson.L6_FlipGuided => Hint.FlipGuided,
        Lesson.L6_FlipReal => Hint.FlipGuided,
        Lesson.L7_Obstacle => Hint.Obstacle,
        _ => Hint.Guided                           // L1, L2, L3: guided, only the right tap taken
    };

    float LeadFor(Hint h) => h switch
    {
        Hint.Guided => st.l1Lead,
        Hint.GuidedNoFilter => st.l2Lead,
        Hint.Assist => st.l3AssistLead,
        Hint.ChevronOnly => st.l3ChevronLead,
        Hint.Normal => st.l3ChevronLead,
        Hint.NoTap => st.l5Lead,
        Hint.FlipGuided => st.l6Lead,
        Hint.FlipReal => st.l6Lead,
        _ => st.l7Lead
    };

    static bool IsSlow(Hint h) => h == Hint.Guided || h == Hint.GuidedNoFilter || h == Hint.FlipGuided || h == Hint.Obstacle;
    static bool IsFiltered(Hint h) => h == Hint.Guided || h == Hint.FlipGuided || h == Hint.Obstacle;
    static bool HasDimmer(Hint h) => IsSlow(h);
    static bool HasThumb(Hint h) => IsSlow(h) || h == Hint.Assist;
    static bool HasArc(Hint h) => IsSlow(h) || h == Hint.Assist || h == Hint.FlipReal;
    static bool HasGhost(Hint h) => IsSlow(h) || h == Hint.Assist;

    float ArrowBoost(Hint h) => h switch
    {
        Hint.Guided or Hint.GuidedNoFilter or Hint.FlipGuided or Hint.Obstacle => 1f,
        Hint.Assist => st.l3AssistBoost,
        Hint.ChevronOnly or Hint.FlipReal => st.l3ChevronBoost,
        _ => 0f
    };

    void SetArrows(Hint h, float secs)
    {
        if (arrows == null) return;
        arrows.tutorialSettings = st.chevrons;
        arrows.tutorialBoost = ArrowBoost(h);
        // L3's chevron-only corner: the lane shrinks back to just the chevrons as the runner closes in.
        arrows.tutorialLaneScale = h == Hint.ChevronOnly
            ? Mathf.Clamp01((secs - 0.15f) / Mathf.Max(0.1f, st.l3ChevronLead))
            : 1f;
    }

    // ------------------------------------------------------------------ L0: grip and halves

    void TickL0()
    {
        if (runner.currentCube == null) return;
        float dt = Time.unscaledDeltaTime;
        if (arrows != null) arrows.tutorialBoost = 0f;

        switch (l0Phase)
        {
            case L0Phase.Prompt:
            {
                runner.forwardSpeed = 0f;
                if (!l0PromptShown)
                {
                    if (runner.IsRevolving) return;
                    int side = L0Sides[l0Step];
                    bool full = l0Step < 2;   // then once more each with lighter hints: arc + thumb, no glow
                    promptSide = side;
                    kind = PromptKind.Tap;
                    tapsWanted = 1;
                    tapsAccepted = 0;
                    promptStart = Time.unscaledTime;
                    filter = Filter.Prompt;
                    ui.SetRest(true, true);
                    ui.ShowPrompt(side, PromptKind.Tap, false, full, true);
                    arc.Show(side, TutorialRollArc.Kind.Roll90, runner.currentSideIndex, MidFace(side));
                    l0PromptShown = true;
                }
                if (tapsAccepted >= 1)
                {
                    ui.SuccessPop();
                    ui.Hide();
                    arc.Hide(promptSide);
                    fx.Sparks(runner.transform.position, CubeScale);
                    filter = Filter.Silent;
                    l0Phase = L0Phase.Pause;
                    l0T = 0f;
                }
                break;
            }

            case L0Phase.Pause:
                runner.forwardSpeed = 0f;
                l0T += dt;
                if (!runner.IsRevolving && l0T >= st.l0StepPause + 0.15f)
                {
                    l0Step++;
                    l0PromptShown = false;
                    l0T = 0f;
                    if (l0Step < L0Sides.Length) l0Phase = L0Phase.Prompt;
                    else
                    {
                        l0Phase = L0Phase.Outro;
                        ui.HandsOff(true);   // thumbs lift
                        ui.SetRest(false, false);   // the seam fades
                    }
                }
                break;

            case L0Phase.Outro:
                l0T += dt;
                float k = TutorialEase.InOutCubic(l0T / Mathf.Max(0.05f, st.l0SpeedEaseSeconds));
                runner.forwardSpeed = tutorialSpeed * k;
                if (l0T >= 0.5f) ui.HandsOff(false);
                if (k >= 1f)
                {
                    runner.forwardSpeed = tutorialSpeed;
                    ui.HandsOff(false);
                    filter = Filter.Open;
                    stage = Stage.Lessons;
                    l0Done = true;
                    TutorialTelemetry.LessonEnd(Lesson.L0_Halves.ToString());
                    Log("[Tutorial] L0 done, rolling");
                }
                break;
        }
    }

    // ------------------------------------------------------------------ L1-L6: the corners

    void OnLegChanged(int prevLeg, int leg)
    {
        Log("[Tutorial] on leg " + leg + " side=" + runner.currentSideIndex);
        if (prevLeg < 0 || prevLeg >= CornerLessons.Length) return;

        Lesson l = CornerLessons[prevLeg];
        TutorialTelemetry.LessonEnd(l.ToString());
        if (l == Lesson.L2_SeeWhatHappens && !failedOnLeg[prevLeg]) TutorialTelemetry.L2(true);
        if (l == Lesson.L4_NormalCue) TutorialTelemetry.L4(!failedOnLeg[prevLeg]);

        // L5 (no tap): praised once the corner is behind.
        if (hint == Hint.NoTap && handsOffShown)
        {
            ui.HandsOff(false);
            Success();
        }
        if (prompted && !succeeded) EndPrompt(false);
        filter = Filter.Open;
        slow = false;
    }

    void BeginCorner(Segment seg, int leg)
    {
        cornerLeg = leg;
        prompted = succeeded = handsOffShown = false;
        slow = false;
        filter = Filter.Open;
        tapsAccepted = 0;
        Lesson l = LessonOfLeg(leg);
        hint = leg < hasRetry.Length && hasRetry[leg] ? retryHint[leg] : DefaultHint(l);
        gen.TutorialSnapshotCoins(seg, gen.SegmentAfter(seg));
        TutorialTelemetry.LessonStart(l.ToString());
        ui.Hide();
        arc.HideAll();
        ghost.Hide();
        Log("[Tutorial] corner " + leg + " lesson " + l + " hint " + hint);
    }

    void TickCorner(Segment seg, int leg)
    {
        if (leg != cornerLeg) BeginCorner(seg, leg);

        Segment next = gen.TutorialSegment(leg + 1) ?? gen.SegmentAfter(seg);
        if (next == null) return;
        int target = runner.SideFacing(next.direction);
        int cur = runner.currentSideIndex;
        int taps = cur == target ? 0 : (cur + 2) % 4 == target ? 2 : 1;
        float secs = SecondsTo(LastCubePos(seg), seg.direction);
        SetArrows(hint, secs);

        if (prompted)
        {
            if (!succeeded && cur == target) { Success(); EndPrompt(true); }
            else UpdateHold(secs, cur == target);
            return;
        }

        if (hint == Hint.NoTap)
        {
            if (taps == 0)
            {
                if (!handsOffShown && secs <= st.l5Lead)
                {
                    handsOffShown = true;
                    ui.HandsOff(true);          // no tap: both thumbs lift
                    filter = Filter.BlockAll;   // taps are held back, with the wrong-half nudge
                }
                return;
            }
            // The runner is off the turn face after all: help it back instead.
            if (handsOffShown) { ui.HandsOff(false); handsOffShown = false; filter = Filter.Open; }
            hint = Hint.Assist;
        }

        if (taps == 0) return;   // already on the turn face: nothing to say
        if (secs <= LeadFor(hint)) StartPrompt(target, taps);
    }

    void StartPrompt(int target, int taps)
    {
        prompted = true;
        succeeded = false;
        targetFace = target;
        tapsAccepted = 0;
        kind = taps == 1 ? PromptKind.Tap : (teachFlip ? PromptKind.DoubleTap : PromptKind.TwoTaps);
        tapsWanted = taps == 1 ? 1 : 2;
        // Re-asked on every corner: the left/right mapping is locked per leg (CubeRailRunner.UpdateLegView).
        promptSide = taps == 1 ? runner.TapDirectionToReach(target) : FlipSide();
        if (promptSide == 0) promptSide = 1;
        promptStart = Time.unscaledTime;

        slow = IsSlow(hint);
        filter = IsFiltered(hint) ? Filter.Prompt : Filter.Open;
        // A correct tap a moment BEFORE the prompt appeared still counts.
        if (filter == Filter.Prompt && lastOpenTapDir == promptSide && Time.unscaledTime - lastOpenTapTime <= st.promptGraceSeconds)
            tapsAccepted = 1;

        if (HasThumb(hint)) ui.ShowPrompt(promptSide, kind, HasDimmer(hint), true, true);
        else ui.Hide();
        if (HasArc(hint))
        {
            TutorialRollArc.Kind ak = kind == PromptKind.Tap ? TutorialRollArc.Kind.Roll90 : kind == PromptKind.DoubleTap ? TutorialRollArc.Kind.Flip180 : TutorialRollArc.Kind.TwoRolls;
            arc.Show(promptSide, ak, runner.currentSideIndex, MidFace(promptSide));
        }
        if (HasGhost(hint)) ghost.Show(target);
        Log("[Tutorial] prompt " + hint + " " + kind + " side=" + promptSide + " face " + runner.currentSideIndex + "->" + target);
    }

    // Guided prompts (only the right tap is taken) never let the corner go by: just before it the world stands
    // still until the taps come. Released while a taken tap is still to be played or the roll is under way, so
    // each roll plays out (in slow motion) before it waits again for the next one.
    void UpdateHold(float secs, bool onTarget)
    {
        holding = !succeeded && !onTarget && IsFiltered(hint) && tapsAccepted < tapsWanted
                  && secs <= GuidedHoldSeconds && runner.QueuedInputCount == 0 && !runner.IsRevolving;
    }

    void EndPrompt(bool success)
    {
        slow = false;
        filter = Filter.Open;
        ui.Hide();
        arc.HideAll();   // a sweep in flight still finishes
        if (success) ghost.Flash(); else ghost.Hide();
    }

    // Small and quick: the chevrons flare, 10-14 sparks in the success colour, a light tap of haptics.
    void Success()
    {
        succeeded = true;
        ui.SuccessPop();
        fx.Sparks(runner.transform.position, CubeScale);
        if (arrows != null) arrows.Burst(1f);
        Haptics.LightImpact();
    }

    // ------------------------------------------------------------------ L7: the obstacle

    void TickObstacle(Segment seg, int leg)
    {
        if (leg != cornerLeg) BeginCorner(seg, leg);
        if (!obstacleBuilt) BuildObstacle(seg);
        if (obstacleLeg != seg) return;
        if (arrows != null) arrows.tutorialBoost = 0f;

        // Flip done (the runner is on the open face): hand over.
        if (runner.currentSideIndex == safeSide)
        {
            if (prompted && !succeeded) { Success(); EndPrompt(true); }
            Handoff();
            return;
        }

        // Ran into it.
        float along = Vector3.Dot(runner.transform.position - obstacleCentre, obstacleLegDir) / Mathf.Max(0.01f, gen.cubeSize);
        if (along > -0.85f) { BeginFail("hit the obstacle"); return; }

        float secs = SecondsTo(obstacleCentre, obstacleLegDir);
        if (prompted || secs > LeadFor(hint))
        {
            if (prompted) { DrawOpenLane(); UpdateHold(secs, false); }
            return;
        }

        int cur = runner.currentSideIndex;
        int taps = (cur + 2) % 4 == safeSide ? 2 : 1;
        StartPrompt(safeSide, taps);
        blockPulseT = 0f;   // the red blocks pulse once
        DrawOpenLane();
    }

    // The open face's lane, from a few cubes before the blocks to just past them.
    void DrawOpenLane()
    {
        if (obstacleHost == null) return;
        float cs = gen.cubeSize;
        Vector3 safeDir = obstacleHost.TransformDirection(SideLocal(safeSide));
        Vector3 start = obstacleCentre + safeDir * (cs * 0.5f + 0.02f * cs) - obstacleLegDir * (cs * 4.5f);
        float pulse = TutorialUIController.Pulse(kind);
        fx.Strip(start, safeDir, obstacleLegDir, cs * st.chevrons.laneWidth, cs * 7f, 1.6f * pulse, st.Guidance);
    }

    void BuildObstacle(Segment seg)
    {
        obstacleBuilt = true;
        obstacleLeg = seg;
        obstacleIdx = Mathf.Clamp(obstacleCubeIndex, 4, Mathf.Max(4, seg.cubes.Count - 6));
        GameObject hostGo = seg.cubes[obstacleIdx];
        if (hostGo == null || gen.cubePrefab == null) { obstacleLeg = null; return; }

        Transform host = hostGo.transform;
        obstacleHost = host;
        obstacleLegDir = seg.direction;
        obstacleCentre = host.position;

        // Sides are numbered like the runner's: 0 top, 1 right, 2 bottom, 3 left (in the cube's own frame).
        int mine = runner.currentSideIndex;
        safeSide = (mine + 2) % 4;

        obstacleBaseScale = gen.cubePrefab.transform.localScale * 0.85f;
        DestroyObstacle();
        obstacleFading = false;
        for (int k = 0; k < 4; k++)
        {
            if (k == safeSide) continue;
            GameObject block = Instantiate(gen.cubePrefab, host.position + host.TransformDirection(SideLocal(k)) * gen.cubeSize, host.rotation);
            block.name = "TutorialObstacle";
            block.SetActive(true);
            // No collider, ever: the runner's forward probe would take it for the next path cube.
            foreach (Collider c in block.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            foreach (Renderer r in block.GetComponentsInChildren<Renderer>())
            {
                r.material.SetColor("_BaseColor", st.danger);
                r.material.SetColor("_Color", st.danger);
                obstacleRenderers.Add(r);
            }
            block.transform.localScale = Vector3.zero;   // grows in with its cube (see UpdateObstacleScale)
            obstacleBlocks.Add(block.transform);
        }

        // Coins along the open face, either side of the obstacle.
        Vector3 safeDir = host.TransformDirection(SideLocal(safeSide));
        int run = 0;
        for (int i = obstacleIdx - 3; i <= obstacleIdx + 3; i++)
            gen.TutorialCoin(seg, i, safeDir, run++);
        gen.TutorialSnapshotCoins(seg, gen.SegmentAfter(seg));
    }

    static Vector3 SideLocal(int side) => side switch
    {
        1 => Vector3.right,
        2 => Vector3.down,
        3 => Vector3.left,
        _ => Vector3.up
    };

    // The blocks are separate objects, so they follow their host cube's fade by scale instead of popping in or out.
    // When the lesson starts they pulse once in the danger red.
    void UpdateObstacleScale()
    {
        if (obstacleBlocks.Count == 0) return;

        float shown = 1f;
        var shading = gen != null ? gen.Shading : null;
        if (obstacleHost == null) shown = 0f;
        else if (shading != null && shading.isActiveAndEnabled) shown = shading.ShownAmount(obstacleHost.gameObject);

        if (obstacleFading) shown = 0f;
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(shown));

        for (int i = obstacleBlocks.Count - 1; i >= 0; i--)
        {
            Transform b = obstacleBlocks[i];
            if (b == null) { obstacleBlocks.RemoveAt(i); continue; }
            b.localScale = Vector3.MoveTowards(b.localScale, obstacleBaseScale * k, obstacleBaseScale.x * 6f * Time.unscaledDeltaTime);
            if (obstacleFading && b.localScale.x < 0.01f) { Destroy(b.gameObject); obstacleBlocks.RemoveAt(i); }
        }

        if (blockPulseT >= 0f)
        {
            blockPulseT += Time.unscaledDeltaTime / Mathf.Max(0.05f, st.popSeconds * 1.6f);
            float p = blockPulseT >= 1f ? 0f : Mathf.Sin(blockPulseT * Mathf.PI);
            if (blockPulseT >= 1f) blockPulseT = -1f;
            Color c = Color.Lerp(st.danger, new Color(Mathf.Min(1f, st.danger.r * 1.5f + 0.1f), st.danger.g * 1.2f, st.danger.b * 1.2f, 1f), p);
            foreach (Renderer r in obstacleRenderers)
            {
                if (r == null) continue;
                r.material.SetColor("_BaseColor", c);
                r.material.SetColor("_Color", c);
            }
        }
    }

    void TickObstacleCleanup()
    {
        if (obstacleBlocks.Count == 0 || obstacleLeg == null) return;
        float along = Vector3.Dot(runner.transform.position - obstacleCentre, obstacleLegDir) / Mathf.Max(0.01f, gen.cubeSize);
        if (along > 4f || gen.TutorialLegOf(gen.PlayerSegment) < 0) obstacleFading = true;
    }

    void DestroyObstacle()
    {
        foreach (Transform b in obstacleBlocks)
            if (b != null) Destroy(b.gameObject);
        obstacleBlocks.Clear();
        obstacleRenderers.Clear();
        obstacleHost = null;
    }

    // ------------------------------------------------------------------ a miss: play it out, then rewind

    void BeginFail(string why)
    {
        int leg = cornerLeg;
        Lesson l = LessonOfLeg(leg);
        Log("[Tutorial] miss on " + l + ": " + why);
        TutorialTelemetry.Retry(l.ToString());
        if (l == Lesson.L2_SeeWhatHappens) TutorialTelemetry.L2(false);
        if (leg >= 0 && leg < failedOnLeg.Length)
        {
            failedOnLeg[leg] = true;
            hasRetry[leg] = true;
            missesOnLeg[leg]++;
            Hint h = RetryHint(l);
            // Missed it again with the lighter help: from now on it is guided (only the right tap taken, and the
            // world waits at the corner), so a player who does nothing is never sent round the same miss forever.
            if (missesOnLeg[leg] >= 2 && !IsFiltered(h)) h = Hint.Guided;
            retryHint[leg] = h;
        }

        failing = true;
        failT = 0f;
        failLeg = leg;
        // When it left the track (game time): the rewind lands rewindToBeforeCorner before that.
        failCornerTime = Time.time - runner.FlyingSeconds;
        failCube = runner.currentCube;   // the leg it missed on (currentCube is a leg's first cube)
        flashLane = l == Lesson.L2_SeeWhatHappens;

        slow = false;
        filter = Filter.Silent;
        ui.Hide();
        ui.HandsOff(false);
        arc.HideAll();
        ghost.Hide();
    }

    void TickFail()
    {
        failT += Time.unscaledDeltaTime;
        // L2: the lane on the face that would have worked flashes twice while the miss plays out.
        if (arrows != null)
            arrows.tutorialLaneFlash = flashLane ? 2f * Mathf.Max(0f, Mathf.Sin(failT / Mathf.Max(0.05f, st.failSeconds) * Mathf.PI * 4f)) : 0f;
        if (failT < st.failSeconds) return;

        if (arrows != null) arrows.tutorialLaneFlash = 0f;
        ui.SetRewind(true);
        float to = failCornerTime - st.rewindToBeforeCorner;
        if (!rewind.Begin(to, st.rewindSeconds, OnRewound, failCube))
        {
            ui.SetRewind(false);
            failing = false;
            FullRebuild("no history to rewind");
        }
    }

    void OnRewound()
    {
        ui.SetRewind(false);
        failing = false;
        int restored = gen.TutorialRestoreCoins(runner.transform.position, gen.PlayerSegment);
        if (restored > 0 && gen.Coins != null) gen.Coins.currentScore = Mathf.Max(0, gen.Coins.currentScore - restored);
        cornerLeg = -2;          // the corner starts again, with its retry hint
        lastOpenTapTime = -10f;
        lastLogLeg = gen.TutorialLegOf(gen.PlayerSegment);
        Log("[Tutorial] rewound to leg " + lastLogLeg + " side=" + runner.currentSideIndex + " (coins back: " + restored + ")");
    }

    // ------------------------------------------------------------------ L8: hand-over

    void Handoff()
    {
        done = true;
        Running = false;
        PlayerPrefs.SetInt(PrefKey, 1);
        PlayerPrefs.Save();

        slow = false;
        RestoreTime();
        ui.Hide();
        ui.SetRest(false, false);
        arc.HideAll();
        ghost.Hide();

        runner.tutorialInputFilter = null;
        runner.forceTapMode = false;
        runner.suppressGameOver = false;
        runner.tutorialOwnsTouch = false;
        if (arrows != null)
        {
            arrows.tutorialMode = false;
            arrows.tutorialBoost = 0f;
            arrows.tutorialLaneFlash = 0f;
        }
        gen.EndTutorial();

        // A ring from the runner, a wave of light along the track ahead.
        float cs = CubeScale;
        fx.RingBurst(runner.transform.position, cs);
        fx.Sparks(runner.transform.position, cs);
        if (runner.currentCube != null)
        {
            Transform cube = runner.currentCube;
            Vector3 n = runner.SideNormal(runner.currentSideIndex);
            Vector3 start = cube.position + n * (cs * 0.52f) + cube.forward * Vector3.Dot(runner.transform.position - cube.position, cube.forward);
            fx.TrackWave(start, n, cube.forward, cs, cs);
        }
        Haptics.MediumImpact();

        handoverTime = Time.unscaledTime;
        deathWatch = true;
        wasGameOver = false;
        TutorialTelemetry.Completed();
        Log("[Tutorial] hand-over");
        StartCoroutine(RampSpeedAndHud());
    }

    IEnumerator RampSpeedAndHud()
    {
        float start = runner.forwardSpeed;
        float t = 0f;
        float total = Mathf.Max(rampSeconds, hudFadeSeconds, 0.01f);
        while (t < total)
        {
            t += Time.deltaTime;
            TutorialHud.Alpha = TutorialEase.OutCubic(t / Mathf.Max(0.01f, hudFadeSeconds));
            // Never pull the speed DOWN: the generator's own score steps may have overtaken the ramp.
            float k = TutorialEase.InOutCubic(t / Mathf.Max(0.01f, rampSeconds));
            runner.forwardSpeed = Mathf.Max(runner.forwardSpeed, Mathf.Lerp(start, fullSpeed, k));
            yield return null;
        }
        TutorialHud.Alpha = 1f;
    }

    void TickAfterHandoff()
    {
        TickObstacleCleanup();
        if (!deathWatch || runner == null) return;
        bool over = runner.IsGameOver;
        if (over && !wasGameOver) TutorialTelemetry.DeathAfterHandover();
        wasGameOver = over;
        if (Time.unscaledTime - handoverTime >= 30f)
        {
            deathWatch = false;
            TutorialTelemetry.Report("30 s after the hand-over");
        }
    }

    // ------------------------------------------------------------------ log

    public readonly List<string> events = new List<string>();
    void Log(string s) { events.Add(Time.time.ToString("0.0") + " " + s); Debug.Log(s); }

    /// <summary>For tests: the screen side the tutorial is prompting, 0 when none.</summary>
    public int WantedTap => prompted && !succeeded ? promptSide : 0;
    public bool InSlowMotion => slow;
    public bool Rewinding => failing || (rewind != null && rewind.Playing);
    /// <summary>For tests: the half the tutorial is asking for right now (L0 included), 0 when none.</summary>
    public int PromptedSide => filter == Filter.Prompt || (prompted && !succeeded) ? promptSide : 0;
    /// <summary>For tests: taps the current prompt wants (2 = the opposite face).</summary>
    public int PromptedTaps => tapsWanted;
    public bool IsDone => done;
}
