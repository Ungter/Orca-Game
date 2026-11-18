using UnityEngine;
using UnityEngine.Serialization;

// The lighthouse island's landmarks: its tower, the spot at the tower's foot, and the ferry's
// landing and berth at its pier. The island (and the ferry to it) opens once the Orca statue's
// anagram is solved.
public sealed class LighthouseIsland : MonoBehaviour
{
    [SerializeField, FormerlySerializedAs("originalTower")] private Transform tower;
    [SerializeField] private Transform entrance;
    [SerializeField] private Transform pierLanding;
    [SerializeField] private Transform ferryBerth;
    private DolphinWordPuzzle anagram;

    public Transform Tower => tower;
    public Vector3 PierLanding => pierLanding.position;
    public Vector3 Berth => ferryBerth.position;
    public Vector3 Entrance => entrance.position;

    public bool IsUnlocked
    {
        get
        {
            if (anagram == null)
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    anagram = root.GetComponentInChildren<DolphinWordPuzzle>(true);
                    if (anagram != null) break;
                }
            return anagram != null && anagram.IsSolved;
        }
    }

    public void Configure(Transform tower, Transform entry, Transform pier, Transform berth)
    {
        this.tower = tower;
        entrance = entry;
        pierLanding = pier;
        ferryBerth = berth;
    }
}
