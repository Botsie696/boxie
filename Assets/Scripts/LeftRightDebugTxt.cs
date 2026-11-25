using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

// Dev-only (never part of a scene): plays runs with real SCREEN taps (CubeRailRunner.SimulateTap, the same path a key press /
// screen tap takes) and writes a per-frame left/right mapping log for the first `legs` legs of each pass: camera vs runner
// (toward / away, behind / in front), face, required face, where LEFT and RIGHT would go (CubeRailRunner.PeekPressFace - the
// game's own mapping, read without deciding anything), which rule decides, drawn / settled / mapping camera yaw, the runner
// cube's rotation, plus every tap / roll / flip / landing / mapping change.
//
// Passes (one Play session, the path re-laid from `seed` before each): "react:<s>" = plain bot that taps <s> after landing;
// "overroll" = over-rolls one-roll legs on purpose (extra roll, pause, then corrects by the face drawn on that side of the
// screen) and takes two-roll legs from top/bottom as two taps 0.5 s apart (keep-spin probe).
// Output: <outBase>_<pass>.txt per pass (leg summary + frames) and <outBase>_legs.csv (one row per leg per pass).
[DefaultExecutionOrder(2000)]
public class LeftRightDebugTxt : MonoBehaviour
{
    public int legs = 30;
    public int seed = 0;                       // 0 = unseeded
    public string[] passes = { "react:0.22" };
    public string outBase = "";
    public float doubleTapGap = 0.10f;
    public float timeScale = 1f;
    public bool drive = true;                  // false = observer: another driver (HumanSimPlayer) taps; its taps are logged as presses               // scans only; the real recordings run at 1
    public bool Done;
    public string Status = "";

    CubeRailRunner runner; DynamicPathGenerator gen; CameraParentRotator rig; Camera cam; PathDepthShading shading;
    Transform mesh;
    const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;
    FieldInfo fQueue, fArc;

    static readonly string[] Face = { "TOP", "RIGHT", "BOTTOM", "LEFT" };
    static readonly string[] TurnName = { "UP", "RIGHT", "DOWN", "LEFT" };

    // ---- per pass
    int passIdx = -1; string passName = ""; float reaction = 0.22f; bool overRollMode, keepSpinMode;
    bool needRestart, waitingRun, running;
    StringBuilder frames, legLog; readonly StringBuilder csv = new StringBuilder(1 << 16);
    readonly StringBuilder events = new StringBuilder(256);
    readonly StringBuilder summary = new StringBuilder(1 << 14);
    int firstSerial, legNo, frameNo, prevSerial, prevFace, prevSign;
    bool prevSideRemap, havePrev; Quaternion prevMeshRot; Vector3 prevMeshUp; Transform prevCube, prevCubeFrame;
    string prevQueue = "-", prevRule = "";
    float lastLand, lastOwnTap;
    int flipAlt = 1; bool fastNext = true;
    int headingHits0, rederives0, rederiveFlips0;
    // pass counters
    bool botTap; int pExternal;
    int pTaps, pDbl, pRolls, pFlips, pMismatch, pLandingFails, pLandingChecks, pWrongCorr, pCorr, pCorrUndo, pProbe, pProbeFail, pViewDiffPresses, pDeaths;

    class PressRec
    {
        public float t, dtLand; public int dir; public string kind; public int from, predicted, landingPred = -1, expected = -1, actual = -1;
        public float drawnYaw, settledYaw, mapYaw; public string rule; public bool merged;
    }
    class LegRec
    {
        public int no, serial, cubes, landFace, need, rolls, landL, landR; public string heading, camRel, why, landRule, header;
        public float drawnYaw, settledYaw, mapYaw; public readonly List<PressRec> presses = new List<PressRec>();
        public readonly List<string> events = new List<string>(); public string mapping = "";
    }
    LegRec leg;
    readonly List<PressRec> pending = new List<PressRec>();

    // driver plan
    enum Plan { None, Dbl2, OverSecond, OverCorrect, ProbeSecond }
    Plan plan; float planAt; int planDir, planTarget, planStart;
    bool overDoneThisLeg, probeDoneThisLeg;

    void Start()
    {
        runner = FindFirstObjectByType<CubeRailRunner>();
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        rig = FindFirstObjectByType<CameraParentRotator>();
        cam = rig != null && rig.boomCamera != null ? rig.boomCamera : Camera.main;
        shading = gen != null ? gen.GetComponent<PathDepthShading>() : null;
        fQueue = typeof(CubeRailRunner).GetField("inputQueue", BF);
        fArc = typeof(CubeRailRunner).GetField("revolveArc", BF);
        mesh = runner.transform.Find("VisualWrapper/PivotContainer/CubeMesh");
        if (string.IsNullOrEmpty(outBase)) outBase = Application.dataPath + "/leftRightRun";
        gen.powerDiamonds = false;   // play-mode only: the autopilot diamond rolls the runner itself and ignores taps
        runner.AutoInputIgnored += c => events.Append(" [AUTOPILOT ignored input " + c + "]");
        runner.TapTaken += d => { events.Append(d > 0 ? " TAP:RIGHT" : " TAP:LEFT"); pTaps++; if (!botTap) { pExternal++; if (drive) events.Append(" ** EXTERNAL TAP (not the bot - keyboard/touch?) **"); else ObservedTap(d); } };
        runner.Rescued += r => events.Append(" CLOSE-CALL-RESCUE(late " + r.lateSeconds.ToString("0.000") + "s)");
        csv.AppendLine("pass;leg;serial;cubes;heading;camRel;landFace;need;rolls;landL;landR;landRule;drawnYaw;settledYaw;mapYaw;presses(kind:dir:from->actual/pred/landPred@dt)");
        summary.AppendLine("scene " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + ", seed " + seed + ", toggles A/B/C/D = "
            + runner.sideWallRunnerRule + "/" + runner.keepSpinOnSideWall + "/" + runner.settleRederiveClearFaces + "/" + runner.useDrawnViewForMapping);
        NextPass();
    }

    void NextPass()
    {
        passIdx++;
        if (passIdx >= passes.Length) { Finish(); return; }
        passName = passes[passIdx];
        int passSeed = seed;
        int at = passName.IndexOf("@");
        if (at >= 0) { int.TryParse(passName.Substring(at + 1), out passSeed); }
        string kindSpec = at >= 0 ? passName.Substring(0, at) : passName;
        overRollMode = kindSpec.StartsWith("overroll");
        keepSpinMode = kindSpec.StartsWith("keepspin");
        reaction = 0.22f;
        if (kindSpec.StartsWith("react:")) float.TryParse(kindSpec.Substring(6), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out reaction);
        yawsSeen.Clear();
        frames = new StringBuilder(1 << 21); legLog = new StringBuilder(1 << 16);
        firstSerial = -1; legNo = 0; frameNo = 0; prevSerial = -999; prevFace = -1; prevSign = 0; havePrev = false; prevCube = null; leg = null;
        pending.Clear(); plan = Plan.None; flipAlt = 1; fastNext = true; lastOwnTap = -10f;
        pExternal = 0;
        pTaps = pDbl = pRolls = pFlips = pMismatch = pLandingFails = pLandingChecks = pWrongCorr = pCorr = pCorrUndo = pProbe = pProbeFail = pViewDiffPresses = pDeaths = 0;
        headingHits0 = runner.HeadingRuleHits; rederives0 = lastRederives = runner.SettleRederives; rederiveFlips0 = runner.SettleRederiveFlips;
        frames.AppendLine("PASS " + passName + " (reaction " + reaction.ToString("0.00") + " s" + (overRollMode ? ", over-roll + keep-spin probe" : "") + ")");
        frames.AppendLine("COLUMNS: f=frame t=game time leg cube a/b face need | heading cam yaw(drawn/settled/map) | L->face[drawn spot] R->face[drawn spot] sign rule | q | rot dRot top->  EVENTS");
        if (passSeed != 0) gen.SeedRuns(passSeed);
        Time.timeScale = timeScale;
        needRestart = true; waitingRun = false; running = false;
        Status = "pass " + passName;
    }

    // ---------------------------------------------------------------- helpers
    int QueueCount => ((Queue<int>)fQueue.GetValue(runner)).Count;
    string QueueText()
    {
        var q = ((Queue<int>)fQueue.GetValue(runner)).ToArray();
        if (q.Length == 0) return "-";
        var s = new StringBuilder();
        foreach (int c in q) s.Append(c == -1 ? "L" : c == 1 ? "R" : c == 2 ? "FLIP" : c == 3 ? "DBL" : c == 10 ? "raw+" : c == -10 ? "raw-" : c.ToString()).Append(',');
        return s.ToString().TrimEnd(',');
    }
    Vector3 N(Transform cube, int side)
    {
        switch (side) { case 0: return cube.up; case 1: return cube.right; case 2: return -cube.up; default: return -cube.right; }
    }
    static int SideOf(Transform cube, Vector3 n)
    {
        Vector3 l = cube.InverseTransformDirection(n);
        if (Mathf.Abs(l.y) > Mathf.Abs(l.x)) return l.y > 0 ? 0 : 2;
        return l.x > 0 ? 1 : 3;
    }
    static string W(Vector3 v)
    {
        float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
        if (ay >= ax && ay >= az) return v.y > 0 ? "+Y(up)" : "-Y(down)";
        if (ax >= az) return v.x > 0 ? "+X" : "-X";
        return v.z > 0 ? "+Z" : "-Z";
    }
    static string Scr(Vector3 v, Quaternion view)
    {
        Vector3 r = view * Vector3.right, u = view * Vector3.up;
        float vm = v.magnitude; if (vm < 1e-6f) return "-";
        float x = Vector3.Dot(v, r) / vm, y = Vector3.Dot(v, u) / vm, m = Mathf.Sqrt(x * x + y * y);
        if (m < 0.2f) return "into/out of screen";
        string s = "";
        if (Mathf.Abs(y) > 0.38f * m) s += y > 0 ? "UP" : "DOWN";
        if (Mathf.Abs(x) > 0.38f * m) s += (s.Length > 0 ? "-" : "") + (x > 0 ? "RIGHT" : "LEFT");
        return "screen-" + s;
    }
    string CamRelation(Vector3 fwd, out string shortTxt)
    {
        Vector3 cf = cam.transform.forward; cf.y = 0f; cf.Normalize();
        if (Mathf.Abs(fwd.y) > 0.7f)
        {
            shortTxt = fwd.y > 0 ? "VERT-UP" : "VERT-DOWN";
            return (fwd.y > 0 ? "climbing straight UP" : "dropping straight DOWN") + " (vertical leg, runs up/down the screen; camera neither behind nor in front)";
        }
        Vector3 h = fwd; h.y = 0f; h.Normalize();
        float d = Vector3.Dot(h, cf);
        Vector3 toCam = cam.transform.position - runner.transform.position; toCam.y = 0f;
        string camSide = Vector3.Dot(toCam, Vector3.Cross(Vector3.up, h)) > 0f ? "runner's RIGHT" : "runner's LEFT";
        if (d > 0.3f) { shortTxt = "AWAY d" + d.ToString("+0.00"); return "AWAY from camera (camera BEHIND the runner, also off to the " + camSide + "), d=" + d.ToString("+0.00;-0.00"); }
        if (d < -0.3f) { shortTxt = "TOWARD d" + d.ToString("+0.00;-0.00"); return "TOWARD the camera (camera IN FRONT of the runner, also off to the " + camSide + "), d=" + d.ToString("+0.00;-0.00"); }
        shortTxt = "SIDEWAYS d" + d.ToString("+0.00;-0.00");
        return "SIDEWAYS across the view (camera beside the runner, on the " + camSide + "), d=" + d.ToString("+0.00;-0.00");
    }
    float DrawnYaw => cam.transform.eulerAngles.y;
    float SettledYaw => rig != null ? rig.SettledViewRotation.eulerAngles.y : DrawnYaw;
    float MapYaw => runner.MappingViewRotation.eulerAngles.y;
    static string Y(float y) => y.ToString("0.0");

    bool NextTarget(out int face, out float dist, out string why)
    {
        face = -1; dist = 0f; why = "";
        var seg = gen.PlayerSegment;
        Transform cube = runner.currentCube;
        if (seg == null || cube == null) return false;
        float along = Vector3.Dot(runner.transform.position - cube.position, cube.forward) / gen.cubeSize;
        foreach (var p in seg.planks)
        {
            if (along > p.y + 0.5f) continue;
            face = p.z; dist = Mathf.Max(along, p.x - 0.5f); why = "plank"; return true;
        }
        if (seg.portalRole == 1) { face = gen.PortalEntrySide; dist = seg.cubes.Count - 1; why = "portal door"; return true; }
        if (seg.turnSide >= 0) { face = seg.turnSide; dist = seg.cubes.Count - 1.05f; why = "corner"; return true; }
        var corners = gen.GetUpcomingCorners(1);
        if (corners.Count == 0) return false;
        face = runner.SideFacing(corners[0].outgoingDirection);
        dist = seg.cubes.Count - 1.05f; why = "corner";
        return true;
    }

    // ---------------------------------------------------------------- driver (screen taps only)
    void Tap(int dir, string kind, int predicted, int expected = -1)
    {
        var p = new PressRec
        {
            t = Time.time, dtLand = Time.time - lastLand, dir = dir, kind = kind, from = runner.currentSideIndex,
            predicted = predicted, expected = expected, drawnYaw = DrawnYaw, settledYaw = SettledYaw, mapYaw = MapYaw
        };
        runner.PeekPressFace(dir, out p.rule);
        if (leg != null && runner.currentSideIndex == leg.landFace && leg.presses.Count == 0 && kind != "dbl2")
            p.landingPred = dir > 0 ? leg.landR : leg.landL;
        if (Mathf.Abs(Mathf.DeltaAngle(p.drawnYaw, p.settledYaw)) > 10f) pViewDiffPresses++;
        events.Append(" [bot " + kind + " " + (dir > 0 ? "RIGHT" : "LEFT") + " from " + Face[p.from] + ", expects " + Face[predicted] + (p.landingPred >= 0 ? ", landing said " + Face[p.landingPred] : "") + "]");
        if (leg != null) leg.presses.Add(p);
        pending.Add(p);
        botTap = true;
        runner.SimulateTap(dir);
        botTap = false;
        lastOwnTap = Time.time;
    }

    void ObservedTap(int dir)
    {
        if (leg == null) return;
        var p = new PressRec { t = Time.time, dtLand = Time.time - lastLand, dir = dir, kind = "sim", from = runner.currentSideIndex, drawnYaw = DrawnYaw, settledYaw = SettledYaw, mapYaw = MapYaw };
        p.predicted = QueueCount == 0 ? runner.PeekPressFace(dir, out p.rule) : -1;
        if (p.predicted < 0) { p.predicted = runner.currentSideIndex; p.rule = "queued behind another press"; }
        if (runner.currentSideIndex == leg.landFace && leg.presses.Count == 0) p.landingPred = dir > 0 ? leg.landR : leg.landL;
        NextTarget(out p.expected, out _, out _);
        leg.presses.Add(p); pending.Add(p);
    }

    int DirTo(int target)
    {
        if (runner.PeekPressFace(-1, out _) == target) return -1;
        if (runner.PeekPressFace(1, out _) == target) return 1;
        return 0;
    }

    void Update()
    {
        if (Done || runner == null || gen == null) return;
        if (needRestart)
        {
            if (runner.IsRunning || runner.IsWaitingToStart || runner.IsGameOver) { needRestart = false; waitingRun = true; runner.Restart(); }
            return;
        }
        if (runner.IsWaitingToStart && drive) { runner.BeginRun(); return; }
        if (waitingRun) { if (!runner.IsRunning || runner.currentCube == null) return; waitingRun = false; running = true; }
        if (!running || !runner.IsRunning || runner.currentCube == null) return;
        if (!CheckLeg()) return;
        if (drive) Drive();
    }

    void Drive()
    {
        int cur = runner.currentSideIndex;
        if (plan != Plan.None)
        {
            if (Time.time < planAt) return;
            if (plan == Plan.Dbl2)
            {
                plan = Plan.None; pDbl++;
                Tap(planDir, "dbl2", (planStart + 2) % 4, (planStart + 2) % 4);
                return;
            }
            if (plan == Plan.OverSecond)
            {
                if (QueueCount > 0 || runner.IsRevolving) return;
                plan = Plan.OverCorrect; planAt = Time.time + 0.10f + 0.30f;   // roll ~0.10 s, then a 0.3 s pause
                Tap(planDir, "over-roll", runner.PeekPressFace(planDir, out _));
                return;
            }
            if (plan == Plan.OverCorrect)
            {
                if (QueueCount > 0 || runner.IsRevolving) { planAt = Time.time + 0.3f; return; }
                plan = Plan.None;
                int T = planTarget, O = cur;
                if (O == T) return;
                if (DynamicPathGenerator.Rolls(O, T) != 1) { events.Append(" [bot: over-roll left the runner opposite the target, plain recovery]"); return; }
                int U = (T + 2) % 4;   // the other neighbour of O... (O's neighbours are T and the face opposite T)
                Transform c = runner.currentCube;
                Vector3 camRight = cam.transform.right;
                float dxT = Vector3.Dot(N(c, T) - N(c, O), camRight), dxU = Vector3.Dot(N(c, U) - N(c, O), camRight);
                pCorr++;
                if (Mathf.Abs(dxT - dxU) > 0.3f)
                {
                    int d = dxT > dxU ? 1 : -1;
                    Tap(d, "correct-by-screen(" + Face[T] + " drawn " + (d > 0 ? "right" : "left") + ")", runner.PeekPressFace(d, out _), T);
                }
                else
                {
                    pCorrUndo++;
                    Tap(-planDir, "correct-undo(" + Face[T] + " drawn straight above/below)", runner.PeekPressFace(-planDir, out _), T);
                }
                return;
            }
            if (plan == Plan.ProbeSecond)
            {
                if (QueueCount > 0 || runner.IsRevolving) { planAt = Time.time + 0.5f; return; }
                pProbe++;
                int exp = (planStart + 2) % 4;
                Tap(planDir, "probe-second(same key 0.5 s later)", runner.PeekPressFace(planDir, out _), exp);
                if (planTarget != exp) { plan = Plan.OverCorrect; planAt = Time.time + 0.10f + 0.30f; }   // then back to the target
                else plan = Plan.None;
                return;
            }
        }

        if (!NextTarget(out int target, out float dist, out _)) return;
        if (cur == target || QueueCount > 0 || runner.IsRevolving) return;
        if (Time.time - lastLand < reaction || Time.time - lastOwnTap < Mathf.Max(0.22f, reaction)) return;
        Transform cube = runner.currentCube;
        float along = Vector3.Dot(runner.transform.position - cube.position, cube.forward) / gen.cubeSize;
        float visible = shading != null && shading.enabled ? shading.VisibleAheadCubes : 24f;
        if (dist - along > visible) return;
        float secondsLeft = (dist - along) * gen.cubeSize / Mathf.Max(0.01f, runner.CurrentMoveSpeed);
        bool horizontal = Mathf.Abs(cube.forward.y) < 0.7f;
        var seg = gen.PlayerSegment;
        bool plain = seg == null || seg.planks.Count == 0;

        int rolls = DynamicPathGenerator.Rolls(cur, target);
        if (rolls == 1)
        {
            int d = DirTo(target);
            if (d == 0) { events.Append(" [bot: NO single press reaches " + Face[target] + "!]"); return; }
            if (keepSpinMode && !probeDoneThisLeg && plain && horizontal && (cur == 0 || cur == 2) && (target == 1 || target == 3) && secondsLeft >= 1.25f)
            {
                // Fix B repro: top/bottom -> side wall, wait 0.5 s on the wall, same key again must carry on round
                probeDoneThisLeg = true;
                Tap(d, "spin-first", target, target);
                plan = Plan.ProbeSecond; planAt = Time.time + 0.10f + 0.50f; planDir = d; planStart = cur; planTarget = target;
                return;
            }
            if (overRollMode && !overDoneThisLeg && plain && secondsLeft >= 0.95f)
            {
                overDoneThisLeg = true;
                Tap(d, "over-first", target, target);
                plan = Plan.OverSecond; planAt = Time.time + 0.25f; planDir = d; planTarget = target;
                return;
            }
            Tap(d, "normal", target, target);
        }
        else
        {
            if (keepSpinMode && !probeDoneThisLeg && plain && horizontal && (cur == 0 || cur == 2) && secondsLeft >= 0.9f)
            {
                probeDoneThisLeg = true;
                int dp = flipAlt; flipAlt = -flipAlt;
                Tap(dp, "probe-first", runner.PeekPressFace(dp, out _));
                plan = Plan.ProbeSecond; planAt = Time.time + 0.10f + 0.50f; planDir = dp; planStart = cur; planTarget = target;
                return;
            }
            int d2 = flipAlt; flipAlt = -flipAlt;
            float gap = fastNext ? 0.04f : doubleTapGap; fastNext = !fastNext;
            Tap(d2, "dbl1(gap " + gap.ToString("0.00") + ")", runner.PeekPressFace(d2, out _));
            plan = Plan.Dbl2; planAt = Time.time + gap; planDir = d2; planStart = cur; planTarget = target;
        }
    }

    // ---------------------------------------------------------------- legs
    bool CheckLeg()
    {
        var seg = gen.PlayerSegment;
        if (seg == null || runner.currentCube == null) return false;
        if (seg.serial == prevSerial) return true;
        Transform cube = runner.currentCube;
        Vector3 fwd = cube.forward;
        int face = runner.currentSideIndex;
        if (firstSerial < 0) firstSerial = seg.serial;
        FlushLeg();
        legNo = seg.serial - firstSerial + 1;
        prevSerial = seg.serial;
        lastLand = Time.time;
        plan = Plan.None; overDoneThisLeg = probeDoneThisLeg = false;
        if (legNo > legs) { EndPass("reached leg " + legs); return false; }

        leg = new LegRec { no = legNo, serial = seg.serial, cubes = seg.cubes.Count, landFace = face, drawnYaw = DrawnYaw, settledYaw = SettledYaw, mapYaw = MapYaw };
        string turnIn = "";
        if (prevCube != null && Vector3.Angle(prevCube.forward, fwd) > 30f) turnIn = "came in by turning " + TurnName[SideOf(prevCube, fwd)] + " off the last leg";
        prevCube = cube;
        NextTarget(out int tgt, out _, out string why);
        leg.need = tgt; leg.why = why; leg.rolls = tgt >= 0 ? DynamicPathGenerator.Rolls(face, tgt) : 0;
        string outTxt;
        var corners = gen.GetUpcomingCorners(1);
        if (corners.Count > 0 && Vector3.Dot(corners[0].incomingDirection.normalized, fwd) > 0.9f)
        {
            Vector3 o = corners[0].outgoingDirection.normalized;
            outTxt = "ends with a turn " + TurnName[SideOf(cube, o)] + " (runner-relative) to world " + W(o) + ", " + Scr(o, cam.transform.rotation) + " -> must be on face " + Face[SideOf(cube, o)];
        }
        else outTxt = seg.portalRole == 1 ? "portal ENTRY runway: must be on face " + Face[gen.PortalEntrySide] + " at the door"
                    : seg.portalRole == 2 ? "portal EXIT runway (no corner check)" : "no corner after it yet";
        leg.camRel = CamRelation(fwd, out string camShort);
        leg.heading = W(fwd) + " " + Scr(fwd, cam.transform.rotation);
        leg.landL = runner.PeekPressFace(-1, out leg.landRule);
        leg.landR = runner.PeekPressFace(1, out _);
        var sbh = new StringBuilder();
        sbh.AppendLine("------------------------------------------------------------------------------------------------");
        sbh.AppendLine("LEG " + legNo + "  (serial " + seg.serial + ", " + seg.cubes.Count + " cubes" + (seg.boost ? ", BOOST leg " + seg.boostLeg : "") + (seg.portalRole == 1 ? ", PORTAL ENTRY" : seg.portalRole == 2 ? ", PORTAL EXIT" : "") + (seg.planks.Count > 0 ? ", " + seg.planks.Count + " plank(s)" : "") + ")");
        sbh.AppendLine("  heading      : world " + leg.heading + "  " + (turnIn.Length > 0 ? "(" + turnIn + ")" : ""));
        sbh.AppendLine("  camera       : " + leg.camRel + "; yaw drawn " + Y(leg.drawnYaw) + " / settled " + Y(leg.settledYaw) + " / mapping " + Y(leg.mapYaw));
        sbh.AppendLine("  landed face  : " + Face[face] + "   needed: " + (tgt >= 0 ? Face[tgt] + " (" + why + ")  rolls needed " + leg.rolls + (leg.rolls == 2 ? " => DOUBLE-TAP flip" : "") : "-"));
        sbh.AppendLine("  corner       : " + outTxt);
        for (int i = 0; i < seg.planks.Count; i++) sbh.AppendLine("  plank        : cubes " + seg.planks[i].x + "-" + seg.planks[i].y + " must be on " + Face[seg.planks[i].z]);
        sbh.Append("  faces on screen (cube centre -> face):");
        for (int s = 0; s < 4; s++) sbh.Append("  " + Face[s] + " " + Scr(N(cube, s), cam.transform.rotation));
        sbh.AppendLine();
        sbh.AppendLine("  leg cube rot : " + cube.eulerAngles.ToString("0") + "  (fwd " + W(fwd) + ", up " + W(cube.up) + ", right " + W(cube.right) + ")");
        sbh.AppendLine("  at landing   : LEFT -> " + Face[leg.landL] + "  RIGHT -> " + Face[leg.landR] + "   rule: " + leg.landRule);
        leg.header = sbh.ToString();
        frames.Append(leg.header);
        return true;
    }

    void FlushLeg()
    {
        if (leg == null) return;
        // any press still waiting for its roll was swallowed (merged into a flip, or dropped)
        foreach (var p in pending) if (p.actual < 0) p.merged = true;
        pending.Clear();
        var sb = new StringBuilder(leg.header);
        sb.AppendLine("  left/right   : " + (leg.mapping.Length > 0 ? leg.mapping : "no mapping change this leg"));
        var pr = new StringBuilder();
        foreach (var p in leg.presses)
        {
            string res = p.merged ? "merged/dropped" : p.actual < 0 ? "?" : Face[p.actual];
            bool bad = !p.merged && p.actual >= 0 && p.actual != p.predicted;
            sb.AppendLine("  press        : +" + p.dtLand.ToString("0.00") + "s " + p.kind + " " + (p.dir > 0 ? "RIGHT" : "LEFT") + " from " + Face[p.from] + " -> " + res
                + "  (expected " + Face[p.predicted] + (p.landingPred >= 0 ? ", landing said " + Face[p.landingPred] : "") + (p.expected >= 0 ? ", target " + Face[p.expected] : "") + ")"
                + "  yaw drawn " + Y(p.drawnYaw) + " settled " + Y(p.settledYaw) + " map " + Y(p.mapYaw) + "  rule " + p.rule + (bad ? "  ** MISMATCH **" : ""));
            pr.Append(p.kind.Split('(')[0]).Append(':').Append(p.dir > 0 ? "R" : "L").Append(':').Append(Face[p.from]).Append("->").Append(res)
              .Append('/').Append(Face[p.predicted]).Append('/').Append(p.landingPred >= 0 ? Face[p.landingPred] : "-").Append('@').Append(p.dtLand.ToString("0.00")).Append(' ');
        }
        foreach (var e in leg.events) sb.AppendLine("    " + e);
        legLog.Append(sb);
        csv.Append(passName).Append(';').Append(leg.no).Append(';').Append(leg.serial).Append(';').Append(leg.cubes).Append(';').Append(leg.heading).Append(';')
           .Append(leg.camRel.Split('(')[0].Trim()).Append(';').Append(Face[leg.landFace]).Append(';').Append(leg.need >= 0 ? Face[leg.need] : "-").Append(';').Append(leg.rolls).Append(';')
           .Append(Face[leg.landL]).Append(';').Append(Face[leg.landR]).Append(';').Append(leg.landRule).Append(';').Append(Y(leg.drawnYaw)).Append(';').Append(Y(leg.settledYaw)).Append(';')
           .Append(Y(leg.mapYaw)).Append(';').Append(pr.ToString().Trim()).AppendLine();
        leg = null;
    }

    void ResolvePress(int newFace, int r)
    {
        if (pending.Count == 0) { events.Append(" (roll without a bot press)"); return; }
        if (r == 2 && pending.Count >= 2) { pending[0].merged = true; pending.RemoveAt(0); }
        var p = pending[0]; pending.RemoveAt(0);
        p.actual = newFace;
        if (p.actual != p.predicted) { pMismatch++; events.Append(" ** MISMATCH: " + p.kind + " expected " + Face[p.predicted] + " **"); }
        if (p.landingPred >= 0) { pLandingChecks++; if (p.actual != p.landingPred) { pLandingFails++; events.Append(" ** differs from what LEFT/RIGHT meant at landing (" + Face[p.landingPred] + ") **"); } }
        if (p.kind.StartsWith("correct") && p.actual != p.expected) { pWrongCorr++; events.Append(" ** WRONG CORRECTION: wanted " + Face[p.expected] + " **"); }
        if (p.kind.StartsWith("probe-second") && p.actual != p.expected) { pProbeFail++; events.Append(" ** KEEP-SPIN PROBE FAILED: wanted " + Face[p.expected] + " **"); }
    }

    // ---------------------------------------------------------------- logger
    void LateUpdate()
    {
        if (Done || !running) return;
        frameNo++;
        if (runner.IsGameOver)
        {
            pDeaths++;
            string m = "!!! PASS " + passName + " ENDED on leg " + legNo + " (" + runner.LastDeathCause + ") face " + Face[runner.currentSideIndex];
            frames.AppendLine(m); if (leg != null) leg.events.Add(m);
            EndPass("death on leg " + legNo);
            return;
        }
        if (!runner.IsRunning || runner.currentCube == null) { events.Length = 0; return; }
        if (!CheckLeg()) return;
        LogFrame();
    }

    void LogFrame()
    {
        var seg = gen.PlayerSegment;
        Transform cube = runner.currentCube;
        Vector3 fwd = cube.forward;
        int face = runner.currentSideIndex;
        int sign = runner.LegRollSign; bool sideRemap = runner.SideRemapActive;

        if (sign != prevSign)
        {
            string m;
            if (prevSign == 0 && sign != 0 && !(sideRemap && !prevSideRemap))
                m = "MAPPING LOCKED by the first played press: RIGHT=raw" + (sign > 0 ? "+1 (clockwise seen from behind)" : "-1 (counter-clockwise seen from behind)") + " by " + prevRule.Replace("would-decide:", "");
            else if (sign == 0) m = "MAPPING CLEARED (" + (runner.SettleRederives > lastRederives ? "settled on a clear face -> re-derive" : prevSideRemap && !sideRemap ? "left the side face" : "new leg") + ") - next press re-derives it";
            else m = "MAPPING SET to RIGHT=raw" + (sign > 0 ? "+1" : "-1") + (sideRemap ? " by SIDE-FACE REMAP" : "");
            events.Append(" <" + m + ">");
            if (leg != null) leg.mapping += (leg.mapping.Length > 0 ? " | " : "") + m;
        }
        if (runner.SettleRederives > lastRederives && sign == prevSign) events.Append(" <settle re-derive (sign was already 0)>");
        lastRederives = runner.SettleRederives;
        if (sideRemap != prevSideRemap)
            events.Append(sideRemap ? " <SIDE-FACE REMAP ON: RIGHT = runner's clockwise roll>" : " <SIDE-FACE REMAP OFF>");

        if (prevFace >= 0 && face != prevFace && havePrev && cube == prevCubeFrame)
        {
            int r = DynamicPathGenerator.Rolls(prevFace, face);
            float arc = (float)fArc.GetValue(runner);
            bool dbl = prevQueue.Contains("DBL") || prevQueue.Contains("FLIP");
            string kind = r == 2 ? "FLIP 180" : dbl ? "DOUBLE-TAP FLIP finishing" : "ROLL";
            if (r == 2 || dbl) pFlips++; else pRolls++;
            string spin = Mathf.Abs(arc) >= 179f ? "half turn" : arc < 0 ? "clockwise seen from behind (raw+1)" : "counter-clockwise seen from behind (raw-1)";
            events.Append(" " + kind + " " + Face[prevFace] + "->" + Face[face] + " arc " + arc.ToString("0") + "deg " + spin);
            ResolvePress(face, r);
        }

        NextTarget(out int need, out _, out string needWhy);
        CamRelation(fwd, out string camShort);
        int L = runner.PeekPressFace(-1, out string rule); int R = runner.PeekPressFace(1, out _);
        Quaternion meshRot = mesh != null ? mesh.rotation : runner.transform.rotation;
        Vector3 meshUp = meshRot * Vector3.up;
        string dRot = "-", topMove = "-";
        if (havePrev)
        {
            Quaternion dq = meshRot * Quaternion.Inverse(prevMeshRot);
            dq.ToAngleAxis(out float ang, out Vector3 ax);
            if (ang > 180f) ang -= 360f;
            if (Mathf.Abs(ang) > 0.05f)
            {
                float about = Vector3.Dot(ax, fwd);
                string kind;
                if (Mathf.Abs(about) > 0.7f) kind = (ang * about > 0 ? "ROLL ccw-from-behind (top->runner-LEFT)" : "ROLL cw-from-behind (top->runner-RIGHT)");
                else if (cube != prevCubeFrame) kind = "CORNER re-orient onto the new leg (snap about world " + W(ax) + ")";
                else kind = "TURN about world " + W(ax);
                dRot = Mathf.Abs(ang).ToString("0.0") + "deg " + kind;
                Vector3 dm = meshUp - prevMeshUp;
                if (dm.magnitude > 0.002f) topMove = Scr(dm, cam.transform.rotation).Replace("screen-", "");
            }
        }
        float along = Vector3.Dot(runner.transform.position - cube.position, fwd) / gen.cubeSize;
        frames.Append("f").Append(frameNo).Append(" t=").Append(Time.time.ToString("0.000"))
              .Append(" leg=").Append(legNo).Append(" cube ").Append(along.ToString("0.0")).Append("/").Append(seg.cubes.Count - 1)
              .Append(" face=").Append(Face[face]).Append(" need=").Append(need >= 0 ? Face[need] + "(" + needWhy + ")" : "-")
              .Append(" | heading ").Append(W(fwd)).Append(" ").Append(Scr(fwd, cam.transform.rotation)).Append(" cam=").Append(camShort)
              .Append(" yaw ").Append(Y(DrawnYaw)).Append("/").Append(Y(SettledYaw)).Append("/").Append(Y(MapYaw))
              .Append(" | L->").Append(Face[L]).Append("[").Append(Scr(N(cube, L) - N(cube, face), cam.transform.rotation).Replace("screen-", "")).Append("]")
              .Append(" R->").Append(Face[R]).Append("[").Append(Scr(N(cube, R) - N(cube, face), cam.transform.rotation).Replace("screen-", "")).Append("]")
              .Append(" sign=").Append(sign).Append(sideRemap ? " SIDE-REMAP" : "").Append(" rule=").Append(rule)
              .Append(runner.PowerAuto ? " AUTOPILOT" : "").Append(" | q=").Append(QueueText()).Append(runner.IsRevolving ? " ROLLING" : "")
              .Append(" | rot=").Append(meshRot.eulerAngles.ToString("0")).Append(" dRot=").Append(dRot).Append(" top->").Append(topMove);
        if (events.Length > 0) { frames.Append("  EVENTS:").Append(events); if (leg != null) leg.events.Add("f" + frameNo + " t=" + Time.time.ToString("0.000") + ":" + events); }
        frames.AppendLine();
        events.Length = 0;

        int sy = Mathf.RoundToInt(SettledYaw);
        if (yawsSeen.Count == 0 || !yawsSeen[yawsSeen.Count - 1].StartsWith(sy + "@")) yawsSeen.Add(sy + "@L" + legNo);
        prevQueue = QueueText(); prevFace = face; prevSign = sign; prevSideRemap = sideRemap; prevCubeFrame = cube; prevRule = rule;
        prevMeshRot = meshRot; prevMeshUp = meshUp; havePrev = true;
    }
    int lastRederives;
    readonly List<string> yawsSeen = new List<string>();

    void EndPass(string why)
    {
        FlushLeg();
        running = false;
        int hh = runner.HeadingRuleHits - headingHits0, rd = runner.SettleRederives - rederives0, rdf = runner.SettleRederiveFlips - rederiveFlips0;
        string line = "PASS " + passName + ": " + why + " | deaths " + pDeaths + ", external taps " + pExternal + ", taps " + pTaps + " (double taps " + pDbl + "), rolls " + pRolls + ", flips " + pFlips
            + ", press/face MISMATCHES " + pMismatch + ", first presses that differed from LEFT/RIGHT at landing " + pLandingFails + "/" + pLandingChecks
            + (overRollMode || keepSpinMode ? ", over-roll corrections " + pCorr + " (by screen side " + (pCorr - pCorrUndo) + ", undo when stacked " + pCorrUndo + ") WRONG " + pWrongCorr + ", keep-spin probes " + pProbe + " failed " + pProbeFail : "")
            + ", camera yaws seen " + string.Join("/", yawsSeen) + ", presses with drawn/settled yaw > 10 deg apart " + pViewDiffPresses + ", HEADING rule reached " + hh + ", settle re-derives " + rd + " (changed meaning " + rdf + ")";
        summary.AppendLine(line);
        var sb = new StringBuilder();
        sb.AppendLine("##LEGSUMMARY " + passName);
        sb.AppendLine(line);
        sb.Append(legLog);
        sb.AppendLine();
        sb.AppendLine("##FRAMES " + passName);
        sb.Append(frames);
        System.IO.File.WriteAllText(outBase + "_" + passName.Replace(':', '_') + ".txt", sb.ToString());
        Debug.Log("[LeftRightDebugTxt] " + line);
        NextPass();
    }

    void Finish()
    {
        Done = true;
        System.IO.File.WriteAllText(outBase + "_legs.csv", csv.ToString());
        System.IO.File.WriteAllText(outBase + "_summary.txt", summary.ToString());
        Time.timeScale = 1f;
        Debug.Log("[LeftRightDebugTxt] done\n" + summary);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
