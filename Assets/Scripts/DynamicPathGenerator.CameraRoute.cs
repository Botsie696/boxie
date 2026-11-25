using UnityEngine;

/// <summary>
/// Read-only route queries for the camera's A / B modes (CameraParentRotator.ZoneGlide.cs / .Follow.cs).
/// Same data source as GetPathAhead (the real cubes of every generated leg, drawn or not), but into
/// preallocated buffers, by arc length and by time, and with the turns marked. Nothing here writes
/// to the path, and no existing member is touched.
/// </summary>
public partial class DynamicPathGenerator
{
    public const int RouteMax = 480;
    public const int RouteCornerMax = 48;

    /// <summary>Route points: [0] = the runner, then the cubes ahead of it in order.</summary>
    public readonly Vector3[] RoutePts = new Vector3[RouteMax];
    /// <summary>Arc length from the runner to each point.</summary>
    public readonly float[] RouteArcs = new float[RouteMax];
    /// <summary>Index in RoutePts of the first cube of the leg after each upcoming turn (corner 0 = the next turn).</summary>
    public readonly int[] RouteCornerIdx = new int[RouteCornerMax];
    public int RouteCount { get; private set; }
    public int RouteCornerCount { get; private set; }
    /// <summary>Legs from the runner's own (0) to the first leg that is a boost or carries bridge planks; 99 = none within the route.</summary>
    public int RouteLegsToSpecial { get; private set; } = 99;

    int routeBuiltFrame = -1;
    Vector3 routeBuiltFor;

    /// <summary>Fills RoutePts / RouteArcs / RouteCornerIdx for the runner at runnerPos. Cached per frame and position.</summary>
    public int BuildRoute(Vector3 runnerPos)
    {
        if (routeBuiltFrame == Time.frameCount && (routeBuiltFor - runnerPos).sqrMagnitude < 1e-6f) return RouteCount;
        routeBuiltFrame = Time.frameCount;
        routeBuiltFor = runnerPos;

        RouteCount = 0;
        RouteCornerCount = 0;
        RouteLegsToSpecial = 99;
        if (allSegments.Count == 0) return 0;

        int s0 = Mathf.Clamp(currentPlayerSegmentIdx, 0, allSegments.Count - 1);

        // nearest cube among the runner's leg and the next, as GetPathAhead does
        int bestS = s0, bestC = 0;
        float bestD = float.MaxValue;
        for (int s = s0; s <= Mathf.Min(s0 + 1, allSegments.Count - 1); s++)
        {
            var cubes = allSegments[s].cubes;
            for (int c = 0; c < cubes.Count; c++)
            {
                if (cubes[c] == null) continue;
                float d = (cubes[c].transform.position - runnerPos).sqrMagnitude;
                if (d < bestD) { bestD = d; bestS = s; bestC = c; }
            }
        }

        RoutePts[0] = runnerPos;
        RouteArcs[0] = 0f;
        int n = 1;
        int legNo = 0;
        for (int s = bestS, c = bestC; s < allSegments.Count && n < RouteMax; s++, c = 0)
        {
            Segment seg = allSegments[s];
            bool newLeg = false;
            if (s > bestS)
            {
                legNo++;
                newLeg = Vector3.Dot(seg.direction, allSegments[s - 1].direction) < 0.9f;
            }
            if ((seg.boost && seg.portalRole == 0 || seg.plankCount > 0) && RouteLegsToSpecial == 99) RouteLegsToSpecial = legNo;

            var cubes = seg.cubes;
            bool first = true;
            for (; c < cubes.Count && n < RouteMax; c++)
            {
                if (cubes[c] == null) continue;
                Vector3 p = cubes[c].transform.position;
                // a cube behind the runner (the nearest one can be) adds nothing
                if (n == 1 && s == bestS && Vector3.Dot(p - runnerPos, seg.direction) < 0f) continue;
                if (first && newLeg && RouteCornerCount < RouteCornerMax) RouteCornerIdx[RouteCornerCount++] = n;
                first = false;
                RoutePts[n] = p;
                RouteArcs[n] = RouteArcs[n - 1] + Vector3.Distance(RoutePts[n - 1], p);
                n++;
            }
        }
        RouteCount = n;
        return n;
    }

    /// <summary>The runner's position along the route after `seconds` at `speed`, then samples at even time steps. Returns the count written (points and times arrays must be the same length).</summary>
    public int SampleRouteAhead(Vector3 runnerPos, float seconds, float speed, Vector3[] pts, float[] times)
    {
        BuildRoute(runnerPos);
        int want = Mathf.Min(pts.Length, times.Length);
        if (RouteCount < 2 || want == 0) return 0;
        int k = 1;
        for (int i = 0; i < want; i++)
        {
            float t = want == 1 ? 0f : seconds * i / (want - 1);
            float arc = Mathf.Min(speed * t, RouteArcs[RouteCount - 1]);
            while (k < RouteCount - 1 && RouteArcs[k] < arc) k++;
            float a0 = RouteArcs[k - 1], a1 = RouteArcs[k];
            float u = a1 > a0 ? Mathf.Clamp01((arc - a0) / (a1 - a0)) : 0f;
            pts[i] = Vector3.Lerp(RoutePts[k - 1], RoutePts[k], u);
            times[i] = t;
        }
        return want;
    }

    /// <summary>The next `count` turns, nearest first: world position of the first cube after each. Returns how many were found.</summary>
    public int GetUpcomingCorners(Vector3 runnerPos, int count, Vector3[] outPts)
    {
        BuildRoute(runnerPos);
        int n = Mathf.Min(count, Mathf.Min(outPts.Length, RouteCornerCount));
        for (int i = 0; i < n; i++) outPts[i] = RoutePts[RouteCornerIdx[i]];
        return n;
    }

    // ------------------------------------------------------------------
    // Route CENTRE LINE for the camera's D / E modes (CameraParentRotator.Rail.cs). The polyline runs through the
    // centres of the cubes (each leg's first and last cube, joined by the diagonal riser at every corner), over every
    // leg the generator still holds: the kept-behind zones and everything laid ahead. Its arc length s is GLOBAL:
    // culling legs behind the runner does not shift it, so an s held by the camera stays valid from frame to frame.
    // ------------------------------------------------------------------
    public const int CentreMax = 512;
    /// <summary>Centre-line vertices, oldest leg first.</summary>
    public readonly Vector3[] CentrePts = new Vector3[CentreMax];
    /// <summary>Global arc length of each vertex.</summary>
    public readonly float[] CentreS = new float[CentreMax];
    /// <summary>Index into the segment list of the leg each vertex belongs to (a riser vertex belongs to the leg it starts).</summary>
    public readonly int[] CentreLeg = new int[CentreMax];
    public int CentreCount { get; private set; }
    /// <summary>Bumped whenever the centre line restarts (new path); any s held from before is void.</summary>
    public int CentreEpoch { get; private set; }

    const int CentreLegMax = 256;
    readonly int[] centreLegSerial = new int[CentreLegMax];
    readonly float[] centreLegS0 = new float[CentreLegMax];
    readonly float[] centreLegS1 = new float[CentreLegMax];
    int centreLegCount, centreBuiltFrame = -1;
    float centreLastS;          // RouteSAt's last answer (monotonic)
    bool centreHaveS;

    /// <summary>Rebuilds the centre line (cached per frame). Returns the vertex count.</summary>
    public int BuildCentreLine()
    {
        if (centreBuiltFrame == Time.frameCount) return CentreCount;
        centreBuiltFrame = Time.frameCount;

        // where does the first leg we hold start, in global s? It was in the previous build unless the path was regenerated.
        float s = 0f;
        bool found = false;
        // the first leg that still has cubes (an emptied leg can linger at the front of the list; keying on it
        // would make every frame look like a new path and restart the camera's blend each frame)
        int firstSerial = -1;
        for (int i = 0; i < allSegments.Count; i++)
            if (allSegments[i].cubes.Count > 0) { firstSerial = allSegments[i].serial; break; }
        for (int i = 0; i < centreLegCount; i++)
            if (centreLegSerial[i] == firstSerial) { s = centreLegS0[i]; found = true; break; }
        if (!found) { centreHaveS = false; CentreEpoch++; }   // new path: s restarts, and the runner's s is found again from scratch

        int n = 0, legs = 0;
        for (int i = 0; i < allSegments.Count && n < CentreMax - 2 && legs < CentreLegMax; i++)
        {
            Segment seg = allSegments[i];
            if (seg.cubes.Count == 0) continue;
            Vector3 a = seg.startPosition, b = seg.endPosition;
            if (n > 0) s += Vector3.Distance(CentrePts[n - 1], a);      // the riser from the previous leg
            centreLegSerial[legs] = seg.serial; centreLegS0[legs] = s;
            CentrePts[n] = a; CentreS[n] = s; CentreLeg[n] = i; n++;
            float len = Vector3.Distance(a, b);
            if (len > 1e-3f) { s += len; CentrePts[n] = b; CentreS[n] = s; CentreLeg[n] = i; n++; }
            centreLegS1[legs] = s;
            legs++;
        }
        CentreCount = n;
        centreLegCount = legs;
        return n;
    }

    /// <summary>Global s of a leg's first / last cube centre (false if the leg is not held).</summary>
    public bool CentreLegRange(int serial, out float s0, out float s1)
    {
        for (int i = 0; i < centreLegCount; i++)
            if (centreLegSerial[i] == serial) { s0 = centreLegS0[i]; s1 = centreLegS1[i]; return true; }
        s0 = s1 = 0f;
        return false;
    }

    // (SegmentBySerial lives in DynamicPathGeneratorPower.cs.)

    /// <summary>Serial of the newest leg laid, -1 if none.</summary>
    public int LatestSerial => allSegments.Count > 0 ? allSegments[allSegments.Count - 1].serial : -1;

    public float CentreSMin =>CentreCount > 0 ? CentreS[0] : 0f;
    public float CentreSMax => CentreCount > 0 ? CentreS[CentreCount - 1] : 0f;

    /// <summary>Point on the centre line at global arc length s (clamped to the known line).</summary>
    public Vector3 CentrePointAt(float s)
    {
        int n = CentreCount;
        if (n == 0) return player != null ? player.transform.position : Vector3.zero;
        if (s <= CentreS[0]) return CentrePts[0];
        if (s >= CentreS[n - 1]) return CentrePts[n - 1];
        int lo = 0, hi = n - 1;
        while (hi - lo > 1) { int m = (lo + hi) >> 1; if (CentreS[m] <= s) lo = m; else hi = m; }
        float a = CentreS[lo], b = CentreS[hi];
        return Vector3.LerpUnclamped(CentrePts[lo], CentrePts[hi], b > a ? (s - a) / (b - a) : 0f);
    }

    /// <summary>
    /// s of the runner's projection on the centre line. Monotonic: it never decreases while the path stays the same
    /// (a regenerated path starts it again). The search stays near the last answer, so a parallel leg close by can
    /// never capture it.
    /// </summary>
    public float RouteSAt(Vector3 runnerPos)
    {
        BuildCentreLine();
        int n = CentreCount;
        if (n < 2) return 0f;
        float lo = centreHaveS ? centreLastS - 2f : float.MinValue;
        float hi = centreHaveS ? centreLastS + 25f : float.MaxValue;
        float best = float.MaxValue, bestS = centreHaveS ? centreLastS : CentreS[0];
        for (int i = 0; i < n - 1; i++)
        {
            if (CentreS[i + 1] < lo || CentreS[i] > hi) continue;
            Vector3 a = CentrePts[i], ab = CentrePts[i + 1] - a;
            float L2 = ab.sqrMagnitude;
            float u = L2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(runnerPos - a, ab) / L2) : 0f;
            float d = (a + ab * u - runnerPos).sqrMagnitude;
            if (d < best) { best = d; bestS = Mathf.Lerp(CentreS[i], CentreS[i + 1], u); }
        }
        if (centreHaveS && best > 64f) { centreHaveS = false; return RouteSAt(runnerPos); }   // lost (teleport): search all
        centreLastS = centreHaveS ? Mathf.Max(centreLastS, bestS) : bestS;
        centreHaveS = true;
        return centreLastS;
    }

    /// <summary>
    /// The route from the runner to upcoming corner `toCornerIndex`, plus `extraFractionOfNextLeg` (0..1) of
    /// the leg that follows it (by cube count). Returns how many points were written; fewer than needed
    /// if the route is not built that far.
    /// </summary>
    public int GetRouteSegment(Vector3 runnerPos, int toCornerIndex, float extraFractionOfNextLeg, Vector3[] outPts)
    {
        BuildRoute(runnerPos);
        int end = RouteCount;
        if (toCornerIndex >= 0 && toCornerIndex < RouteCornerCount)
        {
            int c = RouteCornerIdx[toCornerIndex];
            int next = toCornerIndex + 1 < RouteCornerCount ? RouteCornerIdx[toCornerIndex + 1] : RouteCount;
            end = c + Mathf.CeilToInt((next - c) * Mathf.Clamp01(extraFractionOfNextLeg));
        }
        end = Mathf.Min(end, RouteCount);
        int n = Mathf.Min(end, outPts.Length);
        for (int i = 0; i < n; i++) outPts[i] = RoutePts[i];
        return n;
    }
}
