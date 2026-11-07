using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The clue letter's potion hunt, played on the island itself. The letter holds a hand-drawn
// map of the island cut into the hunt's checkerboard; its inventory number (or a click on its
// slot) slides it up into the bottom-right corner and puts it away again. [R] starts a hunt
// from wherever the player stands on the island: five potions, one of each colour, are set out
// across the map, faint light walls rise from the cell borders round the player, and every
// border crossed costs a step. A crayon trail follows the player on the map. Out of steps, the
// letter burns and rebuilds with the hunt cleared, ready to start again anywhere. With all five
// found the alchemist's house glows on the map, and the alchemist writes the clue word on the
// letter (Deliver). The survey and routes are ClueMap; the art and meshes ClueMapArt.
public class ClueMinigame : MonoBehaviour
{
    public enum HuntState { Idle, Playing, Found, Delivered }

    public const int PotionCount = 5;
    public const string ClueWord = "Fell";
    public const string GroundName = "IslandDemo:pCube1";
    public const string HouseName = "building3";

    // Spots surveyed per frame, a few milliseconds of physics queries.
    private const int SurveySlice = 1500;
    private const float SlideDuration = 0.35f;
    // Share of the screen's area the held letter covers, and its gap to the screen's edges.
    private const float ScreenShare = 0.2f;
    private const float EdgeGap = 14f;
    // Letter pixels per font pixel for the status line.
    private const float StatusFontPixels = 1.6f;
    // A cell only counts as left once the player is this far past its border, so walking along
    // a border doesn't eat steps.
    private const float BorderGrace = 0.6f;
    private const float CollectRadius = 1.3f;
    // Walk speed while a hunt is on.
    private const float HuntSpeedBoost = 1.6f;
    private const float WallHeight = 3f;
    // The light walls: full strength on the player's cell, NearGlow on the rest of the 3 x 3 round
    // it, cross-fading over CellFadeSeconds when the player moves to another cell.
    private const float WallIntensity = 0.23f;
    private const float NearGlow = 0.4f;
    private const float CellFadeSeconds = 0.25f;
    private const float WallFadeSeconds = 0.6f;
    private const float PotionPixelsPerUnit = 11f;
    private const float PopSeconds = 0.35f;
    private const float MessageSeconds = 2.5f;
    private static readonly Vector2 ClueCentre = new Vector2(80f, 46f);
    private static readonly Color HouseGlow = new Color(1f, 0.84f, 0.4f, 1f);
    private static readonly Color32 StatusInk = new Color32(80, 68, 56, 255);

    [SerializeField]
    private string triggerItem = "Clue Letter";

    // The player's clue letter, for the inventory to hold up and the alchemist to finish.
    public static ClueMinigame Current { get; private set; }

    public HuntState State { get; private set; }
    public bool IsHeld { get; private set; }
    public int StepsLeft { get; private set; }
    public int StepBudget { get; private set; }
    public bool IsFound => State == HuntState.Found;
    public bool IsDelivered => State == HuntState.Delivered;
    public bool IsBurning => burn != null && burn.IsPlaying;
    public ClueMap Map => map;

    public int PotionsLeft
    {
        get
        {
            int left = 0;
            foreach (Potion potion in potions) if (!potion.Collected) left++;
            return left;
        }
    }

    // Where the uncollected potions stand, for tests and debugging.
    public List<Vector3> PotionPositions
    {
        get
        {
            var positions = new List<Vector3>();
            foreach (Potion potion in potions) if (!potion.Collected) positions.Add(potion.Rest);
            return positions;
        }
    }

    private sealed class Potion
    {
        public int Colour;
        public Vector3 Rest;
        public GameObject Body;
        public SpriteRenderer Sprite;
        public Light Glow;
        public bool Collected;
        public float Pop = -1f;
        public float Phase;
    }

    // Runtime state, rebuilt rather than restored after a script reload in Play mode.
    [System.NonSerialized] private ClueMap map;
    [System.NonSerialized] private ClueMapArt.Frame frame;
    [System.NonSerialized] private readonly List<Potion> potions = new List<Potion>();
    [System.NonSerialized] private Color32[] marksPixels;
    [System.NonSerialized] private Color32[] trailPixels;
    [System.NonSerialized] private Sprite[] potionSprites;
    [System.NonSerialized] private Mesh[] beamMeshes;
    [System.NonSerialized] private CursiveWriter clueWriter;
    private bool surveyFailed;
    private Transform player;
    private InventorySystem inventory;
    private Vector2Int currentCell;
    private Vector3 startPosition;
    private bool hasStart;
    private Vector2 lastTrail;
    private bool hasTrail;
    private bool trailDirty;
    private bool pendingClue;
    private float slide;
    private string message;
    private float messageUntil;

    private Canvas canvas;
    private RectTransform surface;
    private Image paper;
    private Sprite fallbackPaper;
    private RawImage glowImage;
    private RawImage marksImage;
    private RawImage trailImage;
    private RawImage cluePatch;
    private RawImage clueInk;
    private RawImage markerImage;
    private PixelText stepsLabel;
    private PixelText statusLabel;
    private Texture2D mapTexture;
    private Texture2D marksTexture;
    private Texture2D trailTexture;
    private Texture2D glowTexture;
    private Texture2D markerTexture;
    private bool mapDrawn;
    private Rect house;
    private LetterBurn burn;

    private GameObject walls;
    private Material wallMaterial;
    private Material beamMaterial;
    private Mesh wallMesh;
    private float wallFade;
    private Vector2Int glowCell;
    private Vector2Int glowPrevious;
    private float glowBlend = 1f;
    private PlayerController mover;
    private bool boosted;

    public bool Handles(string itemName)
    {
        return itemName == triggerItem;
    }

    private bool Owned => inventory != null && inventory.Contains(triggerItem);

    private static bool Blocked => PauseMenu.IsPaused || TitleScreen.IsOpen || InventoryInspection.IsOpen || Interactables.Busy;

    // Holds the letter up or puts it away. False when there is no letter to hold.
    public bool ToggleHeld() => SetHeld(!IsHeld);

    public bool SetHeld(bool held)
    {
        if (held && !Owned) return false;
        if (IsHeld == held) return true;
        IsHeld = held;
        if (held) EnsureView();
        if (inventory != null) inventory.NotifyInteraction();
        return true;
    }

    private void Awake()
    {
        inventory = GetComponent<InventorySystem>();
    }

    private void OnEnable() => Current = this;

    private void OnDisable()
    {
        if (Current == this) Current = null;
        Boost(false);
    }

    // The hunt's walk speed boost, on while potions are being hunted.
    private void Boost(bool on)
    {
        if (mover == null && player != null) mover = player.GetComponent<PlayerController>();
        if (mover == null || boosted == on) return;
        mover.SpeedMultiplier = on ? HuntSpeedBoost : 1f;
        boosted = on;
    }

    private void OnDestroy()
    {
        ClearPotions();
        Release(walls);
        Release(wallMaterial);
        Release(beamMaterial);
        Release(wallMesh);
        if (beamMeshes != null) foreach (Mesh mesh in beamMeshes) Release(mesh);
        if (potionSprites != null)
        {
            foreach (Sprite sprite in potionSprites)
            {
                if (sprite == null) continue;
                Release(sprite.texture);
                Release(sprite);
            }
        }
        Release(mapTexture);
        Release(marksTexture);
        Release(trailTexture);
        Release(glowTexture);
        Release(markerTexture);
        if (fallbackPaper != null)
        {
            Release(fallbackPaper.texture);
            Release(fallbackPaper);
        }
        if (Application.isPlaying) burn?.Dispose();
        if (clueWriter != null) Release(clueWriter.Texture);
    }

    private static void Release(Object owned)
    {
        if (owned == null) return;
        if (Application.isPlaying) Destroy(owned);
        else DestroyImmediate(owned);
    }

    private void Update()
    {
        if (inventory == null) inventory = InventorySystem.instance;
        if (player == null) player = Interactables.FindPlayer();
        SurveyStep();
        if (IsHeld && !Owned) SetHeld(false);

        bool blocked = Blocked;
        if (IsBurning)
        {
            // Put away or covered mid-burn: the reset happens at once.
            if (!IsHeld || blocked) burn.Cancel();
            else burn.Tick(Time.unscaledDeltaTime);
        }
        if (clueWriter != null && clueWriter.IsPlaying) clueWriter.Tick(Time.unscaledDeltaTime);
        if (pendingClue && !blocked && Owned)
        {
            pendingClue = false;
            SetHeld(true);
            ShowClue(true);
        }

        Keyboard keyboard = Keyboard.current;
        if (IsHeld && !blocked && !IsBurning && keyboard != null && keyboard.rKey.wasPressedThisFrame)
        {
            if (State == HuntState.Playing) Burn(true);
            else if (State == HuntState.Idle && player != null) StartHunt(player.position);
        }

        if (player != null && !PauseMenu.IsPaused) Track(player.position);
        Boost(State == HuntState.Playing);
        AnimateWorld(Time.deltaTime);
    }

    // ---- Survey ------------------------------------------------------------------------

    private void SurveyStep()
    {
        if (map == null)
        {
            // The opening sails the player in and moors its boat; survey the island once it is over.
            if (TitleScreen.IsOpen || !BeginSurvey()) return;
        }
        if (!map.IsReady && map.Advance(SurveySlice)) OnSurveyed();
    }

    // Surveys the whole island at once (the hunt can't wait for the slices).
    public void SurveyNow()
    {
        if (map == null && !BeginSurvey()) return;
        if (!map.IsReady && map.Advance(int.MaxValue)) OnSurveyed();
    }

    private bool BeginSurvey()
    {
        if (surveyFailed) return false;
        GameObject groundObject = GameObject.Find(GroundName);
        Collider ground = groundObject != null ? groundObject.GetComponent<Collider>() : null;
        if (ground == null)
        {
            surveyFailed = true;
            Debug.LogWarning("[ClueMinigame] No " + GroundName + " collider to map the island from; the potion hunt is off.");
            return false;
        }
        GameObject oceanObject = GameObject.Find("ocean");
        Collider ocean = oceanObject != null ? oceanObject.GetComponent<Collider>() : null;
        if (player == null) player = Interactables.FindPlayer();
        Collider[] ignore = player != null ? player.GetComponentsInChildren<Collider>(true) : new Collider[0];
        map = new ClueMap(ground, ocean, ignore);
        frame = new ClueMapArt.Frame(ground.bounds, ClueMapArt.MapArea);
        return true;
    }

    private void OnSurveyed()
    {
        DrawMapIfReady();
        Shader light = Resources.Load<Shader>("ClueMap/ClueGridLight");
        if (light == null)
        {
            Debug.LogWarning("[ClueMinigame] Missing Resources/ClueMap/ClueGridLight; no light walls or potion beams.");
            return;
        }
        wallMaterial = new Material(light) { name = "Clue grid walls", hideFlags = HideFlags.HideAndDontSave };
        beamMaterial = new Material(light) { name = "Clue potion beams", hideFlags = HideFlags.HideAndDontSave };
        beamMaterial.SetFloat("_Intensity", 0.7f);
        beamMaterial.SetFloat("_FadePower", 1.3f);
        beamMaterial.SetFloat("_EdgeSoftness", 0.9f);
        beamMaterial.SetColor("_Color", Color.white);
        wallMaterial.SetFloat("_Intensity", WallIntensity);
        wallMaterial.SetFloat("_UseCells", 1f);
        wallMaterial.SetFloat("_NearGlow", NearGlow);
        wallMaterial.SetVector("_Grid", new Vector4(map.Origin.x, map.Origin.y, map.CellSize, 0f));
        wallMesh = ClueMapArt.GridWalls(map, WallHeight);
        walls = new GameObject("Clue grid walls");
        walls.AddComponent<MeshFilter>().sharedMesh = wallMesh;
        MeshRenderer renderer = walls.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = wallMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        walls.SetActive(false);
    }

    // ---- The hunt ----------------------------------------------------------------------

    // Starts a hunt from where the player stands: false (with a note on the letter) off the
    // island, mid-hunt or when the potions can't be placed.
    public bool StartHunt(Vector3 at)
    {
        if (State != HuntState.Idle || IsBurning) return false;
        SurveyNow();
        if (map == null || !map.IsReady) return false;
        int start = map.IsLand(map.SampleAt(at)) ? map.NearestWalkable(at, 3) : -1;
        var plan = new List<int>();
        if (start < 0 || !map.PlanHunt(start, PotionCount, new System.Random(), plan, out int budget))
        {
            Say(start < 0 ? "STAND ON THE ISLAND TO START" : "NO ROOM FOR POTIONS HERE");
            return false;
        }

        // Each potion takes a different colour, shuffled.
        var colours = new List<int>();
        for (int c = 0; c < ClueMapArt.PotionColors.Length; c++) colours.Add(c);
        var random = new System.Random();
        for (int k = 0; k < plan.Count; k++)
        {
            int pick = random.Next(colours.Count);
            SpawnPotion(map.SamplePosition(plan[k]), colours[pick]);
            colours.RemoveAt(pick);
        }
        StepBudget = budget;
        StepsLeft = budget;
        currentCell = map.CellAt(at);
        glowCell = glowPrevious = currentCell;
        glowBlend = 1f;
        startPosition = at;
        hasStart = true;
        ClearTrail();
        State = HuntState.Playing;
        DrawTrail(at);
        RedrawMarks();
        return true;
    }

    // Each frame of a hunt: collects potions the player reaches, draws the trail and charges a
    // step for every cell border crossed. Running out of steps burns the letter.
    public void Track(Vector3 position)
    {
        if (State != HuntState.Playing || IsBurning || map == null) return;
        foreach (Potion potion in potions)
        {
            if (potion.Collected) continue;
            Vector3 d = position - potion.Rest;
            if (d.x * d.x + d.z * d.z <= CollectRadius * CollectRadius && Mathf.Abs(d.y) < 2.5f) Collect(potion);
        }
        if (State != HuntState.Playing) return;
        DrawTrail(position);

        Rect cell = map.CellRect(currentCell);
        if (position.x >= cell.xMin - BorderGrace && position.x <= cell.xMax + BorderGrace &&
            position.z >= cell.yMin - BorderGrace && position.z <= cell.yMax + BorderGrace) return;
        Vector2Int next = map.CellAt(position);
        int crossed = Mathf.Abs(next.x - currentCell.x) + Mathf.Abs(next.y - currentCell.y);
        currentCell = next;
        if (crossed > StepsLeft)
        {
            // No steps left to cross with: the letter goes up in flames.
            StepsLeft = 0;
            Burn(false);
            return;
        }
        StepsLeft -= crossed;
    }

    private void Collect(Potion potion)
    {
        potion.Collected = true;
        potion.Pop = 0f;
        RedrawMarks();
        if (PotionsLeft > 0) return;
        State = HuntState.Found;
        // Up comes the letter, with the alchemist's house glowing on it.
        if (!Blocked) SetHeld(true);
    }

    // The alchemist writes the clue word on the letter once every potion is found.
    public bool Deliver()
    {
        if (State != HuntState.Found) return false;
        State = HuntState.Delivered;
        if (inventory != null) inventory.RevealClueWords(ClueWord);
        pendingClue = true;
        return true;
    }

    // The letter burns up from the bottom; while it is ash the hunt is cleared (and a new one
    // starts where the player stands when regenerate is set), then the letter rebuilds.
    private void Burn(bool regenerate)
    {
        void Rebuild()
        {
            ResetHunt();
            if (regenerate && player != null) StartHunt(player.position);
        }

        if (Blocked || !SetHeld(true) || surface == null)
        {
            Rebuild();
            return;
        }
        canvas.gameObject.SetActive(true);
        Canvas.ForceUpdateCanvases();
        burn ??= new LetterBurn(surface, paper);
        burn.Play(Rebuild);
    }

    private void ResetHunt()
    {
        ClearPotions();
        hasStart = false;
        StepsLeft = 0;
        StepBudget = 0;
        State = HuntState.Idle;
        ClearTrail();
        RedrawMarks();
    }

    private void Say(string text)
    {
        message = text;
        messageUntil = Time.unscaledTime + MessageSeconds;
    }

    // ---- Potions and walls on the island -----------------------------------------------

    private void SpawnPotion(Vector3 rest, int colour)
    {
        EnsurePotionArt();
        var body = new GameObject("Clue potion (" + colour + ")");
        body.transform.position = rest;
        SpriteRenderer sprite = body.AddComponent<SpriteRenderer>();
        sprite.sprite = potionSprites[colour];
        body.AddComponent<GrassBillboard>();

        var glowObject = new GameObject("Glow");
        glowObject.transform.SetParent(body.transform, false);
        glowObject.transform.localPosition = Vector3.up * 0.5f;
        Light glow = glowObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = ClueMapArt.PotionColors[colour];
        glow.intensity = 2.5f;
        glow.range = 4.5f;
        glow.shadows = LightShadows.None;

        if (beamMaterial != null)
        {
            var beam = new GameObject("Beam");
            beam.transform.SetParent(body.transform, false);
            beam.AddComponent<MeshFilter>().sharedMesh = beamMeshes[colour];
            MeshRenderer renderer = beam.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = beamMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        potions.Add(new Potion
        {
            Colour = colour,
            Rest = rest,
            Body = body,
            Sprite = sprite,
            Glow = glow,
            Phase = Random.value * Mathf.PI * 2f,
        });
    }

    private void EnsurePotionArt()
    {
        int count = ClueMapArt.PotionColors.Length;
        if (potionSprites != null && potionSprites.Length == count && System.Array.TrueForAll(potionSprites, s => s != null)) return;
        potionSprites = new Sprite[count];
        beamMeshes = new Mesh[count];
        for (int c = 0; c < count; c++)
        {
            Texture2D texture = ClueMapArt.PotionTexture(ClueMapArt.PotionColors[c], 1, "Clue potion " + c);
            potionSprites[c] = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0f), PotionPixelsPerUnit);
            potionSprites[c].name = "Clue potion " + c;
            Color32 beam = ClueMapArt.PotionColors[c];
            beamMeshes[c] = ClueMapArt.Beam(beam, 0.8f, 9f);
        }
    }

    private void ClearPotions()
    {
        foreach (Potion potion in potions) Release(potion.Body);
        potions.Clear();
    }

    // Potions bob and pop when collected; the walls fade in for a hunt and light the cells round the player.
    private void AnimateWorld(float deltaTime)
    {
        for (int k = potions.Count - 1; k >= 0; k--)
        {
            Potion potion = potions[k];
            if (potion.Body == null) continue;
            if (potion.Pop >= 0f)
            {
                potion.Pop += deltaTime;
                float t = Mathf.Clamp01(potion.Pop / PopSeconds);
                potion.Body.transform.localScale = Vector3.one * (1f + 0.8f * t);
                potion.Sprite.color = new Color(1f, 1f, 1f, 1f - t);
                potion.Glow.intensity = 2.5f * (1f - t);
                if (t >= 1f)
                {
                    Release(potion.Body);
                    potion.Body = null;
                }
                continue;
            }
            float bob = 0.12f + Mathf.Sin(Time.time * 2f + potion.Phase) * 0.08f;
            potion.Body.transform.position = potion.Rest + Vector3.up * bob;
        }

        if (walls == null) return;
        wallFade = Mathf.MoveTowards(wallFade, State == HuntState.Playing ? 1f : 0f, deltaTime / WallFadeSeconds);
        if (walls.activeSelf != wallFade > 0f) walls.SetActive(wallFade > 0f);
        wallMaterial.SetFloat("_Fade", wallFade);
        if (currentCell != glowCell)
        {
            glowPrevious = glowCell;
            glowCell = currentCell;
            glowBlend = 0f;
        }
        glowBlend = Mathf.MoveTowards(glowBlend, 1f, deltaTime / CellFadeSeconds);
        wallMaterial.SetVector("_Cells", new Vector4(glowCell.x, glowCell.y, glowPrevious.x, glowPrevious.y));
        wallMaterial.SetFloat("_CellBlend", glowBlend);
    }

    // ---- The letter --------------------------------------------------------------------

    private void EnsureView()
    {
        if (canvas != null)
        {
            DrawMapIfReady();
            return;
        }

        var root = new GameObject("Clue letter", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Under the inspection view and the doorway rooms, over the world.
        canvas.sortingOrder = 90;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        surface = new GameObject("Held letter", typeof(RectTransform)).GetComponent<RectTransform>();
        surface.SetParent(root.transform, false);
        surface.anchorMin = surface.anchorMax = surface.pivot = new Vector2(1f, 0f);
        surface.sizeDelta = new Vector2(ClueMapArt.LetterWidth, ClueMapArt.LetterHeight);

        paper = Layer<Image>("Paper");
        paper.sprite = LetterSprite();
        mapTexture = ClueMapArt.NewTexture("Clue map", ClueMapArt.LetterWidth, ClueMapArt.LetterHeight);
        mapTexture.SetPixels32(new Color32[ClueMapArt.LetterWidth * ClueMapArt.LetterHeight]);
        mapTexture.Apply();
        Layer<RawImage>("Map").texture = mapTexture;
        glowTexture = ClueMapArt.GlowTexture();
        glowImage = Layer<RawImage>("House glow");
        glowImage.texture = glowTexture;
        glowImage.color = HouseGlow;
        glowImage.gameObject.SetActive(false);
        marksPixels = new Color32[ClueMapArt.LetterWidth * ClueMapArt.LetterHeight];
        marksTexture = ClueMapArt.NewTexture("Clue marks", ClueMapArt.LetterWidth, ClueMapArt.LetterHeight);
        marksImage = Layer<RawImage>("Marks");
        marksImage.texture = marksTexture;
        trailPixels = new Color32[ClueMapArt.LetterWidth * ClueMapArt.LetterHeight];
        trailTexture = ClueMapArt.NewTexture("Clue trail", ClueMapArt.LetterWidth, ClueMapArt.LetterHeight);
        trailImage = Layer<RawImage>("Crayon trail");
        trailImage.texture = trailTexture;
        trailDirty = true;
        cluePatch = Layer<RawImage>("Clue patch");
        cluePatch.texture = glowTexture;
        cluePatch.color = new Color32(236, 222, 186, 235);
        cluePatch.gameObject.SetActive(false);
        clueInk = Layer<RawImage>("Clue");
        clueInk.gameObject.SetActive(false);
        markerTexture = ClueMapArt.MarkerTexture();
        markerImage = Layer<RawImage>("You are here");
        markerImage.texture = markerTexture;
        stepsLabel = Label("Steps", TextAnchor.MiddleLeft);
        Place(stepsLabel.rectTransform, new Vector2(100f, ClueMapArt.StatusY), new Vector2(172f, 14f));
        statusLabel = Label("Status", TextAnchor.MiddleRight);
        Place(statusLabel.rectTransform, new Vector2(266f, ClueMapArt.StatusY), new Vector2(168f, 14f));
        root.SetActive(false);

        DrawMapIfReady();
        RedrawMarks();
        if (State == HuntState.Delivered && !pendingClue) ShowClue(false);
    }

    private T Layer<T>(string name) where T : Graphic
    {
        T graphic = new GameObject(name, typeof(RectTransform), typeof(T)).GetComponent<T>();
        graphic.transform.SetParent(surface, false);
        graphic.raycastTarget = false;
        RectTransform rect = graphic.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return graphic;
    }

    private PixelText Label(string name, TextAnchor alignment)
    {
        PixelText label = Layer<PixelText>(name);
        label.color = StatusInk;
        label.alignment = alignment;
        label.uppercase = true;
        label.highlightBrackets = true;
        label.keyColor = new Color32(150, 40, 30, 255);
        return label;
    }

    // Spans a box measured in letter pixels from the top-left, so it scales with the letter.
    private static void Place(RectTransform rect, Vector2 centre, Vector2 size)
    {
        rect.anchorMin = OnLetter(centre + new Vector2(-size.x, size.y) * 0.5f);
        rect.anchorMax = OnLetter(centre + new Vector2(size.x, -size.y) * 0.5f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static Vector2 OnLetter(Vector2 letterPixels)
    {
        return new Vector2(letterPixels.x / ClueMapArt.LetterWidth, 1f - letterPixels.y / ClueMapArt.LetterHeight);
    }

    // The letter's own artwork from the inventory (Letter_screen.png), or plain parchment.
    private Sprite LetterSprite()
    {
        if (inventory != null)
        {
            for (int i = 0; i < inventory.ItemCount(); i++)
            {
                if (Handles(inventory.GetItem(i)) && inventory.GetSprite(i) != null) return inventory.GetSprite(i);
            }
        }
        if (fallbackPaper == null) fallbackPaper = ClueMapArt.PaperSprite();
        return fallbackPaper;
    }

    private void DrawMapIfReady()
    {
        if (mapDrawn || mapTexture == null || map == null || !map.IsReady) return;
        GameObject fountainObject = GameObject.Find("IslandDemo:pCylinder3");
        Renderer fountain = fountainObject != null ? fountainObject.GetComponent<Renderer>() : null;
        var lamps = new List<Vector3>();
        foreach (LampGlow lamp in FindObjectsByType<LampGlow>()) lamps.Add(lamp.transform.position);
        mapTexture.SetPixels32(ClueMapArt.DrawMap(map, frame, fountain != null ? fountain.bounds.center : (Vector3?)null, lamps));
        mapTexture.Apply();
        mapDrawn = true;
        LocateHouse();
    }

    // The alchemist's house on the letter, for its glow: building3's footprint, or the
    // alchemist's door when the house can't be found.
    private void LocateHouse()
    {
        GameObject building = GameObject.Find(HouseName);
        Renderer[] renderers = building != null ? building.GetComponentsInChildren<Renderer>() : new Renderer[0];
        Bounds bounds;
        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        }
        else
        {
            AlchemistEncounter door = FindAnyObjectByType<AlchemistEncounter>();
            if (door == null)
            {
                house = Rect.zero;
                return;
            }
            bounds = new Bounds(door.transform.position, Vector3.one * 8f);
        }
        Vector2 a = frame.ToLetter(bounds.min);
        Vector2 b = frame.ToLetter(bounds.max);
        house = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    private void RedrawMarks()
    {
        if (marksPixels == null) return;
        System.Array.Clear(marksPixels, 0, marksPixels.Length);
        if (hasStart) ClueMapArt.DrawRing(marksPixels, frame.ToLetter(startPosition), 3, ClueMapArt.Ink);
        foreach (Potion potion in potions)
        {
            Vector2 at = frame.ToLetter(potion.Rest);
            if (potion.Collected) ClueMapArt.DrawCross(marksPixels, at);
            else ClueMapArt.DrawPotion(marksPixels, at, ClueMapArt.PotionColors[potion.Colour]);
        }
        marksTexture.SetPixels32(marksPixels);
        marksTexture.Apply();
    }

    private void ClearTrail()
    {
        hasTrail = false;
        if (trailPixels == null) return;
        System.Array.Clear(trailPixels, 0, trailPixels.Length);
        trailDirty = true;
    }

    // Crayon along the player's path, in dabs half a letter pixel apart.
    private void DrawTrail(Vector3 position)
    {
        if (trailPixels == null) return;
        Vector2 at = frame.ToLetter(position);
        if (hasTrail && Vector2.Distance(at, lastTrail) < 0.5f) return;
        Vector2 from = hasTrail ? lastTrail : at;
        int dabs = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, at) / 0.5f));
        for (int i = 1; i <= dabs; i++) ClueMapArt.StampCrayon(trailPixels, Vector2.Lerp(from, at, i / (float)dabs));
        lastTrail = at;
        hasTrail = true;
        trailDirty = true;
    }

    // The clue word written in cursive on a cleared patch of the map; animate writes it out.
    private void ShowClue(bool animate)
    {
        EnsureView();
        if (clueWriter == null) clueWriter = new CursiveWriter(ClueWord, 150f, 17f, 4);
        clueInk.texture = clueWriter.Texture;
        Place(clueInk.rectTransform, ClueCentre, clueWriter.SizeInLetterPixels);
        Place(cluePatch.rectTransform, ClueCentre, clueWriter.SizeInLetterPixels * 1.4f);
        clueInk.gameObject.SetActive(true);
        cluePatch.gameObject.SetActive(true);
        if (animate) clueWriter.Play();
        else if (!clueWriter.HasStarted) clueWriter.Complete();
    }

    private void LateUpdate()
    {
        if (canvas == null) return;
        bool show = IsHeld && !Blocked;
        slide = Mathf.MoveTowards(slide, show ? 1f : 0f, Time.unscaledDeltaTime / SlideDuration);
        bool active = slide > 0f || IsBurning;
        if (canvas.gameObject.activeSelf != active) canvas.gameObject.SetActive(active);
        if (!active) return;

        // About a fifth of the screen, in the bottom-right corner, sliding up from below it.
        Rect bounds = ((RectTransform)canvas.transform).rect;
        float aspect = paper.sprite != null ? paper.sprite.rect.width / paper.sprite.rect.height : 1.6f;
        float height = Mathf.Sqrt(ScreenShare * bounds.width * bounds.height / aspect);
        height = Mathf.Min(height, bounds.height * 0.9f, bounds.width * 0.9f / aspect);
        surface.sizeDelta = new Vector2(height * aspect, height);
        float eased = 1f - Mathf.Pow(1f - slide, 3f);
        surface.anchoredPosition = new Vector2(-EdgeGap, Mathf.Lerp(-height - EdgeGap, EdgeGap, eased));

        float unit = height / ClueMapArt.LetterHeight;
        stepsLabel.fontSize = PixelFont.CapHeight * StatusFontPixels * unit;
        statusLabel.fontSize = stepsLabel.fontSize;
        UpdateLabels();
        UpdateMarker();
        UpdateGlow();
        if (trailDirty)
        {
            trailTexture.SetPixels32(trailPixels);
            trailTexture.Apply();
            trailDirty = false;
        }
    }

    private void UpdateLabels()
    {
        string steps = "";
        string status;
        if (Time.unscaledTime < messageUntil) status = message;
        else if (map == null || !map.IsReady) status = surveyFailed ? "NO MAP OF THIS PLACE" : "MAPPING THE ISLAND...";
        else if (State == HuntState.Idle) status = "[R] START THE HUNT HERE";
        else if (State == HuntState.Playing) status = "POTIONS " + (PotionCount - PotionsLeft) + "/" + PotionCount;
        else if (State == HuntState.Found) status = "BACK TO THE ALCHEMIST!";
        else status = "";
        if (State == HuntState.Playing) steps = "STEPS LEFT " + StepsLeft;
        else if (State == HuntState.Found) steps = "ALL FIVE FOUND";
        stepsLabel.text = steps;
        statusLabel.text = status;
    }

    private void UpdateMarker()
    {
        bool visible = player != null && map != null && !IsBurning;
        Vector2 at = visible ? frame.ToLetter(player.position) : Vector2.zero;
        RectInt area = ClueMapArt.MapArea;
        visible &= at.x >= area.xMin && at.x <= area.xMax && at.y >= area.yMin && at.y <= area.yMax;
        markerImage.gameObject.SetActive(visible);
        if (!visible) return;
        Place(markerImage.rectTransform, at, new Vector2(11f, 11f));
        markerImage.color = new Color(1f, 1f, 1f, 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 6f));
    }

    private void UpdateGlow()
    {
        bool glowing = State == HuntState.Found && house.width > 0f;
        glowImage.gameObject.SetActive(glowing);
        if (!glowing) return;
        float size = Mathf.Max(house.width, house.height) * 2.4f + 16f;
        Place(glowImage.rectTransform, house.center, new Vector2(size, size));
        Color color = HouseGlow;
        color.a = 0.55f + 0.35f * Mathf.Sin(Time.unscaledTime * 3f);
        glowImage.color = color;
    }
}
