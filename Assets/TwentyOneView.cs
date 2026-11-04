using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// An arcade-style queue of the supplied animal tiles, with a dangerous final tile.
public sealed class TwentyOneView : System.IDisposable
{
    private static readonly Color Gold = new Color32(242, 211, 107, 255);
    private static readonly Color Paper = new Color32(241, 226, 185, 255);
    private static readonly Color Muted = new Color32(137, 153, 161, 255);
    private static readonly Color Red = new Color32(231, 91, 82, 255);
    private static readonly Color Well = new Color32(20, 31, 42, 255);
    private readonly List<Object> owned = new List<Object>();
    private readonly Image[] tiles = new Image[21];
    private readonly Image[] frames = new Image[21];
    private readonly PixelText[] numbers = new PixelText[21];
    private readonly Image[] choices = new Image[2];
    private readonly Sprite[] dice = new Sprite[3];
    private readonly GameObject root;
    private readonly Image die;
    private readonly Image hero;
    private readonly Image opponent;
    private readonly PixelText count;
    private readonly PixelText message;
    private readonly PixelText controls;
    private readonly PixelText turn;
    private readonly PixelText face;
    private readonly PixelText orderQuestion;
    private readonly PixelText normalSpeech;
    private readonly PixelText prefix;
    private readonly PixelText keyword;
    private readonly PixelText suffix;
    private readonly PixelText reward;
    private readonly GameObject orderPanel;
    private readonly GameObject resultPanel;

    public bool IsVisible => root != null && root.activeSelf;
    public PixelText KeywordLabel => keyword;

    public TwentyOneView(Transform parent, Sprite[] art, Sprite npc, Sprite player)
    {
        root = new GameObject("Twenty-one game", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(parent, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        Image shade = Picture("World shade", root.transform, null, Vector2.zero, Vector2.zero);
        shade.rectTransform.anchorMin = Vector2.zero;
        shade.rectTransform.anchorMax = Vector2.one;
        shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;
        shade.color = new Color(0f, 0f, 0f, 0.8f);
        RectTransform panel = Rect("Board", root.transform, Vector2.zero, new Vector2(1200f, 640f));
        Picture("Gold frame", panel, null, Vector2.zero, new Vector2(1200f, 640f)).color = Gold;
        Picture("Dark inset", panel, null, Vector2.zero, new Vector2(1192f, 632f)).color = Well;
        Label("Title", panel, "TWENTY-ONE", 35f, new Vector2(0f, 278f), new Vector2(1100f, 50f)).color = Gold;
        Label("Rules", panel, "TAKE 1-3 TILES. DON'T TAKE TILE 21.", 21f,
            new Vector2(0f, 235f), new Vector2(1120f, 36f));
        count = Label("Count", panel, "", 28f, new Vector2(0f, 205f), new Vector2(800f, 38f));
        for (int i = 0; i < 20; i++)
        {
            Vector2 a = TilePosition(i), b = TilePosition(i + 1);
            Label("Queue arrow", panel, i / 7 != (i + 1) / 7 ? "v" : i / 7 % 2 == 0 ? ">" : "<",
                14f, (a + b) * 0.5f, new Vector2(22f, 22f)).color = Muted;
        }
        var tileArt = new Sprite[art.Length];
        for (int i = 0; i < art.Length; i++) tileArt[i] = PixelSprite(art[i]);
        for (int i = 0; i < 21; i++)
        {
            frames[i] = Picture("Tile frame " + (i + 1), panel, null, TilePosition(i), Vector2.one * 78f);
            tiles[i] = Picture("Animal tile " + (i + 1), frames[i].transform, tileArt[i % tileArt.Length],
                Vector2.zero, Vector2.one * 72f);
            Image badge = Picture("Number badge", frames[i].transform, null,
                new Vector2(-22f, 23f), new Vector2(27f, 21f));
            badge.color = Well;
            numbers[i] = Label("Tile number", badge.transform, (i + 1).ToString(), 14f,
                Vector2.zero, new Vector2(27f, 21f));
        }
        opponent = Picture("Al Cachud", panel, PixelSprite(npc), new Vector2(-450f, 28f), new Vector2(220f, 248f));
        hero = Picture("Player", panel, PixelSprite(player), new Vector2(450f, 28f), new Vector2(220f, 248f));
        Label("Opponent name", panel, "AL CACHUD", 21f, new Vector2(-450f, 173f), new Vector2(270f, 35f));
        Label("Player name", panel, "YOU", 21f, new Vector2(450f, 173f), new Vector2(270f, 35f));
        turn = Label("Turn", panel, "", 21f, new Vector2(0f, -89f), new Vector2(900f, 32f));
        turn.color = Gold;
        for (int i = 0; i < 3; i++)
        {
            Texture2D texture = TwentyOneDieArt.Create(i + 1);
            owned.Add(texture);
            dice[i] = Sprite.Create(texture, new Rect(0f, 0f, 32f, 32f), Vector2.one * 0.5f, 32f);
            owned.Add(dice[i]);
        }
        die = Picture("Cycling die", panel, dice[0], new Vector2(0f, -149f), Vector2.one * 112f);
        face = Label("Die number", panel, "1", 28f, new Vector2(0f, -215f), new Vector2(160f, 35f));
        message = Label("Last move", panel, "", 21f, new Vector2(0f, -260f), new Vector2(1120f, 42f));
        controls = Label("Controls", panel, "", 14f, new Vector2(0f, -304f), new Vector2(1150f, 28f));
        controls.highlightBrackets = true;
        orderPanel = Rect("Order question", panel, new Vector2(0f, -177f), new Vector2(800f, 190f)).gameObject;
        Picture("Question border", orderPanel.transform, null, Vector2.zero, new Vector2(800f, 190f)).color = Gold;
        Picture("Question background", orderPanel.transform, null, Vector2.zero, new Vector2(792f, 182f)).color = Well;
        orderQuestion = Label("Can I go second", orderPanel.transform, TwentyOneGame.OrderQuestion, 28f,
            new Vector2(0f, 49f), new Vector2(760f, 42f));
        for (int i = 0; i < 2; i++)
        {
            choices[i] = Picture(i == 0 ? "Yes" : "No", orderPanel.transform, null,
                new Vector2(i == 0 ? -177f : 177f, -30f), new Vector2(322f, 68f));
            Label("Answer", choices[i].transform, i == 0 ? "YES\nAL CACHUD GOES SECOND" : "NO\nI GO SECOND",
                14f, Vector2.zero, new Vector2(310f, 60f)).color = Well;
        }
        resultPanel = Rect("Result dialogue", panel, new Vector2(0f, -177f), new Vector2(1020f, 190f)).gameObject;
        Picture("Dialogue border", resultPanel.transform, null, Vector2.zero, new Vector2(1020f, 190f)).color = Gold;
        Picture("Dialogue inset", resultPanel.transform, null, Vector2.zero, new Vector2(1012f, 182f)).color = Well;
        normalSpeech = Label("Speech", resultPanel.transform, "", 28f,
            new Vector2(0f, 29f), new Vector2(980f, 48f));
        prefix = Label("Insult prefix", resultPanel.transform, "", 28f,
            Vector2.zero, new Vector2(1f, 48f));
        keyword = Label("Highlighted JERK", resultPanel.transform, "", 28f,
            Vector2.zero, new Vector2(1f, 48f));
        keyword.color = Gold;
        suffix = Label("Insult suffix", resultPanel.transform, "", 28f,
            Vector2.zero, new Vector2(1f, 48f));
        prefix.alignment = keyword.alignment = suffix.alignment = TextAnchor.MiddleLeft;
        reward = Label("Revealed clue", resultPanel.transform, "", 21f,
            new Vector2(0f, -43f), new Vector2(980f, 40f));
        reward.color = Gold;
        root.SetActive(false);
    }

    public int OrderChoiceAt(Vector2 screenPoint)
    {
        if (!IsVisible || !orderPanel.activeSelf) return -1;
        for (int i = 0; i < choices.Length; i++)
            if (RectTransformUtility.RectangleContainsScreenPoint(choices[i].rectTransform, screenPoint)) return i;
        return -1;
    }

    public Vector2 OrderChoiceCentre(int index) =>
        RectTransformUtility.WorldToScreenPoint(null, choices[index].rectTransform.position);

    public void SetVisible(bool visible) { if (root != null) root.SetActive(visible); }

    public void Refresh(TwentyOneGame game)
    {
        bool choosing = game.CurrentPhase == TwentyOneGame.Phase.ChoosingOrder;
        bool lost = game.CurrentPhase == TwentyOneGame.Phase.Lost;
        bool drat = game.CurrentPhase == TwentyOneGame.Phase.Drat;
        bool insult = game.CurrentPhase == TwentyOneGame.Phase.Insult;
        orderPanel.SetActive(choosing);
        resultPanel.SetActive(lost || drat || insult);
        bool boardGame = !choosing && !lost && !drat && !insult;
        die.gameObject.SetActive(boardGame);
        face.gameObject.SetActive(boardGame);
        turn.gameObject.SetActive(boardGame);
        message.gameObject.SetActive(boardGame);
        count.text = "COUNT " + game.VisibleTotal + " / 21";
        count.color = game.VisibleTotal >= 18 ? Red : Gold;
        for (int i = 0; i < 21; i++)
        {
            bool taken = i < game.VisibleTotal;
            bool preview = game.IsRolling && i >= game.VisibleTotal && i < game.VisibleTotal + game.Face;
            frames[i].color = i == 20 ? Red : preview ? Gold : taken ? new Color(0.15f, 0.2f, 0.24f) : Muted;
            tiles[i].color = taken ? new Color(1f, 1f, 1f, 0.16f) : choosing ? new Color(1f, 1f, 1f, 0.5f) : Color.white;
            numbers[i].color = i == 20 ? Red : taken ? Muted : Paper;
            frames[i].transform.localScale = Vector3.one * (taken ? 0.87f : preview ? 1.04f : 1f);
        }
        bool playerTurn = game.Match.Started && game.Match.Turn == TwentyOneMatch.Side.Player;
        hero.color = !boardGame || playerTurn ? Color.white : new Color(0.5f, 0.5f, 0.5f);
        opponent.color = !boardGame || !playerTurn ? Color.white : new Color(0.5f, 0.5f, 0.5f);
        opponent.rectTransform.anchoredPosition = new Vector2(-450f + (drat ? Mathf.Sin(game.PhaseTime * 25f) * 4f : 0f), 28f);
        die.sprite = dice[game.Face - 1];
        die.rectTransform.localRotation = Quaternion.Euler(0f, 0f, game.IsRolling ? Mathf.Sin(game.PhaseTime * 25f) * 5f : 0f);
        face.text = game.Face.ToString();
        turn.text = game.CurrentPhase == TwentyOneGame.Phase.Rules ?
            (game.OpponentGoesSecond ? "YOU GO FIRST" : "AL CACHUD GOES FIRST") :
            game.CurrentPhase == TwentyOneGame.Phase.TakingTiles ? "ROLL STOPPED" :
            playerTurn ? "YOUR TURN" : "AL CACHUD'S TURN";
        message.text = game.Match.TurnCount == 0 ? "STOP THE DIE ON 1, 2 OR 3." :
            (game.Match.LastMover == TwentyOneMatch.Side.Player ? "YOU" : "AL CACHUD") +
            " TOOK " + game.Match.LastRoll + (game.Match.LastRoll == 1 ? " TILE." : " TILES.");
        controls.text = choosing ? "[1] YES   [2] NO   [LEFT/RIGHT] SELECT   [ENTER] CHOOSE   [ESC] LEAVE" :
            lost ? "[ENTER] TRY AGAIN    [ESC] LEAVE" :
            drat || insult ? "[ENTER] CONTINUE    [ESC] LEAVE" :
            game.CurrentPhase == TwentyOneGame.Phase.PlayerRolling ? "[SPACE] STOP DICE    [ESC] LEAVE" :
            "WATCH THE DIE AND TILES    [ESC] LEAVE";
        for (int i = 0; i < choices.Length; i++) choices[i].color = i == game.SelectedOrder ? Gold : Muted;
        orderQuestion.text = "AL CACHUD: " + TwentyOneGame.OrderQuestion;
        normalSpeech.gameObject.SetActive(!insult);
        prefix.gameObject.SetActive(insult);
        keyword.gameObject.SetActive(insult);
        suffix.gameObject.SetActive(insult);
        normalSpeech.text = lost ? "YOU TOOK TILE 21. YOU LOSE!" :
            drat ? TwentyOneGame.DratLine.Substring(0, game.VisibleCharacters) : "";
        if (insult)
        {
            int n = game.VisibleCharacters;
            prefix.text = "You're a ".Substring(0, Mathf.Min(n, 9));
            keyword.text = TwentyOneGame.Keyword.Substring(0, Mathf.Clamp(n - 9, 0, 4));
            suffix.text = n > 13 ? "!" : "";
            LayoutInsult();
        }
        reward.text = lost ? "Another round?" : insult && game.KeywordRecorded ? "KEYWORD REVEALED: JERK" : "";
    }

    private void LayoutInsult()
    {
        // Use the actual snapped font size, including at resized canvas scales.
        float pixel = prefix.PixelSize();
        float gap = PixelFont.Spacing * pixel;
        float prefixWidth = PixelFont.Measure("You're a ") * pixel;
        float wordWidth = PixelFont.Measure(TwentyOneGame.Keyword) * pixel;
        float suffixWidth = PixelFont.Measure("!") * pixel;
        float left = -(prefixWidth + wordWidth + suffixWidth + gap * 2f) * 0.5f;
        PlaceFragment(prefix, left, prefixWidth);
        PlaceFragment(keyword, left + prefixWidth + gap, wordWidth);
        PlaceFragment(suffix, left + prefixWidth + wordWidth + gap * 2f, suffixWidth);
    }

    private static void PlaceFragment(PixelText label, float left, float width)
    {
        label.rectTransform.sizeDelta = new Vector2(width, 48f);
        label.rectTransform.anchoredPosition = new Vector2(left + width * 0.5f, 29f);
    }

    private Sprite PixelSprite(Sprite original)
    {
        if (original == null) return null;
        // Keep existing tile imports and world materials unchanged.
        Texture2D source = original.texture;
        var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
        {
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
            copy.Apply();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }
        Sprite sprite = Sprite.Create(copy, original.rect, Vector2.one * 0.5f, original.pixelsPerUnit,
            0, SpriteMeshType.FullRect);
        owned.Add(sprite);
        return sprite;
    }

    private static Vector2 TilePosition(int index)
    {
        int row = index / 7;
        int column = row % 2 == 0 ? index % 7 : 6 - index % 7;
        return new Vector2((column - 3) * 88f, 142f - row * 84f);
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = Vector2.one * 0.5f;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static Image Picture(string name, Transform parent, Sprite sprite, Vector2 position, Vector2 size)
    {
        Image image = Rect(name, parent, position, size).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = sprite != null;
        image.raycastTarget = false;
        return image;
    }

    private static PixelText Label(string name, Transform parent, string text, float size, Vector2 position, Vector2 bounds)
    {
        PixelText label = Rect(name, parent, position, bounds).gameObject.AddComponent<PixelText>();
        label.text = text;
        label.fontSize = size;
        label.color = Paper;
        label.shadowColor = new Color(0f, 0f, 0f, 0.5f);
        label.alignment = TextAnchor.MiddleCenter;
        return label;
    }

    public void Dispose()
    {
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
