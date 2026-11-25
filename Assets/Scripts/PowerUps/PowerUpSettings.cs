using System.Text;
using UnityEngine;

public enum PowerUpType { Shield, Focus, Magnet, Autopilot }

/// <summary>
/// Dev-only counters for the power-ups: how many were spawned, collected and used per type, the legs
/// between placements, and what each shield save was for. Static and allocation-free during play (the
/// summary string is only built on request).
/// </summary>
public static class PowerUpTelemetry
{
    const int Types = 4;
    static readonly int[] spawned = new int[Types];
    static readonly int[] collected = new int[Types];
    static readonly int[] used = new int[Types];
    static readonly int[] lastSpawnLeg = { -1, -1, -1, -1 };
    static readonly int[] gapSum = new int[Types];
    static readonly int[] gapCount = new int[Types];
    static readonly string[] shieldCauses = new string[16];
    static int shieldCauseCount;
    static int lastAnyLeg = -1, minAnyGap = int.MaxValue;

    public static void Reset()
    {
        for (int i = 0; i < Types; i++) { spawned[i] = collected[i] = used[i] = gapSum[i] = gapCount[i] = 0; lastSpawnLeg[i] = -1; }
        shieldCauseCount = 0; lastAnyLeg = -1; minAnyGap = int.MaxValue;
    }

    public static void Spawned(PowerUpType t, int leg)
    {
        int i = (int)t;
        spawned[i]++;
        if (lastSpawnLeg[i] >= 0) { gapSum[i] += leg - lastSpawnLeg[i]; gapCount[i]++; }
        lastSpawnLeg[i] = leg;
        if (lastAnyLeg >= 0) minAnyGap = Mathf.Min(minAnyGap, leg - lastAnyLeg);
        lastAnyLeg = leg;
    }

    public static void Collected(PowerUpType t) { collected[(int)t]++; }

    public static void Used(PowerUpType t, string cause)
    {
        used[(int)t]++;
        if (t == PowerUpType.Shield && shieldCauseCount < shieldCauses.Length) shieldCauses[shieldCauseCount++] = cause;
    }

    public static string Summary()
    {
        var sb = new StringBuilder("[PowerUps] ");
        string[] names = { "shield", "focus", "magnet", "autopilot" };
        for (int i = 0; i < Types; i++)
        {
            sb.Append(names[i]).Append(": spawned ").Append(spawned[i]).Append(", collected ").Append(collected[i]).Append(", used ").Append(used[i]);
            if (gapCount[i] > 0) sb.Append(", avg gap ").Append((gapSum[i] / (float)gapCount[i]).ToString("0.0"));
            sb.Append(" | ");
        }
        sb.Append("min legs between any two: ").Append(minAnyGap == int.MaxValue ? "-" : minAnyGap.ToString());
        if (shieldCauseCount > 0)
        {
            sb.Append(" | shield saves: ");
            for (int i = 0; i < shieldCauseCount; i++) sb.Append(shieldCauses[i]).Append(i + 1 < shieldCauseCount ? ", " : "");
        }
        return sb.ToString();
    }
}

// 
