using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The chasm's atmosphere. Draws a gradient background and fogs the rocks with exactly the same colours, so a rock fades into
/// precisely what is behind it and never shows where it ends.
///
/// Three layers of fog, all computed per pixel in the Custom/ChasmFogLit shader:
///  - Height fog: a fog sea just under the route. Rock is clear above it and sinks into solid fog below, so walls, pillars and
///    floating rocks look like they rise out of the mist. The fog's surface billows and drifts, so its line on the rock is never
///    a flat cut.
///  - Depth haze: rock further behind the route than the shot's focus turns misty, the aerial perspective that sells the scale.
///  - Screen fog: rock fades out towards the bottom of the screen, so the chasm under the route always reads as open.
///
/// The fog follows the camera's current shot (the lowest point of the route in it), so it stays still while a shot holds and
/// glides with the camera between shots; it never reaches the route itself. Rocks join the fog through <see cref="Apply"/>,
/// which PathBackgroundGenerator and NeonWindowDecorator call on the Polybox rocks they spawn. Houses and the rock they stand
/// on can take a share of the same fog (see the Houses header). Untick <see cref="enableFog"/> to go back to the plain look.
/// </summary>
[ExecuteAlways, DisallowMultipleComponent]
public class ChasmFog : MonoBehaviour
{
    [Header("Master switch")]
    public bool enableFog = true;

    [Header("References (found automatically when empty)")]
    public Camera cam;
    public DynamicPathGenerator generator;
    public CubeRailRunner player;
    [Tooltip("Custom/ChasmFogLit. Referenced here so builds include it.")]
    public Shader fogLitShader;
    [Tooltip("Hidden/ChasmFogBackdrop. Referenced here so builds include it.")]
    public Shader backdropShader;
    [Tooltip("Custom/ChasmFogHouseLit (URP Lit with the houses' share of the fog). Referenced here so builds include it.")]
    public Shader houseLitShader;

    [Header("Colour (the background uses these too)")]
    public Color topColor = new Color(0.05f, 0.07f, 0.12f, 1f);
    public Color bottomColor = new Color(0.13f, 0.21f, 0.27f, 1f);
    public Color glowColor = new Color(0.12f, 0.18f, 0.22f, 1f);
    [Range(0f, 2f)] public float glowStrength = 0.8f;
    [Tooltip("Centre of the glow, 0-1 across and up the screen.")]
    public Vector2 glowCentre = new Vector2(0.5f, 0.3f);
    [Tooltip("In screen heights.")]
    [Range(0.1f, 2f)] public float glowRadius = 0.9f;

    [Header("Height fog (rock sinks into it)")]
    [Tooltip("The fog begins this far below the lowest point of the route in the current shot.")]
    public float fogStart = 8f;
    [Tooltip("Rock is completely lost in the fog this far below that point.")]
    public float fogSolid = 55f;
    [Tooltip("How far the fog's surface rises and falls.")]
    public float billowHeight = 8f;
    [Tooltip("Width of one billow, world units.")]
    public float billowSize = 26f;
    [Tooltip("Drift speed of the billows, world units per second.")]
    public float billowSpeed = 1.2f;
    public Vector2 windDirection = new Vector2(1f, 0.4f);

    [Header("Depth haze (rock behind the route turns misty)")]
    [Tooltip("Haze begins this far behind the shot's focus, measured along the ground away from the camera.")]
    public float hazeStart = 12f;
    [Tooltip("...and is at its strongest this far behind it.")]
    public float hazeEnd = 110f;
    [Range(0f, 1f)] public float hazeMax = 0.75f;

    [Header("Screen fog (keeps the bottom of the screen an open chasm)")]
    [Tooltip("Rock below this height on screen (0 = bottom, 1 = top) is fully fogged...")]
    [Range(0f, 1f)] public float screenFogSolid = 0.06f;
    [Tooltip("...fading to clear at this height.")]
    [Range(0f, 1f)] public float screenFogTop = 0.4f;
    [Range(0f, 1f)] public float screenFogStrength = 1f;

    [Header("Veil (a haze over every rock)")]
    [Tooltip("Every rock is at least this much the fog colour, wherever it is, so its shadows are lifted instead of going black. 0 = off.")]
    [Range(0f, 0.9f)] public float veil = 0f;

    [Header("Houses and their rock (NeonWindowDecorator > Houses In Chasm Fog)")]
    [Tooltip("How much of the fog a house and the rock it stands on take, compared with background rock at the same spot. 0.5 = half.")]
    [Range(0f, 1f)] public float houseFogShare = 0.5f;
    [Tooltip("Fraction of that share on the house itself and the top of its rock. The rock takes more the further down it goes.")]
    [Range(0f, 1f)] public float houseFogAtHouse = 0.35f;
    [Tooltip("Depth under the house (world units) at which its rock takes the whole share.")]
    public float houseFogDepth = 30f;

    [Header("Following the shot")]
    public float followSmoothTime = 1f;

    /// <summary>The ChasmFog in the loaded scene, if any.</summary>
    public static ChasmFog Active { get; private set; }

    public bool Running => isActiveAndEnabled && enableFog;

    /// <summary>World height below which rock is always completely fogged (wherever the billows are).</summary>
    public float SolidY => floorY - fogSolid - billowHeight * 0.5f;

    static readonly int TopId = Shader.PropertyToID("_ChasmFogTop");
    static readonly int BottomId = Shader.PropertyToID("_ChasmFogBottom");
    static readonly int GlowId = Shader.PropertyToID("_ChasmFogGlow");
    static readonly int GlowShapeId = Shader.PropertyToID("_ChasmFogGlowShape");
    static readonly int HeightId = Shader.PropertyToID("_ChasmFogHeight");
    static readonly int DepthId = Shader.PropertyToID("_ChasmFogDepth");
    static readonly int WindId = Shader.PropertyToID("_ChasmFogWind");
    static readonly int ScreenId = Shader.PropertyToID("_ChasmFogScreen");
    static readonly int ViewId = Shader.PropertyToID("_ChasmFogView");
    static readonly int ViewPosId = Shader.PropertyToID("_ChasmFogViewPos");
    static readonly int HouseId = Shader.PropertyToID("_ChasmFogHouse");

    const string PackShaderName = "Universal Render Pipeline/PolyboxURP";
    const string UrpLitShaderName = "Universal Render Pipeline/Lit";
    static readonly Dictionary<(Material, bool, bool, bool, bool), Material> fogMaterials = new Dictionary<(Material, bool, bool, bool, bool), Material>();

    float floorY, floorVel;
    Vector3 focus, focusVel;
    bool tracking;
    Vector2 wind;

    GameObject backdrop;
    Material backdropMaterial;
    MeshRenderer backdropRenderer;

    /// <summary>
    /// Switches every Polybox rock material under root to its fogged twin. Does nothing without a ChasmFog in the scene.
    /// Works whether or not the fog is ticked on: at strength 0 the fogged shader renders exactly like the pack's.
    /// receiveShadows false keeps shadows off the rock (the route's shadow on a distant wall reads as a stripe);
    /// URP ignores Renderer.receiveShadows, so it has to be the material's.
    /// keepOwnFade is for props (the rock a house stands on): instead of the chasm's fog layers the rock keeps the fade set in
    /// its Height Fog Start/End, only fading into the chasm's background colour, so it stays clear well below the house.
    /// noFog keeps the rock out of every fog - the pack's height fog, Unity's fog and all of the chasm's layers - whatever the
    /// fog settings are.
    /// house is for a house and the rock it stands on: they take a share of the chasm fog (houseFogShare), least at the house
    /// and more down the column. The rock needs the house's height in its Height Fog End. URP Lit materials (the houses) switch
    /// to Custom/ChasmFogHouseLit; without house they are left alone. noFog wins over house, and house over keepOwnFade.
    /// </summary>
    public static void Apply(GameObject root, bool receiveShadows = true, bool keepOwnFade = false, bool noFog = false, bool house = false)
    {
        ChasmFog fog = Active;
        if (fog == null || root == null) return;
        house &= !noFog;
        keepOwnFade &= !noFog && !house;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null || m.shader == null) continue;
                bool urpLit = m.shader.name == UrpLitShaderName;
                Shader target = m.shader.name == PackShaderName ? fog.fogLitShader : house && urpLit ? fog.houseLitShader : null;
                if (target == null) continue;
                var key = (m, receiveShadows, keepOwnFade, noFog, house);
                if (!fogMaterials.TryGetValue(key, out Material f) || f == null)
                {
                    f = new Material(m) { name = m.name + (noFog ? " (no fog)" : house ? " (Chasm Fog, house)" : keepOwnFade ? " (Chasm Fog, prop)" : receiveShadows ? " (Chasm Fog)" : " (Chasm Fog, no shadows)") };
                    f.shader = target;
                    if (!receiveShadows)
                    {
                        f.SetFloat("_ReceiveShadows", 0f);
                        f.EnableKeyword("_RECEIVE_SHADOWS_OFF");
                    }
                    if (noFog) f.EnableKeyword("_CHASM_FOG_NONE");
                    else if (house && !urpLit) f.EnableKeyword("_CHASM_FOG_HOUSE");
                    else if (keepOwnFade) f.EnableKeyword("_CHASM_FOG_PROP");
                    fogMaterials[key] = f;
                }
                mats[i] = f;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    void Reset() { AutoFind(); }

    void AutoFind()
    {
        if (cam == null) cam = Camera.main;
        if (generator == null) generator = FindFirstObjectByType<DynamicPathGenerator>();
        if (player == null) player = FindFirstObjectByType<CubeRailRunner>();
        if (fogLitShader == null) fogLitShader = Shader.Find("Custom/ChasmFogLit");
        if (backdropShader == null) backdropShader = Shader.Find("Hidden/ChasmFogBackdrop");
        if (houseLitShader == null) houseLitShader = Shader.Find("Custom/ChasmFogHouseLit");
    }

    void OnEnable()
    {
        Active = this;
        AutoFind();
        tracking = false;
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        Track(true);
        PushGlobals();
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        if (Active == this) Active = null;
        Shader.SetGlobalVector(ViewId, Vector4.zero);   // strength 0: fogged rocks look exactly like the pack's
        DestroyBackdrop();
    }

    void OnValidate()
    {
        fogSolid = Mathf.Max(fogSolid, fogStart + 1f);
        hazeEnd = Mathf.Max(hazeEnd, hazeStart + 1f);
        screenFogTop = Mathf.Max(screenFogTop, screenFogSolid + 0.01f);
        billowSize = Mathf.Max(billowSize, 0.5f);
        houseFogDepth = Mathf.Max(houseFogDepth, 0.5f);
        if (isActiveAndEnabled) PushGlobals(false);   // no objects may be created from OnValidate
    }

    void LateUpdate()
    {
        if (cam == null || generator == null || player == null) AutoFind();
        Track(false);
        if (Application.isPlaying)
        {
            Vector2 dir = windDirection.sqrMagnitude > 1e-6f ? windDirection.normalized : Vector2.right;
            wind += dir * (billowSpeed * Time.deltaTime);
        }
        PushGlobals();
    }

    /// <summary>Follows the current shot: the lowest route point in it (for the fog sea) and its focus (for the haze).</summary>
    void Track(bool snap)
    {
        float targetFloor;
        Vector3 targetFocus;
        DynamicPathGenerator.Segment seg = generator != null ? generator.PlayerSegment : null;
        DynamicPathGenerator.PathZone zone = seg != null ? seg.zone : null;
        if (zone != null && zone.boundsStarted)
        {
            targetFloor = zone.zoneBounds.min.y;
            targetFocus = zone.framingSolved ? zone.optimalFocus : zone.zoneBounds.center;
        }
        else if (player != null)
        {
            targetFloor = player.transform.position.y - 2f;
            targetFocus = player.transform.position;
        }
        else
        {
            targetFloor = transform.position.y;
            targetFocus = transform.position;
        }

        if (snap || !tracking || !Application.isPlaying || (targetFocus - focus).sqrMagnitude > 300f * 300f)
        {
            floorY = targetFloor; floorVel = 0f;
            focus = targetFocus; focusVel = Vector3.zero;
            tracking = true;
            return;
        }
        floorY = Mathf.SmoothDamp(floorY, targetFloor, ref floorVel, followSmoothTime);
        focus = Vector3.SmoothDamp(focus, targetFocus, ref focusVel, followSmoothTime);
    }

    void PushGlobals(bool createBackdrop = true)
    {
        bool on = enableFog && isActiveAndEnabled;
        if (on && createBackdrop) EnsureBackdrop();
        if (backdropRenderer != null) backdropRenderer.enabled = on;

        Color top = topColor.linear, bottom = bottomColor.linear, glow = glowColor.linear;
        Shader.SetGlobalVector(TopId, new Vector4(top.r, top.g, top.b, 1f));
        Shader.SetGlobalVector(BottomId, new Vector4(bottom.r, bottom.g, bottom.b, 1f));
        Shader.SetGlobalVector(GlowId, new Vector4(glow.r, glow.g, glow.b, glowStrength));
        Shader.SetGlobalVector(GlowShapeId, new Vector4(glowCentre.x, glowCentre.y, glowRadius, 0f));
        Shader.SetGlobalVector(HeightId, new Vector4(floorY - fogStart, floorY - fogSolid, billowHeight, 1f / Mathf.Max(billowSize, 0.5f)));
        Shader.SetGlobalVector(WindId, new Vector4(wind.x, wind.y, 0f, 0f));
        Shader.SetGlobalVector(ScreenId, new Vector4(screenFogSolid, screenFogTop, screenFogStrength, veil));
        Shader.SetGlobalVector(HouseId, new Vector4(houseFogShare, houseFogAtHouse, 1f / Mathf.Max(houseFogDepth, 0.01f), 0f));
        if (cam != null) PushView(cam, on);
        else Shader.SetGlobalVector(ViewId, Vector4.zero);
    }

    /// <summary>The view-dependent part, set just before each camera renders so it matches that camera exactly.</summary>
    void PushView(Camera c, bool on)
    {
        // Haze is by distance along the ground away from the camera, so a tall wall behind the route is hazy all the way up.
        Vector3 fwd = c.transform.forward, pos = c.transform.position;
        Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
        if (flat.sqrMagnitude > 1e-4f) fwd = flat.normalized;
        float focusDepth = Vector3.Dot(focus - pos, fwd);
        Shader.SetGlobalVector(DepthId, new Vector4(focusDepth + hazeStart, focusDepth + hazeEnd, hazeMax, 0f));
        Shader.SetGlobalVector(ViewId, new Vector4(fwd.x, fwd.y, fwd.z, on ? 1f : 0f));
        Shader.SetGlobalVector(ViewPosId, new Vector4(pos.x, pos.y, pos.z, 0f));
    }

    void OnBeginCamera(ScriptableRenderContext context, Camera c)
    {
        if (c.cameraType == CameraType.Game && c != cam) return;   // other game cameras (UI, previews) keep the main view's values
        PushView(c, enableFog && isActiveAndEnabled);
    }

    // ---------- background ----------

    void EnsureBackdrop()
    {
        if (backdrop != null) return;
        if (backdropShader == null) backdropShader = Shader.Find("Hidden/ChasmFogBackdrop");
        if (backdropShader == null) return;

        backdropMaterial = new Material(backdropShader) { name = "ChasmFogBackdrop", hideFlags = HideFlags.HideAndDontSave };

        // One triangle that covers the screen, given in clip space. Huge bounds so it is never culled.
        var mesh = new Mesh { name = "ChasmFogBackdrop", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(-1f, 3f, 0f), new Vector3(3f, -1f, 0f) };
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);

        backdrop = new GameObject("ChasmFogBackdrop") { hideFlags = HideFlags.HideAndDontSave };
        backdrop.layer = gameObject.layer;
        backdrop.AddComponent<MeshFilter>().sharedMesh = mesh;
        backdropRenderer = backdrop.AddComponent<MeshRenderer>();
        backdropRenderer.sharedMaterial = backdropMaterial;
        backdropRenderer.shadowCastingMode = ShadowCastingMode.Off;
        backdropRenderer.receiveShadows = false;
        backdropRenderer.lightProbeUsage = LightProbeUsage.Off;
        backdropRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    void DestroyBackdrop()
    {
        if (backdrop != null)
        {
            Mesh mesh = backdrop.GetComponent<MeshFilter>().sharedMesh;
            DestroySafe(mesh);
            DestroySafe(backdrop);
        }
        DestroySafe(backdropMaterial);
        backdrop = null;
        backdropRenderer = null;
        backdropMaterial = null;
    }

    static void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }
}
