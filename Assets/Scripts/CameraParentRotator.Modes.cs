using UnityEngine;

/// <summary>
/// Shared plumbing for the camera modes A (zone glide) and B (follow): the mode switch (pop-free, the new
/// mode starts from the pose on screen), the pose writer, boost zoom, the emergency net and small helpers.
/// Mode C (the original rig in CameraParentRotator.cs) never runs any of this: ModeTick() returns false.
/// </summary>
public partial class CameraParentRotator
{
    // ---- mode state shared by A and B ----
    CameraMode activeMode = CameraMode.C_Original;
    bool modeInit;
    bool warnedNoProfile;

    // The pose, as the modes hold it: a focus point, yaw, base ortho (before boost) and boom.
    Vector3 mFocus;
    float mYaw, mOrtho, mBoom, mBoomVel;
    Vector3 emOff, emVel;                       // emergency offset (world, screen plane)
    float mBoostT;                              // 0..1 progress of the boost zoom
    float mBoostMul = 1f;
    float mRenderedOrtho;
    bool emergencyActive;
    int emergencyCount, glideCount;
    float modeSpeed, modeS;                     // runner speed and its s
    Vector3 modeRunner;
    Vector3 mRight, mUp, mFwd;
    readonly float[] scrX = new float[DynamicPathGenerator.RouteMax];
    readonly float[] scrY = new float[DynamicPathGenerator.RouteMax];
    readonly int[] endIdx = new int[DynamicPathGenerator.RouteMax];
    readonly float[] pX0 = new float[DynamicPathGenerator.RouteMax], pX1 = new float[DynamicPathGenerator.RouteMax], pY0 = new float[DynamicPathGenerator.RouteMax], pY1 = new float[DynamicPathGenerator.RouteMax];

    // ---- exposed for the recorder / tooling ----
    public CameraMode Mode { get => cameraMode; set => cameraMode = value; }
    public CameraMode ActiveMode => activeMode;
    public bool ModeEmergencyActive => emergencyActive;
    public int ModeEmergencyCount => emergencyCount;
    public int ModeGlideCount => glideCount;
    public Vector3 ModePoseFocus => mFocus + emOff + SlideWorld();
    public CameraFeelProfile FeelProfile => feelProfile;
    /// <summary>The point the camera looks at (boom distance along the view axis), in every mode: moves with pans, not with the orbit of a yaw turn.</summary>
    public Vector3 ViewFocus => activeMode != CameraMode.C_Original && modeInit && feelProfile != null
        ? ModePoseFocus
        : transform.position + Quaternion.Euler(isometricTiltX, transform.eulerAngles.y, 0f) * Vector3.forward * baseBoom;
    /// <summary>"C", "HOLD", "GLIDE" (A) or "FOLLOW" (B), with "+EMERGENCY" while the net acts.</summary>
    public string ModeStateName => activeMode == CameraMode.C_Original ? "C" : (ModeSub() + (emergencyActive ? "+EMERGENCY" : ""));

    void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.C)) cameraMode = CameraMode.C_Original;
        else if (Input.GetKeyDown(KeyCode.Z)) cameraMode = CameraMode.A_ZoneGlide;
        else if (Input.GetKeyDown(KeyCode.X)) cameraMode = CameraMode.B_Follow;
        else if (Input.GetKeyDown(KeyCode.V)) cameraMode = CameraMode.D_Rail;
        else if (Input.GetKeyDown(KeyCode.B)) cameraMode = CameraMode.E_ZoneDrift;
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    GUIStyle modeLabelStyle;
    /// <summary>3.6 HUD: no dev mode label over the game.</summary>
    [System.NonSerialized] public bool hideModeLabel;
    void OnGUI()
    {
        if (hideModeLabel) return;
        if (modeLabelStyle == null)
        {
            modeLabelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(Screen.height * 0.018f) };
            modeLabelStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
        }
        string name = activeMode == CameraMode.A_ZoneGlide ? "A - ZONE GLIDE" : activeMode == CameraMode.B_Follow ? "B - FOLLOW"
                    : activeMode == CameraMode.D_Rail ? "D - RAIL" : activeMode == CameraMode.E_ZoneDrift ? "E - ZONE DRIFT" : "C - ORIGINAL";
        bool live = activeMode == CameraMode.C_Original || (feelProfile != null && modeInit);
        GUI.Label(new Rect(0f, Screen.height * 0.045f, Screen.width, Screen.height * 0.035f), "CAMERA " + name + (live ? "  (active)" : "  (no profile - running C)") + "   [Z A  X B  C C  V D  B E]", modeLabelStyle);
    }
#endif

    // ------------------------------------------------------------------
    // Entry from LateUpdate. false = mode C, run the original code.
    // ------------------------------------------------------------------
    bool ModeTick()
    {
        if (cameraMode != activeMode) SwitchMode(cameraMode);
        if (activeMode == CameraMode.C_Original) return false;
        if (feelProfile == null)
        {
            if (!warnedNoProfile) { warnedNoProfile = true; Debug.LogWarning("CameraParentRotator: mode " + activeMode + " needs a Feel Profile; running the original camera.", this); }
            return false;
        }
        if (!modeInit) AdoptFromCamera();

        float dt = Time.deltaTime;
        CameraFeelProfile P = feelProfile;
        modeRunner = player.transform.position;
        modeSpeed = player.forwardSpeed;
        modeS = P.SpeedFraction(modeSpeed);

        LockPlayerZone();
        UpdateModeBoost(dt);
        Basis(mYaw, out mRight, out mUp, out mFwd);
        pathGen.BuildRoute(modeRunner);

        if (activeMode == CameraMode.A_ZoneGlide) TickZoneGlide(dt, P);
        else if (activeMode == CameraMode.B_Follow) TickFollow(dt, P);
        else
        {
            TickRailModes(dt, P);      // D / E: own safety net, motion budget and zoom (CameraParentRotator.Rail.cs / .Motion.cs)
            WriteModePose(dt, P);
            return true;
        }

        EmergencyNet(dt, P);
        WriteModePose(dt, P);
        return true;
    }

    bool ModeSettledYaw(out float yaw)
    {
        yaw = 0f;
        if (activeMode == CameraMode.C_Original || !modeInit || feelProfile == null) return false;
        yaw = ModeTargetYaw();
        return true;
    }

    void ModeAdoptCurrentPose()
    {
        if (activeMode != CameraMode.C_Original) AdoptFromCamera();
    }

    // ------------------------------------------------------------------
    // Switching
    // ------------------------------------------------------------------
    void SwitchMode(CameraMode to)
    {
        if (boomCamera == null || player == null || pathGen == null) { activeMode = to; return; }
        CameraMode from = activeMode;
        if (from == CameraMode.C_Original && to != CameraMode.C_Original)
        {
            activeMode = to;
            if (feelProfile != null) AdoptFromCamera();
        }
        else if (to == CameraMode.C_Original)
        {
            if (modeInit) RestoreOriginalFromPose();
            activeMode = to;
            modeInit = false;
        }
        else
        {
            // between A / B / D / E the pose carries over as it is
            Vector3 pose = mFocus + emOff + SlideWorld();
            mYaw += mLean; mLean = 0f;
            activeMode = to;
            if (to == CameraMode.D_Rail || to == CameraMode.E_ZoneDrift) { mFocus = pose; emOff = emVel = Vector3.zero; emergencyActive = false; }
            ResetSubStates();
        }
    }

    /// <summary>Takes the pose that is on screen right now as the starting state of A / B.</summary>
    void AdoptFromCamera()
    {
        float yaw = transform.eulerAngles.y;
        Basis(yaw, out Vector3 r, out Vector3 u, out Vector3 f);
        mYaw = yaw;
        mLean = 0f;
        mBoom = Mathf.Max(1f, baseBoom);
        mBoomVel = 0f;
        mFocus = transform.position + f * mBoom;
        mRenderedOrtho = boomCamera.orthographicSize;
        bool boosting = pathGen != null && pathGen.PlayerOnZigZagBoost;
        mBoostT = boosting ? 1f : 0f;
        mBoostMul = feelProfile != null ? 1f + (feelProfile.boostZoomMultiplier - 1f) * mBoostT : 1f;
        float floor = feelProfile != null ? feelProfile.zoomFloor : MinFrameSize();
        mOrtho = Mathf.Max(floor, mRenderedOrtho / Mathf.Max(0.01f, mBoostMul));
        emOff = emVel = Vector3.zero;
        emergencyActive = false;
        modeInit = true;
        ResetSubStates();
    }

    /// <summary>Hands the pose back to the original rig so the first C frame lands where the last A / B frame was.</summary>
    void RestoreOriginalFromPose()
    {
        Vector3 pose = mFocus + emOff + SlideWorld();
        float f = Mathf.Clamp(followPlayer, 0f, 0.95f);
        currentZone = pathGen.PlayerZone;
        targetZone = null;
        state = CameraState.Static;
        holdTimer = 0f;

        Vector3 runner = player.transform.position;
        Vector3 bf = (pose - runner * f) / (1f - f);          // pose = bf + f * (runner - bf)
        baseFocus = bf;
        baseYaw = mYaw + mLean;     // mLean is 0 outside D / E
        baseBoom = mBoom;
        Basis(baseYaw, out Vector3 r, out Vector3 u, out Vector3 fw);
        mLean = 0f;
        Vector3 d = (pose - bf);
        followOffset = new Vector2(Vector3.Dot(d, r), Vector3.Dot(d, u));
        followVelocity = Vector2.zero;
        frameShift = frameShiftVelocity = Vector2.zero;
        extraOrtho = extraOrthoVelocity = 0f;
        lean = leanVelocity = 0f;
        extentsValid = false;
        boostZoom = Mathf.Max(1f, mBoostMul);
        float norm = Mathf.Max(0.01f, zoomOutMultiplier * boostZoom * Mathf.Max(1f, speedZoom));
        zoomShown = zoomPeak = Mathf.Max(MinFrameSize(), mRenderedOrtho / norm);
        zoomVelocity = 0f;
        baseOrtho = zoomShown;
        if (currentZone != null && currentZone.framingSolved) currentZone.LockFraming();
    }

    // ------------------------------------------------------------------
    // Shared helpers
    // ------------------------------------------------------------------
    void Basis(float yaw, out Vector3 right, out Vector3 up, out Vector3 fwd)
    {
        Quaternion q = Quaternion.Euler(isometricTiltX, yaw, 0f);
        right = q * Vector3.right; up = q * Vector3.up; fwd = q * Vector3.forward;
    }

    // Keeps PlanCameraYaws anchored: the zone the runner is in is committed, as the original rig does when it adopts one.
    void LockPlayerZone()
    {
        DynamicPathGenerator.PathZone pz = pathGen.PlayerZone;
        if (pz == null || pz.framingLocked) return;
        SolveZone(pz);
        if (pz.framingSolved) pz.LockFraming();
    }

    // The yaw the runner's route wants: its zone's, or the next zone's once the runner is on the last leg.
    float DesiredYaw(out DynamicPathGenerator.PathZone zoneUsed)
    {
        DynamicPathGenerator.PathZone z = pathGen.PlayerZone;
        zoneUsed = z;
        if (z == null) return mYaw;
        if (pathGen.IsPlayerNearZoneEnd(z, 0))
        {
            DynamicPathGenerator.PathZone next = pathGen.GetZoneAfter(z);
            if (next != null) { SolveZone(next); if (next.framingSolved) { z = next; zoneUsed = next; } }
        }
        SolveZone(z);
        return z.framingSolved ? z.optimalCameraYaw : mYaw;
    }

    // A yaw change is postponed while a boost or a bridge is close (never for a portal zone, which forces its yaw).
    bool YawChangeBlocked(DynamicPathGenerator.PathZone zone, CameraFeelProfile P)
    {
        if (zone != null && zone.forcedYaw >= 0f) return false;
        return pathGen.RouteLegsToSpecial <= P.blockRotationIfBoostOrBridgeWithinLegs;
    }

    void UpdateModeBoost(float dt)
    {
        CameraFeelProfile P = feelProfile;
        bool boosting = pathGen.PlayerOnZigZagBoost;
        float secs = boosting ? P.boostZoomInSeconds : P.boostZoomOutSeconds;
        mBoostT = Mathf.MoveTowards(mBoostT, boosting ? 1f : 0f, dt / Mathf.Max(0.05f, secs));
        mBoostMul = 1f + (P.boostZoomMultiplier - 1f) * Mathf.SmoothStep(0f, 1f, mBoostT);
    }

    float ModeAspect() => CurrentAspect();

    // Top of the usable screen (fraction of the half-height), under the HUD reserve.
    float TopLimit(CameraFeelProfile P) => 1f - 2f * P.hudTopReserve;

    // Route point after `arc` of travel.
    Vector3 RoutePointAt(float arc)
    {
        var g = pathGen;
        int n = g.RouteCount;
        if (n == 0) return modeRunner;
        if (arc <= 0f || n == 1) return g.RoutePts[0];
        arc = Mathf.Min(arc, g.RouteArcs[n - 1]);
        int k = 1;
        while (k < n - 1 && g.RouteArcs[k] < arc) k++;
        float a0 = g.RouteArcs[k - 1], a1 = g.RouteArcs[k];
        return Vector3.Lerp(g.RoutePts[k - 1], g.RoutePts[k], a1 > a0 ? Mathf.Clamp01((arc - a0) / (a1 - a0)) : 0f);
    }

    // For a runner `arc` along the route: index of the first point ahead, of the next corner, and of the end of
    // the first half of the leg after it (P1 = i0..iCorner, P2 = iCorner..iEnd, P3 = iEnd..iLegEnd).
    void RouteSets(float arc, out int i0, out int iCorner, out int iEnd, out int iLegEnd)
    {
        var g = pathGen;
        int n = g.RouteCount;
        i0 = 1; while (i0 < n - 1 && g.RouteArcs[i0] <= arc) i0++;
        int k = 0;
        while (k < g.RouteCornerCount && g.RouteCornerIdx[k] < i0) k++;
        if (k >= g.RouteCornerCount) { iCorner = iEnd = iLegEnd = n - 1; return; }
        iCorner = g.RouteCornerIdx[k];
        int nxt = k + 1 < g.RouteCornerCount ? g.RouteCornerIdx[k + 1] : n;
        iEnd = Mathf.Min(n - 1, iCorner + Mathf.CeilToInt((nxt - iCorner) * 0.5f));
        iLegEnd = Mathf.Min(n - 1, nxt);
    }

    // True if, for a runner `arc` along the route, the runner leaves the safe box or P1 / P2 (P2 optional) leave the soft window.
    bool OutsideWindow(float arc, Vector3 focus, Vector3 r, Vector3 u, float ortho, float soft, bool includeP2, CameraFeelProfile P)
    {
        float hh = ortho, hw = ortho * ModeAspect();
        float top = Mathf.Min(soft, TopLimit(P));
        Vector3 run = RoutePointAt(arc);
        Vector3 d = run - focus;
        float x = Vector3.Dot(d, r), y = Vector3.Dot(d, u);
        if (Mathf.Abs(x) > P.runnerSafeX * hw || y < P.runnerSafeYMin * hh || y > Mathf.Min(P.runnerSafeYMax, TopLimit(P)) * hh) return true;

        RouteSets(arc, out int i0, out int ic, out int ie, out _);
        int last = includeP2 ? ie : ic;
        float pad = P.mustSeePadding;
        for (int i = i0; i <= last; i++)
        {
            d = pathGen.RoutePts[i] - focus;
            x = Vector3.Dot(d, r); y = Vector3.Dot(d, u);
            if (Mathf.Abs(x) + pad > soft * hw || y - pad < -soft * hh || y + pad > top * hh) return true;
        }
        return false;
    }

    // Smallest slide that puts [min, max] inside [lo, hi]; centres it if it cannot fit.
    static float ShiftRange(float min, float max, float lo, float hi)
    {
        if (max - min >= hi - lo) return (min + max) * 0.5f - (lo + hi) * 0.5f;
        if (max > hi) return max - hi;
        if (min < lo) return min - lo;
        return 0f;
    }

    // ------------------------------------------------------------------
    // Emergency net (A and B): P0 / P1 are about to cross the hard box within emergencyLookAhead seconds.
    // ------------------------------------------------------------------
    void EmergencyNet(float dt, CameraFeelProfile P)
    {
        float hard = activeMode == CameraMode.B_Follow ? P.followHardLimit : P.hardFrameLimit;
        float ortho = mOrtho * mBoostMul;
        float hh = ortho, hw = ortho * ModeAspect();
        float top = Mathf.Min(hard, TopLimit(P));
        Vector3 focus = mFocus + emOff + SlideWorld();
        Basis(mYaw, out Vector3 r, out Vector3 u, out _);

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        float pad = P.mustSeePadding;
        float ahead = modeSpeed * P.emergencyLookAhead;
        for (int pass = 0; pass < 2; pass++)
        {
            float arc = pass == 0 ? 0f : ahead;
            Vector3 d = RoutePointAt(arc) - focus;
            float x = Vector3.Dot(d, r), y = Vector3.Dot(d, u);
            minX = Mathf.Min(minX, x - pad); maxX = Mathf.Max(maxX, x + pad);
            minY = Mathf.Min(minY, y - pad); maxY = Mathf.Max(maxY, y + pad);
        }
        RouteSets(ahead, out int i0, out int ic, out _, out _);
        for (int i = i0; i <= ic; i++)
        {
            Vector3 d = pathGen.RoutePts[i] - focus;
            float x = Vector3.Dot(d, r), y = Vector3.Dot(d, u);
            minX = Mathf.Min(minX, x - pad); maxX = Mathf.Max(maxX, x + pad);
            minY = Mathf.Min(minY, y - pad); maxY = Mathf.Max(maxY, y + pad);
        }
        float sx = ShiftRange(minX, maxX, -hard * hw, hard * hw);
        float sy = ShiftRange(minY, maxY, -hard * hh, top * hh);
        // the runner outranks the route: the correction may not push it out of its safe box
        {
            Vector3 dr = RoutePointAt(0f) - focus;
            float rx = Vector3.Dot(dr, r), ry = Vector3.Dot(dr, u);
            float sfx = (P.runnerSafeX - 0.03f) * hw, sfLo = (P.runnerSafeYMin + 0.03f) * hh, sfHi = (Mathf.Min(P.runnerSafeYMax, TopLimit(P)) - 0.03f) * hh;
            sx = Mathf.Clamp(sx, rx - sfx, rx + sfx); sy = Mathf.Clamp(sy, ry - sfHi, ry - sfLo);
        }
        bool need = Mathf.Abs(sx) > 0.01f || Mathf.Abs(sy) > 0.01f;
        if (need && !emergencyActive)
        {
            emergencyCount++;
            if (P.logDecisions) Debug.Log("[CAM EMERGENCY] #" + emergencyCount + " mode=" + activeMode + " sub=" + ModeSub() + " shift=(" + sx.ToString("F1") + "," + sy.ToString("F1") + ") bounds x[" + minX.ToString("F1") + "," + maxX.ToString("F1") + "] y[" + minY.ToString("F1") + "," + maxY.ToString("F1") + "] half=(" + hw.ToString("F1") + "," + hh.ToString("F1") + ") glideT=" + aGlideT.ToString("F2") + "/" + aGlideDur.ToString("F2") + " hold=" + aHold.ToString("F2") + " s=" + modeS.ToString("F2") + " corners=" + pathGen.RouteCornerCount + " cornerIdx0=" + (pathGen.RouteCornerCount > 0 ? pathGen.RouteCornerIdx[0] : -1) + " routeN=" + pathGen.RouteCount);
        }
        emergencyActive = need;
        if (need)
        {
            Vector3 target = emOff + r * sx + u * sy;
            emOff = Vector3.SmoothDamp(emOff, target, ref emVel, Mathf.Max(0.02f, P.emergencySmoothTime), float.MaxValue, dt);
        }
        else emVel = Vector3.zero;
    }

    // ------------------------------------------------------------------
    // Pose writer
    // ------------------------------------------------------------------
    void WriteModePose(float dt, CameraFeelProfile P)
    {
        float yaw = mYaw + mLean;   // mLean: D / E ambient lean, 0 in A / B
        Basis(yaw, out Vector3 r, out Vector3 u, out Vector3 f);
        float boomTarget = Mathf.Max(boomDistance, pathGen.PlayerZone != null && pathGen.PlayerZone.framingSolved ? pathGen.PlayerZone.optimalBoom : boomDistance);
        mBoom = dt > 0f ? Mathf.SmoothDamp(mBoom, boomTarget, ref mBoomVel, Mathf.Max(0.05f, P.boomSmoothTime)) : boomTarget;

        float ortho = Mathf.Min(mOrtho * mBoostMul, P.absoluteZoomCap);
        ortho = Mathf.Max(ortho, Mathf.Min(P.zoomFloor, P.absoluteZoomCap));
        mRenderedOrtho = ortho;

        Vector3 focus = mFocus + emOff + SlideWorld();
        transform.SetPositionAndRotation(focus - f * mBoom, Quaternion.Euler(0f, yaw, 0f));
        boomCamera.transform.localPosition = Vector3.zero;
        boomCamera.transform.localRotation = Quaternion.Euler(isometricTiltX, 0f, 0f);
        boomCamera.orthographicSize = ortho;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!Application.isPlaying || activeMode == CameraMode.C_Original || feelProfile == null || !feelProfile.debugGizmos || !modeInit || pathGen == null) return;
        Gizmos.color = Color.green; Gizmos.DrawSphere(RoutePointAtSafe(0f), 0.8f);
        pathGen.BuildRoute(player.transform.position);
        RouteSets(0f, out int i0, out int ic, out int ie, out int il);
        for (int i = i0; i <= il && i < pathGen.RouteCount; i++)
        {
            Gizmos.color = i <= ic ? Color.cyan : (i <= ie ? Color.yellow : new Color(1f, 0.5f, 0f));
            Gizmos.DrawWireSphere(pathGen.RoutePts[i] + Vector3.up * 0.8f, 0.35f);
        }
        Gizmos.color = Color.magenta; Gizmos.DrawWireSphere(ModePoseFocus, 1.2f);
    }
    Vector3 RoutePointAtSafe(float arc) => pathGen.RouteCount > 0 ? RoutePointAt(arc) : player.transform.position;
#endif
}
