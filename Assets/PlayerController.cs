using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpSpeed = 10f;
    [SerializeField] private float gravity = -9.81f;

    private CharacterController characterController;
    private float verticalVelocity;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    private void Update()
    {
        bool grounded = characterController.isGrounded;
        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -1f;
        }

        if (grounded && IsJumpPressed())
        {
            verticalVelocity = jumpSpeed;
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector2 input = GetMovementInput();
        Vector3 motion = (CameraRight() * input.x + CameraForward() * input.y) * moveSpeed;
        motion.y = verticalVelocity;

        characterController.Move(motion * Time.deltaTime);
    }

    // Space makes the player jump, but only when standing on the ground.
    private static bool IsJumpPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
    }

    // W always moves the player away from the camera, A/S/D are relative to the
    // camera's facing, so movement follows where the camera is looking.
    private static Vector3 CameraForward()
    {
        if (Camera.main != null)
        {
            Vector3 forward = Camera.main.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
            {
                return forward.normalized;
            }
        }

        return Vector3.forward;
    }

    private static Vector3 CameraRight()
    {
        if (Camera.main != null)
        {
            Vector3 right = Camera.main.transform.right;
            right.y = 0f;
            if (right.sqrMagnitude > 0.0001f)
            {
                return right.normalized;
            }
        }

        return Vector3.right;
    }

    private static Vector2 GetMovementInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return Vector2.zero;
        }

        Vector2 input = Vector2.zero;
        if (keyboard.wKey.isPressed) input.y += 1f;
        if (keyboard.sKey.isPressed) input.y -= 1f;
        if (keyboard.aKey.isPressed) input.x -= 1f;
        if (keyboard.dKey.isPressed) input.x += 1f;
        return input;
    }
}
