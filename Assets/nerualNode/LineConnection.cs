using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class LineConnection : MonoBehaviour
{
    public NodeView nodeA;
    public NodeView nodeB;

    private LineRenderer lr;
    private float flowPhase;
    private const int Segments = 16;
    private const float Curvature = 0.035f;
    private const float LineWidth = 0.0055f;

    void Awake() => lr = GetComponent<LineRenderer>();

    public void Initialize(NodeView a, NodeView b, Material mat)
    {
        nodeA = a;
        nodeB = b;
        flowPhase = Random.Range(0f, Mathf.PI * 2f);

        if (lr == null) lr = GetComponent<LineRenderer>();
        lr.sharedMaterial = mat ?? new Material(Shader.Find("Sprites/Default"));
        lr.startWidth = LineWidth;
        lr.endWidth = LineWidth;
        lr.positionCount = Segments + 1;
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.generateLightingData = false;
        UpdateLine();
    }

    void Update()
    {
        flowPhase += Time.deltaTime * 1.2f;
        UpdateLine();
    }

    public void UpdateLine()
    {
        if (nodeA == null || nodeB == null)
        {
            if (Application.isPlaying) Destroy(gameObject);
            return;
        }
        if (lr == null) return;

        Vector3 start = nodeA.transform.position;
        Vector3 end = nodeB.transform.position;
        float r = (start.magnitude + end.magnitude) * 0.5f;

        Vector3 midDir = (start + end).magnitude < 0.001f ? Vector3.up : (start + end).normalized;
        Vector3 mid = midDir * r * (1f + Curvature);

        if (lr.positionCount != Segments + 1) lr.positionCount = Segments + 1;

        for (int i = 0; i <= Segments; i++)
        {
            float t = i / (float)Segments;
            Vector3 p = Vector3.Lerp(Vector3.Lerp(start, mid, t), Vector3.Lerp(mid, end, t), t);
            lr.SetPosition(i, p.normalized * r);
        }

        // --- Moving Highlight Gradient ---
        Color colorA = LineColor(nodeA);
        Color colorB = LineColor(nodeB);
        
        float hPos = (flowPhase * 0.4f) % 1.0f;
        float hWidth = 0.12f;

        Gradient gradient = new Gradient();
        
        // Define 5 keys to handle the wrapping highlight nicely
        GradientColorKey[] colorKeys = new GradientColorKey[5];
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[5];

        float[] samplePoints = { 0.0f, Mathf.Clamp01(hPos - hWidth), hPos, Mathf.Clamp01(hPos + hWidth), 1.0f };
        for (int i = 0; i < 5; i++)
        {
            float t = samplePoints[i];
            Color baseC = Color.Lerp(colorA, colorB, t);
            float dist = Mathf.Abs(t - hPos);
            if (dist > 0.5f) dist = 1.0f - dist; // wrap check (simple)

            float strength = Mathf.Max(0, 1f - (dist / hWidth));
            strength = strength * strength; // sharper peak

            Color finalC = Color.Lerp(baseC, Color.white, strength * 0.7f);
            float finalA = Mathf.Lerp(baseC.a, 0.6f, strength);

            colorKeys[i] = new GradientColorKey(finalC, t);
            alphaKeys[i] = new GradientAlphaKey(finalA, t);
        }

        gradient.SetKeys(colorKeys, alphaKeys);
        lr.colorGradient = gradient;
    }

    Color LineColor(NodeView node)
    {
        if (node.isNeutral)
            return new Color(0.9f, 0.95f, 1f, 0.15f);
        Color c = node.nodeColor;
        return new Color(c.r, c.g, c.b, 0.35f);
    }
}
