using UnityEngine;

public class StarField : MonoBehaviour
{
    [SerializeField] private int starCount = 250;
    [SerializeField] private float radius = 75f;
    [SerializeField] private float minSize = 0.06f;
    [SerializeField] private float maxSize = 0.28f;

    private Transform camTransform;

    void Start()
    {
        camTransform = Camera.main != null ? Camera.main.transform : null;

        Mesh quad = CreateQuadMesh();

        Material mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = Color.white;

        for (int i = 0; i < starCount; i++)
        {
            var star = new GameObject("Star");
            star.transform.SetParent(transform, false);
            star.transform.localPosition = Random.onUnitSphere * radius;

            float size = Random.Range(minSize, maxSize);
            star.transform.localScale = Vector3.one * size;

            var mf = star.AddComponent<MeshFilter>();
            mf.sharedMesh = quad;

            var mr = star.AddComponent<MeshRenderer>();
            // Each star gets a tinted copy so they vary slightly
            float brightness = Random.Range(0.55f, 1f);
            float blueShift = Random.Range(0f, 0.15f);
            var starMat = new Material(mat);
            starMat.color = new Color(brightness, brightness, Mathf.Min(1f, brightness + blueShift));
            mr.sharedMaterial = starMat;
        }
    }

    void LateUpdate()
    {
        // Stars always surround the camera
        if (camTransform != null)
            transform.position = camTransform.position;
    }

    static Mesh CreateQuadMesh()
    {
        var mesh = new Mesh { name = "StarQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        mesh.uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1) };
        return mesh;
    }
}
