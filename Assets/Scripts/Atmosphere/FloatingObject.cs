using UnityEngine;

public class FloatingObject : MonoBehaviour
{
    public float floatAmplitude = 0.2f;
    public float floatFrequency = 1.0f;
    public Vector3 rotationSpeed = new Vector3(0, 20, 0);
    
    private Vector3 startPos;
    private float seed;

    void Start()
    {
        startPos = transform.localPosition;
        seed = Random.value * Mathf.PI * 2;
    }

    void Update()
    {
        float verticalOffset = Mathf.Sin(Time.time * floatFrequency + seed) * floatAmplitude;
        transform.localPosition = startPos + Vector3.up * verticalOffset;
        transform.Rotate(rotationSpeed * Time.deltaTime);
    }
}
