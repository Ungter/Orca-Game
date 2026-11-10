using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-50)]
public sealed class LighthouseStacker : MonoBehaviour, IInteractable
{
    [SerializeField, Min(1)] private int winThreshold = 10;
    [SerializeField] private Transform originalTower;
    [SerializeField] private Transform entrance;
    [SerializeField] private Transform pierLanding;
    [SerializeField] private Transform ferryBerth;
    private static LighthouseStacker active;
    public static bool IsOpen => active != null;
    public bool HasWon { get; private set; }
    public LighthouseStackMatch Match { get; private set; }
    public LighthouseStackView View { get; private set; }
    public Vector3 PierLanding => pierLanding.position;
    public Vector3 Berth => ferryBerth.position;
    public Vector3 Entrance => entrance.position;
    public int WinThreshold => winThreshold;
    public event System.Action Won;
    private Transform player;
    private Camera cameraView;
    private CameraFollow follow;
    private bool followWasEnabled;
    private bool towerWasActive;
    private Vector3 cameraPosition;
    private Quaternion cameraRotation;
    private float fieldOfView;
    private float farClip;
    private float prompt;
    private bool awardedThisRun;
    private float perfectUntil;
    private DolphinWordPuzzle anagram;
    private LighthouseStartMarker startMarker;

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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active = null;

    public void Configure(Transform tower, Transform entry, Transform pier, Transform berth)
    {
        originalTower = tower;
        entrance = entry;
        pierLanding = pier;
        ferryBerth = berth;
    }

    private void OnEnable() => Interactables.Add(this);
    private void OnDisable()
    {
        Interactables.Remove(this);
        Close();
        if (startMarker != null) startMarker.gameObject.SetActive(false);
    }

    public float InteractDistanceSqr()
    {
        if (player == null) player = Interactables.FindPlayer();
        if (!isActiveAndEnabled || !IsUnlocked || entrance == null || player == null ||
            player.gameObject.scene != gameObject.scene) return float.MaxValue;
        Vector3 delta = player.position - entrance.position;
        float distance = delta.x * delta.x + delta.z * delta.z;
        return Mathf.Abs(delta.y) <= 4f && distance <= 4f * 4f ? distance : float.MaxValue;
    }

    private void Update()
    {
        if (GameplayDebugMenu.BlocksInput) return;
        if (entrance != null && startMarker == null)
            startMarker = LighthouseStartMarker.Create(entrance);
        RefreshStartMarker();
        if (PauseMenu.IsPaused) return;
        Keyboard keyboard = Keyboard.current;
        if (active == this)
        {
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { Close(); return; }
            Tick(Time.deltaTime, keyboard != null && keyboard.spaceKey.wasPressedThisFrame,
                keyboard != null && keyboard.rKey.wasPressedThisFrame);
            return;
        }
        bool usable = !TitleScreen.IsOpen && !InventoryInspection.IsOpen && !Interactables.Busy && Interactables.IsNearest(this);
        prompt = Mathf.MoveTowards(prompt, usable ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        if (usable && keyboard != null && keyboard.eKey.wasPressedThisFrame) Begin(Interactables.FindPlayer());
    }

    public bool Begin(Transform visitor, Camera view = null)
    {
        if (!isActiveAndEnabled || !IsUnlocked || visitor == null || visitor.gameObject.scene != gameObject.scene ||
            originalTower == null || Interactables.Busy || TitleScreen.IsOpen || InventoryInspection.IsOpen || PauseMenu.IsPaused)
            return false;
        cameraView = view != null ? view : Camera.main;
        if (cameraView == null || Resources.Load<GameObject>("WorldModels/LighthouseSection") == null ||
            Resources.Load<GameObject>("WorldModels/LighthouseLantern") == null) return false;
        player = visitor;
        cameraPosition = cameraView.transform.position;
        cameraRotation = cameraView.transform.rotation;
        fieldOfView = cameraView.fieldOfView;
        farClip = cameraView.farClipPlane;
        follow = cameraView.GetComponent<CameraFollow>();
        followWasEnabled = follow != null && follow.enabled;
        if (follow != null) follow.enabled = false;
        towerWasActive = originalTower.gameObject.activeSelf;
        originalTower.gameObject.SetActive(false);
        active = this;
        RefreshStartMarker();
        prompt = 0f;
        cameraView.fieldOfView = 50f;
        cameraView.farClipPlane = Mathf.Max(farClip, 300f);
        Restart();
        PositionCamera(0f, true);
        return true;
    }

    public void Tick(float deltaTime, bool stack = false, bool retry = false)
    {
        if (active != this || PauseMenu.IsPaused) return;
        if (retry && Match.Failed) { Restart(); return; }
        // Drop at the displayed position; don't advance another frame before reading Space.
        if (stack)
        {
            if (Match.Drop() && Match.Perfect) perfectUntil = Time.unscaledTime + 0.6f;
            if (Match.HasWon && !awardedThisRun)
            {
                awardedThisRun = true;
                if (!HasWon) { HasWon = true; Won?.Invoke(); }
            }
        }
        else Match.Tick(deltaTime);
        View.Refresh(Match, deltaTime);
        PositionCamera(deltaTime, false);
    }

    private void Restart()
    {
        View?.Dispose();
        Match = new LighthouseStackMatch(Mathf.Max(1, winThreshold));
        View = new LighthouseStackView(transform, originalTower.position);
        View.Refresh(Match, 0f);
        awardedThisRun = false;
        perfectUntil = 0f;
    }

    private void PositionCamera(float deltaTime, bool instant)
    {
        if (cameraView == null) return;
        Vector3 focus = View.CameraFocus(Match);
        Vector3 offset = new Vector3(18f, 16f, -24f);
        float aspect = Mathf.Max(0.3f, cameraView.aspect);
        if (aspect < 1.3f) offset *= 1.3f / aspect;
        Vector3 target = focus + offset;
        cameraView.transform.position = instant ? target :
            Vector3.Lerp(cameraView.transform.position, target, 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) * 7f));
        cameraView.transform.LookAt(focus);
    }

    public void Close()
    {
        if (active != this) return;
        View?.Dispose();
        View = null;
        if (originalTower != null) originalTower.gameObject.SetActive(towerWasActive);
        if (cameraView != null)
        {
            cameraView.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            cameraView.fieldOfView = fieldOfView;
            cameraView.farClipPlane = farClip;
        }
        if (follow != null) follow.enabled = followWasEnabled;
        active = null;
        RefreshStartMarker();
    }

    public void DebugComplete()
    {
        awardedThisRun = true;
        if (!HasWon) { HasWon = true; Won?.Invoke(); }
        Close();
    }

    private void RefreshStartMarker()
    {
        if (startMarker != null)
            startMarker.gameObject.SetActive(isActiveAndEnabled && IsUnlocked && active != this &&
                !Interactables.Busy && !TitleScreen.IsOpen);
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (active != this)
        {
            if (entrance != null) InteractPrompt.Draw(entrance.position + Vector3.up * 2f, 10f, "E", "Start stacking", 3, prompt);
            return;
        }
        int p = Mathf.Max(1, Mathf.RoundToInt(3f * InteractPrompt.UIScale()));
        Draw(22f, "LIGHTHOUSE STACKER", p, new Color32(242, 211, 107, 255));
        Draw(54f, "SECTIONS " + Match.Score + "   WIN AT " + Match.WinThreshold, p, Color.white);
        string result = Match.Failed ? (HasWon ? "RUN OVER - WIN EARNED!" : "MISSED! TRY AGAIN") :
            HasWon ? "YOU WIN! KEEP STACKING" : "LINE UP THE NEXT SECTION";
        Draw(90f, result, p, new Color32(242, 211, 107, 255));
        if (!Match.Failed && Time.unscaledTime < perfectUntil) Draw(126f, "PERFECT!", p, Color.white);
        Draw(Screen.height - 48f, Match.Failed ? "[R] RETRY    [ESC] LEAVE" : "[SPACE] STACK    [ESC] LEAVE", p, Color.white);
    }

    private static void Draw(float y, string text, int p, Color colour)
    {
        float x = (Screen.width - PixelFont.Measure(text) * p) * 0.5f;
        PixelFont.Draw(new Vector2(x, y), text, p, colour, new Color(0f, 0f, 0f, 0.9f));
    }
}
