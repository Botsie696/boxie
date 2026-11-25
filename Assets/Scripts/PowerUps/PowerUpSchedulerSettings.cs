using UnityEngine;

/// <summary>Shared rules for where power-ups may go. One asset per scene family (Assets/PowerUps/).</summary>
[CreateAssetMenu(menuName = "PowerUps/Scheduler Settings", fileName = "PowerUpSchedulerSettings")]
public class PowerUpSchedulerSettings : ScriptableObject
{
    [Tooltip("At least this many legs between any two power-ups (of any kind).")]
    [Range(1, 20)] public int minLegsBetween = 4;
    [Tooltip("Seconds a roll to another face costs the runner (swipe + roll time). Used by the fairness check.")]
    [Range(0.05f, 1f)] public float secondsPerTap = 0.3f;
    [Tooltip("The human reaction/decision time added to the fairness check (the T_human of the time budget).")]
    [Range(0.1f, 1.5f)] public float humanSeconds = 0.45f;
    [Tooltip("Coins within this many cubes of a pickup are removed so they never overlap it.")]
    [Range(0f, 3f)] public float coinClearCubes = 1f;
    [Tooltip("No pickup on a PEAK-phase leg (only matters when a DifficultyProfile with wave pacing is assigned).")]
    public bool avoidPeakPhase = true;
    [Tooltip("Print a telemetry summary (spawned / collected / used per type) when a run ends, and on every placement.")]
    public bool logTelemetry = false;
}
