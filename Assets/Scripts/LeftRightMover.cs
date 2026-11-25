using UnityEngine;

public class LeftRightMover : MonoBehaviour
{
    [SerializeField] float speed = 6f;

    void Update()
    {
        float dir = Mathf.Clamp(KeyboardDirection() + PointerDirection(), -1f, 1f);
        if (dir != 0f) transform.Translate(dir * speed * Time.deltaTime, 0f, 0f, Space.World);
    }

    static float KeyboardDirection()
    {
        float d = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) d -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) d += 1f;
        return d;
    }

    static float PointerDirection()
    {
        float half = Screen.width * 0.5f;
        if (Input.touchCount > 0)
        {
            float d = 0f;
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) continue;
                d += t.position.x < half ? -1f : 1f;
            }
            return d;
        }
        if (Input.GetMouseButton(0)) return Input.mousePosition.x < half ? -1f : 1f;
        return 0f;
    }
}
