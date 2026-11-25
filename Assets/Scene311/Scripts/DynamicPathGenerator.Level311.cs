using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SCENE 3.11 LEVEL PLAN (generator side). Everything here runs only when the profile hands back a LevelSpec311
/// (Scene311DifficultyProfile); with any other profile, or none, every hook below returns the old behaviour unchanged.
///
///   phases     release / waves (build, peak, glides) / finale / exhale from the spec (PhaseAt311, Plan311)
///   budget     per leg from the spec (wave peaks step up, the finale is highest)
///   turns      wantTaps = 2 legs are laid long enough and prefer a half turn (finale pairs, B4 exits, gauntlet decoys)
///   specials   EnsureSpecialsPlan311: wave boosts / runways / slow motion placed with the spacing rules, plus the
///              finale's signature specials at fixed places
///   boosts     B2 long, B3 broken rhythm (uneven zig-zag legs), B4 exit (a 2-roll corner straight after)
///   runways    stations / designs / gap factor / slider speed from the spec; a glide after every runway
///   bursts     a short-leg burst after the L5 / L7 finale specials
///   power      diamond / shield gaps scaled per level and tier
/// </summary>
public partial class DynamicPathGenerator
{
    public partial class Segment
    {
        public int boostMode311 = -1;   // 3.11: BoostMode311 of a boost leg
        public int designMask311;       // 3.11: runway designs on this leg (bit = DesignKind)
    }

    int boostModeActive = -1;      // the mode of the boost being laid (Tag311)
    int want2Serial = -1;          // the turn at the end of this leg should be a half turn (B4 exit, gauntlet decoy)
    int burstFrom = -1, burstUntil = -1;
    int lastRunwaySerial = -100;
    int boostModeNext;             // BoostMode311 of the boost being planned
    bool boostIsFinaleNext;
    bool slowMoFinaleNext, slowMoFinaleActive;

    void Reset311()
    {
        want2Serial = -1;
        burstFrom = burstUntil = -1;
        lastRunwaySerial = -100;
        boostModeNext = 0;
        boostIsFinaleNext = slowMoFinaleNext = slowMoFinaleActive = false;
        // A restart must draw its level's specials afresh (the old plan's slots are spent).
        specialsCycle = -1;
        specialSlots.Clear();
        pendingSpecial = null;
    }

    LevelSpec311 Spec311At(int cycle) => difficultyProfile != null ? difficultyProfile.Spec311(cycle) : null;
    LevelSpec311 L311 => Spec311At(cycleIndex);
    bool Is311(Segment s) => s != null && Spec311At(s.cycle) != null;

    LegPlan311 Plan311(Segment s) => Spec311At(s.cycle).PlanAt(s.cyclePos);

    /// <summary>Tags a laid leg for the skill model (boost mode). Called from OnSegmentLaidTB.</summary>
    void Tag311(Segment seg)
    {
        if (seg.boost && seg.portalRole == 0) seg.boostMode311 = Mathf.Max(0, boostModeActive);
    }

    // ------------------------------------------------------------------ phases, glides, budget

    WavePhase PhaseAt311(LevelSpec311 L, int pos) => L.PlanAt(pos).phase;

    /// <summary>Is the leg about to be laid a glide (planned, or the leg after a runway)?</summary>
    bool GlideDue311(LevelSpec311 L, int serial)
    {
        int pos = CyclePos(serial);
        LegPlan311 p = L.PlanAt(pos);
        if (p.release || p.exhale || pos <= 0) return false;
        if (p.glide) return true;
        Segment last = allSegments.Count > 0 ? allSegments[allSegments.Count - 1] : null;
        return last != null && last.portalRole == 3 && last.serial == serial - 1;
    }

    float HazardScale311(Segment s, Hazard h)
    {
        LegPlan311 p = Plan311(s);
        if (p.release || p.exhale || p.glide) return 0f;
        if (p.phase != WavePhase.Build && p.phase != WavePhase.Peak) return 0f;
        if (s.cyclePos <= Spec311At(s.cycle).releaseEnd) return 0f;
        float scale = p.intensity;
        if (h == Hazard.Decoy) scale *= p.decoyScale;
        return scale;
    }

    float Budget311(Segment s) => Plan311(s).budget;

    /// <summary>3.11: the most a leg may cost after `prev` (a cap leg lets the next one cost at most costAfterCap).</summary>
    int CostLimitAfter311(Segment prev)
    {
        if (prev == null || prev.cost < 0) return int.MaxValue;
        LevelSpec311 L = Spec311At(prev.cycle);
        if (L == null) return int.MaxValue;
        if (prev.cost < difficultyProfile.LegCostCapForCycle(prev.cycle)) return int.MaxValue;
        if (L.PlanAt(prev.cyclePos).spike) return int.MaxValue;   // the L9 double spike
        return L.costAfterCap;
    }

    // ------------------------------------------------------------------ turns

    bool WantsTwoRoll311(Segment s)
    {
        if (s == null || !Is311(s)) return false;
        if (s.serial == want2Serial) return true;
        return Plan311(s).wantTaps == 2;
    }

    /// <summary>A leg planned to end in a half turn is laid long enough for one.</summary>
    void Lengthen311(ref int segLen)
    {
        LevelSpec311 L = L311;
        if (L == null || !TB) return;
        if (L.PlanAt(CyclePos(segmentSerial)).wantTaps != 2) return;
        int need = MinCubes(2, segmentSerial, false, LegSpeed(segmentSerial, false)) + 1;
        segLen = Mathf.Max(segLen, need);
    }

    /// <summary>Before choosing the turn after `fitFor`: if it should be a half turn and is too short, lengthen it in place.</summary>
    void Prepare311TwoRoll(Segment fitFor)
    {
        if (fitFor == null || !WantsTwoRoll311(fitFor)) return;
        if (MaxTapsByTime(fitFor) < 2) TryExtendForTwoTaps(fitFor);
    }

    /// <summary>Keeps only the half turns, if `fitFor` wants one and any is possible.</summary>
    void PreferTwoRoll311(List<Vector3> possible, Segment fitFor)
    {
        if (fitFor == null || possible.Count == 0 || !WantsTwoRoll311(fitFor)) return;
        bool any = false;
        foreach (Vector3 d in possible) if (Rolls(fitFor.landingSide, SideOnLeg(fitFor.direction, d)) == 2) { any = true; break; }
        if (!any) return;
        possible.RemoveAll(d => Rolls(fitFor.landingSide, SideOnLeg(fitFor.direction, d)) != 2);
    }

    /// <summary>The remix family forced on the leg about to be laid (finale signatures), -1 = none.</summary>
    int Family311()
    {
        LevelSpec311 L = L311;
        return L == null ? -1 : L.PlanAt(CyclePos(segmentSerial)).family;
    }

    // ------------------------------------------------------------------ decoys

    void OnDecoyLaid311(Segment prev, Segment seg)
    {
        if (prev == null || !Is311(prev)) return;
        LevelSpec311 L = Spec311At(prev.cycle);
        // Decoy gauntlet: every decoy is followed by a half turn.
        if (L.signature == FinaleSignature311.DecoyGauntlet && L.PlanAt(prev.cyclePos).finale) want2Serial = seg.serial;
    }

    // ------------------------------------------------------------------ bursts

    float BurstScale311(int leg)
    {
        if (burstFrom < 0 || leg < burstFrom || leg >= burstUntil) return 1f;
        return difficultyProfile is Scene311DifficultyProfile p ? p.burstDurScale : 1f;
    }

    void StartBurst311(int fromSerial)
    {
        var p = difficultyProfile as Scene311DifficultyProfile;
        if (p == null) return;
        burstFrom = fromSerial;
        burstUntil = fromSerial + Mathf.Max(1, p.burstLegs);
    }

    // ------------------------------------------------------------------ specials

    void EnsureSpecialsPlan311(LevelSpec311 L)
    {
        var P = difficultyProfile as Scene311DifficultyProfile;
        int level = cycleIndex + 1;
        var items = new List<SpecialSlot>();

        // Wave specials: runways, boosts, slow motion (placed below). Level 1 has none.
        if (level >= 2 || L.freefall)
        {
            for (int i = 0; i < L.runways; i++) items.Add(new SpecialSlot { kind = SpecialRunway, legs = 1 });
            for (int i = 0; i < L.boosts; i++)
            {
                int mode = RollBoostMode(L);
                float b = Mathf.Lerp(L.boostLegs.x, L.boostLegs.y, (float)specialsRng.NextDouble());
                if (mode == (int)BoostMode311.Long) b *= P.longBoost311;
                items.Add(new SpecialSlot { kind = SpecialBoost, legs = Mathf.Clamp(Mathf.RoundToInt(b), 3, P.boostLegCap311), mode = mode });
            }
            for (int i = 0; i < L.slowMos; i++)
                items.Add(new SpecialSlot { kind = SpecialSlowMo, legs = RollSlowMoLegs(level, P.slowMoLegCap) });
        }

        // The room inside the waves: from the first leg after the release to a few legs before the finale (a Mild
        // finale has no pattern of its own, so its specials may run on into it).
        int from = L.releaseEnd + 2;
        int to = (L.signature == FinaleSignature311.Mild ? L.exhaleStart : L.finaleStart) - 2;
        int room = Mathf.Max(0, to - from);
        // Legs a special takes plus the clear legs after it (4 to anything; boost to boost 6, see the order below).
        int Footprint(SpecialSlot s) => s.kind == SpecialRunway ? 2 + P.specialGap : s.kind == SpecialBoost ? s.legs + P.specialGap : s.legs + 2 + P.specialGap;
        int Count(int kind) { int c = 0; foreach (var s in items) if (s.kind == kind) c++; return c; }
        // Boosts are kept apart by the others where they can be; the pairs that must touch need the extra 2 legs.
        int Total()
        {
            int t = 0; foreach (var s in items) t += Footprint(s);
            int b = Count(SpecialBoost), o = items.Count - b;
            return t + Mathf.Max(0, b - o - 1) * (P.boostGap - P.specialGap);
        }
        int minBoost = Mathf.Max(3, Mathf.FloorToInt(L.boostLegs.x * 0.8f));
        while (Total() > room && items.Count > 0)
        {
            // 1. shorten boosts (down to 80% of the level's shortest), 2. drop slow motion, 3. a boost, 4. a runway
            bool shrunk = false;
            foreach (var s in items) if (s.kind == SpecialBoost && s.legs > minBoost) { s.legs--; shrunk = true; }
            if (shrunk) continue;
            int drop = -1;
            if (Count(SpecialSlowMo) > 0) drop = items.FindLastIndex(s => s.kind == SpecialSlowMo);
            else if (Count(SpecialBoost) > 1) drop = items.FindLastIndex(s => s.kind == SpecialBoost);
            else if (Count(SpecialRunway) > 1) drop = items.FindLastIndex(s => s.kind == SpecialRunway);
            if (drop >= 0) { items.RemoveAt(drop); continue; }
            items.Clear();
        }

        // Random order (two runways never back to back), the slack spread randomly between them.
        for (int i = items.Count - 1; i > 0; i--) { int j = specialsRng.Next(0, i + 1); (items[i], items[j]) = (items[j], items[i]); }
        for (int i = 1; i < items.Count; i++)
            if (items[i].kind == SpecialBoost && items[i - 1].kind == SpecialBoost)
                for (int j = i + 1; j < items.Count; j++)
                    if (items[j].kind != SpecialBoost) { (items[i], items[j]) = (items[j], items[i]); break; }
        for (int i = 1; i < items.Count; i++)
            if (items[i].kind == SpecialRunway && items[i - 1].kind == SpecialRunway)
                for (int j = i + 1; j < items.Count; j++)
                    if (items[j].kind != SpecialRunway) { (items[i], items[j]) = (items[j], items[i]); break; }
        int slack = Mathf.Max(0, room - Total());
        var cuts = new List<int>(items.Count);
        for (int i = 0; i < items.Count; i++) cuts.Add(specialsRng.Next(0, slack + 1));
        cuts.Sort();
        int pos = from, prevCut = 0, lastRunwayAt = -100;
        for (int i = 0; i < items.Count; i++)
        {
            pos += cuts[i] - prevCut;
            prevCut = cuts[i];
            if (items[i].kind == SpecialRunway && pos < lastRunwayAt + P.runwayGap) pos = lastRunwayAt + P.runwayGap;
            items[i].at = pos;
            if (items[i].kind == SpecialRunway) lastRunwayAt = pos;
            pos += Footprint(items[i]);
            if (items[i].kind == SpecialBoost && i + 1 < items.Count && items[i + 1].kind == SpecialBoost) pos += P.boostGap - P.specialGap;
            specialSlots.Add(items[i]);
        }
        specialSlots.RemoveAll(s => s.at > to);

        // The finale's signature specials, at their fixed places.
        foreach (Special311 f in L.fixedSpecials)
        {
            int legs = f.legs;
            if (f.kind == SpecialSlowMo)
            {
                int left = L.exhaleStart - f.at - 2 - (P.BurstAfterFinaleSpecial(L.signature, SpecialSlowMo) ? P.burstLegs : 0);
                legs = Mathf.Min(RollSlowMoLegs(level, P.finaleSlowMoCap), Mathf.Max(3, left));
            }
            specialSlots.Add(new SpecialSlot { kind = f.kind, at = f.at, legs = legs, mode = (int)f.mode, finale = true });
        }
        specialSlots.Sort((a, b) => a.at.CompareTo(b.at));

        if (logBoosts || logSlowMo)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in specialSlots)
                sb.Append(s.kind == SpecialBoost ? "boost" + (BoostMode311)s.mode : s.kind == SpecialSlowMo ? "slowmo" : "runway")
                  .Append('@').Append(s.at).Append('x').Append(s.legs).Append(s.finale ? "F" : "").Append(' ');
            Debug.Log($"[Specials311] level {level} ({L.length} legs, finale {L.finaleStart}-{L.exhaleStart - 1}, {L.signature}): {sb}");
        }
    }

    int RollBoostMode(LevelSpec311 L)
    {
        float total = 0f;
        foreach (float w in L.modeWeights) total += w;
        if (total <= 0f) return 0;
        float r = (float)specialsRng.NextDouble() * total;
        for (int i = 0; i < 4; i++) { r -= L.modeWeights[i]; if (r < 0f) return i; }
        return 0;
    }

    int RollSlowMoLegs(int level, int cap)
    {
        var P = difficultyProfile;
        int legs = specialsRng.Next(Mathf.Min(P.slowMoLegs.x, P.slowMoLegs.y), Mathf.Max(P.slowMoLegs.x, P.slowMoLegs.y) + 1);
        for (int l = P.slowMoFromLevel; l < Mathf.Min(level, 10); l++)
            legs += specialsRng.Next(Mathf.Min(P.slowMoLegsPerLevel.x, P.slowMoLegsPerLevel.y), Mathf.Max(P.slowMoLegsPerLevel.x, P.slowMoLegsPerLevel.y) + 1);
        return Mathf.Clamp(legs, 3, Mathf.Min(cap, P.slowMoLegCap));
    }

    /// <summary>SpecialDue just matched a slot: remember what kind of boost / slow motion it is.</summary>
    void OnSpecialMatched311(SpecialSlot s)
    {
        if (s.kind == SpecialBoost) { boostModeNext = s.mode; boostIsFinaleNext = s.finale; }
        if (s.kind == SpecialSlowMo) slowMoFinaleNext = s.finale;
    }

    // ------------------------------------------------------------------ boosts

    /// <summary>A boost always ends before the exhale legs (a late slot is shortened, or dropped under 3 legs).</summary>
    bool FitBoostBeforeExhale311(ref int legs)
    {
        LevelSpec311 L = L311;
        int left = Mathf.Min(L.exhaleStart - CyclePos(segmentSerial), nextPortalSerial - segmentSerial - 3) - 1;
        legs = Mathf.Min(legs, left);
        if (legs >= 3) return true;
        MarkSpecialDone();
        return false;
    }

    /// <summary>B3 broken rhythm: uneven zig-zag legs. B4 exit: the last leg ends in a 2-roll corner at its floor.</summary>
    void ShapeBoost311(int[] lengths, int middle)
    {
        var P = difficultyProfile as Scene311DifficultyProfile;
        if (P == null || L311 == null) return;
        boostModeActive = boostModeNext;
        int count = lengths.Length;
        // Every leg is held to the strictest floor at the fastest speed anywhere in the boost (a late door can put a
        // speed reset - and so a slower "last leg" - inside it, which the shared sizing reads as the fastest).
        float vmax = 0f, floor = 0f;
        for (int i = 0; i < count; i++)
        {
            vmax = Mathf.Max(vmax, LegSpeed(segmentSerial + i, true));
            floor = Mathf.Max(floor, difficultyProfile.MinLegSeconds(2, segmentSerial + i, true));
        }
        int safe = CubesForSeconds(floor, vmax);
        middle = Mathf.Max(middle, safe);
        for (int i = 0; i < count; i++) lengths[i] = Mathf.Max(lengths[i], safe);
        if (boostModeNext == (int)BoostMode311.Broken)
            for (int i = 1; i < count - 1; i++) lengths[i] = middle + boostRng.Next(0, P.brokenExtraCubes + 1);
        if (boostModeNext == (int)BoostMode311.Exit && count >= 2)
        {
            float v = LegSpeed(segmentSerial + count - 1, true);
            lengths[count - 1] = Mathf.Max(middle, CubesForSeconds(difficultyProfile.MinLegSeconds(2, segmentSerial + count - 1, false) * P.exitLegFloorScale, v));
        }
    }

    /// <summary>The last leg of a boost is being laid (segmentSerial).</summary>
    void OnBoostLastLeg311()
    {
        if (L311 == null) return;
        if (boostModeNext == (int)BoostMode311.Exit) want2Serial = segmentSerial;
        if (boostIsFinaleNext && L311.signature == FinaleSignature311.LongBoostExit) want2Serial = segmentSerial;
        boostModeNext = 0;
        boostIsFinaleNext = false;
    }

    // ------------------------------------------------------------------ runways

    bool RunwayBlocked311() =>
        L311 != null && difficultyProfile is Scene311DifficultyProfile p && segmentSerial < lastHeavyEndSerial + p.specialGap + 1;

    bool BoostBlocked311() =>
        L311 != null && difficultyProfile is Scene311DifficultyProfile p && segmentSerial < lastRunwaySerial + p.specialGap + 1;

    /// <summary>Stations planned for the runway being laid (0 = the old rule: as many as fit).</summary>
    int RunwayStations311()
    {
        LevelSpec311 L = L311;
        if (L == null) return 0;
        return obstacleLegRng.Next(Mathf.Min(L.stations.x, L.stations.y), Mathf.Max(L.stations.x, L.stations.y) + 1);
    }

    int runwayStationsNext;

    /// <summary>Seconds a runway with `stations` stations needs at the current leg (lead + gaps + tail).</summary>
    float RunwaySeconds311(int stations, float lead, float tail)
    {
        LevelSpec311 L = L311;
        float gap = difficultyProfile.MinLegSeconds(2, segmentSerial, false) * L.gapFactor * (1f + L.gapJitter * 0.5f) + 0.15f;
        float fence = FenceSpacing * 1.5f * cubeSize / Mathf.Max(0.5f, LegSpeed(segmentSerial, false));
        return lead + tail + Mathf.Max(0, stations - 1) * (gap + fence * 0.5f) + 0.6f;
    }

    void OnRunwayLaid311(Segment seg)
    {
        if (!Is311(seg)) return;
        lastRunwaySerial = seg.serial;
        LevelSpec311 L = Spec311At(seg.cycle);
        if (L.signature == FinaleSignature311.SliderRunwayBurst && L.PlanAt(seg.cyclePos).finale) StartBurst311(seg.serial + 2);
    }

    /// <summary>3.11: the design row for a runway leg (the finale's signature designs, else the level's weights).</summary>
    DesignEntry[] DesignRow311(Segment seg)
    {
        LevelSpec311 L = Is311(seg) ? Spec311At(seg.cycle) : null;
        if (L == null) return null;
        var list = new List<DesignEntry>(7);
        bool finale = L.PlanAt(seg.cyclePos).finale && L.finaleDesigns != null && L.finaleDesigns.Length > 0;
        if (finale) foreach (int k in L.finaleDesigns) list.Add(new DesignEntry((DesignKind)Mathf.Clamp(k, 0, 6), 1));
        else if (L.designWeights != null)
            for (int k = 0; k < L.designWeights.Length && k < 7; k++)
                if (L.designWeights[k] > 0) list.Add(new DesignEntry((DesignKind)k, L.designWeights[k]));
        return list.Count > 0 ? list.ToArray() : null;
    }

    float DesignGap311(Segment seg, int rolls, bool slide)
    {
        LevelSpec311 L = Spec311At(seg.cycle);
        float floor = difficultyProfile.MinLegSeconds(rolls, seg.serial, false);
        float s = floor * L.gapFactor + (slide ? 0.15f : 0f);
        s = Mathf.Max(obstacleLegMinGapSeconds, s);
        return s * (1f + (float)obstacleLegRng.NextDouble() * L.gapJitter);
    }

    // ------------------------------------------------------------------ slow motion

    void OnSlowMoFinished311()
    {
        if (!slowMoFinaleActive) return;
        slowMoFinaleActive = false;
        LevelSpec311 L = L311;
        if (L != null && L.signature == FinaleSignature311.RunwaySlowBurst) StartBurst311(segmentSerial + 1);
    }

    // ------------------------------------------------------------------ power-ups

    int ScalePowerGap311(int gap)
    {
        LevelSpec311 L = L311;
        return L == null ? gap : Mathf.Max(4, Mathf.RoundToInt(gap * L.powerGapScale));
    }

    int ScaleShieldGap311(int gap)
    {
        LevelSpec311 L = L311;
        return L == null ? gap : Mathf.Max(4, Mathf.RoundToInt(gap * L.shieldGapScale));
    }
}
