using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MainScene prop camera 3.6 only (on the "CheckpointRun" object): the run-level glue between the game and the save.
///
///   Wallet       The run's coins (CoinCollector.currentScore) are banked into SaveSystem at game over, before any
///                restart, when the app is paused/quit and when the scene unloads. Only the part not yet banked is added
///                (bankedThisRun), so nothing is ever counted twice.
///   Unlocks      Every door crossing (PortalZoneController.DoorCrossed) unlocks checkpoint portal + 1 and records a
///                CheckpointSnapshot of that exact moment.
///   Start        GameLaunchRequest (set by MainUi) picks the checkpoint. Checkpoint N > 1: the generator lays the
///                runway out of portal N - 1 (DynamicPathGenerator.Checkpoint.cs), the director puts portal N - 2's look
///                in force, and the very same crossing a runner gets at that door is played - wipe from the previous look,
///                camera cinematic, speed dip, "CHECKPOINT N" label. Coins and streak start fresh.
///   Restart      On a checkpoint run, tap-anywhere restarts at that checkpoint; FROM START (beside MENU) goes back to 1.
/// </summary>
[DefaultExecutionOrder(1000)]   // Awake after LookTransitionDirector (-1000) has applied the set; Start after everyone's Start
public class CheckpointRunController : MonoBehaviour
{
    [Tooltip("Empty = the LookTransitionDirector's set.")]
    public CheckpointSet checkpoints;
    [Tooltip("Editor only: start here when 3.6 is played directly (not from the menu). 0 or 1 = a normal start.")]
    public int editorStartCheckpoint = 0;
    [Tooltip("A run launched from the menu starts by itself after this many seconds (no second PLAY tap). Checkpoint starts go at once.")]
    public float menuStartDelay = 0.35f;
    [Tooltip("Dev builds and the editor: compare a checkpoint start with the snapshot a normal run recorded, and log any difference.")]
    public bool devCompareSnapshots = true;

    DynamicPathGenerator gen;
    CubeRailRunner runner;
    PortalZoneController zones;
    LookTransitionDirector director;
    CameraParentRotator rig;
    RunnerHUD hud;
    GameOverSequence seq;
    CoinCollector collector;

    int startCheckpoint = 1;
    bool fromMenu, crossPending, wasGameOver, running;
    float beginAt = -1f, runStart, enteredAt = -100f;
    int bankedThisRun, reachedCheckpoint = 1;   // reachedCheckpoint: the last door crossed this run (a death restarts there)
    GameObject fromStartButton;
    RectTransform fromStartRect;

    /// <summary>The checkpoint this run (and its restarts) begins at: 1 = the run start.</summary>
    public int StartCheckpoint => startCheckpoint;
    public bool IsCheckpointRun => startCheckpoint > 1;
    public int MaxCheckpoint => checkpoints != null ? checkpoints.Count : 10;
    /// <summary>True while a checkpoint start is coming out of its door (from the load until the cinematic hands the camera back): no pausing then.</summary>
    public bool InCheckpointEntrance => crossPending || (IsCheckpointRun && Time.unscaledTime - enteredAt < 6f && director != null && (director.CameraCinematicActive || Time.unscaledTime - enteredAt < 0.5f));
    /// <summary>Raised after the checkpoint start's door crossing has been played (argument: the checkpoint).</summary>
    public event System.Action<int> CheckpointEntered;

    void Awake()
    {
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        runner = FindFirstObjectByType<CubeRailRunner>();
        zones = FindFirstObjectByType<PortalZoneController>();
        director = FindFirstObjectByType<LookTransitionDirector>();
        rig = FindFirstObjectByType<CameraParentRotator>();
        if (checkpoints == null && director != null) checkpoints = director.checkpoints;
        SaveSystem.EnsureLoaded();

        int cp = GameLaunchRequest.Consume(out fromMenu);
#if UNITY_EDITOR
        if (!fromMenu && editorStartCheckpoint > 1) cp = editorStartCheckpoint;
#endif
        if (fromMenu && cp > SaveSystem.HighestCheckpoint) cp = SaveSystem.HighestCheckpoint;   // never past what is unlocked
        // ENDLESS (MainUi's infinity button): this level's theme forever, no door, score from 0, own best per level.
        // Set before the path is built.
        bool endless = fromMenu && GameLaunchRequest.ConsumedEndless;
        if (gen != null) gen.endlessLevel = endless ? cp : 0;
        if (gen != null) gen.levelScoreFromZero = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("3.11");
        SetStart(cp);
        EndlessRun.Level = endless ? startCheckpoint : 0;
        if (gen != null) gen.endlessLevel = EndlessRun.Level;
    }

    // Before the generator builds a path: where it starts, and the runway the next door's cinematic needs.
    void SetStart(int cp)
    {
        ApplyStart(cp);
        if (fromStartButton != null) fromStartButton.SetActive(false);
    }

    void ApplyStart(int cp)
    {
        startCheckpoint = Mathf.Clamp(cp, 1, MaxCheckpoint);
        if (gen != null && zones != null && director != null && gen.portalZones)
        {
            gen.checkpointStartPortal = startCheckpoint - 1;
            if (startCheckpoint > 1) director.RequestRunwayFor(startCheckpoint - 1);
        }
        else startCheckpoint = 1;
        reachedCheckpoint = startCheckpoint;
    }

    void Start()
    {
        if (runner != null)
        {
            hud = runner.GetComponent<RunnerHUD>();
            seq = runner.GetComponent<GameOverSequence>();
        }
        if (zones != null) { zones.DoorCrossed += OnDoorCrossed; zones.OpeningRestored += OnOpeningRestored; }
        SaveSystem.BeforeAppSuspend += BankCoins;
        BuildFromStartButton();
        // Endless runs mark the level's best on the track (gate + burst + themed popup).
        if (EndlessRun.Active && GetComponent<EndlessHighScore>() == null)
            gameObject.AddComponent<EndlessHighScore>().Init(checkpoints != null ? checkpoints.Get(startCheckpoint) : null);

        if (startCheckpoint > 1)
        {
            if (director != null) director.JumpToPortal(startCheckpoint - 2);
            crossPending = true;           // next LateUpdate: the camera has framed the door zone once
        }
        if (hud != null) hud.hidePlayPanel = true;
        // The first-run tutorial starts the run itself and shows its own hints: no tap-to-play thumb on top of them.
        if (TutorialManager.Running) return;
        // The run (or the checkpoint door) waits for a tap anywhere; restarts later go at once.
        waitingForTap = true;
    }

    // ------------------------------------------------------------------ tap to play (scene start only)

    bool waitingForTap, holdTouch;
    GameObject tapPrompt;
    TutorialUIController tapUI;
    float sideTimer;
    int promptSide = 1;

    // No words: the tutorial's ghost thumb presses on a glowing screen half, swapping sides, until the first tap.
    void BuildTapPrompt()
    {
        tapUI = TutorialUIController.Build();
        tapPrompt = tapUI.gameObject;
        tapUI.ShowPrompt(promptSide, TutorialUIController.PromptKind.Tap, false, true, true);
    }

    void UpdateTapToPlay()
    {
        if (holdTouch)
        {
            // the starting tap is swallowed until the finger lifts (it must never also roll the runner)
            if (Input.touchCount == 0 && !Input.GetMouseButton(0)) { holdTouch = false; runner.pauseInput = false; }
        }
        if (!waitingForTap) return;
        // No tap needed: the run starts as soon as the scene is ready.
        if (!runner.IsWaitingToStart) return;
        waitingForTap = false;
        if (!crossPending) runner.BeginRun();   // a checkpoint start crosses its door in LateUpdate
    }

    void OnDestroy()
    {
        BankCoins();
        EndlessRun.Level = 0;
        if (zones != null) { zones.DoorCrossed -= OnDoorCrossed; zones.OpeningRestored -= OnOpeningRestored; }
        SaveSystem.BeforeAppSuspend -= BankCoins;
        if (hud != null) hud.tapBlocker = null;
        SaveSystem.Flush();
    }

    void Update()
    {
        if (runner == null) return;
        if (collector == null) collector = runner.GetComponent<CoinCollector>();

        // A restart zeroes the run's coin count (everything was banked before it): start the run's tally over.
        int coins = collector != null ? collector.currentScore : 0;
        if (coins < bankedThisRun) bankedThisRun = 0;

        bool over = runner.IsGameOver;
        if (over && !wasGameOver)
        {
            BankCoins();
            AdaptiveEasing.NoteDeath(reachedCheckpoint);   // scene 3.10 only: inert unless its profile has adaptiveEasing on
            // Tap to restart goes back to the checkpoint the run died in, not to where the run was started.
            if (reachedCheckpoint > startCheckpoint) ApplyStart(reachedCheckpoint);
        }
        wasGameOver = over;

        if (runner.IsRunning && !running) runStart = Time.time;
        running = runner.IsRunning;

        // The menu promises "reach score X": a checkpoint unlocks at its score even if its door came a few legs late.
        // (The snapshot is still taken at the real door; RecordCheckpoint keeps the first normal-run one.)
        if (gen != null && checkpoints != null && running && !EndlessRun.Active && !gen.levelScoreFromZero)
        {
            int next = SaveSystem.HighestCheckpoint + 1;
            if (next <= MaxCheckpoint && gen.Score >= checkpoints.NominalLeg(next)) SaveSystem.RecordCheckpoint(next, null, MaxCheckpoint);
        }

        UpdateTapToPlay();

        if (fromStartButton != null)
        {
            bool show = IsCheckpointRun && !EndlessRun.Active && over && hud != null && hud.GameOverPanelRect != null && hud.GameOverPanelRect.gameObject.activeInHierarchy;
            if (fromStartButton.activeSelf != show)
            {
                fromStartButton.SetActive(show);
                // MENU in the panel button row (3.6 HUD): the pair sits centred, MENU alone in the middle
                if (hud.MenuInRow)
                {
                    RectTransform menu = hud.MenuButtonRect;
                    float y = menu.anchoredPosition.y;
                    menu.anchoredPosition = new Vector2(show ? (fromStartRect.sizeDelta.x + 24f) * 0.5f : 0f, y);
                    fromStartRect.anchoredPosition = new Vector2(show ? -(menu.sizeDelta.x + 24f) * 0.5f : 0f, y);
                }
            }
        }
    }

    // LateUpdate: a soft restart flips the runner to Playing in a coroutine (after Update, before LateUpdate), so the
    // crossing lands on that very frame, before any physics step moves it.
    void LateUpdate()
    {
        if (!crossPending || runner == null) return;
        if (runner.IsRebuilding || runner.IsGameOver || waitingForTap)
        {
            if (seq != null) seq.HideRunner();   // out of sight until it comes out of the door
            return;
        }
        crossPending = false;
        bool crossed = zones != null && zones.CrossFromCheckpoint();
        if (!crossed) Debug.LogWarning("[Checkpoint] could not cross into checkpoint " + startCheckpoint + " (no runway out).", this);
        if (runner.IsWaitingToStart) runner.BeginRun();
        enteredAt = Time.unscaledTime;
        CheckpointEntered?.Invoke(startCheckpoint);
    }

    // A soft restart (tap after game over): the director has just put the scene's own look back.
    void OnOpeningRestored()
    {
        if (startCheckpoint <= 1) return;
        if (director != null)
        {
            director.JumpToPortal(startCheckpoint - 2);
            director.RequestRunwayFor(startCheckpoint - 1);
        }
        crossPending = true;
    }

    // ------------------------------------------------------------------ wallet

    /// <summary>Adds the run's coins not banked yet to the wallet. Safe to call any number of times.</summary>
    public void BankCoins()
    {
        if (collector == null && runner != null) collector = runner.GetComponent<CoinCollector>();
        if (collector == null) return;
        int delta = collector.currentScore - bankedThisRun;
        if (delta <= 0) return;
        SaveSystem.AddCoins(delta);
        bankedThisRun += delta;
    }

    // ------------------------------------------------------------------ unlocks + snapshots

    void OnDoorCrossed(int portal)
    {
        int cp = portal + 1;
        AdaptiveEasing.NoteCleared(portal);            // the checkpoint just left is cleared: its easing resets
        if (cp > reachedCheckpoint) reachedCheckpoint = cp;
        CheckpointSnapshot snap = Capture(cp, portal);
        if (devCompareSnapshots && (Debug.isDebugBuild || Application.isEditor) && IsCheckpointRun && cp == startCheckpoint)
            CompareWithReference(snap);
        SaveSystem.RecordCheckpoint(cp, snap, MaxCheckpoint);
    }

    CheckpointSnapshot Capture(int cp, int portal)
    {
        var s = new CheckpointSnapshot
        {
            checkpoint = cp,
            portal = portal,
            scene = gameObject.scene.name,
            savedUtc = System.DateTime.UtcNow.ToString("o"),
            fromCheckpointStart = IsCheckpointRun,
            startedFromCheckpoint = startCheckpoint,
            runSeconds = running ? Time.time - runStart : 0f,
            portalsTaken = gen != null ? gen.PortalsTaken : 0,
        };
        if (gen != null)
        {
            s.legIndex = gen.Score;
            s.displayScore = gen.DisplayScore;
            s.segmentSerial = gen.SegmentCount > 0 && gen.PortalExitSegment != null ? gen.PortalExitSegment.serial : -1;
            s.exitSegmentSerial = gen.PortalExitSegment != null ? gen.PortalExitSegment.serial : -1;
            s.exitRunwayCubes = gen.PortalExitSegment != null ? gen.PortalExitSegment.cubes.Count : 0;
            s.difficultyTier = gen.DifficultyTier;
            s.difficultyProfile = gen.difficultyProfile != null ? gen.difficultyProfile.name : "";
            gen.CaptureState(s.generator);
        }
        if (runner != null)
        {
            s.baseSpeed = runner.forwardSpeed;
            s.speedForLeg = runner.SpeedForLeg(s.legIndex);
            s.moveSpeed = runner.CurrentMoveSpeed;
            s.maxSpeed = runner.maxSpeed;
            s.runnerSide = runner.currentSideIndex;
        }
        CheckpointProfile p = checkpoints != null ? checkpoints.Get(cp) : null;
        s.lookName = p != null && p.look != null ? p.look.name : "";
        if (rig != null)
        {
            s.cameraMode = (int)rig.Mode;
            s.cameraYaw = rig.transform.eulerAngles.y;
        }
        return s;
    }

    // Dev: the start just played against the formula and against what a normal run recorded at this door.
    void CompareWithReference(CheckpointSnapshot s)
    {
        int k = s.portal;
        int nominal = gen != null ? gen.CheckpointLeg(k) : k * 50;   // the profile's own portal schedule
        var sb = new System.Text.StringBuilder();
        sb.Append("[Checkpoint] start at checkpoint ").Append(s.checkpoint).Append(": leg ").Append(s.legIndex)
          .Append(" (formula ").Append(nominal - 1).Append("), speed ").Append(s.baseSpeed.ToString("F2"))
          .Append(" (formula ").Append(runner.SpeedForLeg(nominal - 1).ToString("F2")).Append("), cycle ").Append(s.generator.cycleIndex)
          .Append(", next portal ").Append(s.generator.nextPortalSerial).Append(", runway ").Append(s.exitRunwayCubes).Append(" cubes, look ").Append(s.lookName);
        Debug.Log(sb.ToString(), this);

        CheckpointSnapshot r = SaveSystem.GetSnapshot(s.checkpoint);
        if (r == null || r.fromCheckpointStart)
        {
            Debug.Log("[Checkpoint] no normal-run snapshot for checkpoint " + s.checkpoint + " yet - nothing to compare.", this);
            return;
        }
        int slip = r.legIndex - s.legIndex;
        int mismatches = 0;
        void Check(string what, bool ok, string detail)
        {
            if (ok) return;
            mismatches++;
            Debug.LogWarning("[Checkpoint] mismatch at checkpoint " + s.checkpoint + ": " + what + " - " + detail, this);
        }
        // A real run's door can come a few legs late (a boost in progress, no room): compare against its own leg there.
        Check("speed for the leg", Mathf.Abs(runner.SpeedForLeg(r.legIndex) - r.speedForLeg) < 0.01f, "formula " + runner.SpeedForLeg(r.legIndex).ToString("F2") + " vs recorded " + r.speedForLeg.ToString("F2"));
        Check("speed", Mathf.Abs(s.baseSpeed - runner.SpeedForLeg(s.legIndex)) < 0.05f, "start " + s.baseSpeed.ToString("F2") + " vs formula " + runner.SpeedForLeg(s.legIndex).ToString("F2"));
        Check("doors taken", s.portalsTaken == r.portalsTaken, s.portalsTaken + " vs " + r.portalsTaken);
        Check("wave cycle", s.generator.cycleIndex == r.generator.cycleIndex, s.generator.cycleIndex + " vs " + r.generator.cycleIndex);
        Check("legs to next door", s.generator.nextPortalSerial - s.exitSegmentSerial == r.generator.nextPortalSerial - r.exitSegmentSerial,
              (s.generator.nextPortalSerial - s.exitSegmentSerial) + " vs " + (r.generator.nextPortalSerial - r.exitSegmentSerial));
        Check("runway out", Mathf.Abs(s.exitRunwayCubes - r.exitRunwayCubes) <= 2, s.exitRunwayCubes + " vs " + r.exitRunwayCubes + " cubes");
        Check("difficulty tier", s.difficultyTier == r.difficultyTier || slip != 0, s.difficultyTier + " vs " + r.difficultyTier);
        Check("look", s.lookName == r.lookName, s.lookName + " vs " + r.lookName);
        Check("camera mode", s.cameraMode == r.cameraMode, s.cameraMode + " vs " + r.cameraMode);
        Check("taught decoy/bridge/multi", s.generator.taughtDecoy == r.generator.taughtDecoy && s.generator.taughtBridge == r.generator.taughtBridge
              && s.generator.taughtMulti == r.generator.taughtMulti,
              s.generator.taughtDecoy + "/" + s.generator.taughtBridge + "/" + s.generator.taughtMulti + " vs " + r.generator.taughtDecoy + "/" + r.generator.taughtBridge + "/" + r.generator.taughtMulti);
        Debug.Log("[Checkpoint] checkpoint " + s.checkpoint + " vs normal run: " + (mismatches == 0 ? "match" : mismatches + " mismatch(es)")
                  + (slip != 0 ? " (that run's door came " + slip + " leg(s) off the nominal one)" : ""), this);
    }

    // ------------------------------------------------------------------ restart choices

    /// <summary>FROM START: bank, then soft-restart the run at checkpoint 1.</summary>
    public void RestartFromStart()
    {
        BankCoins();
        SetStart(1);
        crossPending = false;
        runner.Restart();
    }

    /// <summary>Soft-restart at a given checkpoint (dev tests; the game-over tap already restarts at the run's own).</summary>
    public void RestartAt(int checkpoint)
    {
        BankCoins();
        SetStart(checkpoint);
        runner.Restart();
    }

    void BuildFromStartButton()
    {
        if (hud == null || hud.GameOverPanelRect == null || hud.MenuButtonRect == null) return;
        RectTransform menu = hud.MenuButtonRect;
        fromStartButton = Instantiate(menu.gameObject, menu.parent, false);
        fromStartButton.name = "FromStartButton";
        fromStartRect = (RectTransform)fromStartButton.transform;
        fromStartRect.anchoredPosition = menu.anchoredPosition + new Vector2(-(menu.sizeDelta.x + 24f), 0f);
        fromStartRect.sizeDelta = new Vector2(hud.MenuInRow ? menu.sizeDelta.x + 40f : 300f, menu.sizeDelta.y);
        var btn = fromStartButton.GetComponent<Button>();
        btn.onClick = new Button.ButtonClickedEvent();
        btn.onClick.AddListener(RestartFromStart);
        var label = fromStartButton.GetComponentInChildren<Text>();
        if (label != null) { label.text = "FROM START"; label.rectTransform.sizeDelta = new Vector2(290f, 90f); }
        fromStartButton.SetActive(false);
        hud.tapBlocker = pos => fromStartButton != null && fromStartButton.activeInHierarchy
                                && RectTransformUtility.RectangleContainsScreenPoint(fromStartRect, pos, null);
    }

    // ------------------------------------------------------------------ dev test

    [ContextMenu("Dev/Test start from every checkpoint")]
    public void DevTestAll()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Checkpoint] enter Play mode first."); return; }
        StartCoroutine(DevTestRoutine());
    }

    /// <summary>True while DevTestAll runs.</summary>
    public bool DevTestRunning { get; private set; }

    IEnumerator DevTestRoutine()
    {
        DevTestRunning = true;
        int original = startCheckpoint;
        var report = new System.Text.StringBuilder("checkpoint start test (" + System.DateTime.Now + ")\n");
        for (int cp = 2; cp <= MaxCheckpoint; cp++)
        {
            int entered = -1;
            System.Action<int> h = c => entered = c;
            CheckpointEntered += h;
            RestartAt(cp);
            float t0 = Time.unscaledTime;
            while (entered != cp && Time.unscaledTime - t0 < 20f) yield return null;
            CheckpointEntered -= h;
            yield return new WaitForSeconds(0.6f);   // the runway out has scored, the speed has eased in
            int k = cp - 1, expected = gen.CheckpointLeg(k);
            CheckpointSnapshot r = SaveSystem.GetSnapshot(cp);
            report.Append("CP").Append(cp).Append(entered == cp ? "" : " (NOT ENTERED)")
                  .Append(": score ").Append(gen.Score).Append(" (expected ").Append(expected).Append(")")
                  .Append(", speed ").Append(runner.forwardSpeed.ToString("F2")).Append(" (formula ").Append(runner.SpeedForLeg(expected).ToString("F2")).Append(")")
                  .Append(", doors ").Append(gen.PortalsTaken).Append(", cycle ").Append(gen.WaveCycle)
                  .Append(", look ").Append(director != null ? director.CurrentLookName : "?");
            if (r != null && !r.fromCheckpointStart)
                report.Append(" | normal run: leg ").Append(r.legIndex).Append(" speed ").Append(r.baseSpeed.ToString("F2"))
                      .Append(" cycle ").Append(r.generator.cycleIndex).Append(" runway ").Append(r.exitRunwayCubes);
            else report.Append(" | no normal-run snapshot");
            report.Append('\n');
        }
        string path = System.IO.Path.Combine(Application.persistentDataPath, "checkpoint_start_test.txt");
        System.IO.File.WriteAllText(path, report.ToString());
        Debug.Log("[Checkpoint] " + report + "-> " + path, this);
        RestartAt(original);
        DevTestRunning = false;
    }
}
