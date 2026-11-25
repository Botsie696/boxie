using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TrialMonument only. A large horizontal plane with the MonumentShadowFloor material that sits a
/// fixed gap below the lowest track on screen and follows the runner across, so the track always
/// casts a soft cool-blue shadow "onto the ground" like the structure in the reference image.
/// The plane itself is invisible; only the shadow shows.
/// </summary>
[DefaultExecutionOrder(60)] // after PathDepthShading has updated what is visible
public class MonumentShadowFloor : MonoBehaviour
{
    public DynamicPathGenerator pathGen;
    public PathDepthShading shading;
    public Material floorMaterial;
    [Tooltip("World units between the lowest visible cube and the floor.")]
    public float gapBelowTrack = 6f;
    [Tooltip("Size of the plane (world units per side).")]
    public float size = 240f;
    [Tooltip("Seconds the floor takes to settle at a new height.")]
    public float heightSmoothing = 1.2f;

    static readonly int FadeCenterId = Shader.PropertyToID("_FadeCenter");

    Transform plane;
    Material runtimeMat;
    readonly List<Vector3> visible = new List<Vector3>();
    float y, yVel;
    bool placed;

    void Start()
    {
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (shading == null && pathGen != null) shading = pathGen.GetComponent<PathDepthShading>();

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "MonumentShadowFloorQuad";
        go.hideFlags = HideFlags.DontSave;
        Destroy(go.GetComponent<Collider>());
        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = true;
        if (floorMaterial != null) runtimeMat = new Material(floorMaterial) { name = floorMaterial.name + " (Runtime)" };
        r.sharedMaterial = runtimeMat;
        plane = go.transform;
        plane.rotation = Quaternion.Euler(90f, 0f, 0f);
        plane.localScale = new Vector3(size, size, 1f);
    }

    void OnDestroy()
    {
        if (plane != null) Destroy(plane.gameObject);
        if (runtimeMat != null) Destroy(runtimeMat);
    }

    void LateUpdate()
    {
        if (plane == null || pathGen == null) return;

        Vector3 focus = pathGen.player != null ? pathGen.player.transform.position
                      : pathGen.firstCube != null ? pathGen.firstCube.position : transform.position;

        float lowest = focus.y;
        if (shading != null)
        {
            visible.Clear();
            shading.CollectVisible(visible);
            foreach (Vector3 p in visible) lowest = Mathf.Min(lowest, p.y);
        }
        float target = lowest - gapBelowTrack;

        if (!placed) { y = target; placed = true; }
        // Drop quickly (a new leg going down must never poke through), rise slowly.
        if (target < y) y = Mathf.Lerp(y, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
        else y = Mathf.SmoothDamp(y, target, ref yVel, heightSmoothing);

        plane.position = new Vector3(focus.x, y, focus.z);
        if (runtimeMat != null) runtimeMat.SetVector(FadeCenterId, focus);
    }
}
