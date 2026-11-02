using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Real world sprites in front of the doorway, with only instructions and the reward on the HUD.
public class WorldHatStage : MonoBehaviour
{
    private const float ArtScale = 0.014f;
    private static readonly Color Gold = new Color32(242, 211, 107, 255);
    private readonly SpriteRenderer[] hats = new SpriteRenderer[6];
    private readonly SpriteRenderer[] panthers = new SpriteRenderer[6];
    private readonly SpriteRenderer[] markers = new SpriteRenderer[6];
    private readonly PixelText[] names = new PixelText[6];
    private readonly Sprite[] pantherSprites = new Sprite[6];
    private readonly List<Sprite> ownedSprites = new List<Sprite>();
    private ShellMinigame game;
    private GameObject worldRoot;
    private Canvas nameCanvas;
    private PixelText round;
    private PixelText message;
    private PixelText instructions;
    private PixelText controls;
    private Image action;
    private PixelText actionLabel;
    private Texture2D markerTexture;
    private Camera view;
    private Vector3 outward;
    private Vector3 right;

    public RectTransform Hud { get; private set; }
    public Vector3 Centre { get; private set; }
    public Vector3 ViewingPoint { get; private set; }
    public Bounds FramingBounds { get; private set; }
    public bool IsShowing => Hud != null && Hud.gameObject.activeSelf;

    public void Open(ShellMinigame owner, Transform house, Vector3 entrance)
    {
        game = owner;
        if (Hud == null) Build();
        view = Camera.main;

        Transform door = house != null ? house.Find("pCylinder1") : null;
        Renderer doorway = door != null ? door.GetComponent<Renderer>() : null;
        outward = entrance - (doorway != null ? doorway.bounds.center : HouseBounds(house, entrance).center);
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.01f) outward = new Vector3(-1f, 0f, -1f);
        outward.Normalize();
        right = Vector3.Cross(Vector3.up, -outward).normalized;
        Vector3 performancePoint = entrance + outward * 3f;
        performancePoint.y = GroundHeight(performancePoint, entrance.y);
        Centre = performancePoint + Vector3.up * 2.6f;
        Vector3 viewingPoint = entrance + outward * 12f;
        viewingPoint.y = GroundHeight(viewingPoint, entrance.y);
        ViewingPoint = viewingPoint;
        Bounds framing = new Bounds(Centre, Vector3.zero);
        for (int x = -440; x <= 440; x += 880)
            for (int y = -180; y <= 290; y += 470)
                framing.Encapsulate(WorldPoint(x, y));
        FramingBounds = framing;
        Hud.gameObject.SetActive(true);
        worldRoot.SetActive(true);
        Frame();
    }

    private static float GroundHeight(Vector3 point, float fallback)
    {
        Transform player = Interactables.FindPlayer();
        RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * 3f, Vector3.down, 8f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        float height = fallback;
        foreach (RaycastHit hit in hits)
        {
            if (hit.normal.y < 0.6f || (player != null && hit.transform.IsChildOf(player))) continue;
            if (hit.distance >= nearest) continue;
            nearest = hit.distance;
            height = hit.point.y;
        }
        return height;
    }

    public static Bounds HouseBounds(Transform house, Vector3 entrance)
    {
        Bounds bounds = new Bounds(entrance + new Vector3(10f, 10f, 10f), Vector3.one * 20f);
        if (house == null) return bounds;
        Renderer[] renderers = house.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return bounds;
        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    public void Hide()
    {
        if (worldRoot != null) worldRoot.SetActive(false);
        if (Hud != null) Hud.gameObject.SetActive(false);
        view = null;
    }

    public void Frame()
    {
        if (!IsShowing) return;
        view = Camera.main;
        if (view != null)
        {
            nameCanvas.transform.rotation = view.transform.rotation;
        }
        Refresh();
    }

    public void HandleInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 pointer = mouse.position.ReadValue();
        int index = HatAt(pointer);
        game.HoverHat(index);
        if (!mouse.leftButton.wasPressedThisFrame) return;
        if (game.AcceptsGuess) game.GuessHat(index);
        else if (game.CanContinue && RectTransformUtility.RectangleContainsScreenPoint(action.rectTransform, pointer))
            game.ContinueRound();
    }

    // Hit the actual billboard planes, not a screen-space rectangle detached from the house.
    public int HatAt(Vector2 screenPoint)
    {
        if (!IsShowing || view == null || !game.AcceptsGuess) return -1;
        Ray ray = view.ScreenPointToRay(screenPoint);
        int nearest = -1;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < game.HatCount; i++)
        {
            if (game.GetHatPose(i).revealed) continue;
            SpriteRenderer hat = hats[i];
            var plane = new Plane(hat.transform.forward, hat.transform.position);
            if (!plane.Raycast(ray, out float distance)) continue;
            Vector3 point = hat.transform.InverseTransformPoint(ray.GetPoint(distance));
            point.z = 0f;
            if (!hat.sprite.bounds.Contains(point) || distance >= nearestDistance) continue;
            nearest = i;
            nearestDistance = distance;
        }
        if (nearest < 0 || Physics.Raycast(ray, nearestDistance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore)) return -1;
        return nearest;
    }

    public void Refresh()
    {
        if (!IsShowing) return;
        round.text = game.RoundTitle;
        message.text = game.Message;
        instructions.text = game.Instructions;
        controls.text = "[WASD] MOVE  [J/L] CAMERA  " + game.Controls.Replace("[ESC] CLOSE", "[ESC] END") +
            "  WALK AWAY TO END";
        action.gameObject.SetActive(game.CanContinue);
        actionLabel.text = game.ActionLabel;
        Quaternion facing = view != null ? view.transform.rotation : Quaternion.LookRotation(-outward);
        for (int i = 0; i < hats.Length; i++)
        {
            bool active = i < game.HatCount;
            hats[i].gameObject.SetActive(active);
            ShellMinigame.HatPose pose = game.GetHatPose(i);
            panthers[i].gameObject.SetActive(active && pose.showPanther);
            markers[i].gameObject.SetActive(active && (pose.revealed || pose.hovered));
            names[i].gameObject.SetActive(active && pose.showPanther);
            if (!active) continue;
            Draw(hats[i], WorldPoint(pose.x, pose.y + pose.lift * 110f + (pose.hovered ? 8f : 0f)),
                130f * pose.scaleX, 145f * pose.scaleY, facing * Quaternion.Euler(0f, 0f, pose.tilt));
            if (pose.panther >= 0)
            {
                panthers[i].sprite = pantherSprites[pose.panther];
                Draw(panthers[i], WorldPoint(pose.x, -45f), 105f, 105f, facing);
                names[i].text = $"#{pose.panther + 1} {ShellMinigame.PantherName(pose.panther)}";
                names[i].rectTransform.position = WorldPoint(pose.x, -125f);
            }
            Draw(markers[i], WorldPoint(pose.x, -60f), 148f, 153f, facing);
            markers[i].color = pose.revealed ? pose.panther >= 0 ? new Color32(65, 190, 112, 255) :
                new Color32(210, 65, 55, 255) : Gold;
        }
    }

    public void SetRewardVisible(bool visible)
    {
        worldRoot.SetActive(!visible);
        message.gameObject.SetActive(!visible);
        instructions.gameObject.SetActive(!visible);
        if (visible) action.gameObject.SetActive(false);
    }

    private Vector3 WorldPoint(float x, float y) => Centre + right * (x * ArtScale) + Vector3.up * (y * ArtScale);

    private static void Draw(SpriteRenderer renderer, Vector3 position, float width, float height, Quaternion rotation)
    {
        renderer.transform.SetPositionAndRotation(position, rotation);
        Vector3 size = renderer.sprite.bounds.size;
        renderer.transform.localScale = new Vector3(width * ArtScale / size.x, height * ArtScale / size.y, 1f);
    }

    private void Build()
    {
        worldRoot = new GameObject("Entrance hat stage");
        worldRoot.transform.SetParent(transform, false);
        Sprite hat = SpriteFor(Resources.Load<Texture2D>("ShellGame/shell"), false);
        markerTexture = ShellMinigame.CreateMarkerTexture();
        Sprite marker = SpriteFor(markerTexture, false);
        for (int i = 0; i < hats.Length; i++)
        {
            pantherSprites[i] = SpriteFor(ShellMinigame.PantherTexture(i), true);
            hats[i] = Renderer("Hat " + (i + 1), hat, 10);
            panthers[i] = Renderer("Panther " + (i + 1), pantherSprites[i], 2);
            markers[i] = Renderer("Selection ring", marker, 0);
        }
        var labelRoot = new GameObject("Panther identities", typeof(RectTransform), typeof(Canvas));
        labelRoot.transform.SetParent(worldRoot.transform, false);
        labelRoot.transform.localScale = Vector3.one * ArtScale;
        nameCanvas = labelRoot.GetComponent<Canvas>();
        nameCanvas.renderMode = RenderMode.WorldSpace;
        nameCanvas.sortingOrder = 12;
        ((RectTransform)labelRoot.transform).sizeDelta = new Vector2(800f, 500f);
        for (int i = 0; i < names.Length; i++)
            names[i] = Label("Panther identity", labelRoot.transform, 15f, new Vector2(0.5f, 0.5f), new Vector2(130f, 25f));

        var hud = new GameObject("Outdoor hat game HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        hud.transform.SetParent(transform, false);
        Hud = hud.GetComponent<RectTransform>();
        Canvas canvas = hud.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 140;
        CanvasScaler scaler = hud.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        Label("Title", Hud, 20f, new Vector2(0.5f, 0.96f), new Vector2(1200f, 32f)).text = "THE MAGICIAN'S HATS";
        round = Label("Round", Hud, 14f, new Vector2(0.5f, 0.91f), new Vector2(1200f, 30f));
        message = Label("Message", Hud, 20f, new Vector2(0.5f, 0.23f), new Vector2(1200f, 36f));
        instructions = Label("Instructions", Hud, 13f, new Vector2(0.5f, 0.18f), new Vector2(1200f, 32f));
        controls = Label("Controls", Hud, 12f, new Vector2(0.5f, 0.04f), new Vector2(1200f, 28f));
        controls.highlightBrackets = true;
        action = new GameObject("Round action", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        action.transform.SetParent(Hud, false);
        action.rectTransform.anchorMin = action.rectTransform.anchorMax = new Vector2(0.5f, 0.105f);
        action.rectTransform.sizeDelta = new Vector2(300f, 44f);
        action.rectTransform.anchoredPosition = Vector2.zero;
        action.color = Gold;
        action.raycastTarget = false;
        actionLabel = Label("Label", action.transform, 16f, new Vector2(0.5f, 0.5f), new Vector2(300f, 40f));
        actionLabel.color = new Color32(59, 36, 22, 255);
        actionLabel.shadowColor = Color.clear;
        hud.SetActive(false);
        worldRoot.SetActive(false);
    }

    private Sprite SpriteFor(Texture2D texture, bool sheet)
    {
        var rect = new Rect(0f, sheet ? texture.height * 0.5f : 0f, texture.width,
            sheet ? texture.height * 0.5f : texture.height);
        Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        sprite.name = texture.name;
        ownedSprites.Add(sprite);
        return sprite;
    }

    private SpriteRenderer Renderer(string name, Sprite sprite, int order)
    {
        var item = new GameObject(name, typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
        item.transform.SetParent(worldRoot.transform, false);
        item.sprite = sprite;
        item.sortingOrder = order;
        item.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        item.receiveShadows = false;
        return item;
    }

    private static PixelText Label(string name, Transform parent, float size, Vector2 anchor, Vector2 bounds)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(PixelText)).GetComponent<PixelText>();
        label.transform.SetParent(parent, false);
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = anchor;
        label.rectTransform.anchoredPosition = Vector2.zero;
        label.rectTransform.sizeDelta = bounds;
        label.fontSize = size;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Gold;
        label.shadowColor = new Color32(15, 12, 20, 255);
        return label;
    }

    private void OnDisable() => Hide();

    private void OnDestroy()
    {
        Hide();
        foreach (Sprite sprite in ownedSprites)
        {
            if (Application.isPlaying) Destroy(sprite);
            else DestroyImmediate(sprite);
        }
        if (markerTexture == null) return;
        if (Application.isPlaying) Destroy(markerTexture);
        else DestroyImmediate(markerTexture);
    }
}
