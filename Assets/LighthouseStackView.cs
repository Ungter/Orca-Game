using System.Collections.Generic;
using UnityEngine;

// A scrolling window of the tower keeps endless runs bounded in memory and world coordinates.
public sealed class LighthouseStackView : System.IDisposable
{
    public const int VisibleSections = 14;
    public const float SectionHeight = 1.6f;
    private sealed class Section { public GameObject Body; public int Level; }
    private readonly List<Section> sections = new List<Section>();
    private readonly GameObject root;
    private readonly GameObject sectionPrefab;
    private readonly GameObject moving;
    private readonly GameObject lantern;
    private readonly GameObject offcut;
    private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
    private int shownScore;
    private float cutTime = 2f;
    private float fallTime;
    private Vector3 cutStart;
    private Vector3 cutDirection;
    private Vector2 lastCutSize;
    public int SectionCount => sections.Count;
    public Transform Moving => moving.transform;
    public Transform Root => root.transform;

    public LighthouseStackView(Transform owner, Vector3 origin)
    {
        sectionPrefab = Resources.Load<GameObject>("WorldModels/LighthouseSection");
        GameObject cap = Resources.Load<GameObject>("WorldModels/LighthouseLantern");
        if (sectionPrefab == null || cap == null) throw new System.InvalidOperationException("Missing lighthouse stacking prefabs.");
        root = new GameObject("Lighthouse stacking tower");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, owner.gameObject.scene);
        root.transform.position = origin;
        moving = Object.Instantiate(sectionPrefab, root.transform, false);
        moving.name = "Moving lighthouse section";
        lantern = Object.Instantiate(cap, moving.transform, false);
        lantern.transform.localPosition = Vector3.up * SectionHeight;
        offcut = Object.Instantiate(sectionPrefab, root.transform, false);
        offcut.name = "Falling overhang";
        offcut.SetActive(false);
        AddSection(0, Vector2.zero, Vector2.one * LighthouseStackMatch.StartingWidth);
    }

    private void AddSection(int level, Vector2 centre, Vector2 size)
    {
        GameObject part = Object.Instantiate(sectionPrefab, root.transform, false);
        part.name = "Stacked section " + level;
        SetPiece(part, centre, size, level * SectionHeight);
        Colour(part, level);
        sections.Add(new Section { Body = part, Level = level });
    }

    public void Refresh(LighthouseStackMatch match, float deltaTime)
    {
        if (match.Score != shownScore)
        {
            AddSection(match.Score, match.Centre, match.Size);
            shownScore = match.Score;
            if (sections.Count > VisibleSections)
            {
                Release(sections[0].Body);
                sections.RemoveAt(0);
            }
            int first = Mathf.Max(0, match.Score - VisibleSections + 1);
            foreach (Section section in sections)
            {
                Vector3 position = section.Body.transform.localPosition;
                position.y = (section.Level - first) * SectionHeight;
                section.Body.transform.localPosition = position;
            }
            if (match.CutSize.sqrMagnitude > 0f)
            {
                lastCutSize = match.CutSize;
                cutStart = new Vector3(match.CutCentre.x, LevelY(match.Score), match.CutCentre.y);
                cutDirection = new Vector3(match.CutCentre.x - match.Centre.x, 0f, match.CutCentre.y - match.Centre.y).normalized;
                cutTime = 0f;
                offcut.SetActive(true);
                Colour(offcut, match.Score);
            }
        }
        cutTime += Mathf.Max(0f, deltaTime);
        if (offcut.activeSelf)
        {
            SetPiece(offcut, new Vector2(cutStart.x, cutStart.z), lastCutSize, cutStart.y);
            offcut.transform.localPosition += cutDirection * (cutTime * 2f) - Vector3.up * (cutTime * cutTime * 8f);
            offcut.transform.localRotation = Quaternion.Euler(cutTime * 110f, 0f, cutTime * 65f);
            if (cutTime >= 1.2f) offcut.SetActive(false);
        }
        fallTime = match.Failed ? fallTime + Mathf.Max(0f, deltaTime) : 0f;
        SetPiece(moving, match.MovingCentre, match.Size,
            LevelY(match.Score + 1) - fallTime * fallTime * 8f);
        Colour(moving, match.Score + 1);
        // Do not squash the lantern vertically as the tower's footprint gets narrower.
        lantern.transform.localScale = new Vector3(1f, 1f, 1f);
    }

    private float LevelY(int level) => (level - Mathf.Max(0, shownScore - VisibleSections + 1)) * SectionHeight;

    private static void SetPiece(GameObject piece, Vector2 centre, Vector2 size, float y)
    {
        piece.transform.localPosition = new Vector3(centre.x, y, centre.y);
        piece.transform.localScale = new Vector3(size.x / LighthouseStackMatch.StartingWidth, 1f,
            size.y / LighthouseStackMatch.StartingWidth);
    }

    private void Colour(GameObject piece, int level)
    {
        Color colour = level % 3 == 2 ? new Color32(160, 46, 46, 255) : new Color32(235, 228, 207, 255);
        block.SetColor("_BaseColor", colour);
        foreach (Renderer renderer in piece.GetComponentsInChildren<Renderer>())
            if (!renderer.transform.IsChildOf(lantern.transform)) renderer.SetPropertyBlock(block);
    }

    public Vector3 CameraFocus(LighthouseStackMatch match) =>
        root.transform.position + new Vector3(match.Centre.x, Mathf.Max(4f, LevelY(match.Score + 1) + 2f), match.Centre.y);

    public void Dispose() => Release(root);

    private static void Release(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Object.Destroy(value);
        else Object.DestroyImmediate(value);
    }
}
