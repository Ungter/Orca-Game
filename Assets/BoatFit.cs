using UnityEngine;

// Fit the supplied boat to the visible player, not just the much smaller controller capsule.
public sealed class BoatFit
{
    public Vector3 RidePoint { get; private set; }
    public float Length { get; private set; }
    public float HullDepth { get; private set; }
    public float RidingWidth { get; private set; }

    public static BoatFit Fit(GameObject boat, PlayerController player)
    {
        Renderer bench = RidingBench(boat);
        if (bench == null) throw new System.InvalidOperationException("The boat is missing its aft bench.");
        Bounds seat = LocalBounds(boat.transform, bench);
        float required = PlayerWidth(player) + 0.6f;
        float scale = Mathf.Max(1f, required / (seat.size.x * Mathf.Abs(boat.transform.lossyScale.x)));
        boat.transform.localScale *= scale;
        BoxCollider hull = boat.GetComponent<BoxCollider>();
        return new BoatFit
        {
            RidePoint = new Vector3(seat.center.x, seat.max.y, seat.center.z),
            Length = hull.size.z * Mathf.Abs(boat.transform.lossyScale.z),
            HullDepth = hull.size.y * Mathf.Abs(boat.transform.lossyScale.y),
            RidingWidth = seat.size.x * Mathf.Abs(boat.transform.lossyScale.x)
        };
    }

    public static Renderer RidingBench(GameObject boat)
    {
        Renderer aft = null;
        foreach (Renderer part in boat.GetComponentsInChildren<Renderer>())
        {
            if (part.name != "pCube3" && part.name != "pCube4") continue;
            if (aft == null || LocalBounds(boat.transform, part).center.z < LocalBounds(boat.transform, aft).center.z)
                aft = part;
        }
        return aft;
    }

    public static float PlayerWidth(PlayerController player)
    {
        var body = player.GetComponent<CharacterController>();
        float width = body.radius * 2f * Mathf.Max(Mathf.Abs(player.transform.lossyScale.x), Mathf.Abs(player.transform.lossyScale.z));
        SpriteRenderer sprite = player.GetComponent<SpriteRenderer>();
        if (sprite != null && sprite.sprite != null)
            width = Mathf.Max(width, sprite.sprite.bounds.size.x * Mathf.Abs(sprite.transform.lossyScale.x));
        return width;
    }

    public static Bounds LocalBounds(Transform root, Renderer renderer)
    {
        Bounds local = renderer.localBounds;
        Matrix4x4 matrix = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
        Bounds result = new Bounds(matrix.MultiplyPoint3x4(local.min), Vector3.zero);
        for (int i = 0; i < 8; i++)
            result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(
                (i & 1) == 0 ? local.min.x : local.max.x,
                (i & 2) == 0 ? local.min.y : local.max.y,
                (i & 4) == 0 ? local.min.z : local.max.z)));
        return result;
    }
}
