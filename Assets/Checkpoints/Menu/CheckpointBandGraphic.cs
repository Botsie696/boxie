using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One slanted band of the MainUi checkpoint stack: a RawImage showing its legs' RenderTexture through the
/// UI/CheckpointBand shader, with its own material instance (made once) for the per-band values. Taps only count inside
/// the parallelogram (ICanvasRaycastFilter), so the slanted overlaps between neighbours always go to the band under them.
/// </summary>
public class CheckpointBandGraphic : RawImage, ICanvasRaycastFilter
{
    static readonly int RectPxId = Shader.PropertyToID("_RectPx"), SlopeId = Shader.PropertyToID("_Slope"), BandHId = Shader.PropertyToID("_BandH");
    static readonly int FlareId = Shader.PropertyToID("_SeamFlare"), BurstId = Shader.PropertyToID("_Burst"), DangerId = Shader.PropertyToID("_Danger");
    static readonly int BrightId = Shader.PropertyToID("_Bright"), DesatId = Shader.PropertyToID("_Desat"), GrainId = Shader.PropertyToID("_Grain");
    static readonly int BlackId = Shader.PropertyToID("_Black"), TeaseId = Shader.PropertyToID("_TeaseColor"), ExpandId = Shader.PropertyToID("_Expand");
    static readonly int ParallaxId = Shader.PropertyToID("_Parallax"), AlphaId = Shader.PropertyToID("_Alpha"), DrawSeamId = Shader.PropertyToID("_DrawSeam");
    static readonly int DividerColorId = Shader.PropertyToID("_DividerColor"), DividerAlphaId = Shader.PropertyToID("_DividerAlpha"), SweepId = Shader.PropertyToID("_Sweep");

    [System.NonSerialized] public float slope = 0.158f;        // tan(9 degrees): rise per px of width
    [System.NonSerialized] public float bandHeight = 215f;      // vertical distance between the two edges
    [System.NonSerialized] public float expand;                 // 0..1: the launch transition (whole rect, no mask)

    Material instance;

    public void Init(Material shared)
    {
        instance = new Material(shared) { name = shared.name + " (" + name + ")" };
        material = instance;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (instance != null) Destroy(instance);
    }

    public Material Instance => instance;

    public void PushShape()
    {
        if (instance == null) return;
        Rect r = rectTransform.rect;
        instance.SetVector(RectPxId, new Vector4(r.width, r.height, 0f, 0f));
        instance.SetFloat(SlopeId, slope);
        instance.SetFloat(BandHId, bandHeight);
        instance.SetFloat(ExpandId, expand);
    }

    public void SetLook(float bright, float desat, float grain, float black, Color tease)
    {
        if (instance == null) return;
        instance.SetFloat(BrightId, bright);
        instance.SetFloat(DesatId, desat);
        instance.SetFloat(GrainId, grain);
        instance.SetFloat(BlackId, black);
        instance.SetColor(TeaseId, tease);
    }

    public void SetFx(float press, float burst, float danger, float parallax, float alpha, float sweep)
    {
        if (instance == null) return;
        instance.SetFloat(FlareId, press);
        instance.SetFloat(BurstId, burst);
        instance.SetFloat(DangerId, danger);
        instance.SetFloat(ParallaxId, parallax);
        instance.SetFloat(AlphaId, alpha);
        instance.SetFloat(SweepId, sweep);
    }

    public void SetDivider(Color tint, float alpha)
    {
        if (instance == null) return;
        instance.SetColor(DividerColorId, tint);
        instance.SetFloat(DividerAlphaId, alpha);
    }

    public void SetDrawSeam(bool on) { if (instance != null) instance.SetFloat(DrawSeamId, on ? 1f : 0f); }

    public bool ContainsLocal(Vector2 p)
    {
        Rect r = rectTransform.rect;
        if (p.x < 0f || p.x > r.width) return false;
        float k = Mathf.Lerp(slope, 0f, expand);
        float h = Mathf.Lerp(bandHeight, r.height, expand);
        float yb = p.x * k;
        return p.y >= yb && p.y <= yb + h;
    }

    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out Vector2 local)) return false;
        Rect r = rectTransform.rect;
        return ContainsLocal(local - r.min);
    }
}
