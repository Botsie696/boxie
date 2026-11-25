using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tutorial layout for DynamicPathGenerator (TUTORIAL ADDITION). Everything here is inert unless
/// TutorialManager calls BeginTutorialPath: the opening legs are then laid from the lesson plan below and the
/// coins on them are hand-placed. Once the scripted legs are down the ordinary random generator carries on from
/// the end of the last one, so the hand-off needs no scene change and no rebuild.
///
/// The lesson plan is a list of corners, each needing a given number of taps (0 = already on the turn face,
/// 1 = one roll, 2 = the opposite face). The runner lands every leg on the face that looked back at the corner it
/// came round (-incoming heading), so the taps a corner needs follow from the headings alone:
///   next heading == that face        -> 0 taps
///   next heading == the opposite face -> 2 taps (a flip)
///   anything else                     -> 1 tap, and of the two such headings one is a left tap, one a right tap.
/// Each leg's heading is picked to give its corner the lesson's tap count, with one-tap corners alternating
/// between the left and the right half of the screen (worked out against the tutorial's locked camera yaw).
///
/// The camera never turns during the tutorial: every zone is forced to one yaw (tutorialViewYaw), chosen from the
/// opening heading before the first frame. Released at the hand-off.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Tutorial (only used while TutorialManager is running)")]
    [Tooltip("Run the one-time tutorial in this scene, on first play (PlayerPrefs \"TutorialCompleted\"). TutorialTrialMonument2 runs it whether or not this is ticked. Everything it needs is built at run time; a TurnFaceArrows on this object gives the chevron lessons.")]
    public bool tutorialInThisScene = false;
    [Tooltip("Cubes in each scripted lesson leg, in lesson order: L0 + L1 corner, L2, L3 (1), L3 (2), L4, L5, L6 (guided), L6 (real time), L7 obstacle leg.")]
    public int[] lessonLegCubes = { 22, 16, 16, 14, 14, 14, 16, 14, 20 };
    [Tooltip("Coin every Nth cube on the hand-placed coin trails.")]
    [Range(1, 3)] public int tutorialCoinSpacing = 1;
    [Tooltip("Cubes at each end of a scripted leg that carry no coins.")]
    [Range(0, 8)] public int tutorialCoinMargin = 3;

    // Legacy fields of the old three-phase tutorial, kept so scenes that saved them still load. Unused.
    [HideInInspector] public int[] tutorialLegCubes = { 18, 14, 14, 12, 14, 12, 18 };
    [HideInInspector] public int[] tutorialTurns = { 0, 1, -1, 2, 1, -2, -1 };

    /// <summary>Taps each lesson corner needs: L1, L2, L3, L3, L4 one roll; L5 none; L6 twice the opposite face.</summary>
    public static readonly int[] LessonCornerTaps = { 1, 1, 1, 1, 1, 0, 2, 2 };

    /// <summary>True while the scripted opening is in charge (set by BeginTutorialPath, cleared by EndTutorial).</summary>
    public bool tutorialActive;

    /// <summary>The camera yaw the whole tutorial is shot from (set by TutorialManager before the first frame), or -1.</summary>
    [System.NonSerialized] public float tutorialViewYaw = -1f;
    [System.NonSerialized] public float tutorialViewTilt = 35.264f;

    readonly List<Segment> tutorialSegments = new List<Segment>();
    readonly List<PathZone> tutorialForcedZones = new List<PathZone>();
    int tutorialLegsLaid;
    int tutorialPendingLen;
    int tutorialLastOneTapSide;

    public int TutorialLegCount => lessonLegCubes != null ? lessonLegCubes.Length : 0;
    public PathDepthShading Shading
    {
        get { if (shadingRef == null) shadingRef = GetComponent<PathDepthShading>(); return shadingRef; }
    }

    /// <summary>Lays the scripted opening from the start (also the full rebuild when a rewind is not possible).</summary>
    public void BeginTutorialPath()
    {
        tutorialActive = true;
        GenerateNewPath();
    }

    /// <summary>Hand-off: from here on nothing is scripted, the camera may turn again, and the score starts from 0.</summary>
    public void EndTutorial()
    {
        tutorialActive = false;
        foreach (PathZone z in tutorialForcedZones)
            if (z != null && !z.framingLocked && Mathf.Abs(Mathf.DeltaAngle(z.forcedYaw, tutorialViewYaw)) < 0.5f)
                z.forcedYaw = -1f;
        tutorialForcedZones.Clear();
        Score = 0;
        if (player != null) player.SetLegSpeed(0);
    }

    /// <summary>The n-th scripted leg (null if it has not been laid).</summary>
    public Segment TutorialSegment(int n) => n >= 0 && n < tutorialSegments.Count ? tutorialSegments[n] : null;

    /// <summary>Which scripted leg this is, or -1.</summary>
    public int TutorialLegOf(Segment seg) => seg == null ? -1 : tutorialSegments.IndexOf(seg);

    /// <summary>Hand-placed coin on a chosen face (faceDir = the face's outward direction) of one cube of a leg.</summary>
    public void TutorialCoin(Segment seg, int cubeIndex, Vector3 faceDir, int indexInRun)
    {
        if (seg == null || !CoinSystemReady() || cubeIndex < 0 || cubeIndex >= seg.cubes.Count) return;
        GameObject cube = seg.cubes[cubeIndex];
        if (cube != null) PlaceSingleCoin(seg, cube.transform, faceDir, indexInRun);
    }

    // ------------------------------------------------------------------ camera: one yaw for the whole tutorial

    /// <summary>
    /// Pins every zone to tutorialViewYaw (PlanCameraYaws honours forcedYaw), so the camera never turns during
    /// the tutorial. Called as legs are laid and every frame by TutorialManager; cheap when nothing is new.
    /// </summary>
    public void TutorialForceZoneYaws()
    {
        if (!tutorialActive || tutorialViewYaw < 0f) return;
        foreach (PathZone z in zones)
        {
            if (z.forcedYaw >= 0f) continue;
            z.forcedYaw = tutorialViewYaw;
            tutorialForcedZones.Add(z);
            if (!z.framingLocked && (!z.yawPlanned || Mathf.Abs(Mathf.DeltaAngle(z.plannedYaw, tutorialViewYaw)) > 0.5f))
            {
                z.plannedYaw = tutorialViewYaw;
                z.yawPlanned = true;
                z.framingSolved = false;
            }
        }
    }

    // ------------------------------------------------------------------ hooks (see AppendSegment)

    void ResetTutorialLayout()
    {
        tutorialSegments.Clear();
        tutorialForcedZones.Clear();
        tutorialLegsLaid = 0;
        tutorialLastOneTapSide = 0;
    }

    bool TutorialBeginLeg(ref int segLen)
    {
        TutorialForceZoneYaws();
        if (!tutorialActive || tutorialLegsLaid >= TutorialLegCount) return false;
        segLen = Mathf.Max(4, lessonLegCubes[tutorialLegsLaid]);
        tutorialPendingLen = segLen;
        return true;
    }

    static Vector3 SnapAxis(Vector3 d)
    {
        float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
        if (ay >= ax && ay >= az) return new Vector3(0f, Mathf.Sign(d.y), 0f);
        if (ax >= az) return new Vector3(Mathf.Sign(d.x), 0f, 0f);
        return new Vector3(0f, 0f, Mathf.Sign(d.z));
    }

    /// <summary>Rolls from a face (outward normal) to the face that points along `target`: 0, 1 or 2.</summary>
    public static int TapsBetween(Vector3 face, Vector3 target)
    {
        float d = Vector3.Dot(face.normalized, target.normalized);
        return d > 0.9f ? 0 : d < -0.9f ? 2 : 1;
    }

    // The face the runner starts on: the opening cube's top.
    Vector3 TutorialStartFace() => firstCube != null ? SnapAxis(firstCube.up) : Vector3.up;

    /// <summary>
    /// The heading of the leg about to be laid, chosen so the corner into it needs the lesson's taps (see the
    /// class summary). Clear of older track first, then the right tap count, then the screen side.
    /// </summary>
    Vector3 TutorialLegDirection(Vector3 prevDir)
    {
        int leg = tutorialLegsLaid;   // >= 1: the opening leg takes firstCube's heading
        int corner = leg - 1;
        prevDir = SnapAxis(prevDir);
        Vector3 face = corner == 0 ? TutorialStartFace() : -SnapAxis(tutorialSegments[corner - 1].direction);
        int wantTaps = corner < LessonCornerTaps.Length ? LessonCornerTaps[corner] : 1;
        Vector3 origin = lastPosition + prevDir * cubeSize;
        int len = Mathf.Max(4, tutorialPendingLen);

        Vector3 best = Vector3.zero;
        int bestScore = int.MinValue;
        int bestSide = 0;
        Vector3[] axes = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.up, Vector3.down };
        foreach (Vector3 cand in axes)
        {
            if (Mathf.Abs(Vector3.Dot(cand, prevDir)) > 0.1f) continue;   // a turn is always square to the leg
            int taps = TapsBetween(face, cand);
            int side = taps == 1 ? PredictTapSide(prevDir, face, cand) : 0;
            int score = 0;
            if (LegIsClear(origin, cand, len)) score += 1000;
            if (taps == wantTaps) score += 100;
            if (wantTaps == 1 && taps == 1 && (tutorialLastOneTapSide == 0 || side == -tutorialLastOneTapSide)) score += 20;
            if (Mathf.Abs(cand.y) < 0.5f) score += 3;   // level legs read best; climbs and drops when they teach something
            if (score > bestScore) { bestScore = score; best = cand; bestSide = side; }
        }
        if (best == Vector3.zero) best = Vector3.Cross(Vector3.up, prevDir).sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, prevDir).normalized : Vector3.right;
        if (bestSide != 0) tutorialLastOneTapSide = bestSide;
        if (Mathf.Abs(best.y) < 0.9f) lastHorizontalDirection = best;   // the random legs after the script steer off this
        return best;
    }

    /// <summary>
    /// The screen half (-1 left, +1 right) whose tap rolls a runner on `face` of a leg heading `legDir` onto the
    /// face pointing along `target`, against the tutorial's locked view. The same mapping as
    /// CubeRailRunner.ScreenRelativeDir (camera-relative controls, swing projection). 0 if unknown.
    /// </summary>
    public int PredictTapSide(Vector3 legDir, Vector3 face, Vector3 target)
    {
        if (tutorialViewYaw < 0f) return 0;
        Quaternion view = Quaternion.Euler(tutorialViewTilt, tutorialViewYaw, 0f);
        Vector3 camRight = view * Vector3.right, camUp = view * Vector3.up;

        Vector3 refNormal = GetRotationForDir(legDir) * Vector3.up;
        Vector3 rolled = Quaternion.AngleAxis(-90f, legDir) * refNormal;
        Vector3 swing = Vector3.ProjectOnPlane(rolled - refNormal, refNormal);
        Vector2 swingScreen = new Vector2(Vector3.Dot(swing, camRight), Vector3.Dot(swing, camUp));
        Vector2 heading = new Vector2(Vector3.Dot(legDir, camRight), Vector3.Dot(legDir, camUp));
        float sign;
        if (Mathf.Abs(swingScreen.x) > 0.3f * swingScreen.magnitude) sign = Mathf.Sign(swingScreen.x);
        else sign = Mathf.Sign(Vector2.Dot(swingScreen, new Vector2(heading.y, -heading.x)));
        if (sign == 0f) sign = 1f;

        int raw = Vector3.Dot(Quaternion.AngleAxis(-90f, legDir) * face, target) > 0.9f ? 1 : -1;
        return sign > 0f ? raw : -raw;
    }

    void TutorialEndLeg(Segment seg)
    {
        tutorialSegments.Add(seg);
        tutorialLegsLaid++;
    }

    /// <summary>
    /// A dense trail on the face that takes the turn, along the scripted legs before a turn. The last scripted
    /// leg carries none (TutorialManager lays its own coins on the safe face there). Also the hook that pins the
    /// newest zone's camera yaw: it runs after the leg has joined its zone.
    /// </summary>
    bool TutorialCoinsBehindTurn(Segment incoming, Vector3 nextDir)
    {
        TutorialForceZoneYaws();
        int leg = TutorialLegOf(incoming);
        if (leg < 0) return false;
        if (leg >= TutorialLegCount - 1) return true;

        int n = incoming.cubes.Count;
        int spacing = Mathf.Max(1, tutorialCoinSpacing);
        int run = 0;
        for (int i = tutorialCoinMargin; i <= n - 2 - tutorialCoinMargin; i += spacing)
            if (incoming.cubes[i] != null) PlaceSingleCoin(incoming, incoming.cubes[i].transform, nextDir, run++);
        return true;
    }

    // ------------------------------------------------------------------ rewind: coins

    struct TutorialCoinRecord
    {
        public Segment seg;
        public GameObject cube;
        public Vector3 centre, outward, floatAxis;
        public Quaternion stand, blobRot;
        public int index;
        public float size;
    }

    readonly List<TutorialCoinRecord> tutorialCoinSnapshot = new List<TutorialCoinRecord>(64);

    /// <summary>Remembers the coins on these legs (the rewind puts back any collected after this).</summary>
    public void TutorialSnapshotCoins(Segment a, Segment b)
    {
        tutorialCoinSnapshot.Clear();
        foreach (PathCoin c in coins)
        {
            if (c.segment == null || (c.segment != a && c.segment != b)) continue;
            tutorialCoinSnapshot.Add(new TutorialCoinRecord
            {
                seg = c.segment, cube = c.cube, centre = c.cubeCentre, outward = c.outward, floatAxis = c.floatAxis,
                stand = c.stand, blobRot = c.blobRot, index = c.indexInRun, size = c.sizeScale
            });
        }
    }

    /// <summary>
    /// Puts back every snapshot coin that has been collected since and is still ahead of the runner (on its leg
    /// `runnerLeg`, or on a later one). Returns how many were put back, so the count can be taken off the score.
    /// </summary>
    public int TutorialRestoreCoins(Vector3 runnerPos, Segment runnerLeg)
    {
        int restored = 0;
        foreach (TutorialCoinRecord r in tutorialCoinSnapshot)
        {
            if (r.cube == null) continue;
            if (r.seg == runnerLeg && Vector3.Dot(r.centre - runnerPos, runnerLeg.direction) < 0f) continue;   // behind: it was missed anyway

            bool present = false;
            foreach (PathCoin c in coins)
                if (c.cube == r.cube && Vector3.Dot(c.outward, r.outward) > 0.9f) { present = true; break; }
            if (present) continue;

            PathCoin coin = spareCoins.Count > 0 ? spareCoins.Pop() : new PathCoin();
            coin.segment = r.seg;
            coin.cube = r.cube;
            coin.cubeCentre = r.centre;
            coin.outward = r.outward;
            coin.floatAxis = r.floatAxis;
            coin.stand = r.stand;
            coin.blobRot = r.blobRot;
            coin.blob = null;
            coin.indexInRun = r.index;
            coin.sizeScale = r.size;
            coin.view = null;
            coin.viewScale = -1f;
            coins.Add(coin);
            restored++;
        }
        return restored;
    }

    // ------------------------------------------------------------------ first-encounter hints: power-up targets

    /// <summary>The nearest power-up pickup on the track (diamond first, then shield): where it is and the face it sits on.</summary>
    public bool HintPowerUpTarget(out Vector3 pos, out Vector3 outward, out Segment seg, out bool shield)
    {
        if (powerGem != null && powerGem.cube != null)
        {
            pos = powerGem.pos; outward = powerGem.outward; seg = powerGem.seg; shield = false;
            return true;
        }
        if (shieldGem != null && shieldGem.cube != null)
        {
            pos = shieldGem.pos; outward = shieldGem.outward; seg = shieldGem.seg; shield = true;
            return true;
        }
        pos = outward = Vector3.zero; seg = null; shield = false;
        return false;
    }
}
