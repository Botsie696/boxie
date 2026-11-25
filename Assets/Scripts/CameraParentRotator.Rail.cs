using UnityEngine;

/// <summary>
/// The shared RAIL for modes D and E, and MODE D_RAIL itself.
///
/// Rail: the runner is projected on the route's centre line (cube centres, DynamicPathGenerator.CameraRoute.cs),
/// giving an s that only moves forward. RailPoint(leadT) is a Gaussian-weighted mean of the centre line around
/// s + leadT * speed, so corners are rounded over sigma = sigmaT * speed of travel. The camera never reads the
/// runner's transform velocity or its roll arc round a corner for framing - those flip 90 degrees at every turn.
///
/// D: focus = RailPoint(railLeadD) + lead room, directly (no extra smoothing): a continuous glide along the
/// smoothed track that never stops. Yaw: C's plan decides when / what; rotations pivot round the focus and obey
/// the Motion rules. Only a restart / teleport (rail jump > railTeleportDistance) blends, via the spring.
/// </summary>
public partial class CameraParentRotator
{
    // ---- rail state ----
    float rlSpeed, rlSpeedVel, rlS, rlS01, rlLeadDist, rlLeadDistVel;
    int rlEpoch = -1;
    bool rlInit;
    Vector3 rlLeadRoom, rlLeadRoomVel;
    Vector3 rlPrevTarget; bool rlHavePrevTarget;
    bool rlBlending; float rlBlendT;

    static readonly float[] RailW = BuildRailWeights();
    static float[] BuildRailWeights()
    {
        var w = new float[33];
        float sum = 0f;
        for (int k = -16; k <= 16; k++) { w[k + 16] = Mathf.Exp(-0.5f * (k / 8f) * (k / 8f)); sum += w[k + 16]; }
        for (int i = 0; i < 33; i++) w[i] /= sum;
        return w;
    }

    void ResetRailState()
    {
        rlInit = false; rlHavePrevTarget = false;
        rlLeadRoom = rlLeadRoomVel = Vector3.zero;
        rSlide = rSlideVel = Vector3.zero;
        spInit = false;
        rRotating = false; rBudgetShare = 1f; rZoomRateAllowed = 99f;
        mLeanVel = 0f;
        rzTarget = mOrtho; rzVel = 0f; rzLastWiden = Time.time;
        rHavePrev = false; rFocusVel = Vector3.zero;
        rlBlending = true; rlBlendT = 0f;    // the first frames glide from the pose on screen (no pop)
        ResetZoneDrift();
    }

    // ------------------------------------------------------------------
    // Entry for D and E (from ModeTick). WriteModePose runs after.
    // ------------------------------------------------------------------
    void TickRailModes(float dt, CameraFeelProfile P)
    {
        if (dt <= 0f) return;
        RailFrameLimits(P);
        RailUpdate(dt, P);
        if (activeMode == CameraMode.D_Rail) TickRailD(dt, P);
        else TickZoneDrift(dt, P);
        RailNet(dt, P);
        RailMeasureFlow(dt, P);
    }

    // speed, s along the centre line, epoch (a new path restarts everything)
    void RailUpdate(float dt, CameraFeelProfile P)
    {
        pathGen.BuildCentreLine();
        float sProj = pathGen.RouteSAt(modeRunner);
        float v = player.CurrentMoveSpeed;
        if (!rlInit || rlEpoch != pathGen.CentreEpoch)
        {
            bool newPath = rlInit;
            rlInit = true; rlEpoch = pathGen.CentreEpoch;
            rlS = sProj; rlSpeed = v; rlSpeedVel = 0f;
            rlLeadDist = P.railLeadD * v; rlLeadDistVel = 0f;
            if (newPath) { rlBlending = true; rlBlendT = 0f; spInit = false; ResetZoneDrift(); }
        }
        rlSpeed = Mathf.SmoothDamp(rlSpeed, v, ref rlSpeedVel, Mathf.Max(0.05f, P.railSpeedSmooth), float.MaxValue, dt);
        rlS01 = P.SpeedFraction(rlSpeed);
        // predict with the smoothed speed, correct gently toward the projection; never backwards
        bool moving = player.IsRunning && !player.IsGameOver;
        float pred = rlS + (moving ? rlSpeed * dt : 0f);
        float err = sProj - pred;
        float next = Mathf.Abs(err) > 8f ? sProj : pred + err * (1f - Mathf.Exp(-dt / 1.0f));
        rlS = Mathf.Max(rlS, next);
    }

    /// <summary>Gaussian-smoothed centre-line point leadT seconds of travel ahead of the runner.</summary>
    Vector3 RailPoint(float leadT, CameraFeelProfile P) => RailPointAt(leadT * rlSpeed, P);

    /// <summary>Gaussian-smoothed centre-line point leadDist world units of route ahead of the runner.</summary>
    Vector3 RailPointAt(float leadDist, CameraFeelProfile P)
    {
        var g = pathGen;
        if (g.CentreCount < 2) return modeRunner;
        float sig = Mathf.Max(0.5f, P.railSigmaSeconds.Evaluate(rlS01) * rlSpeed);
        float c = rlS + leadDist;
        float lo = g.CentreSMin, hi = g.CentreSMax;
        Vector3 sum = Vector3.zero;
        for (int k = -16; k <= 16; k++)
            sum += g.CentrePointAt(Mathf.Clamp(c + k * sig * 0.125f, lo, hi)) * RailW[k + 16];
        return sum;
    }

    /// <summary>The runner's place on the rail, Gaussian-smoothed over `sigma` world units (corners rounded, not cut much).</summary>
    Vector3 RailRunnerSmooth(float sigma)
    {
        var g = pathGen;
        if (g.CentreCount < 2) return modeRunner;
        sigma = Mathf.Max(0.25f, sigma);
        float lo = g.CentreSMin, hi = g.CentreSMax;
        Vector3 sum = Vector3.zero;
        for (int k = -16; k <= 16; k++)
            sum += g.CentrePointAt(Mathf.Clamp(rlS + k * sigma * 0.125f, lo, hi)) * RailW[k + 16];
        return sum;
    }

    /// <summary>The runner's place on the smoothed rail (no face hops, no roll arc).</summary>
    Vector3 RailRunnerPoint() => pathGen.CentreCount >= 2 ? pathGen.CentrePointAt(rlS) : modeRunner;

    // Lead room: a world offset that puts the runner leadRoom half-screens behind centre, against the direction of
    // the upcoming route (chord over leadRoomLookSeconds), smoothed.
    Vector3 LeadRoomWorld(float dt, CameraFeelProfile P, float hw, float hh)
    {
        Vector3 target = Vector3.zero;
        if (pathGen.CentreCount >= 2)
        {
            Vector3 d = pathGen.CentrePointAt(rlS + Mathf.Max(2f, rlSpeed * P.leadRoomLookSeconds)) - pathGen.CentrePointAt(rlS);
            float dx = Vector3.Dot(d, mRight), dy = Vector3.Dot(d, mUp);
            float m = Mathf.Sqrt(dx * dx + dy * dy);
            if (m > 1e-3f) target = mRight * (dx / m * P.leadRoom * hw) + mUp * (dy / m * P.leadRoom * hh);
        }
        rlLeadRoom = Vector3.SmoothDamp(rlLeadRoom, target, ref rlLeadRoomVel, Mathf.Max(0.05f, P.leadRoomSmooth), float.MaxValue, dt);
        return rlLeadRoom;
    }

    static float Tanh(float x) { float e = Mathf.Exp(-2f * Mathf.Abs(x)); float t = (1f - e) / (1f + e); return x < 0f ? -t : t; }

    // ------------------------------------------------------------------
    // MODE D
    // ------------------------------------------------------------------
    void TickRailD(float dt, CameraFeelProfile P)
    {
        // ---- yaw: C's plan decides when / what; a zone change without a yaw change does nothing ----
        float desired = DesiredYaw(out DynamicPathGenerator.PathZone zone);
        bool allow = !RailRotationBlocked(zone, P);
        RailBudget(dt, P, rRotating ? RailPlannedYawRate() : (allow && Mathf.Abs(Mathf.DeltaAngle(mYaw, desired)) > 1f ? 45f : 0f));
        RailRotationTick(dt, P, desired, allow);

        // ---- lean toward the next zone's yaw (only while holding, never toward a blocked yaw) ----
        float leanYaw = mYaw, progress = 0f;
        DynamicPathGenerator.PathZone pz = pathGen.PlayerZone;
        if (!rRotating && pz != null)
        {
            DynamicPathGenerator.PathZone next = pathGen.GetZoneAfter(pz);
            SolveZone(next);
            if (next != null && next.framingSolved) { leanYaw = next.optimalCameraYaw; progress = pathGen.PlayerProgressThrough(pz); }
        }
        RailLean(dt, P, leanYaw, progress, !rRotating && !RailRotationBlocked(pz, P));
        Basis(mYaw + mLean, out mRight, out mUp, out mFwd);

        // ---- focus ----
        // The lead (railLeadD seconds + the lead room) is a distance ALONG the route, not a screen offset: the focus
        // then rides the smoothed rail itself and turns every corner on the same rounded curve, instead of a lead vector
        // swinging across the screen at each turn. Converted with the screen width an iso leg covers per unit (0.707),
        // and softly saturated so the runner sits at most railLeadMaxD half-screens behind centre.
        float ortho = mOrtho * mBoostMul, hw = ortho * ModeAspect();
        const float isoScreenX = 0.7071f;
        float maxDist = Mathf.Max(0.5f, P.railLeadMaxD * hw / isoScreenX);
        float want = P.railLeadD * rlSpeed + P.leadRoom * hw / isoScreenX;
        want = maxDist * Tanh(want / maxDist);
        rlLeadDist = Mathf.SmoothDamp(rlLeadDist, want, ref rlLeadDistVel, Mathf.Max(0.05f, P.leadRoomSmooth * 1.5f), float.MaxValue, dt);
        Vector3 target = SoftSlide(RailPointAt(rlLeadDist, P), dt, P, 0.7f);
        RailFocusFollow(target, dt, P);
        mFocus = RunnerSafetyClamp(mFocus, P);
        if (smoothTeleportGlide) mFocus = TeleportGlideApply(mFocus, dt);

        // ---- zoom ----
        RailZoom(dt, P, P.railZoomFloorD, mFocus + emOff);
    }

    // D's focus: the target directly, except while blending in after a mode switch, a restart or a teleport.
    void RailFocusFollow(Vector3 target, float dt, CameraFeelProfile P)
    {
        // A zone door glides: follow the new target directly and let TeleportGlideApply ease the camera over.
        if (GlideThisTeleport()) { rlBlending = false; tgPending = true; }
        else if (rlHavePrevTarget && (target - rlPrevTarget).sqrMagnitude > P.railTeleportDistance * P.railTeleportDistance)
        { rlBlending = true; rlBlendT = 0f; spInit = false; }
        rlPrevTarget = target; rlHavePrevTarget = true;

        if (rlBlending)
        {
            if (!spInit) SpringSeed(mFocus, target);
            mFocus = SpringStep(target, dt, P);
            rlBlendT += dt;
            Vector3 err = Vector3.ProjectOnPlane(target - mFocus, mFwd);
            if (rlBlendT >= P.railRestartBlend && (err.sqrMagnitude < 0.01f || rlBlendT > 5f)) rlBlending = false;
        }
        else
        {
            mFocus = target;
            SpringSeed(target, target);   // keeps the spring in step so a later blend starts from here
        }
    }
}
