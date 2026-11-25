using UnityEngine;

namespace Cubie
{
    /// <summary>Fires the Animator "Blink" trigger at random intervals (additive Blink layer).</summary>
    public class CubieBlinker : MonoBehaviour
    {
        public Animator animator;
        public string trigger = "Blink";
        public Vector2 intervalSeconds = new Vector2(2.5f, 6f);
        [Range(0f, 1f)] public float doubleBlinkChance = 0.15f;

        float next;
        int triggerHash;

        void Start()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            triggerHash = Animator.StringToHash(trigger);
            Schedule();
        }

        void Schedule() { next = Time.time + Random.Range(intervalSeconds.x, intervalSeconds.y); }

        void Update()
        {
            if (Time.time < next) return;
            if (animator && animator.runtimeAnimatorController) animator.SetTrigger(triggerHash);
            if (Random.value < doubleBlinkChance) next = Time.time + 0.35f;
            else Schedule();
        }
    }
}
