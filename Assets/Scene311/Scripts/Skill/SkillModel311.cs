using System;
using UnityEngine;

/// <summary>What a leg asked of the player, for the per-feature ratings (scene 3.11).</summary>
public enum Feature311
{
    Corner1, Corner2, Decoy,
    BoostClassic, BoostLong, BoostBroken, BoostExit,
    SlowMo,
    RunwayTotem, RunwayPair, RunwayCorner, RunwayFence, RunwayDrifter, RunwayTwin, RunwayCornerSlider,
    Count
}

/// <summary>The saved skill profile (JSON, persistentDataPath/skill311.json). Hidden from the player.</summary>
[Serializable]
public class SkillSave311
{
    public const int CurrentVersion = 1;
    public const int MaxCheckpoints = 16;

    public int version = CurrentVersion;
    public float rating;
    public int legs;
    public float[] featureRating = new float[(int)Feature311.Count];
    public int[] featureLegs = new int[(int)Feature311.Count];
    public int[] featureFails = new int[(int)Feature311.Count];
    public int runs;
    public int[] attempts = new int[MaxCheckpoints + 1];             // attempts at a checkpoint since its last clear
    public int[] totalAttempts = new int[MaxCheckpoints + 1];
    public int[] attemptsToFirstClear = new int[MaxCheckpoints + 1]; // 0 = not cleared yet
    public int[] deaths = new int[MaxCheckpoints + 1];
    public float[] push = new float[MaxCheckpoints + 1];             // tiers harder, from a quick clear of the one before
    public int rescues, shieldSaves, closeCalls, retries;
    public float reactionEma = -1f, marginEma = -1f;
    public int reactionN, marginN;
    public double sessionSeconds;
    public int sessions;

    public static SkillSave311 New(float startRating)
    {
        var s = new SkillSave311 { rating = startRating };
        for (int i = 0; i < s.featureRating.Length; i++) s.featureRating[i] = startRating;
        return s;
    }

    /// <summary>Old saves: arrays grown to today's sizes, version bumped.</summary>
    public void Migrate(float startRating)
    {
        int f = (int)Feature311.Count, c = MaxCheckpoints + 1;
        featureRating = Grow(featureRating, f, startRating);
        featureLegs = Grow(featureLegs, f);
        featureFails = Grow(featureFails, f);
        attempts = Grow(attempts, c); totalAttempts = Grow(totalAttempts, c); attemptsToFirstClear = Grow(attemptsToFirstClear, c);
        deaths = Grow(deaths, c); push = Grow(push, c, 0f);
        if (rating <= 0f) rating = startRating;
        version = CurrentVersion;
    }

    static int[] Grow(int[] a, int n) { if (a != null && a.Length >= n) return a; var b = new int[n]; if (a != null) Array.Copy(a, b, a.Length); return b; }
    static float[] Grow(float[] a, int n, float fill)
    {
        if (a != null && a.Length >= n) return a;
        var b = new float[n];
        for (int i = 0; i < n; i++) b[i] = fill;
        if (a != null) Array.Copy(a, b, a.Length);
        return b;
    }
}

/// <summary>
/// The skill model itself (no Unity objects: SkillProfile311 drives it in the game, the multi-week simulator in the
/// editor). Elo-style: every leg has a difficulty rating from its cost and slack, expected success =
/// 1 / (1 + 10^((leg - player) / eloScale)), and the overall and per-feature ratings move by K x (result - expected)
/// after each leg, K decaying with experience. The tier is the rating as a continuous -2 .. +2 (Struggling .. Expert);
/// pacing (push after quick clears, mercy after too many attempts) shifts it per checkpoint.
/// </summary>
public class SkillModel311
{
    public readonly SkillTuning311 T;
    public readonly SkillSave311 S;

    public SkillModel311(SkillTuning311 tuning, SkillSave311 save)
    {
        T = tuning;
        S = save ?? SkillSave311.New(tuning.startRating);
        S.Migrate(tuning.startRating);
    }

    public float LegRating(int cost, float slack) =>
        T.legBase + T.costWeight * Mathf.Max(0, cost) + T.slackWeight * Mathf.Max(0f, T.slackRef - slack);

    public float Expected(float player, float leg) => 1f / (1f + Mathf.Pow(10f, (leg - player) / Mathf.Max(1f, T.eloScale)));

    float K(int n) => Mathf.Max(T.kMin, T.kStart / (1f + n / Mathf.Max(1f, T.kHalfLegs)));

    /// <summary>One leg played: result 1 = clean, a rescue less, 0 = a miss (death or shield).</summary>
    public void OnLeg(int featureMask, int cost, float slack, float result)
    {
        float leg = LegRating(cost, slack);
        S.rating += K(S.legs) * (result - Expected(S.rating, leg));
        S.legs++;
        for (int f = 0; f < (int)Feature311.Count; f++)
        {
            if ((featureMask & (1 << f)) == 0) continue;
            float k = K(S.featureLegs[f]) / Mathf.Max(1f, T.featureKScale);
            S.featureRating[f] += k * (result - Expected(S.featureRating[f], leg));
            S.featureLegs[f]++;
            if (result <= 0f) S.featureFails[f]++;
        }
    }

    public float Tier => Mathf.Clamp((S.rating - T.startRating) / Mathf.Max(1f, T.pointsPerTier), -2f, 2f);

    public float FeatureTier(Feature311 f)
    {
        int i = (int)f;
        if (S.featureLegs[i] < 15) return Tier;      // not enough seen to call it a weak spot
        return Mathf.Clamp((S.featureRating[i] - T.startRating) / Mathf.Max(1f, T.pointsPerTier), -2f, 2f);
    }

    bool Weak(params Feature311[] fs)
    {
        float sum = 0f; int n = 0;
        foreach (var f in fs) { if (S.featureLegs[(int)f] < 15) continue; sum += FeatureTier(f); n++; }
        return n > 0 && sum / n < Tier - T.weakFeatureTiers;
    }

    // ---------------------------------------------------------------------- pacing

    public int MercyStep(int checkpoint)
    {
        int high = T.TargetFor(checkpoint).y;
        int a = S.attempts[Mathf.Clamp(checkpoint, 0, SkillSave311.MaxCheckpoints)];
        if (a <= high) return 0;
        int steps = 1 + Mathf.FloorToInt((a - high - 1) / Mathf.Max(1f, T.mercyStepShare * high));
        return Mathf.Clamp(steps, 0, T.mercyMaxSteps);
    }

    public void NoteRunStart() { S.runs++; }
    public void NoteAttempt(int checkpoint)
    {
        int c = Mathf.Clamp(checkpoint, 0, SkillSave311.MaxCheckpoints);
        S.attempts[c]++;
        S.totalAttempts[c]++;
    }
    public void NoteDeath(int checkpoint) => S.deaths[Mathf.Clamp(checkpoint, 0, SkillSave311.MaxCheckpoints)]++;

    /// <summary>The door of `checkpoint` was crossed: its attempts and mercy reset; a quick first clear pushes the next one.</summary>
    public void NoteClear(int checkpoint)
    {
        int c = Mathf.Clamp(checkpoint, 0, SkillSave311.MaxCheckpoints);
        int a = Mathf.Max(1, S.attempts[c]);
        bool first = S.attemptsToFirstClear[c] == 0;
        if (first)
        {
            S.attemptsToFirstClear[c] = a;
            if (c + 1 <= SkillSave311.MaxCheckpoints && a < T.TargetFor(c).x)
                S.push[c + 1] = Mathf.Min(2f, S.push[c + 1] + T.pushTiers);
        }
        S.attempts[c] = 0;
    }

    /// <summary>The tier a run (or the level after a portal) at `checkpoint` is laid with, as the profile's modifiers.</summary>
    public TierMods311 ModsFor(int checkpoint, out float tier, out int mercy)
    {
        mercy = 0;
        tier = 0f;
        if (S.runs < T.collectOnlyRuns) return Base(0f, 0);
        int c = Mathf.Clamp(checkpoint, 0, SkillSave311.MaxCheckpoints);
        mercy = MercyStep(c);
        tier = Mathf.Clamp(Tier + S.push[c], -2f, 2f);
        tier = Mathf.Max(-2f, tier - mercy);
        return Base(tier, mercy);
    }

    TierMods311 Base(float tier, int mercy)
    {
        TierRow311 r = T.RowAt(tier);
        TierRow311 below = T.RowAt(-1f);
        var m = TierMods311.Average;
        m.tier = tier;
        m.mercyStep = mercy;
        m.budget = r.budget;
        m.tHumanAdd = r.tHumanAdd;
        m.rescueAdd = r.rescueAdd;
        m.rescueCap = T.rescueCap;
        m.rescueMin = T.rescueMin;
        m.stationsAdd = r.stations;
        m.stationsMin = Mathf.Min(T.stationsMin, 5);
        m.stationsMax = T.stationsMax;
        m.boostLength = 1f + r.boostLength;
        m.decoys = r.decoys;
        m.releaseAdd = r.releaseAdd;
        m.shieldFreq = r.shieldFreq;
        m.speed = mercy >= 1 ? r.speed : 1f;
        m.finaleGlide = tier <= -0.5f;
        if (S.runs >= T.collectOnlyRuns)
        {
            m.weakDecoy = Weak(Feature311.Decoy);
            m.weakBoost = Weak(Feature311.BoostClassic, Feature311.BoostLong, Feature311.BoostBroken, Feature311.BoostExit);
            m.weakRunway = Weak(Feature311.RunwayTotem, Feature311.RunwayPair, Feature311.RunwayCorner, Feature311.RunwayFence,
                                Feature311.RunwayDrifter, Feature311.RunwayTwin, Feature311.RunwayCornerSlider);
            m.weakSlowMo = Weak(Feature311.SlowMo);
            m.weakTwoRoll = Weak(Feature311.Corner2);
            // A weak feature gets the Below values only (never harder than them).
            if (m.weakDecoy) m.decoys = Mathf.Min(m.decoys, below.decoys);
            if (m.weakBoost) m.boostLength = Mathf.Min(m.boostLength, 1f + below.boostLength);
            if (m.weakRunway) m.stationsAdd = Mathf.Min(m.stationsAdd, below.stations);
        }
        return m;
    }

    // ---------------------------------------------------------------------- signals

    public void NoteReaction(float seconds)
    {
        if (seconds <= 0f || seconds > 2f) return;
        S.reactionEma = S.reactionN == 0 ? seconds : Mathf.Lerp(S.reactionEma, seconds, 0.05f);
        S.reactionN++;
    }

    public void NoteMargin(float seconds)
    {
        if (seconds < -1f || seconds > 3f) return;
        S.marginEma = S.marginN == 0 ? seconds : Mathf.Lerp(S.marginEma, seconds, 0.05f);
        S.marginN++;
    }
}
