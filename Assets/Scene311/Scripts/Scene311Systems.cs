using UnityEngine;

/// <summary>
/// Scene 3.11 bootstrap (object "Scene311Systems"). Turns on the 3.11-only switches in shared code for as long as the
/// scene runs and adds the 3.11 systems, in this order (all before the generator lays its first path):
///   - AutoPlayer.PortalOwnsSteering: the bot holds the door's face on a door runway (one of the audit's "portal" deaths)
///   - SkillProfile311     the hidden skill model + adaptive tier (Phase 3)
///   - MetaSystems311      daily reward, missions, weekly, awards (Phase 4)
///   - Notifications311    local reminders, soft prompt, scheduling (Phase 5)
///   - MetaUI311           the Hub, toasts, daily card, prompt, pause / game-over buttons
///   - development builds only: RunTelemetry, CornerRecorder311, DevMenu311 (F3 / triple tap top-right)
/// </summary>
[DefaultExecutionOrder(-900)]
public class Scene311Systems : MonoBehaviour
{
    public static Scene311Systems Instance { get; private set; }

    public SkillTuning311 skillTuning;
    public MetaTuning311 metaTuning;

    DynamicPathGenerator gen;

    void Awake()
    {
        Instance = this;
        AutoPlayer.PortalOwnsSteering = true;
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        if (GetComponent<SkillProfile311>() == null) gameObject.AddComponent<SkillProfile311>().Init(skillTuning);
        if (GetComponent<MetaSystems311>() == null) gameObject.AddComponent<MetaSystems311>().Init(metaTuning);
        if (GetComponent<Notifications311>() == null) gameObject.AddComponent<Notifications311>();
        if (GetComponent<MetaUI311>() == null) gameObject.AddComponent<MetaUI311>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var p = gen != null ? gen.difficultyProfile as Scene311DifficultyProfile : null;
        if (gen != null && (p == null || p.devTelemetry))
        {
            if (gen.GetComponent<RunTelemetry>() == null) gen.gameObject.AddComponent<RunTelemetry>();
            if (GetComponent<CornerRecorder311>() == null) gameObject.AddComponent<CornerRecorder311>();
        }
        if (GetComponent<DevMenu311>() == null) gameObject.AddComponent<DevMenu311>();
#endif
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        AutoPlayer.PortalOwnsSteering = false;
    }
}
