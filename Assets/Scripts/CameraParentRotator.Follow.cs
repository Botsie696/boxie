using UnityEngine;

/// <summary>
/// MODE B - FOLLOW. A continuous, smoothed follow with a route lead: the focus tracks the runner (plus a
/// small velocity feed-forward) and leans toward the centroid of the route the runner is about to cover, with
/// capped speed and acceleration. Zone hand-offs without a yaw change do nothing visible; a yaw change turns
/// only the yaw while the position keeps following. A zoom that needs a 6% change before it moves, a safety
/// slide for P0 / P1, and the shared emergency net sit on top.
/// </summary>
public partial class CameraParentRotator
{
    Vector3 bTrack, bTrackVel; bool bTrackInit;
    Vector3 bVel, sdVel, lead, leadVel, prevRunner, runnerVel;
    float zoomTarget, zoomVelB, lastWiden;
    Vector2 slide, slideVel;
    bool bRotating, bHavePrev;
    float bYawFrom, bYawTo, bYawT, bYawDur;
    int slideActiveFrames, followFrames;
    readonly Vector3[] bPts = new Vector3[24];
    readonly float[] bTimes = new float[24];

    /// <summary>Share of mode-B frames in which the safety slide had work to do (target < 3%).</summary>
    public float FollowSlideActiveShare => followFrames > 0 ? (float)slideActiveFrames / followFrames : 0f;

    Vector3 SlideWorld() => activeMode == CameraMode.B_Follow ? mRight * slide.x + mUp * slide.y : Vector3.zero;

    void ResetFollowState()
    {
        bVel = sdVel = lead = leadVel = runnerVel = Vector3.zero;
        zoomTarget = mOrtho; zoomVelB = 0f; lastWiden = Time.time;
        slide = slideVel = Vector2.zero;
        bRotating = false; bHavePrev = false; bTrackInit = false;
        slideActiveFrames = followFrames = 0;
    }

    void TickFollow(float dt, CameraFeelProfile P)
    {
        if (dt <= 0f) return;
        float v = Mathf.Max(0.5f, modeSpeed), s = modeS;
        float aspect = ModeAspect();
        followFrames++;

        // runner velocity (a teleport / restart resets it)
        if (!bHavePrev) { prevRunner = modeRunner; bHavePrev = true; }
        Vector3 raw = (modeRunner - prevRunner) / dt;
        if (raw.magnitude > 120f) { raw = Vector3.zero; bVel = Vector3.zero; }
        runnerVel = Vector3.Lerp(runnerVel, raw, 1f - Mathf.Exp(-dt / 0.05f));
        prevRunner = modeRunner;

        // ---- yaw: only the yaw turns (SmootherStep); position keeps following ----
        float desired = DesiredYaw(out DynamicPathGenerator.PathZone zone);
        if (!bRotating && Mathf.Abs(Mathf.DeltaAngle(mYaw, desired)) > 1f && !YawChangeBlocked(zone, P))
        {
            bRotating = true; bYawFrom = mYaw; bYawTo = desired; bYawT = 0f;
            bYawDur = P.rotationMinSeconds + Mathf.Abs(Mathf.DeltaAngle(mYaw, desired)) / 90f * P.secondsPer90.Evaluate(s);
        }
        if (bRotating)
        {
            bYawT += dt;
            float u = Mathf.Clamp01(bYawT / Mathf.Max(0.05f, bYawDur));
            mYaw = Mathf.LerpAngle(bYawFrom, bYawTo, u * u * u * (u * (u * 6f - 15f) + 10f));
            if (u >= 1f) { mYaw = bYawTo; bRotating = false; }
            Basis(mYaw, out mRight, out mUp, out mFwd);
        }

        // ---- B1: focus ----
        float hhNow = mRenderedOrtho, hwNow = hhNow * aspect;
        float T = Mathf.Max(0.2f, P.lookAheadSeconds.Evaluate(s));
        int cnt = pathGen.SampleRouteAhead(modeRunner, T, v, bPts, bTimes);
        Vector3 centroid = modeRunner;
        if (cnt > 0)
        {
            Vector3 sum = Vector3.zero; float ws = 0f;
            for (int i = 0; i < cnt; i++)
            {
                float w = Mathf.Sqrt(Mathf.Max(0f, 1f - bTimes[i] / T));
                sum += bPts[i] * w; ws += w;
            }
            if (ws > 1e-4f) centroid = sum / ws;
        }
        Vector3 leadRaw = (centroid - modeRunner) * P.leadWeight;
        // the runner sits at -lead on screen, so the lead is also limited to what keeps the runner inside its safe box
        float sm = 0.03f, topS = Mathf.Min(P.runnerSafeYMax, TopLimit(P));
        float lxMax = Mathf.Min(P.leadMax, P.runnerSafeX - sm) * hwNow;
        float lx = Mathf.Clamp(Vector3.Dot(leadRaw, mRight), -lxMax, lxMax);
        float ly = Mathf.Clamp(Vector3.Dot(leadRaw, mUp), Mathf.Max(-P.leadMax, -(topS - sm)) * hhNow, Mathf.Min(P.leadMax, -(P.runnerSafeYMin + sm)) * hhNow);
        lead = Vector3.SmoothDamp(lead, mRight * lx + mUp * ly, ref leadVel, Mathf.Max(0.05f, P.leadSmoothTime.Evaluate(s)), float.MaxValue, dt);

        // The runner hops sideways when it changes face; that must not move the camera. Follow a low-passed copy of its
        // position (0.25 s), with the lag cancelled by feed-forward along the ROUTE direction (smooth, unlike the runner's own wobble).
        if (!bTrackInit) { bTrack = modeRunner; bTrackVel = Vector3.zero; bTrackInit = true; }
        bTrack = Vector3.SmoothDamp(bTrack, modeRunner, ref bTrackVel, 0.25f, float.MaxValue, dt);
        Vector3 tangent = pathGen.RouteCount > 2 ? (pathGen.RoutePts[2] - pathGen.RoutePts[0]).normalized : Vector3.zero;
        Vector3 target = bTrack + tangent * (v * 0.25f) + lead;
        Vector3 cur = mFocus;
        Vector3 aim = cur + Vector3.ProjectOnPlane(target - cur, mFwd);
        Vector3 sd = Vector3.SmoothDamp(cur, aim, ref sdVel, Mathf.Max(0.02f, P.followSmoothTime), float.MaxValue, dt);
        Vector3 dv = (sd - cur) / dt - bVel;
        float maxDv = P.followMaxAccel * dt;
        if (dv.magnitude > maxDv) dv = dv.normalized * maxDv;
        bVel += dv;
        float cap = v * P.followSpeedFactor + P.followSpeedPlus;
        if (bVel.magnitude > cap) bVel = bVel.normalized * cap;
        mFocus += bVel * dt;

        // ---- B2: zoom ----
        RouteSets(0f, out int i0, out int ic, out int ie, out int il);
        float pad = P.mustSeePadding;
        float soft = P.followSoftLimit, hard = P.followHardLimit, top = Mathf.Min(soft, TopLimit(P));
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        float p0x = Vector3.Dot(pathGen.RoutePts[0], mRight), p0y = Vector3.Dot(pathGen.RoutePts[0], mUp);
        x0 = x1 = p0x; y0 = y1 = p0y;
        for (int i = i0; i <= ie; i++)
        {
            float x = Vector3.Dot(pathGen.RoutePts[i], mRight), y = Vector3.Dot(pathGen.RoutePts[i], mUp);
            x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
        }
        float reqA = FitOrtho(x1 - x0 + 2f * pad, y1 - y0 + 2f * pad, soft, top, aspect);
        for (int i = ie + 1; i <= il; i++)
        {
            float x = Vector3.Dot(pathGen.RoutePts[i], mRight), y = Vector3.Dot(pathGen.RoutePts[i], mUp);
            x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
        }
        float reqB = FitOrtho(x1 - x0 + 2f * pad, y1 - y0 + 2f * pad, soft, top, aspect);
        float zcap = Mathf.Max(P.zoomFloor, P.zoomCap.Evaluate(s));
        float req = Mathf.Clamp(reqB <= zcap ? reqB : reqA, P.zoomFloor, zcap);

        if (req > zoomTarget) { zoomTarget = req; lastWiden = Time.time; }
        else if (req < zoomTarget * (1f - P.zoomHysteresis) && Time.time - lastWiden > P.zoomTightenDelay) zoomTarget = req;
        zoomTarget = Mathf.Clamp(zoomTarget, P.zoomFloor, zcap);
        float maxZ = zoomTarget > mOrtho ? P.zoomWidenPerSecond : P.zoomTightenPerSecond;
        mOrtho = Mathf.SmoothDamp(mOrtho, zoomTarget, ref zoomVelB, Mathf.Max(0.05f, P.zoomSmoothTime), maxZ, dt);
        mOrtho = Mathf.Max(P.zoomFloor, mOrtho);

        // ---- B4: safety slide for P0 / P1 ----
        float ortho = mOrtho * mBoostMul, hh = ortho, hw = ortho * aspect;
        Vector3 baseF = mFocus + emOff;
        float mnX = float.MaxValue, mxX = float.MinValue, mnY = float.MaxValue, mxY = float.MinValue;
        for (int pass = -1; pass <= ic; pass = pass < 0 ? i0 : pass + 1)
        {
            Vector3 d = (pass < 0 ? pathGen.RoutePts[0] : pathGen.RoutePts[pass]) - baseF;
            float x = Vector3.Dot(d, mRight), y = Vector3.Dot(d, mUp);
            mnX = Mathf.Min(mnX, x - pad); mxX = Mathf.Max(mxX, x + pad);
            mnY = Mathf.Min(mnY, y - pad); mxY = Mathf.Max(mxY, y + pad);
        }
        // the runner (P0) outranks the route (P1): the slide may never push the runner out of its safe box
        float rxR = Vector3.Dot(pathGen.RoutePts[0] - baseF, mRight), ryR = Vector3.Dot(pathGen.RoutePts[0] - baseF, mUp);
        float sfx = (P.runnerSafeX - 0.03f) * hw, sfLo = (P.runnerSafeYMin + 0.03f) * hh, sfHi = (Mathf.Min(P.runnerSafeYMax, TopLimit(P)) - 0.03f) * hh;
        Vector2 softT = new Vector2(ShiftRange(mnX, mxX, -soft * hw, soft * hw), ShiftRange(mnY, mxY, -soft * hh, top * hh));
        softT.x = Mathf.Clamp(softT.x, rxR - sfx, rxR + sfx); softT.y = Mathf.Clamp(softT.y, ryR - sfHi, ryR - sfLo);
        if (softT.sqrMagnitude > 1e-4f) slideActiveFrames++;
        float use = Mathf.Max(Mathf.Max(mxX - slide.x, slide.x - mnX) / hw, Mathf.Max(mxY - slide.y, slide.y - mnY) / hh);
        float closeness = Mathf.InverseLerp(soft, Mathf.Max(hard, soft + 0.001f), use);
        float smooth = Mathf.Lerp(P.slideSoftSmooth, P.slideHardSmooth, closeness);
        slide = Vector2.SmoothDamp(slide, softT, ref slideVel, Mathf.Max(0.01f, smooth), float.MaxValue, dt);
        slide += new Vector2(ShiftRange(mnX - slide.x, mxX - slide.x, -hard * hw, hard * hw),
                             ShiftRange(mnY - slide.y, mxY - slide.y, -hard * hh, Mathf.Min(hard, TopLimit(P)) * hh));
        slide.x = Mathf.Clamp(slide.x, rxR - sfx, rxR + sfx); slide.y = Mathf.Clamp(slide.y, ryR - sfHi, ryR - sfLo);
    }

    // Smallest ortho (half-height) at which a box of the given size fits the window [-soft, soft] x [-soft, top].
    static float FitOrtho(float spanX, float spanY, float soft, float top, float aspect)
    {
        return Mathf.Max(spanY / Mathf.Max(0.01f, soft + top), spanX / Mathf.Max(0.01f, 2f * soft * aspect));
    }
}
