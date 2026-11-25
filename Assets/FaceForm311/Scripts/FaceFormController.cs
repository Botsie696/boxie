using UnityEngine;

/// <summary>
/// MainScene prop camera 3.11 only (on the "FaceForm" object): a gentle read of the leg's planes for checkpoints 3-10,
/// on top of everything the theme already draws. It drives the form-shading globals of PixelCheckpoint/FlatFaceBlockPixel
/// (_FDForm / _FDEdge / _FDLip / _FDLip2; all zero = the shader is exactly what 3.10 has):
///   plane shading   top / lit side / shade side / foot step down a little in brightness (hue kept)
///   edge lip        a thin lighter (graphite on paper in Sketchbook) line along the long top edges of a leg
///   crease          a soft shade just under that lip on the side faces, so the edge has thickness
///   foot            the side face darkens a little towards the leg's underside, so the leg sits in space
/// Checkpoints 1-2 keep the DepthCueController ladder and lip (the look the user liked) and get nothing extra.
/// Purely visual: it reads the generator's portal count and never touches gameplay, the camera or any asset.
/// </summary>
[DefaultExecutionOrder(960)]   // after DepthCueController
public class FaceFormController : MonoBehaviour
{
    [System.Serializable]
    public class FormLook
    {
        public string name = "Checkpoint";
        [Range(0f, 1f)] public float strength = 0f;
        [Header("Plane shading (multipliers, 1 = as the theme has it)")]
        [Range(0.5f, 1f)] public float shadeSide = 0.9f;
        [Range(0.5f, 1.1f)] public float litSide = 1f;
        [Range(0.5f, 1f)] public float bottom = 0.85f;
        [Range(0f, 0.2f)] public float topLift = 0f;
        [Header("Edge lip (top faces)")]
        public Color lipColor = new Color(0.8f, 0.85f, 0.9f);
        [Range(0f, 1f)] public float lip = 0.35f;
        [Range(0.01f, 0.12f)] public float lipWidth = 0.045f;
        [Header("Crease under the top edge / darker foot (side faces)")]
        [Range(0f, 0.4f)] public float crease = 0.14f;
        [Range(0.02f, 0.3f)] public float creaseWidth = 0.12f;
        [Range(0f, 0.4f)] public float foot = 0.12f;
        [Range(0.02f, 0.4f)] public float footWidth = 0.14f;

        public FormLook Copy() => (FormLook)MemberwiseClone();

        public static void Lerp(FormLook a, FormLook b, float t, FormLook into)
        {
            into.strength = Mathf.Lerp(a.strength, b.strength, t);
            into.shadeSide = Mathf.Lerp(a.shadeSide, b.shadeSide, t);
            into.litSide = Mathf.Lerp(a.litSide, b.litSide, t);
            into.bottom = Mathf.Lerp(a.bottom, b.bottom, t);
            into.topLift = Mathf.Lerp(a.topLift, b.topLift, t);
            into.lipColor = Color.Lerp(a.lipColor, b.lipColor, t);
            into.lip = Mathf.Lerp(a.lip, b.lip, t);
            into.lipWidth = Mathf.Lerp(a.lipWidth, b.lipWidth, t);
            into.crease = Mathf.Lerp(a.crease, b.crease, t);
            into.creaseWidth = Mathf.Lerp(a.creaseWidth, b.creaseWidth, t);
            into.foot = Mathf.Lerp(a.foot, b.foot, t);
            into.footWidth = Mathf.Lerp(a.footWidth, b.footWidth, t);
        }
    }

    public DynamicPathGenerator pathGen;
    [Tooltip("Checkpoint 1, 2, 3 ... Checkpoints past the end of the list get nothing.")]
    public FormLook[] looks = new FormLook[0];
    [Tooltip("Seconds the shading takes to blend into the next checkpoint's after its portal.")]
    [Range(0.1f, 4f)] public float blendSeconds = 1.6f;

    static readonly int FormId = Shader.PropertyToID("_FDForm");
    static readonly int EdgeId = Shader.PropertyToID("_FDEdge");
    static readonly int LipId = Shader.PropertyToID("_FDLip");
    static readonly int Lip2Id = Shader.PropertyToID("_FDLip2");

    static readonly FormLook Off = new FormLook { strength = 0f };

    readonly FormLook current = new FormLook();
    FormLook from, to;
    float blendT = 1f;
    int checkpoint = -1;

    static FormLook Make(string name, float strength, float shade, float lit, float bottom, float lift,
                         string lipHex, float lip, float lipW, float crease, float creaseW, float foot, float footW)
    {
        Color c = Color.white;
        ColorUtility.TryParseHtmlString(lipHex, out c);
        return new FormLook
        {
            name = name, strength = strength, shadeSide = shade, litSide = lit, bottom = bottom, topLift = lift,
            lipColor = c, lip = lip, lipWidth = lipW, crease = crease, creaseWidth = creaseW, foot = foot, footWidth = footW
        };
    }

    /// <summary>The authored starting values (checkpoint 1, 2 ... 10); also what Reset puts in the inspector.</summary>
    public static FormLook[] DefaultLooks() => new[]
    {
        Make("1 Start (DepthCue ladder as 3.10)",   0f,   1f,    1f,    1f,    0f, "#ffffff", 0f,    0.045f, 0f,    0.12f, 0f,    0.14f),
        Make("2 Crimson (DepthCue ladder as 3.10)", 0f,   1f,    1f,    1f,    0f, "#ffffff", 0f,    0.045f, 0f,    0.12f, 0f,    0.14f),
        Make("3 Toon (Overgrown Ruins)",            0.8f, 0.82f, 0.97f, 0.70f, 0f, "#b5cdd8", 0.30f, 0.07f,  0.24f, 0.20f, 0.20f, 0.24f),
        Make("4 Pixel (Jellyfish Grotto)",          0.9f, 0.80f, 0.95f, 0.68f, 0f, "#9be8bd", 0.55f, 0.10f,  0.28f, 0.22f, 0.24f, 0.24f),
        Make("5 Storybook (Storybook Keep)",        0.9f, 0.80f, 0.97f, 0.70f, 0f, "#f0a070", 0.40f, 0.08f,  0.26f, 0.22f, 0.20f, 0.24f),
        Make("6 Flower (Blossom Vale)",             0.9f, 0.80f, 0.96f, 0.70f, 0f, "#b8e87e", 0.50f, 0.08f,  0.26f, 0.22f, 0.22f, 0.24f),
        Make("7 Voxel (Brick Meadow)",              0.9f, 0.80f, 0.96f, 0.68f, 0f, "#fbe48a", 0.55f, 0.10f,  0.28f, 0.22f, 0.24f, 0.24f),
        Make("8 Pencil (Sketchbook)",               0.9f, 0.84f, 1f,    0.74f, 0f, "#4a4845", 0.45f, 0.06f,  0.22f, 0.20f, 0.18f, 0.22f),
        Make("9 Space (Rogue Planet)",              0.9f, 0.82f, 1f,    0.72f, 0f, "#cfd6ff", 0.50f, 0.08f,  0.24f, 0.22f, 0.22f, 0.24f),
        Make("10 Spice Route (India / Arabia)",     0.9f, 0.80f, 0.97f, 0.70f, 0f, "#fff6e0", 0.45f, 0.08f,  0.26f, 0.22f, 0.22f, 0.24f),
    };

    void Reset() { looks = DefaultLooks(); }

    void Start()
    {
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (looks == null || looks.Length == 0) looks = DefaultLooks();
        Snap(CurrentCheckpoint());
        Push();
    }

    void OnDisable()
    {
        Shader.SetGlobalVector(FormId, Vector4.zero);
        Shader.SetGlobalVector(EdgeId, Vector4.zero);
        Shader.SetGlobalVector(LipId, Vector4.zero);
        Shader.SetGlobalVector(Lip2Id, Vector4.zero);
    }

    int CurrentCheckpoint() => pathGen != null ? pathGen.PortalsTaken + 1 : 1;

    FormLook LookFor(int cp) => cp >= 1 && cp <= looks.Length && looks[cp - 1] != null ? looks[cp - 1] : Off;

    void Snap(int cp)
    {
        checkpoint = cp;
        from = to = LookFor(cp);
        FormLook.Lerp(to, to, 1f, current);
        blendT = 1f;
    }

    void LateUpdate()
    {
        int cp = CurrentCheckpoint();
        if (cp != checkpoint)
        {
            if (cp == checkpoint + 1)
            {
                from = current.Copy();
                to = LookFor(cp);
                blendT = 0f;
                checkpoint = cp;
            }
            else Snap(cp);
        }
        if (blendT < 1f)
        {
            blendT = Mathf.MoveTowards(blendT, 1f, Time.deltaTime / blendSeconds);
            FormLook.Lerp(from, to, Mathf.SmoothStep(0f, 1f, blendT), current);
        }
        Push();
    }

    void Push()
    {
        Vector4 lc = current.lipColor.linear;
        Shader.SetGlobalVector(FormId, new Vector4(current.strength, current.shadeSide, current.bottom, current.topLift));
        Shader.SetGlobalVector(EdgeId, new Vector4(current.crease, current.creaseWidth, current.foot, current.footWidth));
        Shader.SetGlobalVector(LipId, new Vector4(lc.x, lc.y, lc.z, current.lip));
        Shader.SetGlobalVector(Lip2Id, new Vector4(current.lipWidth, current.litSide, 0f, 0f));
    }
}
