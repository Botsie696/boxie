using UnityEngine;

public class GlobeInputController : MonoBehaviour
{
    public Transform globe;
    public float rotationSensitivity = 0.25f;
    public float sphereRadius = 5f;
    public float baseRotationSpeed = 15f; // Constant minimum speed
    public float inertiaDamping = 1.2f;    // How fast it returns to base speed
    public float selectionRadius = 0.55f; // Generous node hitbox

    private NodeView selectedNode;
    private Vector3 lastMousePos;
    private Vector2 rotVelocity; // Direction and current speed
    private Camera cam;

    void Start()
    {
        cam = Camera.main;
        // Initial random direction at base speed
        rotVelocity = Random.insideUnitCircle.normalized * baseRotationSpeed;
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))      OnDown();
        else if (Input.GetMouseButton(0))
        {
            if (selectedNode != null) DragNode();
            else                      RotateGlobe();
        }
        else if (Input.GetMouseButtonUp(0))
        {
            if (selectedNode != null) OnUp();
        }
        
        ApplyGlobeRotation();
        lastMousePos = Input.mousePosition;
    }

    void OnDown()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.SphereCast(ray, selectionRadius, out RaycastHit hit))
        {
            NodeView node = hit.collider.GetComponent<NodeView>();
            if (node != null && !node.isNeutral)
            {
                // Verify the node is on the front half of the sphere relative to the camera
                Vector3 globeToNode = (node.transform.position - globe.position).normalized;
                Vector3 globeToCam  = (cam.transform.position - globe.position).normalized;
                
                // If dot product is negative, the node is on the far side
                if (Vector3.Dot(globeToNode, globeToCam) < 0) return;

                selectedNode = node;
                selectedNode.originalLocalPos = node.transform.localPosition;
                node.SetSelected(true);
                return;
            }
        }
    }

    void DragNode()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Vector3 center = globe.position;
        Vector3 L  = center - ray.origin;
        float tca  = Vector3.Dot(L, ray.direction);
        if (tca < 0) return;

        float d2 = Vector3.Dot(L, L) - tca * tca;
        Vector3 hitPoint;
        if (d2 > sphereRadius * sphereRadius)
        {
            hitPoint = center + (ray.origin + ray.direction * tca - center).normalized * sphereRadius;
        }
        else
        {
            float thc = Mathf.Sqrt(sphereRadius * sphereRadius - d2);
            hitPoint  = ray.origin + ray.direction * (tca - thc);
        }
        selectedNode.transform.position = hitPoint;
    }

    void RotateGlobe()
    {
        Vector3 delta = Input.mousePosition - lastMousePos;
        float dx = -delta.x * rotationSensitivity * 40f;
        float dy =  delta.y * rotationSensitivity * 40f;

        if (delta.sqrMagnitude > 0.01f)
        {
            rotVelocity = new Vector2(dx, dy);
        }
    }

    void ApplyGlobeRotation()
    {
        // Apply current velocity
        globe.Rotate(Vector3.up,    rotVelocity.x * Time.deltaTime, Space.World);
        globe.Rotate(Vector3.right, rotVelocity.y * Time.deltaTime, Space.World);

        // If not dragging, return magnitude to base speed while keeping the direction
        if (!Input.GetMouseButton(0) || selectedNode != null)
        {
            float currentSpeed = rotVelocity.magnitude;
            if (currentSpeed > 0.001f)
            {
                float newSpeed = Mathf.MoveTowards(currentSpeed, baseRotationSpeed, Time.deltaTime * inertiaDamping * 50f);
                rotVelocity = rotVelocity.normalized * newSpeed;
            }
            else
            {
                // Fallback if it somehow stopped
                rotVelocity = Vector2.right * baseRotationSpeed;
            }
        }
    }

    void OnUp()
    {
        selectedNode.SetSelected(false);
        MatchManager.Instance.TryMerge(selectedNode);
        selectedNode = null;
    }
}
