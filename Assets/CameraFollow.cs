using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 4f, -6f);
    [SerializeField] private Vector3 lookAtHeight = new Vector3(0f, 1f, 0f);
    [SerializeField] private float rotationSpeed = 90f;
    [SerializeField] private float collisionRadius = 0.2f;
    [SerializeField] private float wallPadding = 0.1f;
    [SerializeField] private float zoomOutSpeed = 8f;

    // Distance around the player in the horizontal plane, derived from the offset.
    private float radius;
    // Current horizontal angle in degrees around the player. 0 = +Z, 90 = +X.
    private float cameraAngle;
    private float currentDistance;

    private void Awake()
    {
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                player = GameObject.Find("Player");
            }

            if (player != null)
            {
                target = player.transform;
            }
        }

        radius = new Vector2(offset.x, offset.z).magnitude;
        cameraAngle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        currentDistance = (offset - lookAtHeight).magnitude;
    }

    private void LateUpdate()
    {
        if (target == null || InventoryInspection.IsOpen)
        {
            return;
        }

        // J rotates the camera clockwise around the player, L counter-clockwise.
        // Viewed from above, decreasing the angle moves clockwise.
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.jKey.isPressed) cameraAngle -= rotationSpeed * Time.deltaTime;
            if (keyboard.lKey.isPressed) cameraAngle += rotationSpeed * Time.deltaTime;
        }

        float radians = cameraAngle * Mathf.Deg2Rad;
        Vector3 horizontal = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * radius;
        Vector3 focus = target.position + lookAtHeight;
        Vector3 desiredPosition = target.position + horizontal + Vector3.up * offset.y;
        Vector3 toCamera = desiredPosition - focus;
        float desiredDistance = toCamera.magnitude;
        if (desiredDistance <= 0f)
        {
            return;
        }

        Vector3 direction = toCamera / desiredDistance;
        float clearDistance = desiredDistance;
        foreach (RaycastHit hit in Physics.SphereCastAll(
                     focus, collisionRadius, direction, desiredDistance,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(target))
            {
                continue;
            }

            clearDistance = Mathf.Min(clearDistance, Mathf.Max(0.01f, hit.distance - wallPadding));
        }

        // Move in immediately when a wall blocks the view; ease back out once clear.
        currentDistance = clearDistance < currentDistance
            ? clearDistance
            : Mathf.MoveTowards(currentDistance, clearDistance, zoomOutSpeed * Time.deltaTime);
        transform.position = focus + direction * currentDistance;
        transform.LookAt(focus);
    }
}
