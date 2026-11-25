#if UNITY_EDITOR
using System.Collections;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tool, run in Play mode in MainScene prop camera 3.6 (MenuSliceBaker.Bake()): for every checkpoint it seeds the
/// real DynamicPathGenerator (SeedRuns), lays exactly what a run starting there lays (checkpointStartPortal, the real
/// lengthened runway out of the door), lets the next legs come, frames them with the real camera rig, and records the first
/// legs and the camera into Assets/Checkpoints/Menu/MenuSlices.asset for MainUi. Gameplay code is not changed; the run is
/// reset afterwards (stop Play when done). Re-bake whenever leg rules, portal spacing or the camera feel change.
/// </summary>
public class MenuSliceBaker : MonoBehaviour
{
    public const string AssetPath = "Assets/Checkpoints/Menu/MenuSlices.asset";
    public const int Legs = 5;
    public static bool Running { get; private set; }
    public static string LastReport = "";

    public static void Bake()
    {
        if (!Application.isPlaying) { Debug.LogError("[MenuSliceBaker] enter Play mode in 3.6 first."); return; }
        new GameObject("MenuSliceBaker").AddComponent<MenuSliceBaker>().StartCoroutine(Run());
    }

    static IEnumerator Run()
    {
        Running = true;
        var gen = FindFirstObjectByType<DynamicPathGenerator>();
        var dir = FindFirstObjectByType<LookTransitionDirector>();
        var rig = FindFirstObjectByType<CameraParentRotator>();
        var set = dir != null ? dir.checkpoints : null;
        if (gen == null || rig == null || set == null) { Debug.LogError("[MenuSliceBaker] needs 3.6 (generator, camera rig, checkpoint set)."); Running = false; yield break; }

        var data = AssetDatabase.LoadAssetAtPath<MenuSliceData>(AssetPath);
        if (data == null) { data = ScriptableObject.CreateInstance<MenuSliceData>(); AssetDatabase.CreateAsset(data, AssetPath); }
        data.slices.Clear();
        var report = new System.Text.StringBuilder();

        for (int cp = 1; cp <= set.Count; cp++)
        {
            int seed = 7100 + cp * 37;
            gen.SeedRuns(seed);
            gen.checkpointStartPortal = cp - 1;
            if (cp > 1) dir.RequestRunwayFor(cp - 1);
            gen.GenerateNewPath();
            if (cp > 1) gen.PortalCrossed();   // the door is behind: ordinary legs follow, as after a real crossing
            gen.LayAheadNow();
            yield return null;
            gen.LayAheadNow();
            rig.SnapToPlayerZone();
            yield return null;

            var s = new MenuSliceData.Slice { checkpoint = cp, seed = seed };
            DynamicPathGenerator.Segment first = gen.SegmentAt(0);
            if (first == null || first.cubes.Count == 0) { report.Append("CP").Append(cp).Append(": nothing laid\n"); continue; }
            s.firstLegSerial = gen.CheckpointLeg(cp - 1);
            Transform origin = cp == 1 ? gen.firstCube : first.cubes[0].transform;
            Vector3 o = origin.position;
            s.cubeScale = origin.lossyScale;
            if (cp == 1) Add(s, gen.firstCube, o, 0);
            s.route.Add(Vector3.zero);
            for (int leg = 0; leg < Legs; leg++)
            {
                DynamicPathGenerator.Segment seg = gen.SegmentAt(leg);
                if (seg == null) break;
                foreach (GameObject c in seg.cubes) if (c != null) Add(s, c.transform, o, leg);
                GameObject last = seg.cubes.Count > 0 ? seg.cubes[seg.cubes.Count - 1] : null;
                if (last != null) s.route.Add(last.transform.position - o);
            }
            s.exitTower = cp > 1;
            s.towerForward = first.direction;
            Camera cam = rig.boomCamera != null ? rig.boomCamera : Camera.main;
            s.camEuler = cam.transform.rotation.eulerAngles;
            s.camOrtho = cam.orthographic ? cam.orthographicSize : 22f;
            s.camOffset = cam.transform.position - o;
            data.slices.Add(s);
            report.Append("CP").Append(cp).Append(": seed ").Append(seed).Append(", ").Append(s.cubePos.Count).Append(" cubes in ")
                  .Append(s.route.Count - 1).Append(" legs, cam ").Append(s.camEuler.ToString("F1")).Append(" ortho ").Append(s.camOrtho.ToString("F1")).Append('\n');
        }

        gen.checkpointStartPortal = 0;
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        LastReport = report.ToString();
        Debug.Log("[MenuSliceBaker] baked " + data.slices.Count + " slices -> " + AssetPath + "\n" + LastReport);
        Running = false;
    }

    static void Add(MenuSliceData.Slice s, Transform t, Vector3 o, int leg)
    {
        s.cubePos.Add(t.position - o);
        s.cubeRot.Add(t.rotation);
        s.cubeLeg.Add(leg);
    }
}
#endif
