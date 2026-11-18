using UnityEngine;
using UnityEngine.InputSystem;

// A world object the player can talk to with E (e.g. Panther's gravestone). The conversation plays
// in the shared dialogue box over the game (DialogueRunner); Enter or a click moves it on. The
// player cannot move until it is over.
// Runs before the player and pickups so the press that starts a talk moves and picks up nothing.
[DefaultExecutionOrder(-50)]
public class TalkInteractable : MonoBehaviour, IInteractable
{
    public enum Phase { Idle, Typing, Waiting }

    public static bool IsTalking { get; private set; }

    // True once a conversation has played to the end at least once.
    public bool HasSpoken { get; private set; }

    [SerializeField]
    private float talkRadius = 2.5f;

    [SerializeField]
    private float hintOffsetY = 10f;

    [SerializeField]
    private string hintText = "E";

    [SerializeField]
    private string actionText = "Talk";

    [SerializeField]
    private int promptPixelSize = 3;

    [Header("Conversation")]
    // Panther plays his part of the story (StoryScript.PantherGrave); anyone else says line.
    [SerializeField]
    private Speaker speaker = Speaker.Panther;

    [SerializeField, TextArea]
    private string line = "Something is in the forest";

    private Transform player;
    private Vector3 anchorOffset;
    private float promptAlpha;
    private bool inputGuard;
    private readonly DialogueRunner runner = new DialogueRunner();

    public Phase CurrentPhase => !runner.IsActive ? Phase.Idle : runner.IsTyping ? Phase.Typing : Phase.Waiting;
    public DialogueRunner Runner => runner;
    public int VisibleCharacters => runner.VisibleCharacters;
    public string Text => runner.Text;

    public DialogueLine[] Lines => speaker == Speaker.Panther ? StoryScript.PantherGrave :
        new[] { DialogueLine.Say(speaker, line ?? "") };

    private void Awake()
    {
        player = Interactables.FindPlayer();
        anchorOffset = Interactables.TopCentre(gameObject) - transform.position;
    }

    private void OnEnable()
    {
        Interactables.Add(this);
    }

    private void OnDisable()
    {
        Interactables.Remove(this);
        Cancel();
    }

    private void Update()
    {
        if (PauseMenu.IsPaused || GameplayDebugMenu.BlocksInput)
        {
            // The click that resumes the game must not also move the conversation on.
            inputGuard = true;
            return;
        }

        if (runner.IsActive)
        {
            DialogueInput input = inputGuard ? DialogueInput.None : DialogueInput.Read();
            inputGuard = false;
            promptAlpha = 0f;
            if (input.Escape && runner.CurrentState == DialogueRunner.State.Waiting) Finish();
            else Tick(Time.unscaledDeltaTime, input);
            return;
        }
        inputGuard = false;

        bool blocked = Interactables.Busy || InventoryInspection.IsOpen || TitleScreen.IsOpen;
        bool active = !blocked && Interactables.IsNearest(this);
        promptAlpha = Mathf.MoveTowards(promptAlpha, active ? 1f : 0f, Time.unscaledDeltaTime * 8f);

        Keyboard keyboard = Keyboard.current;
        if (active && keyboard != null && keyboard.eKey.wasPressedThisFrame)
        {
            Begin();
        }
    }

    public void Begin()
    {
        if (IsTalking)
        {
            return;
        }
        IsTalking = true;
        runner.Play(speaker, Lines, Finish);
    }

    // Moves the conversation on. advance = Enter or a click this frame.
    public void Tick(float deltaTime, bool advance) => Tick(deltaTime, advance ? DialogueInput.Continue : DialogueInput.None);

    public void Tick(float deltaTime, DialogueInput input)
    {
        if (!runner.IsActive)
        {
            return;
        }
        runner.Tick(deltaTime, input);
    }

    private void Finish()
    {
        HasSpoken = true;
        Cancel();
    }

    // Ends the conversation immediately and frees the player.
    public void Cancel()
    {
        runner.Stop();
        if (!IsTalking)
        {
            return;
        }
        IsTalking = false;
    }

    public float InteractDistanceSqr()
    {
        if (player == null || !isActiveAndEnabled)
        {
            return float.MaxValue;
        }
        Vector3 delta = player.position - (transform.position + anchorOffset);
        float distance = delta.x * delta.x + delta.z * delta.z;
        return distance <= talkRadius * talkRadius ? distance : float.MaxValue;
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        if (!runner.IsActive)
        {
            InteractPrompt.Draw(transform.position + anchorOffset, hintOffsetY, hintText, actionText, promptPixelSize, promptAlpha);
            return;
        }

        int depth = GUI.depth;
        GUI.depth = -900;
        runner.Draw();
        GUI.depth = depth;
    }
}
