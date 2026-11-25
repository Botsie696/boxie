using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MainUi: the first legs a real run lays at each checkpoint, baked from MainScene prop camera 3.6 with the real
/// DynamicPathGenerator (fixed seed per checkpoint, the same checkpoint-start path a run uses) by MenuSliceBaker, plus the
/// gameplay camera's framing of them. MenuDioramaRig spawns them once; nothing is generated in the menu.
/// Positions are relative to the first cube of the slice.
/// </summary>
[CreateAssetMenu(menuName = "Checkpoints/Menu Slice Data", fileName = "MenuSlices")]
public class MenuSliceData : ScriptableObject
{
    [System.Serializable]
    public class Slice
    {
        public int checkpoint;
        public int seed;
        public int firstLegSerial;
        public Vector3 cubeScale = Vector3.one;
        public List<Vector3> cubePos = new List<Vector3>();
        public List<Quaternion> cubeRot = new List<Quaternion>();
        public List<int> cubeLeg = new List<int>();
        [Tooltip("The runner's route: the first cube, then each corner, then the last cube (camera glide).")]
        public List<Vector3> route = new List<Vector3>();
        public bool exitTower;
        public Vector3 towerForward = Vector3.forward;
        [Header("Gameplay camera at the start of the slice (orthographic)")]
        public Vector3 camEuler = new Vector3(30f, 45f, 0f);
        public float camOrtho = 22f;
        public Vector3 camOffset;   // camera position minus the slice's first cube
    }

    public List<Slice> slices = new List<Slice>();

    public Slice Get(int checkpoint)
    {
        for (int i = 0; i < slices.Count; i++) if (slices[i] != null && slices[i].checkpoint == checkpoint) return slices[i];
        return null;
    }
}
