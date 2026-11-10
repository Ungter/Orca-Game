using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Where the twenty-one game is played, Buckshot Roulette style: a dark room under one hanging
// lamp, Al Cachud seated across the table and the tiles laid out on the felt in front of the
// player. Each roll throws a die across the table, then a cylinder counter hops along the tiles
// it takes. The room floats above the game and only the seated camera sees it.
public sealed class TwentyOneTable : System.IDisposable
{
    // An unnamed layer nothing else uses; while seated, the camera renders only this.
    public const int Layer = 31;
    // URP "Light Layer 7": the moon and street lamps leave the room out, and its lamp lights
    // (and shadows) nothing else.
    public const int LightLayer = 7;
    public const float Height = 150f;
    public const float TableTop = 0.82f;
    public const float Surface = TableTop + 0.001f;
    public const float FeltHalfWidth = 1.35f;
    public const float FeltHalfDepth = 1.05f;
    public const float DieSize = 0.1f;
    public const float FieldOfView = 55f;
    private const float WideAspect = 16f / 9f;
    private const float BoardZ = -0.42f;
    private const float BoardWidth = 2f;
    private const float MaxPitch = 0.26f;
    private const float TokenHeight = 0.1f;
    private const float HopHeight = 0.08f;
    public static readonly Vector3 SeatOffset = new Vector3(0f, 1.62f, -1.75f);
    public static readonly Vector3 OpponentOffset = new Vector3(0f, 0f, 1.35f);
    private static readonly Vector3 LookOffset = new Vector3(0f, 0.95f, 0.2f);
    public static readonly Color Red = new Color32(231, 91, 82, 255);
    private static readonly Color Gold = new Color32(242, 211, 107, 255);
    private static readonly Color Muted = new Color32(137, 153, 161, 255);
    private static readonly Color TakenFrame = new Color(0.15f, 0.2f, 0.24f);
    private static readonly Color Well = new Color32(20, 31, 42, 255);
    private static readonly Color Paper = new Color32(241, 226, 185, 255);
    // The sprite is unlit; this matches it to the lamp-lit table.
    private static readonly Color LampLitSprite = new Color(0.86f, 0.76f, 0.64f);
    // Opposite faces add up to four, as on a six-sided die numbered 1-3 twice.
    private static readonly Vector3[] FaceNormals =
        { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
    private static readonly int[] FaceValues = { 1, 3, 2, 2, 3, 1 };
    private static readonly Dictionary<PrimitiveType, Mesh> Meshes = new Dictionary<PrimitiveType, Mesh>();
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    private readonly List<Object> owned = new List<Object>();
    private readonly Transform anchor;
    private readonly GameObject root;
    private readonly Transform[] tiles = new Transform[TwentyOneMatch.MaxLimit];
    private readonly MeshRenderer[] frames = new MeshRenderer[TwentyOneMatch.MaxLimit];
    private readonly MeshRenderer[] art = new MeshRenderer[TwentyOneMatch.MaxLimit];
    private readonly Transform[] plates = new Transform[TwentyOneMatch.MaxLimit];
    private readonly MeshRenderer[] numbers = new MeshRenderer[TwentyOneMatch.MaxLimit];
    private readonly Color[] frameColors = new Color[TwentyOneMatch.MaxLimit];
    private readonly Transform[] dieFaces = new Transform[6];
    private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
    private readonly Transform token;
    private readonly Transform startPad;
    private readonly Transform die;
    private readonly SpriteRenderer opponent;
    private int laidOutLimit;
    private int columns;
    private float pitch;

    private Camera seated;
    private Vector3 cameraPosition;
    private Quaternion cameraRotation;
    private float cameraFieldOfView;
    private float cameraNear;
    private bool cameraOrthographic;
    private int cameraMask;
    private CameraClearFlags cameraClear;
    private Color cameraBackground;

    public bool IsVisible => root != null && root.activeSelf;
    public Transform Room => root.transform;
    public Camera SeatedCamera => seated;
    public Transform Token => token;
    public Transform Die => die;
    public SpriteRenderer Opponent => opponent;
    public Vector3 SeatPosition => root.transform.TransformPoint(SeatOffset);
    public Transform Tile(int index) => tiles[index];
    public Renderer TileArt(int index) => art[index];
    public Color FrameColor(int index) => frameColors[index];
    public float TileSize => pitch * 0.82f;

    public int ShownTiles
    {
        get
        {
            int shown = 0;
            foreach (Transform tile in tiles) if (tile.gameObject.activeSelf) shown++;
            return shown;
        }
    }

    // The face of the die pointing up.
    public int DieUpFace
    {
        get
        {
            int best = 0;
            for (int i = 1; i < FaceNormals.Length; i++)
                if (die.TransformDirection(FaceNormals[i]).y > die.TransformDirection(FaceNormals[best]).y) best = i;
            return FaceValues[best];
        }
    }

    public TwentyOneTable(Transform anchor, Sprite[] tileArt, Sprite npc)
    {
        this.anchor = anchor;
        root = new GameObject("Twenty-one table") { layer = Layer };
        if (anchor != null && anchor.gameObject.scene.IsValid())
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, anchor.gameObject.scene);
        Transform room = root.transform;
        Quaternion flat = Quaternion.Euler(90f, 0f, 0f);

        Material wood = Lit(new Color(0.3f, 0.17f, 0.09f), 0.25f);
        Material darkWood = Lit(new Color(0.14f, 0.08f, 0.05f), 0.15f);
        Part("Floor", PrimitiveType.Quad, room, Vector3.zero, flat, new Vector3(14f, 14f, 1f),
            Lit(new Color(0.1f, 0.085f, 0.075f), 0.05f), false);
        Part("Back wall", PrimitiveType.Quad, room, new Vector3(0f, 2.5f, 3.4f), Quaternion.identity,
            new Vector3(14f, 5f, 1f), Lit(new Color(0.09f, 0.075f, 0.07f), 0f), false);
        Part("Table", PrimitiveType.Cube, room, new Vector3(0f, TableTop - 0.05f, 0f), Quaternion.identity,
            new Vector3(2.9f, 0.1f, 2.3f), wood, true);
        Part("Felt", PrimitiveType.Cube, room, new Vector3(0f, Surface - 0.005f, 0f), Quaternion.identity,
            new Vector3(FeltHalfWidth * 2f, 0.01f, FeltHalfDepth * 2f), Lit(new Color(0.11f, 0.24f, 0.17f), 0.05f), false);
        for (int x = -1; x <= 1; x += 2)
        for (int z = -1; z <= 1; z += 2)
            Part("Table leg", PrimitiveType.Cube, room, new Vector3(x * 1.3f, (TableTop - 0.1f) * 0.5f, z * 1f),
                Quaternion.identity, new Vector3(0.1f, TableTop - 0.1f, 0.1f), darkWood, true);
        Part("Chair back", PrimitiveType.Cube, room, OpponentOffset + new Vector3(0f, 1.3f, 0.45f), Quaternion.identity,
            new Vector3(1.1f, 1.5f, 0.08f), darkWood, true);

        Part("Lamp cord", PrimitiveType.Cylinder, room, new Vector3(0f, 3.7f, 0f), Quaternion.identity,
            new Vector3(0.015f, 1f, 0.015f), darkWood, false);
        Part("Lamp shade", PrimitiveType.Sphere, room, new Vector3(0f, 2.7f, 0f), Quaternion.identity,
            new Vector3(0.62f, 0.26f, 0.62f), Lit(new Color(0.12f, 0.17f, 0.13f), 0.4f), false);
        Part("Lamp bulb", PrimitiveType.Sphere, room, new Vector3(0f, 2.56f, 0f), Quaternion.identity,
            Vector3.one * 0.14f, Unlit(new Color(1f, 0.88f, 0.62f)), false);
        var lampObject = new GameObject("Lamp light") { layer = Layer };
        lampObject.transform.SetParent(room, false);
        lampObject.transform.localPosition = new Vector3(0f, 2.5f, 0f);
        lampObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Light lamp = lampObject.AddComponent<Light>();
        lamp.type = LightType.Spot;
        lamp.spotAngle = 125f;
        lamp.innerSpotAngle = 55f;
        lamp.range = 7f;
        lamp.intensity = 4f;
        lamp.color = new Color(1f, 0.82f, 0.6f);
        lamp.shadows = LightShadows.Hard;
        lamp.shadowStrength = 0.85f;
        lamp.cullingMask = 1 << Layer;
        lamp.renderingLayerMask = 1 << LightLayer;
        UniversalAdditionalLightData urp = lamp.GetUniversalAdditionalLightData();
        urp.renderingLayers = 1u << LightLayer;
        urp.customShadowLayers = false;

        opponent = new GameObject("Al Cachud", typeof(SpriteRenderer)) { layer = Layer }.GetComponent<SpriteRenderer>();
        opponent.transform.SetParent(room, false);
        opponent.transform.localPosition = OpponentOffset;
        opponent.sprite = npc;
        opponent.color = LampLitSprite;
        opponent.shadowCastingMode = ShadowCastingMode.Off;
        opponent.receiveShadows = false;
        if (npc != null) opponent.transform.localScale = Vector3.one * (2.35f / npc.bounds.size.y);

        var animals = new Material[tileArt.Length];
        for (int i = 0; i < tileArt.Length; i++)
        {
            Texture2D copy = PixelCopy(tileArt[i].texture);
            animals[i] = Lit(Color.white, 0.1f);
            animals[i].mainTexture = copy;
            Rect rect = tileArt[i].rect;
            Vector2 size = new Vector2(copy.width, copy.height);
            animals[i].mainTextureScale = rect.size / size;
            animals[i].mainTextureOffset = rect.min / size;
        }
        Material frame = Lit(Color.white, 0.1f);
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i] = new GameObject("Tile " + (i + 1)) { layer = Layer }.transform;
            tiles[i].SetParent(room, false);
            frames[i] = Part("Frame", PrimitiveType.Quad, tiles[i], Vector3.up * 0.002f, flat, Vector3.one, frame, false);
            art[i] = Part("Animal", PrimitiveType.Quad, tiles[i], Vector3.up * 0.004f, flat, Vector3.one,
                animals[i % animals.Length], false);
            Material number = Lit(Color.white, 0.1f);
            number.mainTexture = NumberTexture(i + 1);
            numbers[i] = Part("Number", PrimitiveType.Quad, tiles[i], Vector3.up * 0.006f, flat, Vector3.one, number, false);
            plates[i] = numbers[i].transform;
        }
        startPad = Part("Start", PrimitiveType.Cylinder, room, Vector3.zero, Quaternion.identity,
            new Vector3(0.13f, 0.002f, 0.13f), Lit(new Color(0.07f, 0.1f, 0.12f), 0.1f), false).transform;
        // Placeholder counter piece.
        token = Part("Counter", PrimitiveType.Cylinder, room, Vector3.zero, Quaternion.identity,
            new Vector3(0.09f, TokenHeight * 0.5f, 0.09f), Lit(new Color(0.78f, 0.16f, 0.12f), 0.45f), true).transform;

        die = new GameObject("Die") { layer = Layer }.transform;
        die.SetParent(room, false);
        var faceMaterials = new Material[3];
        for (int face = 1; face <= 3; face++)
        {
            Texture2D texture = TwentyOneDieArt.Create(face);
            owned.Add(texture);
            Material material = Lit(Color.white, 0.3f);
            material.mainTexture = texture;
            // Crop the transparent margin round the sprite-sized art.
            material.mainTextureScale = Vector2.one * 0.75f;
            material.mainTextureOffset = Vector2.one * 0.125f;
            faceMaterials[face - 1] = material;
        }
        for (int i = 0; i < FaceNormals.Length; i++)
        {
            Vector3 normal = FaceNormals[i];
            Quaternion facing = Quaternion.LookRotation(-normal, Mathf.Abs(normal.y) > 0.5f ? Vector3.forward : Vector3.up);
            dieFaces[i] = Part("Face " + FaceValues[i], PrimitiveType.Quad, die, normal * (DieSize * 0.5f), facing,
                Vector3.one * DieSize, faceMaterials[FaceValues[i] - 1], true).transform;
        }
        root.SetActive(false);
    }

    // Narrower screens than 16:9 widen the view rather than cut off the ends of the board.
    public static float FieldOfViewFor(float aspect)
    {
        if (aspect >= WideAspect || aspect <= 0f) return FieldOfView;
        float half = Mathf.Atan(Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad) * WideAspect / aspect);
        return half * 2f * Mathf.Rad2Deg;
    }

    // Shows the room and sits the camera at the player's side of the table, seeing only the room.
    public void Seat(Camera camera)
    {
        Release();
        root.transform.SetPositionAndRotation((anchor != null ? anchor.position : Vector3.zero) + Vector3.up * Height,
            Quaternion.identity);
        root.SetActive(true);
        if (camera == null) return;
        seated = camera;
        cameraPosition = camera.transform.position;
        cameraRotation = camera.transform.rotation;
        cameraFieldOfView = camera.fieldOfView;
        cameraNear = camera.nearClipPlane;
        cameraOrthographic = camera.orthographic;
        cameraMask = camera.cullingMask;
        cameraClear = camera.clearFlags;
        cameraBackground = camera.backgroundColor;
        Vector3 seat = root.transform.TransformPoint(SeatOffset);
        camera.transform.SetPositionAndRotation(seat,
            Quaternion.LookRotation(root.transform.TransformPoint(LookOffset) - seat, Vector3.up));
        camera.orthographic = false;
        camera.fieldOfView = FieldOfViewFor(camera.aspect);
        camera.nearClipPlane = 0.05f;
        camera.cullingMask = 1 << Layer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.008f, 0.007f, 0.008f, 1f);
    }

    // Hides the room and gives the camera back exactly as it was.
    public void Release()
    {
        if (root != null) root.SetActive(false);
        if (seated == null) return;
        seated.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        seated.orthographic = cameraOrthographic;
        seated.fieldOfView = cameraFieldOfView;
        seated.nearClipPlane = cameraNear;
        seated.cullingMask = cameraMask;
        seated.clearFlags = cameraClear;
        seated.backgroundColor = cameraBackground;
        seated = null;
    }

    public void Refresh(TwentyOneGame game)
    {
        if (!IsVisible) return;
        if (seated != null) seated.fieldOfView = FieldOfViewFor(seated.aspect);
        int limit = game.Match.Limit;
        Layout(limit);
        int visible = game.VisibleTotal;
        bool choosing = game.CurrentPhase == TwentyOneGame.Phase.ChoosingOrder;
        for (int i = 0; i < limit; i++)
        {
            bool last = i == limit - 1;
            bool taken = i < visible;
            bool preview = game.IsRolling && i >= visible && i < visible + game.Face;
            frameColors[i] = last ? Red : preview ? Gold : taken ? TakenFrame : Muted;
            Tint(frames[i], frameColors[i]);
            Tint(art[i], taken ? new Color(0.3f, 0.3f, 0.3f) : choosing ? new Color(0.6f, 0.6f, 0.6f) : Color.white);
            Tint(numbers[i], last ? Red : taken ? Muted : Color.white);
            Vector3 centre = TileCentre(i);
            tiles[i].localPosition = centre + Vector3.up * (preview ? 0.012f : 0f);
        }

        float progress = Mathf.Clamp(game.TokenProgress, 0f, limit);
        int from = Mathf.FloorToInt(progress);
        float step = progress - from;
        Vector3 position = Vector3.Lerp(Spot(from), Spot(Mathf.Min(limit, from + 1)), step);
        token.localPosition = position + Vector3.up * (TokenHeight * 0.5f + 0.006f + Mathf.Sin(step * Mathf.PI) * HopHeight);

        bool thrown = game.Match.Started && game.Match.TurnCount > 0;
        die.gameObject.SetActive(thrown);
        if (thrown) PlaceDie(game.Match.TurnCount, game.Match.LastMover == TwentyOneMatch.Side.Player,
            game.Match.LastRoll, game.ThrowProgress);

        float shake = game.CurrentPhase == TwentyOneGame.Phase.Drat ? Mathf.Sin(game.PhaseTime * 25f) * 0.02f : 0f;
        opponent.transform.localPosition = OpponentOffset + Vector3.right * shake;
    }

    // Where the counter stands after this many tiles: the start pad, then on the last tile taken.
    public Vector3 Spot(int count)
    {
        if (count <= 0) return new Vector3((-1f - (columns - 1) * 0.5f) * pitch, Surface, BoardZ + pitch);
        return TileCentre(Mathf.Min(count, laidOutLimit) - 1);
    }

    public Vector3 DieRest(int turn, bool byPlayer) =>
        new Vector3((byPlayer ? 0.28f : -0.22f) + Wobble(turn) * 0.12f, Surface + DieSize * 0.5f,
            0.32f + Wobble(turn * 7 + 3) * 0.08f);

    public static Vector3 DieStart(bool byPlayer) =>
        byPlayer ? new Vector3(0.5f, 1.3f, -1.3f) : new Vector3(-0.3f, 1.25f, 1.2f);

    // A thrown die: one long arc to the felt, two smaller bounces, then it settles on the roll.
    private void PlaceDie(int turn, bool byPlayer, int roll, float t)
    {
        t = Mathf.Clamp01(t);
        Vector3 start = DieStart(byPlayer);
        Vector3 rest = DieRest(turn, byPlayer);
        float travel = 1f - (1f - t) * (1f - t) * (1f - t);
        Vector3 position = Vector3.Lerp(start, rest, travel);
        const float impact = 0.45f;
        if (t < impact)
        {
            float f = t / impact;
            position.y = Mathf.Lerp(start.y, rest.y, f * f) + Mathf.Sin(f * Mathf.PI) * 0.22f;
        }
        else if (t < 0.75f) position.y = rest.y + Mathf.Sin((t - impact) / 0.3f * Mathf.PI) * 0.07f;
        else if (t < 0.9f) position.y = rest.y + Mathf.Sin((t - 0.75f) / 0.15f * Mathf.PI) * 0.02f;
        else position.y = rest.y;

        Quaternion settled = Quaternion.Euler(0f, Wobble(turn * 3 + 1) * 25f + (byPlayer ? 0f : 180f), 0f) * FaceUp(roll);
        Vector3 along = rest - start;
        along.y = 0f;
        Vector3 axis = Vector3.Cross(Vector3.up, along.sqrMagnitude > 0.0001f ? along.normalized : Vector3.forward);
        float tumble = (1f - t) * (1f - t) * 1080f;
        die.localPosition = position;
        die.localRotation = Quaternion.AngleAxis(tumble, axis) * settled;
    }

    private static Quaternion FaceUp(int roll) =>
        roll == 2 ? Quaternion.Euler(0f, 0f, 90f) : roll == 3 ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity;

    private static float Wobble(int n) => ((n * 37) % 11) / 5f - 1f;

    // Three rows snaking across the felt, from the far side of the board towards the player.
    private void Layout(int limit)
    {
        if (limit == laidOutLimit) return;
        laidOutLimit = limit;
        columns = (limit + 2) / 3;
        pitch = Mathf.Min(MaxPitch, BoardWidth / columns);
        float size = pitch * 0.82f;
        float plateHeight = pitch * 0.32f;
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i].gameObject.SetActive(i < limit);
            tiles[i].localPosition = TileCentre(i);
            frames[i].transform.localScale = new Vector3(pitch * 0.92f, pitch * 0.92f, 1f);
            art[i].transform.localScale = new Vector3(size, size, 1f);
            Texture number = plates[i].GetComponent<Renderer>().sharedMaterial.mainTexture;
            float plateWidth = plateHeight * number.width / number.height;
            plates[i].localScale = new Vector3(plateWidth, plateHeight, 1f);
            plates[i].localPosition = new Vector3(-size * 0.5f + plateWidth * 0.5f, 0.006f, size * 0.5f - plateHeight * 0.5f);
        }
        startPad.localPosition = Spot(0) + Vector3.up * 0.002f;
    }

    private Vector3 TileCentre(int index)
    {
        int row = index / columns;
        int column = row % 2 == 0 ? index % columns : columns - 1 - index % columns;
        return new Vector3((column - (columns - 1) * 0.5f) * pitch, Surface, BoardZ + (1 - row) * pitch);
    }

    private void Tint(Renderer renderer, Color color)
    {
        renderer.GetPropertyBlock(block);
        block.SetColor(BaseColor, color);
        renderer.SetPropertyBlock(block);
    }

    private Texture2D NumberTexture(int number)
    {
        string text = number.ToString();
        int width = PixelFont.Measure(text) + 4;
        int height = PixelFont.CapHeight + 4;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Tile number " + number,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        owned.Add(texture);
        var pixels = new Color32[width * height];
        Color32 well = Well;
        Color32 ink = Paper;
        for (int i = 0; i < pixels.Length; i++) pixels[i] = well;
        int x = 2;
        foreach (char c in text)
        {
            string[] rows = PixelFont.GetRows(c);
            for (int row = 0; row < rows.Length && row < PixelFont.CapHeight; row++)
            for (int column = 0; column < rows[row].Length; column++)
                if (rows[row][column] == '#') pixels[(height - 3 - row) * width + x + column] = ink;
            x += rows[0].Length + PixelFont.Spacing;
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    // A point-filtered copy with mipmaps, leaving the imported tile textures untouched.
    private Texture2D PixelCopy(Texture2D source)
    {
        var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true)
        {
            name = source.name + " (table)",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        owned.Add(copy);
        RenderTexture previous = RenderTexture.active;
        RenderTexture temporary = RenderTexture.GetTemporary(source.width, source.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply(true);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }
        return copy;
    }

    private Material Lit(Color color, float smoothness)
    {
        Material material = PlaceholderMaterials.Lit(color);
        material.SetFloat("_Smoothness", smoothness);
        owned.Add(material);
        return material;
    }

    private Material Unlit(Color color)
    {
        Material material = PlaceholderMaterials.Unlit(color);
        owned.Add(material);
        return material;
    }

    private static MeshRenderer Part(string name, PrimitiveType type, Transform parent, Vector3 position,
        Quaternion rotation, Vector3 scale, Material material, bool castShadows)
    {
        var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { layer = Layer };
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localRotation = rotation;
        part.transform.localScale = scale;
        part.GetComponent<MeshFilter>().sharedMesh = MeshOf(type);
        MeshRenderer renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderingLayerMask = 1u << LightLayer;
        renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        return renderer;
    }

    // The built-in primitive meshes, without the colliders CreatePrimitive adds.
    private static Mesh MeshOf(PrimitiveType type)
    {
        if (Meshes.TryGetValue(type, out Mesh mesh) && mesh != null) return mesh;
        GameObject primitive = GameObject.CreatePrimitive(type);
        mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(primitive);
        Meshes[type] = mesh;
        return mesh;
    }

    public void Dispose()
    {
        Release();
        Destroy(root);
        for (int i = owned.Count - 1; i >= 0; i--) Destroy(owned[i]);
        owned.Clear();
    }

    private static void Destroy(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Object.Destroy(value);
        else Object.DestroyImmediate(value);
    }
}
