using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SPECIALS PER LEVEL (MainScene prop camera 3.11 - off unless specialsPerLevel). One plan per level, drawn when the
/// level starts, that places its boosts, slow-motion sections and obstacle runways so they never land on each other:
///   - level 1 (Start): nothing (the player is still learning to read the path)
///   - boosts in every level from 2: DifficultyProfile.boostsPerLevel of them (one more now and then from level 6),
///     boostLegsFirst legs at level 2 and x specialGrowth (~20%) per level after, one of them sometimes a long one
///   - slow motion from slowMoFromLevel: one in its first level, then none / one / two (slowMoCountChances),
///     its tiny legs growing x specialGrowth per level the same way
///   - the obstacle runway(s) the profile's RunwayPlan asks for
/// The jitter on every count is small enough (+-8%) that a level is never easier than the one before. When a level is
/// too short for everything, the extras go first (a second slow motion, extra boosts), then every length shrinks.
/// Boosts are laid by the boost code (WaveBoostDue asks SpecialDue), slow motion by TakeSlowMoLeg, runways by
/// TakeObstacleLeg (its plan comes from SpecialRunwayPlan). A slot whose window slips by (no room to lay it) is dropped.
/// Uses its own System.Random.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Specials Per Level (3.11) - numbers in the DifficultyProfile")]
    [Tooltip("Plan each level's boosts, slow-motion sections and obstacle runways together (DifficultyProfile 'Specials per level').")]
    public bool specialsPerLevel = false;

    const int SpecialBoost = 0, SpecialSlowMo = 1, SpecialRunway = 2;

    class SpecialSlot { public int kind, at, legs; public bool done; public int mode; public bool finale; }   // mode / finale: 3.11 plan only

    readonly List<SpecialSlot> specialSlots = new List<SpecialSlot>();
    int specialsCycle = -1;
    SpecialSlot pendingSpecial;
    System.Random specialsRng;

    bool SpecialsOn => specialsPerLevel && difficultyProfile != null && endlessLevel == 0 && !obstacleTestMode;

    float Jitter() => Mathf.Lerp(0.92f, 1.08f, (float)specialsRng.NextDouble());

    void EnsureSpecialsPlan()
    {
        if (specialsCycle == cycleIndex) return;
        specialsCycle = cycleIndex;
        specialSlots.Clear();
        pendingSpecial = null;
        specialsRng ??= new System.Random();
        DifficultyProfile P = difficultyProfile;
        int level = cycleIndex + 1;
        LevelSpec311 spec = L311;
        if (spec != null) { EnsureSpecialsPlan311(spec); return; }   // scene 3.11 production plan (DynamicPathGenerator.Level311.cs)
        if (P == null || level < 2) return;

        var items = new List<SpecialSlot>();
        // Obstacle runways: as many as the level's own plan has.
        int[] authored = P.RunwayPlan(cycleIndex);
        int runways = authored != null ? authored.Length : 0;
        for (int i = 0; i < runways; i++) items.Add(new SpecialSlot { kind = SpecialRunway, legs = 1 });

        // Boosts.
        int boosts = specialsRng.Next(Mathf.Min(P.boostsPerLevel.x, P.boostsPerLevel.y), Mathf.Max(P.boostsPerLevel.x, P.boostsPerLevel.y) + 1);
        if (level >= 6 && specialsRng.NextDouble() < P.boostExtraChance) boosts++;
        float grow = Mathf.Pow(P.specialGrowth, level - 2);
        int longOne = specialsRng.NextDouble() < P.longBoostChance ? specialsRng.Next(0, Mathf.Max(1, boosts)) : -1;
        for (int i = 0; i < boosts; i++)
        {
            float b = Mathf.Lerp(P.boostLegsFirst.x, P.boostLegsFirst.y, (float)specialsRng.NextDouble()) * grow * Jitter();
            if (i == longOne) b *= P.longBoostScale;
            items.Add(new SpecialSlot { kind = SpecialBoost, legs = Mathf.Clamp(Mathf.RoundToInt(b), 3, P.boostLegCap) });
        }

        // Slow motion.
        if (slowMoSections && level >= P.slowMoFromLevel)
        {
            int count = 1;
            if (level > P.slowMoFromLevel)
            {
                Vector3 c = P.slowMoCountChances;
                float roll = (float)specialsRng.NextDouble() * Mathf.Max(0.001f, c.x + c.y + c.z);
                count = roll < c.x ? 0 : roll < c.x + c.y ? 1 : 2;
            }
            float sgrow = Mathf.Pow(P.specialGrowth, level - P.slowMoFromLevel);
            for (int i = 0; i < count; i++)
            {
                float t = Mathf.Lerp(P.slowMoLegs.x, P.slowMoLegs.y, (float)specialsRng.NextDouble()) * sgrow * Jitter();
                items.Add(new SpecialSlot { kind = SpecialSlowMo, legs = Mathf.Clamp(Mathf.RoundToInt(t), 3, P.slowMoLegCap) });
            }
        }

        // Fit the level: room from leg 6 to 10 legs before the door.
        int len = Mathf.Max(24, nextPortalSerial - cycleStartSerial);
        int room = len - 16;
        int Footprint(SpecialSlot s) => s.kind == SpecialRunway ? 6 : s.kind == SpecialBoost ? s.legs + 8 : s.legs + 1 + 7;
        int Total() { int t = 0; foreach (var s in items) t += Footprint(s); return t; }
        int slows = 0; foreach (var s in items) if (s.kind == SpecialSlowMo) slows++;
        while (Total() > room)
        {
            int drop = -1;
            if (slows > 1) for (int i = items.Count - 1; i >= 0 && drop < 0; i--) if (items[i].kind == SpecialSlowMo) { drop = i; slows--; }
            if (drop < 0) { int bc = 0; foreach (var s in items) if (s.kind == SpecialBoost) bc++; if (bc > 1) for (int i = items.Count - 1; i >= 0 && drop < 0; i--) if (items[i].kind == SpecialBoost) drop = i; }
            if (drop >= 0) { items.RemoveAt(drop); continue; }
            bool shrunk = false;
            foreach (var s in items) if (s.kind != SpecialRunway && s.legs > 3) { s.legs = Mathf.Max(3, Mathf.RoundToInt(s.legs * 0.85f)); shrunk = true; }
            if (!shrunk) break;
        }

        // Random order, the slack spread randomly between them.
        for (int i = items.Count - 1; i > 0; i--) { int j = specialsRng.Next(0, i + 1); (items[i], items[j]) = (items[j], items[i]); }
        int slack = Mathf.Max(0, room - Total());
        var cuts = new List<int>();
        for (int i = 0; i < items.Count; i++) cuts.Add(specialsRng.Next(0, slack + 1));
        cuts.Sort();
        int pos = 6, prevCut = 0;
        for (int i = 0; i < items.Count; i++)
        {
            pos += cuts[i] - prevCut;
            prevCut = cuts[i];
            items[i].at = pos;
            pos += Footprint(items[i]);
            specialSlots.Add(items[i]);
        }
        if (logBoosts || logSlowMo)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in specialSlots) sb.Append(s.kind == SpecialBoost ? "boost" : s.kind == SpecialSlowMo ? "slowmo" : "runway").Append('@').Append(s.at).Append('x').Append(s.legs).Append(' ');
            Debug.Log($"[Specials] level {level} ({len} legs): {sb}");
        }
    }

    /// <summary>Is a special of `kind` due on the leg about to be laid? Its leg count; remembered until MarkSpecialDone.</summary>
    bool SpecialDue(int kind, out int legs)
    {
        legs = 0;
        EnsureSpecialsPlan();
        int pos = segmentSerial - cycleStartSerial;
        foreach (SpecialSlot s in specialSlots)
        {
            if (s.kind != kind || s.done) continue;
            if (pos > s.at + 4) { s.done = true; continue; }   // its window slipped by: dropped
            if (pos < s.at) return false;
            legs = s.legs;
            pendingSpecial = s;
            OnSpecialMatched311(s);
            return true;
        }
        return false;
    }

    void MarkSpecialDone()
    {
        if (pendingSpecial != null) pendingSpecial.done = true;
        pendingSpecial = null;
    }

    /// <summary>This level's obstacle runways, as positions in the level (TakeObstacleLeg's plan).</summary>
    int[] SpecialRunwayPlan()
    {
        EnsureSpecialsPlan();
        var list = new List<int>();
        foreach (SpecialSlot s in specialSlots) if (s.kind == SpecialRunway) list.Add(s.at);
        list.Sort();
        return list.ToArray();
    }
}
