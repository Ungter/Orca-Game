using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The twenty-one game: the table in the world (TwentyOneTable) and, over it, the title and
// count, the cycling die, the order question and the result dialogue.
public sealed class TwentyOneView : System.IDisposable
{
    private static readonly string[] NumberNames = { "TWENTY", "TWENTY-ONE", "TWENTY-TWO", "TWENTY-THREE",
        "TWENTY-FOUR", "TWENTY-FIVE", "TWENTY-SIX", "TWENTY-SEVEN", "TWENTY-EIGHT", "TWENTY-NINE", "THIRTY" };
    private static readonly Color Gold = new Color32(242, 211, 107, 255);
    private static readonly Color Paper = new Color32(241, 226, 185, 255);
    private static readonly Color Muted = new Color32(137, 153, 161, 255);
    private static readonly Color Red = new Color32(231, 91, 82, 255);
    private static readonly Color Well = new Color32(20, 31, 42, 255);
    private readonly List<Object> owned = new List<Object>();
    private readonly Image[] choices = new Image[2];
    private readonly Sprite[] dice = new Sprite[3];
    private readonly GameObject root;
    private readonly Image die;
    private readonly PixelText title;
    private readonly PixelText rules;
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
    private readonly GameObject rulesPanel;
    private readonly GameObject diePanel;
    private readonly GameObject orderPanel;
    private readonly GameObject resultPanel;

    public bool IsVisible => root != null && root.activeSelf;
    public PixelText KeywordLabel => keyword;
    public TwentyOneTable Table { get; }

    public TwentyOneView(Transform parent, Sprite[] art, Sprite npc)
    {
        Table = new TwentyOneTable(parent, art, npc);
        root = new GameObject("Twenty-one game", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(parent, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        Transform hud = root.transform;
        // Both panels hang over the dark back wall, either side of Al Cachud and above the board.
        rulesPanel = Panel("Rules panel", hud, new Vector2(-470f, 170f), new Vector2(320f, 240f));
        title = Label("Title", rulesPanel.transform, "", 28f, new Vector2(0f, 78f), new Vector2(300f, 40f));
        title.color = Gold;
        rules = Label("Rules", rulesPanel.transform, "", 14f, new Vector2(0f, 14f), new Vector2(300f, 48f));
        count = Label("Count", rulesPanel.transform, "", 28f, new Vector2(0f, -62f), new Vector2(300f, 40f));

        diePanel = Panel("Die panel", hud, new Vector2(470f, 150f), new Vector2(320f, 300f));
        turn = Label("Turn", diePanel.transform, "", 21f, new Vector2(0f, 112f), new Vector2(300f, 32f));
        turn.color = Gold;
        for (int i = 0; i < 3; i++)
        {
            Texture2D texture = TwentyOneDieArt.Create(i + 1);
            owned.Add(texture);
            dice[i] = Sprite.Create(texture, new Rect(0f, 0f, 32f, 32f), Vector2.one * 0.5f, 32f);
            owned.Add(dice[i]);
        }
        die = Picture("Cycling die", diePanel.transform, dice[0], new Vector2(0f, 24f), Vector2.one * 112f);
        face = Label("Die number", diePanel.transform, "1", 28f, new Vector2(0f, -54f), new Vector2(160f, 35f));
        message = Label("Last move", diePanel.transform, "", 14f, new Vector2(0f, -106f), new Vector2(300f, 28f));
        controls = Label("Controls", hud, "", 14f, new Vector2(0f, -338f), new Vector2(1150f, 28f));
        controls.highlightBrackets = true;

        orderPanel = Rect("Order question", hud, new Vector2(0f, -205f), new Vector2(800f, 190f)).gameObject;
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
        resultPanel = Rect("Result dialogue", hud, new Vector2(0f, -205f), new Vector2(1020f, 190f)).gameObject;
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

    // Sits the camera at the table; Hide gives it back.
    public void Show(Camera camera)
    {
        if (root == null) return;
        Table.Seat(camera);
        root.SetActive(true);
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        Table.Release();
    }

    public void Refresh(TwentyOneGame game)
    {
        bool choosing = game.CurrentPhase == TwentyOneGame.Phase.ChoosingOrder;
        bool lost = game.CurrentPhase == TwentyOneGame.Phase.Lost;
        bool drat = game.CurrentPhase == TwentyOneGame.Phase.Drat;
        bool insult = game.CurrentPhase == TwentyOneGame.Phase.Insult;
        orderPanel.SetActive(choosing);
        resultPanel.SetActive(lost || drat || insult);
        bool boardGame = !choosing && !lost && !drat && !insult;
        diePanel.SetActive(boardGame);
        // Only shown while he asks to go second.
        rulesPanel.SetActive(!game.Match.Started);
        int limit = game.Match.Limit;
        title.text = NumberNames[limit - TwentyOneMatch.MinLimit];
        rules.text = "TAKE 1-3 TILES.\nDON'T TAKE TILE " + limit + ".";
        count.text = "COUNT " + game.VisibleTotal + " / " + limit;
        count.color = game.VisibleTotal >= limit - 3 ? Red : Gold;
        Table.Refresh(game);

        bool playerTurn = game.Match.Started && game.Match.Turn == TwentyOneMatch.Side.Player;
        bool playerThrew = game.Match.LastMover == TwentyOneMatch.Side.Player;
        die.sprite = dice[game.Face - 1];
        die.rectTransform.localRotation = Quaternion.Euler(0f, 0f, game.IsRolling ? Mathf.Sin(game.PhaseTime * 25f) * 5f : 0f);
        face.text = game.Face.ToString();
        turn.text = game.CurrentPhase == TwentyOneGame.Phase.Rules ?
            (game.OpponentGoesSecond ? "YOU GO FIRST" : "AL CACHUD GOES FIRST") :
            game.CurrentPhase == TwentyOneGame.Phase.Throwing ? (playerThrew ? "YOU THROW" : "AL CACHUD THROWS") :
            game.CurrentPhase == TwentyOneGame.Phase.TakingTiles ? (playerThrew ? "YOU ROLLED " : "AL CACHUD ROLLED ") + game.Match.LastRoll :
            playerTurn ? "YOUR TURN" : "AL CACHUD'S TURN";
        message.text = game.Match.TurnCount == 0 ? "STOP THE DIE ON 1, 2 OR 3." :
            game.CurrentPhase == TwentyOneGame.Phase.Throwing ? "THE DIE IS ROLLING..." :
            (playerThrew ? "YOU" : "AL CACHUD") +
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
        normalSpeech.text = lost ? "YOU TOOK TILE " + limit + ". YOU LOSE!" :
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

    private static GameObject Panel(string name, Transform parent, Vector2 position, Vector2 size)
    {
        GameObject panel = Rect(name, parent, position, size).gameObject;
        Picture("Border", panel.transform, null, Vector2.zero, size).color = Gold;
        Picture("Inset", panel.transform, null, Vector2.zero, size - Vector2.one * 8f).color =
            new Color(Well.r, Well.g, Well.b, 0.92f);
        return panel;
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
        Table.Dispose();
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
