using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DECOY BRANCHES. Once the score is past decoyAfterLeg, a corner has a decoyChance of being a
/// fork: besides the real turn, a second stub of 3-4 cubes leaves the same corner in another
/// direction and simply ends. It is built exactly like the real turn (same diagonal riser), so the
/// runner can take it, and then runs off the end into the ordinary fall / game over.
///
/// The stub belongs to the real leg that starts at the fork (Segment.decoyCubes). It is not a
/// Segment of its own - so no zone, no score, no coins, no gems - and PathDepthShading draws it
/// with the same fade / see-through / crossed-leg treatment as the real leg beside it.
/// Uses its own System.Random, so the path's UnityEngine.Random sequence is untouched.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Decoy Branches")]
    [Tooltip("At some corners a short dead-end stub leaves beside the real turn. Taking it ends the run.")]
    public bool decoyBranches = true;
    [Tooltip("Chance (0-1) that a corner forks into a decoy, once the score is past decoyAfterLeg. 0.2 = 20%.")]
    [Range(0f, 1f)] public float decoyChance = 0.2f;
    [Tooltip("Decoys only appear on legs after this many points of score.")]
    [Range(0, 200)] public int decoyAfterLeg = 20;
    [Tooltip("Fewest cubes in a decoy stub.")]
    [Range(1, 10)] public int decoyMinCubes = 3;
    [Tooltip("Most cubes in a decoy stub.")]
    [Range(1, 10)] public int decoyMaxCubes = 4;

    [Tooltip("Decoy chance (0-1) per checkpoint: element 0 = checkpoint 0 (start), 1 = after the first door, ... The last element holds for every later checkpoint. Overrides decoyChance.")]
    public float[] decoyChanceByCheckpoint = { 0f, 0.5f };

    /// <summary>Decoy chance for legs laid at the current checkpoint.</summary>
    float CurrentDecoyChance
    {
        get
        {
            float byProfile = difficultyProfile != null ? difficultyProfile.DecoyChanceOverride(cycleIndex) : -1f;
            if (byProfile >= 0f) return Mathf.Clamp01(byProfile);
            if (decoyChanceByCheckpoint == null || decoyChanceByCheckpoint.Length == 0) return decoyChance;
            int i = Mathf.Clamp(Checkpoint, 0, decoyChanceByCheckpoint.Length - 1);
            return Mathf.Clamp01(decoyChanceByCheckpoint[i]);
        }
    }

    private System.Random decoyRng;

    void TryPlaceDecoy(Segment seg, Vector3 prevDir, Vector3 trueDir)
    {
        if (!decoyBranches || CurrentDecoyChance <= 0f) return;
        if (GateBlocksDecoy()) return;   // a triangle gate leg (3.6 only) has no fork at either corner
        decoyRng ??= new System.Random();
        Segment before = allSegments.Count > 0 ? allSegments[allSegments.Count - 1] : null;

        if (Waves)
        {
            // DifficultyProfile: the waves and the hazard budget decide (BUILD phase only, never stacked on a bridge).
            if (!WaveDecoyAllowed(before, seg, out _)) return;
        }
        else
        {
            if (segmentSerial <= decoyAfterLeg) return;
            if (decoyRng.NextDouble() >= CurrentDecoyChance) return;
        }

        int lo = Mathf.Max(1, Mathf.Min(decoyMinCubes, decoyMaxCubes));
        int hi = Mathf.Max(lo, Mathf.Max(decoyMinCubes, decoyMaxCubes));
        int n = decoyRng.Next(lo, hi + 1);

        Vector3 origin = seg.startPosition - trueDir * cubeSize;   // where the real leg's first cube branches from
        bool upOk = upWeight > 0, downOk = downWeight > 0;

        var candidates = new List<Vector3>();
        foreach (Vector3 d in AxisDirs)
        {
            if (!Perpendicular(d, prevDir)) continue;          // a real 90 degree turn, like any other
            if (Vector3.Dot(d, trueDir) > 0.9f) continue;      // not the real one
            if (!VerticalAllowed(d, upOk, downOk)) continue;
            if (!IsVertical(d) && lastHorizontalDirection.sqrMagnitude > 0.01f &&
                Vector3.Dot(d, lastHorizontalDirection) < -0.9f) continue;   // never doubling back on the path
            candidates.Add(d);
        }

        // Shuffle, then take the first that keeps clear of the older track.
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = decoyRng.Next(0, i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        foreach (Vector3 dir in candidates)
        {
            if (!DecoyIsClear(origin, dir, n)) continue;

            seg.decoyCubes = new List<GameObject>();
            Vector3 p = origin;
            for (int i = 0; i < n; i++)
            {
                p += dir * cubeSize;
                GameObject cube = Instantiate(cubePrefab, p, GetRotationForDir(dir));
                cube.name = $"DecoyCube_S{allSegments.Count}_C{i}";
                cube.transform.SetParent(transform);
                cube.SetActive(true);
                seg.decoyCubes.Add(cube);
                occupiedCells[CellOf(p)] = segmentSerial;   // later legs keep clear of it
            }
            if (difficultyProfile != null) OnDecoyLaid(before, seg);
            return;
        }
    }

    // Like LegIsClear, but the leg the fork leaves (segmentSerial - 1) and the real leg beside it
    // (segmentSerial, already laid) are exempt: the stub always starts next to both.
    bool DecoyIsClear(Vector3 origin, Vector3 dir, int n)
    {
        int c = Mathf.Max(1, pathClearance);
        for (int i = 0; i <= n; i++)
        {
            Vector3Int cell = CellOf(origin + dir * (i * cubeSize));
            for (int dx = -c; dx <= c; dx++)
                for (int dy = -c; dy <= c; dy++)
                    for (int dz = -c; dz <= c; dz++)
                        if (occupiedCells.TryGetValue(cell + new Vector3Int(dx, dy, dz), out int owner) &&
                            owner != segmentSerial - 1 && owner != segmentSerial)
                            return false;
        }
        return true;
    }

    void DestroyDecoy(Segment seg)
    {
        if (seg == null || seg.decoyCubes == null) return;
        foreach (GameObject cube in seg.decoyCubes)
            if (cube != null) Destroy(cube);
        seg.decoyCubes = null;
    }
}
