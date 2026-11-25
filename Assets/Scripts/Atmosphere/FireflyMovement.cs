using UnityEngine;

public class FireflyMovement : MonoBehaviour
{
    private Vector3 startPos;
    private float seed;
    public float range = 2.0f;
    public float speed = 1.0f;

    void Start()
    {
        startPos = transform.position;
        seed = Random.value * 100f;
    }

    void Update()
    {
        float t = Time.time * speed + seed;
        Vector3 offset = new Vector3(
            Mathf.PerlinNoise(t, 0) - 0.5f,
            Mathf.PerlinNoise(0, t) - 0.5f,
            Mathf.PerlinNoise(t, t) - 0.5f
        ) * range;
        
        transform.position = startPos + offset;
    }
}
