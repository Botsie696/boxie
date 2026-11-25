using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "MainScene prop camera 4" only. Draws the Polybox rocks PathBackgroundGenerator builds with the
/// scene's flat look material (PropLooks/FlatFaceBlock) instead of the pack's lit material: the
/// rocks are low-poly and flat-shaded, so camera-relative flat colours turn every facet into a
/// crisp tone, like the carved orange crags of the reference, and they sink into the same height
/// fog as the track. Rocks are built off screen, so swapping the material as they appear is never
/// seen. Positions, sizes and the generator's own logic are untouched.
/// </summary>
[DefaultExecutionOrder(950)]
public class PropRockRestyle : MonoBehaviour
{
    public PathBackgroundGenerator source;
    public Material rockMaterial;
    [Tooltip("Seconds between checks for newly built rocks.")]
    [Range(0.05f, 1f)] public float interval = 0.15f;

    readonly HashSet<int> done = new HashSet<int>();
    readonly List<MeshRenderer> buffer = new List<MeshRenderer>();
    float next;

    void Start()
    {
        if (source == null) source = FindFirstObjectByType<PathBackgroundGenerator>();
    }

    void LateUpdate()
    {
        if (source == null || rockMaterial == null || Time.time < next) return;
        next = Time.time + interval;

        if (done.Count > 20000) done.Clear();
        source.GetComponentsInChildren(true, buffer);
        foreach (MeshRenderer r in buffer)
        {
            if (r == null || !done.Add(r.GetInstanceID())) continue;
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = rockMaterial;
            r.sharedMaterials = mats;
        }
    }
}
