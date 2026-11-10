using UnityEngine;
using UnityEngine.InputSystem;

// Discovered clue words supply the letter bank. Drag tiles between the bank and answer slots.
[DefaultExecutionOrder(-50)]
public class DolphinWordPuzzle : MonoBehaviour, IInteractable
{
    private const string Answer = "JEFFTHEKILLER";
    private static readonly string[] ClueWords = { "FELL", "JERK", "THIEF" };
    private const int PanelWidth = 292;
    private const int PanelHeight = 194;
    private const int TileSize = 18;
    private const int TileStep = 20;
    private const int TileStart = 11;
    private const int GroupGap = 6;
    private const int SlotY = 84;
    private const int BankY = 130;

    private static readonly Color Frame = new Color32(26, 15, 10, 255);
    private static readonly Color Wood = new Color32(92, 58, 33, 255);
    private static readonly Color Well = new Color32(36, 25, 19, 255);
    private static readonly Color Paper = new Color32(233, 214, 168, 255);
    private static readonly Color Gold = new Color32(242, 211, 107, 255);
    private static readonly Color Muted = new Color32(132, 113, 87, 255);

    public static bool IsOpen { get; private set; }
    public bool IsSolved { get; private set; }
    public bool IsDragging => dragged >= 0;
    public string ArrangedLetters => new string(slots).Replace('\0', '_');
    public string BankLetters => new string(bank).Replace('\0', '_');
    public int AvailableLetterCount { get; private set; }

    [SerializeField] private float interactRadius = 2.5f;
    [SerializeField] private float hintOffsetY = 10f;
    [SerializeField] private int promptPixelSize = 3;
    [SerializeField] private int uiPixelSize = 3;

    private readonly char[] bank = new char[Answer.Length];
    private readonly char[] slots = new char[Answer.Length];
    private readonly bool[] revealed = new bool[ClueWords.Length];
    private Transform player;
    private Bounds bounds;
    private float promptAlpha;
    private int dragged = -1;
    private bool draggedSlot;
    private Vector2 pointer;
    private Vector2 grabOffset = Vector2.one * (TileSize * 0.5f);

    private void Awake()
    {
        player = Interactables.FindPlayer();
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            bounds = new Bounds(transform.position, Vector3.zero);
        }
        else
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        }
    }

    private void OnEnable() => Interactables.Add(this);

    private void OnDisable()
    {
        Interactables.Remove(this);
        Close();
    }

    private void Update()
    {
        if (GameplayDebugMenu.BlocksInput) return;
        if (PauseMenu.IsPaused) return;
        Keyboard keyboard = Keyboard.current;
        if (IsOpen)
        {
            promptAlpha = 0f;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }
            RefreshClues();
            Mouse mouse = Mouse.current;
            if (mouse == null) { CancelDrag(); return; }
            int p = PixelSize();
            Vector2 screen = mouse.position.ReadValue();
            Vector2 point = (new Vector2(screen.x, Screen.height - screen.y) - PanelOrigin(p)) / p;
            HandlePointer(point, mouse.leftButton.wasPressedThisFrame, mouse.leftButton.wasReleasedThisFrame);
            if (IsDragging && !mouse.leftButton.isPressed && !mouse.leftButton.wasPressedThisFrame) CancelDrag();
            return;
        }

        bool active = !Interactables.Busy && !InventoryInspection.IsOpen &&
            !TitleScreen.IsOpen && Interactables.IsNearest(this);
        promptAlpha = Mathf.MoveTowards(promptAlpha, active ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        if (active && keyboard != null && keyboard.eKey.wasPressedThisFrame) Begin();
    }

    public float InteractDistanceSqr()
    {
        if (player == null || !isActiveAndEnabled) return float.MaxValue;
        Vector3 nearest = bounds.ClosestPoint(player.position);
        Vector3 delta = player.position - nearest;
        float sqr = delta.x * delta.x + delta.z * delta.z;
        return sqr <= interactRadius * interactRadius ? sqr : float.MaxValue;
    }

    public static Vector3 GroundPromptAnchor(Bounds renderedBounds, Vector3 playerPosition)
    {
        Vector3 nearest = renderedBounds.ClosestPoint(playerPosition);
        return new Vector3(nearest.x, Mathf.Min(renderedBounds.max.y, renderedBounds.min.y + 2.5f), nearest.z);
    }

    public void Begin()
    {
        if (IsOpen || Interactables.Busy || InventoryInspection.IsOpen || PauseMenu.IsPaused || TitleScreen.IsOpen) return;
        RefreshClues();
        CancelDrag();
        IsOpen = true;
    }

    public void Close()
    {
        CancelDrag();
        IsOpen = false;
    }

    public void DebugComplete()
    {
        for (int i = 0; i < Answer.Length; i++) { slots[i] = Answer[i]; bank[i] = '\0'; }
        for (int i = 0; i < revealed.Length; i++) revealed[i] = true;
        AvailableLetterCount = Answer.Length;
        IsSolved = true;
        Close();
    }

    private void RefreshClues()
    {
        InventorySystem inventory = InventorySystem.instance;
        if (inventory == null) return;
        int start = 0;
        for (int word = 0; word < ClueWords.Length; word++)
        {
            string text = ClueWords[word];
            if (!revealed[word] && inventory.HasRevealedWord(text))
            {
                revealed[word] = true;
                for (int i = 0; i < text.Length; i++) bank[start + i] = text[i];
                AvailableLetterCount += text.Length;
            }
            start += text.Length;
        }
    }

    private bool Usable(bool isSlot, int index)
    {
        if (index < 0 || index >= Answer.Length) return false;
        if (isSlot) return true;
        int start = 0;
        for (int word = 0; word < ClueWords.Length; word++)
        {
            start += ClueWords[word].Length;
            if (index < start) return revealed[word];
        }
        return false;
    }

    public bool BeginDrag(bool isSlot, int index)
    {
        if (!IsOpen || IsSolved || IsDragging || !Usable(isSlot, index)) return false;
        if ((isSlot ? slots : bank)[index] == '\0') return false;
        dragged = index;
        draggedSlot = isSlot;
        grabOffset = Vector2.one * (TileSize * 0.5f);
        return true;
    }

    public bool DropTile(bool isSlot, int index)
    {
        if (!IsOpen || IsSolved || !IsDragging) return false;
        if (!Usable(isSlot, index)) { CancelDrag(); return false; }
        char[] source = draggedSlot ? slots : bank;
        char[] destination = isSlot ? slots : bank;
        char letter = source[dragged];
        source[dragged] = destination[index];
        destination[index] = letter;
        CancelDrag();
        for (int i = 0; i < Answer.Length; i++)
            if (slots[i] != Answer[i]) return true;
        IsSolved = true;
        return true;
    }

    public void CancelDrag() => dragged = -1;

    // Panel-space coordinates keep dragging independent of screen resolution and UI scale.
    public void HandlePointer(Vector2 point, bool pressed, bool released)
    {
        pointer = point;
        if (!IsOpen || IsSolved) return;
        bool hit = TileAt(point, out bool isSlot, out int index);
        if (pressed && hit && BeginDrag(isSlot, index))
            grabOffset = point - TileRect(index, isSlot).position;
        if (!released || !IsDragging) return;
        if (hit) DropTile(isSlot, index);
        else CancelDrag();
    }

    private bool TileAt(Vector2 point, out bool isSlot, out int index)
    {
        for (int i = 0; i < Answer.Length; i++)
        {
            if (TileRect(i, true).Contains(point)) { isSlot = true; index = i; return true; }
            if (Usable(false, i) && TileRect(i, false).Contains(point)) { isSlot = false; index = i; return true; }
        }
        isSlot = false;
        index = -1;
        return false;
    }

    private static Rect TileRect(int index, bool isSlot)
    {
        int extra = (index >= 4 ? GroupGap : 0) + (index >= (isSlot ? 7 : 8) ? GroupGap : 0);
        return new Rect(TileStart + index * TileStep + extra, isSlot ? SlotY : BankY, TileSize, TileSize);
    }

    private int PixelSize()
    {
        int desired = Mathf.Max(1, Mathf.RoundToInt(uiPixelSize * InteractPrompt.UIScale()));
        return Mathf.Max(1, Mathf.Min(desired, Screen.width / PanelWidth, Screen.height / PanelHeight));
    }

    private static Vector2 PanelOrigin(int p) =>
        new Vector2(Mathf.RoundToInt((Screen.width - PanelWidth * p) * 0.5f),
            Mathf.RoundToInt((Screen.height - PanelHeight * p) * 0.5f));

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (!IsOpen)
        {
            InteractPrompt.Draw(GroundPromptAnchor(bounds, player != null ? player.position : transform.position),
                hintOffsetY, "E", "Arrange", promptPixelSize, promptAlpha);
            return;
        }

        int p = PixelSize();
        Matrix4x4 previous = GUI.matrix;
        int previousDepth = GUI.depth;
        GUI.depth = -1000;
        Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
        GUI.matrix = previous * Matrix4x4.TRS(PanelOrigin(p), Quaternion.identity, new Vector3(p, p, 1f));
        Fill(new Rect(0, 0, PanelWidth, PanelHeight), Frame);
        Fill(new Rect(2, 2, PanelWidth - 4, PanelHeight - 4), Wood);
        Fill(new Rect(4, 4, PanelWidth - 8, PanelHeight - 8), Well);
        DrawCentred(11, "DOLPHIN STATUE", Gold);
        DrawCentred(30, "REVEALED WORDS", Gold);
        string words = "";
        for (int i = 0; i < ClueWords.Length; i++)
            words += (i > 0 ? "     " : "") + (revealed[i] ? ClueWords[i] : "????");
        DrawCentred(47, words, Paper);
        DrawCentred(66, IsSolved ? "YOU WIN" : AvailableLetterCount == 0 ?
            "FIND CLUES TO UNLOCK TILES" : "DRAG LETTERS INTO THE ANSWER", IsSolved ? Gold : Paper);
        DrawCentred(114, "LETTER BANK", Gold);

        bool hovering = TileAt(pointer, out bool hoverSlot, out int hoverIndex);
        for (int i = 0; i < Answer.Length; i++)
        {
            bool slotSource = IsDragging && draggedSlot && dragged == i;
            bool bankSource = IsDragging && !draggedSlot && dragged == i;
            DrawTile(TileRect(i, true), slotSource ? '\0' : slots[i],
                IsDragging && hovering && hoverSlot && hoverIndex == i, IsSolved, false);
            DrawTile(TileRect(i, false), bankSource ? '\0' : bank[i],
                IsDragging && hovering && !hoverSlot && hoverIndex == i, false, !Usable(false, i));
        }
        DrawCentred(158, AvailableLetterCount < Answer.Length ? "MORE CLUES ADD MORE LETTERS" : "ALL CLUE LETTERS FOUND", Muted);
        DrawCentred(177, IsSolved ? "JEFF THE KILLER  [ESC] CLOSE" : "[DRAG] MOVE TILES    [ESC] CLOSE", Paper);
        if (IsDragging)
        {
            char letter = (draggedSlot ? slots : bank)[dragged];
            DrawTile(new Rect(pointer - grabOffset, Vector2.one * TileSize), letter, true, false, false);
        }
        GUI.matrix = previous;
        GUI.depth = previousDepth;
    }

    private static void DrawTile(Rect rect, char letter, bool highlighted, bool won, bool locked)
    {
        Fill(new Rect(rect.x + 1f, rect.y + 2f, rect.width, rect.height), Frame);
        Fill(rect, highlighted || won ? Gold : Wood);
        Fill(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 3f), locked || letter == '\0' ? Well : Paper);
        if (letter == '\0' && !locked) return;
        string text = locked ? "?" : letter.ToString();
        PixelFont.Draw(rect.x + Mathf.Floor((rect.width - PixelFont.Measure(text)) * 0.5f),
            rect.y + 6f, text, locked ? Muted : Frame);
    }

    private static void DrawCentred(int y, string text, Color color) =>
        PixelFont.Draw((PanelWidth - PixelFont.Measure(text)) * 0.5f, y, text, color);

    private static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
