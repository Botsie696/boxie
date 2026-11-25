using UnityEngine;

/// <summary>
/// Tuning for CameraParentRotator's A (zone glide), B (follow), D (rail) and E (zone drift) modes. Mode C (original) ignores it.
/// "s" = clamp01((runnerSpeed - 11.9) / (32 - 11.9)) from the runner's current forwardSpeed, boost included.
/// Every (curve) field is an AnimationCurve over s (0 = opening speed, 1 = 32 u/s).
/// </summary>
[CreateAssetMenu(menuName = "Runner/Camera Feel Profile", fileName = "CameraFeel")]
public class CameraFeelProfile : ScriptableObject
{
    [Header("Speed scale")]
    [Tooltip("Speed at s = 0.")] public float speedLow = 11.9f;
    [Tooltip("Speed at s = 1.")] public float speedHigh = 32f;

    [Header("Framing (fractions of the half-screen)")]
    [Tooltip("The runner must stay inside this horizontal range (+-). P0, hard.")] public float runnerSafeX = 0.45f;
    [Tooltip("Lowest the runner may be (negative = below centre).")] public float runnerSafeYMin = -0.60f;
    [Tooltip("Highest the runner may be.")] public float runnerSafeYMax = 0.30f;
    [Tooltip("Share of the FULL screen height reserved at the top for the HUD; gameplay stays below it.")] [Range(0f, 0.3f)] public float hudTopReserve = 0.12f;
    [Tooltip("Comfort box: the must-see set should stay inside this share of the half-screen.")] [Range(0.4f, 0.98f)] public float softFrameLimit = 0.80f;
    [Tooltip("Never-leave box for P0/P1; the emergency net acts when they would cross it.")] [Range(0.5f, 0.99f)] public float hardFrameLimit = 0.92f;
    [Tooltip("World units added around must-see points (half a cube plus air).")] public float mustSeePadding = 0.75f;

    [Header("Zoom")]
    [Tooltip("Orthographic size is never tighter than this.")] public float zoomFloor = 23.1f;
    [Tooltip("Widest the fit zoom may go, by speed (curve).")] public AnimationCurve zoomCap = AnimationCurve.Linear(0f, 28f, 1f, 32f);
    [Tooltip("Hard ceiling on the final ortho size, boost included.")] public float absoluteZoomCap = 34f;
    [Tooltip("Zoom multiplier while on a zig-zag boost (SmoothStep).")] public float boostZoomMultiplier = 1.10f;
    public float boostZoomInSeconds = 0.7f;
    public float boostZoomOutSeconds = 0.7f;

    [Header("Rotation")]
    [Tooltip("Seconds per 90 degrees of yaw (curve); a 90 degree turn takes rotationMinSeconds + this.")] public AnimationCurve secondsPer90 = AnimationCurve.Linear(0f, 1.0f, 1f, 0.9f);
    public float rotationMinSeconds = 0.6f;
    [Tooltip("A yaw change is postponed if a boost or bridge leg is within this many legs ahead.")] public int blockRotationIfBoostOrBridgeWithinLegs = 2;

    [Header("Misc")]
    [Tooltip("Seconds the camera boom length takes to settle (never steps).")] public float boomSmoothTime = 0.5f;
    [Tooltip("Reserved: degrees of in-place lean. Not used by A/B in this version.")] public float leanDegrees = 4f;
    public float leanSmoothTime = 0.9f;
    public bool debugGizmos = true;
    [Tooltip("Log every glide decision (mode A) to the console.")] public bool logDecisions = false;
    [Tooltip("Emergency correction smooth time (both modes).")] public float emergencySmoothTime = 0.12f;
    [Tooltip("The emergency net looks this far ahead (seconds).")] public float emergencyLookAhead = 0.25f;

    [Header("Mode A - zone glide")]
    [Tooltip("Seconds ahead the runner is predicted when deciding to glide (curve).")] public AnimationCurve lookPredict = AnimationCurve.Linear(0f, 0.6f, 1f, 0.8f);
    [Tooltip("Least time a shot is held before the next glide may start (curve). The emergency net never waits.")] public AnimationCurve minHoldSeconds = AnimationCurve.Linear(0f, 1.0f, 1f, 0.8f);
    [Tooltip("A shot should be holdable at least this long; otherwise zoom out a little.")] public float targetHold = 1.6f;
    [Tooltip("How far ahead (seconds) the planner simulates the runner.")] public float maxPlanSeconds = 5f;
    [Tooltip("Shortest glide (curve).")] public AnimationCurve minGlide = AnimationCurve.Linear(0f, 1.2f, 1f, 1.0f);
    [Tooltip("A glide is at least distance / (this x runner speed) long.")] public float maxGlideSpeedFactor = 1.5f;
    [Tooltip("No glide may START within this many seconds after one ends (emergency excepted).")] public float postGlideCalm = 0.25f;
    [Tooltip("Skip a glide if the new focus is closer than this ...")] public float skipGlideDistance = 2.5f;
    [Tooltip("... and the zoom changes by less than this share.")] public float skipGlideZoomPct = 0.04f;
    [Tooltip("0 = a shot must keep the runner inside its safe box from the moment the glide STARTS (safest, shorter holds); 1 = only from the moment the glide ENDS (longer holds, but the runner can leave the safe box while the camera is still travelling).")] [Range(0f, 1f)] public float planFromGlideEndShare = 0f;
    [Tooltip("The runner starts in the trailing part of the frame, at most this far (half-screens) from centre.")] public float trailingOffsetMax = 0.35f;

    [Header("Mode B - follow")]
    [Tooltip("Seconds of route the lead looks ahead (curve).")] public AnimationCurve lookAheadSeconds = AnimationCurve.Linear(0f, 2.0f, 1f, 2.4f);
    [Tooltip("Share of the centroid offset used as lead.")] public float leadWeight = 0.55f;
    [Tooltip("Lead is clamped to this many half-screens per axis.")] public float leadMax = 0.38f;
    [Tooltip("Lead smoothing (curve).")] public AnimationCurve leadSmoothTime = AnimationCurve.Linear(0f, 0.75f, 1f, 0.55f);
    public float followSmoothTime = 0.10f;
    [Tooltip("Focus speed cap = runnerSpeed * this + followSpeedPlus.")] public float followSpeedFactor = 1.35f;
    public float followSpeedPlus = 4f;
    public float followMaxAccel = 90f;
    [Tooltip("Zoom hysteresis: ignore fit changes smaller than this share.")] public float zoomHysteresis = 0.06f;
    public float zoomWidenPerSecond = 3.5f;
    public float zoomTightenPerSecond = 1.5f;
    [Tooltip("Seconds after the last widening before tightening may start.")] public float zoomTightenDelay = 1.2f;
    public float zoomSmoothTime = 0.6f;
    [Tooltip("Safety slide smooth time at the soft / hard limit.")] public float slideSoftSmooth = 0.35f;
    public float slideHardSmooth = 0.08f;
    [Tooltip("B softFrameLimit override (spec: 0.80).")] [Range(0.4f, 0.98f)] public float followSoftLimit = 0.80f;
    [Tooltip("B hardFrameLimit override (spec: 0.92).")] [Range(0.5f, 0.99f)] public float followHardLimit = 0.92f;

    // ================= modes D (rail) and E (zone drift) =================
    [Header("Rail (modes D and E)")]
    [Tooltip("Seconds the runner speed used by the rail is smoothed over (SmoothDamp). Boost included.")] public float railSpeedSmooth = 0.5f;
    [Tooltip("Gaussian width of the rail smoothing, in seconds of travel, by speed (curve). Wider = rounder corners, calmer camera.")] public AnimationCurve railSigmaSeconds = AnimationCurve.Linear(0f, 1.15f, 1f, 0.65f);
    [Tooltip("Mode D: seconds of route ahead of the runner the rail point sits.")] public float railLeadD = 0.35f;
    [Tooltip("A rail target jump larger than this (world units, one frame) is a restart / teleport: blend instead of following.")] public float railTeleportDistance = 5f;
    [Tooltip("Seconds of the blend after a restart or teleport.")] public float railRestartBlend = 0.3f;
    [Tooltip("Lead room: the runner sits this many half-screens behind centre, against the direction of the upcoming route.")] [Range(0f, 0.5f)] public float leadRoom = 0.25f;
    [Tooltip("Seconds of upcoming route averaged for the lead-room direction.")] public float leadRoomLookSeconds = 1.0f;
    [Tooltip("Seconds the lead-room offset is smoothed over.")] public float leadRoomSmooth = 0.6f;
    [Tooltip("Mode D: the total lead (rail lead + lead room) saturates softly at this many half-screens, so the runner stays near centre.")] [Range(0.1f, 0.9f)] public float railLeadMaxD = 0.30f;
    [Tooltip("Mode E: the same saturation for E's lead room.")] [Range(0.1f, 0.9f)] public float railLeadMaxE = 0.55f;
    [Tooltip("Ambient lean toward the next shot's yaw, degrees (never more than 3).")] [Range(0f, 3f)] public float railLeanDegrees = 3f;
    [Tooltip("Lean smooth time, seconds.")] public float railLeanSmooth = 0.9f;
    [Tooltip("Fastest any ambient (unplanned) yaw motion may be, degrees per second.")] public float ambientYawRateMax = 2f;

    [Header("Motion (modes D and E)")]
    [Tooltip("Mode E focus spring stiffness (critically damped), rad/s.")] public float springOmega = 2.6f;
    [Tooltip("Mode E focus spring: largest acceleration at the opening speed, world units / s^2.")] public float springMaxAccel = 20f;
    [Tooltip("The spring acceleration cap grows with (speed / speedLow)^2 up to this multiple, since corners at speed need v^2 more acceleration to follow.")] public float springAccelSpeedScaleMax = 3f;
    [Tooltip("Motion budget: most screen flow allowed, % of screen height per second (position + rotation + zoom). Over it, zoom slows first, then rotation; position is never slowed below runner safety.")] public float flowMax = 45f;
    [Tooltip("Least time a planned 90 degree rotation takes, seconds (SmoothStep).")] public float rotSecondsPer90 = 2.1f;
    [Tooltip("Least time a planned 180 degree rotation takes, seconds.")] public float rotSecondsPer180 = 3.0f;
    [Tooltip("Peak yaw rate of a planned rotation, degrees per second (the duration is lengthened to respect it).")] public float rotPeakDegPerSec = 70f;
    [Tooltip("Rotation is never stretched below this share of its planned speed by the motion budget, so it always finishes.")] [Range(0.1f, 1f)] public float rotMinBudgetShare = 0.35f;
    [Tooltip("Safety net box (half-screens): the runner / next corner must never be predicted past it.")] [Range(0.5f, 0.99f)] public float railHardLimit = 0.92f;
    [Tooltip("Safety net look-ahead, seconds.")] public float railNetLookAhead = 0.25f;
    [Tooltip("Safety net correction smooth time, seconds.")] public float railNetSmooth = 0.12f;
    [Tooltip("Seconds the safety-net offset takes to fade back once it is no longer needed.")] public float railNetRelease = 1.2f;
    [Tooltip("Mode D zoom floor (ortho size).")] public float railZoomFloorD = 23.1f;
    [Tooltip("Mode E zoom floor (ortho size).")] public float railZoomFloorE = 25.0f;
    [Tooltip("Zoom fit ignores changes smaller than this share.")] public float railZoomHysteresis = 0.06f;
    [Tooltip("Fastest the zoom widens, ortho units per second.")] public float railZoomWidenPerSecond = 2.5f;
    [Tooltip("Fastest the zoom tightens, ortho units per second.")] public float railZoomTightenPerSecond = 1.0f;
    [Tooltip("Seconds after the last widening before the zoom may tighten.")] public float railZoomTightenDelay = 1.5f;
    [Tooltip("Zoom SmoothDamp time, seconds.")] public float railZoomSmooth = 1.0f;
    [Tooltip("Thumb keep-out zones: share of the screen height at the bottom (soft).")] [Range(0f, 0.5f)] public float thumbZoneHeight = 0.24f;
    [Tooltip("Thumb keep-out zones: share of the screen width on EACH outer side (soft).")] [Range(0f, 0.5f)] public float thumbZoneWidth = 0.38f;
    [Tooltip("Gaze zone: the central share of width and height the runner and the next corner should stay in (recorder target 95% of the time).")] [Range(0.2f, 1f)] public float gazeZone = 0.60f;
    [Tooltip("Every limit (HUD reserve, thumb zones, safe box) is measured inside Screen.safeArea.")] public bool useSafeArea = true;

    [Header("ZoneDrift (mode E)")]
    [Tooltip("Legs per shot (target).")] public int zoneLegs = 3;
    [Tooltip("A shot may grow to this many legs so that it lasts at least zoneMinSeconds.")] public int zoneMaxLegs = 5;
    [Tooltip("Shortest a shot should last at the current speed, seconds (shot changes never come faster).")] public float zoneMinSeconds = 3.5f;
    [Tooltip("Seconds of route ahead of the runner the rail point sits (mode E).")] [Range(0.2f, 0.7f)] public float railLeadE = 0.45f;
    [Tooltip("Share of the way from the shot's anchor to the rail point the focus sits (0 = locked shot, 1 = pure rail).")] [Range(0f, 0.8f)] public float followWeight = 0.8f;
    [Tooltip("The move to the next shot starts when the runner has covered this share of the current shot.")] [Range(0.1f, 0.9f)] public float transitionStartFrac = 0.40f;
    [Tooltip("Duration of the move to the next shot by speed (curve); a yaw change makes it at least the rotation time.")] public AnimationCurve transitionDur = AnimationCurve.Linear(0f, 2.4f, 1f, 1.8f);
    [Tooltip("Shot score: per corner inside the soft frame.")] public float scoreCornerInFrame = 3.0f;
    [Tooltip("Shot score: per corner whose turn face faces the camera.")] public float scoreTurnFaceVisible = 2.0f;
    [Tooltip("Shot score: per corner / runner sample hidden behind a nearer cube (negative).")] public float scoreOccluded = -2.5f;
    [Tooltip("Shot score: per cube running toward the camera (negative).")] public float scoreApproachCube = -0.5f;
    [Tooltip("Shot score: bonus for a yaw that differs from the last two shots' (only where rotation is allowed).")] public float scoreVariety = 0.8f;
    [Tooltip("Shot score: cost per 90 degrees of yaw change.")] public float scoreAngleChangeCost = 11f;
    [Tooltip("Shot score: cost per world unit of pan from the previous shot's focus.")] public float scorePanPerUnit = 0.08f;
    [Tooltip("Shot score: per corner / runner sample inside a thumb zone (negative).")] public float scoreThumb = -2.0f;
    [Tooltip("Shot score: per upcoming leg running mostly vertically on screen.")] public float scorePortraitLeg = 0.5f;

    public float SpeedFraction(float speed) => Mathf.Clamp01((speed - speedLow) / Mathf.Max(0.01f, speedHigh - speedLow));
}
