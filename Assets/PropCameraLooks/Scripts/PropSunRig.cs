using UnityEngine;

/// <summary>
/// "MainScene prop camera 5" only. Keeps the sun at a fixed direction relative to the camera instead
/// of the world, so every shadow (the track's and the tower's on the floor, the rocks') falls the same
/// way on screen - toward the lower left, like the long shadows of the reference - however far the
/// camera rig has yawed along the path. Only the sun light is rotated; the camera and everything else
/// are untouched.
/// </summary>
[DefaultExecutionOrder(1000)]
public class PropSunRig : MonoBehaviour
{
    public Light sun;
    [Tooltip("Sun elevation above the horizon, degrees (lower = longer shadows).")]
    [Range(10f, 80f)] public float pitch = 38f;
    [Tooltip("Sun yaw relative to the camera's yaw. -135 sends shadows toward the lower left of the screen.")]
    public float yawOffset = -135f;
    [Tooltip("Seconds the sun takes to follow a camera yaw swing.")]
    [Range(0.01f, 3f)] public float smoothTime = 0.35f;

    float yaw, yawVel;
    bool placed;

    void Start()
    {
        if (sun == null) sun = RenderSettings.sun;
        if (sun == null)
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.isActiveAndEnabled) { sun = l; break; }
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (sun == null || cam == null) return;
        float target = cam.transform.eulerAngles.y + yawOffset;
        if (!placed) { yaw = target; placed = true; }
        yaw = Mathf.SmoothDampAngle(yaw, target, ref yawVel, smoothTime);
        sun.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}
