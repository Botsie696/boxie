using UnityEngine;

/// <summary>
/// Lives on one pole of an obstacle leg (DynamicPathGeneratorObstacleLeg.cs, 3.9 only). Goes by the runner's
/// POSITION in the host cube's frame, like BridgePlankGuard, not by a trigger: a trigger entered mid-roll is
/// ignored and never fires again, so a roll that LANDS on the pole's face over the pole would slip through.
/// Over this cube, on the pole's face, not rolling, pole drawn in = the run ends against the pole
/// (CubeRailRunner.HitObstacle), and PathObstacleGuard.LastHit is set so the shield breaks this pole.
/// </summary>
[DefaultExecutionOrder(61)] // after PathObstacleGuard (60) has scaled the pole for this frame
public class ObstacleLegPoleGuard : MonoBehaviour
{
    public CubeRailRunner player;
    public PathObstacleGuard guard;
    public Transform host;
    public int blockedSide;

    [Tooltip("Half the runner's reach along the leg, in cubes, plus half the pole: closer than this to the pole's cube centre = touching.")]
    public float reach = 0.48f;

    void Update()
    {
        if (player == null || host == null || guard == null) return;
        if (player.isFlying || player.IsRevolving || player.IsGameOver) return;
        if (transform.localScale.y < 0.5f) return;   // not drawn in yet (or fading out behind)

        Vector3 local = host.InverseTransformPoint(player.transform.position);
        if (Mathf.Abs(local.z) > reach) return;
        if (Mathf.Abs(local.x) > 1.8f || Mathf.Abs(local.y) > 1.8f) return;   // another leg passing nearby

        int side = Mathf.Abs(local.y) >= Mathf.Abs(local.x) ? (local.y > 0f ? 0 : 2) : (local.x > 0f ? 1 : 3);
        if (side != blockedSide) return;
        PathObstacleGuard.LastHit = guard;
        player.HitObstacle();
    }
}
