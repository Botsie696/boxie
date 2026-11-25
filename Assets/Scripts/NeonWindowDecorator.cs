using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Monument-Valley-style set dressing for the cube rail. Watches DynamicPathGenerator's zones
/// and, for a fraction of them, spawns a small tiered stone tower off to the side, well clear of
/// the path, with a glowing window embedded in it and a matching point light so nearby path
/// cubes pick up a coloured specular hit. Every zone also gets a sparse handful of firefly
/// particles drifting near the path for ambiance.
///
/// Purely decorative - nothing here has a collider, so none of it can interrupt the runner.
///
/// Zones are generated and culled continuously by the path generator, so decorations are keyed
/// off the same PathZone references and torn down the moment their zone drops out of
/// pathGen.Zones (culled behind the runner, or the whole path was regenerated on restart).
/// </summary>
public class NeonWindowDecorator : MonoBehaviour
{
    [Header("References")]
    public DynamicPathGenerator pathGen;

    [Header("Tower Spawn")]
    [Range(0f, 1f)] public float spawnChance = 0.5f;
    [Tooltip("Distance from the path centre. Kept large and clear of zoneBounds so the tower reads as background scenery, not part of the path.")]
    public float lateralOffset = 16f;
    public float heightOffset = 7f;
    public float windowWidth = 3f;
    public float windowHeight = 4f;
    [Tooltip("Unused now that towers are placed in the emptiest patch of the frame. Kept so saved scenes still load.")]
    public float sideClearance = 8f;

    [Header("Background Models (replace the stepped towers)")]
    [Tooltip("Drop any FBX / prefab here. Each one is split into its separate pieces (the houses inside it), every piece is re-centred so its pivot is the bottom-centre of its own bounds (which zeroes out wherever it was scattered in the file), and scaled to the tower's size. Whenever at least one usable piece exists, houses are placed instead of the stepped towers, at exactly the same spots. Leave empty to keep the towers.")]
    public GameObject[] backgroundModels;
    [Tooltip("Longest horizontal side every piece is scaled to. 0 = same as the stepped tower's base width.")]
    public float modelWidth = 0f;
    [Tooltip("A piece is never taller than this after scaling. 0 = same as the tallest stepped tower.")]
    public float modelMaxHeight = 0f;

    [Header("Background Props (one prefab each, instead of splitting a model)")]
    [Tooltip("Place the prefabs listed below instead of splitting Background Models into pieces. For packs that ship one prop per prefab (market stalls, rides, a big house) rather than one file holding a row of houses. Everything downstream is the same: the props stand in the same pockets, on the same rock columns, and take the same lights and fog.")]
    public bool useBackgroundProps = false;
    [Tooltip("The props to choose from. Each one gets its own size, its own chance, and its own distance behind the route, so a big landmark can be set to stand far back while the stalls crowd in.")]
    public BackgroundProp[] backgroundProps;
    [Tooltip("How much of its slot on the rock the prop itself takes. The rest is the ground around it, where the clutter goes.")]
    [Range(0.3f, 1f)] public float propFill = 0.62f;

    [Header("Prop Clutter (small pieces around each prop)")]
    [Tooltip("Small props - crates, toys, bins - scattered on the rock around the main one. Leave empty for none.")]
    public GameObject[] propClutter;
    [Tooltip("Fewest pieces of clutter around a prop.")]
    [Range(0, 6)] public int clutterMin = 1;
    [Tooltip("Most pieces of clutter around a prop.")]
    [Range(0, 6)] public int clutterMax = 3;
    [Tooltip("Width in world units each piece of clutter is scaled to.")]
    public float clutterWidth = 1.4f;
    [Tooltip("How much of the rock's ledge may be used. The flat top is measured across its widest span, so its corners are already falling away - anything placed past this hangs in the air. A piece with no room left is simply not placed.")]
    [Range(0.2f, 1f)] public float ledgeSafety = 0.62f;

    [Header("Forest (trees around the props that ask for one)")]
    [Tooltip("Trees and bushes to ring a prop with. Used only by props with Forest ticked.")]
    public GameObject[] forestProps;
    [Tooltip("Fewest trees around such a prop.")]
    [Range(0, 12)] public int forestMin = 3;
    [Tooltip("Most trees around such a prop.")]
    [Range(0, 12)] public int forestMax = 6;
    [Tooltip("Width in world units each tree is scaled to.")]
    public float forestWidth = 5f;

    /// <summary>One prop in the manual list: what to place, how big, how often, and how far back.</summary>
    [System.Serializable]
    public class BackgroundProp
    {
        public GameObject prefab;
        [Tooltip("Width in world units of the patch of rock this prop is given. The prop itself fills Prop Fill of it. 0 = the prefab's own size.")]
        public float slotWidth = 8f;
        [Tooltip("Chance of this prop against the others. 0 leaves it out.")]
        [Range(0f, 10f)] public float weight = 1f;
        [Tooltip("How far behind the route this one stands, min and max. Leave at 0 for the shared House Depth Behind - set it for a landmark that has to sit well back.")]
        public Vector2 depthBehind;
        [Tooltip("Scatter clutter on the rock around this prop.")]
        public bool clutter = true;
        [Tooltip("Ring this one with trees, so it stands in its own patch of forest. Needs Forest Props filled in.")]
        public bool forest = false;
        [Tooltip("How wide the rock under this one is, as a multiple of its slot. 0 = the shared Cliff Width Over House. A landmark on a big cliff, with room for a wood around it, wants more.")]
        [Range(0f, 4f)] public float cliffWidth = 0f;
        [Tooltip("Colour of this prop's lamps. Leave the alpha at 0 to use the shared House Light Colour - set it for a rock that should glow its own colour.")]
        [ColorUsage(false)] public Color lightColor = new Color(0f, 0f, 0f, 0f);
        [Tooltip("Multiplies this prop's lamp brightness.")]
        [Range(0.1f, 4f)] public float lightScale = 1f;
    }

    [Tooltip("Chance, per zone, of an EXTRA house tucked right up against the path - in the inner corners and just below the legs, close but not covering them. Only applies to background models.")]
    [Range(0f, 1f)] public float tuckedHouseChance = 0.3f;
    [Tooltip("How much of the house's width may separate a tucked house from the path on screen. Smaller hugs the path harder.")]
    [Range(0f, 0.4f)] public float tuckedClearance = 0.06f;
    [Tooltip("Chance, per zone, of a house dropped into an empty pocket beside the path (inner corners, gaps between legs) so the corners are not left bare. Replaces tuckedHouseChance, which is kept only so saved scenes still load.")]
    [Range(0f, 1f)] public float pocketHouseChance = 0.6f;
    [Tooltip("How many are tried per zone. Each one looks for its own pocket and keeps clear of the ones already standing, so a busy fairground can be asked for without crowding the route. 1 is the old behaviour.")]
    [Range(1, 4)] public int housesPerZone = 1;
    [Tooltip("A house standing on a cliff keeps at least this fraction of the route's width between itself and the middle of the route on screen. Smaller lets houses stand closer in.")]
    [Range(0f, 0.3f)] public float houseSideKeepOut = 0.17f;
    [Tooltip("How far behind the route (camera depth) a house on a cliff stands, min and max. Smaller brings it closer to the route.")]
    public Vector2 houseDepthBehind = new Vector2(14f, 32f);

    [Header("Cliffs (rock each background house stands on)")]
    [Tooltip("Cliff prefabs. Each house gets a column of these stacked beneath it, a different rock every time, running down past the bottom of the screen so the foot of the cliff is never seen. Leave empty for floating houses.")]
    public GameObject[] cliffPrefabs;
    [Tooltip("Width of the top rock relative to the house it carries.")]
    [Range(0.8f, 1.6f)] public float cliffWidthOverHouse = 1.05f;
    [Tooltip("How far below the house the column runs, in world units. Must clear the bottom of the screen.")]
    public float cliffDepth = 140f;
    [Tooltip("Rock tint, so the pale pack textures read as the dark stone in the reference.")]
    public Color cliffTint = new Color(0.2f, 0.22f, 0.32f, 1f);
    [Tooltip("The pack's materials fade to a cyan height fog below y = 5.7. Cliffs use this instead, fading in from just under the house down to the foot of the column.")]
    public Color cliffFogColor = new Color(0.02f, 0.03f, 0.07f, 1f);

    [Header("Cliffs Never Cover The Path")]
    [Tooltip("A house and the rock it stands on are faded out while any part of them is BETWEEN the camera and a piece of drawn track, and faded back in the moment the view is clear. Placement already keeps them behind the route, but the camera turns with every shot and the route keeps growing past them, so this is the guarantee. Off elsewhere, so those scenes keep the houses they were tuned with.")]
    public bool cliffsNeverCoverPath = false;
    [Tooltip("Room left around the rock, in world units. A sight-line passing this close to it counts as covered.")]
    [Range(0f, 8f)] public float cliffCoverMargin = 1.5f;
    [Tooltip("Seconds a covering house takes to fade out, and to come back.")]
    [Range(0.1f, 2f)] public float cliffCoverFade = 0.45f;

    [Header("Keep Path Clear")]
    [Tooltip("Shrink a tower away whenever its screen footprint covers any path cube or the runner, from whatever angle the camera is using right now. Placement only knows the shot of the zone the tower was built for, but a tower stays alive across several shots at different yaws and the path keeps growing past it - this is the check that actually guarantees the route is never covered.")]
    public bool hideTowersOverPath = true;
    [Tooltip("Viewport margin around a tower's footprint. Track closer to the tower than this counts as covered. Kept tiny so a tower only goes away when it is really on top of the route.")]
    [Range(0f, 0.2f)] public float screenMargin = 0.012f;
    [Tooltip("Seconds for a tower to shrink away or grow back.")]
    public float fadeTime = 0.4f;

    [Header("No pop-in / pop-out")]
    [Tooltip("A house is only built while the camera cannot see where it will stand (it waits, and tries again, until the camera looks away), and a house whose stretch of route has been left behind is only removed once no part of it is on screen. So a house is never seen appearing or disappearing: it has always been there.")]
    public bool houseNeverPopsInView = false;
    [Tooltip("Safety net: a house that has been left behind is removed after this many seconds even if it is still on screen.")]
    public float retireTimeout = 45f;
    [Tooltip("Extra room around a new house's footprint that must also be off screen.")]
    public float appearMargin = 6f;

    [Header("Fog")]
    [Tooltip("Keep the rock every house stands on completely out of the fog: no fade down the column, no haze. The column still runs well past the bottom of the screen, so its foot is never seen. Houses and path cubes only take Unity's scene fog (Lighting > Environment), so switch that off too for a scene with no fog on them at all.")]
    public bool housesClearOfFog = false;
    [Tooltip("Houses and the rock they stand on join the chasm fog but only take part of it (ChasmFog > Houses and their rock): the least on the house, more down the column. Off: the rock keeps its own long fade and the house takes no chasm fog. Ignored while Houses Clear Of Fog is on.")]
    public bool housesInChasmFog = false;

    bool HousesTakeFogShare => housesInChasmFog && !housesClearOfFog;

    [Header("Materials")]
    public Material[] neonMaterials;
    public Material[] wallMaterials;

    [Header("Point Light")]
    public float pointLightRange = 20f;
    public float pointLightIntensity = 2.2f;

    [Header("House Lights")]
    [Tooltip("Light the background houses from within: their windows glow and they cast a warm pool over the rock they stand on.")]
    public bool houseLights = true;
    [Tooltip("Colour of the light inside the houses.")]
    [ColorUsage(false)] public Color houseLightColor = new Color(1f, 0.72f, 0.42f, 1f);
    [Tooltip("Brightness of the lamp inside the house. It lights the house's own walls, its porches and the rock around it.")]
    [Range(0f, 12f)] public float houseLightIntensity = 3.2f;
    [Tooltip("Reach of that lamp, as a multiple of the house's width. Keep it short enough not to reach the track.")]
    [Range(0.5f, 6f)] public float houseLightRange = 2.2f;
    [Tooltip("Brightness of the second lamp, just under the house, which pools on the top of the rock it stands on. 0 = none.")]
    [Range(0f, 12f)] public float houseGroundLightIntensity = 4f;
    [Tooltip("Reach of that one, as a multiple of the house's width.")]
    [Range(0.5f, 6f)] public float houseGroundLightRange = 2.6f;
    [Tooltip("Brightness of a third lamp out in FRONT of the house, lighting the face that is turned toward the track. 0 = none. Houses that are lit from within need nothing here; a market stall or a painted booth, which is mostly a dark closed box from outside, comes alive with it.")]
    [Range(0f, 12f)] public float houseFrontLightIntensity = 0f;
    [Tooltip("How far out in front it stands, as a multiple of the house's width.")]
    [Range(0.2f, 3f)] public float houseFrontLightOffset = 0.8f;
    [Tooltip("Reach of the front lamp, as a multiple of the house's width.")]
    [Range(0.5f, 6f)] public float houseFrontLightRange = 2.6f;

    [Header("House Windows (lit from the albedo)")]
    [Tooltip("Make the window panes painted into the house models glow. Costs nothing per house: it is a few instructions in the house shader.")]
    public bool houseWindowGlow = true;
    [Tooltip("Colour of the light behind the panes.")]
    [ColorUsage(false)] public Color windowGlowColor = new Color(1f, 0.76f, 0.42f, 1f);
    [Tooltip("How brightly the panes glow.")]
    [Range(0f, 6f)] public float windowGlowStrength = 3f;
    [Tooltip("How bright a spot of the texture has to be before it counts as a lit window. Lower it and roofs and sand start to glow too.")]
    [Range(0f, 1f)] public float windowGlowBrightness = 0.5f;
    [Tooltip("How warm it has to be. This is what keeps plaster and pale stone dark.")]
    [Range(0f, 0.6f)] public float windowGlowWarmth = 0.12f;
    [Tooltip("How soft the edge of both tests is.")]
    [Range(0.01f, 0.5f)] public float windowGlowSoftness = 0.16f;
    [Tooltip("Custom/ChasmFogHouseLit. Referenced here so builds include it.")]
    public Shader houseGlowShader;

    [Header("House Halo")]
    [Tooltip("A soft halo around each lit house, so it reads as giving off light without any post-processing.")]
    public bool houseHalo = true;
    [Tooltip("Width of the halo, as a multiple of the house's width.")]
    [Range(0.5f, 4f)] public float houseHaloSize = 1f;
    [Tooltip("Brightness of the halo.")]
    [Range(0f, 2f)] public float houseHaloStrength = 0.3f;

    [Header("Fireflies")]
    public Material fireflyMaterial;
    [Range(0, 20)] public int fireflyCount = 6;
    public float fireflyRadius = 10f;
    [Tooltip("Colours the drifting lights are drawn from, one per particle. Leave empty for the warm gold-to-green they have always had; fill it for the coloured stars of a fairground.")]
    [ColorUsage(false)] public Color[] fireflyColors;
    [Tooltip("Size of one light, min and max.")]
    public Vector2 fireflySize = new Vector2(0.05f, 0.14f);

    private readonly Dictionary<DynamicPathGenerator.PathZone, GameObject> spawned =
        new Dictionary<DynamicPathGenerator.PathZone, GameObject>();

    class Tower
    {
        public Transform root;
        public List<Vector3> corners = new List<Vector3>(); // oriented-box corners of every mesh, world space at full size - never the shrunk ones, or hiding would un-hide it
        public Light light;
        public float baseIntensity;
        public float visibility;    // 0 = shrunk away, 1 = full size
        public bool hidden;
        public Vector3 basePos;     // where the root stands
        public bool isHouse;        // background houses never shrink, hide or move: placement alone keeps them clear of the route
    }

    /// <summary>One house cut out of a background model: pivot at its bottom-centre, plus the uniform scale that brings it to tower size.</summary>
    class HouseTemplate
    {
        public GameObject prefab;
        public float scale;
        public float width;          // the patch of rock this one is given (the prop itself may fill only part of it)
        public float height;         // after scaling
        public float weight = 1f;    // chance against the other templates
        public Vector2 depthBehind;  // zero = the shared houseDepthBehind
        public bool clutter = true;
        public bool forest;
        public float cliffWidth;     // 0 = the shared cliffWidthOverHouse
        public Color lightColor;     // alpha 0 = the shared houseLightColor
        public float lightScale = 1f;
    }

    float lastPlaceScore = -1f;       // enclosure score of the spot PlaceInEmptySpace last returned; -1 when nothing fit
    const float minPocketScore = 2f;  // track on at least this many of 8 sides, or it is open space, not a pocket

    private readonly List<HouseTemplate> houses = new List<HouseTemplate>();
    private readonly List<HouseTemplate> clutterPieces = new List<HouseTemplate>();
    private readonly List<HouseTemplate> forestPieces = new List<HouseTemplate>();
    private GameObject housePool;
    private Vector2 activeDepthBehind;   // the depth the house being placed right now wants
    private float activeCliffWidth;      // ...and how wide it wants the rock under it

    private readonly List<Tower> towers = new List<Tower>();
    private readonly List<Vector3> pathOnScreen = new List<Vector3>(); // viewport x, y and distance from the camera
    private readonly List<Vector3> pathInWorld = new List<Vector3>();  // the same points, in the world
    private readonly List<Vector3> visiblePoints = new List<Vector3>();
    private PathDepthShading shading;

    void Start()
    {
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (houseGlowShader == null) houseGlowShader = Shader.Find("Custom/ChasmFogHouseLit");
        PushWindowGlowGlobals();
        BuildHousePool();
    }

    void OnValidate()
    {
        if (isActiveAndEnabled) PushWindowGlowGlobals();   // the sliders read live, in play mode and out of it
    }

    void OnDestroy()
    {
        if (housePool != null) Destroy(housePool);
        foreach (var kv in glowMaterials) if (kv.Value != null) Destroy(kv.Value);
        glowMaterials.Clear();
        if (haloMaterial != null) Destroy(haloMaterial);
        if (haloMesh != null) Destroy(haloMesh);
        haloMaterial = null;
        haloMesh = null;
    }

    /// <summary>
    /// Splits every background model into its individual pieces. Descends through single-child
    /// wrappers, then treats each child of the first node that branches as one house. Each piece
    /// is wrapped in a holder whose origin is the bottom-centre of the piece's bounds, so wherever
    /// the piece was scattered in the file it now sits at zero.
    /// </summary>
    [ContextMenu("Rebuild House Pool")]
    void BuildHousePool()
    {
        houses.Clear();
        clutterPieces.Clear();
        forestPieces.Clear();
        if (housePool != null) Destroy(housePool);
        housePool = null;

        if (useBackgroundProps && backgroundProps != null && backgroundProps.Length > 0)
        {
            BuildPropPool();
            return;
        }
        if (backgroundModels == null) return;

        float targetWidth = modelWidth > 0f ? modelWidth : windowWidth * 2.2f;
        float maxHeight = modelMaxHeight > 0f ? modelMaxHeight : windowHeight * 0.68f * 3f;

        housePool = new GameObject("HousePool");
        housePool.SetActive(false); // templates live here, invisible, and are cloned per tower

        foreach (GameObject model in backgroundModels)
        {
            if (model == null) continue;

            GameObject instance = Instantiate(model, housePool.transform, false);
            bool instanceUsed = false;

            foreach (Transform piece in FindPieces(instance.transform))
            {
                if (!TryWorldBounds(piece, out Bounds b)) continue;

                float footprint = Mathf.Max(b.size.x, b.size.z);
                if (footprint < 1e-4f || b.size.y < footprint * 0.1f) continue; // ground slab / flat plate, not a house

                var holder = new GameObject(piece.name + "_House");
                holder.transform.SetParent(housePool.transform, false);
                holder.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
                piece.SetParent(holder.transform, true);
                if (piece == instance.transform) instanceUsed = true;

                foreach (Collider c in holder.GetComponentsInChildren<Collider>(true)) c.enabled = false;

                float scale = targetWidth / footprint;
                if (b.size.y * scale > maxHeight) scale = maxHeight / b.size.y;

                houses.Add(new HouseTemplate { prefab = holder, scale = scale, width = footprint * scale, height = b.size.y * scale });
            }

            if (!instanceUsed) Destroy(instance); // what is left is an empty shell
        }
    }

    /// <summary>
    /// The manual list: one prefab is one prop, taken as it comes. Each is wrapped in a holder
    /// whose origin is the bottom-centre of its bounds (so it stands on the rock however its pivot
    /// was authored) and scaled to fill its share of the slot it is given. The clutter is built the
    /// same way, just small.
    /// </summary>
    void BuildPropPool()
    {
        housePool = new GameObject("HousePool");
        housePool.SetActive(false);   // templates live here, invisible, and are cloned per tower

        bool anyClutter = propClutter != null && propClutter.Length > 0 && clutterMax > 0;
        bool anyForest = forestProps != null && forestProps.Length > 0 && forestMax > 0;

        foreach (BackgroundProp p in backgroundProps)
        {
            if (p == null || p.prefab == null || p.weight <= 0f) continue;
            bool leavesRoom = (anyClutter && p.clutter) || (anyForest && p.forest);
            HouseTemplate t = WrapProp(p.prefab, p.slotWidth, leavesRoom ? propFill : 1f);
            if (t == null) continue;
            t.weight = p.weight;
            t.depthBehind = p.depthBehind;
            t.clutter = p.clutter;
            t.forest = p.forest;
            t.cliffWidth = p.cliffWidth;
            t.lightColor = p.lightColor;
            t.lightScale = p.lightScale;
            houses.Add(t);
        }

        if (forestProps != null && forestMax > 0)
            foreach (GameObject tree in forestProps)
            {
                HouseTemplate t = WrapProp(tree, forestWidth, 1f);
                if (t != null) forestPieces.Add(t);
            }

        if (!anyClutter) return;
        foreach (GameObject piece in propClutter)
        {
            HouseTemplate t = WrapProp(piece, clutterWidth, 1f);
            if (t != null) clutterPieces.Add(t);
        }
    }

    /// <summary>Wraps one prefab in a bottom-centred holder and works out the scale that brings it to width * fill.</summary>
    HouseTemplate WrapProp(GameObject prefab, float slotWidth, float fill)
    {
        if (prefab == null) return null;

        GameObject instance = Instantiate(prefab, housePool.transform, false);
        instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        if (!TryWorldBounds(instance.transform, out Bounds b) || b.size.y < 1e-4f) { Destroy(instance); return null; }

        var holder = new GameObject(prefab.name + "_Prop");
        holder.transform.SetParent(housePool.transform, false);
        holder.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
        instance.transform.SetParent(holder.transform, true);

        foreach (Collider c in holder.GetComponentsInChildren<Collider>(true)) c.enabled = false;

        float footprint = Mathf.Max(b.size.x, b.size.z);
        float slot = slotWidth > 0.01f ? slotWidth : footprint;
        float scale = footprint > 1e-4f ? slot * fill / footprint : 1f;

        return new HouseTemplate { prefab = holder, scale = scale, width = slot, height = b.size.y * scale };
    }

    /// <summary>
    /// Drops a few small props onto the ring of rock between the main prop and the edge of its
    /// ledge, each turned a different way. They hang off the same tower root, so they live and die
    /// with it and never have to be tracked on their own.
    /// </summary>
    void AddPropClutter(Transform parent, HouseTemplate house, Vector3 top, Bounds propBounds)
    {
        ScatterOnLedge(parent, clutterPieces, clutterMin, clutterMax, house, top, InnerRadius(house, propBounds), 0.55f);
    }

    /// <summary>
    /// How much room the prop itself takes. Measured from what it draws, but never more than most of
    /// its slot: a prop turned 45 degrees has a world box half again as wide as the prop, and taking
    /// that at face value would leave no rock for anything to stand on.
    /// </summary>
    static float InnerRadius(HouseTemplate house, Bounds propBounds)
    {
        float measured = Mathf.Max(propBounds.size.x, propBounds.size.z) * 0.5f;
        return Mathf.Min(measured, house.width * 0.35f);
    }

    /// <summary>A ring of trees, further out than the clutter, so the prop stands in its own wood.</summary>
    void AddPropForest(Transform parent, HouseTemplate house, Vector3 top, Bounds propBounds)
    {
        ScatterOnLedge(parent, forestPieces, forestMin, forestMax, house, top, InnerRadius(house, propBounds), 1f);
    }

    /// <summary>
    /// Drops pieces onto the rock around a prop, each turned a different way. They hang off the same
    /// tower root, so they live and die with it and never have to be tracked on their own.
    ///
    /// Nothing is ever put past ledgeSafety of the rock's half width: the flat top is measured across
    /// its widest span, so its corners are already falling away, and a piece out there hangs in the
    /// air. A piece with no room left is not placed at all rather than placed badly.
    /// </summary>
    void ScatterOnLedge(Transform parent, List<HouseTemplate> pool, int min, int max,
                        HouseTemplate house, Vector3 top, float inner, float spread)
    {
        if (pool.Count == 0) return;

        float usable = house.width * activeCliffWidth * 0.5f * ledgeSafety;
        int wanted = Random.Range(Mathf.Min(min, max), max + 1);

        for (int i = 0; i < wanted; i++)
        {
            HouseTemplate piece = pool[Random.Range(0, pool.Count)];
            float pad = piece.width * 0.7f;
            float near = inner + pad;
            float far = usable - pad;
            if (far < near) continue;                                  // no rock left to stand on

            float radius = Random.Range(near, Mathf.Min(far, near + piece.width * spread));
            float angle = Random.Range(0f, 360f);

            GameObject c = Instantiate(piece.prefab, parent);
            c.name = piece.prefab.name;
            c.transform.SetPositionAndRotation(
                top + Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * radius),
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            c.transform.localScale = Vector3.one * (piece.scale * Random.Range(0.85f, 1.15f));

            if (housesClearOfFog) ChasmFog.Apply(c, noFog: true);
            else if (HousesTakeFogShare) ChasmFog.Apply(c, house: true);
            ApplyWindowGlow(c);
        }
    }

    /// <summary>Templates in the pool - houses cut out of the models, or the props from the manual list.</summary>
    public int HousePoolCount => houses.Count;
    /// <summary>Small pieces available to scatter around a prop.</summary>
    public int ClutterPoolCount => clutterPieces.Count;
    /// <summary>Houses and towers standing right now.</summary>
    public int TowerCount => towers.Count;
    /// <summary>Zones still waiting for the route ahead before their house can be placed.</summary>
    public int PendingHouseCount => pendingHouses.Count;
    /// <summary>Enclosure score of the last spot tried: under minPocketScore means no pocket was found and no house was placed.</summary>
    public float LastPlaceScore => lastPlaceScore;

    /// <summary>One template, by weight.</summary>
    HouseTemplate PickHouse()
    {
        if (houses.Count == 0) return null;

        float total = 0f;
        foreach (HouseTemplate h in houses) total += Mathf.Max(0f, h.weight);
        if (total <= 0f) return houses[Random.Range(0, houses.Count)];

        float r = Random.value * total;
        foreach (HouseTemplate h in houses)
        {
            r -= Mathf.Max(0f, h.weight);
            if (r <= 0f) return h;
        }
        return houses[houses.Count - 1];
    }

    static List<Transform> FindPieces(Transform root)
    {
        Transform node = root;
        while (node.childCount == 1 && node.GetComponent<Renderer>() == null) node = node.GetChild(0);

        var pieces = new List<Transform>();
        if (node.childCount == 0 || node.GetComponent<Renderer>() != null)
        {
            pieces.Add(node); // the model is a single piece
            return pieces;
        }

        foreach (Transform child in node)
            if (child.GetComponentInChildren<Renderer>(true) != null) pieces.Add(child);
        return pieces;
    }

    /// <summary>World-space mesh bounds of a hierarchy, worked out from the meshes so it is right even while inactive.</summary>
    static bool TryWorldBounds(Transform root, out Bounds bounds)
    {
        Bounds acc = default;
        bool any = false;

        void Add(Mesh mesh, Matrix4x4 m)
        {
            if (mesh == null) return;
            Bounds mb = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + new Vector3(
                    (i & 1) == 0 ? -mb.extents.x : mb.extents.x,
                    (i & 2) == 0 ? -mb.extents.y : mb.extents.y,
                    (i & 4) == 0 ? -mb.extents.z : mb.extents.z);
                Vector3 w = m.MultiplyPoint3x4(corner);
                if (!any) { acc = new Bounds(w, Vector3.zero); any = true; }
                else acc.Encapsulate(w);
            }
        }

        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            if (mf.GetComponent<Renderer>() != null) Add(mf.sharedMesh, mf.transform.localToWorldMatrix);
        foreach (var sk in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            Add(sk.sharedMesh, sk.transform.localToWorldMatrix);
        bounds = acc;
        return any;
    }

    void Update()
    {
        if (pathGen == null) return;

        var zones = pathGen.Zones;

        foreach (var zone in zones)
        {
            if (!zone.isSealed || zone.segments.Count == 0) continue;
            if (spawned.ContainsKey(zone)) continue;

            spawned[zone] = BuildZoneDecor(zone);
        }

        // Drop decorations whose zone no longer exists (culled behind, or path regenerated).
        List<DynamicPathGenerator.PathZone> dead = null;
        foreach (var kv in spawned)
        {
            bool stillLive = false;
            foreach (var z in zones) { if (z == kv.Key) { stillLive = true; break; } }
            if (stillLive) continue;

            if (kv.Value != null)
            {
                if (houseNeverPopsInView) retiring.Add(new Retiring { go = kv.Value, since = Time.time });
                else Destroy(kv.Value);
            }
            (dead ??= new List<DynamicPathGenerator.PathZone>()).Add(kv.Key);
        }
        if (dead != null) foreach (var z in dead) spawned.Remove(z);
        if (houseNeverPopsInView) DropRetired();

        BuildPendingHouses();
        UpdateTowerVisibility();
        PushWindowGlowGlobals();
        UpdateHalos(Camera.main);
    }

    /// <summary>
    /// Shrinks away any tower that covers the route in the CURRENT camera view, and grows it
    /// back once the view no longer passes through it. Runs against the live camera, so it holds
    /// through transitions, yaw changes and track generated after the tower was placed.
    /// </summary>
    void UpdateTowerVisibility()
    {
        towers.RemoveAll(t => t.root == null);
        if (towers.Count == 0) return;

        Camera cam = Camera.main;
        bool check = (hideTowersOverPath || cliffsNeverCoverPath) && cam != null;

        if (check)
        {
            pathOnScreen.Clear();
            pathInWorld.Clear();

            // Only track that is actually drawn counts: with the visible window on, most of the
            // route is hidden, and a tower standing over a hidden stretch covers nothing.
            if (shading == null) shading = pathGen.GetComponent<PathDepthShading>();
            if (shading != null && shading.enabled && shading.limitVisibleLegs)
            {
                visiblePoints.Clear();
                shading.CollectVisible(visiblePoints);
                foreach (Vector3 p in visiblePoints) AddScreenPoint(cam, p);
            }
            else
            {
                foreach (var zone in pathGen.Zones)
                    foreach (var seg in zone.segments)
                        foreach (var cube in seg.cubes)
                            if (cube != null) AddScreenPoint(cam, cube.transform.position);
            }

            if (pathGen.player != null) AddScreenPoint(cam, pathGen.player.transform.position);
        }

        float step = Time.deltaTime / Mathf.Max(0.01f, fadeTime);
        float houseStep = Time.deltaTime / Mathf.Max(0.01f, cliffCoverFade);

        foreach (Tower t in towers)
        {
            // Houses are placed once and stay exactly where they are until their zone is culled -
            // unless this scene asks for the guarantee that nothing ever stands over the track.
            if (t.isHouse && !(cliffsNeverCoverPath && check)) continue;

            float targetScale = 1f;
            if (check)
            {
                t.hidden = t.isHouse
                    ? CoversPathInFront(cam, t.corners, cliffCoverMargin)
                    : CoversPath(ScreenRect(cam, t.corners), screenMargin);
                if (t.hidden) targetScale = 0f;
            }
            else t.hidden = false;

            t.visibility = Mathf.MoveTowards(t.visibility, targetScale, t.isHouse ? houseStep : step);
            float s = t.visibility;

            bool active = s > 0.001f;
            if (t.root.gameObject.activeSelf != active) t.root.gameObject.SetActive(active);
            if (!active) continue;

            t.root.localScale = Vector3.one * s;
            if (t.light != null) t.light.intensity = t.baseIntensity * s;
        }
    }

    void AddScreenPoint(Camera cam, Vector3 world)
    {
        Vector3 vp = cam.WorldToViewportPoint(world);
        if (vp.z <= 0f) return;
        if (vp.x < -0.2f || vp.x > 1.2f || vp.y < -0.2f || vp.y > 1.2f) return;
        pathOnScreen.Add(vp);
        pathInWorld.Add(world);
    }

    static Rect ScreenRect(Camera cam, List<Vector3> corners)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (Vector3 c in corners)
        {
            Vector3 vp = cam.WorldToViewportPoint(c);
            minX = Mathf.Min(minX, vp.x); maxX = Mathf.Max(maxX, vp.x);
            minY = Mathf.Min(minY, vp.y); maxY = Mathf.Max(maxY, vp.y);
        }
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// True when this house and the rock under it stand BETWEEN the camera and a piece of drawn
    /// track. The camera is orthographic, so every sight-line to it is the same direction: a ray
    /// from each track point towards the camera, tested against the box around the house and its
    /// column. Track in FRONT of the rock draws over it and is not covered, so it never counts -
    /// which is what keeps a house standing behind the route from fading for nothing.
    ///
    /// The same test the background rocks use (PathBackgroundGenerator.UpdateBlocking).
    /// </summary>
    bool CoversPathInFront(Camera cam, List<Vector3> corners, float margin)
    {
        if (corners.Count == 0 || pathInWorld.Count == 0) return false;

        Bounds box = new Bounds(corners[0], Vector3.zero);
        for (int i = 1; i < corners.Count; i++) box.Encapsulate(corners[i]);
        box.Expand(margin * 2f);

        Vector3 toCamera = -cam.transform.forward;
        if (box.Contains(cam.transform.position)) return true;

        foreach (Vector3 p in pathInWorld)
            if (box.IntersectRay(new Ray(p, toCamera))) return true;
        return false;
    }

    bool CoversPath(Rect r, float margin)
    {
        float xMin = r.xMin - margin, xMax = r.xMax + margin;
        float yMin = r.yMin - margin, yMax = r.yMax + margin;
        foreach (Vector3 p in pathOnScreen)
            if (p.x > xMin && p.x < xMax && p.y > yMin && p.y < yMax) return true;
        return false;
    }

    class PendingHouse { public DynamicPathGenerator.PathZone zone; public Transform parent; public float nextTry; }
    private readonly List<PendingHouse> pendingHouses = new List<PendingHouse>();

    class Retiring { public GameObject go; public float since; }
    private readonly List<Retiring> retiring = new List<Retiring>();
    bool houseDeferred;   // set by BuildTower when the house would have appeared in view

    static bool SeenBy(Plane[] planes, GameObject go)
    {
        foreach (Renderer rd in go.GetComponentsInChildren<Renderer>())
            if (rd.enabled && GeometryUtility.TestPlanesAABB(planes, rd.bounds)) return true;
        return false;
    }

    /// <summary>Removes decor whose zone is gone, but only once the camera can no longer see any of it.</summary>
    void DropRetired()
    {
        if (retiring.Count == 0) return;
        Camera cam = Camera.main;
        Plane[] planes = cam != null ? GeometryUtility.CalculateFrustumPlanes(cam) : null;
        for (int i = retiring.Count - 1; i >= 0; i--)
        {
            Retiring r = retiring[i];
            if (r.go == null) { retiring.RemoveAt(i); continue; }
            if (planes != null && Time.time - r.since < retireTimeout && SeenBy(planes, r.go)) continue;
            Destroy(r.go);
            retiring.RemoveAt(i);
        }
    }

    bool SeenByCamera(Bounds b)
    {
        Camera cam = Camera.main;
        if (cam == null) return false;
        b.Expand(appearMargin * 2f);
        return GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cam), b);
    }

    /// <summary>True if the box overlaps any track cube (compared against each cube's own bounds, so it does not depend on the cubes having colliders).</summary>
    bool TouchesPath(Bounds box)
    {
        foreach (var z in pathGen.Zones)
            foreach (var seg in z.segments)
                foreach (var cube in seg.cubes)
                {
                    if (cube == null) continue;
                    Renderer r = cube.GetComponentInChildren<Renderer>();
                    if (r != null && r.bounds.Intersects(box)) return true;
                }
        if (pathGen.player != null && box.Contains(pathGen.player.transform.position)) return true;
        return false;
    }

    /// <summary>True if any track cube or the runner is within radius of the column's axis, measured flat on the ground. Camera-independent, so the cliff keeps its distance whichever way the view swings.</summary>
    bool NearPath(Vector3 axis, float radius)
    {
        float r2 = radius * radius;
        foreach (var z in pathGen.Zones)
            foreach (var seg in z.segments)
                foreach (var cube in seg.cubes)
                {
                    if (cube == null) continue;
                    Vector3 d = cube.transform.position - axis;
                    if (d.x * d.x + d.z * d.z < r2) return true;
                }
        if (pathGen.player != null)
        {
            Vector3 d = pathGen.player.transform.position - axis;
            if (d.x * d.x + d.z * d.z < r2) return true;
        }
        return false;
    }

    void BuildPendingHouses()
    {
        var zones = pathGen.Zones;
        for (int i = pendingHouses.Count - 1; i >= 0; i--)
        {
            PendingHouse p = pendingHouses[i];
            int idx = pathGen.IndexOfZone(p.zone);
            if (idx < 0 || p.parent == null) { pendingHouses.RemoveAt(i); continue; } // passed and culled: no longer relevant
            if (idx + 2 >= zones.Count || !zones[idx + 2].isSealed) continue;
            if (houseNeverPopsInView && Time.time < p.nextTry) continue;
            houseDeferred = false;
            BuildTower(p.zone, p.parent, true);
            if (houseNeverPopsInView && houseDeferred) { p.nextTry = Time.time + 0.5f; continue; }   // it would have shown up in view: wait for the camera to look away
            pendingHouses.RemoveAt(i);
        }
    }

    GameObject BuildZoneDecor(DynamicPathGenerator.PathZone zone)
    {
        var container = new GameObject("ZoneDecor");
        container.transform.SetParent(transform, false);

        bool canBuild = houses.Count > 0 ||
                        (wallMaterials != null && wallMaterials.Length > 0 &&
                         neonMaterials != null && neonMaterials.Length > 0);
        bool spawnTower = canBuild && houses.Count == 0 && Random.value <= spawnChance; // with background models, only pocket houses

        if (spawnTower) BuildTower(zone, container.transform, false);
        // The pockets a house fills are formed by track that does not exist yet, so it waits until the route two zones on has been laid.
        if (houses.Count > 0)
            for (int i = 0; i < Mathf.Max(1, housesPerZone); i++)
                if (Random.value <= pocketHouseChance)
                    pendingHouses.Add(new PendingHouse { zone = zone, parent = container.transform });
        if (fireflyMaterial != null && fireflyCount > 0) BuildFireflies(zone, container.transform);

        return container;
    }

    /// <summary>A small stepped tower - a Monument Valley silhouette - standing well clear of the path.</summary>
    void BuildTower(DynamicPathGenerator.PathZone zone, Transform parent, bool tucked, int attempt = 0)
    {
        Vector3 heading = Vector3.zero;
        foreach (var seg in zone.segments) heading += new Vector3(seg.direction.x, 0f, seg.direction.z);
        if (heading.sqrMagnitude < 0.01f) heading = Vector3.forward;
        heading.Normalize();

        Vector3 cross = Vector3.Cross(Vector3.up, heading).normalized;
        float side = Random.value < 0.5f ? 1f : -1f;
        cross *= side;

        Vector3 pathPoint = zone.zoneBounds.center;
        Vector3 baseCentre = pathPoint + cross * lateralOffset;
        Vector3 away = cross; // direction from the path out to the tower

        // Stand the tower in the emptiest patch of the frame, BEHIND the track as the camera
        // sees it, so it shows up in the shot but can only ever be hidden by the track and never
        // hide it. Falls back to the old spot with no camera rig.
        HouseTemplate house = PickHouse();
        // A landmark prop can ask to stand further back than the rest (BackgroundProp.depthBehind).
        activeDepthBehind = house != null && house.depthBehind.y > 0.01f ? house.depthBehind : houseDepthBehind;
        activeCliffWidth = house != null && house.cliffWidth > 0.01f ? house.cliffWidth : cliffWidthOverHouse;
        // The rock under a house is activeCliffWidth times its slot and it is the rock, not the house,
        // that can end up over the track - so that is the width the spot has to be found for.
        bool onCliff = cliffPrefabs != null && cliffPrefabs.Length > 0;
        float footprint = house != null ? house.width * (onCliff ? Mathf.Max(1f, activeCliffWidth) : 1f) : windowWidth * 2.2f;
        float standHeight = house != null ? house.height : windowHeight * 0.68f * 3f; // three tiers is the tallest a tower gets

        CameraParentRotator rig = FindFirstObjectByType<CameraParentRotator>();
        if (rig != null)
        {
            // The shot showing this stretch is the next zone's, so place for that yaw.
            int zi = pathGen.IndexOfZone(zone);
            var shot = zi + 1 < pathGen.Zones.Count ? pathGen.Zones[zi + 1] : zone;
            rig.EnsureZoneSolved(shot);
            if (shot.framingSolved)
                baseCentre = PlaceInEmptySpace(zone, rig.isometricTiltX, shot.optimalCameraYaw, footprint, standHeight, tucked, out away);
            else lastPlaceScore = -1f;
        }

        // A house only goes where it truly fits a pocket beside the route. No pocket, no house: fewer, better placed.
        if (house != null && (rig == null || lastPlaceScore < minPocketScore)) return;

        Quaternion facing = Quaternion.LookRotation(-away, Vector3.up) *
                             Quaternion.Euler(0f, Random.Range(-6f, 6f), 0f); // slight imperfection, less "fake"

        // Everything hangs off one root at the tower's base, so it can be shrunk away as a unit.
        var towerRoot = new GameObject("Tower");
        towerRoot.transform.SetParent(parent, false);
        towerRoot.transform.position = baseCentre;
        parent = towerRoot.transform;

        if (house != null)
        {
            // Same spot the tower's lowest tier would occupy: heightOffset above the base.
            GameObject h = Instantiate(house.prefab, parent);
            h.name = house.prefab.name;
            h.transform.SetPositionAndRotation(baseCentre + Vector3.up * heightOffset,
                                               facing * Quaternion.Euler(0f, 90f * Random.Range(0, 4), 0f));
            h.transform.localScale = Vector3.one * house.scale;
            if (housesClearOfFog) ChasmFog.Apply(h, noFog: true);   // a house built from pack materials would otherwise take the pack's height fog
            else if (HousesTakeFogShare) ChasmFog.Apply(h, house: true);
            ApplyWindowGlow(h);   // after the fog, so the lit twin keeps whatever share of it this scene gives its houses

            // A box collider around the whole house (a trigger, so it can never push or stop the runner),
            // and a hard check that it touches no track cube. If it does, throw it away and pick another spot.
            Bounds hb = default; bool any = false;
            foreach (Renderer r in h.GetComponentsInChildren<Renderer>())
            {
                if (!any) { hb = r.bounds; any = true; } else hb.Encapsulate(r.bounds);
            }
            if (any)
            {
                var box = towerRoot.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = hb.center - towerRoot.transform.position;
                box.size = hb.size;

                if (TouchesPath(hb))
                {
                    Destroy(towerRoot);
                    if (attempt < 4) BuildTower(zone, parent.parent != null ? parent.parent : parent, tucked, attempt + 1);
                    return;
                }
            }

            List<Vector3> columnPoints = null;
            float columnRadius = 0f;
            Vector3 columnTop = baseCentre + Vector3.up * heightOffset;
            Bounds seen = hb;   // what the camera could see of the finished house: the house and the part of its column not yet lost in the fog
            if (any && cliffPrefabs != null && cliffPrefabs.Length > 0)
            {
                Bounds column = BuildCliff(towerRoot.transform, columnTop, house.width, out columnPoints, out columnRadius);
                // No rock with a flat enough top for this house, or the column runs into the track: throw it away and pick another spot.
                if (column.size == Vector3.zero || TouchesPath(column) || NearPath(columnTop, columnRadius + house.width * 0.6f))
                {
                    Destroy(towerRoot);
                    if (attempt < 4) BuildTower(zone, parent.parent != null ? parent.parent : parent, tucked, attempt + 1);
                    return;
                }
                Vector3 lo = column.min;
                lo.y = Mathf.Max(lo.y, columnTop.y - cliffDepth * 0.5f);
                seen.Encapsulate(lo);
                seen.Encapsulate(column.max);
            }

            // Crates, toys and bins on the rock around it, so a stall reads as a pitch and not a model on a pedestal.
            if (any && house.clutter && clutterPieces.Count > 0) AddPropClutter(towerRoot.transform, house, columnTop, hb);
            if (any && house.forest && forestPieces.Count > 0) AddPropForest(towerRoot.transform, house, columnTop, hb);

            // Built and checked, but the camera could see it appear: throw it away this frame (before anything is drawn) and wait.
            if (houseNeverPopsInView && any && SeenByCamera(seen))
            {
                towerRoot.SetActive(false);
                Destroy(towerRoot);
                houseDeferred = true;
                return;
            }

            RegisterTower(towerRoot, null, true);
            // The column counts as occupied ground for the houses placed after this one.
            if (columnPoints != null)
            {
                Tower t = towers[towers.Count - 1];
                t.corners.AddRange(columnPoints);
            }
            // After RegisterTower, so the halo's quad is never taken for part of the house's footprint.
            if (any && houseLights) AddHouseLights(towerRoot.transform, hb, facing * Vector3.forward, house.lightColor, house.lightScale);
            return;
        }

        Material stone = wallMaterials[Random.Range(0, wallMaterials.Length)];
        Material neon = neonMaterials[Random.Range(0, neonMaterials.Length)];

        int tiers = Random.Range(2, 4);
        float width = windowWidth * 2.2f;
        float depth = width * 0.8f;
        float y = heightOffset;

        Vector3 windowWorldPos = Vector3.zero;
        Quaternion windowRot = facing;
        float windowTierWidth = width;

        for (int i = 0; i < tiers; i++)
        {
            float tierHeight = windowHeight * Random.Range(0.55f, 0.8f);

            GameObject tier = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tier.name = "TowerTier" + i;
            DisableCollision(tier);
            tier.transform.SetParent(parent, false);
            tier.transform.rotation = facing;
            tier.transform.position = baseCentre + Vector3.up * (y + tierHeight * 0.5f)
                                       - (facing * Vector3.forward) * (depth * 0.02f * i); // tiny setback per tier
            tier.transform.localScale = new Vector3(width, tierHeight, depth);
            tier.GetComponent<MeshRenderer>().sharedMaterial = stone;

            // Embed the window on the lowest, widest tier - most visible from the path.
            if (i == 0)
            {
                windowWorldPos = baseCentre + Vector3.up * (y + tierHeight * 0.5f) + (facing * Vector3.forward) * (depth * 0.51f);
                windowTierWidth = width;
            }

            y += tierHeight;
            width *= Random.Range(0.65f, 0.8f);
            depth *= Random.Range(0.65f, 0.8f);
        }

        GameObject window = GameObject.CreatePrimitive(PrimitiveType.Quad);
        window.name = "NeonWindow";
        DisableCollision(window);
        window.transform.SetParent(parent, false);
        window.transform.SetPositionAndRotation(windowWorldPos, facing);
        window.transform.localScale = new Vector3(Mathf.Min(windowWidth, windowTierWidth * 0.5f), windowHeight * 0.5f, 1f);
        window.GetComponent<MeshRenderer>().sharedMaterial = neon;

        GameObject lightGO = new GameObject("NeonLight");
        lightGO.transform.SetParent(parent, false);
        lightGO.transform.position = windowWorldPos + (facing * Vector3.forward) * 1.5f;
        Light lt = lightGO.AddComponent<Light>();
        lt.type = LightType.Point;
        lt.range = pointLightRange;
        lt.intensity = pointLightIntensity;
        lt.color = neon.HasProperty("_EmissionColor") ? (Color)neon.GetColor("_EmissionColor") : neon.color;
        lt.shadows = LightShadows.None;

        RegisterTower(towerRoot, lt);
    }

    private readonly List<int> cliffBag = new List<int>();

    /// <summary>Next cliff index, dealt from a shuffled bag so the same rock does not turn up twice until all have been used.</summary>
    int NextCliff()
    {
        if (cliffBag.Count == 0)
        {
            for (int i = 0; i < cliffPrefabs.Length; i++) if (cliffPrefabs[i] != null) cliffBag.Add(i);
            for (int i = cliffBag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (cliffBag[i], cliffBag[j]) = (cliffBag[j], cliffBag[i]);
            }
        }
        if (cliffBag.Count == 0) return -1;
        int last = cliffBag.Count - 1;
        int idx = cliffBag[last];
        cliffBag.RemoveAt(last);
        return idx;
    }

    /// <summary>
    /// Measures the flat top of a rock: the middle and width of the highest slice of its mesh, not its overall bounds,
    /// which are much wider than the ledge a house can actually stand on.
    /// </summary>
    static void MeasureTop(Transform root, Bounds b, out Vector2 topCenter, out float topY, out float topWidth)
    {
        topCenter = new Vector2(b.center.x, b.center.z);
        topY = b.max.y;
        topWidth = Mathf.Max(b.size.x, b.size.z) * 0.6f; // unreadable mesh: assume a ledge about that wide
        float cut = b.max.y - b.size.y * 0.06f;
        bool any = false; float x0 = 0, x1 = 0, z0 = 0, z1 = 0;

        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh m = mf.sharedMesh;
            if (m == null || !m.isReadable) continue;
            Matrix4x4 mat = mf.transform.localToWorldMatrix;
            foreach (Vector3 v in m.vertices)
            {
                Vector3 w = mat.MultiplyPoint3x4(v);
                if (w.y < cut) continue;
                if (!any) { x0 = x1 = w.x; z0 = z1 = w.z; any = true; }
                else { x0 = Mathf.Min(x0, w.x); x1 = Mathf.Max(x1, w.x); z0 = Mathf.Min(z0, w.z); z1 = Mathf.Max(z1, w.z); }
            }
        }
        if (!any) return;
        topCenter = new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
        topWidth = Mathf.Max(x1 - x0, z1 - z0);
    }

    /// <summary>Ground occupied by each background house and its cliff: x, z, radius, and the house's top height in w.</summary>
    public void GetHouseFootprints(List<Vector4> into)
    {
        into.Clear();
        foreach (Tower t in towers)
        {
            if (!t.isHouse || t.root == null || t.corners.Count == 0) continue;
            Vector3 min = t.corners[0], max = t.corners[0];
            foreach (Vector3 c in t.corners) { min = Vector3.Min(min, c); max = Vector3.Max(max, c); }
            float radius = 0.5f * new Vector2(max.x - min.x, max.z - min.z).magnitude;
            into.Add(new Vector4((min.x + max.x) * 0.5f, (min.z + max.z) * 0.5f, radius, max.y));
        }
    }

    /// <summary>
    /// Stacks cliff pieces under a house. The top rock is one whose flat top is wide enough for the house (a peaked rock is
    /// rejected), scaled so that top is a little wider than the house, with the house's base sunk slightly into it. Each rock
    /// below is a different one, overlapped so the joins sit inside the rock, until the column reaches cliffDepth. Rocks fade
    /// into the camera's background colour with depth. Returns empty bounds when no rock suits, else the column's world bounds
    /// plus sample points down it for placement checks.
    /// </summary>
    Bounds BuildCliff(Transform parent, Vector3 top, float houseWidth, out List<Vector3> samples, out float radius)
    {
        samples = new List<Vector3>();
        radius = 0f;
        Bounds all = new Bounds(top, Vector3.zero);
        float y = top.y;
        float bottom = top.y - cliffDepth;
        float footprint0 = 0f, widest = 0f;
        int prev = -1;

        // Fade to exactly the camera's background, so the foot of the column is never seen.
        Color fog = cliffFogColor;
        Camera cam = Camera.main;
        if (cam != null && cam.clearFlags == CameraClearFlags.SolidColor) fog = cam.backgroundColor;

        for (int n = 0; n < 14 && y > bottom + 0.01f; n++)
        {
            GameObject holder = null, piece = null;
            Bounds b = default; Vector2 tc = default; float topY = 0f, topW = 0f;
            bool fitted = false;

            for (int tries = 0; tries < (n == 0 ? 30 : 1) && !fitted; tries++)
            {
                int idx = NextCliff();
                if (idx < 0) break;
                if (n > 0 && idx == prev && cliffPrefabs.Length > 1) idx = NextCliff();
                if (holder != null) DestroyImmediate(holder);

                holder = new GameObject("Cliff" + n);
                holder.transform.SetParent(parent, false);
                holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                piece = Instantiate(cliffPrefabs[idx], holder.transform, false);
                if (!TryWorldBounds(piece.transform, out b) || b.size.y < 1e-3f) continue;

                prev = idx;
                if (n == 0)
                {
                    MeasureTop(piece.transform, b, out tc, out topY, out topW);
                    // A rock whose ledge is much narrower than its body would leave a house overhanging, or need a huge rock to carry it.
                    fitted = topW > 0.5f && Mathf.Max(b.size.x, b.size.z) / topW <= 1.45f;
                }
                else
                {
                    tc = new Vector2(b.center.x, b.center.z);
                    topY = b.max.y;
                    fitted = true;
                }
            }
            if (!fitted) { if (holder != null) DestroyImmediate(holder); if (n == 0) return default; break; }

            foreach (Collider c in holder.GetComponentsInChildren<Collider>(true)) c.enabled = false;

            var block = new MaterialPropertyBlock();
            foreach (Renderer r in piece.GetComponentsInChildren<Renderer>())
            {
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", cliffTint);
                block.SetColor("_Color", cliffTint);
                block.SetColor("_HeightFogColor", fog);
                // Clear rock just under the house, fully the background colour well before the foot - or, kept clear of fog, never.
                // Taking a share of the chasm fog, the End is instead the house's height: the share grows with depth under it.
                block.SetFloat("_HeightFogEnd", housesClearOfFog ? -1e6f : HousesTakeFogShare ? top.y : top.y - 6f);
                block.SetFloat("_HeightFogStart", housesClearOfFog ? -1e6f - 1f : HousesTakeFogShare ? top.y - 1f : top.y - cliffDepth * 0.45f);
                r.SetPropertyBlock(block);
            }
            // With a ChasmFog in the scene: the same fade as above, into the chasm's background colour - or a share of the chasm
            // fog, or no fog at all.
            ChasmFog.Apply(piece, keepOwnFade: true, noFog: housesClearOfFog, house: HousesTakeFogShare);

            // Pivot the piece so the holder's origin is the middle of its top, sunk a little so the seam is buried.
            float sink = b.size.y * (n == 0 ? 0.03f : 0.04f);
            piece.transform.position -= new Vector3(tc.x, topY - sink, tc.y);

            float body = Mathf.Max(b.size.x, b.size.z);
            float s;
            if (n == 0)
            {
                s = houseWidth * activeCliffWidth / topW;   // the ledge, not the whole rock, is what has to carry the house
                footprint0 = body * s;
            }
            else s = footprint0 * Random.Range(0.75f, 0.95f) / body;   // the column never gets wider than the rock the house stands on
            float sy = s;

            float height = (b.size.y - sink) * sy;
            float remaining = y - bottom;
            if (height < remaining * 0.999f && n >= 12) sy = Mathf.Min(sy * 4f, remaining / (b.size.y - sink)); // out of pieces: stretch the last one

            holder.transform.SetPositionAndRotation(new Vector3(top.x, y, top.z), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            holder.transform.localScale = new Vector3(s, sy, s);

            height = (b.size.y - sink) * sy;
            float footprint = body * s;
            widest = Mathf.Max(widest, footprint);
            all.Encapsulate(new Bounds(new Vector3(top.x, y - height * 0.5f, top.z), new Vector3(footprint, height, footprint)));

            y -= height * 0.8f;   // 20% overlap into the rock above
        }

        radius = widest * 0.5f;
        float half = radius * 1.05f;
        for (float sy2 = top.y; sy2 > top.y - Mathf.Min(cliffDepth, 45f); sy2 -= 4f)
            foreach (float dx in new[] { -half, 0f, half })
                samples.Add(new Vector3(top.x + dx, sy2, top.z));
        return all;
    }

    /// <summary>Records the tower's full-size bounds, then starts it hidden so it grows in only once clear of the route.</summary>
    void RegisterTower(GameObject towerRoot, Light lt, bool house = false)
    {
        var record = new Tower { root = towerRoot.transform, light = lt, baseIntensity = lt != null ? lt.intensity : 0f, basePos = towerRoot.transform.position, isHouse = house };
        // Tight footprint: the corners of each mesh's own (rotated) box, not the inflated
        // world-axis box, so a turned house does not claim empty space around it.
        foreach (Renderer r in towerRoot.GetComponentsInChildren<Renderer>())
        {
            Mesh mesh = null;
            if (r is MeshRenderer) { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
            else if (r is SkinnedMeshRenderer sk) mesh = sk.sharedMesh;

            Bounds mb = mesh != null ? mesh.bounds : default;
            Matrix4x4 m = mesh != null ? r.transform.localToWorldMatrix : Matrix4x4.identity;
            if (mesh == null) mb = r.bounds; // world-space fallback
            for (int c = 0; c < 8; c++)
                record.corners.Add(m.MultiplyPoint3x4(mb.center + new Vector3(
                    (c & 1) == 0 ? -mb.extents.x : mb.extents.x,
                    (c & 2) == 0 ? -mb.extents.y : mb.extents.y,
                    (c & 4) == 0 ? -mb.extents.z : mb.extents.z)));
        }

        // Start shrunk and grow in only once the view is clear of it, rather than popping in
        // over the track for a frame.
        record.visibility = house ? 1f : 0f;
        towerRoot.SetActive(house);
        towers.Add(record);
    }

    // ------------------------------------------------------------------
    // House lights
    //
    // A background house is a lamp in the dark: light inside it that washes over its own walls and
    // the rock it stands on, the window panes painted into its texture lit from behind, and a soft
    // halo so it reads as giving off light with no post-processing in the scene. Two point lights
    // and one quad per house, no shadows, and the window glow is a few instructions in the house
    // shader - nothing here scales with how much of the house is on screen.
    // ------------------------------------------------------------------

    static readonly int WindowGlowId = Shader.PropertyToID("_HouseWindowGlow");
    static readonly int WindowGlowShapeId = Shader.PropertyToID("_HouseWindowGlowShape");
    static readonly int HaloColorId = Shader.PropertyToID("_Color");
    const string WindowGlowKeyword = "_WINDOW_GLOW";
    const string UrpLitName = "Universal Render Pipeline/Lit";

    private readonly Dictionary<Material, Material> glowMaterials = new Dictionary<Material, Material>();
    private readonly List<Transform> halos = new List<Transform>();
    private Material haloMaterial;
    private Mesh haloMesh;
    private Color lightTint = Color.white;   // the colour and boost of the prop whose lamps are being built
    private float lightBoost = 1f;
    private Color appliedHaloColor = Color.clear;

    /// <summary>The window settings are global, not per material, so every house in the scene stays in one SRP batch.</summary>
    void PushWindowGlowGlobals()
    {
        Color c = windowGlowColor.linear;
        float strength = houseLights && houseWindowGlow ? windowGlowStrength : 0f;
        Shader.SetGlobalVector(WindowGlowId, new Vector4(c.r, c.g, c.b, strength));
        Shader.SetGlobalVector(WindowGlowShapeId,
            new Vector4(windowGlowBrightness, windowGlowSoftness, windowGlowWarmth, windowGlowSoftness * 0.5f));
    }

    /// <summary>Swaps a house's materials for twins that light the window panes painted into their albedo.</summary>
    void ApplyWindowGlow(GameObject house)
    {
        if (!houseLights || !houseWindowGlow || house == null) return;
        if (houseGlowShader == null) houseGlowShader = Shader.Find("Custom/ChasmFogHouseLit");
        if (houseGlowShader == null) return;

        foreach (Renderer r in house.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null || m.shader == null) continue;
                if (m.IsKeywordEnabled(WindowGlowKeyword)) continue;               // already a lit twin
                if (m.shader.name != UrpLitName && m.shader != houseGlowShader) continue;

                if (!glowMaterials.TryGetValue(m, out Material g) || g == null)
                {
                    bool plain = m.shader.name == UrpLitName;   // this scene leaves its houses out of the chasm fog
                    g = new Material(m) { name = m.name + " (Lit Windows)" };   // the copy keeps the fog keyword this scene put on it
                    g.shader = houseGlowShader;
                    g.EnableKeyword(WindowGlowKeyword);
                    // Lighting the windows must not quietly put the house into the fog as well: on the house shader,
                    // no keyword means the house's share of the chasm fog.
                    if (plain) g.EnableKeyword("_CHASM_FOG_NONE");
                    glowMaterials[m] = g;
                }
                mats[i] = g;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    /// <summary>Lamp inside the house, lamp at its foot for the rock, an optional one out in front, and the halo around them.</summary>
    void AddHouseLights(Transform parent, Bounds hb, Vector3 front, Color tint, float boost)
    {
        float width = Mathf.Max(0.01f, Mathf.Max(hb.size.x, hb.size.z));
        Vector3 inside = new Vector3(hb.center.x, hb.min.y + hb.size.y * 0.45f, hb.center.z);
        // A prop can ask for its own colour (a rock that glows green, say); alpha 0 means it takes the shared one.
        Color colour = tint.a > 0.001f ? tint : houseLightColor;
        lightTint = colour;
        lightBoost = Mathf.Max(0.01f, boost);

        if (houseFrontLightIntensity > 0.01f)
            NewHouseLight(parent, "HouseFrontLight",
                          inside + front * (width * houseFrontLightOffset) + Vector3.up * (hb.size.y * 0.15f),
                          width * houseFrontLightRange, houseFrontLightIntensity);

        if (houseLightIntensity > 0.01f)
            NewHouseLight(parent, "HouseLight", inside, width * houseLightRange, houseLightIntensity);

        if (houseGroundLightIntensity > 0.01f)
            // Under the house, not inside it: the house itself is a wide lid, and a lamp above it lights nothing below.
            NewHouseLight(parent, "HouseGroundLight",
                          new Vector3(hb.center.x, hb.min.y - width * 0.3f, hb.center.z),
                          width * houseGroundLightRange, houseGroundLightIntensity);

        if (!houseHalo || houseHaloStrength <= 0.001f || !HaloReady()) return;

        var go = new GameObject("HouseHalo");
        go.transform.SetParent(parent, false);
        go.transform.position = inside;
        go.transform.localScale = Vector3.one * (width * houseHaloSize);
        go.AddComponent<MeshFilter>().sharedMesh = haloMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = haloMaterial;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        halos.Add(go.transform);
    }

    void NewHouseLight(Transform parent, string name, Vector3 position, float range, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        Light lt = go.AddComponent<Light>();
        lt.type = LightType.Point;
        lt.color = lightTint;
        lt.range = range;
        lt.intensity = intensity * lightBoost;
        lt.shadows = LightShadows.None;   // shadows from a light this close to the rock cost more than they show
        lt.renderMode = LightRenderMode.ForcePixel;
    }

    bool HaloReady()
    {
        if (haloMesh != null && haloMaterial != null) return true;

        Shader glow = Shader.Find("Custom/SoftGlow");
        if (glow == null) return false;

        if (haloMesh == null)
        {
            haloMesh = new Mesh { name = "HouseHaloQuad" };
            haloMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f), new Vector3( 0.5f, -0.5f, 0f),
            };
            haloMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            haloMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            haloMesh.RecalculateBounds();
        }
        if (haloMaterial == null)
        {
            haloMaterial = new Material(glow) { name = "HouseHalo" };
            appliedHaloColor = Color.clear;
        }
        return true;
    }

    /// <summary>Turns the halos to face the camera and keeps their colour in step with the sliders.</summary>
    void UpdateHalos(Camera cam)
    {
        if (halos.Count == 0) return;

        if (haloMaterial != null)
        {
            Color want = houseLightColor;
            want.a = houseHalo ? houseHaloStrength : 0f;
            if (appliedHaloColor != want)
            {
                appliedHaloColor = want;
                haloMaterial.SetColor(HaloColorId, want);
            }
        }

        Quaternion faceCamera = cam != null ? cam.transform.rotation : Quaternion.identity;
        for (int i = halos.Count - 1; i >= 0; i--)
        {
            Transform t = halos[i];
            if (t == null) { halos.RemoveAt(i); continue; }
            t.rotation = faceCamera;
        }
    }

    /// <summary>
    /// Finds a tower spot in the emptiest part of the shot: it tries a batch of random spots
    /// inside the frame the nearby track occupies, all set BEHIND that track as the camera sees
    /// it, and keeps the one whose on-screen footprint is furthest from every cube. The tower
    /// therefore shows up in the picture, in a gap in the track, without ever standing between the
    /// viewer and the route.
    /// </summary>
    Vector3 PlaceInEmptySpace(DynamicPathGenerator.PathZone zone, float tilt, float yaw, float towerWidth, float towerHeight, bool tucked, out Vector3 away)
    {
        Quaternion camRot = Quaternion.Euler(tilt, yaw, 0f);
        Quaternion inv = Quaternion.Inverse(camRot);
        away = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward; // horizontal view direction: from the track out to the tower

        // Nearby track in camera space: this zone and the ones either side of it.
        var pts = new List<Vector3>();
        var zones = pathGen.Zones;
        int idx = pathGen.IndexOfZone(zone);
        for (int z = Mathf.Max(0, idx - 1); z <= Mathf.Min(zones.Count - 1, idx + 2); z++)
            foreach (var seg in zones[z].segments)
                foreach (var cube in seg.cubes)
                    if (cube != null) pts.Add(inv * cube.transform.position);

        if (pts.Count == 0) pts.Add(inv * zone.zoneBounds.center);

        // Track the frame spans BEFORE adding other houses, so their spots never stretch the search area.
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue, maxZ = float.MinValue;
        foreach (Vector3 p in pts)
        {
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            maxZ = Mathf.Max(maxZ, p.z);
        }
        Vector2 centre = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

        int pathCount = pts.Count;

        // Houses already standing count as occupied too, so they do not pile up in one pocket.
        foreach (Tower t in towers)
            if (!t.hidden) // a house that is shrunk away is not occupying its spot
                foreach (Vector3 c in t.corners) pts.Add(inv * c);

        // A tucked house is judged on its real footprint so it can drop into small pockets; a regular one keeps a generous box.
        float halfW = towerWidth * (tucked ? 0.55f : 0.75f);
        float yPad = towerWidth * (tucked ? 0.1f : 0.2f);

        Vector3 best = camRot * new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, maxZ + 12f);
        float bestScore = float.MinValue, fallbackScore = float.MinValue;
        Vector3 fallbackBest = best;
        bool found = false;
        float reach = towerWidth * 1.8f;

        float columnScreen = (cliffPrefabs != null && cliffPrefabs.Length > 0 ? Mathf.Min(cliffDepth, 45f) : 0f) * Mathf.Cos(tilt * Mathf.Deg2Rad);
        float wantClear = towerWidth * (tucked ? tuckedClearance : 0.15f);   // gap that is "clear enough"; more than this earns nothing
        for (int attempt = 0; attempt < (tucked ? 700 : 300); attempt++)
        {
            // Keep the whole house inside the frame the track spans, so it is not clipped at the screen edge.
            // Regular houses stay inside the frame; a tucked one may reach just past the track's edge, into the gaps outside the outer legs.
            float inX = tucked ? (cliffPrefabs != null && cliffPrefabs.Length > 0 ? halfW * 0.25f : -halfW * 0.6f) : Mathf.Min(halfW, (maxX - minX) * 0.4f);
            float inY = tucked ? -towerHeight * 0.15f : Mathf.Min(towerHeight * 0.5f, (maxY - minY) * 0.4f);
            Vector3 camPos = new Vector3(Random.Range(minX + inX, maxX - inX), Random.Range(minY + inY, maxY - inY), maxZ + (cliffPrefabs != null && cliffPrefabs.Length > 0 ? Random.Range(activeDepthBehind.x, activeDepthBehind.y) : Random.Range(6f, 24f)));
            Vector3 world = camRot * camPos;

            // The tower floats heightOffset above its base and rises towerHeight from there.
            Vector3 lo = inv * (world + Vector3.up * heightOffset);
            Vector3 hi = inv * (world + Vector3.up * (heightOffset + towerHeight));
            float xMin = camPos.x - halfW, xMax = camPos.x + halfW;
            float yMin = Mathf.Min(lo.y, hi.y) - yPad, yMax = Mathf.Max(lo.y, hi.y) + 0.5f;
            float yMinHouse = yMin - columnScreen;

            // Distance from the tower's footprint to the nearest cube, on screen.
            float nearest = float.MaxValue, nearestHouse = float.MaxValue;
            for (int k = 0; k < pts.Count; k++)
            {
                Vector3 p = pts[k];
                float dx = Mathf.Max(xMin - p.x, 0f, p.x - xMax);
                // Other houses are judged against the whole column this one will stand on, not just the house.
                float dy = Mathf.Max((k < pathCount ? yMin : yMinHouse) - p.y, 0f, p.y - yMax);
                float d2 = dx * dx + dy * dy;
                if (k < pathCount) nearest = Mathf.Min(nearest, d2);
                else nearestHouse = Mathf.Min(nearestHouse, d2);
            }

            float clear = Mathf.Sqrt(nearest);
            float apart = nearestHouse == float.MaxValue ? towerWidth : Mathf.Sqrt(nearestHouse);

            // How enclosed the spot is by track: how many of 8 compass sectors around it hold path
            // within reach. A gap between two legs or in an inner corner scores high; beside one
            // straight leg, or in open space, scores low.
            int mask = 0, near = 0;
            float cy = (yMin + yMax) * 0.5f, reach2 = reach * reach;
            for (int k = 0; k < pathCount; k++)
            {
                float dx = pts[k].x - camPos.x, dy = pts[k].y - cy;
                if (dx * dx + dy * dy > reach2) continue;
                near++;
                mask |= 1 << (Mathf.FloorToInt((Mathf.Atan2(dy, dx) + Mathf.PI) / (Mathf.PI * 0.25f)) & 7);
            }
            int sectors = 0;
            for (int m = mask; m != 0; m &= m - 1) sectors++;

            bool fits = clear >= wantClear && apart >= towerWidth * 0.6f;
            // With a cliff under it the house must stand out at the sides, like the reference, never behind the middle of the route.
            if (columnScreen > 0f && Mathf.Abs(camPos.x - centre.x) - halfW * 1.3f < (maxX - minX) * houseSideKeepOut) fits = false;
            if (fits)
            {
                float score = sectors + Mathf.Min(near, 60) * 0.01f + (tucked ? 0.002f * (centre.y - camPos.y) : 0f);
                if (score > bestScore) { bestScore = score; best = world; found = true; }
            }
            else if (!found)
            {
                // Nothing fits yet: keep the least-bad spot so there is always an answer.
                float fallback = Mathf.Min(clear, wantClear) + Mathf.Min(apart, towerWidth * 0.6f);
                if (fallback > fallbackScore) { fallbackScore = fallback; fallbackBest = world; }
            }
        }

        lastPlaceScore = found ? bestScore : -1f;
        if (!found) best = fallbackBest;
        return best;
    }

    /// <summary>A handful of small drifting glow particles near the path. Visual only - no collision module, so they can never block or slow the runner.</summary>
    void BuildFireflies(DynamicPathGenerator.PathZone zone, Transform parent)
    {
        GameObject go = new GameObject("Fireflies");
        go.transform.SetParent(parent, false);
        go.transform.position = zone.zoneBounds.center;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(Mathf.Min(fireflySize.x, fireflySize.y), Mathf.Max(fireflySize.x, fireflySize.y));
        main.startColor = FireflyColor();
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = fireflyCount * 2;

        var emission = ps.emission;
        emission.rateOverTime = fireflyCount * 0.15f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = fireflyRadius;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.15f;
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.2f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = fireflyMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var collision = ps.collision;
        collision.enabled = false; // never interact with the player or the path
    }

    /// <summary>The colour one drifting light starts with: the old warm range, or one of the colours listed for the scene.</summary>
    ParticleSystem.MinMaxGradient FireflyColor()
    {
        if (fireflyColors == null || fireflyColors.Length < 2)
            return new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(0.7f, 1f, 0.6f));

        int n = Mathf.Min(fireflyColors.Length, 8);   // a Gradient holds eight colour keys
        var keys = new GradientColorKey[n];
        for (int i = 0; i < n; i++) keys[i] = new GradientColorKey(fireflyColors[i], n == 1 ? 0f : i / (float)(n - 1));

        var grad = new Gradient();
        grad.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return new ParticleSystem.MinMaxGradient(grad) { mode = ParticleSystemGradientMode.RandomColor };
    }

    static void DisableCollision(GameObject go)
    {
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
    }
}
