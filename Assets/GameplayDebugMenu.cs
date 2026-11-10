using UnityEngine;
using UnityEngine.InputSystem;

// Runtime tools, owned by the player and reset with the scene. Never saves progress to disk.
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public sealed class GameplayDebugMenu : MonoBehaviour
{
    public enum Minigame { PotionHunt, Hats, TwentyOne, Digging, Anagram, Lighthouse }
    private static readonly string[] Names =
        { "Potion hunt", "Panther hats", "21 game", "Digging skill checks", "Dolphin anagram", "Lighthouse stacker" };
    private static GameplayDebugMenu active;
    private static int closingFrame = -1;
    public static bool IsOpen => active != null;
    public static bool BlocksInput => IsOpen || (Application.isPlaying && closingFrame == Time.frameCount);
    public string Status { get; private set; } = "Choose a minigame to finish.";
    private PlayerController player;
    private CursorLockMode cursorLock;
    private bool cursorVisible;
    private Vector2 scroll;
    private GUIStyle heading;
    private GUIStyle body;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { active = null; closingFrame = -1; }
    private void Awake() => player = GetComponent<PlayerController>();

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.backquoteKey.wasPressedThisFrame) SetOpen(active != this);
        else if (active == this && keyboard.escapeKey.wasPressedThisFrame) SetOpen(false);
    }

    public void SetOpen(bool open)
    {
        if (open)
        {
            if (active != null || !isActiveAndEnabled) return;
            cursorLock = Cursor.lockState;
            cursorVisible = Cursor.visible;
            active = this;
            ShowCursor();
        }
        else if (active == this)
        {
            active = null;
            closingFrame = Time.frameCount;
            RestoreCursor();
        }
    }

    public bool SetSpeedHack(bool on)
    {
        if (player == null) player = GetComponent<PlayerController>();
        if (player == null || !player.isActiveAndEnabled || TitleScreen.IsOpen) return false;
        player.SetDebugFly(on);
        return true;
    }

    public bool CompleteMinigame(Minigame choice)
    {
        if (!isActiveAndEnabled || TitleScreen.IsOpen || PauseMenu.IsPaused) return false;
        // A completed game may restore its own cursor. Keep that as the new return state,
        // rather than restoring the cursor belonging to a game that has now closed.
        bool open = active == this;
        if (open) RestoreCursor();
        bool completed = false;
        switch (choice)
        {
            case Minigame.PotionHunt:
                AlchemistEncounter alchemist = FindInScene<AlchemistEncounter>();
                ClueMinigame hunt = FindInScene<ClueMinigame>();
                completed = alchemist != null ? alchemist.DebugCompleteHunt() : hunt != null && hunt.DebugComplete();
                break;
            case Minigame.Hats:
                MagicianEncounter magician = FindInScene<MagicianEncounter>();
                ShellMinigame hats = FindInScene<ShellMinigame>();
                completed = magician != null ? magician.DebugCompleteTrick() : hats != null && hats.DebugComplete();
                break;
            case Minigame.TwentyOne:
                TwentyOneGame twentyOne = FindInScene<TwentyOneGame>();
                completed = twentyOne != null && twentyOne.DebugComplete();
                break;
            case Minigame.Digging:
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                    foreach (DigSite dig in root.GetComponentsInChildren<DigSite>(true))
                    { dig.DebugComplete(); completed = true; }
                break;
            case Minigame.Anagram:
                DolphinWordPuzzle puzzle = FindInScene<DolphinWordPuzzle>();
                if (puzzle != null) { puzzle.DebugComplete(); completed = true; }
                break;
            case Minigame.Lighthouse:
                LighthouseStacker lighthouse = FindInScene<LighthouseStacker>();
                if (lighthouse != null) { lighthouse.DebugComplete(); completed = true; }
                break;
        }
        if (open)
        {
            cursorLock = Cursor.lockState;
            cursorVisible = Cursor.visible;
            ShowCursor();
        }
        Status = completed ? Names[(int)choice] + " completed." : "That minigame is not available in this scene.";
        return completed;
    }

    private T FindInScene<T>() where T : Component
    {
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);
            if (found != null) return found;
        }
        return null;
    }

    public Rect WindowRect
    {
        get
        {
            float width = Mathf.Min(500f, Screen.width - 20f);
            float height = Mathf.Min(570f, Screen.height - 20f);
            return new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        }
    }

    private void OnGUI()
    {
        if (active != this) return;
        int depth = GUI.depth;
        GUI.depth = -10000;
        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none);
        GUI.Window(GetEntityId().GetHashCode(), WindowRect, DrawWindow, "DEBUG TOOLS");
        GUI.depth = depth;
    }

    private void DrawWindow(int id)
    {
        heading ??= new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
        body ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Space(8f);
        GUILayout.Label("[`] TOGGLE WINDOW    [ESC] CLOSE", heading);
        GUILayout.Space(12f);
        bool enabledBefore = GUI.enabled;
        GUI.enabled = player != null && player.isActiveAndEnabled && !TitleScreen.IsOpen;
        bool speed = player != null && player.DebugFlyEnabled;
        bool nextSpeed = GUILayout.Toggle(speed, " Speed hack: 5x movement + noclip", GUILayout.Height(30f));
        if (nextSpeed != speed) SetSpeedHack(nextSpeed);
        GUI.enabled = enabledBefore;
        GUILayout.Label("WASD move, Shift up, Ctrl down. Stays on when the window closes.", body);
        GUILayout.Space(16f);
        GUILayout.Label("FINISH MINIGAMES", heading);
        GUILayout.Label("Works before starting or during a minigame.");
        GUI.enabled = !TitleScreen.IsOpen && !PauseMenu.IsPaused;
        for (int i = 0; i < Names.Length; i++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Names[i], GUILayout.Height(32f));
            if (GUILayout.Button("Finish", GUILayout.Width(100f), GUILayout.Height(30f))) CompleteMinigame((Minigame)i);
            GUILayout.EndHorizontal();
        }
        GUI.enabled = enabledBefore;
        GUILayout.Space(8f);
        GUILayout.Label(TitleScreen.IsOpen ? "Start the game to use these tools." : Status, body);
        if (GUILayout.Button("Close", GUILayout.Height(34f))) SetOpen(false);
        GUILayout.EndScrollView();
    }

    private static void ShowCursor() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    private void RestoreCursor() { Cursor.lockState = cursorLock; Cursor.visible = cursorVisible; }
    private void OnDisable()
    {
        SetOpen(false);
        if (player != null) player.SetDebugFly(false);
    }
}
