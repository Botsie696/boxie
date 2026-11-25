using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// TEMPORARY play-mode test harness. Not part of the game - attach it, run, read the console,
/// detach it.
///
/// Three jobs:
///
///   1. Plays like a person. Reads DynamicPathGenerator.GetUpcomingCorners(), works out which
///      face the turn opens on, and commits the input after a human reaction delay. Inputs go
///      into the SAME queue CubeRailRunner's key and swipe handlers feed, via reflection, so
///      no gameplay code is modified or bypassed to run the test.
///
///   2. Proves the camera is STILL. Every frame it diffs the camera's position, rotation and
///      orthographic size against the previous frame. While the rig reports Static those deltas
///      must be exactly zero - that is the claim the whole zone architecture rests on, and a
///      damper left anywhere in the rig shows up here as a small non-zero drift rather than as
///      something you have to catch by eye.
///
///   3. Watches visibility. Logs Camera.main.WorldToViewportPoint of the runner every N frames
///      and flags anything outside the safe box.
/// </summary>
public class MockHumanPlayer : MonoBehaviour
{
    [Header("Simulated Human")]
    public bool simulateInput = true;
    [Tooltip("Seconds out at which a person NOTICES the corner and starts to act.")]
    public float perceptionLead = 1.30f;
    [Tooltip("Human reaction delay between noticing the corner and committing the input.")]
    public float reactionLead = 0.20f;
    public float reactionJitter = 0.05f;

    [Header("Probe")]
    public int logEveryFrames = 60;
    public float playerMinViewportY = 0.05f;
    [Tooltip("Camera movement above this per frame counts as drift while the rig claims to be Static.")]
    public float stillnessEpsilon = 0.0001f;

    private CubeRailRunner player;
    private DynamicPathGenerator pathGen;
    private CameraParentRotator rig;
    private Camera cam;

    private Queue<int> inputQueue;
    private Vector3 pendingCornerPos;
    private Vector3 handledCornerPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    private bool hasPending;
    private float pendingArmedAt;
    private float pendingLead;

    // stillness tracking
    private Vector3 lastCamPos;
    private Quaternion lastCamRot;
    private float lastOrtho;
    private bool havePrevPose;
    private int staticFrames;
    private int staticDriftFrames;
    private float maxStaticPosDrift;
    private float maxStaticOrthoDrift;
    private int transitionFrames;
    private int transitionCount;
    private int lastZoneIndex = -999;

    // run stats
    private int frames, samples, cornersHandled;
    private int playerOutOfFrame, flyingSamples, cubeChanges;
    private Transform lastCube;
    private float orthoMin = float.MaxValue, orthoMax;

    void Start()
    {
        player = FindFirstObjectByType<CubeRailRunner>();
        pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        rig = FindFirstObjectByType<CameraParentRotator>();
        cam = Camera.main;

        if (player != null)
        {
            FieldInfo f = typeof(CubeRailRunner).GetField("inputQueue",
                BindingFlags.Instance | BindingFlags.NonPublic);
            inputQueue = f != null ? f.GetValue(player) as Queue<int> : null;
        }

        Debug.Log($"[MOCK] start player={(player != null)} pathGen={(pathGen != null)} rig={(rig != null)} " +
                  $"queue={(inputQueue != null)} aspect={(cam != null ? cam.aspect : 0f):F3} " +
                  $"res={Screen.width}x{Screen.height}");
    }

    void Update()
    {
        if (player == null || pathGen == null) return;

        DriveInput();

        if (player.currentCube != lastCube) { if (lastCube != null) cubeChanges++; lastCube = player.currentCube; }
        if (player.isFlying) flyingSamples++;

        frames++;
        if (logEveryFrames > 0 && frames % logEveryFrames == 0) LogProbe();
    }

    // Runs after the rig has committed this frame's pose.
    void LateUpdate()
    {
        if (rig == null || cam == null) return;

        Vector3 pos = cam.transform.position;
        Quaternion rot = cam.transform.rotation;
        float ortho = cam.orthographicSize;

        bool isStatic = rig.CurrentState == CameraParentRotator.CameraState.Static;

        if (havePrevPose)
        {
            if (isStatic)
            {
                staticFrames++;

                float posDrift = Vector3.Distance(pos, lastCamPos);
                float rotDrift = Quaternion.Angle(rot, lastCamRot);
                float orthoDrift = Mathf.Abs(ortho - lastOrtho);

                if (posDrift > stillnessEpsilon || rotDrift > stillnessEpsilon || orthoDrift > stillnessEpsilon)
                {
                    staticDriftFrames++;
                    maxStaticPosDrift = Mathf.Max(maxStaticPosDrift, posDrift);
                    maxStaticOrthoDrift = Mathf.Max(maxStaticOrthoDrift, orthoDrift);
                }
            }
            else
            {
                transitionFrames++;
            }
        }

        int zoneIdx = rig.CurrentZoneIndex;
        if (zoneIdx != lastZoneIndex)
        {
            if (lastZoneIndex != -999) transitionCount++;
            lastZoneIndex = zoneIdx;
        }

        lastCamPos = pos;
        lastCamRot = rot;
        lastOrtho = ortho;
        havePrevPose = true;
    }

    // ------------------------------------------------------------------
    // Human-timed input
    // ------------------------------------------------------------------

    void DriveInput()
    {
        var corners = pathGen.GetUpcomingCorners(1);
        if (corners.Count == 0) { hasPending = false; return; }

        Vector3 cornerPos = corners[0].position;
        float speed = Mathf.Max(0.01f, player.forwardSpeed);
        float timeToCorner = Vector3.Distance(player.transform.position, cornerPos) / speed;

        // One commitment per corner. Re-deciding one already acted on lands mid-rotation, reads
        // a stale side index, and queues another quarter turn past the open face.
        if (Vector3.Distance(cornerPos, handledCornerPos) <= 0.01f) return;

        if (!hasPending || Vector3.Distance(cornerPos, pendingCornerPos) > 0.01f)
        {
            if (timeToCorner > perceptionLead) return;
            pendingCornerPos = cornerPos;
            pendingArmedAt = Time.time;
            pendingLead = Mathf.Max(0f, reactionLead + Random.Range(-reactionJitter, reactionJitter));
            hasPending = true;
        }

        if (Time.time - pendingArmedAt < pendingLead) return;
        hasPending = false;
        handledCornerPos = cornerPos;

        int presses = PressesToOpenTurn(corners[0], out int dir);
        if (presses > 0 && simulateInput && inputQueue != null)
        {
            for (int i = 0; i < presses && inputQueue.Count < 5; i++) inputQueue.Enqueue(dir);
            cornersHandled++;
        }
    }

    /// <summary>
    /// The runner's forward probe only finds the corner cube from the face pointing toward the
    /// outgoing leg, so taking a turn means rotating onto that face first. ChangeSide(d) spins
    /// the face normal by -90*d around the cube's forward axis.
    /// </summary>
    int PressesToOpenTurn(DynamicPathGenerator.PathCorner c, out int dir)
    {
        dir = 1;
        if (player.currentCube == null) return 0;

        Vector3 axis = player.currentCube.forward;
        Vector3 want = c.outgoingDirection.normalized;
        Vector3 have = CurrentFaceNormal();

        if (Vector3.Dot(have, want) > 0.9f) return 0;
        if (Vector3.Dot(have, want) < -0.9f) return 2;

        if (Vector3.Dot(Quaternion.AngleAxis(-90f, axis) * have, want) > 0.9f) { dir = 1; return 1; }
        dir = -1;
        return 1;
    }

    // Mirrors CubeRailRunner.GetFaceNormalWorld, which is private.
    Vector3 CurrentFaceNormal()
    {
        Transform c = player.currentCube;
        switch (player.currentSideIndex)
        {
            case 0: return c.up;
            case 1: return c.right;
            case 2: return -c.up;
            case 3: return -c.right;
            default: return c.up;
        }
    }

    // ------------------------------------------------------------------
    // Probe
    // ------------------------------------------------------------------

    void LogProbe()
    {
        if (cam == null || rig == null) return;

        Vector3 vp = cam.WorldToViewportPoint(player.transform.position);
        bool bad = vp.y < playerMinViewportY || vp.x < 0f || vp.x > 1f || vp.y > 1f;

        samples++;
        if (bad) playerOutOfFrame++;
        orthoMin = Mathf.Min(orthoMin, rig.CurrentOrthoSize);
        orthoMax = Mathf.Max(orthoMax, rig.CurrentOrthoSize);

        string line = $"[FRAME {frames}] {(bad ? "FAIL" : "ok")} state={rig.CurrentState} " +
                      $"zone={rig.CurrentZoneIndex}({rig.CurrentZoneSegmentCount}seg) " +
                      $"player=({vp.x:F3},{vp.y:F3}) ortho={rig.CurrentOrthoSize:F1} yaw={rig.CurrentYaw:F0} " +
                      $"pan={rig.CurrentPanMagnitude:F2} speed={player.forwardSpeed:F1} " +
                      $"cubes={cubeChanges} fly={player.isFlying}";

        if (bad) Debug.LogError(line);
        else Debug.Log(line);
    }

    void OnDisable()
    {
        if (samples == 0) return;

        // One line each: the Unity console truncates multi-line entries.
        Debug.Log($"[SUMMARY-RUN] frames={frames} samples={samples} inputs={cornersHandled} " +
                  $"cornersTaken={cubeChanges} flyingFrames={flyingSamples} outOfFrame={playerOutOfFrame}");

        Debug.Log($"[SUMMARY-STILLNESS] staticFrames={staticFrames} driftFrames={staticDriftFrames} " +
                  $"maxPosDrift={maxStaticPosDrift:F6} maxOrthoDrift={maxStaticOrthoDrift:F6}");

        float moving = staticFrames + transitionFrames;
        Debug.Log($"[SUMMARY-ZONES] transitions={transitionCount} transitionFrames={transitionFrames} " +
                  $"staticShare={(moving > 0 ? 100f * staticFrames / moving : 0f):F1}% " +
                  $"ortho min={orthoMin:F1} max={orthoMax:F1}");
    }
}
