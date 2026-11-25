using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Seeded, automated checks of the last-moment rescue (DifficultyProfile close call). Add it to any object in
/// Play mode in a scene with a profile (or tick runOnStart); it drives the runner itself through
/// CubeRailRunner.SimulateTap, exactly like screen taps, and logs PASS / FAIL / SKIP per case:
///
///   a) correct 1 tap 60 ms late ............ rescued, lands on the next leg
///   b) correct 1 tap 120 ms late ........... falls
///   c) wrong direction 30 ms late .......... falls
///   d) 2-roll turn, second tap 70 ms late .. rescued
///   e) 3 rolls (the long way) in the window  falls
///   f) late on two boost legs in a row ..... both rescued (new leg each time)
///   g) late correct roll onto a plank ...... rescued; wrong plank face late ... falls
///   h) committed to a decoy stub ........... no rescue
///   i) at boost top speed (~38 u/s) ........ a 90 ms-late tap is still rescued (overshoot cap)
///
/// Between cases it steers correctly by itself. A case whose leg never comes up within its time limit is SKIP.
/// Editor tooling only: nothing in the game adds it.
/// </summary>
public class RescueSelfTest : MonoBehaviour
{
    public bool runOnStart = true;
    public int seed = 3;
    [Tooltip("Seconds a case may wait for a suitable leg before it is skipped.")]
    public float caseTimeout = 90f;
    [Range(1f, 4f)] public float timeScale = 1f;
    public List<string> results = new List<string>();

    CubeRailRunner runner;
    DynamicPathGenerator gen;
    bool steering = true;
    float lastSteerTap = -10f;

    void Start()
    {
        runner = FindFirstObjectByType<CubeRailRunner>();
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        if (runOnStart) StartCoroutine(RunAll());
    }

    void Update()
    {
        if (runner == null || gen == null) return;
        if (Time.timeScale > 0.5f) Time.timeScale = timeScale;
        if (runner.IsWaitingToStart) runner.BeginRun();
        if (steering) Steer();
    }

    // ------------------------------------------------------------------ helpers

    bool Corner(out int inner, out Vector3 outDir)
    {
        inner = -1; outDir = Vector3.zero;
        if (runner.currentCube == null) return false;
        var cs = gen.GetUpcomingCorners(1);
        if (cs.Count == 0 || Vector3.Dot(runner.currentCube.forward, cs[0].incomingDirection.normalized) < 0.9f) return false;
        outDir = cs[0].outgoingDirection.normalized;
        inner = runner.SideFacing(outDir);
        return true;
    }

    float Along() => Vector3.Dot(runner.transform.position - runner.currentCube.position, runner.currentCube.forward) / gen.cubeSize;

    // Next face this leg needs: a plank ahead (or the one under the runner), else the corner.
    int NeededFace(out bool onPlank)
    {
        onPlank = false;
        var seg = gen.PlayerSegment;
        float a = runner.currentCube != null ? Along() : 0f;
        if (seg != null)
            foreach (var p in seg.planks)
            {
                if (a > p.y + 0.5f) continue;
                onPlank = a >= p.x - 0.6f;
                return p.z;
            }
        return Corner(out int inner, out _) ? inner : -1;
    }

    void Steer()
    {
        if (!runner.IsRunning || runner.IsRevolving || runner.RescuePending || Time.time - lastSteerTap < 0.16f) return;
        int face = NeededFace(out bool onPlank);
        if (face < 0 || face == runner.currentSideIndex || onPlank) return;
        TapToward(face);
    }

    void TapToward(int face)
    {
        int rolls = DynamicPathGenerator.Rolls(runner.currentSideIndex, face);
        int dir = runner.TapDirectionToReach(rolls == 1 ? face : (runner.currentSideIndex + 1) % 4);
        runner.SimulateTap(dir == 0 ? 1 : dir);
        lastSteerTap = Time.time;
    }

    IEnumerator Fresh()
    {
        if (runner.IsGameOver) { yield return new WaitForSecondsRealtime(1.2f); runner.Restart(); }
        while (!runner.IsRunning) yield return null;
        yield return new WaitForSeconds(0.3f);
    }

    // Waits for a leg (just landed) that matches, with the runner NOT steered on it. False on timeout.
    IEnumerator WaitForLeg(System.Func<bool> match, bool[] ok)
    {
        float until = Time.realtimeSinceStartup + caseTimeout;
        Transform seen = null;
        while (Time.realtimeSinceStartup < until)
        {
            if (runner.IsGameOver) yield return Fresh();
            if (runner.IsRunning && runner.currentCube != seen && runner.SecondsToCorner > 0.35f)
            {
                seen = runner.currentCube;
                if (match()) { steering = false; ok[0] = true; yield break; }
            }
            yield return null;
        }
        ok[0] = false;
    }

    // Holds until the corner is `late` seconds past its commit point.
    IEnumerator UntilAfterCommit(float late)
    {
        float t = Time.time + runner.SecondsToCorner + late;
        while (Time.time < t && !runner.IsGameOver) yield return null;
    }

    // Outcome: true = survived with a new rescue, false = fell (or no rescue).
    IEnumerator Outcome(int rescuesBefore, bool[] rescued)
    {
        float t = Time.time + 0.4f;
        while (Time.time < t && !runner.IsGameOver) yield return null;
        rescued[0] = !runner.IsGameOver && runner.RescuesThisRun > rescuesBefore;
        steering = true;
    }

    void Log(string id, string what, bool? pass)
    {
        string line = $"[RescueSelfTest] {id}) {what}: {(pass == null ? "SKIP" : pass.Value ? "PASS" : "FAIL")}";
        results.Add(line);
        if (pass == false) Debug.LogWarning(line); else Debug.Log(line);
    }

    // ------------------------------------------------------------------ cases

    IEnumerator Late1Tap(string id, string label, float late, bool wrongDir, bool expectRescue, float forceSpeed = 0f)
    {
        yield return Fresh();
        var ok = new bool[1];
        yield return WaitForLeg(() => Corner(out int inner, out _) && DynamicPathGenerator.Rolls(runner.currentSideIndex, inner) == 1
                                      && gen.PlayerSegment != null && gen.PlayerSegment.plankCount == 0, ok);
        if (!ok[0]) { Log(id, label, null); steering = true; yield break; }
        if (forceSpeed > 0f) runner.forwardSpeed = forceSpeed;
        Corner(out int face, out _);
        int before = runner.RescuesThisRun;
        yield return UntilAfterCommit(late);
        if (forceSpeed > 0f) runner.forwardSpeed = forceSpeed;
        int dir = runner.TapDirectionToReach(face);
        runner.SimulateTap(wrongDir ? -dir : dir);
        var r = new bool[1];
        yield return Outcome(before, r);
        Log(id, label, r[0] == expectRescue);
    }

    IEnumerator Late2Roll(string id, string label, float late, int rolls, bool expectRescue)
    {
        yield return Fresh();
        var ok = new bool[1];
        yield return WaitForLeg(() => Corner(out int inner, out _) && DynamicPathGenerator.Rolls(runner.currentSideIndex, inner) == (rolls == 3 ? 1 : 2)
                                      && gen.PlayerSegment != null && gen.PlayerSegment.plankCount == 0, ok);
        if (!ok[0]) { Log(id, label, null); steering = true; yield break; }
        Corner(out int face, out _);
        int before = runner.RescuesThisRun;
        int dir;
        if (rolls == 2)
        {
            // First roll well before the corner, second late.
            dir = runner.TapDirectionToReach((runner.currentSideIndex + 1) % 4);
            yield return new WaitForSeconds(Mathf.Max(0f, runner.SecondsToCorner - 0.25f));
            runner.SimulateTap(dir);
            yield return UntilAfterCommit(late);
            runner.SimulateTap(dir);
        }
        else
        {
            // The long way round: three rolls the wrong direction, all inside the window.
            dir = -runner.TapDirectionToReach(face);
            yield return UntilAfterCommit(0.005f);
            for (int k = 0; k < 3; k++) { runner.SimulateTap(dir); yield return new WaitForSeconds(0.025f); }
        }
        var r = new bool[1];
        yield return Outcome(before, r);
        Log(id, label, r[0] == expectRescue);
    }

    IEnumerator BoostTwice()
    {
        const string id = "f", label = "late on two consecutive boost legs, both rescued";
        yield return Fresh();
        var ok = new bool[1];
        yield return WaitForLeg(() => gen.PlayerOnZigZagBoost && gen.PlayerBoostLeg >= 1 && Corner(out int inner, out _)
                                      && DynamicPathGenerator.Rolls(runner.currentSideIndex, inner) >= 1, ok);
        if (!ok[0]) { Log(id, label, null); steering = true; yield break; }
        int before = runner.RescuesThisRun;
        for (int leg = 0; leg < 2; leg++)
        {
            Corner(out int face, out _);
            int rolls = DynamicPathGenerator.Rolls(runner.currentSideIndex, face);
            int dir = runner.TapDirectionToReach(rolls == 1 ? face : (runner.currentSideIndex + 1) % 4);
            if (rolls == 2) { runner.SimulateTap(dir); yield return new WaitForSeconds(0.1f); }
            yield return UntilAfterCommit(0.05f);
            runner.SimulateTap(dir);
            yield return new WaitForSeconds(0.12f);
            if (runner.IsGameOver) break;
        }
        bool pass = !runner.IsGameOver && runner.RescuesThisRun >= before + 2;
        steering = true;
        Log(id, label, pass);
    }

    IEnumerator PlankLate(bool wrongFace)
    {
        string id = "g", label = wrongFace ? "late roll onto the WRONG plank face, falls" : "late correct roll onto a plank, rescued";
        yield return Fresh();
        var ok = new bool[1];
        yield return WaitForLeg(() => gen.PlayerSegment != null && gen.PlayerSegment.plankCount > 0
                                      && DynamicPathGenerator.Rolls(runner.currentSideIndex, gen.PlayerSegment.planks[0].z) == 1, ok);
        if (!ok[0]) { Log(id, label, null); steering = true; yield break; }
        var p = gen.PlayerSegment.planks[0];
        int before = runner.RescuesThisRun;
        float speed = Mathf.Max(0.01f, runner.CurrentMoveSpeed);
        float t = Time.time + ((p.x - 0.5f) - Along()) * gen.cubeSize / speed + 0.05f;
        while (Time.time < t && !runner.IsGameOver) yield return null;
        int dir = runner.TapDirectionToReach(p.z);
        runner.SimulateTap(wrongFace ? -dir : dir);
        var r = new bool[1];
        yield return Outcome(before, r);
        Log(id, label, wrongFace ? !r[0] : r[0]);
    }

    IEnumerator DecoyNoRescue()
    {
        const string id = "h", label = "committed to a decoy stub, no rescue";
        yield return Fresh();
        var ok = new bool[1];
        yield return WaitForLeg(() => gen.PlayerSegment != null && gen.PlayerSegment.decoyAtEnd, ok);
        if (!ok[0]) { Log(id, label, null); steering = true; yield break; }
        var next = gen.SegmentAfter(gen.PlayerSegment);
        if (next == null || next.decoyCubes == null || next.decoyCubes.Count == 0) { Log(id, label, null); steering = true; yield break; }
        Vector3 last = gen.PlayerSegment.cubes[gen.PlayerSegment.cubes.Count - 1].transform.position;
        Vector3 decoyDir = (next.decoyCubes[0].transform.position - (last + runner.currentCube.forward * gen.cubeSize)).normalized;
        int decoyFace = runner.SideFacing(decoyDir);
        // Sit on the decoy's face, let it take the stub, then try the real turn late.
        while (runner.currentSideIndex != decoyFace && !runner.IsGameOver) { TapToward(decoyFace); yield return new WaitForSeconds(0.18f); }
        int before = runner.RescuesThisRun;
        yield return UntilAfterCommit(0.03f);
        Corner(out int inner, out _);
        if (inner >= 0) runner.SimulateTap(runner.TapDirectionToReach(inner));
        float end = Time.time + 1f;
        while (Time.time < end && !runner.IsGameOver) yield return null;
        steering = true;
        Log(id, label, runner.IsGameOver && runner.RescuesThisRun == before);
    }

    public IEnumerator RunAll()
    {
        Random.InitState(seed);
        results.Clear();
        yield return Late1Tap("a", "correct 1 tap 60 ms late, rescued", 0.06f, false, true);
        yield return Late1Tap("b", "correct 1 tap 120 ms late, falls", 0.12f, false, false);
        yield return Late1Tap("c", "wrong direction 30 ms late, falls", 0.03f, true, false);
        yield return Late2Roll("d", "2-roll turn, second tap 70 ms late, rescued", 0.07f, 2, true);
        yield return Late2Roll("e", "3 rolls the long way in the window, falls", 0f, 3, false);
        yield return BoostTwice();
        yield return PlankLate(false);
        yield return PlankLate(true);
        yield return DecoyNoRescue();
        yield return Late1Tap("i", "at ~38 u/s a 90 ms-late tap is still rescued", 0.088f, false, true, 38.4f * (gen != null ? gen.sizeMultiplier : 1f));
        Debug.Log("[RescueSelfTest] done\n" + string.Join("\n", results));
    }
}
