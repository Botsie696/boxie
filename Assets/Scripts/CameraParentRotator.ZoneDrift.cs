using UnityEngine;

/// <summary>
/// MODE E_ZONEDRIFT: zone to zone, the best viewpoint for each, with a gentle drift inside a shot.
///
/// Zones are grouped by the CAMERA (the generator is untouched): zoneLegs legs each, grown up to zoneMaxLegs
/// so a shot lasts at least zoneMinSeconds at the current speed. Each shot is solved once its legs and the
/// first leg after it exist: 4 iso yaws x (focus = centre of the screen box of the shot's legs + the next leg),
/// scored for corners in frame, visible turn faces, occlusion, cubes running at the camera, thumb zones and
/// vertical legs; a Viterbi pass over the shots ahead adds the cost of each yaw change and pan (and a small
/// variety bonus), as PlanCameraYaws does for C. A shot is locked when the move to it starts.
///
/// Inside a shot: target = lerp(anchor + lead room, RailPoint(railLeadE), followWeight), through the focus spring.
/// The anchor moves only during a transition (SmoothStep, yaw and pan in ONE move, started at
/// transitionStartFrac of the shot). Never two shot changes within 3 s.
/// </summary>
public partial class CameraParentRotator
{
    const int EShotMax = 12;
    const int ELegBuf = 8;
    const int EOccMax = 900;
    const float EMinShotGap = 3.0f;

    int eCount, eNextId;
    readonly int[] eFirst = new int[EShotMax], eLast = new int[EShotMax], eId = new int[EShotMax];
    readonly bool[] eSolved = new bool[EShotMax], eLocked = new bool[EShotMax], eRotAllowed = new bool[EShotMax];
    readonly float[] eForced = new float[EShotMax], eYaw = new float[EShotMax];
    readonly Vector3[] eFocusSel = new Vector3[EShotMax];
    readonly float[] eUnary = new float[EShotMax * 4], eCost = new float[EShotMax * 4];
    readonly int[] eBack = new int[EShotMax * 4];
    readonly Vector3[] eCandFocus = new Vector3[EShotMax * 4];
    readonly DynamicPathGenerator.Segment[] eLegs = new DynamicPathGenerator.Segment[ELegBuf];
    readonly Vector3[] eOcc = new Vector3[EOccMax];
    int eOccN;

    Vector3 eAnchor; int eAnchorId = -1; bool eAnchorInit;
    bool eTrans, eYawChange;
    Vector3 eFromF, eToF;
    float eFromYaw, eToYaw, eU, eDur, eDeg;
    int eTargetId = -1;
    float eLastChangeTime = -100f, ePrevShotYaw;
    int eShotChanges;

    /// <summary>Shot changes (transitions started) in mode E since it was entered.</summary>
    public int ModeShotChanges => eShotChanges;

    void ResetZoneDrift()
    {
        eCount = 0; eAnchorInit = false; eAnchorId = -1; eTargetId = -1;
        eTrans = false; eYawChange = false; eLastChangeTime = -100f;
    }

    float CandYaw(int k) => Mathf.Repeat(isoYawOffset + 90f * k, 360f);

    int ShotIndexOfId(int id)
    {
        for (int i = 0; i < eCount; i++) if (eId[i] == id) return i;
        return -1;
    }

    int ShotIndexOfSerial(int serial)
    {
        for (int i = 0; i < eCount; i++) if (serial >= eFirst[i] && serial <= eLast[i]) return i;
        return -1;
    }

    // ------------------------------------------------------------------
    void TickZoneDrift(float dt, CameraFeelProfile P)
    {
        DynamicPathGenerator.Segment ps = pathGen.PlayerSegment;
        int rs = ps != null ? ps.serial : -1;
        if (rs >= 0) EUpdateShots(rs, P);
        int cur = rs >= 0 ? ShotIndexOfSerial(rs) : -1;

        if (cur >= 0 && !eAnchorInit)
        {
            eAnchorInit = true;
            eAnchor = mFocus; eAnchorId = eId[cur];
            eLocked[cur] = true; eYaw[cur] = mYaw; eFocusSel[cur] = mFocus;
            ePrevShotYaw = mYaw;
        }
        EPlan(P);

        // ---- start the move to the next shot ----
        float progress = 0f;
        if (cur >= 0)
        {
            float sA = 0f, sB = 1f;
            if (pathGen.CentreLegRange(eFirst[cur], out float a0, out _)) sA = a0;
            if (pathGen.CentreLegRange(eLast[cur], out _, out float b1)) sB = b1;
            progress = sB > sA ? Mathf.Clamp01((rlS - sA) / (sB - sA)) : 0f;
            if (!eTrans && eAnchorInit && Time.time - eLastChangeTime >= EMinShotGap)
            {
                int to = -1;
                bool nextReady = cur + 1 < eCount && eSolved[cur + 1];
                if (eId[cur] != eAnchorId) to = progress >= P.transitionStartFrac && nextReady ? cur + 1 : cur;
                else if (progress >= P.transitionStartFrac && nextReady) to = cur + 1;
                if (to >= 0 && (eSolved[to] || eLocked[to])) EStartTransition(to, P);
            }
        }

        // ---- the move (yaw and pan together) ----
        RailBudget(dt, P, eTrans && eYawChange ? SmoothStepRate(eDeg, eDur, eU) : 0f);
        if (eTrans)
        {
            eU = Mathf.Clamp01(eU + dt / eDur * (eYawChange ? rBudgetShare : 1f));
            float k = Mathf.SmoothStep(0f, 1f, eU);
            eAnchor = Vector3.LerpUnclamped(eFromF, eToF, k);
            if (eYawChange) mYaw = Mathf.LerpAngle(eFromYaw, eToYaw, k);
            if (eU >= 1f)
            {
                eTrans = false; eAnchorId = eTargetId;
                if (eYawChange) mYaw = eToYaw;
                eYawChange = false;
            }
        }

        // ---- ambient sway toward the next shot's yaw ----
        float leanYaw = mYaw; bool leanOk = false;
        if (!eTrans && cur >= 0 && cur + 1 < eCount && eSolved[cur + 1] && eRotAllowed[cur + 1] && eId[cur] == eAnchorId)
        { leanYaw = eYaw[cur + 1]; leanOk = !RailRotationBlocked(pathGen.PlayerZone, P); }
        RailLean(dt, P, leanYaw, progress / Mathf.Max(0.05f, P.transitionStartFrac), leanOk);
        Basis(mYaw + mLean, out mRight, out mUp, out mFwd);

        // ---- focus ----
        float ortho = mOrtho * mBoostMul, hh = ortho, hw = ortho * ModeAspect();
        Vector3 room = LeadRoomWorld(dt, P, hw, hh);
        Vector3 anchor = eAnchorInit ? eAnchor : RailRunnerPoint();
        Vector3 target = Vector3.LerpUnclamped(anchor + room, RailPoint(P.railLeadE, P), P.followWeight);
        target = EKeepRunnerSafe(SoftSlide(target, dt, P, 0.6f), hw, hh, P);
        if (!spInit) SpringSeed(mFocus, target);
        // A zone door glides (the target only partly follows the runner, so the jump test below can miss it).
        if (GlideThisTeleport()) TeleportGlideStart(target);
        else if (rlHavePrevTarget && (target - rlPrevTarget).sqrMagnitude > P.railTeleportDistance * P.railTeleportDistance * 16f) SpringSeed(mFocus, target);
        rlPrevTarget = target; rlHavePrevTarget = true;
        mFocus = RunnerSafetyClamp(SpringStep(target, dt, P), P);
        if (smoothTeleportGlide) mFocus = TeleportGlideApply(mFocus, dt);

        RailZoom(dt, P, P.railZoomFloorE, mFocus + emOff);
    }

    // The runner (on the rail) stays inside its safe box and above the thumb zones relative to the target.
    Vector3 EKeepRunnerSafe(Vector3 target, float hw, float hh, CameraFeelProfile P)
    {
        Vector3 d = RailRunnerPoint() - target;
        float x = Vector3.Dot(d, mRight) / hw, y = Vector3.Dot(d, mUp) / hh;
        float sx = P.runnerSafeX - 0.05f;
        float yLo = Mathf.Max(P.runnerSafeYMin + 0.05f, fThumbY + 0.04f), yHi = Mathf.Min(P.runnerSafeYMax, fHud) - 0.05f;
        float cx = Mathf.Clamp(x, -sx, sx), cy = yLo < yHi ? Mathf.Clamp(y, yLo, yHi) : y;
        return target + mRight * ((x - cx) * hw) + mUp * ((y - cy) * hh);
    }

    void EStartTransition(int i, CameraFeelProfile P)
    {
        float toYaw = eYaw[i];
        Vector3 toF = eFocusSel[i];
        bool yawChange = Mathf.Abs(Mathf.DeltaAngle(mYaw, toYaw)) > 1f;
        if (yawChange && RailRotationBlocked(pathGen.PlayerZone, P))
        {
            // no rotation now (boost / bridge / power-up close): same yaw, that yaw's framing of the shot
            int k = NearestCand(mYaw);
            toYaw = mYaw; toF = eCandFocus[i * 4 + k];
            yawChange = false;
        }
        if (!yawChange) toYaw = mYaw;
        eFromF = eAnchor; eToF = toF;
        eYawChange = yawChange;
        if (yawChange) { eFromYaw = mYaw + mLean; mLean = 0f; mLeanVel = 0f; }
        else eFromYaw = mYaw;
        eToYaw = toYaw;
        eDeg = Mathf.Abs(Mathf.DeltaAngle(eFromYaw, eToYaw));
        eDur = Mathf.Max(0.3f, P.transitionDur.Evaluate(rlS01));
        if (yawChange) eDur = Mathf.Max(eDur, RotationSeconds(eDeg, P));
        eU = 0f; eTrans = true; eTargetId = eId[i];
        eLocked[i] = true; eYaw[i] = toYaw; eFocusSel[i] = toF;
        ePrevShotYaw = mYaw;
        eShotChanges++; eLastChangeTime = Time.time;
        if (P.logDecisions) Debug.Log("[CAM E] shot " + eId[i] + " legs " + eFirst[i] + "-" + eLast[i] + " yaw " + eFromYaw.ToString("F0") + " -> " + eToYaw.ToString("F0") + " pan " + Vector3.Distance(eFromF, eToF).ToString("F1") + " u over " + eDur.ToString("F2") + " s");
    }

    int NearestCand(float yaw)
    {
        int best = 0; float bd = float.MaxValue;
        for (int k = 0; k < 4; k++) { float d = Mathf.Abs(Mathf.DeltaAngle(yaw, CandYaw(k))); if (d < bd) { bd = d; best = k; } }
        return best;
    }

    // ------------------------------------------------------------------
    // Shots: prune behind, append sealed groups ahead, solve one per frame.
    // ------------------------------------------------------------------
    void EUpdateShots(int rs, CameraFeelProfile P)
    {
        if (eCount > 0 && (rs < eFirst[0] || rs > eLast[eCount - 1] + 40)) ResetZoneDrift();   // regenerated path
        while (eCount > 1 && eLast[0] < rs) RemoveShot0();

        int latest = pathGen.LatestSerial;
        float v = Mathf.Max(5f, rlSpeed);
        while (eCount < EShotMax)
        {
            int first = eCount == 0 ? rs : eLast[eCount - 1] + 1;
            int legs = 0, last = first - 1;
            float dur = 0f;
            bool sealedShot = false;
            while (legs < P.zoneMaxLegs)
            {
                int sn = first + legs;
                if (sn > latest || !pathGen.CentreLegRange(sn, out float s0, out float s1)) break;
                float len = pathGen.CentreLegRange(sn + 1, out float n0, out _) ? n0 - s0 : s1 - s0 + 1.4f;
                dur += len / v; legs++; last = sn;
                if (legs >= Mathf.Max(1, P.zoneLegs) && dur >= P.zoneMinSeconds) { sealedShot = true; break; }
            }
            if (legs >= Mathf.Max(1, P.zoneMaxLegs)) sealedShot = true;
            if (!sealedShot) break;
            int i = eCount++;
            eFirst[i] = first; eLast[i] = last; eId[i] = eNextId++;
            eSolved[i] = false; eLocked[i] = false; eRotAllowed[i] = true; eForced[i] = -1f; eYaw[i] = mYaw; eFocusSel[i] = mFocus;
        }

        for (int i = 0; i < eCount; i++)
            if (!eSolved[i] && !eLocked[i]) { ESolveShot(i, P); break; }   // one solve per frame
    }

    void RemoveShot0()
    {
        for (int i = 1; i < eCount; i++)
        {
            eFirst[i - 1] = eFirst[i]; eLast[i - 1] = eLast[i]; eId[i - 1] = eId[i];
            eSolved[i - 1] = eSolved[i]; eLocked[i - 1] = eLocked[i]; eRotAllowed[i - 1] = eRotAllowed[i];
            eForced[i - 1] = eForced[i]; eYaw[i - 1] = eYaw[i]; eFocusSel[i - 1] = eFocusSel[i];
            for (int k = 0; k < 4; k++) { eUnary[(i - 1) * 4 + k] = eUnary[i * 4 + k]; eCandFocus[(i - 1) * 4 + k] = eCandFocus[i * 4 + k]; }
        }
        eCount--;
    }

    void ESolveShot(int i, CameraFeelProfile P)
    {
        int L = 0;
        for (int sn = eFirst[i]; sn <= eLast[i] + 1; sn++)
        {
            if (L >= ELegBuf) break;
            DynamicPathGenerator.Segment sg = pathGen.SegmentBySerial(sn);
            if (sg == null) return;            // the first leg after the shot is not laid yet
            eLegs[L++] = sg;
        }
        int shotLegs = L - 1;

        float forced = -1f;
        for (int j = 0; j < shotLegs; j++)
            if (eLegs[j].zone != null && eLegs[j].zone.forcedYaw >= 0f) { forced = eLegs[j].zone.forcedYaw; break; }
        bool rot = eLegs[0].zone == null || eLegs[0].zone.yawChangeAllowed;
        for (int sn = eFirst[i] - 2; sn <= eFirst[i] + 1; sn++)
        {
            DynamicPathGenerator.Segment sg = pathGen.SegmentBySerial(sn);
            if (sg != null && ((sg.boost && sg.portalRole == 0) || sg.plankCount > 0)) rot = false;
        }
        if (forced >= 0f) rot = true;
        eForced[i] = forced; eRotAllowed[i] = rot;

        // nearby cubes, for occlusion
        eOccN = 0;
        for (int sn = eFirst[i] - 1; sn <= eLast[i] + 2; sn++)
        {
            DynamicPathGenerator.Segment sg = pathGen.SegmentBySerial(sn);
            if (sg == null) continue;
            for (int c = 0; c < sg.cubes.Count && eOccN < EOccMax; c++)
                if (sg.cubes[c] != null) eOcc[eOccN++] = sg.cubes[c].transform.position;
        }

        float sA = 0f, sB = 0f;
        pathGen.CentreLegRange(eFirst[i], out sA, out _);
        pathGen.CentreLegRange(eLast[i], out _, out sB);
        float aspect = ModeAspect(), soft = P.softFrameLimit, top = Mathf.Min(soft, fHud), pad = P.mustSeePadding;
        float cap = Mathf.Max(P.railZoomFloorE, P.zoomCap.Evaluate(rlS01));

        for (int k = 0; k < 4; k++)
        {
            float yaw = CandYaw(k);
            Basis(yaw, out Vector3 r, out Vector3 u, out Vector3 f);
            if (forced >= 0f && Mathf.Abs(Mathf.DeltaAngle(yaw, forced)) > 1f) { eUnary[i * 4 + k] = -100000f; eCandFocus[i * 4 + k] = eLegs[0].startPosition; continue; }

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            Vector3 mean = Vector3.zero;
            for (int j = 0; j < L; j++)
            {
                Accum2(eLegs[j].startPosition, r, u, pad, ref minX, ref maxX, ref minY, ref maxY);
                Accum2(eLegs[j].endPosition, r, u, pad, ref minX, ref maxX, ref minY, ref maxY);
                mean += eLegs[j].startPosition + eLegs[j].endPosition;
            }
            mean /= 2f * L;
            float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
            Vector3 focus = r * cx + u * cy + f * Vector3.Dot(mean, f);
            float H = Mathf.Clamp(Mathf.Max((maxY - minY) * 0.5f / Mathf.Max(0.05f, top), (maxX - minX) * 0.5f / Mathf.Max(0.05f, soft * aspect)), P.railZoomFloorE, cap);
            float hw = H * aspect;

            float score = 0f;
            for (int j = 1; j < L; j++)
            {
                if (Vector3.Dot(eLegs[j].direction, eLegs[j - 1].direction) >= 0.9f) continue;
                Vector3 c = eLegs[j].startPosition, d = c - focus;
                float x = Vector3.Dot(d, r), y = Vector3.Dot(d, u);
                if (Mathf.Abs(x) <= soft * hw && y >= -soft * H && y <= top * H) score += P.scoreCornerInFrame;
                if (Vector3.Dot(eLegs[j].direction, -f) > 0.15f) score += P.scoreTurnFaceVisible;
                if (InThumb(x / hw, y / H)) score += P.scoreThumb;
                if (EOccluded(c, r, u, f)) score += P.scoreOccluded;
            }
            for (int q = 0; q < 10; q++)
            {
                Vector3 p = pathGen.CentrePointAt(Mathf.Lerp(sA, sB, (q + 0.5f) / 10f)), d = p - focus;
                if (InThumb(Vector3.Dot(d, r) / hw, Vector3.Dot(d, u) / H)) score += P.scoreThumb;
                if (EOccluded(p, r, u, f)) score += P.scoreOccluded;
            }
            for (int j = 0; j < L; j++)
            {
                Vector3 dir = eLegs[j].direction, hdir = new Vector3(dir.x, 0f, dir.z);
                if (j < shotLegs && hdir.sqrMagnitude > 0.01f && Vector3.Dot(hdir.normalized, f) < -0.5f) score += P.scoreApproachCube * eLegs[j].cubes.Count;
                if (Mathf.Abs(Vector3.Dot(dir, u)) > Mathf.Abs(Vector3.Dot(dir, r))) score += P.scorePortraitLeg;
            }
            eUnary[i * 4 + k] = score;
            eCandFocus[i * 4 + k] = focus;
        }
        eSolved[i] = true;
    }

    static void Accum2(Vector3 p, Vector3 r, Vector3 u, float pad, ref float minX, ref float maxX, ref float minY, ref float maxY)
    {
        float x = Vector3.Dot(p, r), y = Vector3.Dot(p, u);
        minX = Mathf.Min(minX, x - pad); maxX = Mathf.Max(maxX, x + pad);
        minY = Mathf.Min(minY, y - pad); maxY = Mathf.Max(maxY, y + pad);
    }

    // A cube nearer to the camera (by more than 2.5 u) covers p on screen (as ScoreYaw measures overlap).
    bool EOccluded(Vector3 p, Vector3 r, Vector3 u, Vector3 f)
    {
        float px = Vector3.Dot(p, r), py = Vector3.Dot(p, u), pz = Vector3.Dot(p, f);
        for (int i = 0; i < eOccN; i++)
        {
            Vector3 q = eOcc[i];
            if (Vector3.Dot(q, f) > pz - 2.5f) continue;
            float dx = Vector3.Dot(q, r) - px, dy = Vector3.Dot(q, u) - py;
            if (dx * dx + dy * dy < 1.44f) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Viterbi over the unlocked solved shots after the one the camera is on (or moving to).
    // Scores: higher = better.
    // ------------------------------------------------------------------
    void EPlan(CameraFeelProfile P)
    {
        int startIdx; float fixYaw; Vector3 fixFocus;
        if (eTrans) { startIdx = ShotIndexOfId(eTargetId); fixYaw = eToYaw; fixFocus = eToF; }
        else { startIdx = ShotIndexOfId(eAnchorId); fixYaw = mYaw; fixFocus = eAnchor; }
        int i0 = startIdx >= 0 ? startIdx + 1 : 0;
        while (i0 < eCount && eLocked[i0]) i0++;
        int n = 0;
        while (i0 + n < eCount && eSolved[i0 + n] && !eLocked[i0 + n]) n++;
        if (n == 0) return;

        for (int m = 0; m < n; m++)
        {
            int i = i0 + m;
            for (int k = 0; k < 4; k++)
            {
                float yk = CandYaw(k);
                Vector3 fk = eCandFocus[i * 4 + k];
                float best = float.MinValue; int bj = -1;
                if (m == 0) best = EPair(fixYaw, fixFocus, ePrevShotYaw, yk, fk, eRotAllowed[i], P);
                else
                {
                    for (int j = 0; j < 4; j++)
                    {
                        int pj = (i - 1) * 4 + j;
                        float two = m == 1 ? fixYaw : CandYaw(eBack[pj] >= 0 ? eBack[pj] : j);
                        float val = eCost[pj] + EPair(CandYaw(j), eCandFocus[pj], two, yk, fk, eRotAllowed[i], P);
                        if (val > best) { best = val; bj = j; }
                    }
                }
                eCost[i * 4 + k] = eUnary[i * 4 + k] + best;
                eBack[i * 4 + k] = bj;
            }
        }
        int last = i0 + n - 1, pick = 0;
        for (int k = 1; k < 4; k++) if (eCost[last * 4 + k] > eCost[last * 4 + pick]) pick = k;
        for (int i = last; i >= i0; i--)
        {
            eYaw[i] = CandYaw(pick);
            eFocusSel[i] = eCandFocus[i * 4 + pick];
            int b = eBack[i * 4 + pick];
            if (b >= 0) pick = b;
        }
    }

    float EPair(float prevYaw, Vector3 prevFocus, float twoBackYaw, float yaw, Vector3 focus, bool rotAllowed, CameraFeelProfile P)
    {
        float q = Mathf.Abs(Mathf.DeltaAngle(prevYaw, yaw)) / 90f;
        float s = -P.scorePanPerUnit * Vector3.Distance(prevFocus, focus);
        if (q >= 0.5f)
        {
            if (!rotAllowed) return s - 100000f;
            s -= q < 1.5f ? P.scoreAngleChangeCost : P.scoreAngleChangeCost * 4f;
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, twoBackYaw)) > 1f) s += P.scoreVariety;
        }
        return s;
    }
}
