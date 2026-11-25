using UnityEngine;
using UnityEngine.Rendering;

/// <summary>How a menu band's diorama camera moves (MainUi checkpoint stack). Gentle and continuous, never a yaw snap.</summary>
public enum MenuShotMotion { HighOrbit, LowSideTrack, TopDownPushIn, TelephotoPan, Static }

/// <summary>The shot a checkpoint's band in the main menu films its diorama with.</summary>
[System.Serializable]
public struct MenuCameraPreset
{
    [Tooltip("The kind of slow, continuous move.")]
    public MenuShotMotion motion;
    [Tooltip("Camera position relative to the diorama's focus point (world units, before the motion is applied).")]
    public Vector3 positionOffset;
    [Tooltip("x = pitch, y = yaw, in degrees: where the shot looks from at the start of its move.")]
    public Vector2 angles;
    [Tooltip("Vertical field of view, degrees. Small = telephoto.")]
    [Range(5f, 90f)] public float fov;
    [Tooltip("Speed of the move (cycles or units per second, depending on the motion).")]
    public float motionSpeed;
    [Tooltip("Size of the move (degrees of orbit/pan, or units of push-in/track).")]
    public float motionAmplitude;
}

/// <summary>Optional RenderSettings fog. The prop-camera scenes draw their fog in their own shaders (ChasmFog and the
/// FlatFaceBlock family, which are in <see cref="CheckpointProfile.look"/>) and keep RenderSettings.fog OFF; this is only
/// applied when <see cref="enabled"/> is on.</summary>
[System.Serializable]
public struct CheckpointFog
{
    public bool enabled;
    public FogMode mode;
    public Color color;
    public float density;
    public float start, end;
}

/// <summary>
/// One checkpoint of the run (MainScene prop camera 3.6 onward): the single source of truth that gameplay (the portal's
/// look change, via <see cref="CheckpointSet.BuildRuntimeSequence"/>) and the main menu (its live band) both read.
///
/// Checkpoint 1 is the run start; checkpoint N (N >= 2) is reached by going through portal N - 1.
///
/// The atmosphere is the <see cref="look"/> (an AtmosphereLook captured from a scene): block/track palette per role,
/// sky, ChasmFog backdrop (the scenes' real fog), sun colour/intensity/rotation, ambient, camera background, the URP
/// post-processing profile, the path tint gradient and the accent colours. The fields under it cover what a look does
/// not carry. A profile with no look is a placeholder: its portal plays no look change.
/// </summary>
[CreateAssetMenu(menuName = "Checkpoints/Checkpoint Profile", fileName = "CP00_")]
public class CheckpointProfile : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("1 = run start, N = after portal N - 1.")]
    [Min(1)] public int index = 1;
    public string displayName = "";
    [Tooltip("Nominal leg (score) of this checkpoint = (index - 1) x the set's legsBetweenPortals; written by Tools > Checkpoints. " +
             "The real leg can slip a few legs (a boost running, no room for the runway) - the save's snapshot holds the exact one.")]
    public int legIndex;

    [Header("Atmosphere (palette, sky, ChasmFog, sun, ambient, post volume, accents)")]
    [Tooltip("The look this checkpoint's portal wipes the world into. Null = placeholder (the portal changes nothing). " +
             "Checkpoint 1's look is only used by the menu: in gameplay the scene's own look is read live at start.")]
    public AtmosphereLook look;
    [Tooltip("Menu diorama: also turn its sun to the look's captured rotation. Gameplay never turns the sun (as in 3.5).")]
    public bool applySunRotation = false;
    [Tooltip("Optional RenderSettings.skybox. Null = untouched (the prop scenes draw their sky on a backdrop quad, which is in the look).")]
    public Material skyboxMaterial;
    [Tooltip("Optional RenderSettings fog, off by default (see CheckpointFog).")]
    public CheckpointFog fog;
    [Tooltip("Ambient particles for this checkpoint (menu diorama; gameplay has none today). Null = none.")]
    public GameObject ambientParticlePrefab;

    [Header("Portal transition (gameplay)")]
    [Tooltip("Label shown as the wipe passes. Empty = \"CHECKPOINT {index}\".")]
    public string label = "";
    public bool cameraCinematic = true;
    [Tooltip("Keep the pyramid obstacle's material and glow as they were before this portal.")]
    public bool keepObstacle = true;
    public bool takePlankColor = true;
    public bool overrideTiming;
    public CheckpointLookSequence.Timing timing = CheckpointLookSequence.Timing.Default;

    [Header("Main menu")]
    public MenuCameraPreset menuCamera;
    [Tooltip("Hint colour the band shows while this checkpoint is still locked.")]
    public Color lockedTeaseColor = new Color(1f, 0.85f, 0.4f, 1f);

    /// <summary>The portal that reaches this checkpoint (0 for the run start).</summary>
    public int Portal => index - 1;
    public bool IsPlaceholder => look == null;
    public string LabelText => string.IsNullOrEmpty(label) ? "CHECKPOINT " + index : label;
    public VolumeProfile PostProfile => look != null ? look.volumeProfile : null;
}
