using System.Collections.Generic;
using UnityEngine;

namespace Lumi
{
    /// <summary>
    /// Script/Inspector control of Lumi's face on top of any animation:
    /// eye look (target or sliders), lid open/close, brow angry/sad and raise.
    /// All values are additive to the playing clip; leave at 0 to just use the clip.
    /// </summary>
    [DefaultExecutionOrder(9000)]
    [DisallowMultipleComponent]
    public class LumiFace : MonoBehaviour
    {
        [Header("Eyes")]
        [Tooltip("Optional world target for both eyes to look at.")]
        public Transform lookTarget;
        [Range(-1f, 1f)] public float lookX;   // + = character's right
        [Range(-1f, 1f)] public float lookY;   // + = up
        [Range(0f, 45f)] public float maxEyeAngle = 30f;

        [Header("Lids")]
        [Tooltip("+ = wide open, - = sleepy/squint.")]
        [Range(-1f, 1f)] public float lidOpen;
        [Tooltip("1 = fully closed.")]
        [Range(0f, 1f)] public float close;

        [Header("Brows")]
        [Tooltip("+ = angry (inner ends down), - = sad/worried (inner ends up).")]
        [Range(-1f, 1f)] public float browAngry;
        [Tooltip("+ = raised, - = lowered.")]
        [Range(-1f, 1f)] public float browRaise;
        public float browMaxAngle = 20f;
        public float browMaxRaise = 0.03f;

        [Header("Smile (rest = subtle smug smirk)")]
        [Tooltip("0 = clip pose, + = bigger smile, - = worried / flat.")]
        [Range(-1f, 1f)] public float smile;
        [Tooltip("0 = even, 1 = her left corner lifted more (smirk).")]
        [Range(0f, 1f)] public float smirk;

        Transform center, eyeL, eyeR, lidUL, lidUR, lidLL, lidLR, browL, browR;
        Transform smileL, smileC, smileR;
        Vector3 frontLocal;
        struct Rest { public Transform t; public Quaternion r; public Vector3 p; }
        readonly List<Rest> rests = new List<Rest>();

        void Awake()
        {
            var map = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (!map.ContainsKey(t.name)) map[t.name] = t;
            Transform F(string n) { map.TryGetValue(n, out var t); return t; }
            center = F("Body_Upper");
            eyeL = F("Eye_L"); eyeR = F("Eye_R");
            lidUL = F("Lid_Upper_L"); lidUR = F("Lid_Upper_R");
            lidLL = F("Lid_Lower_L"); lidLR = F("Lid_Lower_R");
            browL = F("Brow_L"); browR = F("Brow_R");
            var glasses = F("Face_Front") ?? F("Glasses");
            Vector3 front = (center && glasses)
                ? Vector3.ProjectOnPlane(glasses.position - center.position, transform.up)
                : transform.forward;
            frontLocal = transform.InverseTransformDirection(front.normalized);
            smileL = F("Smile_L"); smileC = F("Smile_C"); smileR = F("Smile_R");
            foreach (var t in new[] { eyeL, eyeR, lidUL, lidUR, lidLL, lidLR, browL, browR, smileL, smileC, smileR })
                if (t) rests.Add(new Rest { t = t, r = t.localRotation, p = t.localPosition });
        }

        void Update()
        {
            foreach (var r in rests) { r.t.localRotation = r.r; r.t.localPosition = r.p; }
        }

        void LateUpdate()
        {
            if (!center) return;
            Vector3 up = transform.up;
            Vector3 front = transform.TransformDirection(frontLocal);
            Vector3 right = Vector3.Cross(up, front).normalized;

            foreach (var eye in new[] { eyeL, eyeR })
            {
                if (!eye) continue;
                Vector3 desired = lookTarget
                    ? (lookTarget.position - eye.position).normalized
                    : Quaternion.AngleAxis(lookX * maxEyeAngle, up) * Quaternion.AngleAxis(-lookY * maxEyeAngle, right) * front;
                desired = Vector3.RotateTowards(front, desired, maxEyeAngle * Mathf.Deg2Rad, 0f);
                eye.rotation = Quaternion.FromToRotation(front, desired) * eye.rotation;
            }

            // Upper lid closes by rotating its front edge down (around `right`), lower lid closes upward.
            float upper = close * 45f - lidOpen * 20f;
            float lower = close * 15f - lidOpen * 8f;
            foreach (var l in new[] { lidUL, lidUR }) if (l) l.rotation = Quaternion.AngleAxis(upper, right) * l.rotation;
            foreach (var l in new[] { lidLL, lidLR }) if (l) l.rotation = Quaternion.AngleAxis(lower, -right) * l.rotation;

            float scale = Mathf.Abs(transform.lossyScale.y);
            foreach (var b in new[] { browL, browR })
            {
                if (!b) continue;
                float side = Mathf.Sign(Vector3.Dot(b.position - center.position, right));
                Vector3 inward = -right * side;
                Vector3 axis = Vector3.Cross(inward, -up).normalized;
                b.rotation = Quaternion.AngleAxis(browAngry * browMaxAngle, axis) * b.rotation;
                b.position += up * (browRaise * browMaxRaise * scale);
            }

            // Smile: corners lift up + out (her left more when smirk > 0), centre dips slightly. Layered on top of the clip.
            if (smileL) smileL.position += (right * 0.006f + up * (0.012f * (1f + 1.2f * smirk))) * (smile * scale);
            if (smileR) smileR.position += (-right * 0.006f + up * (0.012f * (1f - 0.6f * smirk))) * (smile * scale);
            if (smileC) smileC.position += up * (-0.004f * smile * scale);
        }
    }
}
