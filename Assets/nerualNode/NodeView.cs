using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class NodeView : MonoBehaviour
{
    public Color nodeColor;
    public bool isNeutral;
    public List<NodeView> neighbors = new List<NodeView>();
    public List<LineConnection> connections = new List<LineConnection>();
    public Vector3 originalLocalPos;

    private Renderer rend;
    private MaterialPropertyBlock propBlock;
    private GameObject halo;
    private GameObject glow;
    private float pulsePhase;

    public void Initialize()
    {
        rend = GetComponent<Renderer>();
        propBlock = new MaterialPropertyBlock();
        originalLocalPos = transform.localPosition;
        pulsePhase = Random.Range(0f, Mathf.PI * 2f);
        UpdateVisuals();
        BuildHalo();
        BuildGlow();
    }

    void BuildHalo()
    {
        halo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        halo.transform.SetParent(transform, false);
        halo.transform.localScale = Vector3.one * 3.5f;
        Destroy(halo.GetComponent<Collider>());
        var r = halo.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(1f, 1f, 1f, 0f);
        r.material = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        halo.SetActive(false);
    }

    void BuildGlow()
    {
        glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.transform.SetParent(transform, false);
        glow.transform.localScale = Vector3.one * 1.8f;
        Destroy(glow.GetComponent<Collider>());
        var r = glow.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        Color c = isNeutral ? new Color(0.92f, 0.95f, 1f) : nodeColor;
        mat.color = new Color(c.r, c.g, c.b, 0.08f);
        r.material = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    public void SetColor(Color c)
    {
        nodeColor = c;
        isNeutral = false;
        UpdateVisuals();
    }

    public void ActivateFromWhite(Color c)
    {
        isNeutral = false;
        nodeColor = c;
        UpdateVisuals();
        pulsePhase = 0;
        StartCoroutine(ActivationEffect());
    }

    IEnumerator ActivationEffect()
    {
        float dur = 0.6f;
        float elapsed = 0;
        Vector3 startScale = transform.localScale;
        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            float s = 1f + Mathf.Sin((elapsed / dur) * Mathf.PI) * 0.5f;
            transform.localScale = startScale * s;
            yield return null;
        }
        transform.localScale = startScale;
    }

    public void DestroyWithBurst()
    {
        if (MatchManager.Instance != null)
            MatchManager.Instance.SpawnBurst(transform.position, nodeColor);
            
        var allLines = new List<LineConnection>(connections);
        foreach (var line in allLines)
        {
            if (line == null) continue;
            NodeView other = (line.nodeA == this) ? line.nodeB : line.nodeA;
            RemoveConnection(other, line);
            other.RemoveConnection(this, line);
            Destroy(line.gameObject);
        }
        Destroy(gameObject);
    }

    public void UpdateVisuals()
    {
        if (rend == null) return;
        Color c = isNeutral ? new Color(0.92f, 0.95f, 1f) : nodeColor;
        rend.GetPropertyBlock(propBlock);
        propBlock.SetColor("_Color", c);
        if (rend.sharedMaterial != null && rend.sharedMaterial.HasProperty("_EmissionColor"))
            propBlock.SetColor("_EmissionColor", c * (isNeutral ? 1.2f : 3.8f));
        rend.SetPropertyBlock(propBlock);
        
        if (glow != null)
        {
            glow.GetComponent<Renderer>().material.color = new Color(c.r, c.g, c.b, 0.08f);
        }
    }

    void Update()
    {
        if (rend == null) return;
        pulsePhase += Time.deltaTime * (isNeutral ? 0.8f : 1.5f);
        float pulse = isNeutral
            ? (0.85f + 0.15f * Mathf.Sin(pulsePhase))
            : (0.8f + 0.2f * Mathf.Sin(pulsePhase));
        Color c = isNeutral ? new Color(0.92f, 0.95f, 1f) : nodeColor;
        rend.GetPropertyBlock(propBlock);
        if (rend.sharedMaterial != null && rend.sharedMaterial.HasProperty("_EmissionColor"))
            propBlock.SetColor("_EmissionColor", c * (isNeutral ? 1.1f : 3.5f) * pulse);
        rend.SetPropertyBlock(propBlock);
    }

    public void SetSelected(bool on)
    {
        if (halo == null) return;
        halo.SetActive(on);
        if (on)
        {
            Color hc = isNeutral ? Color.white : nodeColor;
            halo.GetComponent<Renderer>().material.color = new Color(hc.r, hc.g, hc.b, 0.15f);
        }
    }

    public void AddConnection(NodeView nb, LineConnection line)
    {
        if (!neighbors.Contains(nb)) neighbors.Add(nb);
        if (!connections.Contains(line)) connections.Add(line);
    }

    public void RemoveConnection(NodeView nb, LineConnection line)
    {
        neighbors.Remove(nb);
        connections.Remove(line);
    }

    public void Shake()
    {
        StartCoroutine(ShakeRoutine());
    }

    IEnumerator ShakeRoutine()
    {
        Vector3 start = transform.localPosition;
        float dur = 0.2f;
        float elapsed = 0;
        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            transform.localPosition = start + Random.insideUnitSphere * 0.05f;
            yield return null;
        }
        transform.localPosition = start;
    }

    public IEnumerator FadeOut(float duration = 0.45f)
    {
        Vector3 startScale = transform.localScale;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            transform.localScale = startScale * (1f - Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        Destroy(gameObject);
    }
}