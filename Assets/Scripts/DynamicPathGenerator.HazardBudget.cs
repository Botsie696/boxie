/// <summary>
/// HAZARD BUDGET (DifficultyProfile, section 6.2). Every leg has a cost:
///   taps (0/1/2) + obstacle 2 + decoy 2 + bridge 3 (+2 per extra plank, +1 more if opposite faces)
///   + boost leg 3 + 1 if shorter than the leg range's middle.
/// Each wave phase earns a budget per leg (BUILD: average, PEAK: peak), growing per cycle; a hazard is only
/// laid when the phase has the budget left for it. Hard rules on top: no leg over the cycle's cost cap, no
/// stacking (decoy + bridge, obstacle + multi-plank), and after a leg of cost &gt;= 5 the next costs &lt;= 2
/// (the turn filter in the TimeBudget partial enforces the taps side of that).
/// </summary>
public partial class DynamicPathGenerator
{
    float budgetCredit, budgetSpent;
    int budgetPhaseKey = -1;

    void ResetBudget()
    {
        budgetCredit = budgetSpent = 0f;
        budgetPhaseKey = -1;
    }

    float BudgetPerLeg(Segment s)
    {
        var P = difficultyProfile;
        if (Is311(s)) return Budget311(s);   // scene 3.11: per leg from the level plan
        return s.phase == WavePhase.Peak ? P.PeakForCycle(s.cycle) : P.BuildAvgForCycle(s.cycle);
    }

    void EnsureBudgetPhase(Segment s)
    {
        int key = s.cycle * 8 + (int)s.phase;
        if (key == budgetPhaseKey) return;
        budgetPhaseKey = key;
        budgetCredit = budgetSpent = 0f;
    }

    /// <summary>May leg s take on `hazardCost` more? Checks the phase, the per-leg cap, the hard-leg rule and the phase budget.</summary>
    bool CanSpend(Segment s, int hazardCost)
    {
        if (!Waves) return true;
        var P = difficultyProfile;
        if (s.phase == WavePhase.Release || s.phase == WavePhase.Cooldown) return false;
        if (s.glide || s.breather || s.cyclePos < P.ShapeForCycle(s.cycle).firstHazardLeg || s.boost || s.portalRole != 0) return false;

        int taps = s.taps >= 0 ? s.taps : 0;
        if (s.hazardCost + hazardCost + taps > P.LegCostCapForCycle(s.cycle)) return false;

        // A probe for the leg about to be laid is not in the list yet: its "before" is the newest leg.
        Segment prev = allSegments.Contains(s) ? SegmentBefore(s) : (allSegments.Count > 0 ? allSegments[allSegments.Count - 1] : null);
        int limit = CostLimitAfter(prev, s);
        if (limit != int.MaxValue && s.hazardCost + hazardCost + taps > limit) return false;

        EnsureBudgetPhase(s);
        float credit = budgetCredit + BudgetPerLeg(s);
        float tapsEstimate = s.taps >= 0 ? s.taps : 1f;
        return budgetSpent + tapsEstimate + s.hazardCost + hazardCost <= credit + 0.001f;
    }

    void SpendBudget(Segment s)
    {
        if (!Waves) return;
        EnsureBudgetPhase(s);
        budgetCredit += BudgetPerLeg(s);
        budgetSpent += s.cost;
    }

    /// <summary>Wrong-face coin lures stay off boost, bridge and glide legs and off any leg costing lureMaxCost or more.</summary>
    bool LureAllowedOn(Segment s)
    {
        if (!Waves || s == null) return true;
        if (s.boost || s.plankCount > 0 || s.glide) return false;
        return s.cost < difficultyProfile.lureMaxCost;
    }
}
