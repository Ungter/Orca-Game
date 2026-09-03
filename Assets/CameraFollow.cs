using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 4f, -6f);
    [SerializeField] private Vector3 lookAtHeight = new Vector3(0f, 1f, 0f);
    [SerializeField] private float rotationSpeed = 90f;

    // Distance around the player in the horizontal plane, derived from the offset.
    private float radius;
    // Current horizontal angle in degrees around the player. 0 = +Z, 90 = +X.
    private float cameraAngle;

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
    }

    private void LateUpdate()
    {
        if (target == null)
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

        // Keep the camera at the same distance and height above the player while
        // rotating around them, looking down at the player.
        transform.position = target.position + horizontal + Vector3.up * offset.y;
        transform.LookAt(target.position + lookAtHeight);
    }
}
