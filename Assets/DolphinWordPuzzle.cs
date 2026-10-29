using UnityEngine;
using UnityEngine.InputSystem;

// The statue shows all three discovered words for now. Their letters form an anagram
// of the answer; later progression can reveal the clue words one at a time.
// Click a letter, then a slot (or another letter) to move or swap it.
[DefaultExecutionOrder(-50)]
public class DolphinWordPuzzle : MonoBehaviour, IInteractable
{
    private const string Answer = "JEFFTHEKILLER";
    private const string StartingLetters = "FELLJERKTHIEF";
    private const int PanelWidth = 292;
    private const int PanelHeight = 152;
    private const int TileSize = 18;
    private const int TileStep = 20;
    private const int TileStart = 11;
    private const int GroupGap = 6;
    private const int SlotY = 80;
    private const int BankY = 110;

    private static readonly Color Frame = new Color32(26, 15, 10, 255);
    private static readonly Color Wood = new Color32(92, 58, 33, 255);
    private static readonly Color Well = new Color32(36, 25, 19, 255);
    private static readonly Color Paper = new Color32(233, 214, 168, 255);
    private static readonly Color Gold = new Color32(242, 211, 107, 255);

    public static bool IsOpen { get; private set; }
    public bool IsSolved { get; private set; }
    public string ArrangedLetters => new string(slots).Replace('\0', '_');

    [SerializeField] private float interactRadius = 2.5f;
    [SerializeField] private float hintOffsetY = 10f;
    [SerializeField] private int promptPixelSize = 3;
    [SerializeField] private int uiPixelSize = 3;

    private char[] bank = StartingLetters.ToCharArray();
    private char[] slots = new char[Answer.Length];
    private Transform player;
    private Bounds bounds;
    private float promptAlpha;
    private int selected = -1;
    private bool selectedSlot;

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
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !IsSolved)
            {
                int p = PixelSize();
                Vector2 origin = PanelOrigin(p);
                Vector2 point = new Vector2(mouse.position.ReadValue().x,
                    Screen.height - mouse.position.ReadValue().y);
                point = (point - origin) / p;
                for (int i = 0; i < Answer.Length; i++)
                {
                    if (TileRect(i, SlotY, true).Contains(point)) { ChooseTile(true, i); return; }
                    if (TileRect(i, BankY, false).Contains(point)) { ChooseTile(false, i); return; }
                }
            }
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

    // The dolphin is tall enough that a prompt above its head is off-screen at walking
    // distance. Keep it near the base and on the side closest to the player instead.
    public static Vector3 GroundPromptAnchor(Bounds renderedBounds, Vector3 playerPosition)
    {
        Vector3 nearest = renderedBounds.ClosestPoint(playerPosition);
        return new Vector3(nearest.x, Mathf.Min(renderedBounds.max.y, renderedBounds.min.y + 2.5f), nearest.z);
    }

    public void Begin()
    {
        IsOpen = true;
        selected = -1;
    }

    public void Close()
    {
        IsOpen = false;
        selected = -1;
    }

    // Public so the tile rules can be tested without synthetic mouse events.
    public void ChooseTile(bool isSlot, int index)
    {
        if (!IsOpen || IsSolved || index < 0 || index >= Answer.Length) return;
        char[] destination = isSlot ? slots : bank;
        if (selected < 0)
        {
            if (destination[index] != '\0')
            {
                selected = index;
                selectedSlot = isSlot;
            }
            return;
        }
        if (selected == index && selectedSlot == isSlot)
        {
            selected = -1;
            return;
        }
        char[] source = selectedSlot ? slots : bank;
        char letter = source[selected];
        source[selected] = destination[index];
        destination[index] = letter;
        selected = -1;

        for (int i = 0; i < Answer.Length; i++)
        {
            if (slots[i] != Answer[i]) return;
        }
        IsSolved = true;
    }

    private static Rect TileRect(int index, int y, bool grouped)
    {
        int extra = grouped ? (index >= 4 ? GroupGap : 0) + (index >= 7 ? GroupGap : 0) : 0;
        int x = TileStart + index * TileStep + extra;
        return new Rect(x, y, TileSize, TileSize);
    }

    private int PixelSize()
    {
        int desired = Mathf.Max(1, Mathf.RoundToInt(uiPixelSize * InteractPrompt.UIScale()));
        return Mathf.Max(1, Mathf.Min(desired, Screen.width / PanelWidth, Screen.height / PanelHeight));
    }

    private static Vector2 PanelOrigin(int p)
    {
        return new Vector2(Mathf.RoundToInt((Screen.width - PanelWidth * p) * 0.5f),
            Mathf.RoundToInt((Screen.height - PanelHeight * p) * 0.5f));
    }

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
        GUI.matrix = previous * Matrix4x4.TRS(PanelOrigin(p), Quaternion.identity, new Vector3(p, p, 1f));
        Fill(new Rect(0, 0, PanelWidth, PanelHeight), Frame);
        Fill(new Rect(2, 2, PanelWidth - 4, PanelHeight - 4), Wood);
        Fill(new Rect(4, 4, PanelWidth - 8, PanelHeight - 8), Well);
        DrawCentred(11, "DOLPHIN STATUE", Gold);
        DrawCentred(30, "CLUES", Gold);
        DrawCentred(47, "FELL     JERK     THIEF", Paper);
        DrawCentred(67, IsSolved ? "YOU WIN" : "ARRANGE THE LETTERS", IsSolved ? Gold : Paper);

        for (int i = 0; i < Answer.Length; i++)
        {
            DrawTile(TileRect(i, SlotY, true), slots[i], selectedSlot && selected == i, IsSolved);
            DrawTile(TileRect(i, BankY, false), bank[i], !selectedSlot && selected == i, false);
        }
        DrawCentred(137, IsSolved ? "JEFF THE KILLER  [ESC] CLOSE" :
            "[CLICK] PICK / PLACE    [ESC] CLOSE", Paper);
        GUI.matrix = previous;
        GUI.depth = previousDepth;
    }

    private static void DrawTile(Rect rect, char letter, bool highlighted, bool won)
    {
        Fill(rect, highlighted || won ? Gold : Wood);
        Fill(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4), Well);
        if (letter != '\0')
        {
            string text = letter.ToString();
            PixelFont.Draw(rect.x + Mathf.Floor((rect.width - PixelFont.Measure(text)) * 0.5f),
                rect.y + 6, text, highlighted || won ? Gold : Paper);
        }
    }

    private static void DrawCentred(int y, string text, Color color)
    {
        PixelFont.Draw((PanelWidth - PixelFont.Measure(text)) * 0.5f, y, text, color);
    }

    private static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
