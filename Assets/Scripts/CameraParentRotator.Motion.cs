using UnityEngine;

/// <summary>
/// Shared motion layer for modes D (rail) and E (zone drift): frame limits measured inside Screen.safeArea
/// (HUD reserve, thumb keep-out zones, gaze zone), the focus spring, the zoom fit, the motion budget, planned
/// rotations, the ambient lean and the safety net. Modes A / B / C never call anything here.
/// Everything is allocation-free and uses Time.deltaTime, so it behaves the same at any frame rate.
/// </summary>
public partial class CameraParentRotator
{
    // ---- frame limits in half-screen units (centre 0, edges -1 / +1), refreshed each frame ----
    float fL = -1f, fR = 1f, fB = -1f, fT = 1f, fHud = 0.76f, fThumbY = -0.52f, fThumbL = -0.24f, fThumbR = 0.24f;

    // ---- focus spring (E; D only for restart / teleport blends) ----
    Vector3 spX, spV, spTargetPrev, spTargetVel;
    bool spInit;

    // ---- zoom ----
    float rzTarget, rzVel, rzLastWiden;

    // ---- planned rotation (D) and ambient lean (D and E) ----
    bool rRotating;
    float rYawFrom, rYawTo, rRotU, rRotDur;
    float mLean, mLeanVel;           // degrees, added to mYaw by WriteModePose; always 0 in A / B
    float rBudgetShare = 1f;          // 1 = rotation at its planned speed; lower = stretched by the motion budget
    float rZoomRateAllowed = 99f;     // ortho units / s the budget leaves for the zoom

    // ---- flow measurement ----
    Vector3 rPrevFocus, rFocusVel;
    float rPrevYaw, rPrevOrtho, rFlow, rFlowPos, rRadius = 4f, rLastBudgetLog = -10f, rLastNetTime = -10f;
    bool rHavePrev, rOverBudget;
    int rOverBudgetFrames, rFrames;

    // ---- teleport glide (D and E, opt-in) ----
    [Header("Teleport Glide (modes D / E)")]
    [Tooltip("On a teleport (a zone door): the follow restarts settled at the new spot and already moving with the runner, and the camera eases over to it from where it was, setting off at once and landing softly - no snap, no stall, no catch-up rush. Zone doors only; restarts are unchanged. Off = the spring restarts from rest and the safety clamp snaps the runner into the box.")]
    public bool smoothTeleportGlide = false;
    [Tooltip("Seconds the glide over to the new spot takes.")]
    [Range(0.2f, 3f)] public float teleportGlideSeconds = 0.9f;
    Vector3 tgOffset, tgPrevFinal;
    float tgT = -1f;
    bool tgPending, tgHavePrev;

    bool RailMode => activeMode == CameraMode.D_Rail || activeMode == CameraMode.E_ZoneDrift;

    /// <summary>Screen flow this frame, % of screen height per second (position + rotation + zoom). D / E only.</summary>
    public float ModeFlow => RailMode ? rFlow : 0f;
    /// <summary>True when this frame's flow exceeded the profile's flowMax. D / E only.</summary>
    public bool ModeOverBudget => RailMode && rOverBudget;
    /// <summary>True while a planned (not ambient) rotation is running. D / E only.</summary>
    public bool ModeRotating => RailMode && (rRotating || (eTrans && eYawChange));
    public int ModeOverBudgetFrames => rOverBudgetFrames;

    // ------------------------------------------------------------------
    // Frame limits (Screen.safeArea)
    // ------------------------------------------------------------------
    void RailFrameLimits(CameraFeelProfile P)
    {
        float x0 = 0f, x1 = 1f, y0 = 0f, y1 = 1f;
        if (P.useSafeArea && Screen.width > 0 && Screen.height > 0)
        {
            Rect sa = Screen.safeArea;
            x0 = Mathf.Clamp01(sa.xMin / Screen.width); x1 = Mathf.Clamp01(sa.xMax / Screen.width);
            y0 = Mathf.Clamp01(sa.yMin / Screen.height); y1 = Mathf.Clamp01(sa.yMax / Screen.height);
            if (x1 - x0 < 0.2f || y1 - y0 < 0.2f) { x0 = 0f; x1 = 1f; y0 = 0f; y1 = 1f; }
        }
        fL = 2f * x0 - 1f; fR = 2f * x1 - 1f; fB = 2f * y0 - 1f; fT = 2f * y1 - 1f;
        fHud = fT - 2f * P.hudTopReserve;
        fThumbY = fB + 2f * P.thumbZoneHeight * (y1 - y0);
        fThumbL = fL + 2f * P.thumbZoneWidth * (x1 - x0);
        fThumbR = fR - 2f * P.thumbZoneWidth * (x1 - x0);
    }

    /// <summary>x, y in half-screen units. True inside a thumb keep-out zone.</summary>
    bool InThumb(float x, float y) => y < fThumbY && (x < fThumbL || x > fThumbR);

    // ------------------------------------------------------------------
    // 2.1 Focus spring: critically damped, with target-velocity feed-forward, in the screen plane.
    // The depth component (along the view axis) follows the target exactly: it is invisible in an
    // orthographic view but keeps the near plane honest.
    // ------------------------------------------------------------------
    void SpringSeed(Vector3 x, Vector3 target)
    {
        spX = x; spV = Vector3.zero; spTargetPrev = target; spTargetVel = Vector3.zero; spInit = true;
    }

    // ------------------------------------------------------------------
    // Teleport glide (smoothTeleportGlide). TeleportGlideStart: the spring is put at the new target, already moving
    // with the runner, so the underlying shot is settled from the first frame. TeleportGlideApply (called on the final
    // focus every frame, after the safety clamp) adds an offset that starts as "where the camera was minus where it
    // now settles" and eases out to zero over teleportGlideSeconds. Purely additive: the spring never sees it.
    // ------------------------------------------------------------------
    int tgPortalsSeen = -1;

    /// <summary>True while a teleport glide is easing the camera over (smoothTeleportGlide).</summary>
    public bool TeleportGliding => tgT >= 0f;

    // Only a zone-door crossing glides; a restart / regeneration keeps the original blend.
    bool GlideThisTeleport()
    {
        if (!smoothTeleportGlide || pathGen == null) return false;
        return tgPortalsSeen >= 0 && pathGen.PortalsTaken > tgPortalsSeen;   // tgPortalsSeen = last frame's count (TeleportGlideApply)
    }

    Vector3 GlideRunnerVelocity()
    {
        var p = pathGen != null ? pathGen.player : null;
        return p != null ? p.transform.forward * p.forwardSpeed : Vector3.zero;
    }

    void TeleportGlideStart(Vector3 target)
    {
        Vector3 v = GlideRunnerVelocity();
        spX = target; spV = v; spTargetPrev = target; spTargetVel = v; spInit = true;
        tgPending = true;
    }

    Vector3 TeleportGlideApply(Vector3 focus, float dt)
    {
        if (tgPending)
        {
            tgPending = false;
            if (tgHavePrev)
            {
                tgOffset = tgPrevFinal - focus;
                tgT = 0f;
            }
        }
        Vector3 final = focus;
        if (tgT >= 0f)
        {
            tgT += dt;
            float T = Mathf.Max(0.1f, teleportGlideSeconds);
            float u = Mathf.Clamp01(tgT / T);
            // Ease-out: the picture has just cut (the runner jumped), so the camera sets off at once - no hang at the
            // old spot - and lands softly (zero slope at the end).
            float r = 1f - u;
            final = focus + tgOffset * (r * r * r);
            if (u >= 1f) tgT = -1f;
        }
        tgPrevFinal = final; tgHavePrev = true;
        if (pathGen != null) tgPortalsSeen = pathGen.PortalsTaken;
        return final;
    }

    Vector3 SpringStep(Vector3 target, float dt, CameraFeelProfile P)
    {
        if (!spInit) SpringSeed(target, target);
        Vector3 prev = spTargetPrev;
        Vector3 raw = (target - prev) / dt;
        if (raw.sqrMagnitude > 200f * 200f) { raw = spTargetVel; prev = target - raw * dt; }   // teleport: no velocity spike
        spTargetVel = Vector3.Lerp(spTargetVel, raw, 1f - Mathf.Exp(-dt / 0.05f));             // ~3 frames
        spTargetPrev = target;

        Vector3 f = mFwd;
        spX += f * Vector3.Dot(target - spX, f);
        spV = Vector3.ProjectOnPlane(spV, f);
        Vector3 tv = Vector3.ProjectOnPlane(spTargetVel, f);
        float w = Mathf.Max(0.1f, P.springOmega);
        float vr = rlSpeed / Mathf.Max(1f, P.speedLow);
        float aMax = Mathf.Max(0.1f, P.springMaxAccel) * Mathf.Clamp(vr * vr, 1f, Mathf.Max(1f, P.springAccelSpeedScaleMax));
        int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 120f), 1, 8);
        float h = dt / steps;
        for (int i = 1; i <= steps; i++)
        {
            Vector3 tgt = Vector3.LerpUnclamped(prev, target, (float)i / steps);
            Vector3 acc = w * w * Vector3.ProjectOnPlane(tgt - spX, f) + 2f * w * (tv - spV);
            float am = acc.magnitude;
            if (am > aMax) acc *= aMax / am;
            spV += acc * h;
            spX += spV * h;
        }
        return spX;
    }

    // ------------------------------------------------------------------
    // 2.5 Zoom: runner + route to the next corner + route to the corner after; if that needs more than the cap,
    // runner + route to the next corner + half the leg after it. Inside softFrameLimit, below the HUD, and (for
    // the runner and the route to the next corner) out of the thumb zones. Hysteresis, rate limits, delay, SmoothDamp.
    // ------------------------------------------------------------------
    float FitHalfHeight(Vector3 focus, int last, int thumbLast, float soft, float aspect, CameraFeelProfile P)
    {
        float xr = soft - (1f - fR), xl = soft - (1f + fL);
        float yt = Mathf.Min(soft - (1f - fT), fHud), yb = soft - (1f + fB);
        float pad = P.mustSeePadding, H = 0f;
        var g = pathGen;
        int n = Mathf.Min(last, g.RouteCount - 1);
        for (int i = 0; i <= n; i++)
        {
            Vector3 d = g.RoutePts[i] - focus;
            float x = Vector3.Dot(d, mRight), y = Vector3.Dot(d, mUp);
            float h = Mathf.Max((Mathf.Abs(x) + pad) / Mathf.Max(0.01f, aspect * (x >= 0f ? xr : xl)),
                                y >= 0f ? (y + pad) / Mathf.Max(0.01f, yt) : (pad - y) / Mathf.Max(0.01f, yb));
            if (i <= thumbLast && y < 0f && fThumbY < 0f)
            {
                float ty = (pad - y) / -fThumbY;
                float tx = x >= 0f ? (x + pad) / Mathf.Max(0.01f, aspect * fThumbR) : (pad - x) / Mathf.Max(0.01f, aspect * -fThumbL);
                if (fThumbR <= 0f || fThumbL >= 0f) tx = float.MaxValue;
                h = Mathf.Max(h, Mathf.Min(ty, tx));
            }
            H = Mathf.Max(H, h);
        }
        return H;
    }

    void RailZoom(float dt, CameraFeelProfile P, float floor, Vector3 focus)
    {
        float aspect = ModeAspect();
        float cap = Mathf.Max(floor, P.zoomCap.Evaluate(rlS01));
        var g = pathGen;
        int n = g.RouteCount, cc = g.RouteCornerCount;
        float req = floor;
        if (n > 0)
        {
            int c1 = cc > 0 ? g.RouteCornerIdx[0] : n - 1;
            int c2 = cc > 1 ? g.RouteCornerIdx[1] : n - 1;
            req = FitHalfHeight(focus, c2, c1, P.softFrameLimit, aspect, P);
            if (req > cap)
            {
                RouteSets(0f, out _, out _, out int ie, out _);
                req = FitHalfHeight(focus, ie, c1, P.softFrameLimit, aspect, P);
            }
        }
        req = Mathf.Clamp(req, floor, cap);
        if (req > rzTarget) { rzTarget = req; rzLastWiden = Time.time; }
        else if (req < rzTarget * (1f - P.railZoomHysteresis) && Time.time - rzLastWiden > P.railZoomTightenDelay) rzTarget = req;
        rzTarget = Mathf.Clamp(rzTarget, floor, cap);
        float maxRate = rzTarget > mOrtho ? P.railZoomWidenPerSecond : P.railZoomTightenPerSecond;
        maxRate = Mathf.Max(0.05f, Mathf.Min(maxRate, rZoomRateAllowed));
        mOrtho = Mathf.SmoothDamp(mOrtho, rzTarget, ref rzVel, Mathf.Max(0.05f, P.railZoomSmooth), maxRate, dt);
    }

    // ------------------------------------------------------------------
    // 2.2 Motion budget. Position is never slowed (the rail keeps the runner safe); what is left of flowMax goes
    // to rotation, then zoom; if the rotation alone is over, it is stretched (never below rotMinBudgetShare).
    // plannedYawRate = deg/s the planned rotation wants this frame at full speed.
    // ------------------------------------------------------------------
    void RailBudget(float dt, CameraFeelProfile P, float plannedYawRate)
    {
        float ortho = Mathf.Max(1f, mOrtho * mBoostMul);
        float remaining = P.flowMax - rFlowPos;
        float rotFlow = 100f * plannedYawRate * Mathf.Deg2Rad * rRadius / (2f * ortho);
        float zoomFlow = Mathf.Max(0f, remaining - rotFlow);
        rZoomRateAllowed = Mathf.Max(0.3f, zoomFlow * ortho / 100f);
        float share = rotFlow > 1e-3f ? Mathf.Clamp(remaining / rotFlow, P.rotMinBudgetShare, 1f) : 1f;
        rBudgetShare = Mathf.MoveTowards(rBudgetShare, share, dt * 2f);
    }

    // ------------------------------------------------------------------
    // 2.3 Planned rotations (>= rotSecondsPer90 per 90 degrees, SmoothStep, peak <= rotPeakDegPerSec).
    // ------------------------------------------------------------------
    float RotationSeconds(float deg, CameraFeelProfile P)
    {
        float q = deg / 90f;
        float t = q <= 1.01f ? P.rotSecondsPer90 : Mathf.Lerp(P.rotSecondsPer90, P.rotSecondsPer180, Mathf.Clamp01(q - 1f));
        return Mathf.Max(t, 1.5f * deg / Mathf.Max(1f, P.rotPeakDegPerSec), 0.3f);
    }

    /// <summary>Instantaneous yaw rate (deg/s) of a SmoothStep move of `deg` over `dur` at progress u.</summary>
    static float SmoothStepRate(float deg, float dur, float u) => 6f * u * (1f - u) * deg / Mathf.Max(0.05f, dur);

    // True if no planned rotation may start now: a boost, a bridge or a power-up handover is on or within the profile's legs.
    bool RailRotationBlocked(DynamicPathGenerator.PathZone zone, CameraFeelProfile P)
    {
        if (SuppressYawChanges) return true;   // LookTransition cinematic in flight (off everywhere else)
        if (pathGen.PlayerOnZigZagBoost || player.PowerAuto || pathGen.PowerZoom > 1.001f) return zone == null || zone.forcedYaw < 0f;
        return YawChangeBlocked(zone, P);
    }

    void RailRotationTick(float dt, CameraFeelProfile P, float desiredYaw, bool allowStart)
    {
        if (!rRotating && allowStart && Mathf.Abs(Mathf.DeltaAngle(mYaw, desiredYaw)) > 1f)
        {
            rRotating = true;
            rYawFrom = mYaw + mLean; mLean = 0f; mLeanVel = 0f;     // the lean is folded into the move: no pop
            rYawTo = desiredYaw; rRotU = 0f;
            rRotDur = RotationSeconds(Mathf.Abs(Mathf.DeltaAngle(rYawFrom, rYawTo)), P);
            if (P.logDecisions) Debug.Log("[CAM " + ModeSub() + "] rotate " + rYawFrom.ToString("F0") + " -> " + rYawTo.ToString("F0") + " over " + rRotDur.ToString("F2") + " s");
        }
        if (!rRotating) return;
        rRotU = Mathf.Clamp01(rRotU + dt / rRotDur * rBudgetShare);
        mYaw = Mathf.LerpAngle(rYawFrom, rYawTo, Mathf.SmoothStep(0f, 1f, rRotU));
        if (rRotU >= 1f) { mYaw = rYawTo; rRotating = false; }
    }

    float RailPlannedYawRate() => rRotating ? SmoothStepRate(Mathf.Abs(Mathf.DeltaAngle(rYawFrom, rYawTo)), rRotDur, rRotU) : 0f;

    // Ambient lean: toward `towardYaw` by at most railLeanDegrees * progress, never faster than ambientYawRateMax.
    void RailLean(float dt, CameraFeelProfile P, float towardYaw, float progress, bool allowed)
    {
        float target = 0f;
        if (allowed)
            target = Mathf.Clamp(Mathf.DeltaAngle(mYaw, towardYaw), -P.railLeanDegrees, P.railLeanDegrees) * Mathf.Clamp01(progress);
        mLean = Mathf.SmoothDamp(mLean, target, ref mLeanVel, Mathf.Max(0.05f, P.railLeanSmooth), Mathf.Max(0.01f, P.ambientYawRateMax), dt);
        mLean = Mathf.Clamp(mLean, -3f, 3f);
    }

    // ------------------------------------------------------------------
    // 2.4 Safety net: the runner (now and in railNetLookAhead seconds) and the next corner, against the hard box
    // of the PREDICTED pose. A spring correction (railNetSmooth) while needed; it fades out (railNetRelease) after.
    // The runner outranks the corner: the correction may not push the runner out of its safe box.
    // ------------------------------------------------------------------
    void RailNet(float dt, CameraFeelProfile P)
    {
        float aspect = ModeAspect();
        float ortho = mOrtho * mBoostMul, hh = ortho, hw = ortho * aspect;
        float lim = P.railHardLimit, look = P.railNetLookAhead, pad = P.mustSeePadding;
        Vector3 focus = mFocus + emOff;
        Vector3 vPred = Vector3.ProjectOnPlane(rFocusVel, mFwd);
        if (vPred.sqrMagnitude > Mathf.Pow(2f * rlSpeed + 20f, 2f)) vPred = Vector3.zero;   // a restart / blend jump is not a velocity
        Vector3 fPred = focus + vPred * look;
        float xr = (lim - (1f - fR)) * hw, xl = -(lim - (1f + fL)) * hw;
        float yt = Mathf.Min(lim - (1f - fT), fHud) * hh, yb = -(lim - (1f + fB)) * hh;

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        Vector3 runNow = RailRunnerPoint();
        Vector3 runAhead = RoutePointAt(Mathf.Max(0.5f, rlSpeed) * look);
        Accum(runNow - focus, pad, ref minX, ref maxX, ref minY, ref maxY);
        Accum(runAhead - fPred, pad, ref minX, ref maxX, ref minY, ref maxY);
        if (pathGen.RouteCornerCount > 0 && pathGen.RouteArcs[pathGen.RouteCornerIdx[0]] < Mathf.Max(8f, rlSpeed * 1.5f))
        {
            // the corner joins the check once it is imminent (1.5 s), and only if runner and corner fit the hard box together at this zoom
            Vector3 c1 = pathGen.RoutePts[pathGen.RouteCornerIdx[0]];
            Vector3 dc = c1 - runNow;
            float spanX = Mathf.Abs(Vector3.Dot(dc, mRight)) + 2f * pad, spanY = Mathf.Abs(Vector3.Dot(dc, mUp)) + 2f * pad;
            if (spanX <= xr - xl && spanY <= yt - yb) Accum(c1 - fPred, pad, ref minX, ref maxX, ref minY, ref maxY);
        }
        float sx = ShiftRange(minX, maxX, xl, xr);
        float sy = ShiftRange(minY, maxY, yb, yt);
        {
            Vector3 dr = runNow - focus;
            float rx = Vector3.Dot(dr, mRight), ry = Vector3.Dot(dr, mUp);
            float sfx = (P.runnerSafeX - 0.03f) * hw;
            float sfLo = (P.runnerSafeYMin + 0.03f) * hh, sfHi = (Mathf.Min(P.runnerSafeYMax, fHud) - 0.03f) * hh;
            // limits a correction; never creates one (a runner merely near its box edge is not an emergency)
            sx = Mathf.Clamp(sx, Mathf.Min(0f, rx - sfx), Mathf.Max(0f, rx + sfx));
            sy = Mathf.Clamp(sy, Mathf.Min(0f, ry - sfHi), Mathf.Max(0f, ry - sfLo));
        }
        // a dead zone to engage (grazing the box by a few cm is not an emergency), a small one to let go
        float engage = emergencyActive ? 0.01f : 0.25f;
        bool need = Mathf.Abs(sx) > engage || Mathf.Abs(sy) > engage;
        if (need && !emergencyActive && Time.time - rLastNetTime > 0.5f)
        {
            emergencyCount++;
        {
            Vector3 dn = runNow - focus, da = runAhead - fPred;
            string c1s = "-";
            if (pathGen.RouteCornerCount > 0) { Vector3 dc = pathGen.RoutePts[pathGen.RouteCornerIdx[0]] - fPred; c1s = (Vector3.Dot(dc, mRight) / hw).ToString("F2") + "," + (Vector3.Dot(dc, mUp) / hh).ToString("F2") + " arc " + pathGen.RouteArcs[pathGen.RouteCornerIdx[0]].ToString("F0"); }
            Debug.Log("[CAM NET] #" + emergencyCount + " mode=" + activeMode + " sub=" + ModeSub() + " shift=(" + sx.ToString("F2") + "," + sy.ToString("F2") + ") half=(" + hw.ToString("F1") + "," + hh.ToString("F1") + ") speed=" + rlSpeed.ToString("F1") + " t=" + Time.time.ToString("F1")
                + " runNow=" + (Vector3.Dot(dn, mRight) / hw).ToString("F2") + "," + (Vector3.Dot(dn, mUp) / hh).ToString("F2")
                + " runAhead=" + (Vector3.Dot(da, mRight) / hw).ToString("F2") + "," + (Vector3.Dot(da, mUp) / hh).ToString("F2") + " C1=" + c1s
                + " vF=" + rFocusVel.magnitude.ToString("F1"));
        }
        }
        if (need) rLastNetTime = Time.time;
        emergencyActive = need;
        if (need) emOff = Vector3.SmoothDamp(emOff, emOff + mRight * sx + mUp * sy, ref emVel, Mathf.Max(0.02f, P.railNetSmooth), float.MaxValue, dt);
        else emOff = Vector3.SmoothDamp(emOff, Vector3.zero, ref emVel, Mathf.Max(0.05f, P.railNetRelease), float.MaxValue, dt);
    }

    // ------------------------------------------------------------------
    // Soft frame slide (both modes): eases the runner and the imminent next corner back inside softFrameLimit
    // (under the HUD), long before the safety net would have to act. The target is absolute (measured from the
    // un-slid focus), so there is no feedback loop; SmoothDamp keeps it free of steps. The runner keeps its safe box.
    // ------------------------------------------------------------------
    Vector3 rSlide, rSlideVel;

    Vector3 SoftSlide(Vector3 focus, float dt, CameraFeelProfile P, float smooth)
    {
        float ortho = mOrtho * mBoostMul, hh = ortho, hw = ortho * ModeAspect();
        float soft = P.softFrameLimit, pad = P.mustSeePadding;
        float xr = (soft - (1f - fR)) * hw, xl = -(soft - (1f + fL)) * hw;
        float yt = Mathf.Min(soft - (1f - fT), fHud) * hh, yb = -(soft - (1f + fB)) * hh;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        Vector3 run = RailRunnerPoint();
        Accum(run - focus, pad, ref minX, ref maxX, ref minY, ref maxY);
        float sx = ShiftRange(minX, maxX, xl, xr), sy = ShiftRange(minY, maxY, yb, yt);
        // the next corner is phased in by how imminent it is (2.5 s -> 1.5 s of travel), so a new corner never steps the target
        var g = pathGen;
        if (g.RouteCornerCount > 0)
        {
            float arc = g.RouteArcs[g.RouteCornerIdx[0]], v = Mathf.Max(4f, rlSpeed);
            float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(v * 2.5f, v * 1.5f, arc));
            Vector3 c1 = g.RoutePts[g.RouteCornerIdx[0]], dc = c1 - run;
            if (w > 0f && Mathf.Abs(Vector3.Dot(dc, mRight)) + 2f * pad <= xr - xl && Mathf.Abs(Vector3.Dot(dc, mUp)) + 2f * pad <= yt - yb)
            {
                Accum(c1 - focus, pad, ref minX, ref maxX, ref minY, ref maxY);
                sx = Mathf.Lerp(sx, ShiftRange(minX, maxX, xl, xr), w);
                sy = Mathf.Lerp(sy, ShiftRange(minY, maxY, yb, yt), w);
            }
        }
        Vector3 dr = run - focus;
        float rx = Vector3.Dot(dr, mRight), ry = Vector3.Dot(dr, mUp);
        float sfx = (P.runnerSafeX - 0.05f) * hw;
        float sfLo = Mathf.Max(P.runnerSafeYMin + 0.05f, fThumbY + 0.04f) * hh, sfHi = (Mathf.Min(P.runnerSafeYMax, fHud) - 0.05f) * hh;
        sx = Mathf.Clamp(sx, rx - sfx, rx + sfx);
        if (sfLo < sfHi) sy = Mathf.Clamp(sy, ry - sfHi, ry - sfLo);
        rSlide = Vector3.SmoothDamp(rSlide, mRight * sx + mUp * sy, ref rSlideVel, Mathf.Max(0.05f, smooth), float.MaxValue, dt);
        return focus + rSlide;
    }

    // ------------------------------------------------------------------
    // Runner safety clamp (both modes, after the focus is final): the runner (on the rail) can never sit past its
    // safe box (and the thumb zones) minus KneeM. A soft knee 2*KneeM wide means the correction starts with zero
    // slope - no velocity step - and it is stateless, so it never lags. "Keep the runner safe" outranks smoothness.
    // ------------------------------------------------------------------
    const float KneeM = 0.025f;

    static float Knee(float over)   // how far to pull back, for a point `over` past the inner edge (half-screens)
    {
        if (over <= 0f) return 0f;
        return over < 2f * KneeM ? over * over / (4f * KneeM) : over - KneeM;
    }

    Vector3 RunnerSafetyClamp(Vector3 focus, CameraFeelProfile P)
    {
        float ortho = mOrtho * mBoostMul, hh = ortho, hw = ortho * ModeAspect();
        // reference: the rail lightly rounded (sigma = 0.3 half-widths), so a clamped corner is a curve, not a kink;
        // it cuts a corner apex by about sigma / 2.5 (~0.1 half-widths) - a brief, accepted overshoot that keeps the camera calm
        Vector3 d = RailRunnerSmooth(0.3f * hw) - focus;
        float x = Vector3.Dot(d, mRight) / hw, y = Vector3.Dot(d, mUp) / hh;
        const float cut = 0f;
        float xMax = P.runnerSafeX - 2f * KneeM - cut;
        float yHi = Mathf.Min(P.runnerSafeYMax, fHud) - 2f * KneeM - cut;
        float yLo = Mathf.Max(P.runnerSafeYMin, fThumbY + 0.02f) + 2f * KneeM + cut;
        float sx = x > 0f ? Knee(x - xMax) : -Knee(-x - xMax);
        float sy = yLo < yHi ? (y > yHi ? Knee(y - yHi) : -Knee(yLo - y)) : 0f;
        return focus + mRight * (sx * hw) + mUp * (sy * hh);
    }

    void Accum(Vector3 d, float pad, ref float minX, ref float maxX, ref float minY, ref float maxY)
    {
        float x = Vector3.Dot(d, mRight), y = Vector3.Dot(d, mUp);
        minX = Mathf.Min(minX, x - pad); maxX = Mathf.Max(maxX, x + pad);
        minY = Mathf.Min(minY, y - pad); maxY = Mathf.Max(maxY, y + pad);
    }

    // ------------------------------------------------------------------
    // Flow measurement (after the pose is final for the frame)
    // ------------------------------------------------------------------
    void RailMeasureFlow(float dt, CameraFeelProfile P)
    {
        Vector3 focus = mFocus + emOff;
        float yaw = mYaw + mLean, ortho = Mathf.Max(1f, mOrtho * mBoostMul);
        rRadius = Mathf.Max(4f, Vector3.ProjectOnPlane(modeRunner - focus, mFwd).magnitude);
        if (rHavePrev && dt > 0f && (focus - rPrevFocus).sqrMagnitude < P.railTeleportDistance * P.railTeleportDistance * 4f)
        {
            rFocusVel = (focus - rPrevFocus) / dt;
            float v = Vector3.ProjectOnPlane(rFocusVel, mFwd).magnitude;
            float yawRate = Mathf.Abs(Mathf.DeltaAngle(rPrevYaw, yaw)) * Mathf.Deg2Rad / dt;
            float zr = Mathf.Abs(ortho - rPrevOrtho) / dt;
            rFlowPos = 100f * v / (2f * ortho);
            rFlow = rFlowPos + 100f * yawRate * rRadius / (2f * ortho) + 100f * zr / ortho;
            rOverBudget = rFlow > P.flowMax;
            rFrames++;
            if (rOverBudget)
            {
                rOverBudgetFrames++;
                if (P.logDecisions && Time.time - rLastBudgetLog > 2f)
                {
                    rLastBudgetLog = Time.time;
                    Debug.Log("[CAM BUDGET] flow " + rFlow.ToString("F0") + " %/s (position " + rFlowPos.ToString("F0") + ") over " + P.flowMax.ToString("F0") + " | frames over so far " + rOverBudgetFrames + "/" + rFrames);
                }
            }
        }
        else { rFocusVel = Vector3.zero; rFlow = rFlowPos = 0f; rOverBudget = false; }
        rPrevFocus = focus; rPrevYaw = yaw; rPrevOrtho = ortho; rHavePrev = true;
    }
}
