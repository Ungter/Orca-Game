using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpSpeed = 10f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Sprites")]
    [SerializeField] private Sprite frontIdle;
    [SerializeField] private Sprite backIdle;
    [SerializeField] private Sprite[] frontWalk;
    [SerializeField] private Sprite[] backWalk;
    [SerializeField] private float walkFrameRate = 6f;
    [SerializeField] private Renderer shadowCaster;
    [SerializeField] private Light shadowLight;
    [Tooltip("Narrows the shadow; a flat billboard casts its full front width even when lit from the side.")]
    [SerializeField, Range(0.1f, 1f)] private float shadowWidthScale = 0.1f;
    [Tooltip("Raises the shadow quad so shadow bias doesn't push the feet into the ground.")]
    [SerializeField] private float shadowLift = 0.25f;

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");

    private CharacterController characterController;
    private float verticalVelocity;
    private SpriteRenderer spriteRenderer;
    private bool facingBack;
    private float walkTimer;
    private MaterialPropertyBlock shadowBlock;
    private Vector3 shadowBaseScale;
    private Vector3 shadowBasePosition;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (shadowLight == null)
        {
            shadowLight = FindDirectionalLight();
        }

        if (shadowCaster != null)
        {
            shadowBaseScale = shadowCaster.transform.localScale;
            shadowBasePosition = shadowCaster.transform.localPosition;
        }

        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            UpdateShadowCaster(spriteRenderer.sprite);
        }
    }

    private void Update()
    {
        if (InventoryInspection.IsOpen)
        {
            UpdateSprite(Vector2.zero);
            return;
        }

        if (Camera.main != null)
        {
            // Rotates the object to face the main camera continuously
            transform.LookAt(Camera.main.transform);
            transform.rotation = Quaternion.Euler(0, Camera.main.transform.eulerAngles.y, 0);
        }
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
        UpdateSprite(input);
    }

    // W (and W diagonals) shows the back walk, S (and S diagonals) the front walk.
    // Pure A/D keeps the last facing; standing still shows that facing's idle.
    private void UpdateSprite(Vector2 input)
    {
        if (spriteRenderer == null)
        {
            return;
        }

        if (input.y > 0f) facingBack = true;
        else if (input.y < 0f) facingBack = false;

        Sprite[] walk = facingBack ? backWalk : frontWalk;
        Sprite next;
        if (input != Vector2.zero && walk != null && walk.Length > 0)
        {
            walkTimer += Time.deltaTime * walkFrameRate;
            next = walk[(int)walkTimer % walk.Length];
        }
        else
        {
            walkTimer = 0f;
            next = facingBack ? backIdle : frontIdle;
        }

        if (next == null || next == spriteRenderer.sprite)
        {
            return;
        }

        spriteRenderer.sprite = next;
        UpdateShadowCaster(next);
    }

    // The shadow quad is shadows-only; point its texture at the current sprite's
    // frame so the cast shadow matches the animation.
    private void UpdateShadowCaster(Sprite sprite)
    {
        if (shadowCaster == null)
        {
            return;
        }

        Texture2D tex = sprite.texture;
        Rect r = sprite.textureRect;
        Vector4 st = new Vector4(r.width / tex.width, r.height / tex.height, r.x / tex.width, r.y / tex.height);

        shadowBlock ??= new MaterialPropertyBlock();
        shadowCaster.GetPropertyBlock(shadowBlock);
        shadowBlock.SetTexture(BaseMapId, tex);
        shadowBlock.SetVector(BaseMapStId, st);
        shadowBlock.SetTexture(MainTexId, tex);
        shadowBlock.SetVector(MainTexStId, st);
        shadowCaster.SetPropertyBlock(shadowBlock);
    }

    // The sprite billboards toward the camera, but the shadow quad faces the light
    // so its shadow keeps the sprite's true width from every camera angle.
    private void LateUpdate()
    {
        if (shadowCaster == null)
        {
            return;
        }

        Transform quad = shadowCaster.transform;
        quad.localScale = new Vector3(shadowBaseScale.x * shadowWidthScale, shadowBaseScale.y, shadowBaseScale.z);
        quad.localPosition = shadowBasePosition + Vector3.up * shadowLift;

        if (shadowLight == null)
        {
            return;
        }

        Vector3 lightDir = shadowLight.transform.forward;
        lightDir.y = 0f;
        if (lightDir.sqrMagnitude > 0.0001f)
        {
            shadowCaster.transform.rotation = Quaternion.LookRotation(lightDir.normalized, Vector3.up);
        }
    }

    private static Light FindDirectionalLight()
    {
        if (RenderSettings.sun != null)
        {
            return RenderSettings.sun;
        }

        foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type == LightType.Directional)
            {
                return light;
            }
        }

        return null;
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
