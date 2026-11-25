using UnityEngine;

public class PulseEffect : MonoBehaviour
{
    public float minIntensity = 2f;
    public float maxIntensity = 10f;
    public float pulseSpeed = 2f;
    
    private Material mat;
    private Color baseColor;

    void Start()
    {
        Renderer r = GetComponent<Renderer>();
        if (r != null)
        {
            mat = r.material;
            baseColor = mat.GetColor("_EmissionColor");
            // Normalize base color if it's already bright
            float maxComponent = Mathf.Max(baseColor.r, baseColor.g, baseColor.b);
            if (maxComponent > 1f) baseColor /= maxComponent;
        }
    }

    void Update()
    {
        if (mat == null) return;
        
        float intensity = Mathf.Lerp(minIntensity, maxIntensity, (Mathf.Sin(Time.time * pulseSpeed) + 1f) / 2f);
        mat.SetColor("_EmissionColor", baseColor * intensity);
    }
}
