using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A thin line of light along the stretch of track the runner is about to take - brightest at
/// the next cube, dimming and thinning further along, and gone past a few cubes. It starts just
/// ahead of the runner, so it is erased as they move onto it, and every cube along it brightens
/// as the runner closes in.
///
/// The line is lifted a little TOWARD the camera from the cube centres. In an orthographic view
/// moving along the view direction does not move anything on screen, so the line still runs
/// exactly down the middle of each beam - but it now sits in front of its own cube instead of
/// buried inside it, while beams that genuinely cross in front of it still hide it.
///
/// Owned and configured by DynamicPathGenerator (the Guide Trail settings); this class only draws.
/// </summary>
public class PathGuideTrail
{
    private readonly LineRenderer line;
    private readonly List<Vector3> points = new List<Vector3>();
    private readonly Material ownMaterial;
    private readonly Gradient gradient = new Gradient();
    private Color gradientColor;
    private bool gradientBuilt;

    public PathGuideTrail(Transform parent, Material material)
    {
        var go = new GameObject("GuideTrail");
        go.transform.SetParent(parent, false);

        line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        line.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.5f); // thins toward the far end
        line.positionCount = 0;

        if (material == null)
        {
            // Any unlit shader that honours vertex colour and alpha will do.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("PathGuideTrail: no unlit shader found - assign one to Guide Trail Material.");
                shader = Shader.Find("Hidden/InternalErrorShader");
            }

            ownMaterial = new Material(shader) { name = "GuideTrail (runtime)" };
            material = ownMaterial;
        }
        line.sharedMaterial = material;
    }

    public void Hide()
    {
        if (line != null) line.enabled = false;
    }

    public void Dispose()
    {
        if (line != null) Object.Destroy(line.gameObject);
        if (ownMaterial != null) Object.Destroy(ownMaterial);
    }

    /// <summary>
    /// Draws the part of the route polyline between arc distances from and from + length.
    /// route[0] is the centre of the cube under the runner; lift is added to every point.
    /// </summary>
    public void Draw(List<Vector3> route, float from, float length, float width, Color color, Vector3 lift)
    {
        if (line == null) return;

        points.Clear();
        float to = from + length;
        float walked = 0f;

        for (int i = 0; i + 1 < route.Count && walked < to; i++)
        {
            Vector3 a = route[i];
            Vector3 b = route[i + 1];
            float span = Vector3.Distance(a, b);
            if (span < 0.0001f) continue;

            float start = walked;
            float end = walked + span;
            if (end > from && start < to)
            {
                if (points.Count == 0) points.Add(Vector3.Lerp(a, b, Mathf.Clamp01((from - start) / span)));
                points.Add(Vector3.Lerp(a, b, Mathf.Clamp01((to - start) / span)));
            }
            walked = end;
        }

        if (points.Count < 2)
        {
            Hide();
            return;
        }

        for (int i = 0; i < points.Count; i++) points[i] += lift;

        if (!gradientBuilt || gradientColor != color) BuildGradient(color);

        line.enabled = true;
        line.widthMultiplier = width;
        line.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
    }

    // Bright at the next cube, darker and fainter along the way, fully faded by the end.
    void BuildGradient(Color c)
    {
        Color far = Color.Lerp(c, Color.black, 0.55f);
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(c, 0f),
                new GradientColorKey(Color.Lerp(c, far, 0.5f), 0.4f),
                new GradientColorKey(far, 1f),
            },
            new[]
            {
                new GradientAlphaKey(c.a, 0f),
                new GradientAlphaKey(c.a * 0.7f, 0.3f),
                new GradientAlphaKey(c.a * 0.4f, 0.7f),
                new GradientAlphaKey(c.a * 0.2f, 1f),
            });

        line.colorGradient = gradient;
        gradientColor = c;
        gradientBuilt = true;
    }
}
