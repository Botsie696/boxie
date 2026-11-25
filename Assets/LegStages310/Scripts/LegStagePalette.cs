using UnityEngine;

/// <summary>
/// WITHIN-LEVEL LEG DESIGN (scene 3.10, skill within_level_legs_Design): the colours each level's legs move through as the
/// level goes on. A level (checkpoint) is split in thirds by score: stage 0 = the level's own look (never changed),
/// stage 1 from ~33 %, stage 2 from ~66 %. Only the legs change - background, rocks, lighting and the runner stay.
///
/// A stage re-colours the legs in one of two ways (both may be used):
///   face  - flat looks (Start, Crimson, Toon): the look's top colour becomes `face`; the sides keep their luminance share
///           of the top, so the shading reads exactly the same in the new hue.
///   pick  - textured looks (Pixel moss, Storybook roofs): every leg pixel near `pickFrom`'s hue is moved to `pickTo`'s hue,
///           saturation and value (relative), leaving the rest of the texture alone.
/// Each stage also names the obstacle colour (pillars and triangles of the obstacle legs) - never yellow.
/// The obstacle legs (portalRole 3) get the level's own runway design instead of a stage colour.
/// </summary>
[CreateAssetMenu(menuName = "Runner/Leg Stage Palette", fileName = "LegStagePalette")]
public class LegStagePalette : ScriptableObject
{
    [System.Serializable]
    public class Stage
    {
        [Tooltip("Face re-colour target (the top face colour). Alpha 0 = keep the look's own faces.")]
        public Color face = Color.clear;
        [Tooltip("Hue pick: leg pixels near this colour's hue ...")]
        public Color pickFrom = Color.clear;
        [Tooltip("... move to this colour (hue, and saturation / value relative to pickFrom). Alpha 0 on either = off.")]
        public Color pickTo = Color.clear;
        [Range(0.02f, 0.25f)] public float pickWidth = 0.08f;
        [Tooltip("Diagonal stripes (obstacle runways), 0 = none.")]
        [Range(0f, 0.6f)] public float stripes;
        [Tooltip("Colour of the obstacle pillars / triangles while this stage is laid.")]
        public Color obstacle = new Color(0.9f, 0.28f, 0.3f, 1f);
    }

    [System.Serializable]
    public class Level
    {
        public string name;
        [Tooltip("3 stages: 0 = the look as authored (keep face/pick off), 1 = from ~33 %, 2 = from ~66 %.")]
        public Stage[] stages = new Stage[3];
        [Tooltip("The obstacle legs of this level (face / pick / stripes; obstacle colour unused - the stage's is used).")]
        public Stage obstacleRunway = new Stage();
    }

    [Tooltip("Index = level = portals crossed (0 = the start, 1 = after portal 1 ...). Levels past the end are left alone.")]
    public Level[] levels;
    [Tooltip("Stage boundaries as a share of the level's legs.")]
    public Vector2 stageAt = new Vector2(1f / 3f, 2f / 3f);
    [Tooltip("Strength of the pale line round the first cube of a new stage (the threshold the runner crosses). 0 = none.")]
    [Range(0f, 1f)] public float thresholdLine = 0.7f;

    static Color H(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color c); return c; }
    static Stage Face(string face, string obstacle) => new Stage { face = H(face), obstacle = H(obstacle) };
    static Stage Keep(string obstacle) => new Stage { obstacle = H(obstacle) };
    static Stage Pick(string from, string to, float width, string obstacle) =>
        new Stage { pickFrom = H(from), pickTo = H(to), pickWidth = width, obstacle = H(obstacle) };

    /// <summary>Obstacle-leg TREES (replace the totem pillars): crown shape (ObstacleTreeMesh: 0 stepped blocks, 1 ball,
    /// 2 cone, 3 diamond), trunk colour and the three bright leaf colours (one per stage, never yellow). The dressing
    /// follows each level's theme; the look's own shader gives the material (toon ink, pixel snap, watercolour ...).
    /// Pixel colours are all in the pixel palette.</summary>
    public static void TreeStyle(int level, int stage, out int shape, out Color trunk, out Color leaves)
    {
        string[] trunks = { "7B5436", "7A4A52", "3A2E3A", "DD8C64", "6B4A33", "8A5A44", "8B5A2B", "3A3A3A", "4A3A78", "6B3A22" };
        int[] shapes = { 0, 3, 1, 0, 2, 1, 0, 2, 3, 1 };
        string[][] lv =
        {
            new[] { "8CE04A", "5FE08C", "3FE0B0" },   // Start: lime -> mint -> aqua over grey stone / sandstone
            new[] { "2EE6C8", "7CF04A", "5FD8FF" },   // Crimson: teal crystal -> lime -> sky on purple / pink / amber
            new[] { "FF8F2E", "FF5C7A", "C46BFF" },   // Toon: warm orange -> coral -> violet against the cool sky
            new[] { "7DF0F9", "E06981", "3FD08A" },   // Pixel: palette cyan -> pink -> jade
            new[] { "3FD16A", "A8E04A", "FF9EC8" },   // Storybook: emerald -> lime -> blossom pink
            new[] { "FF5FA8", "FF9A3C", "FFC2E0" },   // Flower: magenta -> orange -> blush
            new[] { "8CFF4A", "4AE0FF", "FF7AD0" },   // Voxel: lime -> sky -> pink
            new[] { "6A6A6A", "4A4A4A", "2E2E2E" },   // Pencil: graphite greys (the page is black and white)
            new[] { "FF8A3C", "5FE8FF", "FF5FB0" },   // Space: engine orange -> console cyan -> nebula magenta on blue / indigo hulls
            new[] { "2FBF71", "FF6FB0", "3FD8E0" },   // Spice Route: mango green -> bougainvillea pink -> turquoise
        };
        level = Mathf.Clamp(level, 0, lv.Length - 1);
        stage = Mathf.Clamp(stage, 0, 2);
        shape = shapes[level];
        trunk = H(trunks[level]);
        leaves = H(lv[level][stage]);
    }

    /// <summary>The tuned colours (also what Tools > Leg Stages > Rebuild Palette writes). Chosen per theme against each
    /// level's sky/chasm so legs never read as background, and away from the runner's yellow.</summary>
    public void FillDefaults()
    {
        levels = new[]
        {
            // 0 Start: grey stone on a dark teal chasm -> warm sandstone -> terracotta (warming toward the crimson level).
            new Level { name = "Start", stages = new[] { Keep("E5484D"), Face("C9AE80", "8C5BE0"), Face("C98060", "E04F9A") },
                        obstacleRunway = new Stage { face = H("E2DED3"), stripes = 0.24f } },
            // 1 Crimson: red on purple -> dusty pink -> ochre gold (user's reference images). Obstacles go teal / sea / sky.
            new Level { name = "Crimson", stages = new[] { Keep("25D3C2"), Face("E9B3BE", "18A88F"), Face("C2853A", "3AA8F0") },
                        obstacleRunway = new Stage { face = H("E8E2EC"), stripes = 0.24f } },
            // 2 Toon ruins: slate on a bright teal/mint sky -> ochre sandstone -> rust brick (warm against the cool sky).
            new Level { name = "Toon", stages = new[] { Keep("E5503C"), Face("B08C5E", "7B3FA8"), Face("A25C4A", "3448C0") },
                        obstacleRunway = new Stage { face = H("E9E1CF"), stripes = 0.26f } },
            // 3 Pixel grotto: jade moss on indigo -> peach lichen -> pale bone (all in the pixel palette, so the snap keeps them).
            new Level { name = "Pixel", stages = new[] { Keep("E0304E"), Pick("3FD08A", "F4A07A", 0.09f, "7DF0F9"), Pick("3FD08A", "E4E1E5", 0.09f, "DD8C64") },
                        obstacleRunway = new Stage { pickFrom = H("3FD08A"), pickTo = H("E06981"), pickWidth = 0.09f, stripes = 0.22f } },
            // 4 Storybook: orange roofs on a teal wash -> brick red roofs -> plum roofs.
            new Level { name = "Storybook", stages = new[] { Keep("7B3FC4"), Pick("DB632C", "B83A2A", 0.05f, "1F7A5A"), Pick("DB632C", "7A3E8C", 0.05f, "E07A1F") },
                        obstacleRunway = new Stage { pickFrom = H("DB632C"), pickTo = H("3A3A44"), pickWidth = 0.05f, stripes = 0.2f } },
            // 5 Flower (Blossom Vale): spring-green meadow + moss on a cyan sky / pale mist -> deep teal -> twilight violet
            // (cooler and deeper each step, never red: red reads as danger). The flowers keep their colours. Obstacles blue / orange / coral.
            new Level { name = "Flower", stages = new[] { Keep("3448D8"), Pick("4FA840", "1E6E5A", 0.1f, "F0702A"), Pick("4FA840", "5B3A9E", 0.1f, "F05A8C") },
                        obstacleRunway = new Stage { pickFrom = H("4FA840"), pickTo = H("56607A"), pickWidth = 0.1f, stripes = 0.22f } },
            // 6 Voxel (Brick Meadow): every leg already has its own slab / brick colours (VoxelLeg.hlsl), so the stages keep
            // the legs and only move the obstacles: red -> purple -> deep green. The obstacle runways get dark stripes.
            new Level { name = "Voxel", stages = new[] { Keep("E8361E"), Keep("7B3FC4"), Keep("1F7A5A") },
                        obstacleRunway = new Stage { stripes = 0.24f } },
            // 7 Pencil (Graphite Sketchbook): black and white; the four sketchbook pages are drawn by the leg shader itself
            // (PencilLeg.hlsl), so the stages keep the legs. Obstacles are near-solid graphite whatever is written here.
            new Level { name = "Pencil", stages = new[] { Keep("2A2A2A"), Keep("2A2A2A"), Keep("2A2A2A") },
                        obstacleRunway = new Stage { stripes = 0.18f } },
            // 8 Space (Rogue Planet): the oil-painted spaceship legs (SpaceLeg.hlsl: deck / portholes / glass corridors /
            // engines / whole ships) carry their own colours, so the stages keep the legs and only move the obstacles:
            // hot red -> magenta -> blaze orange (never yellow, never the periwinkle deck). Runways get dark stripes.
            new Level { name = "Space", stages = new[] { Keep("E5304A"), Keep("D03CB0"), Keep("F0602A") },
                        obstacleRunway = new Stage { stripes = 0.2f } },
            // 9 Spice Route (India, then Arabia): every leg is its own painted craft (SpiceLeg.hlsl: temple stone, rangoli, mirror
            // work, Madhubani, marble, zari / girih, Iznik, mashrabiya, carpet, zellige, ebru), so the stages keep the legs and
            // only move the obstacles: crimson -> royal purple -> vermilion (never yellow, never the saffron / gold of the legs).
            new Level { name = "SpiceRoute", stages = new[] { Keep("D9142E"), Keep("7E1FA8"), Keep("E0441A") },
                        obstacleRunway = new Stage { stripes = 0.2f } },
        };
    }
}
