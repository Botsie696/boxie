using UnityEngine;

/// <summary>
/// TrialCamera scene only. Replaces the runner's left/right mapping with a plain 2D-screen test.
///
/// On every tap the runner has two local roll options: toward its local right (+1) and toward its
/// local left (-1). A point a little way along each is projected with Camera.WorldToScreenPoint;
/// the one with the lower screen X is what a LEFT tap does, the higher one what a RIGHT tap does.
///
/// It plugs into CubeRailRunner without editing it: cameraRelativeControls is switched off (so the
/// runner's own mapping is out of the way), every tap is swallowed through tutorialInputFilter, and
/// the mapped roll goes back in through QueueRoll. A double-tap becomes two rolls the same way,
/// which lands on the opposite face just as the flip does.
/// </summary>
[DefaultExecutionOrder(-50)]
public class TrialCameraControls : MonoBehaviour
{
    [Tooltip("How far along each local side the test points sit, in runner-widths.")]
    public float probeDistance = 1f;

    private CubeRailRunner runner;
    private float lastTapTime = -10f;
    private int lastTapDir;
    private const float DoubleTapWindow = 0.25f;

    void Start()
    {
        runner = GetComponent<CubeRailRunner>();
        if (runner == null) runner = FindFirstObjectByType<CubeRailRunner>();
        if (runner == null) { enabled = false; return; }

        runner.cameraRelativeControls = false;
        runner.forceTapMode = true;
        runner.tutorialInputFilter = OnTap;
    }

    void OnDestroy()
    {
        if (runner != null && runner.tutorialInputFilter == (System.Func<int, bool>)OnTap)
            runner.tutorialInputFilter = null;
    }

    // Always returns false: the runner's own queueing is bypassed and QueueRoll does the work.
    bool OnTap(int screenDir)
    {
        int roll = RollForScreenTap(screenDir);

        bool doubleTap = screenDir == lastTapDir && Time.unscaledTime - lastTapTime <= DoubleTapWindow;
        if (doubleTap)
        {
            runner.QueueRoll(roll);
            lastTapDir = 0;
        }
        else
        {
            runner.QueueRoll(roll);
            lastTapDir = screenDir;
            lastTapTime = Time.unscaledTime;
        }
        return false;
    }

    /// <summary>Roll direction (+1 = runner's local right, -1 = local left) that the tap (-1 left, +1 right) means on screen.</summary>
    int RollForScreenTap(int screenDir)
    {
        Camera cam = Camera.main;
        if (cam == null || runner.currentCube == null) return screenDir;

        // The runner's frame as the roll will use it: heading along the cube, up = the face it is on.
        // (transform.right would swing mid-roll.)
        Vector3 forward = runner.currentCube.forward;
        Vector3 up = runner.SideNormal(runner.currentSideIndex);
        Vector3 right = Vector3.Cross(up, forward).normalized;

        Vector3 origin = runner.transform.position;
        Vector3 leftPoint = origin - right * probeDistance;
        Vector3 rightPoint = origin + right * probeDistance;

        float leftX = cam.WorldToScreenPoint(leftPoint).x;
        float rightX = cam.WorldToScreenPoint(rightPoint).x;

        // Local right is the one that lands further right on screen -> a right tap rolls local right.
        if (Mathf.Abs(rightX - leftX) < 0.5f) return screenDir; // side-on to the view: no screen meaning
        bool localRightIsScreenRight = rightX > leftX;
        return localRightIsScreenRight ? screenDir : -screenDir;
    }
}
