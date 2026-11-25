using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stage-director camera for the cube rail runner.
///
/// The rig frames a ZONE - a cluster of segments solved as one diorama shot by
/// DynamicPathGenerator - holds it while the runner crosses it, then travels to the next shot.
///
///   Static        - pose := currentZone's solved shot, held for at least minHoldSeconds.
///   Transitioning - pose := SmootherStep(from, to, t). Moves that rotate take longer.
///
/// Camera angles are not picked zone by zone. DynamicPathGenerator.PlanCameraYaws chooses them
/// for the whole run of zones ahead at once and charges for every change of angle, so the
/// camera keeps its angle across several shots and turns in single 90 degree steps.
///
/// On top of the shot sit three bounded motions, each with its own slider:
///   * follow        - the shot drifts part of the way toward the runner (0 = locked diorama),
///   * lean          - the camera turns a few degrees in place toward the NEXT shot's angle,
///                     so the move that follows has less rotation left to do,
///   * keep-in-frame - a hard constraint that the runner and the next stretch of track are on
///                     screen every frame, in every state. This is what stops the route
///                     vanishing mid-transition.
///
/// The pose is held as a FOCUS point plus yaw, and the camera position is derived from it.
/// Interpolating the camera POSITION instead (as this rig used to) cuts the corner of the orbit
/// when the yaw changes: halfway through a large turn the camera sits well inside its boom
/// length, and the near plane clips the very track it is supposed to be showing.
/// </summary>
public partial class CameraParentRotator : MonoBehaviour
{
    public enum CameraState { Static, Transitioning }

    /// <summary>C = the original rig below, untouched. A / B / D / E = the experimental modes in CameraParentRotator.ZoneGlide.cs / .Follow.cs / .Rail.cs / .ZoneDrift.cs.</summary>
    public enum CameraMode { C_Original = 0, A_ZoneGlide = 1, B_Follow = 2, D_Rail = 3, E_ZoneDrift = 4 }

    [Header("Camera Mode (A / B / D / E are experimental; C is the original behaviour)")]
    [Tooltip("C_Original runs the code in this file exactly as before. A_ZoneGlide / B_Follow / D_Rail / E_ZoneDrift use the Feel Profile. In the Editor and development builds the keys C / Z / X / V / B switch to C / A / B / D / E at runtime.")]
    [SerializeField] CameraMode cameraMode = CameraMode.C_Original;
    [Tooltip("Tuning for modes A, B, D and E.")]
    [SerializeField] CameraFeelProfile feelProfile;

    [Header("References")]
    public CubeRailRunner player;
    public DynamicPathGenerator pathGen;
    private float boostZoom = 1f;   // 1 = normal, up to pathGen.boostCameraZoomOut while on a boost
    [Tooltip("Child camera. Auto-found in children when left empty.")]
    public Camera boomCamera;

    [Header("Locked Isometric Angle")]
    [Tooltip("Held EXACTLY. 35.264 = atan(1/sqrt(2)), the true isometric tilt.")]
    public float isometricTiltX = 35.264f;
    [Tooltip("Shoulder offset from the zone's average heading. 45 with a snapped heading gives the four classic isometric yaws and opens the inner corner of every turn.")]
    public float isoYawOffset = 45f;
    [Tooltip("Snap the zone heading to 90 degrees before adding the offset. Off = the yaw follows the raw average.")]
    public bool quantiseZoneYaw = true;

    [Header("Choosing The Angle")]
    [Tooltip("Cost of one 90 degree change of angle when planning the shots ahead. Higher = the camera keeps the same angle for longer and turns only when the path really needs it. 0 = every shot takes its own best angle.")]
    [Range(0f, 30f)] public float angleChangeCost = 10f;

    [Header("Zone Framing")]
    [Tooltip("World units of slack around the zone bounds.")]
    public float framingPadding = 3f;
    [Tooltip("Floor on the solved size, so a short zone is not framed so tightly that a cube fills the screen. The Orthographic Size set on the Camera in the scene is a floor too - the rig never frames tighter than the zoom you authored.")]
    public float minOrthoSize = 12f;
    [Tooltip("Pull-back along the view axis. Orthographic framing does not depend on it - it only decides what the near plane clips - and the solver raises it for a large zone.")]
    public float boomDistance = 60f;

    [Header("Zone Transition")]
    [Tooltip("Ceiling on a move timed to the runner. Moves that rotate are lengthened past this by secondsPer90Degrees.")]
    public float transitionDuration = 2.2f;
    [Tooltip("Time the move to the runner crossing the hand-off segment, so the new shot lands as they arrive.")]
    public bool matchTransitionToRunner = true;
    [Tooltip("Floor on any move, so even a short hand-off eases rather than cuts.")]
    public float minTransitionDuration = 1.1f;
    [Tooltip("How many segments before the end of a zone the move begins.")]
    [Range(0, 3)] public int transitionLeadSegments = 0;
    [Tooltip("Distance beyond which a change of zone is treated as a teleport (restart, regeneration) and cut to rather than travelled.")]
    public float snapDistance = 200f;
    [Tooltip("Shortest time a shot is held before the camera may move again. Stops a new move starting right after the last one landed; keep-in-frame covers the runner meanwhile.")]
    [Range(0f, 10f)] public float minHoldSeconds = 4f;
    [Tooltip("Most the zoom may tighten in one shot change, as a share of the current size. It can widen as much as it needs to. Stops the zoom pumping in and out between shots.")]
    [Range(0f, 0.5f)] public float maxZoomInPerShot = 0.25f;
    [Tooltip("Extra seconds a move takes per 90 degrees it rotates, so turns are slow and a straight pan stays quick.")]
    [Range(0f, 5f)] public float secondsPer90Degrees = 1.8f;

    [Header("Follow & Lean")]
    [Tooltip("How much a held shot drifts toward the runner before the camera changes angle. 0 = fully locked diorama, 1 = keeps the runner centred.")]
    [Range(0f, 1f)] public float followPlayer = 0.3f;
    [Tooltip("Seconds the follow takes to catch up. Higher = lazier, calmer drift.")]
    public float followSmoothTime = 0.8f;
    [Tooltip("Degrees the camera turns in place during a held shot, toward the angle of the next shot, so the move after it rotates less. 0 = never turns in place.")]
    [Range(0f, 20f)] public float inPlaceLeanDegrees = 8f;
    public float leanSmoothTime = 1.5f;

    [Header("Keep Path In Frame")]
    [Tooltip("Fewest cubes ahead of the runner that must always be on screen, together with the runner, in every state including transitions.")]
    [Range(0, 40)] public int lookAheadCubes = 12;
    [Tooltip("The look-ahead also runs on until this many turns have been passed, so upcoming corners are never cut off at the screen edge. 0 = just the cube count.")]
    [Range(0, 4)] public int lookAheadCorners = 2;
    [Tooltip("Ceiling on the look-ahead, so a very long leg cannot force a huge zoom-out.")]
    [Range(10, 120)] public int lookAheadMaxCubes = 90;
    [Tooltip("Seconds the look-ahead's extent is eased over, so the window sliding along the track (and jumping a whole leg at each corner) does not make the zoom or pan step. 0 = raw.")]
    [Range(0f, 1.5f)] public float extentSmoothTime = 0.35f;
    [Tooltip("Share of the half-screen those points may reach before the camera starts easing to bring them back.")]
    [Range(0.3f, 0.95f)] public float softFrameLimit = 0.7f;
    [Tooltip("Share of the half-screen those points are NEVER allowed past. Enforced every frame without smoothing, so it is the guarantee rather than the comfort zone.")]
    [Range(0.5f, 0.98f)] public float hardFrameLimit = 0.88f;
    [Tooltip("Seconds the comfort correction takes to settle.")]
    public float keepInFrameSmoothTime = 0.5f;
    [Tooltip("Multiplies the final zoom: 1 = as fitted, 1.2 = 20% further out. Widens the shot so you can see track fading in ahead and out behind. Applied on top of everything else, so the hard frame limit still holds.")]
    [Range(0.6f, 2.5f)] public float zoomOutMultiplier = 1.2f;
    [Tooltip("Fit the zoom to the track that is actually drawn, so it tightens when only a little of it is on show. Off = only ever zoom out, as the shot needs.")]
    public bool fitZoomToVisibleTrack = true;
    [Tooltip("Seconds the zoom takes to settle on its fit. Slow on purpose - the zoom drifting is far more noticeable than the pan - while the hard frame limit still snaps wider when it has to.")]
    [Range(0.2f, 6f)] public float zoomSmoothTime = 2.2f;
    [Tooltip("How fast the zoom is allowed to TIGHTEN, in orthographic units per second. Low = the view stays wide for a while after a big shot instead of pumping back in. It widens as fast as it needs to.")]
    [Range(0.2f, 10f)] public float zoomTightenPerSecond = 1.5f;

    // All three features below default to OFF, so every scene that does not switch them on behaves as before.
    [Header("Speed Zoom (opt-in)")]
    [Tooltip("Widen the zoom as the runner gets faster: 1.0 at the starting speed up to speedZoomMax at the runner's max speed.")]
    public bool speedZoomEnabled = false;
    [Range(1f, 2f)] public float speedZoomMax = 1.4f;
    [Tooltip("Seconds the speed zoom takes to catch up. Higher = gentler widening.")]
    public float speedZoomSmoothTime = 1.5f;

    [Header("Hard Box Spring (opt-in)")]
    [Tooltip("Replaces the instant hard-limit clamp with a progressive spring: the keep-in-frame slide uses keepInFrameSmoothTime near the soft limit and lerps down to hardSpringFastSmoothTime as the content nears the hard limit.")]
    public bool hardBoxSpring = false;
    [Tooltip("Smooth time at the hard limit. Small = strong rubber band.")]
    public float hardSpringFastSmoothTime = 0.05f;

    [Header("Look-ahead Centre (opt-in)")]
    [Tooltip("Damp the CENTRE of the path-ahead box separately from its size, so a new corner entering the look-ahead drifts the frame instead of snapping it.")]
    public bool smoothLookAheadCenter = false;
    [Tooltip("Seconds the centre of the look-ahead box takes to settle. Its size still uses extentSmoothTime.")]
    public float lookAheadCenterSmoothTime = 0.6f;

    [Header("Speed-scaled Turn Lead (opt-in)")]
    [Tooltip("Start the camera turn earlier as the runner gets faster: adds up to leadMaxExtraSegments to transitionLeadSegments, scaled by speed between the starting and maximum speed.")]
    public bool speedScaledLead = false;
    [Range(0, 3)] public int leadMaxExtraSegments = 2;

    [Header("Flip Impact (opt-in)")]
    [Tooltip("TriggerFlipImpact() does nothing unless this is on.")]
    public bool flipImpactEnabled = false;
    [Tooltip("Peak shake offset in world units (screen axes; the camera is orthographic).")]
    public float shakeMagnitude = 0.4f;
    public float shakeDuration = 0.15f;
    [Tooltip("Amount taken off the zoom multiplier at the peak of the punch-in.")]
    public float punchAmount = 1.5f;
    public float punchDuration = 0.1f;
    [Tooltip("The zoom multiplier never goes below this, so a 1.5 punch cannot invert or collapse the view. 0.92 = 8% punch-in.")]
    [Range(0.5f, 1f)] public float punchMinZoom = 0.92f;

    // ---- runtime state ----
    private float speedZoom = 1f, speedZoomVelocity, speedZoomStart = -1f;
    private float shakeTimer = -1f, punchTimer = -1f;
    private CameraState state = CameraState.Static;
    private DynamicPathGenerator.PathZone currentZone;
    private DynamicPathGenerator.PathZone targetZone;

    // the authored shot, before follow / lean / keep-in-frame
    private Vector3 baseFocus;
    private float baseYaw;
    private float baseOrtho;
    private float baseBoom;

    // transition endpoints
    private Vector3 fromFocus;
    private float fromYaw;
    private float fromOrtho;
    private float fromBoom;
    private float activeTargetOrtho;
    private float transitionTimer;
    private float activeTransitionDuration = 1.5f;
    private float holdTimer;

    private float lean, leanVelocity;
    private Vector2 followOffset, followVelocity;     // in screen axes: x = camera right, y = camera up
    private Vector2 frameShift, frameShiftVelocity;   // keep-in-frame slide, same axes
    private float extraOrtho, extraOrthoVelocity;     // keep-in-frame zoom-out (fit-zoom off)
    private float zoomPeak, zoomShown, zoomVelocity;  // fit-zoom: the envelope, and the eased size

    private float extMinX, extMaxX, extMinY, extMaxY; // eased look-ahead extent, relative to focus
    private Vector4 extVel;
    private Vector2 lookCenterVel, lookHalfVel;
    private bool extentsValid;

    private PathDepthShading visibleShading;
    private int plannedAtGeneration = -1;
    private float authoredOrthoSize;   // the Camera's own size in the scene, read before the rig first writes it
    private readonly List<Vector3> mustSee = new List<Vector3>();

    // ---- exposed for tooling / the play-mode harness ----
    public CameraState CurrentState => state;
    public float CurrentOrthoSize => baseOrtho;
    public float CurrentYaw => baseYaw;
    public float CurrentPanMagnitude => (followOffset + frameShift).magnitude;
    public int CurrentZoneIndex => pathGen != null ? pathGen.IndexOfZone(currentZone) : -1;
    public int CurrentZoneSegmentCount => currentZone != null ? currentZone.segments.Count : 0;

    // ---- LookTransition hooks (MainScene prop camera 3.2 only; nothing subscribes anywhere else) ----
    /// <summary>Raised at the very end of LateUpdate, after whichever mode has written its pose: a subscriber may read
    /// that pose (it is this frame's live target) and overwrite the rig's transform, camera tilt and size. No subscriber = no change.</summary>
    public event System.Action<CameraParentRotator> CameraOverride;
    /// <summary>The Camera's Orthographic Size as authored in the scene (the rig never frames tighter).</summary>
    public float AuthoredOrthoSize => authoredOrthoSize;
    /// <summary>While true, modes D / E start no planned yaw change and no lean (RailRotationBlocked). Runtime only, default off.</summary>
    [System.NonSerialized] public bool SuppressYawChanges;

    /// <summary>
    /// The camera's orientation right now, without the lean sway - or, while it is swinging, the
    /// orientation that swing ends on. No guessing about moves that have not started. The runner
    /// reads it when it lands on a new leg to decide whether left/right need to change.
    /// </summary>
    public Quaternion SettledViewRotation
    {
        get
        {
            if (ModeSettledYaw(out float modeYaw)) return Quaternion.Euler(isometricTiltX, modeYaw, 0f);
            float yaw = baseYaw;
            if (state == CameraState.Transitioning && targetZone != null) yaw = targetZone.optimalCameraYaw;
            else if (pathGen != null && currentZone == null)
            {
                // No shot taken yet (first frame): the rig opens on the runner's zone straight away.
                DynamicPathGenerator.PathZone playerZone = pathGen.PlayerZone;
                if (playerZone != null)
                {
                    SolveZone(playerZone);
                    if (playerZone.framingSolved) yaw = playerZone.optimalCameraYaw;
                }
            }
            return Quaternion.Euler(isometricTiltX, yaw, 0f);
        }
    }

    void Start()
    {
        if (player == null) player = FindFirstObjectByType<CubeRailRunner>();
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (boomCamera == null) boomCamera = GetComponentInChildren<Camera>();
        if (boomCamera == null) boomCamera = Camera.main;

        if (boomCamera != null)
        {
            boomCamera.orthographic = true;
            authoredOrthoSize = boomCamera.orthographicSize;
        }

        if (player == null || pathGen == null || boomCamera == null)
        {
            Debug.LogWarning("CameraParentRotator: missing player, generator or camera - the rig will not run.", this);
            return;
        }

        speedZoomStart = player.forwardSpeed;

        // Open already framed rather than sliding in from the scene's authored pose.
        PlanIfStale();
        currentZone = pathGen.PlayerZone;
        AdoptZone(currentZone);
        ApplyPose();
    }

    /// <summary>TUTORIAL ADDITION: after the path was regenerated, drop any glide in progress and open on the runner's zone again, as Start does.</summary>
    public void SnapToPlayerZone()
    {
        if (player == null || pathGen == null || boomCamera == null) return;
        state = CameraState.Static;
        targetZone = null;
        PlanIfStale();
        currentZone = pathGen.PlayerZone;
        AdoptZone(currentZone);
        ApplyPose();
        ModeAdoptCurrentPose();   // A / B restart from the opening pose (no-op in C)
    }

    void LateUpdate()
    {
        if (player == null || pathGen == null || boomCamera == null) return;

        PlanIfStale();

        if (ModeTick()) { CameraOverride?.Invoke(this); return; }   // A / B drive the pose; C (the default) falls through to the original code below

        switch (state)
        {
            case CameraState.Static: TickStatic(); break;
            case CameraState.Transitioning: TickTransition(); break;
        }

        UpdateLean();
        ApplyPose();
        CameraOverride?.Invoke(this);
    }

    // ------------------------------------------------------------------
    // Planning
    // ------------------------------------------------------------------

    /// <summary>
    /// Lets set dressing ask where the shot for a zone will stand, before the rig gets there.
    /// Uses the rig's own settings and the current plan, so the answer is the one the camera
    /// will use unless later track changes the plan.
    /// </summary>
    public void EnsureZoneSolved(DynamicPathGenerator.PathZone zone)
    {
        PlanIfStale();
        SolveZone(zone);
    }

    // Re-plan the angles whenever the generator has added track.
    void PlanIfStale()
    {
        if (pathGen == null || pathGen.pathGenerationCount == plannedAtGeneration) return;
        plannedAtGeneration = pathGen.pathGenerationCount;

        pathGen.PlanCameraYaws(isometricTiltX, isoYawOffset, quantiseZoneYaw,
                               angleChangeCost, pathGen.LatestHeading);
    }

    void SolveZone(DynamicPathGenerator.PathZone zone)
    {
        if (zone == null || !zone.isSealed || zone.segments.Count == 0) return;

        zone.SolveFraming(
            isometricTiltX,
            isoYawOffset,
            quantiseZoneYaw,
            CurrentAspect(),
            framingPadding,
            MinFrameSize(),
            boomDistance,
            pathGen != null ? pathGen.LatestHeading : Vector3.forward);
    }

    // ------------------------------------------------------------------
    // States
    // ------------------------------------------------------------------

    /// <summary>Holds the shot and watches for the cue to leave.</summary>
    void TickStatic()
    {
        holdTimer += Time.deltaTime;

        DynamicPathGenerator.PathZone playerZone = pathGen.PlayerZone;

        // No shot yet, or the path was regenerated out from under it: establish, don't travel.
        if (currentZone == null || pathGen.IndexOfZone(currentZone) < 0)
        {
            currentZone = playerZone;
            AdoptZone(currentZone);
            return;
        }

        // A shot that has only just landed is held, even if the runner is already leaving it.
        // Keep-in-frame holds them and the track ahead on screen in the meantime, which is far
        // calmer than starting a second move on the back of the first.
        if (holdTimer < minHoldSeconds) return;

        // The runner outran the shot entirely - go straight to where they are.
        if (playerZone != null && playerZone != currentZone)
        {
            BeginTransition(playerZone);
            return;
        }

        int lead = transitionLeadSegments;
        if (speedScaledLead)
            lead += Mathf.RoundToInt(Mathf.Lerp(0f, leadMaxExtraSegments, SpeedFraction()));

        if (pathGen.IsPlayerNearZoneEnd(currentZone, lead))
        {
            DynamicPathGenerator.PathZone next = pathGen.GetZoneAfter(currentZone);
            if (next != null) BeginTransition(next);
        }
    }

    /// <summary>
    /// Travels between two shots on a single SmootherStep, which has zero velocity AND zero
    /// acceleration at both ends, so the move eases in and settles without a jolt.
    /// </summary>
    void TickTransition()
    {
        if (targetZone == null) { FinishTransition(); return; }

        transitionTimer += Time.deltaTime;

        float duration = Mathf.Max(0.01f, activeTransitionDuration);
        float t = SmootherStep(Mathf.Clamp01(transitionTimer / duration));

        baseFocus = Vector3.Lerp(fromFocus, targetZone.optimalFocus, t);
        baseYaw = Mathf.LerpAngle(fromYaw, targetZone.optimalCameraYaw, t);
        baseOrtho = Mathf.Lerp(fromOrtho, activeTargetOrtho, t);
        baseBoom = Mathf.Max(fromBoom, targetZone.optimalBoom);

        if (transitionTimer >= duration) FinishTransition();
    }

    void BeginTransition(DynamicPathGenerator.PathZone next)
    {
        SolveZone(next);
        if (next == null || !next.framingSolved) return;

        // A regenerated path can put the next shot on the far side of the world; sweeping the
        // camera across that reads as a mistake, not a move.
        if (Vector3.Distance(baseFocus, next.optimalFocus) > snapDistance)
        {
            currentZone = next;
            AdoptZone(next);
            return;
        }

        // Start from what is on screen, lean included, so the move picks up where it is.
        fromFocus = baseFocus;
        fromYaw = baseYaw + lean;
        fromOrtho = baseOrtho;
        fromBoom = baseBoom;

        // The zoom may grow to fit the next shot, but shrinks only a little per shot. Following
        // each zone's own size made it pump between roughly 30 and 57 from one shot to the next,
        // which reads as the world breathing. Zooming back in over a few shots is imperceptible.
        activeTargetOrtho = Mathf.Max(next.optimalOrthoSize, fromOrtho * (1f - maxZoomInPerShot));
        lean = 0f;
        leanVelocity = 0f;

        float timed = matchTransitionToRunner
            ? Mathf.Clamp(EstimateHandoverSeconds(), minTransitionDuration, transitionDuration)
            : transitionDuration;

        float quarters = Mathf.Abs(Mathf.DeltaAngle(fromYaw, next.optimalCameraYaw)) / 90f;
        activeTransitionDuration = Mathf.Max(timed, minTransitionDuration + quarters * secondsPer90Degrees);

        // Claim the destination before travelling to it, so the plan cannot move it mid-flight.
        next.LockFraming();

        targetZone = next;
        transitionTimer = 0f;
        state = CameraState.Transitioning;
    }

    /// <summary>How long the runner still needs to clear the current zone.</summary>
    float EstimateHandoverSeconds()
    {
        if (player == null || currentZone == null) return transitionDuration;

        DynamicPathGenerator.Segment final = currentZone.FinalSegment;
        if (final == null) return transitionDuration;

        float distance = Vector3.Distance(player.transform.position, final.endPosition);
        return distance / Mathf.Max(0.01f, player.forwardSpeed);
    }

    void FinishTransition()
    {
        if (targetZone != null)
        {
            currentZone = targetZone;
            // Keep follow and keep-in-frame: they are holding the runner on screen right now.
            AdoptZone(currentZone, resetMotion: false);
            baseOrtho = activeTargetOrtho; // the size the move landed on, not the zone's own
        }

        targetZone = null;
        state = CameraState.Static;
    }

    /// <summary>Snaps the rig onto a zone's solved shot.</summary>
    void AdoptZone(DynamicPathGenerator.PathZone zone, bool resetMotion = true)
    {
        SolveZone(zone);

        if (zone != null && zone.framingSolved)
        {
            zone.LockFraming();
            baseFocus = zone.optimalFocus;
            baseYaw = zone.optimalCameraYaw;
            baseOrtho = zone.optimalOrthoSize;
            baseBoom = zone.optimalBoom;
        }
        else
        {
            // Nothing framed yet: look at the runner on the default angle so frame one is still
            // a valid isometric shot.
            baseYaw = isoYawOffset;
            baseOrtho = Mathf.Max(MinFrameSize(), 1f);
            baseFocus = player != null ? player.transform.position : Vector3.zero;
            baseBoom = boomDistance;
        }

        if (resetMotion)
        {
            followOffset = followVelocity = Vector2.zero;
            frameShift = frameShiftVelocity = Vector2.zero;
            extraOrtho = extraOrthoVelocity = 0f;
            zoomPeak = zoomShown = zoomVelocity = 0f;
            extentsValid = false;
            lean = leanVelocity = 0f;
        }

        state = CameraState.Static;
        transitionTimer = 0f;
        holdTimer = 0f;
    }

    // ------------------------------------------------------------------
    // Lean
    // ------------------------------------------------------------------

    /// <summary>
    /// During a held shot, turns the camera in place - about the shot's focus - a few degrees
    /// toward the next shot's angle, growing as the runner crosses the zone. The move that
    /// follows then has that much less rotation to do.
    /// </summary>
    void UpdateLean()
    {
        if (state != CameraState.Static) return;

        float target = 0f;
        if (inPlaceLeanDegrees > 0f && currentZone != null)
        {
            DynamicPathGenerator.PathZone next = pathGen.GetZoneAfter(currentZone);
            SolveZone(next);
            if (next != null && next.framingSolved)
            {
                float delta = Mathf.DeltaAngle(baseYaw, next.optimalCameraYaw);
                target = Mathf.Clamp(delta, -inPlaceLeanDegrees, inPlaceLeanDegrees) *
                         pathGen.PlayerProgressThrough(currentZone);
            }
        }

        lean = Mathf.SmoothDamp(lean, target, ref leanVelocity, Mathf.Max(0.01f, leanSmoothTime));
    }

    // ------------------------------------------------------------------
    // Placement
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the final pose from the shot plus follow, lean and keep-in-frame, and commits it.
    /// The pivot carries the yaw and the child camera the tilt, so parent * child rebuilds
    /// Euler(tilt, yaw, 0) exactly and the isometric angle cannot drift.
    /// </summary>
    void ApplyPose()
    {
        if (boomCamera == null) return;

        float yaw = baseYaw + lean;
        Quaternion rot = Quaternion.Euler(isometricTiltX, yaw, 0f);
        Vector3 right = rot * Vector3.right;
        Vector3 up = rot * Vector3.up;
        Vector3 forward = rot * Vector3.forward;
        float aspect = CurrentAspect();
        float dt = Time.deltaTime;

        // 1. Follow: drift part of the way toward the runner.
        Vector2 followTarget = Vector2.zero;
        if (player != null && followPlayer > 0f)
        {
            Vector3 d = player.transform.position - baseFocus;
            followTarget = new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up)) * followPlayer;
        }
        followOffset = dt > 0f
            ? Vector2.SmoothDamp(followOffset, followTarget, ref followVelocity, Mathf.Max(0.01f, followSmoothTime))
            : followTarget;

        Vector3 focus = baseFocus + right * followOffset.x + up * followOffset.y;

        // 2. Keep the runner and the track just ahead of them in frame.
        float ortho = baseOrtho * zoomOutMultiplier;
        Vector2 shift = Vector2.zero;
        if (CollectMustSee(focus, right, up, out float minX, out float maxX, out float minY, out float maxY))
        {
            float soft = Mathf.Min(softFrameLimit, hardFrameLimit);
            float hard = hardFrameLimit;

            // Fit the zoom to what is on show. Wider when the drawn track needs it (the hard limit
            // snaps, so nothing is ever cut off); and, if fitZoomToVisibleTrack is on, tighter
            // when the drawn track is compact - never below the authored zoom - so the runner is
            // not left tiny in a frame sized for track that is hidden or already faded.
            float spanHalfH = Mathf.Max((maxY - minY) * 0.5f, (maxX - minX) * 0.5f / Mathf.Max(0.01f, aspect));
            if (fitZoomToVisibleTrack)
            {
                // The zoom follows the UPPER ENVELOPE of the fit: it widens as soon as the drawn
                // track needs it and tightens only slowly (zoomTightenPerSecond), so it does not
                // swing in and out as the window of visible track slides along. It is a size of
                // its own - not an offset from the shot's base zoom, which is itself moving during
                // a camera move and made the total pump. The hard limit still snaps if it must.
                float fit = Mathf.Max(spanHalfH / soft, MinFrameSize());
                zoomPeak = zoomPeak <= 0f ? fit : Mathf.Max(fit, zoomPeak - zoomTightenPerSecond * dt);

                if (zoomShown <= 0f || dt <= 0f) zoomShown = zoomPeak;
                else
                {
                    float smooth = Mathf.Max(0.05f, zoomPeak > zoomShown ? zoomSmoothTime * 0.5f : zoomSmoothTime);
                    zoomShown = Mathf.SmoothDamp(zoomShown, zoomPeak, ref zoomVelocity, smooth);
                }

                ortho = Mathf.Max(zoomShown, spanHalfH / hard) * zoomOutMultiplier;
            }
            else
            {
                float hardExtra = Mathf.Max(0f, spanHalfH / hard - baseOrtho);
                float softExtra = Mathf.Max(0f, spanHalfH / soft - baseOrtho);
                extraOrtho = dt > 0f
                    ? Mathf.SmoothDamp(extraOrtho, softExtra, ref extraOrthoVelocity, Mathf.Max(0.01f, zoomSmoothTime))
                    : softExtra;
                extraOrtho = Mathf.Max(extraOrtho, hardExtra);
                ortho = (baseOrtho + extraOrtho) * zoomOutMultiplier;
            }

            // Boost: pull back while the runner is on a zig-zag boost, ease back after.
            float boostGoal = pathGen.PlayerOnZigZagBoost ? Mathf.Max(1f, pathGen.boostCameraZoomOut) : 1f;
            boostGoal = Mathf.Max(boostGoal, pathGen.PowerZoom);   // power-diamond autopilot: a single glide out
            float boostSecs = boostGoal > boostZoom ? pathGen.boostCameraZoomInSeconds : pathGen.boostCameraZoomOutSeconds;
            boostZoom = dt > 0f
                ? Mathf.MoveTowards(boostZoom, boostGoal, dt * Mathf.Abs(Mathf.Max(pathGen.boostCameraZoomOut, pathGen.PowerZoom) - 1f) / Mathf.Max(0.05f, boostSecs))
                : boostGoal;
            ortho *= boostZoom;

            // Speed zoom: only ever widens (>= 1), so MinFrameSize and the hard limits still hold.
            ortho *= UpdateSpeedZoom(dt);

            // Flip punch-in: may tighten, so the hard limit is re-imposed below.
            float punch = PunchMultiplier(dt);
            if (punch < 1f) ortho = Mathf.Max(ortho * punch, spanHalfH / hard);

            float halfH = ortho;
            float halfW = ortho * aspect;

            // Slide: ease toward the smallest shift that puts everything in the comfort box...
            Vector2 softTarget = new Vector2(
                ShiftToFit(minX, maxX, halfW * soft),
                ShiftToFit(minY, maxY, halfH * soft));
            float frameSmooth = Mathf.Max(0.01f, keepInFrameSmoothTime);
            if (hardBoxSpring)
            {
                // Progressive spring: how much of the half-screen the content uses at the current shift.
                // The smooth time runs from keepInFrameSmoothTime at the soft limit down to
                // hardSpringFastSmoothTime at the hard limit, so it pulls harder the closer it gets.
                float use = Mathf.Max(
                    Mathf.Max(maxX - frameShift.x, frameShift.x - minX) / Mathf.Max(0.01f, halfW),
                    Mathf.Max(maxY - frameShift.y, frameShift.y - minY) / Mathf.Max(0.01f, halfH));
                float closeness = Mathf.InverseLerp(soft, Mathf.Max(hard, soft + 0.001f), use);
                frameSmooth = Mathf.Lerp(frameSmooth, Mathf.Min(frameSmooth, Mathf.Max(0.01f, hardSpringFastSmoothTime)), closeness);
            }
            frameShift = dt > 0f
                ? Vector2.SmoothDamp(frameShift, softTarget, ref frameShiftVelocity, frameSmooth)
                : softTarget;

            // ...then enforce the hard box outright. Folding the hard correction back into the
            // eased value means it is not undone next frame. (Skipped by the progressive spring.)
            if (!hardBoxSpring)
            {
                frameShift += new Vector2(
                    ShiftToFit(minX - frameShift.x, maxX - frameShift.x, halfW * hard),
                    ShiftToFit(minY - frameShift.y, maxY - frameShift.y, halfH * hard));
            }

            shift = frameShift;
        }

        focus += right * shift.x + up * shift.y;

        transform.SetPositionAndRotation(focus - forward * baseBoom, Quaternion.Euler(0f, yaw, 0f));

        boomCamera.transform.localPosition = ShakeOffset(dt);
        boomCamera.transform.localRotation = Quaternion.Euler(isometricTiltX, 0f, 0f);
        boomCamera.orthographicSize = ortho;
    }

    // ------------------------------------------------------------------
    // Speed zoom & flip impact (opt-in)
    // ------------------------------------------------------------------

    // 0 at the runner's starting speed, 1 at its max speed. The starting speed is the lowest seen,
    // so a restart (speed reset) re-bases it.
    float SpeedFraction()
    {
        if (player == null) return 0f;
        if (speedZoomStart < 0f || player.forwardSpeed < speedZoomStart) speedZoomStart = player.forwardSpeed;

        float span = player.maxSpeed - speedZoomStart;
        return span > 0.01f ? Mathf.Clamp01((player.forwardSpeed - speedZoomStart) / span) : 0f;
    }

    float UpdateSpeedZoom(float dt)
    {
        if (!speedZoomEnabled || player == null) { speedZoom = 1f; speedZoomVelocity = 0f; return 1f; }

        float target = Mathf.Lerp(1f, Mathf.Max(1f, speedZoomMax), SpeedFraction());

        speedZoom = dt > 0f
            ? Mathf.SmoothDamp(speedZoom, target, ref speedZoomVelocity, Mathf.Max(0.01f, speedZoomSmoothTime))
            : target;
        return Mathf.Max(1f, speedZoom);
    }

    /// <summary>
    /// Call when a double-tap / flick flip is executed: a short screen shake plus a brief punch-in.
    /// Does nothing unless flipImpactEnabled is on.
    /// </summary>
    public void TriggerFlipImpact()
    {
        if (!flipImpactEnabled) return;
        shakeTimer = 0f;
        punchTimer = 0f;
    }

    // Multiplier on the ortho size: 1 normally; dips by up to punchAmount (floored at punchMinZoom)
    // at the moment of impact and eases back over punchDuration.
    float PunchMultiplier(float dt)
    {
        if (punchTimer < 0f) return 1f;
        punchTimer += dt;
        float u = punchTimer / Mathf.Max(0.01f, punchDuration);
        if (u >= 1f) { punchTimer = -1f; return 1f; }

        float envelope = 1f - Mathf.SmoothStep(0f, 1f, u);
        return Mathf.Max(punchMinZoom, 1f - punchAmount * envelope);
    }

    // Random screen-plane offset for the child camera, decaying to zero over shakeDuration.
    Vector3 ShakeOffset(float dt)
    {
        if (shakeTimer < 0f) return Vector3.zero;
        shakeTimer += dt;
        float u = shakeTimer / Mathf.Max(0.01f, shakeDuration);
        if (u >= 1f) { shakeTimer = -1f; return Vector3.zero; }

        Vector2 o = Random.insideUnitCircle * (shakeMagnitude * (1f - u));
        return new Vector3(o.x, o.y, 0f);
    }

    // Screen-plane extent of the runner and the next lookAheadCubes cubes, relative to focus.
    bool CollectMustSee(Vector3 focus, Vector3 right, Vector3 up,
                        out float minX, out float maxX, out float minY, out float maxY)
    {
        minX = minY = float.MaxValue;
        maxX = maxY = float.MinValue;

        // At least as far as the drawn track reaches: that is what has to be on screen.
        if (visibleShading == null) visibleShading = pathGen.GetComponent<PathDepthShading>();
        int shown = visibleShading != null && visibleShading.enabled
            ? Mathf.CeilToInt(Mathf.Min(visibleShading.VisibleAheadCubes, lookAheadMaxCubes)) : 0;
        pathGen.GetPathAhead(Mathf.Max(lookAheadCubes, shown), lookAheadCorners, lookAheadMaxCubes, mustSee);

        const float pad = 0.75f; // half a cube plus a little air

        // The look-ahead window changes in steps - a cube at a time, and by a whole leg when the
        // runner passes a corner - and every zoom and pan below is derived from its extent, so
        // raw it made the view jump. Its extent is eased instead. The runner is added afterwards,
        // exact, so the smoothing can never let them out of frame.
        bool any = mustSee.Count > 0;
        float pMinX = float.MaxValue, pMaxX = float.MinValue, pMinY = float.MaxValue, pMaxY = float.MinValue;
        foreach (Vector3 p in mustSee)
        {
            Vector3 d = p - focus;
            float x = Vector3.Dot(d, right);
            float y = Vector3.Dot(d, up);
            pMinX = Mathf.Min(pMinX, x - pad); pMaxX = Mathf.Max(pMaxX, x + pad);
            pMinY = Mathf.Min(pMinY, y - pad); pMaxY = Mathf.Max(pMaxY, y + pad);
        }

        if (any)
        {
            float dt = Time.deltaTime;
            float st = Mathf.Max(0.01f, extentSmoothTime);
            if (!extentsValid || dt <= 0f || extentSmoothTime <= 0f)
            {
                extMinX = pMinX; extMaxX = pMaxX; extMinY = pMinY; extMaxY = pMaxY;
                extVel = Vector4.zero;
                extentsValid = true;
            }
            else
            {
                if (smoothLookAheadCenter)
                {
                    // Centre and half-size are damped separately, each as a Vector2, then rebuilt into
                    // the box: a new corner widening the box moves its centre slowly, not in one step.
                    Vector2 rawCenter = new Vector2((pMinX + pMaxX) * 0.5f, (pMinY + pMaxY) * 0.5f);
                    Vector2 rawHalf = new Vector2((pMaxX - pMinX) * 0.5f, (pMaxY - pMinY) * 0.5f);
                    Vector2 curCenter = new Vector2((extMinX + extMaxX) * 0.5f, (extMinY + extMaxY) * 0.5f);
                    Vector2 curHalf = new Vector2((extMaxX - extMinX) * 0.5f, (extMaxY - extMinY) * 0.5f);

                    curCenter = Vector2.SmoothDamp(curCenter, rawCenter, ref lookCenterVel, Mathf.Max(0.01f, lookAheadCenterSmoothTime));
                    curHalf = Vector2.SmoothDamp(curHalf, rawHalf, ref lookHalfVel, st);

                    extMinX = curCenter.x - curHalf.x; extMaxX = curCenter.x + curHalf.x;
                    extMinY = curCenter.y - curHalf.y; extMaxY = curCenter.y + curHalf.y;
                }
                else
                {
                    extMinX = Mathf.SmoothDamp(extMinX, pMinX, ref extVel.x, st);
                    extMaxX = Mathf.SmoothDamp(extMaxX, pMaxX, ref extVel.y, st);
                    extMinY = Mathf.SmoothDamp(extMinY, pMinY, ref extVel.z, st);
                    extMaxY = Mathf.SmoothDamp(extMaxY, pMaxY, ref extVel.w, st);
                }
            }
            minX = extMinX; maxX = extMaxX; minY = extMinY; maxY = extMaxY;
        }

        if (player != null)
        {
            Vector3 d = player.transform.position - focus;
            float x = Vector3.Dot(d, right);
            float y = Vector3.Dot(d, up);
            minX = Mathf.Min(minX, x - pad); maxX = Mathf.Max(maxX, x + pad);
            minY = Mathf.Min(minY, y - pad); maxY = Mathf.Max(maxY, y + pad);
            any = true;
        }
        return any;
    }

    // Smallest slide that puts [min, max] inside [-half, half]; centres it if it cannot fit.
    static float ShiftToFit(float min, float max, float half)
    {
        if (max - min >= 2f * half) return (min + max) * 0.5f;
        if (max > half) return max - half;
        if (min < -half) return min + half;
        return 0f;
    }

    float MinFrameSize() => Mathf.Max(minOrthoSize, authoredOrthoSize);

    static float SmootherStep(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    float CurrentAspect()
    {
        if (boomCamera != null && boomCamera.aspect > 0.01f) return boomCamera.aspect;
        return 9f / 19.5f;
    }

    // ------------------------------------------------------------------
    // Editor aid
    // ------------------------------------------------------------------

    void OnDrawGizmosSelected()
    {
        if (currentZone == null || !currentZone.isSealed) return;

        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireCube(currentZone.zoneBounds.center, currentZone.zoneBounds.size);

        if (targetZone != null && targetZone.isSealed)
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            Gizmos.DrawWireCube(targetZone.zoneBounds.center, targetZone.zoneBounds.size);
        }
    }
}
