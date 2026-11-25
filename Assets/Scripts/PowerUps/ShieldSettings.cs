using UnityEngine;

/// <summary>Everything about the shield power-up. Edit the asset; every scene that uses it follows.</summary>
[CreateAssetMenu(menuName = "PowerUps/Shield Settings", fileName = "ShieldSettings")]
public class ShieldSettings : ScriptableObject
{
    [Header("Placement")]
    [Tooltip("First leg a shield may appear on.")]
    [Range(0, 200)] public int firstLeg = 20;
    [Tooltip("Fewest legs between one shield (placed) and the next.")]
    [Range(10, 300)] public int gapMin = 60;
    [Tooltip("Most legs between two shields. The gap is drawn uniformly between the two.")]
    [Range(10, 300)] public int gapMax = 90;
    [Tooltip("Chance the shield sits on a face the next turn does NOT need (a deliberate roll and a roll back).")]
    [Range(0f, 1f)] public float offTurnFaceChance = 0.7f;
    [Tooltip("Chance (when one is due) that it waits for the leg right after a bridge or decoy leg.")]
    [Range(0f, 1f)] public float afterHazardChance = 0.3f;
    [Tooltip("Where along the leg it sits (fraction of the leg).")]
    [Range(0.1f, 0.9f)] public float alongMin = 0.35f;
    [Range(0.1f, 0.9f)] public float alongMax = 0.55f;
    [Tooltip("A shield collected while one is held becomes this many coins instead.")]
    [Range(0, 100)] public int coinsIfHeld = 15;

    [Header("Model")]
    [Tooltip("Size of the crest, as a fraction of a cube.")]
    [Range(0.3f, 1.5f)] public float size = 0.75f;
    [Tooltip("How high the pickup floats above its face, in cubes.")]
    [Range(0.1f, 1.5f)] public float hover = 0.55f;
    [Tooltip("Radius of the pickup's bubble, in cubes.")]
    [Range(0.2f, 1.5f)] public float bubbleRadius = 0.42f;
    [Tooltip("Diameter of the bubble round the runner once collected, as a multiple of the runner's size. 1.45 hugs it; bigger hides the track.")]
    [Range(1.1f, 3f)] public float heldBubbleScale = 1.45f;
    [ColorUsage(false, true)] public Color bodyColor = new Color(0.184f, 0.827f, 0.541f, 1f);   // emerald #2FD38A
    [ColorUsage(false, true)] public Color rimColor = Color.white;

    [Header("Idle")]
    [Tooltip("Seconds per full turn of the crest.")]
    [Range(0.5f, 12f)] public float spinSeconds = 3f;
    [Tooltip("Bob height, in cubes, and its period in seconds.")]
    [Range(0f, 0.4f)] public float bob = 0.1f;
    [Range(0.5f, 6f)] public float bobPeriod = 1.8f;
    [Tooltip("Seconds between brightness waves sweeping over the bubble's cells.")]
    [Range(0.5f, 6f)] public float shimmerPeriod = 1.5f;
    [Tooltip("Height of the faint light pillar, in cubes (0 = none).")]
    [Range(0f, 12f)] public float pillarHeight = 6f;

    [Header("Collect")]
    [Tooltip("Pickup radius, in cubes, around the pickup. The runner's last frame's path is swept, so a fast runner cannot skip it.")]
    [Range(0.3f, 2f)] public float pickupRadius = 0.8f;
    [Tooltip("Seconds the game freezes at the pickup (unscaled).")]
    [Range(0f, 0.1f)] public float hitStopSeconds = 0.03f;
    [Tooltip("Seconds the bubble takes to wrap the runner (it overshoots to 1.15x, then settles).")]
    [Range(0.05f, 1f)] public float wrapSeconds = 0.25f;

    [Header("While held")]
    [Tooltip("Alpha of the thin bubble around the runner. Keep it low so it never hides the runner or its face.")]
    [Range(0.02f, 0.4f)] public float heldAlpha = 0.16f;
    [Tooltip("Seconds between the shimmer sweeps and the HUD glow pulses while held.")]
    [Range(1f, 8f)] public float heldShimmerPeriod = 3f;

    [Header("The save")]
    [Tooltip("Time scale during the slow-motion dip.")]
    [Range(0.1f, 1f)] public float dipScale = 0.35f;
    [Tooltip("Seconds (real) to ease down into the dip, hold it, and ease back out.")]
    [Range(0.01f, 0.3f)] public float dipIn = 0.06f;
    [Range(0f, 0.6f)] public float dipHold = 0.2f;
    [Range(0.05f, 1f)] public float dipOut = 0.35f;
    [Tooltip("Seconds the bubble flashes white before it breaks.")]
    [Range(0f, 0.3f)] public float crackFlashSeconds = 0.08f;
    [Tooltip("Hexagonal shards of the broken bubble.")]
    [Range(4, 40)] public int shardCount = 20;
    [Tooltip("Seconds the correct route ahead is shown as glowing ghost cubes (the teaching flash).")]
    [Range(0f, 2f)] public float teachSeconds = 0.6f;
    [Tooltip("Cubes of route shown in the teaching flash.")]
    [Range(2, 20)] public int teachCubes = 10;
    [Tooltip("Seconds the SAVED! text stays up.")]
    [Range(0.2f, 2f)] public float savedTextSeconds = 0.7f;
    [Tooltip("Seconds the runner cannot die after a save; it flickers at 10 Hz meanwhile.")]
    [Range(0f, 3f)] public float invulnerableSeconds = 1f;
    [ColorUsage(false, true)] public Color dangerShardColor = new Color(1f, 0.25f, 0.2f, 1f);

    [Header("HUD")]
    [Tooltip("Shown under the HUD icon the first time a player ever collects a shield (1.6 s).")]
    public string firstTimeHint = "SHIELD - saves you once";
}
