using UnityEngine;

/// <summary>
/// What the DifficultyProfile hands to the rest of the scene at start (camera caps, the runner's fair input,
/// feedback FX, telemetry), the corner query CubeRailRunner's exact corner check uses, and the close-call
/// bonus points. Every entry point is a no-op without a profile.
/// </summary>
public partial class DynamicPathGenerator
{
    /// <summary>Bonus points from close calls this run (DifficultyProfile.rescueBonusPoints each).</summary>
    public int BonusPoints { get; private set; }
    /// <summary>The number on screen: one per leg reached plus the close-call bonus. Score alone drives the speed and the waves.</summary>
    public int DisplayScore => Mathf.Max(0, Score + BonusPoints - ScoreBase);
    /// <summary>Fired with the bonus just added (for the +1 popup).</summary>
    public event System.Action<int> BonusAwarded;

    public void AddBonus(int points)
    {
        if (points <= 0) return;
        BonusPoints += points;
        BonusAwarded?.Invoke(points);
    }

    // ---- Close-call streak: back-to-back legs with a close call pay +1, +2, +3 ... a leg without one resets to +1.
    /// <summary>Close calls in a row, one per leg (0 = streak broken). The next one pays CloseCallStreak + 1.</summary>
    public int CloseCallStreak { get; private set; }
    /// <summary>Diamonds collected this run (the streak value per close call). They stay when the streak breaks.</summary>
    public int Diamonds { get; private set; }
    /// <summary>Fired per close call with (points paid, streak, diamonds total).</summary>
    public event System.Action<int, int, int> CloseCallAwarded;
    bool closeCallSincePrevCorner;

    /// <summary>Called when the runner lands a corner: a leg that ended with no close call since the last corner breaks the streak.</summary>
    public void NoteCornerLanded()
    {
        if (!closeCallSincePrevCorner) CloseCallStreak = 0;
        closeCallSincePrevCorner = false;
    }

    /// <summary>A close call: diamonds equal to the streak length (1, 2, 3 ...). Not added to the score.</summary>
    public void AwardCloseCall()
    {
        CloseCallStreak++;
        Diamonds += CloseCallStreak;     // the streak pays diamonds only; the score stays one per leg
        closeCallSincePrevCorner = true;
        CloseCallAwarded?.Invoke(CloseCallStreak, CloseCallStreak, Diamonds);
    }

    void ResetCloseCallStreak()
    {
        CloseCallStreak = 0;
        Diamonds = 0;
        closeCallSincePrevCorner = false;
    }

    void ApplyProfileOverrides()
    {
        BonusPoints = 0;
        var P = difficultyProfile;
        if (P == null) return;
        if (player == null) player = FindFirstObjectByType<CubeRailRunner>();
        if (player != null) player.ApplyProfile(P, this);

        // CheckpointDifficultyProfile (scene 3.7): gates and obstacles can be forced off by the profile itself.
        if (P is CheckpointDifficultyProfile cdp)
        {
            if (cdp.noGates) gateSections = false;
            if (cdp.noObstacles) obstacleSections = false;
        }

        if (P.cameraOverrides)
        {
            // Runtime only: the scene keeps its authored values.
            boostCameraZoomOut = P.boostZoom;
            boostCameraZoomInSeconds = P.boostZoomIn;
            boostCameraZoomOutSeconds = P.boostZoomOut;
            var rig = FindFirstObjectByType<CameraParentRotator>();
            if (rig != null) rig.speedZoomMax = Mathf.Min(rig.speedZoomMax, P.speedZoomMax);
        }

        if (P.showRealDeadline)
        {
            // The chevrons and the face lane end where the corner is decided, not at the cube's far edge.
            float inset = Mathf.Max(0f, 0.5f - P.cornerCommitOffset);
            var arrows = GetComponent<TurnFaceArrows>();
            if (arrows != null) arrows.cornerGap = Mathf.Max(arrows.cornerGap, inset);
            var lane = player != null ? player.GetComponent<ActiveFaceHighlight>() : null;
            if (lane != null) lane.endInsetCubes = inset;
        }

        if (P.feedbackFX)
        {
            var arrows = GetComponent<TurnFaceArrows>();
            if (arrows != null) arrows.holdCue = true;
            if (player != null && player.GetComponent<CloseCallFX>() == null) player.gameObject.AddComponent<CloseCallFX>();
        }
        if (P.telemetry && GetComponent<RunTelemetry>() == null)
            gameObject.AddComponent<RunTelemetry>();
    }

    /// <summary>
    /// The corner at the end of the leg whose first cube is `legCube` (or the opening leg, for firstCube):
    /// the next leg's first cube, both headings, the centre of this leg's last cube and the leg itself.
    /// False on a portal runway (a door, not a corner) or if the next leg is not laid yet.
    /// </summary>
    public bool TryGetCornerAfter(Transform legCube, out Transform nextCube, out Vector3 inDir, out Vector3 outDir,
                                  out Vector3 lastCubePos, out Segment leg)
    {
        nextCube = null; inDir = outDir = lastCubePos = Vector3.zero; leg = null;
        if (legCube == null || allSegments.Count == 0) return false;

        int s = -1;
        int from = Mathf.Clamp(currentPlayerSegmentIdx - 1, 0, allSegments.Count - 1);
        for (int pass = 0; pass < 2 && s < 0; pass++)
        {
            int a = pass == 0 ? from : 0, b = pass == 0 ? Mathf.Min(allSegments.Count, from + 4) : allSegments.Count;
            for (int i = a; i < b; i++)
            {
                var c = allSegments[i].cubes;
                if (c.Count > 0 && c[0] != null && c[0].transform == legCube) { s = i; break; }
            }
        }
        if (s < 0 && legCube == firstCube) s = 0;
        if (s < 0 || s + 1 >= allSegments.Count) return false;

        leg = allSegments[s];
        Segment next = allSegments[s + 1];
        if (leg.portalRole == 1 || next.portalRole == 2 || leg.cubes.Count == 0 || next.cubes.Count == 0) return false;
        if (!IsDirectionChange(s + 1)) return false;
        GameObject last = leg.cubes[leg.cubes.Count - 1];
        if (last == null || next.cubes[0] == null) return false;

        nextCube = next.cubes[0].transform;
        inDir = leg.direction;
        outDir = next.direction;
        lastCubePos = last.transform.position;
        return true;
    }

    /// <summary>The leg after `leg`, or null.</summary>
    public Segment SegmentAfter(Segment leg)
    {
        int i = allSegments.IndexOf(leg);
        return i >= 0 && i + 1 < allSegments.Count ? allSegments[i + 1] : null;
    }
}
