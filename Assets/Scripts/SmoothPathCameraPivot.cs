using UnityEngine;
using System.Collections.Generic;

public class SmoothPathCameraPivot : MonoBehaviour
{
    [Header("References")]
    public Transform target;
    public DynamicPathGenerator pathGen;

    [Header("Movement Smoothing")]
    [Range(0.1f, 10f)]
    public float positionSmoothing = 2.0f; // Slower for "calming" feel
    [Range(0.1f, 5f)]
    public float rotationSmoothing = 0.5f; // Very slow rotation

    [Header("Path Visibility")]
    [Tooltip("How many path points to consider for framing")]
    public int pointsToConsider = 8;
    
    private float targetYRotation;
    private int lastPathGenCount = -1;
    private Vector3 currentVelocity;

    void Start()
    {
        if (target == null)
        {
            var player = GameObject.Find("MainPlayer") ?? GameObject.Find("Player");
            if (player != null) target = player.transform;
        }

        if (pathGen == null)
            pathGen = Object.FindFirstObjectByType<DynamicPathGenerator>();

        if (target != null)
        {
            transform.position = target.position;
            targetYRotation = transform.eulerAngles.y;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        // 1. Smoothly follow target position using SmoothDamp for more organic feel
        transform.position = Vector3.SmoothDamp(transform.position, target.position, ref currentVelocity, 1f / positionSmoothing);

        // 2. Update target rotation when path changes
        if (pathGen != null && pathGen.pathGenerationCount != lastPathGenCount)
        {
            lastPathGenCount = pathGen.pathGenerationCount;
            UpdateTargetRotation();
        }

        // 3. Smoothly rotate to target Y
        float currentY = transform.eulerAngles.y;
        float newY = Mathf.LerpAngle(currentY, targetYRotation, Time.deltaTime * rotationSmoothing);
        transform.rotation = Quaternion.Euler(0, newY, 0);
    }

    void UpdateTargetRotation()
    {
        List<Vector3> points = pathGen.GetPathKeyPositions();
        if (points.Count < 2) return;

        // Find where the player is in the path to get the upcoming points
        int startIndex = 0;
        float minDist = float.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            float d = Vector3.Distance(target.position, points[i]);
            if (d < minDist)
            {
                minDist = d;
                startIndex = i;
            }
        }

        int count = Mathf.Min(points.Count - startIndex, pointsToConsider);
        if (count < 2) count = Mathf.Min(points.Count, pointsToConsider); // Fallback to start if player is at end
        
        List<Vector3> upcomingPoints = points.GetRange(startIndex, count);
        targetYRotation = ComputeBestYRotation(upcomingPoints);
    }

    float ComputeBestYRotation(List<Vector3> points)
    {
        float bestSpread = -1f;
        float bestAngle = 0f;

        // Sweep angles to find which one shows the most path width (Right axis)
        for (int deg = 0; deg < 360; deg += 5) 
        {
            float rad = deg * Mathf.Deg2Rad;
            float rx = Mathf.Cos(rad);
            float rz = -Mathf.Sin(rad);

            float minP = float.MaxValue, maxP = float.MinValue;
            foreach (var p in points)
            {
                float proj = p.x * rx + p.z * rz;
                if (proj < minP) minP = proj;
                if (proj > maxP) maxP = proj;
            }

            float spread = maxP - minP;
            if (spread > bestSpread)
            {
                bestSpread = spread;
                bestAngle = deg;
            }
        }

        // We want the camera to be positioned such that it looks at the centroid.
        // bestAngle represents the 'Right' vector of the camera view that maximizes spread.
        // The camera 'Forward' should be orthogonal to this.
        
        Vector3 centroid = Vector3.zero;
        foreach (var p in points) centroid += p;
        centroid /= points.Count;

        Vector3 toCentroid = new Vector3(centroid.x - target.position.x, 0f, centroid.z - target.position.z).normalized;
        if (toCentroid.magnitude < 0.01f) toCentroid = target.forward;

        float angle1 = (bestAngle - 90f);
        float angle2 = (bestAngle + 90f);

        float r1 = angle1 * Mathf.Deg2Rad;
        Vector3 fwd1 = new Vector3(Mathf.Sin(r1), 0f, Mathf.Cos(r1));
        
        float r2 = angle2 * Mathf.Deg2Rad;
        Vector3 fwd2 = new Vector3(Mathf.Sin(r2), 0f, Mathf.Cos(r2));

        // Choose the angle that faces towards the path
        return Vector3.Dot(fwd1, toCentroid) >= Vector3.Dot(fwd2, toCentroid) ? angle1 : angle2;
    }
}
