using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The maths of one SLIDING obstacle-leg pillar group (MainScene prop camera 3.11 + Test Obstacles,
/// DynamicPathGeneratorObstacleLegDesigns.cs). Standing pillars are ordinary 3.10 poles (PlacePole); only the ones that
/// slide toward the runner need this.
///
/// Its place is a function of the RUNNER'S PROGRESS p along the leg (cubes from cube 0), never of the clock: the
/// runner's speed is constant on a leg, so a restart, a pause or a slow-down can never put it somewhere the generator
/// did not plan. The faces it blocks never change - one pillar per face in `faces` (bit s = face s, 0 up 1 right
/// 2 down 3 left) - only its place along the leg does:  z(p) = zStart - approach x p.
/// </summary>
public sealed class HazardPattern
{
    public float zStart;          // centre z at p <= 0 (cubes)
    public float approach;        // slides toward the runner at this x the runner's speed
    public float halfDepth = 0.5f; // half its length along the leg (cubes), for the kill window and for crossing checks
    public int faces;             // the faces it blocks

    public const float RunnerReach = 0.42f; // the runner's half length, a touch forgiving

    public float Reach => RunnerReach + halfDepth;

    public float ZAt(float p) => zStart - approach * Mathf.Max(0f, p);

    /// <summary>Progress at which the hazard is level with the runner.</summary>
    public float MeetP => zStart / (1f + approach);
}

/// <summary>
/// Lives on a sliding pillar group (named "ObstaclePole" so LegStageColors paints it in the stage's obstacle colour,
/// and parented to a cube of the leg so the zone's culling takes it along). Places itself on the leg every frame from
/// its HazardPattern, fades with the track under it (like PathObstacleGuard), and ends the run by the runner's POSITION
/// (like ObstacleLegPoleGuard: a trigger entered mid-roll would be missed).
/// </summary>
[DefaultExecutionOrder(61)]
public class ObstacleLegHazard : MonoBehaviour
{
    public static readonly List<ObstacleLegHazard> Active = new List<ObstacleLegHazard>();

    public CubeRailRunner player;
    public DynamicPathGenerator pathGen;
    public DynamicPathGenerator.Segment seg;
    public HazardPattern pattern;
    public PathObstacleGuard shieldGuard;  // disabled stand-in the runner's shield shatters (PathObstacleGuard.LastHit)

    PathDepthShading shading;

    void OnEnable() { Active.Add(this); }
    void OnDisable() { Active.Remove(this); }

    void Start()
    {
        if (pathGen != null) shading = pathGen.GetComponent<PathDepthShading>();
    }

    /// <summary>Where the runner meets this hazard, as a point on the leg's centre line (what the bot steers for).</summary>
    public Vector3 MeetPoint()
    {
        if (seg == null || seg.cubes.Count == 0 || seg.cubes[0] == null) return transform.position;
        Transform c0 = seg.cubes[0].transform;
        return c0.position + c0.forward * (pattern.MeetP * pathGen.cubeSize);
    }

    void Update()
    {
        if (player == null || pathGen == null || seg == null || pattern == null) return;
        int n = seg.cubes.Count;
        if (n == 0 || seg.cubes[0] == null) return;
        Transform c0 = seg.cubes[0].transform;
        float cs = Mathf.Max(0.01f, pathGen.cubeSize);

        // Runner's progress: 0 until it reaches the leg, huge once it is past (the hazard is then done).
        DynamicPathGenerator.Segment ps = pathGen.PlayerSegment;
        bool onLeg = ps == seg;
        float p;
        if (onLeg) p = Vector3.Dot(player.transform.position - c0.position, c0.forward) / cs;
        else if (ps != null && ps.serial < seg.serial) p = 0f;
        else p = n + 200f;

        float z = pattern.ZAt(p);
        transform.SetPositionAndRotation(c0.position + c0.forward * (z * cs), c0.rotation);

        // Drawn exactly with the track under it (it can never appear before the track reaches it, and it goes
        // away at the rate the leg behind the runner fades).
        int idx = Mathf.Clamp(Mathf.RoundToInt(z), 0, n - 1);
        GameObject host = seg.cubes[idx];
        float shown = 1f;
        if (shading != null && host != null)
        {
            shown = shading.ShownAmount(host);
            shown *= 1f - Mathf.SmoothStep(0f, 1f, shading.CrossedAmount(host));
        }
        float s = Mathf.SmoothStep(0f, 1f, shown);
        transform.localScale = Vector3.one * Mathf.Max(0.0001f, s);

        if (!onLeg || s < 0.5f) return;
        if (player.isFlying || player.IsRevolving || player.IsGameOver) return;
        if (Mathf.Abs(z - p) > pattern.Reach) return;

        Vector3 local = c0.InverseTransformPoint(player.transform.position);
        if (Mathf.Abs(local.x) > 1.8f || Mathf.Abs(local.y) > 1.8f) return;
        int side = Mathf.Abs(local.y) >= Mathf.Abs(local.x) ? (local.y > 0f ? 0 : 2) : (local.x > 0f ? 1 : 3);
        if ((pattern.faces & (1 << side)) == 0) return;
        PathObstacleGuard.LastHit = shieldGuard;
        player.HitObstacle();
    }
}
