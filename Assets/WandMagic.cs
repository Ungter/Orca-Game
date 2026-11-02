using UnityEngine;

// The lost wand leaks rising glints until the pickup hides it and all its children.
[RequireComponent(typeof(PickupItem), typeof(GrassBillboard))]
public class WandMagic : MonoBehaviour
{
    private void Start()
    {
        var effect = new GameObject("Leaking magic");
        effect.transform.SetParent(transform, false);
        var sparkles = effect.AddComponent<DigSparkles>();
        sparkles.Configure(transform.position + Vector3.up * 0.45f, 0.4f,
            new Color(0.6f, 0.85f, 1f), 0.6f, 3f);
        sparkles.Visible = true;
    }
}
