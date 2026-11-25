using UnityEngine;

/// <summary>
/// "MainScene prop camera 1/2/3" only. Feeds the look shaders (PropLooks/FlatFaceBlock) the one
/// thing they cannot work out for themselves: the height the runner is at, as the global
/// _PropFogRefY. Their height fog is measured down from it, so whatever hangs below the runner
/// sinks into the backdrop colour while the track the runner is on stays clear - whether the path
/// has climbed or fallen. The height follows the runner smoothly (dropping a little faster than it
/// rises) so the fog never jumps at a corner.
///
/// Nothing here touches the camera, the path generator or the runner.
/// </summary>
[DefaultExecutionOrder(900)]
public class PropLookController : MonoBehaviour
{
    public DynamicPathGenerator pathGen;
    [Tooltip("Seconds the fog reference takes to follow the runner up.")]
    [Range(0.05f, 5f)] public float riseSmoothTime = 1.2f;
    [Tooltip("Seconds the fog reference takes to follow the runner down.")]
    [Range(0.05f, 5f)] public float dropSmoothTime = 0.6f;
    [Tooltip("Added to the runner's height (positive = the fog line sits higher).")]
    public float heightOffset = 0f;
    [Tooltip("Optional: kept under the runner every frame (scene 4's rising sparks). Empty = nothing follows.")]
    public Transform followRunner;
    [Tooltip("Offset from the runner (x/z) and from the fog reference height (y) for followRunner.")]
    public Vector3 followOffset = new Vector3(0f, -9f, 0f);

    [Header("Runner face highlight (scene 4)")]
    [Tooltip("Lights the rail face the runner is on (needs _RunnerGlowStrength on the block materials) so the player can always tell which side they are on. Off = nothing changes.")]
    public bool highlightRunnerFace = false;
    [Tooltip("Radius, in cubes, of the lit patch of the face around the runner.")]
    [Range(1f, 8f)] public float highlightRadius = 3.5f;
    [Tooltip("Seconds the glow takes to come back up after the runner rolls onto another face (it dips briefly so the change is felt).")]
    [Range(0.01f, 0.5f)] public float highlightRefade = 0.12f;
    [Tooltip("Optional: material using PropLooks/RunnerXRay. Draws a glowing silhouette of the runner wherever the rail hides it.")]
    public Material runnerXRayMaterial;

    static readonly int FogRefId = Shader.PropertyToID("_PropFogRefY");
    static readonly int RunnerPId = Shader.PropertyToID("_PropRunnerP");
    static readonly int RunnerNId = Shader.PropertyToID("_PropRunnerN");

    float y, yVel;
    bool placed;

    float glowOn;
    int lastSide = -1;
    Transform lastCube;
    Vector3 glowPoint, glowNormal = Vector3.up;
    MeshRenderer xrayRenderer, xraySource;

    void Start()
    {
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
    }

    // The face under the runner: its world normal, and the point on that face right beneath the runner.
    void UpdateRunnerFace()
    {
        CubeRailRunner p = pathGen != null ? pathGen.player : null;
        bool live = highlightRunnerFace && p != null && p.currentCube != null && p.IsRunning && !p.isFlying;
        if (live)
        {
            int side = ((p.currentSideIndex % 4) + 4) % 4;
            Vector3 n = p.SideNormal(side);
            if (side != lastSide || p.currentCube != lastCube) { glowOn = Mathf.Min(glowOn, 0.35f); lastSide = side; lastCube = p.currentCube; }
            glowNormal = n;
            // The point of that face right under the runner, found the way the runner grounds itself:
            // straight into the face. (currentCube can be on another leg, so its height is not used.)
            Vector3 from = p.transform.position;
            if (Physics.Raycast(from, -n, out RaycastHit hit, 1.6f, ~0, QueryTriggerInteraction.Ignore) && Vector3.Dot(hit.normal, n) > 0.9f)
                glowPoint = hit.point;
            else
                glowPoint = from - n * 0.5f; // mid-roll or off the rail: assume it rests on the face
            glowOn = Mathf.MoveTowards(glowOn, 1f, Time.deltaTime / Mathf.Max(0.01f, highlightRefade));
        }
        else
        {
            glowOn = Mathf.MoveTowards(glowOn, 0f, Time.deltaTime / 0.15f);
            lastSide = -1;
        }
        Shader.SetGlobalVector(RunnerPId, new Vector4(glowPoint.x, glowPoint.y, glowPoint.z, glowOn));
        Shader.SetGlobalVector(RunnerNId, new Vector4(glowNormal.x, glowNormal.y, glowNormal.z, highlightRadius));
        UpdateXRay(p);
    }

    // A second draw of the runner's cube that only shows where the rail is in front of it.
    void UpdateXRay(CubeRailRunner p)
    {
        if (runnerXRayMaterial == null || p == null) return;
        if (xraySource == null)
        {
            foreach (MeshRenderer mr in p.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material m = mr.sharedMaterial;
                if (m == null || m.shader == null || m.shader.name != "PropLooks/FlatFaceBlock") continue;
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                xraySource = mr;
                var go = new GameObject("RunnerXRay") { layer = mr.gameObject.layer };
                go.transform.SetParent(mr.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                xrayRenderer = go.AddComponent<MeshRenderer>();
                xrayRenderer.sharedMaterial = runnerXRayMaterial;
                xrayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                xrayRenderer.receiveShadows = false;
                break;
            }
        }
        if (xrayRenderer != null && xraySource != null)
            xrayRenderer.enabled = highlightRunnerFace && xraySource.enabled && xraySource.gameObject.activeInHierarchy;
    }

    void LateUpdate()
    {
        float target = transform.position.y;
        if (pathGen != null && pathGen.player != null) target = pathGen.player.transform.position.y;
        else if (pathGen != null && pathGen.firstCube != null) target = pathGen.firstCube.position.y;
        target += heightOffset;

        if (!placed) { y = target; placed = true; }
        y = Mathf.SmoothDamp(y, target, ref yVel, target < y ? dropSmoothTime : riseSmoothTime);
        Shader.SetGlobalFloat(FogRefId, y);

        UpdateRunnerFace();

        if (followRunner != null && pathGen != null && pathGen.player != null)
        {
            Vector3 p = pathGen.player.transform.position;
            followRunner.position = new Vector3(p.x + followOffset.x, y + followOffset.y, p.z + followOffset.z);
        }
    }

    void OnDisable()
    {
        placed = false;
        // Leave nothing lit for the next scene.
        Shader.SetGlobalVector(RunnerPId, Vector4.zero);
        if (xrayRenderer != null) Destroy(xrayRenderer.gameObject);
        xrayRenderer = null;
        xraySource = null;
        glowOn = 0f;
    }
}
