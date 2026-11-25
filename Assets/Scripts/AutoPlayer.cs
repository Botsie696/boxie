using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Plays the game by itself, for testing how the camera and path look over a long run.
///
/// Rule: take the INNER path. At every upcoming corner the runner has to be standing on the face
/// that opens toward the outgoing leg - the inside of the turn - or its forward probe never finds
/// the corner cube and it flies off the end. So a little before each corner this works out how
/// many quarter turns get it onto that face and the quickest direction to spin, then presses
/// them, a human-like moment after noticing the turn.
///
/// Presses go into the same input queue the runner's keys and swipes feed (reached by
/// reflection, since it is private), so no gameplay code is changed or bypassed. Add it to any
/// object in Play mode, or press L in the editor (AutoPlayHotkey). Nothing here is saved.
///
/// It also reports on the run. Console lines tagged [BOT] log every time the runner leaves or
/// rejoins the track and every camera transition (yaw swing, zoom, how long the last shot was
/// held), and flag any frame where the runner's position along the route jumps. With
/// takeScreenshots on it saves a picture at those moments to Assets/Screenshots.
/// </summary>
public class AutoPlayer : MonoBehaviour
{
    [Header("Steering")]
    [Tooltip("Seconds before a corner at which the bot notices it and starts to act.")]
    public float noticeLeadSeconds = 1.3f;
    [Tooltip("Reaction delay between noticing the corner and pressing.")]
    public float reactionSeconds = 0.2f;

    [Header("Human bot (dev)")]
    [Tooltip("Act like a person: notice each corner when the runner lands on its leg, then press after a sampled reaction delay " +
             "(normal, mean/sd below), with a small chance of an extra late input. Off = the old perfect bot.")]
    public bool humanBot = false;
    public float humanMean = 0.30f;
    public float humanSd = 0.07f;
    [Range(0f, 1f)] public float lateChance = 0.03f;
    public float lateExtraSeconds = 0.12f;
    public int humanSeed = 12345;
    System.Random humanRng;

    // Reaction delay for one corner / hazard. Legacy bot: fixed delay, none at high speed. Human bot: sampled per corner.
    float ReactionFor(float secondsToTarget)
    {
        if (!humanBot) return secondsToTarget > 0.8f ? reactionSeconds : 0f;
        humanRng ??= new System.Random(humanSeed);
        double u1 = 1.0 - humanRng.NextDouble(), u2 = humanRng.NextDouble();
        float g = (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        float r = humanMean + humanSd * g;
        if (humanRng.NextDouble() < lateChance) r += lateExtraSeconds;
        return Mathf.Max(0.08f, r);
    }

    float NoticeLead => humanBot ? 99f : noticeLeadSeconds;

    [Header("Report")]
    [Tooltip("Watch the run and log [BOT] lines. Off = just steer, quietly.")]
    public bool report = true;
    public bool takeScreenshots = false;
    [Tooltip("Most screenshots saved in one run.")]
    public int maxScreenshots = 24;
    [Tooltip("Distance from the nearest path cube beyond which the runner counts as off the track.")]
    public float offTrackDistance = 2.4f;

    [Header("Results")]
    public int cornersHandled;
    public int offTrackEvents;
    public float offTrackSeconds;
    public int cameraTransitions;
    public int routeJumps;
    public float longestJump;
    public float zoomMin = float.MaxValue;
    public float zoomMax;

    CubeRailRunner player;
    DynamicPathGenerator pathGen;
    CameraParentRotator rig;
    PathDepthShading shading;
    Queue<int> inputQueue;

    Vector3 handledCorner = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    Vector3 pendingCorner;
    bool hasPending;
    float armedAt;
    float armedReaction;

    // A bridge plank (BridgePlankGuard.allowedSide) or a zone-door threshold (top face only) coming
    // up on the straight leg the runner is already on - no turn involved, just the right face.
    Vector3 pendingHazard;
    int pendingHazardSide;
    bool hasPendingHazard;
    float hazardArmedAt;
    float hazardArmedReaction;

    // An obstacle wall (PathObstacleGuard.blockedSide) coming up on the leg the runner is already
    // on - the opposite of a hazard: any side EXCEPT this one is safe.
    Vector3 pendingObstacle;
    int pendingObstacleBadSide;
    bool hasPendingObstacle;
    float obstacleArmedAt;
    float obstacleArmedReaction;

    // report state
    bool offTrack;
    float offTrackSince;
    CameraParentRotator.CameraState lastState;
    float stateSince;
    float yawAtTransitionStart;
    float orthoAtTransitionStart;
    float lastTransitionEnd;
    Vector3 lastRoutePoint;
    bool haveRoutePoint;
    int shots;
    readonly List<(float at, string name)> scheduledShots = new List<(float, string)>();
    readonly List<Vector3> nearest = new List<Vector3>();

    void Start()
    {
        player = FindFirstObjectByType<CubeRailRunner>();
        pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        rig = FindFirstObjectByType<CameraParentRotator>();

        if (player != null)
        {
            FieldInfo f = typeof(CubeRailRunner).GetField("inputQueue", BindingFlags.Instance | BindingFlags.NonPublic);
            inputQueue = f != null ? f.GetValue(player) as Queue<int> : null;
        }

        if (player == null || pathGen == null || inputQueue == null)
            Debug.LogWarning("[BOT] could not find the runner, path generator or input queue.");

        if (takeScreenshots) Directory.CreateDirectory(Path.Combine(Application.dataPath, "Screenshots"));
    }

    void Update()
    {
        if (player == null || pathGen == null || inputQueue == null) return;

        Steer();
        if (!report) return;
        Trace();
        WatchTrack();
        WatchCamera();
        WatchRoutePosition();
        RunScheduledShots();
    }

    // ------------------------------------------------------------------
    // Steering
    // ------------------------------------------------------------------

    // Closed loop: once the runner is on the leg that leads into the next corner and the reaction
    // delay has passed, it keeps checking the face every frame and presses again whenever the
    // runner is not on the inner face, so a late or dropped press cannot send it off the end.
    void Steer()
    {
        Transform cube = player.currentCube;
        if (cube == null || player.isFlying || player.IsGameOver) return;

        if (SteerGate()) return;   // a triangle gate ahead owns the steering until the runner is through it
        SteerCorner(cube);
        SteerHazard(cube);
        SteerObstacle(cube);
    }

    // Triangle gate (DynamicPathGeneratorGate.cs, 3.6 only): hold the landing face until the frame is behind, then let
    // the corner steering take over. True while a gate is ahead on this leg.
    bool SteerGate()
    {
        if (!pathGen.TryGetGateAhead(player.transform.position, out int holdSide, out _)) return false;
        if (player.currentSideIndex != holdSide && inputQueue.Count == 0 && !player.IsRevolving)
        {
            int presses = PressesToSide(holdSide, out int direction);
            if (presses == 2) player.QueueFlip();
            else if (presses == 1) player.QueueRoll(direction);
            if (report) AddTrace($"GATE HOLD x{presses} side={holdSide}");
        }
        return true;
    }

    /// <summary>
    /// On a door's runway in the door (FindPortalAhead) owns the steering: without this the corner steering aimed at the
    /// corner beyond the door and rolled the runner back off the door's face every few frames, so the bot crossed or
    /// fell at random (the "portal" deaths of the 3.11 audit). Off by default (old behaviour); Scene311Systems sets it.
    /// </summary>
    public static bool PortalOwnsSteering;

    void SteerCorner(Transform cube)
    {
        if (PortalOwnsSteering && FindPortalAhead(cube, out _, out _)) { hasPending = false; return; }
        var corners = pathGen.GetUpcomingCorners(1);
        if (corners.Count == 0) { hasPending = false; return; }
        DynamicPathGenerator.PathCorner corner = corners[0];

        // The generator can move on to the next leg a moment before the runner lands on it; the
        // current cube's axis is only right for this corner once the runner is on its incoming leg.
        if (Vector3.Dot(cube.forward, corner.incomingDirection.normalized) < 0.9f) return;

        float along = Vector3.Dot(corner.position - player.transform.position, cube.forward);
        float seconds = Mathf.Max(0f, along) / Mathf.Max(0.01f, player.forwardSpeed);

        if (!hasPending || (corner.position - pendingCorner).sqrMagnitude > 0.0001f)
        {
            if (seconds > NoticeLead) return;
            pendingCorner = corner.position;
            armedAt = Time.time;
            hasPending = true;
            // At high speed a leg is over in a fraction of a second: no reaction delay then (legacy bot).
            armedReaction = ReactionFor(seconds);
        }

        if (Time.time - armedAt < armedReaction) return;
        if (inputQueue.Count > 0) return; // presses already on their way; judge the face once they land

        int presses = PressesToInnerFace(corner, out int direction);
        if (presses == 0) return;
        // An obstacle still ahead on this leg that blocks the turn face: hold the safe face until it is passed.
        if (FindObstacleAhead(cube, out Vector3 obstaclePoint, out int obstacleMask, out _))
        {
            int inner = SideFacing(cube, corner.outgoingDirection.normalized);
            if (inner >= 0 && (obstacleMask & (1 << inner)) != 0 && Vector3.Dot(obstaclePoint - corner.position, cube.forward) < 0f) return;
        }

        // A raw roll: the screen-relative mapping would make the result depend on which camera
        // view the runner had locked in on the frame the bot pressed.
        // A half turn is one flip (the double-tap move): quicker than two rolls, which wait out a cooldown between them.
        if (presses == 2) player.QueueFlip();
        else player.QueueRoll(direction);
        if (report) AddTrace($"PRESS x{presses} dir={direction} secsToCorner={seconds:F2}");

        if ((corner.position - handledCorner).sqrMagnitude > 0.0001f)
        {
            handledCorner = corner.position;
            cornersHandled++;
        }
    }

    // Quarter turns needed to stand on the face that points along the outgoing leg.
    // ChangeSide(d) spins the face normal by -90 * d degrees about the cube's forward axis.
    int PressesToInnerFace(DynamicPathGenerator.PathCorner corner, out int direction)
    {
        direction = 1;
        Transform cube = player.currentCube;
        if (cube == null) return 0;

        Vector3 axis = cube.forward;
        Vector3 want = corner.outgoingDirection.normalized;
        Vector3 have = FaceNormal(cube, player.currentSideIndex);

        if (Vector3.Dot(have, want) > 0.9f) return 0;
        if (Vector3.Dot(have, want) < -0.9f) return 2;

        if (Vector3.Dot(Quaternion.AngleAxis(-90f, axis) * have, want) > 0.9f) { direction = 1; return 1; }
        direction = -1;
        return 1;
    }

    // For the report: is the runner standing on the face that opens toward the next corner?
    string OnInnerFace()
    {
        var corners = pathGen.GetUpcomingCorners(1);
        if (corners.Count == 0 || player.currentCube == null) return "-";
        return PressesToInnerFace(corners[0], out _) == 0 ? "yes" : "no";
    }

    // Same closed-loop pattern as SteerCorner, but for a straight-leg hazard that needs a specific
    // face rather than a turn: a bridge plank (BridgePlankGuard.allowedSide) or the top-only
    // threshold of a zone door. Runs independently of corner steering so it can act well before -
    // or well after - the next actual turn.
    void SteerHazard(Transform cube)
    {
        if (!FindBridgeAhead(cube, out Vector3 point, out int side) && !FindPortalAhead(cube, out point, out side))
        {
            hasPendingHazard = false;
            return;
        }

        float along = Vector3.Dot(point - player.transform.position, cube.forward);
        float seconds = Mathf.Max(0f, along) / Mathf.Max(0.01f, player.forwardSpeed);

        if (!hasPendingHazard || (point - pendingHazard).sqrMagnitude > 0.0001f)
        {
            if (seconds > NoticeLead) return;
            pendingHazard = point;
            pendingHazardSide = side;
            hazardArmedAt = Time.time;
            hasPendingHazard = true;
            hazardArmedReaction = ReactionFor(seconds);
        }

        if (Time.time - hazardArmedAt < hazardArmedReaction) return;
        if (inputQueue.Count > 0) return;

        int presses = PressesToSide(pendingHazardSide, out int direction);
        if (presses == 0) return;

        if (presses == 2) player.QueueFlip();
        else player.QueueRoll(direction);
        if (report) AddTrace($"HAZARD PRESS x{presses} dir={direction} targetSide={pendingHazardSide} secsToHazard={seconds:F2}");
    }

    // The nearest upcoming BridgePlankGuard cube on the runner's current leg, if any.
    bool FindBridgeAhead(Transform cube, out Vector3 point, out int side)
    {
        point = default; side = -1;
        DynamicPathGenerator.Segment seg = pathGen.PlayerSegment;
        if (seg == null) return false;

        foreach (GameObject go in seg.cubes)
        {
            if (go == null) continue;
            var guard = go.GetComponent<BridgePlankGuard>();
            if (guard == null) continue;
            float along = Vector3.Dot(go.transform.position - player.transform.position, cube.forward);
            if (along <= 0f) continue; // already on or past this plank
            point = go.transform.position;
            side = guard.allowedSide;
            return true;
        }
        return false;
    }

    // The zone-door threshold at the end of the runway in, if the runner is on that runway. The door
    // stands on whichever face the runner is already on when it's built, so the bot's job is just to
    // hold that face - any other face flies past it and the run is lost.
    bool FindPortalAhead(Transform cube, out Vector3 point, out int side)
    {
        point = default; side = -1;
        DynamicPathGenerator.Segment entry = pathGen.PortalEntrySegment;
        if (entry == null || pathGen.PlayerSegment != entry) return false;

        float along = Vector3.Dot(pathGen.PortalEntryEnd - player.transform.position, cube.forward);
        if (along <= 0f) return false;
        point = pathGen.PortalEntryEnd;
        side = pathGen.PortalEntrySide;
        return true;
    }

    // Closed loop again, but inverted from SteerHazard: an obstacle blocks one face (a pyramid) or two opposite faces
    // (an obstacle leg station: PathObstacleGuard.blockedSide on each pillar), so the runner just has to be on a face
    // that is not blocked while crossing it - it re-checks every frame and presses away the instant it finds itself on
    // a blocked face (choosing a face the next station leaves open too), then leaves the corner steering
    // (SteerCorner) to put it back on the turn face after.
    void SteerObstacle(Transform cube)
    {
        if (!FindObstacleAhead(cube, out Vector3 point, out int badSide, out int nextMask))
        {
            hasPendingObstacle = false;
            return;
        }

        float along = Vector3.Dot(point - player.transform.position, cube.forward);
        float seconds = Mathf.Max(0f, along) / Mathf.Max(0.01f, player.forwardSpeed);

        if (!hasPendingObstacle || (point - pendingObstacle).sqrMagnitude > 0.0001f)
        {
            if (seconds > NoticeLead) return;
            pendingObstacle = point;
            pendingObstacleBadSide = badSide;
            obstacleArmedAt = Time.time;
            hasPendingObstacle = true;
            obstacleArmedReaction = ReactionFor(seconds);
        }

        if (Time.time - obstacleArmedAt < obstacleArmedReaction) return;
        if ((pendingObstacleBadSide & (1 << player.currentSideIndex)) == 0) return; // already on a safe face
        if (inputQueue.Count > 0) return;

        int targetSide = BestSafeSide(pendingObstacleBadSide, nextMask);
        int presses = PressesToSide(targetSide, out int direction);
        if (presses == 0) return;

        if (presses == 2) player.QueueFlip();
        else player.QueueRoll(direction);
        if (report) AddTrace($"OBSTACLE PRESS x{presses} dir={direction} targetSide={targetSide} badSide={pendingObstacleBadSide} secsToObstacle={seconds:F2}");
    }

    readonly List<PathObstacleGuard> guardScratch = new List<PathObstacleGuard>();

    // Blocked faces of one cube as a bit mask (bit s = face s), 0 = no obstacle on it. An obstacle leg station has
    // two pillars on opposite faces of the same cube, so every guard on the cube counts.
    int BlockedMask(GameObject go)
    {
        guardScratch.Clear();
        go.GetComponentsInChildren(guardScratch);
        int mask = 0;
        foreach (PathObstacleGuard g in guardScratch)
            if (g != null && g.blockedSide >= 0 && g.blockedSide < 4) mask |= 1 << g.blockedSide;
        return mask;
    }

    // The nearest upcoming obstacle cube on the runner's current leg, if any: its position, the mask of the faces it
    // blocks, and the mask of the station after it (0 = none).
    bool FindObstacleAhead(Transform cube, out Vector3 point, out int badMask, out int nextMask)
    {
        point = default; badMask = 0; nextMask = 0;
        DynamicPathGenerator.Segment seg = pathGen.PlayerSegment;
        if (seg == null) return false;

        bool found = false;
        foreach (GameObject go in seg.cubes)
        {
            if (go == null) continue;
            float along = Vector3.Dot(go.transform.position - player.transform.position, cube.forward);
            if (along <= 0f) continue; // already on or past this obstacle
            int mask = BlockedMask(go);
            if (mask == 0) continue;
            if (!found) { point = go.transform.position; badMask = mask; found = true; }
            else { nextMask = mask; break; }
        }

        // Sliding pillars (3.11 obstacle legs): they move with the runner's progress, so the bot plans for where
        // and what they are when the runner reaches them (ObstacleLegHazard.MeetPoint / HazardPattern.faces).
        if (ObstacleLegHazard.Active.Count > 0)
        {
            float bestAlong = found ? Vector3.Dot(point - player.transform.position, cube.forward) : float.MaxValue;
            float secondAlong = float.MaxValue;
            int bestMask = badMask; Vector3 bestPoint = point;
            int secondMask = nextMask;
            foreach (ObstacleLegHazard h in ObstacleLegHazard.Active)
            {
                if (h == null || h.seg != seg || h.pattern == null) continue;
                Vector3 mp = h.MeetPoint();
                float along = Vector3.Dot(mp - player.transform.position, cube.forward);
                if (along <= 0f) continue;
                int m = h.pattern.faces;
                if (m == 0) continue;
                if (along < bestAlong)
                {
                    secondAlong = bestAlong; secondMask = bestMask;
                    bestAlong = along; bestMask = m; bestPoint = mp; found = true;
                }
                else if (along < secondAlong) { secondAlong = along; secondMask = m; }
            }
            if (found) { point = bestPoint; badMask = bestMask; nextMask = secondAlong < float.MaxValue ? secondMask : nextMask; }
        }
        return found;
    }

    // The face not in badMask that takes the fewest presses to reach, preferring one the next station leaves open.
    int BestSafeSide(int badMask, int nextMask = 0)
    {
        int best = -1, bestCost = int.MaxValue;
        for (int s = 0; s < 4; s++)
        {
            if ((badMask & (1 << s)) != 0) continue;
            int cost = PressesToSide(s, out _) * 2 + ((nextMask & (1 << s)) != 0 ? 1 : 0);
            if (cost < bestCost) { bestCost = cost; best = s; }
        }
        return best < 0 ? (player.currentSideIndex + 1) % 4 : best;
    }

    // Face index whose normal points along dir on the runner's cube, -1 if none.
    int SideFacing(Transform cube, Vector3 dir)
    {
        for (int s = 0; s < 4; s++)
            if (Vector3.Dot(FaceNormal(cube, s), dir) > 0.9f) return s;
        return -1;
    }

    // Quarter turns needed to stand on targetSide, the same way PressesToInnerFace works out a
    // corner's inner face - just from a face index instead of a world direction.
    int PressesToSide(int targetSide, out int direction)
    {
        direction = 1;
        Transform cube = player.currentCube;
        if (cube == null) return 0;
        if (targetSide == player.currentSideIndex) return 0;

        Vector3 axis = cube.forward;
        Vector3 have = FaceNormal(cube, player.currentSideIndex);
        Vector3 want = FaceNormal(cube, targetSide);

        if (Vector3.Dot(have, want) < -0.9f) return 2;
        if (Vector3.Dot(Quaternion.AngleAxis(-90f, axis) * have, want) > 0.9f) { direction = 1; return 1; }
        direction = -1;
        return 1;
    }

    static Vector3 FaceNormal(Transform c, int side)
    {
        switch (side)
        {
            case 1: return c.right;
            case 2: return -c.up;
            case 3: return -c.right;
            default: return c.up;
        }
    }

    // ------------------------------------------------------------------
    // Report
    // ------------------------------------------------------------------

    // The last second or so of runner state, dumped when it leaves the track.
    readonly Queue<string> trace = new Queue<string>();
    const int TraceLength = 90;

    void AddTrace(string line)
    {
        trace.Enqueue($"{Time.time:F3} {line}");
        while (trace.Count > TraceLength) trace.Dequeue();
    }

    void Trace()
    {
        Transform cube = player.currentCube;
        var corners = pathGen.GetUpcomingCorners(1);
        string c = "-";
        if (corners.Count > 0 && cube != null)
        {
            float along = Vector3.Dot(corners[0].position - player.transform.position, cube.forward);
            bool onLeg = Vector3.Dot(cube.forward, corners[0].incomingDirection.normalized) > 0.9f;
            c = $"cornerSeg={corners[0].segmentIndex} along={along:F2} onLeg={onLeg} inner={OnInnerFace()}";
        }
        AddTrace($"cube={(cube ? cube.name : "null")} side={player.currentSideIndex} fly={player.isFlying} " +
                 $"q={inputQueue.Count} playerSeg={pathGen.PlayerSegmentIndex} {c} pos={player.transform.position}");
    }

    void WatchTrack()
    {
        pathGen.GetPathAhead(1, 0, 1, nearest);
        if (nearest.Count == 0) return;

        float d = Vector3.Distance(player.transform.position, nearest[0]);
        bool off = d > offTrackDistance;

        if (off && !offTrack)
        {
            offTrack = true;
            offTrackSince = Time.time;
            offTrackEvents++;
            Debug.LogWarning("[BOT] trace before leaving the track:\n" + string.Join("\n", trace));
            Debug.LogWarning($"[BOT] t={Time.time:F1} OFF TRACK dist={d:F1} speed={player.forwardSpeed:F1} " +
                             $"side={player.currentSideIndex} flying={player.isFlying} queued={inputQueue.Count} " +
                             $"innerFace={OnInnerFace()} cam={CamSummary()}");
            Shot("offtrack");
        }
        else if (!off && offTrack)
        {
            offTrack = false;
            offTrackSeconds += Time.time - offTrackSince;
            Debug.Log($"[BOT] t={Time.time:F1} back on track after {Time.time - offTrackSince:F2}s");
        }
    }

    void WatchCamera()
    {
        if (rig == null) return;

        var state = rig.CurrentState;
        if (state != lastState)
        {
            if (state == CameraParentRotator.CameraState.Transitioning)
            {
                yawAtTransitionStart = rig.CurrentYaw;
                orthoAtTransitionStart = Camera.main.orthographicSize;
                cameraTransitions++;
                Debug.Log($"[BOT] t={Time.time:F1} CAMERA move #{cameraTransitions} starts; last shot held " +
                          $"{Time.time - lastTransitionEnd:F1}s yaw={rig.CurrentYaw:F0} ortho={Camera.main.orthographicSize:F1}");
                Shot("cam_start");
                Schedule(0.6f, "cam_mid");
            }
            else
            {
                lastTransitionEnd = Time.time;
                float swing = Mathf.DeltaAngle(yawAtTransitionStart, rig.CurrentYaw);
                Debug.Log($"[BOT] t={Time.time:F1} CAMERA move ends after {Time.time - stateSince:F1}s: " +
                          $"yaw {yawAtTransitionStart:F0}->{rig.CurrentYaw:F0} (swing {swing:F0}) " +
                          $"ortho {orthoAtTransitionStart:F1}->{Camera.main.orthographicSize:F1}");
                Schedule(0.3f, "cam_end");
            }
            lastState = state;
            stateSince = Time.time;
        }

        if (Camera.main != null) { float o = Camera.main.orthographicSize; zoomMin = Mathf.Min(zoomMin, o); zoomMax = Mathf.Max(zoomMax, o); }

        // The runner must never leave the frame.
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 vp = cam.WorldToViewportPoint(player.transform.position);
            if (vp.x < 0.02f || vp.x > 0.98f || vp.y < 0.02f || vp.y > 0.98f)
                Debug.LogWarning($"[BOT] t={Time.time:F1} runner near/off screen edge vp=({vp.x:F2},{vp.y:F2}) cam={CamSummary()}");
        }
    }

    // The runner's projected position on the route must move smoothly with them. A jump here is
    // what drives the path shading (colour, size, opacity) to change all at once.
    void WatchRoutePosition()
    {
        if (shading == null) shading = pathGen.GetComponent<PathDepthShading>();
        if (shading == null) return;

        Vector3 p = shading.RoutePoint;
        if (haveRoutePoint && Time.deltaTime > 0f)
        {
            float moved = Vector3.Distance(p, lastRoutePoint);
            float expected = player.forwardSpeed * Time.deltaTime;
            if (moved > expected * 2f + 0.75f)
            {
                routeJumps++;
                longestJump = Mathf.Max(longestJump, moved);
                Debug.LogWarning($"[BOT] t={Time.time:F1} ROUTE POSITION JUMP moved={moved:F2} expected={expected:F2}");
            }
        }
        lastRoutePoint = p;
        haveRoutePoint = true;
    }

    string CamSummary() => rig == null ? "-" : $"{rig.CurrentState} yaw={rig.CurrentYaw:F0} ortho={Camera.main.orthographicSize:F1} zone={rig.CurrentZoneIndex}";

    void Schedule(float after, string name) => scheduledShots.Add((Time.time + after, name));

    void RunScheduledShots()
    {
        for (int i = scheduledShots.Count - 1; i >= 0; i--)
        {
            if (Time.time < scheduledShots[i].at) continue;
            Shot(scheduledShots[i].name);
            scheduledShots.RemoveAt(i);
        }
    }

    void Shot(string tag)
    {
        if (!takeScreenshots || shots >= maxScreenshots) return;
        shots++;
        ScreenCapture.CaptureScreenshot(Path.Combine(Application.dataPath, "Screenshots", $"run_{shots:00}_{tag}_t{Time.time:F0}.png"));
    }

    void OnDisable()
    {
        if (!report) return;
        Debug.Log($"[BOT] SUMMARY t={Time.time:F1} corners={cornersHandled} offTrackEvents={offTrackEvents} " +
                  $"offTrackSeconds={offTrackSeconds:F1} cameraMoves={cameraTransitions} routeJumps={routeJumps} " +
                  $"longestJump={longestJump:F2} zoom={zoomMin:F1}..{zoomMax:F1}");
    }
}
