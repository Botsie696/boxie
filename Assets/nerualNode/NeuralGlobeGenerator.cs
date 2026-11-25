using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class NeuralGlobeGenerator : MonoBehaviour
{
    public GameObject nodePrefab;
    public int nodeCount = 110;
    public int coloredPairCount = 7;
    public float radius = 5f;
    public int maxConnections = 4;
    public float nodeScale = 0.075f;
    public bool level2 = false;

    public Color[] nodePalette = new Color[]
    {
        new Color(1.000f, 0.231f, 0.188f), // Red
        new Color(1.000f, 0.584f, 0.000f), // Orange
        new Color(1.000f, 0.839f, 0.039f), // Yellow
        new Color(0.196f, 0.843f, 0.294f), // Lime
        new Color(0.000f, 0.784f, 0.325f), // Green
        new Color(0.000f, 0.898f, 1.000f), // Cyan
        new Color(0.039f, 0.518f, 1.000f), // Blue
        new Color(0.345f, 0.337f, 0.839f), // Indigo
        new Color(0.686f, 0.322f, 0.871f), // Purple
        new Color(1.000f, 0.176f, 0.333f)  // Pink
    };

    [HideInInspector] public Material nodeMaterial;
    [HideInInspector] public Material lineMaterial;

    private List<NodeView> allNodes = new List<NodeView>();

    void Awake()
    {
        // Dark/transparent background
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 1f);
            mainCam.clearFlags = CameraClearFlags.SolidColor;
        }

        // Build a node material
        Shader nodeShader = Shader.Find("Standard")
                         ?? Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Sprites/Default");
        nodeMaterial = new Material(nodeShader);
        if (nodeMaterial.HasProperty("_EmissionColor"))
        {
            nodeMaterial.EnableKeyword("_EMISSION");
            nodeMaterial.SetColor("_EmissionColor", Color.white * 1.5f);
        }
        if (nodeMaterial.HasProperty("_Metallic"))  nodeMaterial.SetFloat("_Metallic", 0f);
        if (nodeMaterial.HasProperty("_Glossiness")) nodeMaterial.SetFloat("_Glossiness", 0.8f);
        if (nodeMaterial.HasProperty("_Smoothness")) nodeMaterial.SetFloat("_Smoothness", 0.8f);

        lineMaterial = new Material(Shader.Find("Sprites/Default"));
    }

    void Start()
    {
        GenerateWeb();
    }

    [ContextMenu("Generate Web")]
    public void GenerateWeb()
    {
        foreach (var n in allNodes)
            if (n != null) DestroyImmediate(n.gameObject);
        allNodes.Clear();

        List<Vector3> positions = FibonacciSphere(nodeCount, radius);

        // Assign colors: all neutral, then place coloredPairCount pairs
        bool[] isNeutralFlags = Enumerable.Repeat(true, nodeCount).ToArray();
        Color[] colorAssign  = Enumerable.Repeat(new Color(0.88f, 0.92f, 0.98f), nodeCount).ToArray();

        List<int> shuffled = Enumerable.Range(0, nodeCount).OrderBy(_ => Random.value).ToList();
        for (int i = 0; i < coloredPairCount && i < nodePalette.Length; i++)
        {
            int idx0 = shuffled[i * 2];
            int idx1 = shuffled[i * 2 + 1];
            colorAssign[idx0]  = nodePalette[i];
            colorAssign[idx1]  = nodePalette[i];
            isNeutralFlags[idx0] = false;
            isNeutralFlags[idx1] = false;
        }

        for (int i = 0; i < nodeCount; i++)
        {
            GameObject obj = Instantiate(nodePrefab, transform);
            obj.transform.localPosition = positions[i];
            obj.transform.localScale    = Vector3.one * nodeScale;
            obj.name = $"Node_{i}";

            var rend = obj.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = nodeMaterial;

            NodeView view  = obj.AddComponent<NodeView>();
            view.nodeColor = colorAssign[i];
            view.isNeutral = isNeutralFlags[i];
            view.Initialize();
            allNodes.Add(view);
        }

        ConnectNodes();
    }

    List<Vector3> FibonacciSphere(int n, float r)
    {
        var pts = new List<Vector3>(n);
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < n; i++)
        {
            float y    = 1f - (i / (float)(n - 1)) * 2f;
            float rad  = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float theta = goldenAngle * i;
            float x = Mathf.Cos(theta) * rad + Random.Range(-0.06f, 0.06f);
            float z = Mathf.Sin(theta) * rad + Random.Range(-0.06f, 0.06f);
            pts.Add(new Vector3(x, y, z).normalized * r);
}
        return pts;
    }

    void ConnectNodes()
    {
        // Max chord length that qualifies as "nearby" on a sphere of this radius
        float nearThreshold = radius * 0.55f;

        for (int i = 0; i < allNodes.Count; i++)
{
            NodeView a = allNodes[i];
            if (a.neighbors.Count >= maxConnections) continue;

            var candidates = allNodes
                .Where(b => b != a
                    && !a.neighbors.Contains(b)
                    && a.neighbors.Count < maxConnections
                    && b.neighbors.Count < maxConnections)
                .OrderBy(b => Vector3.Distance(a.transform.localPosition, b.transform.localPosition));

            foreach (var b in candidates)
            {
                if (a.neighbors.Count >= maxConnections) break;
                float dist = Vector3.Distance(a.transform.localPosition, b.transform.localPosition);
                if (dist > nearThreshold) break;
                CreateConnection(a, b);
            }
        }
    }

    public void CreateConnection(NodeView a, NodeView b)
    {
        if (a.neighbors.Contains(b) || b.neighbors.Contains(a)) return;
        if (a.neighbors.Count >= maxConnections || b.neighbors.Count >= maxConnections) return;

        GameObject lineObj = new GameObject($"Line_{a.name}_{b.name}");
        lineObj.transform.SetParent(transform);

        LineConnection conn = lineObj.AddComponent<LineConnection>();
        conn.Initialize(a, b, lineMaterial);

        a.AddConnection(b, conn);
        b.AddConnection(a, conn);
    }

    public List<NodeView> GetNodes() => allNodes;
}
