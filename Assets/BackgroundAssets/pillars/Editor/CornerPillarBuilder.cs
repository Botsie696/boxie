using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools > Corner Props > Build Pillars
///
/// Turns every .glb in Assets/BackgroundAssets/pillars (sorted by file name = the order they are used in) into a
/// game-ready corner prop under pillars/Built:
///   * the three images packed in the glb (base colour, normal, glTF metallic-roughness) are pulled straight out of
///     its binary chunk, shrunk to textureSize and saved as PNGs - the metallic-roughness map is repacked into URP's
///     layout (metal in R, smoothness = 1 - roughness in A);
///   * a URP Lit material wearing them;
///   * the mesh (1.5 M triangles out of the generator - far too heavy for a phone) is welded and reduced with a
///     quadric edge-collapse to targetTriangles, UVs carried per triangle corner so the texture still lands, smooth
///     normals rebuilt; it is stood upright with its pivot in the middle of its base;
///   * a prefab Pillar_NN, and the CornerPropSet CornerProps_Pillars listing them in order (5 legs each).
/// Then (separate item) the set is wired into MainScene prop camera 3.7's DynamicPathGenerator.
///
/// Re-runnable: everything in Built is overwritten. Add a .glb to the folder and run it again to use it.
/// </summary>
public static class CornerPillarBuilder
{
    const string SourceDir = "Assets/BackgroundAssets/pillars";
    const string BuiltDir = SourceDir + "/Built";
    const string SetPath = SourceDir + "/CornerProps_Pillars.asset";
    const string ScenePath = "Assets/MainScene prop camera 3.7.unity";

    public static int targetTriangles = 6000;
    public static int textureSize = 1024;
    // Self-glow: the base texture is also the emission map, so a pillar lights itself in its own colours and reads
    // against the dark chasm of 3.7 without a real light (mobile). 0 = lit by the scene only.
    public static float glow = 0.85f;

    [MenuItem("Tools/Corner Props/Build Pillars")]
    public static void BuildMenu() => Build();

    [MenuItem("Tools/Corner Props/Wire Pillars Into 3.7")]
    public static void WireMenu() => WireInto37();

    public static CornerPropSet Build()
    {
        if (!AssetDatabase.IsValidFolder(BuiltDir)) AssetDatabase.CreateFolder(SourceDir, "Built");

        string[] glbs = Directory.GetFiles(SourceDir, "*.glb").Select(p => p.Replace('\\', '/')).OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal).ToArray();
        var prefabs = new List<GameObject>();
        try
        {
            for (int i = 0; i < glbs.Length; i++)
            {
                string name = $"Pillar_{i + 1:00}";
                EditorUtility.DisplayProgressBar("Corner pillars", $"{name}  ({Path.GetFileName(glbs[i])})", i / (float)glbs.Length);
                GameObject prefab = BuildOne(glbs[i], name);
                if (prefab != null) prefabs.Add(prefab);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        var set = AssetDatabase.LoadAssetAtPath<CornerPropSet>(SetPath);
        if (set == null) { set = ScriptableObject.CreateInstance<CornerPropSet>(); AssetDatabase.CreateAsset(set, SetPath); }
        set.props = prefabs.ToArray();
        if (set.legsPerProp < 1) set.legsPerProp = 5;
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Debug.Log($"[CornerPillarBuilder] built {prefabs.Count} pillars -> {SetPath}");
        return set;
    }

    /// <summary>Turns the props on in 3.7 with the pillar set for every checkpoint. Saves the scene only if it was not already open.</summary>
    public static void WireInto37()
    {
        var set = AssetDatabase.LoadAssetAtPath<CornerPropSet>(SetPath);
        if (set == null) set = Build();

        var scene = EditorSceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (DynamicPathGenerator gen in root.GetComponentsInChildren<DynamicPathGenerator>(true))
            {
                Undo.RecordObject(gen, "Wire corner pillars");
                gen.spawnCornerProps = true;
                gen.cornerPropSets = new[] { set };
                EditorUtility.SetDirty(gen);
                Debug.Log($"[CornerPillarBuilder] corner pillars on for {gen.name} in {scene.name}");
            }

        EditorSceneManager.MarkSceneDirty(scene);
        if (opened) { EditorSceneManager.SaveScene(scene); EditorSceneManager.CloseScene(scene, true); }
    }

    // ------------------------------------------------------------------
    // One model
    // ------------------------------------------------------------------

    static GameObject BuildOne(string glbPath, string name)
    {
        // --- textures, straight out of the glb ---
        byte[] glb = File.ReadAllBytes(glbPath);
        if (!ReadGlb(glb, out string json, out int binStart, out int binLength)) { Debug.LogError($"[CornerPillarBuilder] not a glb: {glbPath}"); return null; }
        var gltf = JsonUtility.FromJson<Gltf>(json);
        GltfMaterial mat = gltf.materials != null && gltf.materials.Length > 0 ? gltf.materials[0] : null;

        Texture2D baseMap = null, normalMap = null, maskMap = null;
        if (mat != null)
        {
            byte[] baseBytes = ImageBytes(gltf, glb, binStart, mat.pbrMetallicRoughness?.baseColorTexture);
            byte[] normBytes = ImageBytes(gltf, glb, binStart, mat.normalTexture);
            byte[] mrBytes = ImageBytes(gltf, glb, binStart, mat.pbrMetallicRoughness?.metallicRoughnessTexture);
            if (baseBytes != null) baseMap = SaveTexture(baseBytes, $"{BuiltDir}/{name}_BaseColor.png", false, Repack.None);
            if (normBytes != null) normalMap = SaveTexture(normBytes, $"{BuiltDir}/{name}_Normal.png", true, Repack.None);
            if (mrBytes != null) maskMap = SaveTexture(mrBytes, $"{BuiltDir}/{name}_MetalSmooth.png", true, Repack.GltfToUrpMetallic);
        }
        AssetDatabase.Refresh();
        ConfigureTexture($"{BuiltDir}/{name}_BaseColor.png", TextureImporterType.Default, true);
        ConfigureTexture($"{BuiltDir}/{name}_Normal.png", TextureImporterType.NormalMap, false);
        ConfigureTexture($"{BuiltDir}/{name}_MetalSmooth.png", TextureImporterType.Default, false);
        baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{BuiltDir}/{name}_BaseColor.png");
        normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{BuiltDir}/{name}_Normal.png");
        maskMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{BuiltDir}/{name}_MetalSmooth.png");

        // --- material ---
        string matPath = $"{BuiltDir}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (material == null) { material = new Material(lit); AssetDatabase.CreateAsset(material, matPath); }
        else material.shader = lit;
        Color baseColor = Color.white;
        if (mat?.pbrMetallicRoughness?.baseColorFactor != null && mat.pbrMetallicRoughness.baseColorFactor.Length == 4)
        {
            float[] f = mat.pbrMetallicRoughness.baseColorFactor;
            baseColor = new Color(f[0], f[1], f[2], f[3]).gamma;   // glTF factors are linear
        }
        material.SetTexture("_BaseMap", baseMap);
        material.SetTexture("_MainTex", baseMap);
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_Color", baseColor);
        if (normalMap != null) { material.SetTexture("_BumpMap", normalMap); material.SetFloat("_BumpScale", 1f); material.EnableKeyword("_NORMALMAP"); }
        if (maskMap != null)
        {
            material.SetTexture("_MetallicGlossMap", maskMap);
            material.SetFloat("_Smoothness", 1f);      // with a map: smoothness = map alpha x this
            material.SetFloat("_Metallic", 1f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        material.SetTexture("_EmissionMap", baseMap);
        material.SetColor("_EmissionColor", Color.white * glow);
        if (glow > 0f) { material.EnableKeyword("_EMISSION"); material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        else material.DisableKeyword("_EMISSION");
        material.SetFloat("_SpecularHighlights", 1f);
        material.SetFloat("_EnvironmentReflections", 1f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        Material fadeTwin = SaveFadeTwin(material, $"{BuiltDir}/{name}_Fade.mat");

        // --- mesh: the importer's, stood upright, welded, reduced ---
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
        if (source == null) { Debug.LogError($"[CornerPillarBuilder] the glb has not been imported: {glbPath}"); return null; }
        GameObject temp = UnityEngine.Object.Instantiate(source);
        Mesh reduced;
        try
        {
            MeshFilter mf = temp.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m => m.sharedMesh != null);
            if (mf == null) { Debug.LogError($"[CornerPillarBuilder] no mesh in {glbPath}"); return null; }
            // The importer may put the glTF node rotation (Z-up -> Y-up) on the ROOT, so take the full world matrix
            // with the root parked at the origin - rotation and scale kept, position dropped.
            temp.transform.position = Vector3.zero;
            Matrix4x4 toRoot = mf.transform.localToWorldMatrix;
            reduced = Reduce(mf.sharedMesh, toRoot, targetTriangles, name);
        }
        finally { UnityEngine.Object.DestroyImmediate(temp); }

        string meshPath = $"{BuiltDir}/{name}_Mesh.asset";
        var oldMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (oldMesh != null) { EditorUtility.CopySerialized(reduced, oldMesh); reduced = oldMesh; }
        else AssetDatabase.CreateAsset(reduced, meshPath);

        // --- prefab ---
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = reduced;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        var fader = go.AddComponent<CornerPropFader>();   // fades with the legs (PathDepthShading)
        fader.opaqueMaterials = new[] { material };
        fader.fadeTwins = new[] { fadeTwin };
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{BuiltDir}/{name}.prefab");
        UnityEngine.Object.DestroyImmediate(go);
        Debug.Log($"[CornerPillarBuilder] {name}: {Path.GetFileName(glbPath)} -> {reduced.triangles.Length / 3} triangles, height {reduced.bounds.size.y:0.00}");
        return prefab;
    }

    // ------------------------------------------------------------------
    // Fade twin
    // ------------------------------------------------------------------

    /// <summary>
    /// The transparent copy CornerPropFader swaps to while the prop fades with the legs. Saved as an asset so the
    /// transparent shader variant is kept in a build. Re-run safe: overwrites the twin from the current material.
    /// </summary>
    public static Material SaveFadeTwin(Material opaque, string path)
    {
        var twin = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (twin == null) { twin = new Material(opaque); AssetDatabase.CreateAsset(twin, path); }
        else twin.CopyPropertiesFromMaterial(opaque);
        twin.shader = opaque.shader;
        twin.shaderKeywords = opaque.shaderKeywords;
        twin.name = Path.GetFileNameWithoutExtension(path);
        CornerPropFader.MakeTransparent(twin);
        EditorUtility.SetDirty(twin);
        return twin;
    }

    // ------------------------------------------------------------------
    // glb reading
    // ------------------------------------------------------------------

    [Serializable] class Gltf { public GltfMaterial[] materials; public GltfTexture[] textures; public GltfImage[] images; public GltfBufferView[] bufferViews; }
    [Serializable] class GltfMaterial { public string name; public GltfPbr pbrMetallicRoughness; public GltfTexRef normalTexture; }
    [Serializable] class GltfPbr { public float[] baseColorFactor; public GltfTexRef baseColorTexture; public GltfTexRef metallicRoughnessTexture; }
    [Serializable] class GltfTexRef { public int index = -1; }
    [Serializable] class GltfTexture { public int source = -1; }
    [Serializable] class GltfImage { public int bufferView = -1; public string mimeType; }
    [Serializable] class GltfBufferView { public int buffer; public int byteOffset; public int byteLength; }

    static bool ReadGlb(byte[] glb, out string json, out int binStart, out int binLength)
    {
        json = null; binStart = binLength = 0;
        if (glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != 0x46546C67) return false;   // "glTF"
        int offset = 12;
        while (offset + 8 <= glb.Length)
        {
            int length = (int)BitConverter.ToUInt32(glb, offset);
            uint type = BitConverter.ToUInt32(glb, offset + 4);
            if (type == 0x4E4F534A) json = Encoding.UTF8.GetString(glb, offset + 8, length);   // JSON
            else if (type == 0x004E4942) { binStart = offset + 8; binLength = length; }          // BIN
            offset += 8 + length;
        }
        return json != null;
    }

    static byte[] ImageBytes(Gltf gltf, byte[] glb, int binStart, GltfTexRef texRef)
    {
        if (texRef == null || texRef.index < 0 || gltf.textures == null || texRef.index >= gltf.textures.Length) return null;
        int src = gltf.textures[texRef.index].source;
        if (src < 0 || gltf.images == null || src >= gltf.images.Length) return null;
        int bv = gltf.images[src].bufferView;
        if (bv < 0 || gltf.bufferViews == null || bv >= gltf.bufferViews.Length) return null;
        GltfBufferView view = gltf.bufferViews[bv];
        var bytes = new byte[view.byteLength];
        Buffer.BlockCopy(glb, binStart + view.byteOffset, bytes, 0, view.byteLength);
        return bytes;
    }

    enum Repack { None, GltfToUrpMetallic }

    /// <summary>Decodes, shrinks to textureSize (never grows), optionally repacks, and writes a PNG.</summary>
    static Texture2D SaveTexture(byte[] encoded, string path, bool linear, Repack repack)
    {
        var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
        if (!src.LoadImage(encoded)) { UnityEngine.Object.DestroyImmediate(src); return null; }

        int w = Mathf.Min(textureSize, src.width), h = Mathf.Min(textureSize, src.height);
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var dst = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
        dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        dst.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        UnityEngine.Object.DestroyImmediate(src);

        if (repack == Repack.GltfToUrpMetallic)
        {
            // glTF: G = roughness, B = metalness.  URP Lit: R = metallic, A = smoothness.
            Color32[] px = dst.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                byte metal = px[i].b, smooth = (byte)(255 - px[i].g);
                px[i] = new Color32(metal, metal, metal, smooth);
            }
            dst.SetPixels32(px);
            dst.Apply();
        }

        File.WriteAllBytes(path, dst.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(dst);
        return null;   // loaded back through the AssetDatabase once imported
    }

    static void ConfigureTexture(string path, TextureImporterType type, bool sRGB)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;
        imp.textureType = type;
        imp.sRGBTexture = sRGB;
        imp.maxTextureSize = textureSize;
        imp.mipmapEnabled = true;
        imp.alphaIsTransparency = false;
        imp.textureCompression = TextureImporterCompression.Compressed;
        imp.SaveAndReimport();
    }

    // ------------------------------------------------------------------
    // Mesh reduction: quadric edge collapse (after Garland & Heckbert / Sp4cerat's fast simplifier)
    // ------------------------------------------------------------------

    struct Quadric
    {
        public double m0, m1, m2, m3, m4, m5, m6, m7, m8, m9;
        public static Quadric Plane(double a, double b, double c, double d) => new Quadric
        {
            m0 = a * a, m1 = a * b, m2 = a * c, m3 = a * d, m4 = b * b, m5 = b * c, m6 = b * d, m7 = c * c, m8 = c * d, m9 = d * d
        };
        public static Quadric operator +(Quadric x, Quadric y) => new Quadric
        {
            m0 = x.m0 + y.m0, m1 = x.m1 + y.m1, m2 = x.m2 + y.m2, m3 = x.m3 + y.m3, m4 = x.m4 + y.m4,
            m5 = x.m5 + y.m5, m6 = x.m6 + y.m6, m7 = x.m7 + y.m7, m8 = x.m8 + y.m8, m9 = x.m9 + y.m9
        };
        public double Error(Vector3d p) =>
            m0 * p.x * p.x + 2 * m1 * p.x * p.y + 2 * m2 * p.x * p.z + 2 * m3 * p.x
            + m4 * p.y * p.y + 2 * m5 * p.y * p.z + 2 * m6 * p.y + m7 * p.z * p.z + 2 * m8 * p.z + m9;
    }

    struct Vector3d
    {
        public double x, y, z;
        public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3d operator *(Vector3d a, double s) => new Vector3d(a.x * s, a.y * s, a.z * s);
        public static double Dot(Vector3d a, Vector3d b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3d Cross(Vector3d a, Vector3d b) => new Vector3d(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public Vector3d Normalized { get { double l = Math.Sqrt(x * x + y * y + z * z); return l > 1e-20 ? new Vector3d(x / l, y / l, z / l) : this; } }
    }

    struct Tri
    {
        public int v0, v1, v2;
        public double e0, e1, e2, e3;
        public bool deleted, dirty;
        public Vector3d n;
        public Vector2 uv0, uv1, uv2;
        public int V(int j) => j == 0 ? v0 : j == 1 ? v1 : v2;
        public void SetV(int j, int v) { if (j == 0) v0 = v; else if (j == 1) v1 = v; else v2 = v; }
        public Vector2 UV(int j) => j == 0 ? uv0 : j == 1 ? uv1 : uv2;
        public void SetUV(int j, Vector2 uv) { if (j == 0) uv0 = uv; else if (j == 1) uv1 = uv; else uv2 = uv; }
        public double E(int j) => j == 0 ? e0 : j == 1 ? e1 : e2;
    }

    struct Vert { public Vector3d p; public int tstart, tcount; public Quadric q; public bool border; }
    struct Ref { public int tid, tvertex; }

    class Simplifier
    {
        public Tri[] tris; public int triCount;
        public Vert[] verts; public int vertCount;
        Ref[] refs = new Ref[0]; int refCount;
        public static string Report;
        long rejBorder, rejFlip, collapses;

        void PushRef(Ref r)
        {
            if (refCount == refs.Length) Array.Resize(ref refs, Math.Max(16, refs.Length * 2));
            refs[refCount++] = r;
        }

        public void Simplify(int target, double aggressiveness, Action<float> progress)
        {
            for (int i = 0; i < triCount; i++) tris[i].deleted = false;
            int deletedTris = 0;
            int startCount = triCount;
            var deleted0 = new List<bool>(); var deleted1 = new List<bool>();

            for (int iteration = 0; iteration < 2000; iteration++)   // big models need far more than the classic 100 passes
            {
                if (startCount - deletedTris <= target) break;   // startCount: triCount shrinks when UpdateMesh compacts
                progress?.Invoke(1f - (startCount - deletedTris - target) / (float)Math.Max(1, startCount - target));
                if (iteration % 5 == 0) UpdateMesh(iteration);
                for (int i = 0; i < triCount; i++) tris[i].dirty = false;

                double threshold = 0.000000001 * Math.Pow(iteration + 3, aggressiveness);
                for (int i = 0; i < triCount; i++)
                {
                    Tri t = tris[i];
                    if (t.e3 > threshold || t.deleted || t.dirty) continue;
                    for (int j = 0; j < 3; j++)
                    {
                        if (t.E(j) >= threshold) continue;
                        int i0 = t.V(j), i1 = t.V((j + 1) % 3);
                        if (verts[i0].border != verts[i1].border) { rejBorder++; continue; }

                        CalculateError(i0, i1, out Vector3d p);
                        Resize(deleted0, verts[i0].tcount); Resize(deleted1, verts[i1].tcount);
                        if (Flipped(p, i1, i0, deleted0)) { rejFlip++; continue; }
                        if (Flipped(p, i0, i1, deleted1)) { rejFlip++; continue; }

                        UpdateUVs(i0, p, deleted0);
                        UpdateUVs(i1, p, deleted1);

                        verts[i0].p = p;
                        verts[i0].q = verts[i1].q + verts[i0].q;
                        int tstart = refCount;
                        UpdateTriangles(i0, i0, deleted0, ref deletedTris);
                        UpdateTriangles(i0, i1, deleted1, ref deletedTris);
                        int tcount = refCount - tstart;
                        if (tcount <= verts[i0].tcount)
                        {
                            if (tcount > 0) Array.Copy(refs, tstart, refs, verts[i0].tstart, tcount);
                        }
                        else verts[i0].tstart = tstart;
                        verts[i0].tcount = tcount;
                        collapses++;
                        break;
                    }
                    if (startCount - deletedTris <= target) break;   // startCount: triCount shrinks when UpdateMesh compacts
                }
            }
            int borders = 0; for (int i = 0; i < vertCount; i++) if (verts[i].border) borders++;
            Report = $"verts {vertCount} borders {borders} tris {startCount} -> {startCount - deletedTris}; rejects: border {rejBorder} flip {rejFlip}; collapses {collapses}";
            CompactMesh();
        }

        static void Resize(List<bool> l, int n) { while (l.Count < n) l.Add(false); if (l.Count > n) l.RemoveRange(n, l.Count - n); }

        // Would moving vertex `self` (its triangles) to p flip or degenerate one of them?
        bool Flipped(Vector3d p, int other, int self, List<bool> deleted)
        {
            Vert v = verts[self];
            for (int k = 0; k < v.tcount; k++)
            {
                Ref r = refs[v.tstart + k];
                Tri t = tris[r.tid];
                if (t.deleted) continue;
                int id1 = t.V((r.tvertex + 1) % 3), id2 = t.V((r.tvertex + 2) % 3);
                if (id1 == other || id2 == other) { deleted[k] = true; continue; }
                Vector3d d1 = (verts[id1].p - p).Normalized, d2 = (verts[id2].p - p).Normalized;
                if (Math.Abs(Vector3d.Dot(d1, d2)) > 0.999) return true;
                Vector3d n = Vector3d.Cross(d1, d2).Normalized;
                deleted[k] = false;
                if (Vector3d.Dot(n, t.n) < 0.2) return true;
            }
            return false;
        }

        // The moved corner keeps its triangle's own UV chart: its UV is re-read from p's barycentrics in that triangle.
        void UpdateUVs(int vi, Vector3d p, List<bool> deleted)
        {
            Vert v = verts[vi];
            for (int k = 0; k < v.tcount; k++)
            {
                Ref r = refs[v.tstart + k];
                Tri t = tris[r.tid];
                if (t.deleted || deleted[k]) continue;
                Vector3d a = verts[t.v0].p, b = verts[t.v1].p, c = verts[t.v2].p;
                Barycentric(p, a, b, c, out double u, out double w0, out double w1);
                Vector2 uv = (float)u * t.uv0 + (float)w0 * t.uv1 + (float)w1 * t.uv2;
                tris[r.tid].SetUV(r.tvertex, uv);
            }
        }

        static void Barycentric(Vector3d p, Vector3d a, Vector3d b, Vector3d c, out double u, out double v, out double w)
        {
            Vector3d v0 = b - a, v1 = c - a, v2 = p - a;
            double d00 = Vector3d.Dot(v0, v0), d01 = Vector3d.Dot(v0, v1), d11 = Vector3d.Dot(v1, v1);
            double d20 = Vector3d.Dot(v2, v0), d21 = Vector3d.Dot(v2, v1);
            double denom = d00 * d11 - d01 * d01;
            if (Math.Abs(denom) < 1e-30) { u = 1; v = w = 0; return; }
            v = (d11 * d20 - d01 * d21) / denom;
            w = (d00 * d21 - d01 * d20) / denom;
            u = 1.0 - v - w;
        }

        void UpdateTriangles(int i0, int vi, List<bool> deleted, ref int deletedTris)
        {
            Vert v = verts[vi];
            for (int k = 0; k < v.tcount; k++)
            {
                Ref r = refs[v.tstart + k];
                if (tris[r.tid].deleted) continue;
                if (deleted[k]) { tris[r.tid].deleted = true; deletedTris++; continue; }
                tris[r.tid].SetV(r.tvertex, i0);
                tris[r.tid].dirty = true;
                tris[r.tid].e0 = CalculateError(tris[r.tid].v0, tris[r.tid].v1, out _);
                tris[r.tid].e1 = CalculateError(tris[r.tid].v1, tris[r.tid].v2, out _);
                tris[r.tid].e2 = CalculateError(tris[r.tid].v2, tris[r.tid].v0, out _);
                tris[r.tid].e3 = Math.Min(tris[r.tid].e0, Math.Min(tris[r.tid].e1, tris[r.tid].e2));
                PushRef(r);
            }
        }

        void UpdateMesh(int iteration)
        {
            if (iteration > 0)
            {
                int dst = 0;
                for (int i = 0; i < triCount; i++) if (!tris[i].deleted) tris[dst++] = tris[i];
                triCount = dst;
            }

            // Triangle lists per vertex.
            for (int i = 0; i < vertCount; i++) { verts[i].tstart = 0; verts[i].tcount = 0; }
            for (int i = 0; i < triCount; i++) { verts[tris[i].v0].tcount++; verts[tris[i].v1].tcount++; verts[tris[i].v2].tcount++; }
            int tstart = 0;
            for (int i = 0; i < vertCount; i++) { verts[i].tstart = tstart; tstart += verts[i].tcount; verts[i].tcount = 0; }
            if (refs.Length < triCount * 3) refs = new Ref[triCount * 3];
            refCount = triCount * 3;
            for (int i = 0; i < triCount; i++)
                for (int j = 0; j < 3; j++)
                {
                    int vi = tris[i].V(j);
                    refs[verts[vi].tstart + verts[vi].tcount] = new Ref { tid = i, tvertex = j };
                    verts[vi].tcount++;
                }

            if (iteration != 0) return;

            // Open edges: a vertex on an edge used by a single triangle is a border vertex.
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < vertCount; i++) verts[i].border = false;
            for (int i = 0; i < vertCount; i++)
            {
                counts.Clear();
                Vert v = verts[i];
                for (int k = 0; k < v.tcount; k++)
                {
                    Tri t = tris[refs[v.tstart + k].tid];
                    for (int j = 0; j < 3; j++)
                    {
                        int id = t.V(j);
                        counts[id] = counts.TryGetValue(id, out int c) ? c + 1 : 1;
                    }
                }
                foreach (var kv in counts) if (kv.Value == 1) verts[kv.Key].border = true;
            }

            // Quadrics.
            for (int i = 0; i < vertCount; i++) verts[i].q = default;
            for (int i = 0; i < triCount; i++)
            {
                Vector3d p0 = verts[tris[i].v0].p, p1 = verts[tris[i].v1].p, p2 = verts[tris[i].v2].p;
                Vector3d n = Vector3d.Cross(p1 - p0, p2 - p0).Normalized;
                tris[i].n = n;
                Quadric q = Quadric.Plane(n.x, n.y, n.z, -Vector3d.Dot(n, p0));
                verts[tris[i].v0].q = verts[tris[i].v0].q + q;
                verts[tris[i].v1].q = verts[tris[i].v1].q + q;
                verts[tris[i].v2].q = verts[tris[i].v2].q + q;
            }
            for (int i = 0; i < triCount; i++)
            {
                tris[i].e0 = CalculateError(tris[i].v0, tris[i].v1, out _);
                tris[i].e1 = CalculateError(tris[i].v1, tris[i].v2, out _);
                tris[i].e2 = CalculateError(tris[i].v2, tris[i].v0, out _);
                tris[i].e3 = Math.Min(tris[i].e0, Math.Min(tris[i].e1, tris[i].e2));
            }
        }

        void CompactMesh()
        {
            int dst = 0;
            for (int i = 0; i < vertCount; i++) verts[i].tcount = 0;
            for (int i = 0; i < triCount; i++)
            {
                if (tris[i].deleted) continue;
                tris[dst++] = tris[i];
                verts[tris[i].v0].tcount = 1; verts[tris[i].v1].tcount = 1; verts[tris[i].v2].tcount = 1;
            }
            triCount = dst;
            dst = 0;
            for (int i = 0; i < vertCount; i++)
            {
                if (verts[i].tcount == 0) continue;
                verts[i].tstart = dst;
                verts[dst].p = verts[i].p;
                dst++;
            }
            for (int i = 0; i < triCount; i++)
            {
                tris[i].v0 = verts[tris[i].v0].tstart;
                tris[i].v1 = verts[tris[i].v1].tstart;
                tris[i].v2 = verts[tris[i].v2].tstart;
            }
            vertCount = dst;
        }

        static double Det(Quadric q, int a11, int a12, int a13, int a21, int a22, int a23, int a31, int a32, int a33)
        {
            double M(int i) => i switch { 0 => q.m0, 1 => q.m1, 2 => q.m2, 3 => q.m3, 4 => q.m4, 5 => q.m5, 6 => q.m6, 7 => q.m7, 8 => q.m8, _ => q.m9 };
            return M(a11) * M(a22) * M(a33) + M(a13) * M(a21) * M(a32) + M(a12) * M(a23) * M(a31)
                 - M(a13) * M(a22) * M(a31) - M(a11) * M(a23) * M(a32) - M(a12) * M(a21) * M(a33);
        }

        double CalculateError(int a, int b, out Vector3d result)
        {
            Quadric q = verts[a].q + verts[b].q;
            bool border = verts[a].border && verts[b].border;
            double det = Det(q, 0, 1, 2, 1, 4, 5, 2, 5, 7);
            if (det != 0 && !border)
            {
                result = new Vector3d(
                    -1 / det * Det(q, 1, 2, 3, 4, 5, 6, 5, 7, 8),
                     1 / det * Det(q, 0, 2, 3, 1, 5, 6, 2, 7, 8),
                    -1 / det * Det(q, 0, 1, 3, 1, 4, 6, 2, 5, 8));
                // On smooth, dense meshes (the generator's 1.5 M triangle pillars) the quadric is nearly singular and
                // the "optimal" point shoots far off along the surface; every collapse to it would then be rejected as
                // a flip and the reduction stalls. Only trust it when it stays near the edge.
                Vector3d mid = (verts[a].p + verts[b].p) * 0.5, e = verts[a].p - verts[b].p, off = result - mid;
                if (Vector3d.Dot(off, off) <= Vector3d.Dot(e, e)) return q.Error(result);
            }
            Vector3d p1 = verts[a].p, p2 = verts[b].p, p3 = (p1 + p2) * 0.5;
            double e1 = q.Error(p1), e2 = q.Error(p2), e3 = q.Error(p3);
            double min = Math.Min(e1, Math.Min(e2, e3));
            result = min == e1 ? p1 : min == e2 ? p2 : p3;
            return min;
        }
    }

    /// <summary>
    /// The importer's mesh -> a light one: positions welded (the importer splits them along UV seams, which would
    /// read as open borders), reduced, then split again wherever two corners of a vertex want different UVs, with
    /// smooth area-weighted normals shared across those splits so the seams do not show as creases. Stood on its base:
    /// pivot in the middle of the bottom.
    /// </summary>
    static Mesh Reduce(Mesh src, Matrix4x4 toRoot, int target, string name)
    {
        Vector3[] pos = src.vertices;
        Vector2[] uv = src.uv;
        if (uv == null || uv.Length != pos.Length) uv = new Vector2[pos.Length];

        var weld = new Dictionary<Vector3, int>(pos.Length);
        var remap = new int[pos.Length];
        var welded = new List<Vector3d>(pos.Length);
        for (int i = 0; i < pos.Length; i++)
        {
            Vector3 p = toRoot.MultiplyPoint3x4(pos[i]);
            if (!weld.TryGetValue(p, out int id)) { id = welded.Count; weld[p] = id; welded.Add(new Vector3d(p.x, p.y, p.z)); }
            remap[i] = id;
        }

        bool flip = toRoot.determinant < 0f;   // a mirroring transform turns the winding inside out
        var s = new Simplifier();
        s.vertCount = welded.Count;
        s.verts = new Vert[welded.Count];
        for (int i = 0; i < welded.Count; i++) s.verts[i].p = welded[i];

        var trisOut = new List<Tri>();
        for (int sub = 0; sub < src.subMeshCount; sub++)
        {
            int[] idx = src.GetTriangles(sub);
            for (int i = 0; i + 2 < idx.Length; i += 3)
            {
                int a = idx[i], b = flip ? idx[i + 2] : idx[i + 1], c = flip ? idx[i + 1] : idx[i + 2];
                int wa = remap[a], wb = remap[b], wc = remap[c];
                if (wa == wb || wb == wc || wa == wc) continue;
                trisOut.Add(new Tri { v0 = wa, v1 = wb, v2 = wc, uv0 = uv[a], uv1 = uv[b], uv2 = uv[c] });
            }
        }
        s.tris = trisOut.ToArray();
        s.triCount = s.tris.Length;
        trisOut = null;

        s.Simplify(target, 7.0, f => EditorUtility.DisplayProgressBar("Corner pillars", $"{name}: reducing {src.vertexCount:N0} vertices", Mathf.Clamp01(f)));
        Debug.Log($"[CornerPillarBuilder] {name} reduce: {Simplifier.Report}");

        // Smooth normals per welded vertex.
        var normals = new Vector3d[s.vertCount];
        for (int i = 0; i < s.triCount; i++)
        {
            Tri t = s.tris[i];
            Vector3d n = Vector3d.Cross(s.verts[t.v1].p - s.verts[t.v0].p, s.verts[t.v2].p - s.verts[t.v0].p);   // area weighted
            normals[t.v0] = normals[t.v0] + n; normals[t.v1] = normals[t.v1] + n; normals[t.v2] = normals[t.v2] + n;
        }

        // Bounds for the pivot.
        double minY = double.MaxValue, minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
        for (int i = 0; i < s.vertCount; i++)
        {
            Vector3d p = s.verts[i].p;
            minY = Math.Min(minY, p.y); minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x); minZ = Math.Min(minZ, p.z); maxZ = Math.Max(maxZ, p.z);
        }
        var pivot = new Vector3d((minX + maxX) * 0.5, minY, (minZ + maxZ) * 0.5);

        var outPos = new List<Vector3>(); var outNrm = new List<Vector3>(); var outUv = new List<Vector2>();
        var outIdx = new List<int>(s.triCount * 3);
        var split = new Dictionary<(int, Vector2), int>();
        for (int i = 0; i < s.triCount; i++)
        {
            Tri t = s.tris[i];
            for (int j = 0; j < 3; j++)
            {
                int v = t.V(j); Vector2 u = t.UV(j);
                var key = (v, new Vector2(Mathf.Round(u.x * 8192f), Mathf.Round(u.y * 8192f)));
                if (!split.TryGetValue(key, out int id))
                {
                    id = outPos.Count;
                    split[key] = id;
                    Vector3d p = s.verts[v].p - pivot;
                    outPos.Add(new Vector3((float)p.x, (float)p.y, (float)p.z));
                    Vector3d n = normals[v].Normalized;
                    outNrm.Add(new Vector3((float)n.x, (float)n.y, (float)n.z));
                    outUv.Add(u);
                }
                outIdx.Add(id);
            }
        }

        var mesh = new Mesh { name = name + "_Mesh" };
        mesh.indexFormat = outPos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(outPos);
        mesh.SetNormals(outNrm);
        mesh.SetUVs(0, outUv);
        mesh.SetTriangles(outIdx, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }
}
