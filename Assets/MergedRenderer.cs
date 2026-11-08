using UnityEngine;

// Left by the map import (IslandLampLights.MergeFoliage) on each tree, shrub and hedge part
// whose mesh was merged into one per plant. The part keeps its collider; this points at the
// renderer that draws it now, so anything asking "does this collider cast a shadow?"
// (SpriteLightTint) still gets the right answer.
[DisallowMultipleComponent]
public class MergedRenderer : MonoBehaviour
{
    public Renderer Target;
}
