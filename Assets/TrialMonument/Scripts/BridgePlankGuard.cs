using UnityEngine;

/// <summary>
/// Lives on one cube of a bridge's narrow stretch (DynamicPathGeneratorBridge.cs).
///
/// Ends the run on the wrong side. The cube keeps an ordinary full box collider (landing and the
/// forward probe work as on any other leg), so this is what makes the open sides deadly: while the
/// runner is over THIS cube and not on allowedSide, it sets isFlying, which CubeRailRunner turns into
/// a game over after FailFlyingSeconds, the same as running off the end of the track.
///
/// It goes by the runner's POSITION, not player.currentCube: currentCube only changes at corners
/// (the forward probe only ever hits the next leg's first cube), so mid-leg it is still the leg's
/// first cube and never this one.
///
/// Also keeps the plank's black edge outline (a separate renderer, since PathDepthShading colours
/// the whole cube) riding on PathDepthShading's visual proxy, so it grows, shrinks and hides with it.
/// </summary>
[DefaultExecutionOrder(60)] // after PathDepthShading (50) has set this frame's visual scale
public class BridgePlankGuard : MonoBehaviour
{
    public int allowedSide;
    [Tooltip("Shared by every cube of one plank (the runner may be rescued once per plank).")]
    public int plankId;
    public CubeRailRunner player;
    public PathDepthShading shading;
    public MeshRenderer border;

    Transform visual;
    Renderer visualRenderer;

    void Update()
    {
        if (player == null || player.isFlying || player.IsRevolving || player.IsGameOver) return;

        // Runner position in this cube's frame: z along the leg, x/y around the beam (in cube units,
        // the same frame CubeRailRunner snaps to - it rides at radius ~1.05).
        Vector3 local = transform.InverseTransformPoint(player.transform.position);
        if (Mathf.Abs(local.z) > 0.5f) return;                               // not over this cube
        if (Mathf.Abs(local.x) > 1.8f || Mathf.Abs(local.y) > 1.8f) return;  // another leg passing nearby

        int side = Mathf.Abs(local.y) >= Mathf.Abs(local.x) ? (local.y > 0f ? 0 : 2) : (local.x > 0f ? 1 : 3);
        if (side == allowedSide) return;
        // DifficultyProfile: a correct roll onto the plank a moment late is a close call, not a fall. The runner
        // decides (and falls if it is not rescued). Without a profile: off the plank is off the track, as before.
        if (player.HandlesPlankMiss) player.ReportPlankMiss(plankId, allowedSide, (local.z + 0.5f) * transform.lossyScale.z);
        else { player.PlankFallSide = allowedSide; player.PlankFallTime = Time.time; player.isFlying = true; }
    }

    void LateUpdate()
    {
        if (border == null) return;

        if (visual == null)
        {
            visual = transform.Find("PathVisual");   // PathDepthShading's proxy, made the first time it sees the cube
            if (visual != null)
            {
                visualRenderer = visual.GetComponent<Renderer>();
                border.transform.SetParent(visual, false);   // inherit its shrink/grow
            }
        }

        bool show = shading == null;   // with shading on, nothing shows until its proxy exists (no pop-in)
        if (visual != null)
        {
            show = visualRenderer != null && visualRenderer.enabled;
            if (show && shading != null)
                show = shading.ShownAmount(gameObject) > 0.35f && shading.CrossedAmount(gameObject) < 0.5f;
        }
        if (border.enabled != show) border.enabled = show;
    }
}
