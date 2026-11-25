using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The on-track look shared by the pickups: a flat-shaded mesh standing on its face (spinning about the
/// face normal and bobbing), an optional hex bubble round it, a contact glow on the face, a faint light
/// pillar and a few rising sparkles. One instance per pickup type, built once and reused - nothing is
/// created after construction. All additive/unlit, no colliders, no lights, no shadows.
/// </summary>
public class PickupVisual
{
    public readonly Transform root;
    readonly MeshRenderer crest, bubble, contact, pillar;
    readonly MeshRenderer[] sparks = new MeshRenderer[4];
    readonly Material[] crestMats;
    readonly Material bubbleMat, contactMat, pillarMat, sparkMat;
    readonly Color accent;
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int FadeId = Shader.PropertyToID("_Fade");
    static readonly int WaveId = Shader.PropertyToID("_Wave");
    static readonly int FlashId = Shader.PropertyToID("_Flash");
    public bool Visible { get; private set; }

    public PickupVisual(Transform parent, string name, Mesh mesh, Color[] subColors, Color rim, Color accentColor, bool withBubble)
    {
        accent = accentColor;
        root = new GameObject(name).transform;
        root.SetParent(parent, false);

        Material flat = Resources.Load<Material>("PowerUps/PowerUpFlat");
        Shader flatShader = flat != null ? flat.shader : Shader.Find("Custom/PowerUpFlat");
        crestMats = new Material[subColors.Length];
        for (int i = 0; i < subColors.Length; i++)
        {
            crestMats[i] = flat != null ? new Material(flat) : new Material(flatShader);
            crestMats[i].SetColor(ColorId, subColors[i]);
            crestMats[i].SetColor("_Rim", Color.Lerp(rim, subColors[i], 0.4f));
        }
        crest = NewRenderer("Crest", mesh, crestMats);

        Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        Mesh sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        Shader glow = Shader.Find("Custom/SoftGlow");
        contactMat = new Material(glow) { name = name + "Contact" };
        contactMat.SetFloat("_Falloff", 1.8f); contactMat.SetFloat("_Core", 0.15f);
        pillarMat = new Material(glow) { name = name + "Pillar" };
        pillarMat.SetFloat("_Falloff", 2.4f); pillarMat.SetFloat("_Core", 0.05f);
        sparkMat = new Material(glow) { name = name + "Spark" };
        sparkMat.SetFloat("_Falloff", 2f); sparkMat.SetFloat("_Core", 0.3f);
        contact = NewRenderer("Contact", quad, contactMat);
        pillar = NewRenderer("Pillar", quad, pillarMat);
        for (int i = 0; i < sparks.Length; i++) sparks[i] = NewRenderer("Spark" + i, quad, sparkMat);

        if (withBubble)
        {
            Material b = Resources.Load<Material>("PowerUps/PowerUpBubble");
            bubbleMat = b != null ? new Material(b) : new Material(Shader.Find("Custom/PowerUpBubble"));
            bubbleMat.SetColor(ColorId, accent);
            bubbleMat.SetFloat("_Alpha", 0.25f);
            bubble = NewRenderer("Bubble", sphere, bubbleMat);
        }
        Hide();
    }

    MeshRenderer NewRenderer(string name, Mesh mesh, params Material[] mats)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = mats;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return r;
    }

    public void Hide()
    {
        Visible = false;
        root.gameObject.SetActive(false);
    }

    public void Destroy()
    {
        foreach (Material m in crestMats) if (m != null) Object.Destroy(m);
        if (bubbleMat != null) Object.Destroy(bubbleMat);
        if (contactMat != null) Object.Destroy(contactMat);
        if (pillarMat != null) Object.Destroy(pillarMat);
        if (sparkMat != null) Object.Destroy(sparkMat);
        if (root != null) Object.Destroy(root.gameObject);
    }

    public struct Pose
    {
        public Vector3 pos, faceCentre, outward, legDir;
        public float grow, time, cubeSize, size, bubbleRadius, spinSeconds, bob, bobPeriod, shimmerPeriod, pillarHeight;
        public Quaternion cameraRotation;
        public bool billboardFacing;      // true: stand the mesh to face the camera (focus's eye) instead of spinning
        public float pulse;               // 0 = none: breathing glow
    }

    public void Draw(in Pose p)
    {
        if (!Visible) { Visible = true; root.gameObject.SetActive(true); }
        float cs = p.cubeSize;
        float bobY = Mathf.Sin(p.time * Mathf.PI * 2f / Mathf.Max(0.1f, p.bobPeriod)) * p.bob * cs;
        Vector3 c = p.pos + p.outward * bobY;
        float g = Mathf.Max(0.0001f, p.grow);

        Quaternion rot;
        if (p.billboardFacing) rot = p.cameraRotation;
        else rot = Quaternion.AngleAxis(p.time * 360f / Mathf.Max(0.1f, p.spinSeconds), p.outward) * Quaternion.LookRotation(p.legDir, p.outward);
        crest.transform.SetPositionAndRotation(c, rot);
        crest.transform.localScale = Vector3.one * (p.size * cs * g);

        if (bubble != null)
        {
            bubble.transform.SetPositionAndRotation(c, Quaternion.identity);
            bubble.transform.localScale = Vector3.one * (p.bubbleRadius * 2f * cs * g);
            bubbleMat.SetFloat(WaveId, Mathf.Repeat(p.time / Mathf.Max(0.2f, p.shimmerPeriod), 1f) * 1.8f - 0.4f);
        }

        float breathe = p.pulse > 0f ? 0.75f + 0.25f * Mathf.Sin(p.time * Mathf.PI * 2f / p.pulse) : 1f;
        contact.transform.SetPositionAndRotation(p.faceCentre + p.outward * (0.02f * cs), Quaternion.LookRotation(p.outward, p.legDir));
        contact.transform.localScale = Vector3.one * (cs * 1.15f * g);
        contactMat.SetColor(ColorId, accent);
        contactMat.SetFloat(FadeId, 0.45f * g * breathe);

        if (p.pillarHeight > 0f)
        {
            pillar.enabled = true;
            Vector3 flat = p.cameraRotation * Vector3.forward; flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
            float h = p.pillarHeight * cs * g;
            pillar.transform.SetPositionAndRotation(p.faceCentre + Vector3.up * (h * 0.5f), Quaternion.LookRotation(flat.normalized, Vector3.up));
            pillar.transform.localScale = new Vector3(cs * 0.7f * g, h, 1f);
            pillarMat.SetColor(ColorId, accent);
            pillarMat.SetFloat(FadeId, 0.22f * g);
        }
        else pillar.enabled = false;

        Vector3 side = Vector3.Cross(p.outward, p.legDir);
        sparkMat.SetColor(ColorId, Color.Lerp(accent, Color.white, 0.35f));
        for (int i = 0; i < sparks.Length; i++)
        {
            float ph = Mathf.Repeat(p.time * 0.55f + i * 0.25f, 1f);
            Vector3 sp = c + p.outward * ((ph * 1.1f - 0.1f) * cs) + side * (Mathf.Sin(i * 2.3f + ph * 3f) * 0.32f * cs);
            sparks[i].transform.SetPositionAndRotation(sp, p.cameraRotation);
            sparks[i].transform.localScale = Vector3.one * (cs * 0.16f * g * Mathf.Sin(ph * Mathf.PI));
        }
        sparkMat.SetFloat(FadeId, 0.9f * g);
    }
}
