using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ordered checkpoints of one scene (MainScene prop camera 3.6: Checkpoints/CheckpointSet_3_6), plus the 3.6-only
/// portal spacing. LookTransitionDirector.checkpoints points here; when it is set, the director builds its portal
/// sequence from these profiles at Awake, so they are the only place a portal's look is defined. Scenes that leave the
/// field empty (3.5 and older) keep their own CheckpointLookSequence asset, unchanged.
/// </summary>
[CreateAssetMenu(menuName = "Checkpoints/Checkpoint Set", fileName = "CheckpointSet_")]
public class CheckpointSet : ScriptableObject
{
    [Tooltip("Index 0 = checkpoint 1 (run start), index 1 = checkpoint 2 (after portal 1), ...")]
    public List<CheckpointProfile> checkpoints = new List<CheckpointProfile>();

    [Tooltip("Path legs between portals in the scene using this set (applied to DynamicPathGenerator.legsBetweenPortals before the path is built). " +
             "0 = keep the scene's own value. The wave cycles of the DifficultyProfile are not affected.")]
    [Range(0, 200)] public int legsBetweenPortals = 50;

    [Tooltip("Timing for every portal whose profile does not override it.")]
    public CheckpointLookSequence.Timing defaultTiming = CheckpointLookSequence.Timing.Default;

    [Tooltip("Optional. The difficulty profile of the game scene using this set, so the menu (which has no generator) can show the real " +
             "portal legs of a non-uniform schedule. Empty = uniform spacing unless a generator has set DifficultyProfile.Active.")]
    public DifficultyProfile scheduleProfile;

    public int Count => checkpoints.Count;

    /// <summary>Checkpoint by its 1-based index, or null.</summary>
    public CheckpointProfile Get(int index)
    {
        for (int i = 0; i < checkpoints.Count; i++)
            if (checkpoints[i] != null && checkpoints[i].index == index) return checkpoints[i];
        return null;
    }

    /// <summary>The checkpoint portal N leads to (portal 1 -> checkpoint 2), or null.</summary>
    public CheckpointProfile ForPortal(int portal) => Get(portal + 1);

    /// <summary>Nominal leg of a checkpoint with this set's spacing (the snapshot holds the exact one).</summary>
    public int NominalLeg(int index)
    {
        int k = Mathf.Max(0, index - 1), L = Mathf.Max(3, legsBetweenPortals);
        DifficultyProfile p = DifficultyProfile.Active != null ? DifficultyProfile.Active : scheduleProfile;
        return p != null ? p.PortalLeg(k, L) : k * L;
    }

    /// <summary>
    /// A runtime (unsaved) CheckpointLookSequence equivalent to these profiles: one entry per checkpoint 2+ that has a look,
    /// for portal index - 1. Checkpoint 1's look is the base. The caller owns (and destroys) the instance.
    /// </summary>
    public CheckpointLookSequence BuildRuntimeSequence()
    {
        var s = ScriptableObject.CreateInstance<CheckpointLookSequence>();
        s.name = name + " (runtime sequence)";
        s.hideFlags = HideFlags.DontSave;
        CheckpointProfile first = Get(1);
        s.baseLook = first != null ? first.look : null;
        s.defaultTiming = defaultTiming;
        for (int i = 0; i < checkpoints.Count; i++)
        {
            CheckpointProfile p = checkpoints[i];
            if (p == null || p.index < 2 || p.look == null) continue;
            s.entries.Add(new CheckpointLookSequence.Entry
            {
                portal = p.Portal,
                look = p.look,
                label = p.LabelText,
                cameraCinematic = p.cameraCinematic,
                keepObstacle = p.keepObstacle,
                takePlankColor = p.takePlankColor,
                overrideTiming = p.overrideTiming,
                timing = p.timing,
            });
        }
        return s;
    }
}
