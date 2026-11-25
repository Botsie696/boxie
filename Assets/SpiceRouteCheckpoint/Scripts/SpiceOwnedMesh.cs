using UnityEngine;

/// <summary>Destroys a runtime-built mesh together with the object that shows it (a Spice Route cliff top's merged
/// dressing goes when the background generator retires its rock).</summary>
public class SpiceOwnedMesh : MonoBehaviour
{
    public Mesh mesh;

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }
}
