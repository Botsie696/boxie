using System.Collections.Generic;
using UnityEngine;

namespace Boxi
{
    /// <summary>
    /// Spring-bone secondary motion for Boxi's fur, tuft, bow and glasses.
    /// Runs after the Animator, so physics layers on top of whatever clip is playing
    /// (including clips you author later in Unity that never key the fur bones).
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public class BoxiFurPhysics : MonoBehaviour
    {
        [System.Serializable]
        public class BoneGroup
        {
            [Tooltip("Every bone whose name starts with this prefix uses this group.")]
            public string namePrefix = "Fur_";
            [Tooltip("Spring strength pulling the tip back to the animated pose.")]
            public float stiffness = 120f;
            [Tooltip("Velocity damping. Higher = settles faster, less wobble.")]
            public float damping = 9f;
            [Tooltip("Gravity pull, in multiples of Physics.gravity.")]
            public float gravity = 0.15f;
            [Tooltip("How strongly wind / movement airflow pushes these bones.")]
            [Range(0f, 2f)] public float airInfluence = 1f;
            [Tooltip("1 = tips fully lag behind body motion, 0 = tips move rigidly with the body.")]
            [Range(0f, 1f)] public float inertia = 1f;
            [Tooltip("Maximum swing away from the animated pose, in degrees.")]
            [Range(0f, 90f)] public float maxAngle = 45f;
            [Tooltip("Simulated tip length for end bones (metres, before object scale).")]
            public float tipLength = 0.12f;
        }

        public List<BoneGroup> groups = new List<BoneGroup>
        {
            new BoneGroup { namePrefix = "Fur_",    stiffness = 120f, damping = 9f,  gravity = 0.15f, airInfluence = 1f,   inertia = 1f,   maxAngle = 45f, tipLength = 0.12f },
            new BoneGroup { namePrefix = "Tuft_",   stiffness = 70f,  damping = 6f,  gravity = 0.3f,  airInfluence = 1.2f, inertia = 1f,   maxAngle = 60f, tipLength = 0.15f },
            new BoneGroup { namePrefix = "Bow",     stiffness = 220f, damping = 12f, gravity = 0.2f,  airInfluence = 0.5f, inertia = 0.8f, maxAngle = 25f, tipLength = 0.06f },
            new BoneGroup { namePrefix = "Glasses", stiffness = 900f, damping = 35f, gravity = 0.1f,  airInfluence = 0f,   inertia = 0.4f, maxAngle = 5f,  tipLength = 0.08f },
        };

        [Header("Wind")]
        [Tooltip("World-space wind velocity (m/s).")]
        public Vector3 wind = Vector3.zero;
        [Tooltip("Converts airflow (m/s) into push force. ~0.9 gives ~35 deg of fur sweep at 10 m/s.")]
        public float windStrength = 0.9f;
        [Range(0f, 1f)] public float turbulence = 0.35f;
        public float turbulenceFrequency = 2.5f;

        [Header("Movement")]
        [Tooltip("For in-place runners: set to the speed the world scrolls past (e.g. transform.forward * runSpeed).")]
        public Vector3 externalVelocity = Vector3.zero;
        [Tooltip("Also treat this object's real movement as airflow.")]
        public bool useTransformVelocity = true;
        [Tooltip("Moves larger than this in one frame reset the simulation (teleports, respawns).")]
        public float teleportDistance = 1f;
        [Tooltip("Fast movers (a runner): the tips are carried along with the body's smoothed velocity, so steady travel only " +
                 "shows as airflow and the fur swings on the changes - hops, rolls, turns - instead of being dragged flat.")]
        public bool carryWithBody = false;

        [Header("Blend")]
        [Range(0f, 1f)] public float weight = 1f;

        const float Step = 1f / 90f;

        class Bone
        {
            public Transform t;
            public BoneGroup g;
            public Quaternion restLocal;
            public Vector3 axisLocal;
            public float length;
            public Vector3 tip, vel, prevTarget;
            public float seed;
            public int depth;
        }

        readonly List<Bone> bones = new List<Bone>();
        Vector3 lastPos, smoothVel;

        void Awake() { Collect(); }
        void OnEnable() { ResetSimulation(); }

        /// <summary>Re-scan bones (call after changing group prefixes at runtime).</summary>
        public void Collect()
        {
            bones.Clear();
            float rootScale = Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.y));
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                foreach (var g in groups)
                {
                    if (string.IsNullOrEmpty(g.namePrefix) || !t.name.StartsWith(g.namePrefix)) continue;
                    var b = new Bone { t = t, g = g, restLocal = t.localRotation, seed = Random.value * 100f };
                    Vector3 dir;
                    if (t.childCount > 0) { dir = t.GetChild(0).position - t.position; b.length = dir.magnitude / rootScale; }
                    else { dir = t.parent ? t.position - t.parent.position : t.up; b.length = g.tipLength; }
                    if (dir.sqrMagnitude < 1e-10f) dir = t.up;
                    b.axisLocal = t.InverseTransformDirection(dir.normalized).normalized;
                    for (var p = t; p != null && p != transform; p = p.parent) b.depth++;
                    bones.Add(b);
                    break;
                }
            }
            bones.Sort((a, c) => a.depth.CompareTo(c.depth));
        }

        public void ResetSimulation()
        {
            lastPos = transform.position;
            smoothVel = Vector3.zero;
            float s = Mathf.Abs(transform.lossyScale.y);
            foreach (var b in bones)
            {
                Vector3 target = b.t.position + b.t.TransformDirection(b.axisLocal) * b.length * s;
                b.tip = b.prevTarget = target;
                b.vel = Vector3.zero;
            }
        }

        // Restore rest pose before the Animator runs, so bones that the current clip
        // does not animate don't accumulate last frame's physics rotation.
        void Update()
        {
            foreach (var b in bones) b.t.localRotation = b.restLocal;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || bones.Count == 0) return;

            Vector3 pos = transform.position;
            Vector3 moved = pos - lastPos;
            lastPos = pos;
            if (moved.magnitude > teleportDistance) { ResetSimulation(); return; }

            Vector3 v = useTransformVelocity ? moved / dt : Vector3.zero;
            smoothVel = Vector3.Lerp(smoothVel, v, 1f - Mathf.Exp(-10f * dt));
            if (carryWithBody)
            {
                Vector3 carry = (useTransformVelocity ? smoothVel : moved / dt) * dt;
                foreach (var b in bones) { b.tip += carry; b.prevTarget += carry; }
            }
            Vector3 air = wind - smoothVel - externalVelocity;
            float airMag = air.magnitude;
            float time = Time.time * turbulenceFrequency;
            float scale = Mathf.Abs(transform.lossyScale.y);

            foreach (var b in bones)
            {
                Transform t = b.t;
                Vector3 pivot = t.position;
                Vector3 animDir = t.TransformDirection(b.axisLocal);
                float len = b.length * scale;
                Vector3 target = pivot + animDir * len;

                // Part of the body motion is carried rigidly by the tip (inertia < 1).
                b.tip += (target - b.prevTarget) * (1f - b.g.inertia);
                b.prevTarget = target;

                Vector3 turb = new Vector3(
                    Mathf.PerlinNoise(b.seed, time) - 0.5f,
                    Mathf.PerlinNoise(b.seed + 13.1f, time) - 0.5f,
                    Mathf.PerlinNoise(b.seed + 27.7f, time) - 0.5f) * 2f;
                Vector3 force = Physics.gravity * b.g.gravity
                              + (air + turb * turbulence * (airMag + 0.5f)) * (windStrength * b.g.airInfluence);

                float remaining = Mathf.Min(dt, 0.1f);
                int guard = 0;
                while (remaining > 1e-6f && guard++ < 10)
                {
                    float h = Mathf.Min(Step, remaining);
                    remaining -= h;
                    Vector3 acc = (target - b.tip) * b.g.stiffness - b.vel * b.g.damping + force;
                    b.vel += acc * h;
                    b.tip += b.vel * h;
                }

                Vector3 dir = b.tip - pivot;
                if (dir.sqrMagnitude < 1e-12f) dir = animDir;
                dir = Vector3.RotateTowards(animDir, dir.normalized, b.g.maxAngle * Mathf.Deg2Rad, 0f);
                b.tip = pivot + dir * len;

                Quaternion q = Quaternion.FromToRotation(animDir, dir);
                if (weight < 1f) q = Quaternion.Slerp(Quaternion.identity, q, weight);
                t.rotation = q * t.rotation;
            }
        }
    }
}
