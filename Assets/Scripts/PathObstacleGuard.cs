using UnityEngine;

/// <summary>
/// Lives on a spawned obstacle (DynamicPathGeneratorObstacle.cs). Drawn exactly like the cube it
/// stands on: PathDepthShading only tracks path cubes, so this reads the host cube's ShownAmount /
/// CrossedAmount every frame (same formula DynamicPathGeneratorCoins.cs uses for coins) and scales
/// itself in step - it cannot appear before the track reaches it, and it shrinks away and disables
/// itself at the same rate the leg behind the runner fades out. Destruction rides along for free:
/// it is parented to the path cube, so DynamicPathGenerator culling that cube takes this with it.
///
/// The collider (always a trigger, on a layer the ground probe never scans - see
/// DynamicPathGeneratorObstacle.obstacleLayerName) is disabled while hidden, so an obstacle that has
/// not been drawn in yet can never kill the runner.
///
/// Ending the run: OnTriggerEnter calls CubeRailRunner.HitObstacle(), which stops the runner dead
/// against the obstacle (instead of the usual fly-off-the-edge window) and hands straight to
/// GameOverSequence, so it shatters right there like hitting a wall instead of sailing through it.
/// </summary>
[DefaultExecutionOrder(60)] // after PathDepthShading (50) has set this frame's ShownAmount/CrossedAmount
public class PathObstacleGuard : MonoBehaviour
{
    public CubeRailRunner player;

    /// <summary>The cube face (0 up, 1 right, 2 down, 3 left - CubeRailRunner.currentSideIndex's
    /// convention) this obstacle sits on, so AutoPlayer knows which side to stay off.</summary>
    public int blockedSide;

    /// <summary>The obstacle that last ended (or tried to end) the run, for the shield's save.</summary>
    public static PathObstacleGuard LastHit;

    GameObject hostCube;
    PathDepthShading shading;
    Vector3 baseScale;
    Collider[] cols;
    Light glow;
    float baseGlowIntensity;
    bool visible = true;   // forced true until Init runs, so it never flashes on for one frame

    /// <summary>Wires this guard to the cube it rides on and the look it should fade with.</summary>
    public void Init(GameObject hostCube, PathDepthShading shading, Vector3 baseScale, Collider[] cols, Light glow)
    {
        this.hostCube = hostCube;
        this.shading = shading;
        this.baseScale = baseScale;
        this.cols = cols;
        this.glow = glow;
        baseGlowIntensity = glow != null ? glow.intensity : 0f;
        visible = false;
        ApplyVisibility(0f);
    }

    void LateUpdate()
    {
        if (hostCube == null) { Destroy(gameObject); return; }   // its cube was culled underneath it

        float shown = shading != null ? shading.ShownAmount(hostCube) : 1f;
        if (shading != null) shown *= 1f - Mathf.SmoothStep(0f, 1f, shading.CrossedAmount(hostCube));
        ApplyVisibility(shown);
    }

    void ApplyVisibility(float shown)
    {
        bool show = shown > 0.01f;
        if (show != visible)
        {
            visible = show;
            foreach (Collider c in cols) if (c != null) c.enabled = show;
        }

        float scale = Mathf.SmoothStep(0f, 1f, shown);
        transform.localScale = baseScale * scale;
        if (glow != null) glow.intensity = baseGlowIntensity * scale;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!visible) return;
        if (player == null || player.isFlying || player.IsRevolving || player.IsGameOver) return;
        if (other.GetComponentInParent<CubeRailRunner>() != player) return;
        LastHit = this;
        player.HitObstacle();
    }
}
