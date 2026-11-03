using UnityEngine;

// The lost wand leaks rising glints once it lies above ground, until the pickup hides it
// and all its children. While it is buried under a dig site the magic (glints and light)
// stays dark, so the site only lights up through its own sparkles.
[RequireComponent(typeof(PickupItem), typeof(GrassBillboard))]
public class WandMagic : MonoBehaviour
{
    private PickupItem pickup;
    private DigSparkles sparkles;

    private void Start()
    {
        pickup = GetComponent<PickupItem>();
        var effect = new GameObject("Leaking magic");
        effect.transform.SetParent(transform, false);
        sparkles = effect.AddComponent<DigSparkles>();
        sparkles.Configure(transform.position + Vector3.up * 0.45f, 0.4f,
            new Color(0.6f, 0.85f, 1f), 0.6f, 3f);
    }

    // Every frame: the dig site buries the wand in its own Start, which may run after this one.
    private void Update()
    {
        if (sparkles != null) sparkles.Visible = pickup == null || !pickup.IsBuried;
    }
}
