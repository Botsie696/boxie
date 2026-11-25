using UnityEngine;

/// <summary>Where a leg sits in its portal cycle (DifficultyProfile, section 6.1).</summary>
public enum WavePhase { Release, Build, Peak, Cooldown }

/// <summary>
/// WAVES (DifficultyProfile.wavePacing). Each portal cycle (legsBetweenPortals, 30 here) is one deliberate
/// wave of tension and release:
///
///   RELEASE  legs 1-5    after the portal: 0-1 tap legs, no hazards; the only place the camera may turn
///   BUILD    legs 6-20   mixed legs; hazards ramp up against the budget, one "featured" hazard per cycle
///   PEAK     legs 21-27  a boost (odd cycles) or a bridge cluster (even cycles), all time-floored
///   COOL     legs 28-30  the portal runway, coins, no hazards
///
/// The opening cycle teaches: a short 5-leg boost at leg 14 with generous floors, then the first decoy,
/// the first bridge and (next cycle) the first multi-plank bridge, each in an easy form. From cycle
/// patternFamiliesFromCycle on, each cycle features a remix family (staircase, peel, climb/dive, plank
/// weave, decoy gauntlet) built from the same pieces. The old "boost every 12-16 legs" is replaced by this
/// schedule. Breathers: 2 legs of cost &lt;= 1 after every boost and bridge cluster, and glide legs.
/// Inert without a profile (or with wavePacing unticked).
/// </summary>
public partial class DynamicPathGenerator
{
    public partial class PathZone
    {
        /// <summary>False = the camera must keep the previous zone's yaw when it moves to this one (wave pacing).</summary>
        public bool yawChangeAllowed = true;
    }

    enum Hazard { Obstacle, Decoy, Bridge, MultiPlank, Mixed, None }
    enum Family { None = -1, Staircase, Peel, ClimbDive, PlankWeave, DecoyGauntlet }

    bool Waves => difficultyProfile != null && difficultyProfile.wavePacing;

    int cycleIndex, cycleStartSerial;
    int breatherFrom = -1, breatherUntil = -1;
    int lastHeavyEndSerial = -100;         // last leg of the latest boost or bridge cluster
    bool taughtDecoy, taughtBridge, taughtMulti;
    bool peakBoostDone, buildBoostDone;
    int buildBoostsDone, pendingBuildBoost;   // build boosts planned this cycle (buildBoostDone stays for the checkpoint save)
    int decoysThisCycle, lastDecoySerial = -100;
    Family family = Family.None;
    int peelSide;
    System.Random waveRng;

    /// <summary>The portal cycle the path is being laid in (0 = the opening, onboarding cycle).</summary>
    public int WaveCycle => cycleIndex;
    /// <summary>The wave phase of the leg the runner is on.</summary>
    public WavePhase PlayerWavePhase => PlayerSegment != null ? PlayerSegment.phase : WavePhase.Release;

    int CyclePos(int serial) => serial - cycleStartSerial;

    WavePhase PhaseAt(int pos)
    {
        var P = difficultyProfile;
        if (P == null) return WavePhase.Release;
        LevelSpec311 L = L311;
        if (L != null) return PhaseAt311(L, pos);   // scene 3.11: the level plan
        WaveShape S = P.ShapeForCycle(cycleIndex);
        if (pos <= S.releaseEnd) return WavePhase.Release;
        if (pos <= S.buildEnd) return WavePhase.Build;
        if (pos <= S.peakEnd) return WavePhase.Peak;
        return WavePhase.Cooldown;
    }

    void ResetWaves()
    {
        waveRng ??= new System.Random();
        cycleIndex = 0;
        cycleStartSerial = 0;
        breatherFrom = breatherUntil = -1;
        lastHeavyEndSerial = -100;
        taughtDecoy = taughtBridge = taughtMulti = false;
        peakBoostDone = buildBoostDone = false;
        buildBoostsDone = 0;
        decoysThisCycle = 0;
        lastDecoySerial = -100;
        family = Family.None;
        ResetBudget();
    }

    // The runway out of a portal starts the next cycle.
    void BeginCycle(int serial)
    {
        cycleIndex++;
        cycleStartSerial = serial;
        peakBoostDone = buildBoostDone = false;
        buildBoostsDone = 0;
        decoysThisCycle = 0;
        var P = difficultyProfile;
        int forcedFamily = P != null ? P.ForcedFamily(cycleIndex) : -1;
        family = forcedFamily >= 0 ? (Family)forcedFamily
               : P != null && P.ShapeForCycle(cycleIndex).families ? (Family)(waveRng.Next(0, 5)) : Family.None;
        peelSide = waveRng.Next(0, 2);
        ResetBudget();
    }

    bool IsBoostCycle
    {
        get
        {
            PeakKind k = difficultyProfile != null ? difficultyProfile.ShapeForCycle(cycleIndex).peakKind : PeakKind.Auto;
            return k == PeakKind.Boost || (k == PeakKind.Auto && cycleIndex % 2 == 1);
        }
    }

    void AssignWaveInfo(Segment seg)
    {
        seg.cycle = cycleIndex;
        seg.cyclePos = CyclePos(seg.serial);
        seg.phase = PhaseAt(seg.cyclePos);
        seg.breather = seg.serial >= breatherFrom && seg.serial < breatherUntil;
        seg.glide = Waves && WaveGlideDue(seg.serial) && !seg.boost && seg.portalRole == 0;
    }

    bool WaveGlideDue(int serial)
    {
        var P = difficultyProfile;
        if (!Waves) return false;
        LevelSpec311 L = L311;
        if (L != null) return GlideDue311(L, serial);   // scene 3.11: planned glides + the leg after every runway
        int pos = CyclePos(serial);
        if (PhaseAt(pos) != WavePhase.Build) return false;
        int[] glides = P.ShapeForCycle(cycleIndex).glideLegs;
        if (glides != null) foreach (int g in glides) if (g == pos) return true;
        return false;
    }

    int GlideCubes(int serial)
    {
        Vector2 d = difficultyProfile.DurationRange(serial);
        return CubesForSeconds(d.y * difficultyProfile.ShapeForCycle(cycleIndex).glideLengthScale, LegSpeed(serial, false));
    }

    Hazard FeaturedHazard()
    {
        if (cycleIndex == 0) return Hazard.None;
        int fixedHazard = difficultyProfile != null ? difficultyProfile.ShapeForCycle(cycleIndex).featured : -1;
        if (fixedHazard >= 0 && fixedHazard <= (int)Hazard.None)
        {
            Hazard fh = (Hazard)fixedHazard;
            if (fh == Hazard.Obstacle && !obstacleSections) fh = Hazard.Mixed;
            if (family != Family.PlankWeave && family != Family.DecoyGauntlet) return fh;
        }
        if (family == Family.PlankWeave) return Hazard.MultiPlank;
        if (family == Family.DecoyGauntlet) return Hazard.Decoy;
        Hazard[] rot = obstacleSections
            ? new[] { Hazard.Obstacle, Hazard.Decoy, Hazard.Bridge, Hazard.MultiPlank, Hazard.Mixed }
            : new[] { Hazard.Decoy, Hazard.Bridge, Hazard.MultiPlank, Hazard.Mixed };
        return rot[(cycleIndex - 1) % rot.Length];
    }

    // How likely a hazard is on a BUILD leg, as a multiple of the scene's own chance.
    float HazardScale(Segment s, Hazard h)
    {
        var P = difficultyProfile;
        if (Is311(s)) return HazardScale311(s, h);   // scene 3.11: build and peak legs of the waves and the finale
        WaveShape S = P.ShapeForCycle(s.cycle);
        if (s.phase != WavePhase.Build || s.cyclePos < S.firstHazardLeg) return 0f;
        float ramp = Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(S.firstHazardLeg, S.buildEnd, s.cyclePos));
        Hazard f = FeaturedHazard();
        if (f == Hazard.None || f == Hazard.Mixed) return ramp;
        bool featured = f == h || (f == Hazard.MultiPlank && h == Hazard.Bridge);
        return ramp * (featured ? P.featuredBoost : P.nonFeaturedScale);
    }

    // ------------------------------------------------------------------ turns

    /// <summary>Most rolls the turn at the end of s may need once the wave and budget caps are applied.</summary>
    int WaveMaxTaps(Segment s, int byTime)
    {
        if (!Waves) return byTime;
        var P = difficultyProfile;
        int m = byTime;
        if (s.boost && s.portalRole == 0) return m;              // scheduled: only the time floor applies
        if (s.phase == WavePhase.Release || s.glide || s.breather) m = Mathf.Min(m, 1);

        int fixedCost = LegCost(s) - Mathf.Max(0, s.taps);       // everything but the taps
        m = Mathf.Min(m, P.LegCostCapForCycle(s.cycle) - fixedCost);
        if (Is311(s) && Plan311(s).exhale) m = Mathf.Min(m, 2 - fixedCost);   // scene 3.11: exhale legs cost <= 2
        Segment prev = SegmentBefore(s);
        int limit = CostLimitAfter(prev, s);
        if (limit != int.MaxValue) m = Mathf.Min(m, limit - fixedCost);
        if (s.breather) m = Mathf.Min(m, 1 - fixedCost);
        return Mathf.Max(0, m);
    }

    /// <summary>The most leg s may cost because of the leg before it (the hard-leg rule; 3.11: the cap-leg rule).</summary>
    int CostLimitAfter(Segment prev, Segment s)
    {
        if (Is311(s)) return CostLimitAfter311(prev);
        var P = difficultyProfile;
        return prev != null && prev.cost >= P.hardLegCost ? P.nextLegCostAfterHard : int.MaxValue;
    }

    Segment SegmentBefore(Segment s)
    {
        int i = allSegments.IndexOf(s);
        return i > 0 ? allSegments[i - 1] : null;
    }

    // Late-game families steer the turn odds for the BUILD phase only; the authored reroll runs everywhere else.
    bool ApplyFamilyWeights(out Vector4 saved)
    {
        saved = new Vector4(leftWeight, rightWeight, upWeight, downWeight);
        Family fam = family;
        bool on = Waves && family != Family.None && PhaseAt(CyclePos(segmentSerial)) == WavePhase.Build;
        int f311 = Waves ? Family311() : -1;                     // scene 3.11: a finale's family, whatever the phase
        if (f311 >= 0) { fam = (Family)f311; on = true; }
        if (!on) return false;
        switch (fam)
        {
            case Family.Staircase: leftWeight = 4; rightWeight = 4; upWeight = 0; downWeight = 0; return true;
            case Family.Peel:
                leftWeight = peelSide == 0 ? 5 : 1; rightWeight = peelSide == 0 ? 1 : 5; upWeight = 1; downWeight = 1; return true;
            case Family.ClimbDive: leftWeight = 1; rightWeight = 1; upWeight = 3; downWeight = 3; return true;
        }
        return false;
    }

    void RestoreWeights(Vector4 w)
    {
        leftWeight = (int)w.x; rightWeight = (int)w.y; upWeight = (int)w.z; downWeight = (int)w.w;
    }

    // ------------------------------------------------------------------ boosts

    /// <summary>Is a boost due on the leg about to be laid, and how many legs long. Replaces the 12-16 leg timer.</summary>
    bool WaveBoostDue(out int legs, out bool peak)
    {
        legs = 0;
        peak = false;
        var P = difficultyProfile;
        int pos = CyclePos(segmentSerial);
        if (NoSpecialsHere) return false;                           // 3.11: the first level has no boost
        if (segmentSerial < lastHeavyEndSerial + 7) return false;   // never within 6 legs of another boost or cluster
        if (BoostBlocked311()) return false;                        // 3.11: nor right after an obstacle runway
        if (SpecialsOn && L311 != null) return SpecialDue(SpecialBoost, out legs) && FitBoostBeforeExhale311(ref legs);   // 3.11 production plan
        if (SpecialsOn) return SpecialDue(SpecialBoost, out legs);   // 3.11: the level's own plan (DynamicPathGeneratorSpecials.cs)

        WaveShape S = P.ShapeForCycle(cycleIndex);
        int doneBuild = Mathf.Max(buildBoostsDone, buildBoostDone ? 1 : 0);
        if (cycleIndex == 0)
        {
            if (!buildBoostDone && pos >= S.onboardingBoost.x && pos <= S.onboardingBoost.x + 1) legs = S.onboardingBoost.y;
        }
        else
        {
            int peakStart = S.buildEnd + 1;
            if (IsBoostCycle && !peakBoostDone && pos >= peakStart && pos <= peakStart + 1)
            {
                legs = Mathf.Min(S.peakBoostLegs, S.peakEnd - pos + 1);
                peak = true;
            }
            else if (S.buildBoosts != null)
            {
                // A build boost whose window slipped by (no room, too close to the last boost) is dropped, not waited for.
                int idx = doneBuild;
                while (idx < S.buildBoosts.Length && pos > S.buildBoosts[idx].x + 1) idx++;
                if (idx < S.buildBoosts.Length && pos >= S.buildBoosts[idx].x)
                {
                    legs = S.buildBoosts[idx].y;
                    pendingBuildBoost = idx;
                }
            }
        }
        return legs >= 2;
    }

    void OnWaveBoostPlanned(bool peak)
    {
        if (SpecialsOn) MarkSpecialDone();
        if (peak) { peakBoostDone = true; return; }
        buildBoostDone = true;
        buildBoostsDone = cycleIndex == 0 ? 1 : pendingBuildBoost + 1;
    }

    void OnBoostFinished()
    {
        lastHeavyEndSerial = segmentSerial;
        breatherFrom = segmentSerial + 1;
        breatherUntil = segmentSerial + 3;
    }

    // ------------------------------------------------------------------ bridges

    /// <summary>
    /// How many planks the leg about to be laid should get (0 = no bridge), and whether it is a teaching
    /// bridge (adjacent faces, generous lead-in). The leg's facts are not on a Segment yet, so they are
    /// worked out from segmentSerial.
    /// </summary>
    int WaveBridgePlanks(out bool teaching)
    {
        teaching = false;
        var P = difficultyProfile;
        int pos = CyclePos(segmentSerial);
        WavePhase phase = PhaseAt(pos);
        var probe = new Segment { serial = segmentSerial, cycle = cycleIndex, cyclePos = pos, phase = phase,
                                  breather = segmentSerial >= breatherFrom && segmentSerial < breatherUntil };
        WaveShape S = P.ShapeForCycle(cycleIndex);
        if (probe.breather || WaveGlideDue(segmentSerial) || pos < S.firstHazardLeg) return 0;
        Segment prev = allSegments.Count > 0 ? allSegments[allSegments.Count - 1] : null;
        if (prev != null && prev.plankCount > 0 && phase != WavePhase.Peak) return 0;

        int cap = P.LegCostCapForCycle(cycleIndex);
        int maxPlanks = Mathf.Max(1, 1 + (cap - 1 - P.costBridge) / Mathf.Max(1, P.costExtraPlank));
        bridgeRng ??= new System.Random();

        // The opening cycle's PEAK is the first bridge: one plank, taught.
        if (cycleIndex == 0)
        {
            if (!taughtBridge && phase == WavePhase.Peak && pos >= 23 && pos <= 25) { teaching = true; return 1; }
            return 0;
        }

        // Bridge-cluster PEAK (even cycles).
        if (phase == WavePhase.Peak)
        {
            if (IsBoostCycle || S.peakKind == PeakKind.None || S.clusterBridgeLegs == null) return 0;
            int lastCluster = -1;
            bool here = false;
            foreach (int c in S.clusterBridgeLegs) { if (c == pos) here = true; lastCluster = Mathf.Max(lastCluster, c); }
            if (!here) return 0;
            if (pos == lastCluster) { lastHeavyEndSerial = segmentSerial; breatherFrom = segmentSerial + 1; breatherUntil = segmentSerial + 3; }
            return RollPlanks(maxPlanks, family == Family.PlankWeave ? 0.8 : bridgeMultiChance);
        }

        if (phase != WavePhase.Build || !bridgeSections) return 0;

        // The first multi-plank bridge is taught early in cycle 1.
        if (!taughtMulti && taughtBridge && pos >= S.firstHazardLeg + 2 && maxPlanks >= 2 && CanSpend(probe, P.costBridge + P.costExtraPlank))
        {
            teaching = true;
            return 2;
        }

        float chance = bridgeChance * HazardScale(probe, Hazard.Bridge);
        if (bridgeRng.NextDouble() >= chance) return 0;
        bool multiFeatured = FeaturedHazard() == Hazard.MultiPlank;
        int planks = RollPlanks(maxPlanks, multiFeatured ? Mathf.Max(0.6f, bridgeMultiChance) : bridgeMultiChance);
        while (planks > 0 && !CanSpend(probe, P.costBridge + (planks - 1) * P.costExtraPlank)) planks--;
        return planks;
    }

    int RollPlanks(int maxPlanks, double multiChance)
    {
        if (bridgeRng.NextDouble() >= multiChance) return 1;
        int lo = Mathf.Max(1, Mathf.Min(bridgeMultiPlankRange.x, bridgeMultiPlankRange.y));
        int hi = Mathf.Max(lo, Mathf.Max(bridgeMultiPlankRange.x, bridgeMultiPlankRange.y));
        return Mathf.Clamp(bridgeRng.Next(lo, hi + 1), 1, maxPlanks);
    }

    bool PlankWeave => family == Family.PlankWeave;

    void OnBridgeLaid(int planks, bool teaching)
    {
        if (planks <= 0) return;
        taughtBridge = true;
        if (planks > 1 && teaching) taughtMulti = true;
        if (planks > 1) taughtMulti = true;
    }

    // ------------------------------------------------------------------ decoys

    /// <summary>May the corner at the end of `prev` (the start of `seg`) get a decoy stub?</summary>
    bool WaveDecoyAllowed(Segment prev, Segment seg, out bool forced)
    {
        forced = false;
        var P = difficultyProfile;
        if (prev == null || prev.boost || prev.portalRole != 0 || prev.plankCount > 0 || seg.plankCount > 0) return false;
        bool hazardPhase = prev.phase == WavePhase.Build || (Is311(prev) && prev.phase == WavePhase.Peak);   // 3.11: peaks and the finale too
        if (prev.glide || prev.breather || !hazardPhase || prev.cyclePos < P.ShapeForCycle(prev.cycle).firstHazardLeg) return false;
        if (seg.serial - lastDecoySerial < 2) return false;     // never on adjacent legs

        if (cycleIndex == 0)
        {
            // Teaching decoy: the first one sits at the end of a long, easy leg.
            if (taughtDecoy || !P.TeachOpeningDecoy || prev.cyclePos < 10) return false;
            if (prev.taps > 1 || prev.seconds < 1.3f * difficultyProfile.MinLegSeconds(prev.taps, prev.serial, false)) return false;
            forced = CanSpend(prev, P.costDecoy);
            return forced;
        }

        if (family == Family.DecoyGauntlet && decoysThisCycle >= 2) return false;
        if (!CanSpend(prev, P.costDecoy)) return false;
        float chance = CurrentDecoyChance * HazardScale(prev, Hazard.Decoy);
        decoyRng ??= new System.Random();
        return decoyRng.NextDouble() < chance;
    }

    void OnDecoyLaid(Segment prev, Segment seg)
    {
        taughtDecoy = true;
        decoysThisCycle++;
        lastDecoySerial = seg.serial;
        if (prev != null)
        {
            prev.decoyAtEnd = true;
            prev.hazardCost += difficultyProfile.costDecoy;
        }
        OnDecoyLaid311(prev, seg);
    }

    // ------------------------------------------------------------------ camera

    // A zone the camera may turn into: its first leg is in RELEASE or is a portal runway.
    void MarkZoneYawPermission(PathZone zone, Segment first)
    {
        if (!Waves || !difficultyProfile.restrictCameraRotation) { zone.yawChangeAllowed = true; return; }
        zone.yawChangeAllowed = first.portalRole != 0 || first.phase == WavePhase.Release;
    }

    // The yaw the camera is planned to hold on the newest zone that has one, or -1.
    float LatestPlannedYaw()
    {
        for (int i = zones.Count - 1; i >= 0; i--)
        {
            if (zones[i].yawPlanned) return zones[i].plannedYaw;
            if (zones[i].framingSolved) return zones[i].optimalCameraYaw;
        }
        return -1f;
    }

    static Vector3 YawForward(float yaw) => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
}
