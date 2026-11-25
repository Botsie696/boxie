using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// LookTransition: which look each zone door changes the world into. Portal N (the N-th door the runner goes through,
/// counted from 1) with an entry here plays the transition into that entry's look; a door with no entry behaves exactly
/// as it always did (the look in force simply carries on). Looks chain: after portal 1 the world is in entry 1's look, and
/// that is what a later entry wipes away from.
/// </summary>
[CreateAssetMenu(menuName = "Look Transition/Checkpoint Look Sequence", fileName = "CheckpointLookSequence")]
public class CheckpointLookSequence : ScriptableObject
{
    [System.Serializable]
    public struct Timing
    {
        [Tooltip("A. Seconds from the teleport for the camera to glide from the two-tower shot to the chase shot behind the runner.")]
        public float followThrough;
        [Tooltip("B. Seconds the camera holds behind the runner; the left-to-right wipe runs across all of it and the label appears.")]
        public float reveal;
        [Tooltip("C. Seconds to crane back up into the camera mode's own live pose.")]
        public float riseBack;
        [Tooltip("D. Seconds on the normal camera before the first turn. Sizes the exit runway; never under 1.2.")]
        public float readTime;
        [Tooltip("Seconds from the teleport by which sun, ambient, fog and post-processing have reached the new look exactly (they keep easing during play).")]
        public float settleEnd;
        [Tooltip("Lowest runner speed during A-B, as a share of its own speed (eased in, and back to 1 across C). 1 = no slow-down. Never under 0.85.")]
        [Range(0.85f, 1f)] public float speedFloor;
        [Tooltip("Seconds the slow-down takes to ease in at the start of A.")]
        public float speedEaseIn;
        [Tooltip("Extra seconds of runway, at full speed, on top of A-D (rounding, the eased speed change).")]
        public float runwayMargin;
        [Tooltip("Seconds after the runner enters the door that the wipe begins. 0 = the moment it goes in (the user's choice for 3.2).")]
        public float wipeDelay;
        [Tooltip("Seconds the wipe takes to cross the screen. 0 = from its start to the end of B (the camera's hold behind the runner).")]
        public float wipeDuration;

        /// <summary>Wipe start and length, in seconds from the moment the runner enters the door.</summary>
        public float WipeStart => Mathf.Max(0f, wipeDelay);
        public float WipeLength => wipeDuration > 0.01f ? wipeDuration : Mathf.Max(0.3f, followThrough + reveal - WipeStart);

        public static Timing Default => new Timing
        {
            followThrough = 0.8f, reveal = 1.6f, riseBack = 1.0f, readTime = 1.5f, settleEnd = 7.0f,
            speedFloor = 0.85f, speedEaseIn = 0.4f, runwayMargin = 0.15f
        };

        /// <summary>Teleport to the end of C: the camera is back on the mode.</summary>
        public float CameraSeconds => followThrough + reveal + riseBack;

        /// <summary>The exit runway, in seconds at the runner's full speed, that A-D take with the slow-down applied.</summary>
        public float RunwaySecondsAtFullSpeed
        {
            get
            {
                float f = Mathf.Clamp(speedFloor, 0.85f, 1f);
                float ease = Mathf.Min(speedEaseIn, followThrough + reveal);
                return ease * (1f + f) * 0.5f            // easing down (smoothstep averages half way)
                     + (followThrough + reveal - ease) * f
                     + riseBack * (1f + f) * 0.5f        // easing back up across C
                     + Mathf.Max(1.2f, readTime)
                     + runwayMargin;
            }
        }
    }

    [System.Serializable]
    public class Entry
    {
        [Tooltip("Door number, counted from 1 (1 = the first door of the run).")]
        [Min(1)] public int portal = 1;
        public AtmosphereLook look;
        [Tooltip("Small label shown under the score as the wipe passes. Empty = \"CHECKPOINT {portal}\".")]
        public string label = "";
        [Tooltip("Glide the camera behind the runner for the reveal, then back. Off = the wipe plays on the normal camera (and the runway is not lengthened).")]
        public bool cameraCinematic = true;
        [Tooltip("Keep the pyramid obstacle exactly as it was before this door (its material and glow). The user chose this for portal 1: the target look's obstacle loses contrast on its path.")]
        public bool keepObstacle = true;
        [Tooltip("Take the target look's bridge-plank colour (on), or keep the current one (off).")]
        public bool takePlankColor = true;
        public bool overrideTiming;
        public Timing timing = Timing.Default;

        public string LabelText => string.IsNullOrEmpty(label) ? "CHECKPOINT " + portal : label;
    }

    [Tooltip("For the survey/report only: the look of the scene before the first door, as captured from the base scene. At run time the director reads the running scene itself.")]
    public AtmosphereLook baseLook;
    public Timing defaultTiming = Timing.Default;
    public List<Entry> entries = new List<Entry>();

    public Entry Find(int portal)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i] != null && entries[i].portal == portal && entries[i].look != null) return entries[i];
        return null;
    }

    public Timing TimingFor(Entry e) => e != null && e.overrideTiming ? e.timing : defaultTiming;
}
