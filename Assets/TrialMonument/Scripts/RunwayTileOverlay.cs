using UnityEngine;

/// <summary>
/// Lives on one cube of a portal runway (DynamicPathGeneratorPortalTiles.cs). Keeps that cube's square
/// tile lines - a thin raised square inset on each of its four long faces, the paving of the reference -
/// riding on PathDepthShading's visual proxy, so they grow, shrink, fade and hide with the cube.
///
/// The lines are a separate unlit renderer (PathDepthShading colours the whole cube), so this reads the
/// colour the shading last gave the cube's proxy and paints the lines a darker shade of it: the tiles
/// follow the route colour (blue on the way in, red beyond the door) and its fade, through a dither
/// rather than an alpha, so nothing pops.
/// </summary>
[DefaultExecutionOrder(60)] // after PathDepthShading (50) has set this frame's colour and scale
public class RunwayTileOverlay : MonoBehaviour
{
    public MeshRenderer tiles;
    [Range(0.2f, 1f)] public float seamShade = 0.6f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int FadeId = Shader.PropertyToID("_Fade");

    Transform visual;
    Renderer visualRenderer;
    MaterialPropertyBlock cubeBlock, tileBlock;

    void LateUpdate()
    {
        if (tiles == null) return;

        if (visual == null)
        {
            visual = transform.Find("PathVisual");   // PathDepthShading's proxy, made the first time it sees the cube
            if (visual != null)
            {
                visualRenderer = visual.GetComponent<Renderer>();
                tiles.transform.SetParent(visual, false);   // inherit its shrink/grow
            }
        }

        // With shading on, nothing shows until its proxy exists (no pop-in).
        bool show = false;
        if (visual != null && visualRenderer != null && visualRenderer.enabled)
        {
            if (cubeBlock == null) cubeBlock = new MaterialPropertyBlock();
            visualRenderer.GetPropertyBlock(cubeBlock);
            Color c = cubeBlock.isEmpty ? Color.white : cubeBlock.GetColor(BaseColorId);
            if (c.a > 0.02f)
            {
                show = true;
                if (tileBlock == null) tileBlock = new MaterialPropertyBlock();
                tiles.GetPropertyBlock(tileBlock);
                tileBlock.SetColor(ColorId, new Color(c.r * seamShade, c.g * seamShade, c.b * seamShade, 1f));
                tileBlock.SetFloat(FadeId, Mathf.Clamp01(c.a));
                tiles.SetPropertyBlock(tileBlock);
            }
        }
        if (tiles.enabled != show) tiles.enabled = show;
    }
}
