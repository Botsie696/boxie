using UnityEngine;

/// <summary>
/// TrialMonument only. Keeps a quad with the MonumentSky material filling the camera's view just
/// in front of the far plane, so the gradient and stars sit behind everything whatever the camera
/// does. The camera itself is never touched - the quad is a separate object that copies the
/// camera's pose each frame. Stars drift a little with the camera for a hint of parallax.
/// </summary>
[DefaultExecutionOrder(1000)] // after the camera rig has moved this frame
public class MonumentSkyBackdrop : MonoBehaviour
{
    public Camera targetCamera;
    public Material skyMaterial;
    [Tooltip("Distance in front of the far clip plane the quad is drawn at.")]
    public float farInset = 2f;
    [Tooltip("How far the stars drift per world unit the camera moves (0 = stars glued to the screen).")]
    [Range(0f, 1f)] public float starParallax = 0.08f;

    static readonly int StarOffsetId = Shader.PropertyToID("_StarOffset");
    static readonly int AspectId = Shader.PropertyToID("_Aspect");
    static readonly int DensityId = Shader.PropertyToID("_StarDensity");

    Transform quad;
    Material runtimeMat;

    void OnEnable()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (quad == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "MonumentSkyQuad";
            go.hideFlags = HideFlags.DontSave;
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (skyMaterial != null) runtimeMat = new Material(skyMaterial) { name = skyMaterial.name + " (Runtime)" };
            r.sharedMaterial = runtimeMat;
            quad = go.transform;
        }
        quad.gameObject.SetActive(true);
    }

    void OnDisable()
    {
        if (quad != null) quad.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (quad != null) Destroy(quad.gameObject);
        if (runtimeMat != null) Destroy(runtimeMat);
    }

    void LateUpdate()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null || quad == null) return;

        Transform ct = cam.transform;
        float d = Mathf.Max(cam.nearClipPlane + 0.1f, cam.farClipPlane - farInset);
        float h, w;
        if (cam.orthographic)
        {
            h = cam.orthographicSize * 2f;
        }
        else
        {
            h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }
        w = h * cam.aspect;

        quad.SetPositionAndRotation(ct.position + ct.forward * d, ct.rotation);
        quad.localScale = new Vector3(w * 1.02f, h * 1.02f, 1f);

        if (runtimeMat != null)
        {
            runtimeMat.SetFloat(AspectId, cam.aspect);
            // Stars are laid out in screen space (cells per screen height); shift them by the
            // camera's own movement across the view so they drift a touch as it pans.
            float cellsPerUnit = runtimeMat.GetFloat(DensityId) / Mathf.Max(0.01f, h);
            Vector3 p = ct.position;
            Vector2 off = new Vector2(Vector3.Dot(p, ct.right), Vector3.Dot(p, ct.up)) * cellsPerUnit * starParallax;
            runtimeMat.SetVector(StarOffsetId, off);
        }
    }
}
