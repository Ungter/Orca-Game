using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class GrassBillboard : MonoBehaviour
{
    [SerializeField] private Transform target;

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
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        if (Camera.main != null)
        {
            // Rotates the object to face the main camera continuously
            transform.LookAt(Camera.main.transform);
            transform.rotation = Quaternion.Euler(0, Camera.main.transform.eulerAngles.y, 0);
        }
    }
}
