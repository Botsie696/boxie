using UnityEngine;

/// <summary>
/// All of the runner's "soul" animation, played on a child hierarchy so the root's Rigidbody, collider and
/// isFlying raycasts never see it:
///
///   Root (Rigidbody, Collider, CubeRailRunner)
///    -> VisualWrapper   hop height, corner drift, squash / stretch scale
///     -> PivotContainer edge pivot for the vault, holds the roll about the heading
///      -> CubeMesh      the cube itself (moved here from the root) + the continuous forward pitch
///
/// The runner root takes a new face orientation instantly (CubeRailRunner.BeginRevolve); this rig starts from the
/// OLD orientation and plays the roll into it, so what you see is the vault, not a slerp.
/// Built at runtime in Awake, so every scene that has a CubeRailRunner gets it.
/// </summary>
public class RunnerVisualRig : MonoBehaviour
{
    [Header("1. Dice roll")]
    [Tooltip("Degrees of pitch per unit travelled. 0 = off (current). 90 = one quarter turn per cube length (true rolling, very fast). 27 = 0.3x, a relaxed roll.")]
    public float degreesPerUnit = 0f;

    [Header("2. Roll (left / right tap, double-tap flip)")]
    [Tooltip("The roll plays this many times longer than the physics roll (0.10s step / 0.12s flip). 1 = the visual keeps pace with the gameplay.")]
    public float jumpTimeScale = 1f;

    [Header("4. Cornering")]
    public float cornerDrift = 0.15f;
    public float cornerDuration = 0.35f;

    Transform wrapper, pivot, mesh;
    CubeRailRunner runner;

    float pitch;

    bool vaultActive;
    float vaultT, vaultDur = 0.15f, vaultArc, vaultCarry, lastPhi;

    float cornerT = -1f;
    int cornerSign;

    void Awake()
    {
        runner = GetComponent<CubeRailRunner>();
        wrapper = NewChild("VisualWrapper", transform);
        pivot = NewChild("PivotContainer", wrapper);
        mesh = NewChild("CubeMesh", pivot);
        mesh.gameObject.layer = gameObject.layer;

        // The cube itself moves off the root onto CubeMesh (the root keeps its Rigidbody and collider).
        MeshFilter mf = GetComponent<MeshFilter>();
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mf != null && mr != null)
        {
            mesh.gameObject.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            MeshRenderer nr = mesh.gameObject.AddComponent<MeshRenderer>();
            nr.sharedMaterials = mr.sharedMaterials;
            nr.shadowCastingMode = mr.shadowCastingMode;
            nr.receiveShadows = mr.receiveShadows;
            nr.lightProbeUsage = mr.lightProbeUsage;
            nr.reflectionProbeUsage = mr.reflectionProbeUsage;
            nr.motionVectorGenerationMode = mr.motionVectorGenerationMode;
            nr.renderingLayerMask = mr.renderingLayerMask;
            nr.sortingLayerID = mr.sortingLayerID;
            nr.sortingOrder = mr.sortingOrder;
            nr.enabled = mr.enabled;
            DestroyImmediate(mr);
            DestroyImmediate(mf);
        }

        // Existing cosmetic children (skins, overlays) ride on the cube. Anything with a live collider stays put.
        var kids = new System.Collections.Generic.List<Transform>();
        foreach (Transform c in transform) if (c != wrapper) kids.Add(c);
        foreach (Transform c in kids)
            if (c.GetComponentInChildren<MeshRenderer>(true) != null && !HasLiveCollider(c))
                c.SetParent(mesh, true);
    }

    static Transform NewChild(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static bool HasLiveCollider(Transform t)
    {
        foreach (Collider c in t.GetComponentsInChildren<Collider>(false))
            if (c.enabled) return true;
        return false;
    }

    /// <summary>A roll to a new face: arcDeg is the signed turn about the heading (about +-90, or 180 for a flip).</summary>
    public void BeginVault(float arcDeg, float duration, bool flip)
    {
        // A roll that starts before the last one settled carries its leftover angle, so nothing pops.
        vaultCarry = vaultActive ? lastPhi : 0f;
        vaultActive = true;
        vaultArc = arcDeg;
        vaultDur = Mathf.Max(0.01f, duration * jumpTimeScale);
        vaultT = 0f;
    }

    /// <summary>The path turned. turnSign: +1 = right turn, -1 = left turn.</summary>
    public void BeginCorner(int turnSign)
    {
        cornerSign = turnSign;
        cornerT = 0f;
    }

    // Close-call whip (CubeRailRunner rescue): the root has been put on the new leg; the visual starts where the
    // runner was and swings round the corner edge onto it on a quarter arc: offset(u) = o (cos t in + sin t out) - o out.
    float blendT = -1f, blendDur, blendDist;
    Vector3 blendIn, blendOut;

    /// <summary>Plays the rescued cube from where it was (overshoot `dist` past the corner along `inDir`) round to the root on `outDir`.</summary>
    public void BeginBlend(Vector3 inDir, Vector3 outDir, float dist, float seconds)
    {
        blendIn = inDir.normalized;
        blendOut = outDir.normalized;
        blendDist = dist;
        blendDur = Mathf.Max(0.01f, seconds);
        blendT = 0f;
    }

    /// <summary>Back to a clean, un-animated cube (restart / tutorial reset).</summary>
    public void ResetVisual()
    {
        blendT = -1f;
        vaultActive = false;
        cornerT = -1f;
        pitch = 0f;
        lastPhi = vaultCarry = 0f;
        Apply(Vector3.zero, Vector3.one, Quaternion.identity, Vector3.zero);
        mesh.localRotation = Quaternion.identity;
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);

    void LateUpdate()
    {
        float dt = Time.deltaTime;

        // 1. Dice roll: 90 degrees of pitch per cube length travelled, locked to the runner's speed.
        if (runner != null && runner.IsRunning)
            pitch += runner.CurrentMoveSpeed / runner.UnitSize * degreesPerUnit * dt;
        else
            pitch = Mathf.MoveTowards(pitch, Mathf.Round(pitch / 90f) * 90f, 360f * dt);
        pitch = Mathf.Repeat(pitch, 360f);
        mesh.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        Vector3 pos = Vector3.zero;
        Vector3 scale = Vector3.one;
        Quaternion roll = Quaternion.identity;
        Vector3 pivotPos = Vector3.zero;

        // 2 + 3. Plain roll round the edge onto the new face: no hop, squash, lean or overshoot.
        if (vaultActive)
        {
            vaultT += dt;
            float u = Mathf.Clamp01(vaultT / vaultDur);
            float phi = (-vaultArc + vaultCarry) * (1f - Smooth(u)); // starts at the old orientation, lands on the new one
            lastPhi = phi;
            roll = Quaternion.AngleAxis(phi, Vector3.forward);

            if (u >= 1f)
            {
                vaultActive = false;
                lastPhi = vaultCarry = 0f;
            }
        }


        // 4. Cornering: drift to the outside of the turn and spring back.
        if (cornerT >= 0f)
        {
            float u = Mathf.Clamp01(cornerT / Mathf.Max(0.01f, cornerDuration));
            pos.x += -cornerSign * cornerDrift * Mathf.Sin(Mathf.PI * u);
            cornerT += dt;
            if (u >= 1f) cornerT = -1f;
        }

        if (blendT >= 0f)
        {
            float u = Mathf.Clamp01(blendT / blendDur);
            float th = Smooth(u) * Mathf.PI * 0.5f;
            Vector3 worldOffset = blendDist * (Mathf.Cos(th) * blendIn + Mathf.Sin(th) * blendOut) - blendDist * blendOut;
            pos += transform.InverseTransformVector(worldOffset);
            blendT += dt;
            if (u >= 1f) blendT = -1f;
        }

        Apply(pos, scale, roll, pivotPos);
    }

    void Apply(Vector3 pos, Vector3 scale, Quaternion roll, Vector3 pivotPos)
    {
        wrapper.localPosition = pos;
        wrapper.localScale = scale;
        pivot.localPosition = pivotPos;
        pivot.localRotation = roll;
        mesh.localPosition = -pivotPos;
    }
}
