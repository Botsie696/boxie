using UnityEngine;

/// <summary>
/// Chase camera for the obstacle legs (DynamicPathGeneratorObstacleLeg.cs, 3.9 only; added by the generator).
/// While the runner is on an obstacle leg the camera glides from the live shot to a pose behind the runner that looks up
/// the leg (like the portal exit chase shot) and glides back before the end, so the turn after it is read on the normal
/// shot. Taps and speed are left alone.
///
/// Smooth through rolls: the shot follows the runner's place ALONG the leg (projected onto the leg's centre line), never
/// its position around the beam, so changing faces does not move the camera at all - the same calm the portal exit shot
/// has (there the runner cannot roll).
///
/// Where the runner sits: the rules modes D and E frame by (CameraFeelProfile, read from the rig when it has one) - the
/// HUD reserve at the top, the thumb keep-out zones (two thumbs: bottom band and the outer sides) and the central gaze
/// zone. The runner sits centred left-right, just above the bottom thumb band; the leg runs up the screen from it.
///
/// Size by time: lookAheadSeconds of track at the runner's speed must fit from the runner to under the HUD, and no more,
/// so nothing is smaller than it needs to be; never tighter than the authored size, never much wider than the live shot.
///
/// Through CameraParentRotator.CameraOverride: every frame it reads the pose the rig's mode wrote and blends it with the
/// chase pose by how far along the leg the runner is. Stands aside while the portal cinematic owns the camera.
/// </summary>
public class ObstacleLegCamera : MonoBehaviour
{
    public DynamicPathGenerator pathGen;

    [Header("Angle")]
    [Tooltip("Camera pitch behind the runner. Steeper than the portal chase shot so the leg runs up the portrait screen.")]
    [Range(24f, 60f)] public float chaseTilt = 40f;
    [Tooltip("Degrees the view is turned off the leg's heading: small = the leg runs straight up the screen (centred between the thumbs); some is kept so the side faces still read.")]
    [Range(0f, 45f)] public float chaseYawOffset = 14f;

    [Header("Runner on screen (modes D/E rules)")]
    [Tooltip("Margin kept above the bottom thumb band, as a share of the screen height.")]
    [Range(0f, 0.2f)] public float aboveThumbMargin = 0.05f;
    [Tooltip("Used when the rig has no CameraFeelProfile: thumb band at the bottom (share of screen height), HUD reserve at the top, gaze zone.")]
    [Range(0f, 0.5f)] public float fallbackThumbZoneHeight = 0.24f;
    [Range(0f, 0.3f)] public float fallbackHudTopReserve = 0.12f;
    [Range(0.2f, 1f)] public float fallbackGazeZone = 0.60f;

    [Header("Size by time")]
    [Tooltip("Seconds of track ahead of the runner the shot holds at its speed: a choice reaction (~0.5 s) + a double roll + margin, so the next pillar pairs are always in view.")]
    [Range(1f, 5f)] public float lookAheadSeconds = 2.2f;
    [Tooltip("Never tighter than the Camera's authored size times this (1 = the authored size is the floor).")]
    [Range(0.5f, 1.5f)] public float minSizeOfAuthored = 1f;
    [Tooltip("Never wider than the live shot times this.")]
    [Range(0.8f, 2f)] public float maxSizeOfLive = 1.1f;
    [Tooltip("Seconds the chase anchor eases after the runner (along the leg only; 0 = locked to it).")]
    [Range(0f, 0.5f)] public float anchorSmooth = 0.08f;
    [Tooltip("Print the solved framing when an obstacle leg starts.")]
    public bool logSize = false;

    CameraParentRotator rig;
    CubeRailRunner runner;
    LookTransitionDirector director;
    DynamicPathGenerator.Segment seg;
    float chaseYaw, chaseOrtho, runnerX, runnerY, anchorAlong, anchorVel;

    /// <summary>0 = the live shot, 1 = fully behind the runner.</summary>
    public float Weight { get; private set; }

    void Start()
    {
        if (pathGen == null) pathGen = GetComponent<DynamicPathGenerator>();
        rig = FindFirstObjectByType<CameraParentRotator>();
        runner = FindFirstObjectByType<CubeRailRunner>();
        director = FindFirstObjectByType<LookTransitionDirector>();
        if (rig != null) rig.CameraOverride += OnCameraOverride;
    }

    void OnDestroy()
    {
        if (rig != null) rig.CameraOverride -= OnCameraOverride;
    }

    void OnCameraOverride(CameraParentRotator r)
    {
        Weight = 0f;
        if (pathGen == null || runner == null || r.boomCamera == null) return;
        if (director != null && director.CameraCinematicActive) return;
        if (runner.IsGameOver || runner.IsRebuilding || !pathGen.PlayerOnObstacleLeg) { seg = null; return; }

        DynamicPathGenerator.Segment s = pathGen.PlayerSegment;
        float along = Vector3.Dot(runner.transform.position - s.startPosition, s.direction);
        if (s != seg) { seg = s; anchorAlong = along; anchorVel = 0f; PickChase(r, s); }
        anchorAlong = anchorSmooth > 0f ? Mathf.SmoothDamp(anchorAlong, along, ref anchorVel, anchorSmooth) : along;

        float d = along / Mathf.Max(0.01f, pathGen.cubeSize);
        float wIn = EaseOut(d / Mathf.Max(0.5f, s.obstacleGlideInCubes));
        float wOut = 1f - Smoother((d - s.obstacleGlideOutFrom) / Mathf.Max(0.5f, s.obstacleGlideOutCubes));
        float w = Mathf.Min(wIn, wOut);
        if (w <= 0.0001f) return;
        Weight = w;
        WritePose(r, s, w);
    }

    void PickChase(CameraParentRotator r, DynamicPathGenerator.Segment s)
    {
        float liveYaw = r.transform.eulerAngles.y;
        Vector3 dir = s.direction; dir.y = 0f;
        float heading = dir.sqrMagnitude > 1e-4f ? Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg : liveYaw;
        float y1 = heading + chaseYawOffset, y2 = heading - chaseYawOffset;
        bool plus = Mathf.Abs(Mathf.DeltaAngle(liveYaw, y1)) <= Mathf.Abs(Mathf.DeltaAngle(liveYaw, y2));
        chaseYaw = plus ? y1 : y2;

        // The D/E framing rules (half-height units: -1 bottom .. +1 top; half-width units for x).
        CameraFeelProfile P = r.FeelProfile;
        float thumbH = P != null ? P.thumbZoneHeight : fallbackThumbZoneHeight;
        float hud = P != null ? P.hudTopReserve : fallbackHudTopReserve;
        float gaze = P != null ? P.gazeZone : fallbackGazeZone;
        float thumbTop = -1f + 2f * (thumbH + aboveThumbMargin);   // just above the bottom thumb band
        runnerY = Mathf.Max(thumbTop, -gaze);                       // ... and never below the gaze zone
        float top = 1f - 2f * hud;                                   // under the HUD

        // The leg leans sideways by the yaw offset: start the runner a little to the other side of centre so the run
        // ahead is balanced about the middle, within the gaze zone (both thumbs keep the sides).
        Camera cam = r.boomCamera;
        float v = Mathf.Max(1f, runner.CurrentMoveSpeed);
        float dist = v * lookAheadSeconds;
        float climb = Mathf.Cos(chaseYawOffset * Mathf.Deg2Rad) * Mathf.Sin(chaseTilt * Mathf.Deg2Rad);
        float side = Mathf.Sin(chaseYawOffset * Mathf.Deg2Rad);
        float byHeight = dist * climb / Mathf.Max(0.1f, top - runnerY);
        float bySide = dist * side / Mathf.Max(0.1f, cam.aspect * gaze * 2f);
        float live = cam.orthographicSize;
        float lo = r.AuthoredOrthoSize * minSizeOfAuthored, hi = Mathf.Max(lo, live * maxSizeOfLive);
        chaseOrtho = Mathf.Clamp(Mathf.Max(byHeight, bySide), lo, hi);

        // Sideways shift of the run ahead on screen (half-widths), split about the centre, clamped to the gaze zone.
        float spanX = dist * side / Mathf.Max(0.01f, chaseOrtho * cam.aspect);
        float sign = SideSign(r, s, plus);
        runnerX = Mathf.Clamp(-0.5f * spanX * sign, -gaze, gaze);

        if (logSize)
            Debug.Log($"[ObstacleLegCamera] {v:0.0} u/s, {dist:0} units ahead -> size {chaseOrtho:0.0} (height {byHeight:0.0}, side {bySide:0.0}, live {live:0.0}, authored {r.AuthoredOrthoSize:0.0}); runner at x {runnerX:0.00} y {runnerY:0.00}");
    }

    // +1 when the leg ahead drifts to the screen's right in the chase view, -1 to the left.
    float SideSign(CameraParentRotator r, DynamicPathGenerator.Segment s, bool plus)
    {
        Vector3 right = Quaternion.Euler(0f, chaseYaw, 0f) * Vector3.right;
        return Vector3.Dot(s.direction, right) >= 0f ? 1f : -1f;
    }

    // The live pose blended with the chase pose by w (the runner's screen place blends, so it never leaves the frame).
    void WritePose(CameraParentRotator r, DynamicPathGenerator.Segment s, float w)
    {
        Transform rt = r.transform;
        Camera cam = r.boomCamera;
        Transform ct = cam.transform;
        float liveYaw = rt.eulerAngles.y;
        float liveTilt = ct.localEulerAngles.x;
        if (liveTilt > 180f) liveTilt -= 360f;
        float liveOrtho = cam.orthographicSize;
        Vector3 liveFocus = r.ViewFocus;
        float boom = Vector3.Distance(liveFocus, rt.position);
        if (boom < 1f) boom = r.boomDistance;

        float yaw = liveYaw + Mathf.DeltaAngle(liveYaw, chaseYaw) * w;
        float tilt = Mathf.Lerp(liveTilt, chaseTilt, w);
        float ortho = Mathf.Lerp(liveOrtho, chaseOrtho, w);
        Quaternion q = Quaternion.Euler(tilt, yaw, 0f);
        Vector3 fwd = q * Vector3.forward, right = q * Vector3.right, up = q * Vector3.up;

        // The anchor: the runner's place along the leg's centre line - rolling round the beam never moves it.
        Vector3 anchor = s.startPosition + s.direction * anchorAlong;
        Quaternion qLive = Quaternion.Euler(liveTilt, liveYaw, 0f);
        Vector3 dl = anchor - liveFocus;
        float liveX = Vector3.Dot(dl, qLive * Vector3.right) / Mathf.Max(0.01f, liveOrtho * cam.aspect);
        float liveY = Vector3.Dot(dl, qLive * Vector3.up) / Mathf.Max(0.01f, liveOrtho);
        float liveDepth = Vector3.Dot(-dl, qLive * Vector3.forward);
        float sx = Mathf.Lerp(liveX, runnerX, w);
        float sy = Mathf.Lerp(liveY, runnerY, w);
        float depth = Mathf.Lerp(liveDepth, 0f, w);
        Vector3 focus = anchor - right * (sx * ortho * cam.aspect) - up * (sy * ortho) + fwd * depth;

        rt.SetPositionAndRotation(focus - fwd * boom, Quaternion.Euler(0f, yaw, 0f));
        ct.localRotation = Quaternion.Euler(tilt, 0f, 0f);
        ct.localPosition = Vector3.Lerp(ct.localPosition, Vector3.zero, w);
        cam.orthographicSize = ortho;
    }

    static float Smoother(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
    static float EaseOut(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x; }
}
