using UnityEngine;

/// <summary>
/// MODE A - ZONE GLIDE. The camera is either completely still or performing ONE planned glide
/// (focus, ortho and yaw together). Nothing else moves it - no follow, no keep-in-frame drift, no
/// separate zoom drift - except the boost zoom and the emergency net (CameraParentRotator.Modes.cs).
///
/// When to move: predictive and need-based. Each frame the runner is predicted lookPredict seconds along
/// the real route; if the runner would leave the safe box, or the route to the next corner and the first
/// half of the leg after it would leave the soft frame, a glide starts (or a planned yaw change that is
/// now allowed). Where to: the shot that can be held longest (see PlanShot).
/// </summary>
public partial class CameraParentRotator
{
    enum AState { Hold, Glide }
    AState aState;
    float aHold, aCalm, aReplanCd, aGlideT, aGlideDur, aRetargetCd;
    int aRetargets;
    Vector3 gFrom, gTo, aPrevFocus;
    float gYawFrom, gYawTo, gOrthoFrom, gOrthoTo, gM0;
    bool aYawBlockLogged;

    string ModeSub() => activeMode == CameraMode.A_ZoneGlide ? (aState == AState.Glide ? "GLIDE" : "HOLD")
                      : activeMode == CameraMode.D_Rail ? "RAIL"
                      : activeMode == CameraMode.E_ZoneDrift ? (eTrans ? "ZONEDRIFT-TRANS" : "ZONEDRIFT")
                      : "FOLLOW";

    float ModeTargetYaw()
    {
        if (activeMode == CameraMode.A_ZoneGlide) return aState == AState.Glide ? gYawTo : mYaw;
        if (activeMode == CameraMode.D_Rail) return rRotating ? rYawTo : mYaw;
        if (activeMode == CameraMode.E_ZoneDrift) return eTrans ? eToYaw : mYaw;
        return bRotating ? bYawTo : mYaw;
    }

    void ResetSubStates()
    {
        aState = AState.Hold; aHold = 0f; aCalm = 0f; aReplanCd = 0f; aGlideT = 0f; aRetargets = 0; aPrevFocus = mFocus;
        ResetFollowState();
        ResetRailState();
    }

    void TickZoneGlide(float dt, CameraFeelProfile P)
    {
        float v = Mathf.Max(0.5f, modeSpeed);
        float s = modeS;
        Vector3 vel = dt > 0f ? (mFocus - aPrevFocus) / dt : Vector3.zero;
        aCalm -= dt;
        aReplanCd -= dt;

        if (aState == AState.Hold)
        {
            aHold += dt;
            if (aCalm <= 0f && aReplanCd <= 0f && pathGen.RouteCount >= 2)
            {
                float desired = DesiredYaw(out DynamicPathGenerator.PathZone zone);
                bool yawDiff = Mathf.Abs(Mathf.DeltaAngle(mYaw, desired)) > 1f;
                bool yawNeed = yawDiff && !YawChangeBlocked(zone, P);
                if (yawDiff && !yawNeed && !aYawBlockLogged) { aYawBlockLogged = true; Debug.Log("[CAM A] yaw change postponed: boost/bridge within " + P.blockRotationIfBoostOrBridgeWithinLegs + " legs"); }
                if (!yawDiff) aYawBlockLogged = false;

                bool want = yawNeed || (aHold >= P.minHoldSeconds.Evaluate(s) && PredictViolation(P, v, s));
                if (want) StartGlide(yawNeed ? desired : mYaw, P, v, s);
            }
        }
        else
        {
            aGlideT += dt;
            float dur = Mathf.Max(0.05f, aGlideDur);
            float u = Mathf.Clamp01(aGlideT / dur);
            float a = gM0 - 2f, b = 3f - 2f * gM0;
            float h = a * u * u * u + b * u * u + gM0 * u;               // SmoothStep when gM0 = 0
            mFocus = Vector3.LerpUnclamped(gFrom, gTo, h);
            mOrtho = Mathf.LerpUnclamped(gOrthoFrom, gOrthoTo, h);
            mYaw = Mathf.LerpAngle(gYawFrom, gYawTo, u * u * u * (u * (u * 6f - 15f) + 10f));

            aRetargetCd -= dt;
            if (u < 0.85f && aRetargetCd <= 0f && aRetargets < 3)
            {
                aRetargetCd = 0.25f;
                float remaining = dur * (1f - u);
                Basis(gYawTo, out Vector3 r, out Vector3 up, out _);
                if (RunnerOutside(v * (remaining + 0.3f), gTo + emOff, r, up, gOrthoTo * mBoostMul, P)) Retarget(P, v, s, vel);
            }
            if (u >= 1f)
            {
                mFocus = gTo; mOrtho = gOrthoTo; mYaw = gYawTo;
                aState = AState.Hold; aHold = 0f; aCalm = P.postGlideCalm; glideCount++;
            }
        }
        aPrevFocus = mFocus;
    }

    bool PredictViolation(CameraFeelProfile P, float v, float s)
    {
        float a = v * P.lookPredict.Evaluate(s);
        Vector3 focus = mFocus + emOff;
        float ortho = mOrtho * mBoostMul;
        for (int i = 0; i < 3; i++)
            if (OutsideWindow(a * i * 0.5f, focus, mRight, mUp, ortho, P.softFrameLimit, true, P)) return true;
        return false;
    }

    bool RunnerOutside(float arc, Vector3 focus, Vector3 r, Vector3 u, float ortho, CameraFeelProfile P)
    {
        Vector3 d = RoutePointAt(arc) - focus;
        float x = Vector3.Dot(d, r), y = Vector3.Dot(d, u);
        return Mathf.Abs(x) > P.runnerSafeX * ortho * ModeAspect() || y < P.runnerSafeYMin * ortho || y > Mathf.Min(P.runnerSafeYMax, TopLimit(P)) * ortho;
    }

    void StartGlide(float yawT, CameraFeelProfile P, float v, float s)
    {
        if (!PlanShot(yawT, v, s, P, out Vector3 f, out float o)) { aReplanCd = 0.3f; return; }

        // fold any emergency offset into the shot, so the glide starts from the pose that is on screen
        mFocus += emOff; emOff = Vector3.zero; emVel = Vector3.zero;

        float d =Vector3.ProjectOnPlane(f - mFocus, mFwd).magnitude;
        float quarters = Mathf.Abs(Mathf.DeltaAngle(mYaw, yawT)) / 90f;
        bool yawChange = quarters > 0.011f;
        if (!yawChange && d < P.skipGlideDistance && Mathf.Abs(o - mOrtho) / Mathf.Max(0.01f, mOrtho) < P.skipGlideZoomPct)
        {
            aReplanCd = 0.2f;
            return;
        }

        float dur = Mathf.Max(P.minGlide.Evaluate(s), d / (P.maxGlideSpeedFactor * v));
        if (yawChange) dur = Mathf.Max(dur, P.rotationMinSeconds + quarters * P.secondsPer90.Evaluate(s));

        gFrom = mFocus; gTo = f; gYawFrom = mYaw; gYawTo = yawT; gOrthoFrom = mOrtho; gOrthoTo = o;
        gM0 = 0f; aGlideDur = dur; aGlideT = 0f; aRetargets = 0; aRetargetCd = 0.25f;
        aState = AState.Glide;
        if (P.logDecisions) LogGlide(P, v, s, d, dur);
    }

    void LogGlide(CameraFeelProfile P, float v, float s, float d, float dur)
    {
        Basis(gYawFrom, out Vector3 r0, out Vector3 u0, out _);
        Basis(gYawTo, out Vector3 r1, out Vector3 u1, out _);
        float ho = gOrthoFrom * mBoostMul, hn = gOrthoTo * mBoostMul, asp = ModeAspect();
        Vector3 run0 = RoutePointAt(0f), runE = RoutePointAt(v * dur);
        Vector3 a0 = run0 - gFrom, a1 = run0 - gTo, a2 = runE - gTo;
        float firstBad = -1f; string why = "";
        for (float t = dur; t <= dur + lastPlanHold + 0.5f; t += 0.1f)
        {
            float arc = v * t;
            if (RunnerOutside(arc, gTo, r1, u1, hn, P)) { firstBad = t; why = "runner"; break; }
            if (OutsideWindow(arc, gTo, r1, u1, hn, P.softFrameLimit, true, P)) { firstBad = t; why = "union"; break; }
        }
        int midBad = 0, midN = 0;
        for (float t = 0f; t <= dur; t += dur / 10f)
        {
            float uu = Mathf.Clamp01(t / dur), hh2 = uu * uu * (3f - 2f * uu);
            if (RunnerOutside(v * t, Vector3.Lerp(gFrom, gTo, hh2), r1, u1, Mathf.Lerp(gOrthoFrom, gOrthoTo, hh2) * mBoostMul, P)) midBad++;
            midN++;
        }
        Debug.Log("[CAM A] glide runner outside safe box in " + midBad + "/" + midN + " samples");
        Debug.Log("[CAM A] planCheck firstViolation t=" + firstBad.ToString("F1") + " (" + why + ") planHold=" + lastPlanHold.ToString("F2"));
        Debug.Log("[CAM A] glide #" + (glideCount + 1) + " v=" + v.ToString("F1") + " s=" + s.ToString("F2") + " hold(plan)=" + lastPlanHold.ToString("F2") + " ortho " + gOrthoFrom.ToString("F1") + "->" + gOrthoTo.ToString("F1") + " dist=" + d.ToString("F1") + " dur=" + dur.ToString("F2")
            + " | runner rel(old pose) x=" + (Vector3.Dot(a0, r0) / (ho * asp)).ToString("F2") + " y=" + (Vector3.Dot(a0, u0) / ho).ToString("F2")
            + " | rel(new pose) now x=" + (Vector3.Dot(a1, r1) / (hn * asp)).ToString("F2") + " y=" + (Vector3.Dot(a1, u1) / hn).ToString("F2")
            + " at end x=" + (Vector3.Dot(a2, r1) / (hn * asp)).ToString("F2") + " y=" + (Vector3.Dot(a2, u1) / hn).ToString("F2") + " holdSoFar=" + aHold.ToString("F2"));
    }

    // The planned end of the glide no longer holds the runner: aim at a fresh shot, carrying the current velocity (no restart from rest).
    void Retarget(CameraFeelProfile P, float v, float s, Vector3 vel)
    {
        if (!PlanShot(gYawTo, v, s, P, out Vector3 f, out float o)) return;
        if (Vector3.ProjectOnPlane(f - gTo, mFwd).magnitude < 1f && Mathf.Abs(o - gOrthoTo) < 0.3f) return;

        aRetargets++;
        Vector3 from = mFocus;
        float dist = Vector3.ProjectOnPlane(f - from, mFwd).magnitude;
        float dur = Mathf.Max(0.8f, dist / (P.maxGlideSpeedFactor * v));
        float m0 = 0f;
        if (dist > 0.05f) m0 = Mathf.Clamp(Mathf.Max(0f, Vector3.Dot(vel, (f - from).normalized)) * dur / dist, 0f, 3f);

        gFrom = from; gTo = f; gOrthoFrom = mOrtho; gOrthoTo = o; gYawFrom = mYaw;
        gM0 = m0; aGlideDur = dur; aGlideT = 0f;
        glideCount += 0;
    }

    // ------------------------------------------------------------------
    // Planning: the shot that can be held longest.
    // ------------------------------------------------------------------
    // Simulates the runner along the route (it IS the route, so each route point is a runner position); the
    // union of required points grows as the runner advances - P0 (runner), P1 (to the next corner), P2 (first half
    // of the leg after it). The shot ends where the union stops fitting the soft window at that ortho.
    float lastPlanHold;
    bool PlanShot(float yaw, float v, float s, CameraFeelProfile P, out Vector3 focusOut, out float orthoOut)
    {
        focusOut = mFocus; orthoOut = mOrtho; lastPlanHold = 0f;
        var g = pathGen;
        int n = g.RouteCount;
        if (n < 2) return false;

        Basis(yaw, out Vector3 r, out Vector3 u, out Vector3 f);
        for (int i = 0; i < n; i++) { scrX[i] = Vector3.Dot(g.RoutePts[i], r); scrY[i] = Vector3.Dot(g.RoutePts[i], u); }

        int k = 0;
        for (int i = 0; i < n; i++)
        {
            while (k < g.RouteCornerCount && g.RouteCornerIdx[k] <= i) k++;
            if (k < g.RouteCornerCount)
            {
                int c = g.RouteCornerIdx[k];
                int nxt = k + 1 < g.RouteCornerCount ? g.RouteCornerIdx[k + 1] : n;
                endIdx[i] = Mathf.Min(n - 1, c + Mathf.CeilToInt((nxt - c) * 0.5f));
            }
            else endIdx[i] = n - 1;
        }

        float cap = Mathf.Max(P.zoomFloor, P.zoomCap.Evaluate(s));
        float o0 = Mathf.Clamp(mOrtho, P.zoomFloor, cap);
        float boost = g.PlayerOnZigZagBoost ? Mathf.Max(mBoostMul, P.boostZoomMultiplier) : mBoostMul;
        float pad = P.mustSeePadding;
        float aspect = ModeAspect();
        const float M = 0.03f;   // planning margin (half-screens): shots are planned slightly inside the limits so the runner is never put exactly on an edge
        float soft = P.softFrameLimit - M, top = Mathf.Min(soft, TopLimit(P) - M);
        float maxArc = v * P.maxPlanSeconds;

        // The shot only has to hold from the moment the glide ENDS: the runner is not required to be inside the final
        // frame while the camera is still travelling, so planning starts at the runner position reached after the glide.
        int iS = 0;
        float startArc = v * P.minGlide.Evaluate(s) * P.planFromGlideEndShare;
        while (iS < n - 2 && g.RouteArcs[iS] < startArc) iS++;

        // prefix boxes of the route points (independent of the zoom): the runner's path and the union both grow with them
        float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue;
        for (int j = iS; j < n; j++)
        {
            a0 = Mathf.Min(a0, scrX[j]); a1 = Mathf.Max(a1, scrX[j]); b0 = Mathf.Min(b0, scrY[j]); b1 = Mathf.Max(b1, scrY[j]);
            pX0[j] = a0; pX1[j] = a1; pY0[j] = b0; pY1[j] = b1;
        }

        float bestO = -1f, bestHold = -1f;
        int bestLast = 0, bestUnion = 0;
        for (int ci = -2; ci <= 2; ci++)
        {
            float o = Mathf.Clamp(o0 + ci, P.zoomFloor, cap);
            float eff = o * boost, hwE = eff * aspect, hhE = eff;
            float rSx = (P.runnerSafeX - M) * hwE, rYlo = (P.runnerSafeYMin + M) * hhE, rYhi = (Mathf.Min(P.runnerSafeYMax, TopLimit(P)) - M) * hhE;

            // A shot is good for runner position i if ONE focus keeps the runner's path 0..i inside its safe box
            // AND the union (route 0..endIdx[i], padded) inside the soft window - on both axes.
            int last = -1;
            for (int i = iS; i < n && g.RouteArcs[i] <= maxArc; i++)
            {
                int e = endIdx[i];
                float loX = Mathf.Max(pX1[i] - rSx, pX1[e] + pad - soft * hwE), hiX = Mathf.Min(pX0[i] + rSx, pX0[e] - pad + soft * hwE);
                float loY = Mathf.Max(pY1[i] - rYhi, pY1[e] + pad - top * hhE), hiY = Mathf.Min(pY0[i] - rYlo, pY0[e] - pad + soft * hhE);
                if (loX > hiX || loY > hiY) break;
                last = i;
            }
            float hold = last >= 0 ? (g.RouteArcs[last] - g.RouteArcs[iS]) / v : 0f;   // time the shot holds AFTER the glide
            int ls = Mathf.Max(iS, last);
            if (bestO < 0f || hold > bestHold + 0.01f) { bestO = o; bestHold = hold; bestLast = ls; bestUnion = endIdx[ls]; }
            if (bestHold >= P.targetHold) break;
        }
        if (bestO < 0f) return false;
        lastPlanHold = bestHold;

        // ---- focus: satisfy the runner's safe box (P0) first, then the soft window for the union, runner on the trailing side ----
        float eo = bestO * boost, hw = eo * aspect, hh = eo;
        float rx0 = float.MaxValue, rx1 = float.MinValue, ry0 = float.MaxValue, ry1 = float.MinValue;
        for (int j = iS; j <= bestLast; j++)
        {
            rx0 = Mathf.Min(rx0, scrX[j]); rx1 = Mathf.Max(rx1, scrX[j]);
            ry0 = Mathf.Min(ry0, scrY[j]); ry1 = Mathf.Max(ry1, scrY[j]);
        }
        float ux0 = float.MaxValue, ux1 = float.MinValue, uy0 = float.MaxValue, uy1 = float.MinValue;
        for (int j = iS; j <= bestUnion && j < n; j++)
        {
            ux0 = Mathf.Min(ux0, scrX[j]); ux1 = Mathf.Max(ux1, scrX[j]);
            uy0 = Mathf.Min(uy0, scrY[j]); uy1 = Mathf.Max(uy1, scrY[j]);
        }
        ux0 -= pad; ux1 += pad; uy0 -= pad; uy1 += pad;

        float sxs = (P.runnerSafeX - M) * hw, ylo = (P.runnerSafeYMin + M) * hh, yhi = (Mathf.Min(P.runnerSafeYMax, TopLimit(P)) - M) * hh;
        float fx = SolveFocusAxis(rx0, rx1, -sxs, sxs, ux0, ux1, -soft * hw, soft * hw, Mathf.Sign((ux0 + ux1) * 0.5f - scrX[iS]), P.trailingOffsetMax * hw);
        float fy = SolveFocusAxis(ry0, ry1, ylo, yhi, uy0, uy1, -soft * hh, top * hh, Mathf.Sign((uy0 + uy1) * 0.5f - scrY[iS]), P.trailingOffsetMax * hh);

        float depth = Vector3.Dot(modeRunner, f);
        focusOut = r * fx + u * fy + f * depth;
        orthoOut = bestO;
        return true;
    }

    // One axis of the focus. The runner's path [r0, r1] must stay inside [rLo, rHi] relative to the focus (priority),
    // the union [u0, u1] inside [wLo, wHi]. dir = which way the route goes; the runner is put on the trailing side,
    // by at most `trailMax` from the middle of the allowed range.
    static float SolveFocusAxis(float r0, float r1, float rLo, float rHi, float u0, float u1, float wLo, float wHi, float dir, float trailMax)
    {
        float fLoR = r1 - rHi, fHiR = r0 - rLo;      // focus range keeping the runner in its box
        float fLoU = u1 - wHi, fHiU = u0 - wLo;      // focus range keeping the union in the window
        float lo = Mathf.Max(fLoR, fLoU), hi = Mathf.Min(fHiR, fHiU);
        if (lo > hi)
        {
            // cannot satisfy both: the runner wins, the union gets what is left
            lo = fLoR; hi = fHiR;
            if (lo > hi) return (lo + hi) * 0.5f;
            float want = Mathf.Clamp((fLoU + fHiU) * 0.5f, lo, hi);
            return want;
        }
        float mid = (lo + hi) * 0.5f;
        return mid + dir * Mathf.Min((hi - lo) * 0.5f, trailMax);
    }
}
