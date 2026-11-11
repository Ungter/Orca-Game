using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// The final game, played from the top of the lighthouse once the dolphin anagram is solved. The
// player presses E at the foot of the lighthouse's stair (trigger); the screen fades to black,
// they are carried up into the lantern (lookout) and look out over the main island in the first
// person. The island splits into hexes (MouseTrapIsland), the houses stand as walls, and the
// villain (a plain cube for now) starts somewhere in its heart.
//
// Each turn the player aims the lighthouse's light at a hex with WASD (its beam swings over onto
// it, quickly but smoothly) and strikes it with Space or E: it sinks into the sea. Then the villain
// takes a step towards the shore by the shortest way, knocking flat any fence he runs through.
// Shut him in so he can't reach the shore and he is caught; let him reach it and he gets away
// onto the water. Either way the screen fades slowly to black, the ending's line shows, and the
// game goes back to the title screen.
//
// While it runs, every light on the island seems to come from the lighthouse: the moonlight is
// turned to fall from it, the lighthouse's turning beam is put out, the aim is a six-sided shaft
// from the lantern whose foot lights exactly the aimed hex (no round spot smearing across the
// ground at that low angle), and the strike flashes a spot light from the lantern. When anything
// blocks the view of the villain, a cone of view opens to him (MouseTrapSightline).
[DefaultExecutionOrder(-40)]
public sealed class LighthouseMouseTrap : MonoBehaviour, IInteractable
{
    public const string WinText = "You solved the mystery and captured the villain";
    public const string LoseText = "You solved the mystery, but the villain got away...";
    public const string EscapedText = "HE ESCAPED!";
    public const string CaughtText = "TRAPPED!";
    public const float MouseSize = 4.5f;
    private const float FadeOutTime = 0.6f;
    private const float FadeInTime = 1f;
    private const float EndFadeTime = 3.5f;
    private const float TextFadeTime = 1.2f;
    private const float TextHoldTime = 4.5f;
    private const float StrikeDelay = 0.8f;
    private const float HopTime = 0.55f;
    private const float EscapeHopTime = 1.1f;
    private const float ResultHoldTime = 2.6f;
    // The aiming beam: how fast it swings onto a new hex (1/s), its look and its cone.
    private const float BeamFollow = 14f;
    // How far (degrees) the view turns from the action while the clue letter is up, so it isn't
    // hidden behind the letter.
    private const float LetterLookDown = 4f;
    private const float LetterLookRight = 7f;
    private const float ShaftDensity = 0.006f;
    private const float PoolBrightness = 1.1f;
    private static readonly Color Gold = new Color32(242, 211, 107, 255);
    private static readonly Color Lantern = new Color(1f, 0.82f, 0.52f);

    public enum Phase { Idle, Entering, Splitting, Aiming, Striking, MouseMoving, Caught, Escaped, Ending, Home }

    [SerializeField] private Transform trigger;
    [SerializeField] private Transform entrance;
    [SerializeField] private Transform lookout;
    [Tooltip("Centre to corner of each hex, in metres.")]
    [SerializeField, Min(4f)] private float hexRadius = 10f;
    // Big meshes a player build can't read back, and readable copies of them (Tools > Orca > Set
    // Up Mouse Trap bakes these), so the paved slabs can be cut into hexes too.
    [SerializeField] private List<Mesh> unreadableMeshes = new List<Mesh>();
    [SerializeField] private List<Mesh> readableCopies = new List<Mesh>();

    private static LighthouseMouseTrap active;
    public static bool IsOpen => active != null;

    public Phase CurrentPhase { get; private set; } = Phase.Idle;
    public MouseTrapBoard Board { get; private set; }
    public MouseTrapIsland Island { get; private set; }
    public MouseTrapSightline Sightline { get; private set; }
    public Hex Aim { get; private set; }
    public Transform Mouse => mouse != null ? mouse.transform : null;
    public Transform AimBeam => beam != null ? beam.transform : null;
    // The aimed hex lit on the ground, and the corners of the hex the beam lights now.
    public Transform AimedLight => pool != null ? pool.transform : null;
    public IReadOnlyList<Vector3> LitHex => footprint;
    // Where the beam is shining now, on its way to the aimed hex.
    public Vector3 BeamPoint => beamPoint;
    public Light StrikeLight => strikeLight;
    // The player's clue letter, up in the corner with the board on its map while the game runs.
    public ClueMinigame Letter => letter;
    public MouseTrapLetter LetterBoard => letterBoard;
    public Vector3 Eye { get; private set; }
    public Vector3 LanternPosition { get; private set; }
    public float Fade => fade;
    public string Ending { get; private set; }
    public Transform Trigger => trigger;
    public Transform Entrance => entrance;
    public Transform Lookout => lookout;
    public float HexRadius => hexRadius;
    // Fixes the next game's random start (tests); otherwise each game differs.
    public int? Seed { get; set; }

    private DolphinWordPuzzle anagram;
    private Transform visitor;
    private Camera view;
    private float prompt;
    private float phaseTime;
    private float fade;
    private float textFade;
    private float textHold;
    private bool escaped;
    private string message;

    // The game's own objects.
    private GameObject mouse;
    private Material mouseMaterial;
    private GameObject beam;
    private Material beamMaterial;
    private Mesh beamMesh;
    private Color beamColour;
    private Vector3 beamPoint;
    private bool beamPlaced;
    private GameObject pool;
    private Material poolMaterial;
    private Mesh poolMesh;
    private readonly Vector3[] footprint = new Vector3[6];
    private readonly Vector4[] shaftPlanes = new Vector4[8];
    private float beamStrength;
    private Material seam;
    private Material cone;
    private GameObject lights;
    private Light strikeLight;
    private float strikeTime = -1f;
    private Hex struck;
    private float refused;
    private Vector3 hopFrom, hopTo;
    private Quaternion mouseTurn = Quaternion.identity;
    private Vector3 rollAxis;
    private float anchorX;
    private bool lastVertical;
    private Vector2Int heldMove;
    private float holdTime;
    private float repeatTime;

    // What the game changed, to put back if it is ever left (tests); the ending reloads the scene.
    private Vector3 cameraPosition;
    private Quaternion cameraRotation;
    private float cameraFov, cameraNear, cameraFar;
    private CameraFollow follow;
    private bool followWasEnabled;
    private PlayerController playerController;
    private bool playerWasEnabled;
    private CharacterController body;
    private bool bodyWasEnabled;
    private Vector3 visitorPosition;
    private readonly List<(Behaviour behaviour, bool enabled)> paused = new List<(Behaviour, bool)>();
    private readonly List<(Renderer renderer, bool enabled)> hidden = new List<(Renderer, bool)>();
    private readonly List<(GameObject owner, bool active)> putOut = new List<(GameObject, bool)>();
    private ClueMinigame letter;
    private MouseTrapLetter letterBoard;
    private (Hex, int, Vector2Int, bool, Phase) letterShown;
    private bool letterDrawn;
    private Light moon;
    private Quaternion moonRotation;
    private float moonIntensity;
    private float fogDensity;
    private bool atmosphereHeld;
    private bool cameraHeld;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active = null;

    public void Configure(Transform stair, Transform foot, Transform lantern)
    {
        trigger = stair;
        entrance = foot;
        lookout = lantern;
    }

    public void SetReadableCopies(List<Mesh> originals, List<Mesh> copies)
    {
        unreadableMeshes = originals ?? new List<Mesh>();
        readableCopies = copies ?? new List<Mesh>();
    }

    public bool IsUnlocked
    {
        get
        {
            if (anagram == null && gameObject.scene.isLoaded)
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    anagram = root.GetComponentInChildren<DolphinWordPuzzle>(true);
                    if (anagram != null) break;
                }
            return anagram != null && anagram.IsSolved;
        }
    }

    private void OnEnable() => Interactables.Add(this);

    private void OnDisable()
    {
        Interactables.Remove(this);
        if (active == this) Abort();
    }

    public float InteractDistanceSqr()
    {
        if (visitor == null) visitor = Interactables.FindPlayer();
        if (!isActiveAndEnabled || active != null || !IsUnlocked || entrance == null || visitor == null ||
            visitor.gameObject.scene != gameObject.scene) return float.MaxValue;
        Vector3 delta = visitor.position - entrance.position;
        float distance = delta.x * delta.x + delta.z * delta.z;
        return Mathf.Abs(delta.y) <= 4f && distance <= 4f * 4f ? distance : float.MaxValue;
    }

    private void Update()
    {
        if (GameplayDebugMenu.BlocksInput) return;
        if (PauseMenu.IsPaused)
        {
            Sightline?.Suspend();
            return;
        }
        Keyboard keyboard = Keyboard.current;
        if (active == this)
        {
            bool strike = keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame);
            Tick(Time.deltaTime, ReadMove(keyboard, Time.deltaTime), strike);
            return;
        }
        bool usable = active == null && !TitleScreen.IsOpen && !InventoryInspection.IsOpen && !Interactables.Busy && Interactables.IsNearest(this);
        prompt = Mathf.MoveTowards(prompt, usable ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        if (usable && keyboard != null && keyboard.eKey.wasPressedThisFrame) Begin(Interactables.FindPlayer());
    }

    // WASD (or the arrows), repeating while held.
    private Vector2Int ReadMove(Keyboard keyboard, float deltaTime)
    {
        if (keyboard == null) return Vector2Int.zero;
        Vector2Int pressed = Vector2Int.zero, held = Vector2Int.zero;
        void Key(UnityEngine.InputSystem.Controls.KeyControl a, UnityEngine.InputSystem.Controls.KeyControl b, Vector2Int dir)
        {
            if (a.wasPressedThisFrame || b.wasPressedThisFrame) pressed = dir;
            if (a.isPressed || b.isPressed) held = dir;
        }
        Key(keyboard.wKey, keyboard.upArrowKey, Vector2Int.up);
        Key(keyboard.sKey, keyboard.downArrowKey, Vector2Int.down);
        Key(keyboard.aKey, keyboard.leftArrowKey, Vector2Int.left);
        Key(keyboard.dKey, keyboard.rightArrowKey, Vector2Int.right);
        if (pressed != Vector2Int.zero)
        {
            heldMove = pressed;
            holdTime = 0f;
            repeatTime = 0f;
            return pressed;
        }
        if (held == Vector2Int.zero || held != heldMove)
        {
            heldMove = held;
            holdTime = 0f;
            return Vector2Int.zero;
        }
        holdTime += deltaTime;
        if (holdTime < 0.32f) return Vector2Int.zero;
        repeatTime += deltaTime;
        if (repeatTime < 0.14f) return Vector2Int.zero;
        repeatTime = 0f;
        return held;
    }

    public bool Begin(Transform player, Camera camera = null)
    {
        if (!isActiveAndEnabled || active != null || !IsUnlocked || player == null || lookout == null ||
            player.gameObject.scene != gameObject.scene || Interactables.Busy || TitleScreen.IsOpen ||
            InventoryInspection.IsOpen || PauseMenu.IsPaused) return false;
        view = camera != null ? camera : SceneCamera();
        if (view == null) return false;
        visitor = player;
        active = this;
        CurrentPhase = Phase.Entering;
        phaseTime = 0f;
        fade = 0f;
        textFade = 0f;
        textHold = 0f;
        prompt = 0f;
        escaped = false;
        message = null;
        Ending = null;
        return true;
    }

    private Camera SceneCamera()
    {
        Camera main = Camera.main;
        if (main != null && main.gameObject.scene == gameObject.scene) return main;
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                if (camera.CompareTag("MainCamera")) return camera;
        return null;
    }

    // ---- Turns -------------------------------------------------------------------------------

    public void Tick(float deltaTime, Vector2Int move = default, bool strike = false)
    {
        if (active != this) return;
        deltaTime = Mathf.Max(0f, deltaTime);
        phaseTime += deltaTime;
        refused = Mathf.Max(0f, refused - deltaTime);
        switch (CurrentPhase)
        {
            case Phase.Entering:
                fade = Mathf.MoveTowards(fade, 1f, deltaTime / FadeOutTime);
                if (fade >= 1f)
                {
                    if (!Setup()) { Abort(); return; }
                    Island.BeginSplit();
                    ShowLetter();
                    Enter(Phase.Splitting);
                }
                break;
            case Phase.Splitting:
                fade = Mathf.MoveTowards(fade, 0f, deltaTime / FadeInTime);
                if (fade <= 0f && !Island.IsSplitting) Enter(Phase.Aiming);
                break;
            case Phase.Aiming:
                if (move != Vector2Int.zero) MoveAim(move);
                if (strike) TryStrike();
                break;
            case Phase.Striking:
                if (phaseTime >= StrikeDelay) MouseTurn();
                break;
            case Phase.MouseMoving:
                if (phaseTime >= HopTime)
                {
                    mouseTurn = Quaternion.AngleAxis(90f, rollAxis) * mouseTurn;
                    Enter(Phase.Aiming);
                }
                break;
            case Phase.Caught:
                if (phaseTime >= ResultHoldTime) BeginEnding();
                break;
            case Phase.Escaped:
                if (phaseTime >= EscapeHopTime + ResultHoldTime) BeginEnding();
                break;
            case Phase.Ending:
                fade = Mathf.MoveTowards(fade, 1f, deltaTime / EndFadeTime);
                if (fade >= 1f)
                {
                    textFade = Mathf.MoveTowards(textFade, 1f, deltaTime / TextFadeTime);
                    if (textFade >= 1f) textHold += deltaTime;
                    if (textHold >= TextHoldTime) GoHome();
                }
                break;
        }
        if (Island == null) return;
        Island.Tick(deltaTime);
        PoseMouse();
        AimCamera(deltaTime);
        UpdateLights(deltaTime);
        UpdateLetter();
        bool watching = CurrentPhase == Phase.Aiming || CurrentPhase == Phase.Striking || CurrentPhase == Phase.MouseMoving ||
            CurrentPhase == Phase.Caught || CurrentPhase == Phase.Escaped || CurrentPhase == Phase.Splitting;
        if (Sightline != null)
        {
            if (watching && mouse != null) Sightline.Update(mouse.transform.position, MouseSize, deltaTime);
            else Sightline.Suspend();
        }
    }

    private void Enter(Phase phase)
    {
        CurrentPhase = phase;
        phaseTime = 0f;
    }

    // Moves the aim to the neighbouring hex that lies most nearly the way pressed on screen. Up
    // and down zigzag between the two hexes either side, keeping to the column they started in.
    public bool MoveAim(Vector2Int move)
    {
        if (Board == null || view == null || move == Vector2Int.zero) return false;
        Vector3 here = view.WorldToScreenPoint(Island.Top(Aim));
        bool vertical = move.x == 0;
        if (vertical && !lastVertical) anchorX = here.x;
        lastVertical = vertical;
        Vector2 key = ((Vector2)move).normalized;
        Hex? best = null;
        float bestScore = 0.3f;
        for (int d = 0; d < 6; d++)
        {
            Hex next = Aim.Neighbour(d);
            if (!Board.Contains(next)) continue;
            Vector3 there = view.WorldToScreenPoint(Island.Top(next));
            if (there.z <= 0f) continue;
            Vector2 step = there - here;
            if (step.sqrMagnitude < 1f) continue;
            float score = Vector2.Dot(step.normalized, key);
            if (vertical) score -= 0.25f * Mathf.Abs(there.x - anchorX) / step.magnitude;
            if (score > bestScore)
            {
                bestScore = score;
                best = next;
            }
        }
        if (!best.HasValue) return false;
        Aim = best.Value;
        return true;
    }

    // Puts the aim straight on a hex of the board (tests).
    public bool AimAt(Hex hex)
    {
        if (Board == null || !Board.Contains(hex)) return false;
        Aim = hex;
        return true;
    }

    public bool TryStrike()
    {
        if (CurrentPhase != Phase.Aiming || Board == null) return false;
        if (!Board.Strike(Aim))
        {
            refused = 0.4f;
            return false;
        }
        Island.Sink(Aim);
        struck = Aim;
        strikeTime = 0f;
        Enter(Phase.Striking);
        return true;
    }

    private void MouseTurn()
    {
        MouseTrapBoard.Step step = Board.MouseTurn();
        hopFrom = MouseRest(step.From);
        switch (step.Kind)
        {
            case MouseTrapBoard.StepKind.Moved:
                hopTo = MouseRest(step.To);
                Roll(hopTo - hopFrom);
                Island.KnockDown(hopFrom, hopTo);
                Enter(Phase.MouseMoving);
                break;
            case MouseTrapBoard.StepKind.Escaped:
                hopTo = Island.WaterBeyond(step.From, step.Direction) + Vector3.up * (MouseSize * 0.2f);
                Roll(hopTo - hopFrom);
                Island.KnockDown(hopFrom, hopTo);
                escaped = true;
                message = EscapedText;
                Enter(Phase.Escaped);
                break;
            default:
                message = CaughtText;
                Enter(Phase.Caught);
                break;
        }
    }

    private void Roll(Vector3 towards)
    {
        towards.y = 0f;
        rollAxis = towards.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, towards.normalized) : Vector3.right;
    }

    private void BeginEnding()
    {
        Ending = Board.State == MouseTrapBoard.Outcome.Trapped ? WinText : LoseText;
        message = null;
        HideLetter();
        Enter(Phase.Ending);
    }

    // ---- The clue letter's map ---------------------------------------------------------------

    // The player's clue letter from the alchemist's hunt, in this scene.
    private ClueMinigame FindLetter()
    {
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            ClueMinigame found = root.GetComponentInChildren<ClueMinigame>(true);
            if (found != null) return found;
        }
        return null;
    }

    // Brings the letter up into the corner with the board on its map.
    private void ShowLetter()
    {
        if (letter == null || !letter.BeginBoard()) return;
        letterBoard = new MouseTrapLetter(letter.BoardFrame, Island.Grid, Board);
        letterShown = default;
        letterDrawn = false;
        UpdateLetter();
    }

    private void HideLetter()
    {
        if (letter != null) letter.EndBoard();
        letterBoard = null;
    }

    // Keeps the letter's board in step with the game: redrawn when the aim, the sunk hexes, the
    // villain's place on the map or the game's stage changes.
    private void UpdateLetter()
    {
        if (letterBoard == null || mouse == null) return;
        Vector2 at = letterBoard.ToLetter(mouse.transform.position);
        var shown = (Aim, Board.Sunk.Count, new Vector2Int(Mathf.RoundToInt(at.x), Mathf.RoundToInt(at.y)), refused > 0f, CurrentPhase);
        if (letterDrawn && shown.Equals(letterShown)) return;
        letterShown = shown;
        letterDrawn = true;
        string right = CurrentPhase == Phase.Caught ? CaughtText : CurrentPhase == Phase.Escaped ? EscapedText : "SHUT HIM IN!";
        string prompt = CurrentPhase == Phase.Aiming ? "[WASD] AIM  [SPACE] STRIKE" : "";
        letter.SetBoard(letterBoard.Draw(Board, Aim, refused > 0f, mouse.transform.position), "STRIKES " + Board.Strikes, right, prompt);
    }

    private void GoHome()
    {
        if (CurrentPhase == Phase.Home) return;
        Enter(Phase.Home);
        if (!Application.isPlaying) return;
        // Like the pause menu's HOME: a fresh scene opens on the title screen.
        active = null;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ---- Setting up and putting back ---------------------------------------------------------

    private bool Setup()
    {
        int seed = Seed ?? System.Environment.TickCount;
        var copies = new Dictionary<Mesh, Mesh>();
        for (int i = 0; i < Mathf.Min(unreadableMeshes.Count, readableCopies.Count); i++)
            if (unreadableMeshes[i] != null && readableCopies[i] != null) copies[unreadableMeshes[i]] = readableCopies[i];
        seam = MaterialFrom("MouseTrap/MouseTrapSeam");
        cone = MaterialFrom("MouseTrap/SightCone");
        Island = MouseTrapIsland.Build(gameObject.scene, hexRadius, seed, seam, copies);
        if (Island == null || Island.Cells.Count == 0) return false;
        Board = new MouseTrapBoard(Island.Cells, Island.Walls, seed);
        if (Board.CentralCells().Count == 0) return false;

        Renderer lookoutRenderer = lookout.GetComponent<Renderer>();
        Eye = lookoutRenderer != null ? lookoutRenderer.bounds.center : lookout.position;
        Transform beamLight = FindUnder(lookout.parent, "lightHouseBigLight");
        LanternPosition = beamLight != null ? beamLight.position : Eye + Vector3.up * 4f;

        HidePlayer();
        PutOutLighthouse();
        HoldAtmosphere();

        follow = view.GetComponent<CameraFollow>();
        followWasEnabled = follow != null && follow.enabled;
        if (follow != null) follow.enabled = false;
        cameraPosition = view.transform.position;
        cameraRotation = view.transform.rotation;
        cameraFov = view.fieldOfView;
        cameraNear = view.nearClipPlane;
        cameraFar = view.farClipPlane;
        cameraHeld = true;
        view.fieldOfView = 42f;
        view.nearClipPlane = 0.3f;
        view.farClipPlane = Mathf.Max(cameraFar, 1500f);
        Vector3 start = Island.Surface(Board.Mouse);
        view.transform.SetPositionAndRotation(Eye, Quaternion.LookRotation(Vector3.Lerp(start, Island.BoardBounds.center, 0.3f) - Eye));

        // Start aiming at the villain's neighbour nearest the lighthouse.
        Aim = Board.Mouse;
        float nearest = float.MaxValue;
        for (int d = 0; d < 6; d++)
        {
            Hex next = Board.Mouse.Neighbour(d);
            if (!Board.Contains(next)) continue;
            float distance = (Island.Surface(next) - Eye).sqrMagnitude;
            if (distance < nearest) { nearest = distance; Aim = next; }
        }

        mouse = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mouse.name = "Villain (mouse)";
        if (mouse.TryGetComponent(out Collider box))
        {
            if (Application.isPlaying) Destroy(box);
            else DestroyImmediate(box);
        }
        mouse.transform.localScale = Vector3.one * MouseSize;
        mouseMaterial = PlaceholderMaterials.Unlit(new Color(0.93f, 0.9f, 0.82f));
        mouseMaterial.name = "Villain";
        mouse.GetComponent<MeshRenderer>().sharedMaterial = mouseMaterial;
        mouse.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        SceneManager.MoveGameObjectToScene(mouse, gameObject.scene);
        mouseTurn = Quaternion.identity;

        lights = new GameObject("Mouse trap lights");
        SceneManager.MoveGameObjectToScene(lights, gameObject.scene);
        lights.transform.position = LanternPosition;
        strikeLight = MakeSpot("Strike light", LightShadows.Soft);
        strikeLight.enabled = false;
        MakeBeam();
        beamPlaced = false;
        beamStrength = 0f;

        Sightline = new MouseTrapSightline(view, Island.Occluders, cone);
        // The clue letter's map, ready to come up when the screen clears (surveyed now, while it
        // is black, if the hunt never did).
        letter = FindLetter();
        if (letter != null) letter.SurveyNow();
        PoseMouse();
        return true;
    }

    private static Material MaterialFrom(string path)
    {
        Material template = Resources.Load<Material>(path);
        if (template == null) return null;
        var material = new Material(template);
        return material;
    }

    private Light MakeSpot(string name, LightShadows shadows)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(lights.transform, false);
        Light light = holder.AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = Lantern;
        light.shadows = shadows;
        light.shadowStrength = 0.85f;
        return light;
    }

    // The player goes up into the lantern, out of sight (the view is theirs).
    private void HidePlayer()
    {
        playerController = visitor.GetComponent<PlayerController>();
        body = visitor.GetComponent<CharacterController>();
        playerWasEnabled = playerController != null && playerController.enabled;
        bodyWasEnabled = body != null && body.enabled;
        visitorPosition = visitor.position;
        if (playerController != null) playerController.enabled = false;
        if (body != null) body.enabled = false;
        foreach (Renderer r in visitor.GetComponentsInChildren<Renderer>(true))
        {
            hidden.Add((r, r.enabled));
            r.enabled = false;
        }
        foreach (Light l in visitor.GetComponentsInChildren<Light>(true))
        {
            paused.Add((l, l.enabled));
            l.enabled = false;
        }
        foreach (PlayerLantern lantern in visitor.GetComponentsInChildren<PlayerLantern>(true))
        {
            paused.Add((lantern, lantern.enabled));
            lantern.enabled = false;
        }
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            foreach (InventoryHUD hud in root.GetComponentsInChildren<InventoryHUD>(true))
            {
                paused.Add((hud, hud.enabled));
                hud.enabled = false;
            }
        visitor.position = Eye - Vector3.up * 1.6f;
    }

    // The turning beam goes out, and the lantern's glass and frame round the eye are hidden so
    // they don't fill the view.
    private void PutOutLighthouse()
    {
        Transform tower = lookout.parent != null ? lookout.parent : lookout;
        foreach (LighthouseBeam beam in tower.GetComponentsInChildren<LighthouseBeam>(true))
        {
            putOut.Add((beam.gameObject, beam.gameObject.activeSelf));
            beam.gameObject.SetActive(false);
        }
        foreach (Renderer r in tower.GetComponentsInChildren<Renderer>(false))
        {
            Bounds b = r.bounds;
            b.Expand(3f);
            if (!r.enabled || !b.Contains(Eye)) continue;
            hidden.Add((r, r.enabled));
            r.enabled = false;
        }
        foreach (Light l in tower.GetComponentsInChildren<Light>(false))
        {
            if (!l.enabled || (l.transform.position - Eye).sqrMagnitude > 12f * 12f) continue;
            paused.Add((l, l.enabled));
            l.enabled = false;
        }
    }

    // The moon falls from the lighthouse, a little brighter, and the fog thins so the island
    // reads from the lantern (as on the title screen).
    private void HoldAtmosphere()
    {
        moon = RenderSettings.sun;
        if (moon == null)
            foreach (Light l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional && l.gameObject.scene == gameObject.scene) { moon = l; break; }
        fogDensity = RenderSettings.fogDensity;
        atmosphereHeld = true;
        Vector3 across = Island.BoardBounds.center - LanternPosition;
        float distance = across.magnitude;
        RenderSettings.fogDensity = TitleScreen.TitleFogDensity(fogDensity, distance * 1.25f);
        if (moon == null) return;
        moonRotation = moon.transform.rotation;
        moonIntensity = moon.intensity;
        Vector3 flat = new Vector3(across.x, 0f, across.z).normalized;
        float fall = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(-across.y / distance, -1f, 1f)) * Mathf.Rad2Deg, 24f, 50f);
        // Tipped down from level by the fall angle, still heading away from the lighthouse.
        moon.transform.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(fall, Vector3.Cross(Vector3.up, flat)) * flat);
        moon.intensity = moonIntensity * TitleScreen.TitleMoonBoost;
    }

    // Leaves the game without its ending, putting everything back (tests, or the component going
    // away). The ending itself reloads the scene instead.
    public void Abort()
    {
        if (active != this) return;
        HideLetter();
        Sightline?.Dispose();
        Sightline = null;
        Island?.Dispose();
        Island = null;
        Board = null;
        if (mouse != null) DestroyNow(mouse);
        if (lights != null) DestroyNow(lights);
        foreach (Object o in new Object[] { mouseMaterial, beamMaterial, beamMesh, poolMaterial, poolMesh, cone, seam }) if (o != null) DestroyNow(o);
        foreach (var (r, enabled) in hidden) if (r != null) r.enabled = enabled;
        hidden.Clear();
        foreach (var (b, enabled) in paused) if (b != null) b.enabled = enabled;
        paused.Clear();
        foreach (var (owner, on) in putOut) if (owner != null) owner.SetActive(on);
        putOut.Clear();
        if (atmosphereHeld)
        {
            RenderSettings.fogDensity = fogDensity;
            if (moon != null) { moon.transform.rotation = moonRotation; moon.intensity = moonIntensity; }
            atmosphereHeld = false;
        }
        if (visitor != null && playerController != null)
        {
            visitor.position = visitorPosition;
            playerController.enabled = playerWasEnabled;
            if (body != null) body.enabled = bodyWasEnabled;
        }
        if (view != null && cameraHeld)
        {
            view.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            view.fieldOfView = cameraFov;
            view.nearClipPlane = cameraNear;
            view.farClipPlane = cameraFar;
            if (follow != null) follow.enabled = followWasEnabled;
            cameraHeld = false;
        }
        playerController = null;
        active = null;
        CurrentPhase = Phase.Idle;
        fade = 0f;
    }

    private static void DestroyNow(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    private static Transform FindUnder(Transform root, string name)
    {
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // ---- Each frame --------------------------------------------------------------------------

    private Vector3 MouseRest(Hex hex) => Island.Surface(hex) + Vector3.up * (MouseSize * 0.5f);

    private void PoseMouse()
    {
        if (mouse == null || Board == null) return;
        Vector3 position = MouseRest(Board.Mouse);
        Quaternion rotation = mouseTurn;
        switch (CurrentPhase)
        {
            case Phase.MouseMoving:
            {
                float t = Mathf.Clamp01(phaseTime / HopTime);
                position = Vector3.Lerp(hopFrom, hopTo, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 3f;
                rotation = Quaternion.AngleAxis(90f * t, rollAxis) * mouseTurn;
                break;
            }
            case Phase.Caught:
            {
                // Shut in: he shakes.
                float shake = Mathf.Clamp01(1f - phaseTime / ResultHoldTime) * 0.35f;
                position += new Vector3(Mathf.Sin(phaseTime * 61f), 0f, Mathf.Sin(phaseTime * 47f)) * shake;
                break;
            }
        }
        if (escaped)
        {
            float t = CurrentPhase == Phase.Escaped ? Mathf.Clamp01(phaseTime / EscapeHopTime) : 1f;
            if (t < 1f)
            {
                position = Vector3.Lerp(hopFrom, hopTo, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 7f;
                rotation = Quaternion.AngleAxis(180f * t, rollAxis) * mouseTurn;
            }
            else
            {
                // Off on the water, bobbing away.
                float bob = Time.time;
                position = hopTo + Vector3.up * (Mathf.Sin(bob * 2.1f) * 0.35f);
                rotation = Quaternion.AngleAxis(180f, rollAxis) * mouseTurn * Quaternion.Euler(Mathf.Sin(bob * 1.7f) * 6f, 0f, Mathf.Sin(bob * 1.3f) * 6f);
            }
        }
        mouse.transform.SetPositionAndRotation(position, rotation);
    }

    // Looks from the lantern towards the aim, keeping the villain in view.
    private void AimCamera(float deltaTime)
    {
        if (view == null || mouse == null || CurrentPhase == Phase.Ending || CurrentPhase == Phase.Home) return;
        Vector3 target = Vector3.Lerp(mouse.transform.position, Island.Top(Aim), 0.6f);
        Quaternion want = Quaternion.LookRotation(target - Eye);
        // With the letter up in the bottom-right corner, look a little down and right of the
        // action so it sits up and left of middle, clear of the letter.
        if (letterBoard != null) want *= Quaternion.Euler(LetterLookDown, LetterLookRight, 0f);
        view.transform.position = Eye;
        view.transform.rotation = Quaternion.Slerp(view.transform.rotation, want, 1f - Mathf.Exp(-deltaTime * 3f));
    }

    private void UpdateLights(float deltaTime)
    {
        // The beam shines while the player aims and strikes, and stays on the struck hex while the
        // villain moves. It aims at a hex's top as it stood before sinking, so on a sunk hex it
        // shines into the hole, not past it onto the hex in front. On a new aim it swings across.
        bool lit = CurrentPhase == Phase.Aiming || CurrentPhase == Phase.Striking || CurrentPhase == Phase.MouseMoving;
        beamStrength = Mathf.MoveTowards(beamStrength, lit ? 1f : 0f, deltaTime * 2.5f);
        Vector3 target = Island.Top(Aim);
        if (!beamPlaced)
        {
            beamPoint = target;
            beamPlaced = true;
        }
        else beamPoint = Vector3.Lerp(beamPoint, target, 1f - Mathf.Exp(-deltaTime * BeamFollow));

        if (strikeTime >= 0f) strikeTime += deltaTime;
        float flash = strikeTime < 0f ? 0f : strikeTime < 0.08f ? strikeTime / 0.08f : Mathf.Exp(-(strikeTime - 0.08f) * 3f);
        if (strikeTime > 3f) strikeTime = -1f;
        // Refused (a house, a sunk hex, his own): the light flushes red for a moment.
        float red = Mathf.Clamp01(refused / 0.4f);
        Color warning = new Color(1f, 0.18f, 0.12f) * Mathf.Max(0.01f, beamColour.maxColorComponent);
        Color colour = Color.Lerp(beamColour, warning, red);
        bool showing = beamStrength > 0.001f;
        if (beam != null) beam.SetActive(showing);
        if (pool != null) pool.SetActive(showing);
        if (showing)
        {
            // The lit hex: the aimed hex's own outline, carried along with the beam as it swings.
            Vector2 slide = new Vector2(beamPoint.x - target.x, beamPoint.z - target.z);
            for (int i = 0; i < 6; i++)
            {
                Vector2 corner = Island.Grid.Corner(Aim, i) + slide;
                footprint[i] = new Vector3(corner.x, Island.LitHeight(corner.x, corner.y), corner.y);
            }
            if (beam != null)
            {
                ShapeShaft();
                beamMaterial.SetColor("_Color", colour * (1f + 2f * flash));
                beamMaterial.SetFloat("_Density", ShaftDensity * beamStrength);
            }
            if (pool != null)
            {
                LayPool(Island.Grid.Centre(Aim) + slide);
                poolMaterial.SetColor("_Color", colour * (PoolBrightness * beamStrength * (1f + 2.5f * flash)));
            }
        }
        if (strikeLight != null)
        {
            strikeLight.enabled = flash > 0.02f;
            if (strikeLight.enabled) PointSpot(strikeLight, Island.Top(struck), 1.5f, 9f * flash);
        }
    }

    // The aiming beam: a six-sided shaft from the lantern whose foot is the hex (Orca/Hex Beam),
    // and the hex itself lit, laid over the ground (added light in the seams' shader).
    private void MakeBeam()
    {
        beamColour = new Color(1f, 0.72f, 0.32f) * 1.6f;
        beamMaterial = MaterialFrom("MouseTrap/HexBeam");
        if (beamMaterial != null)
        {
            beamMaterial.name = "Aim beam";
            beamMaterial.SetFloat("_NearFade", 35f);
            beamMesh = new Mesh { name = "Aim beam" };
            beam = new GameObject("Aim beam");
            beam.transform.SetParent(lights.transform, false);
            // Its mesh is built in world space.
            beam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            beam.AddComponent<MeshFilter>().sharedMesh = beamMesh;
            MeshRenderer drawn = beam.AddComponent<MeshRenderer>();
            drawn.sharedMaterial = beamMaterial;
            drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            drawn.receiveShadows = false;
            beam.SetActive(false);
        }
        if (seam != null)
        {
            poolMaterial = new Material(seam) { name = "Aimed hex light" };
            poolMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            poolMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            poolMesh = new Mesh { name = "Aimed hex light" };
            pool = new GameObject("Aimed hex light");
            pool.transform.SetParent(lights.transform, false);
            pool.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            pool.AddComponent<MeshFilter>().sharedMesh = poolMesh;
            MeshRenderer drawn = pool.AddComponent<MeshRenderer>();
            drawn.sharedMaterial = poolMaterial;
            drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            drawn.receiveShadows = false;
            pool.SetActive(false);
        }
    }

    // The shaft: from the lantern through the lit hex's corners, a little past the ground (which
    // cuts it off), with its eight bounding planes for the shader.
    private void ShapeShaft()
    {
        Vector3 apex = LanternPosition;
        Vector3 middle = Vector3.zero;
        for (int i = 0; i < 6; i++) middle += footprint[i];
        middle /= 6f;
        Vector3 axis = (middle - apex).normalized;
        var far = new Vector3[6];
        float deepest = 0f;
        for (int i = 0; i < 6; i++)
        {
            far[i] = apex + (footprint[i] - apex) * 1.1f;
            deepest = Mathf.Max(deepest, Vector3.Dot(far[i] - apex, axis));
        }
        Vector3 farMiddle = apex + (middle - apex) * 1.1f;
        var positions = new List<Vector3>(8) { apex };
        positions.AddRange(far);
        positions.Add(farMiddle);
        beamMesh.SetVertices(positions);
        if (beamMesh.GetIndexCount(0) == 0)
        {
            var triangles = new List<int>(36);
            for (int i = 0; i < 6; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % 6;
                // Corners run anticlockwise seen from above; these face out of the shaft.
                triangles.AddRange(new[] { 0, b, a });
                triangles.AddRange(new[] { 7, a, b });
            }
            beamMesh.SetTriangles(triangles, 0);
        }
        beamMesh.RecalculateBounds();
        Vector3 inside = Vector3.Lerp(apex, farMiddle, 0.5f);
        for (int i = 0; i < 6; i++)
        {
            Vector3 normal = Vector3.Cross(far[i] - apex, far[(i + 1) % 6] - apex).normalized;
            if (Vector3.Dot(normal, inside - apex) > 0f) normal = -normal;
            shaftPlanes[i] = new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, apex));
        }
        Vector3 top = apex + axis * 0.5f;
        shaftPlanes[6] = new Vector4(-axis.x, -axis.y, -axis.z, Vector3.Dot(axis, top));
        Vector3 bottom = apex + axis * deepest;
        shaftPlanes[7] = new Vector4(axis.x, axis.y, axis.z, -Vector3.Dot(axis, bottom));
        beamMaterial.SetVectorArray("_HexBeamPlanes", shaftPlanes);
    }

    // The lit hex laid over whatever it falls on (the ground, or the water where a hex has gone),
    // brighter towards its edge so its outline reads crisply from the lantern.
    private void LayPool(Vector2 centre)
    {
        // Rings out from the middle, as shares of the way to the edge: the last two close together,
        // so the bright band along the edge is narrow and the hex's outline crisp.
        float[] rings = { 0f, 0.25f, 0.5f, 0.72f, 0.88f, 0.94f, 1f };
        int Steps = rings.Length - 1;
        var positions = new List<Vector3>(6 * (Steps + 1) * (Steps + 2) / 2);
        var colours = new List<Color>(positions.Capacity);
        var triangles = new List<int>(6 * Steps * Steps * 3);
        for (int side = 0; side < 6; side++)
        {
            Vector2 a = new Vector2(footprint[side].x, footprint[side].z) - centre;
            Vector2 b = new Vector2(footprint[(side + 1) % 6].x, footprint[(side + 1) % 6].z) - centre;
            int first = positions.Count;
            // A triangle of the hex (middle, corner, next corner) split into a grid of small ones.
            for (int ring = 0; ring <= Steps; ring++)
                for (int along = 0; along <= ring; along++)
                {
                    float r = rings[ring];
                    Vector2 p = centre + (ring == 0 ? Vector2.zero : Vector2.Lerp(a, b, along / (float)ring) * r);
                    positions.Add(new Vector3(p.x, Island.LitHeight(p.x, p.y) + 0.3f, p.y));
                    colours.Add(new Color(1f, 1f, 1f, r >= 0.94f ? 1f : r >= 0.88f ? 0.75f : 0.42f));
                }
            for (int ring = 0; ring < Steps; ring++)
            {
                int row = first + ring * (ring + 1) / 2, next = first + (ring + 1) * (ring + 2) / 2;
                for (int along = 0; along <= ring; along++)
                {
                    triangles.AddRange(new[] { row + along, next + along, next + along + 1 });
                    if (along < ring) triangles.AddRange(new[] { row + along, next + along + 1, row + along + 1 });
                }
            }
        }
        poolMesh.Clear();
        poolMesh.SetVertices(positions);
        poolMesh.SetColors(colours);
        poolMesh.SetTriangles(triangles, 0);
        poolMesh.RecalculateBounds();
    }

    // A spot from the lantern onto a hex, wide enough for it, as bright there as asked (spot
    // lights fall off with the square of the distance).
    private void PointSpot(Light light, Vector3 at, float widen, float brightness)
    {
        Vector3 from = LanternPosition;
        Vector3 to = at - from;
        float distance = Mathf.Max(1f, to.magnitude);
        light.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to / distance));
        light.range = distance + 80f;
        light.spotAngle = Mathf.Clamp(2f * Mathf.Atan(hexRadius * widen / distance) * Mathf.Rad2Deg, 1f, 60f);
        light.innerSpotAngle = light.spotAngle * 0.6f;
        light.intensity = brightness * distance * distance;
    }

    // ---- Screen ------------------------------------------------------------------------------

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        GUI.depth = -60;
        if (active != this)
        {
            if (active == null && entrance != null && IsUnlocked)
                InteractPrompt.Draw(entrance.position + Vector3.up * 2f, 10f, "E", "Climb the lighthouse", 3, prompt);
            return;
        }
        int p = Mathf.Max(1, Mathf.RoundToInt(3f * InteractPrompt.UIScale()));
        if (!PauseMenu.IsPaused) Sightline?.Draw();
        bool playing = CurrentPhase == Phase.Aiming || CurrentPhase == Phase.Striking || CurrentPhase == Phase.MouseMoving ||
            CurrentPhase == Phase.Splitting;
        if (playing && !PauseMenu.IsPaused)
        {
            Centred(22f, "TRAP THE VILLAIN", p, Gold);
            Centred(54f, "DON'T LET HIM REACH THE SEA", p, Color.white);
            // With the letter up, the controls are written on it instead.
            if (letterBoard == null) Centred(Screen.height - 48f, "[WASD] AIM    [SPACE] STRIKE", p, Color.white);
        }
        if (message != null && (CurrentPhase == Phase.Caught || CurrentPhase == Phase.Escaped))
            Centred(Screen.height * 0.3f, message, p * 2, Gold);
        if (fade > 0f)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, fade);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }
        if (CurrentPhase == Phase.Ending && Ending != null && textFade > 0f)
        {
            int big = Mathf.Max(1, Mathf.RoundToInt(4f * InteractPrompt.UIScale()));
            List<string> lines = PixelFont.Wrap(Ending, Mathf.FloorToInt(Screen.width * 0.85f / big));
            float height = PixelFont.CapHeight * big * 2.2f;
            float y = Screen.height * 0.5f - lines.Count * height * 0.5f;
            Color colour = new Color(Gold.r, Gold.g, Gold.b, textFade);
            foreach (string line in lines)
            {
                Centred(y, line, big, colour);
                y += height;
            }
        }
    }

    private static void Centred(float y, string text, int p, Color colour)
    {
        float x = (Screen.width - PixelFont.Measure(text) * p) * 0.5f;
        PixelFont.Draw(new Vector2(x, y), text, p, colour, new Color(0f, 0f, 0f, 0.9f * colour.a));
    }
}
