using System.Collections.Generic;
using UnityEngine;

// Something in the world the player walks up to and presses E on.
public interface IInteractable
{
    // Squared ground distance from the player while it is in range and usable; float.MaxValue otherwise.
    float InteractDistanceSqr();
}

// Every enabled interactable, so only the one nearest the player shows its prompt and
// answers E: one press never picks up, talks and digs at once.
public static class Interactables
{
    private static readonly List<IInteractable> All = new List<IInteractable>();

    // Something has taken over the player's input: a conversation or a dig.
    public static bool Busy => TalkInteractable.IsTalking || DigSite.IsDigging || DolphinWordPuzzle.IsOpen;

    public static void Add(IInteractable interactable)
    {
        if (!All.Contains(interactable))
        {
            All.Add(interactable);
        }
    }

    public static void Remove(IInteractable interactable)
    {
        All.Remove(interactable);
    }

    public static bool IsNearest(IInteractable interactable)
    {
        IInteractable nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (IInteractable candidate in All)
        {
            float distance = candidate.InteractDistanceSqr();
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
        }
        return nearest != null && ReferenceEquals(nearest, interactable);
    }

    public static Transform FindPlayer()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject == null)
        {
            playerObject = GameObject.Find("Player");
        }
        return playerObject != null ? playerObject.transform : null;
    }

    // Top-centre of everything the object and its children render. Groups like graveStone1
    // keep their meshes on children, so the object's own pivot says little about where it is.
    public static Vector3 TopCentre(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return root.transform.position + Vector3.up;
        }
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
    }
}
