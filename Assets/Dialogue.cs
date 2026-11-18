using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum Speaker { None, Player, Orca, AlCachud, Iguana, Magician, Panther, Antelope }

// One line of a conversation. AsksName stops after the line for the player to type their name.
public readonly struct DialogueLine
{
    public readonly Speaker Speaker;
    public readonly string Text;
    public readonly bool AsksName;

    public DialogueLine(Speaker speaker, string text, bool asksName = false)
    {
        Speaker = speaker;
        Text = text ?? "";
        AsksName = asksName;
    }

    public static DialogueLine Say(Speaker speaker, string text) => new DialogueLine(speaker, text);
}

// One frame of keyboard and mouse input for a conversation.
public struct DialogueInput
{
    public bool Advance;
    public bool Click;
    // GUI space: pixels from the top-left of the screen.
    public Vector2 Pointer;
    // A number key, 0-based; -1 for none.
    public int Choose;
    // -1 up, +1 down.
    public int Move;
    public bool Escape;
    public string Typed;
    public bool Backspace;

    public static DialogueInput None => new DialogueInput { Choose = -1 };
    public static DialogueInput Continue => new DialogueInput { Choose = -1, Advance = true };

    public static DialogueInput Read()
    {
        DialogueInput input = None;
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard != null)
        {
            input.Advance = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            input.Escape = keyboard.escapeKey.wasPressedThisFrame;
            input.Backspace = keyboard.backspaceKey.wasPressedThisFrame;
            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) input.Move = -1;
            else if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) input.Move = 1;
            for (Key key = Key.Digit1; key <= Key.Digit9; key++)
                if (keyboard[key].wasPressedThisFrame) input.Choose = key - Key.Digit1;
            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            string typed = "";
            for (Key key = Key.A; key <= Key.Z; key++)
            {
                if (!keyboard[key].wasPressedThisFrame) continue;
                char letter = (char)('a' + (key - Key.A));
                typed += shift ? char.ToUpperInvariant(letter) : letter;
            }
            if (keyboard.spaceKey.wasPressedThisFrame) typed += " ";
            if (keyboard.minusKey.wasPressedThisFrame) typed += "-";
            if (keyboard.quoteKey.wasPressedThisFrame) typed += "'";
            input.Typed = typed;
        }
        if (mouse != null)
        {
            input.Click = mouse.leftButton.wasPressedThisFrame;
            Vector2 position = mouse.position.ReadValue();
            input.Pointer = new Vector2(position.x, Screen.height - position.y);
        }
        return input;
    }
}

// The art shown for each speaker: a ghost head where one exists, else their normal sprite.
public struct Portrait
{
    public Texture2D Texture;
    public Rect UV;
    public bool FacesLeft;
    public bool IsValid => Texture != null;
}

public static class DialoguePortraits
{
    public const string SetResource = "DialoguePortraits";

    // The magician has no face yet: a big pixel question mark stands in for him.
    private static readonly string[] QuestionMark =
    {
        "....KKKKKKK.....",
        "..KKyyyyyyyKK...",
        ".KywwyyyyyyygK..",
        "KywyyKKKKKyyygK.",
        "KyyyK.....KyygK.",
        "KggK......KyygK.",
        ".KK.......KyygK.",
        ".........KyyygK.",
        "........KyyygK..",
        ".......KyyygK...",
        "......KyyggK....",
        ".....KyygK......",
        ".....KyygK......",
        ".....KyygK......",
        ".....KGGGK......",
        "......KKK.......",
        "................",
        "......KKK.......",
        ".....KyyyK......",
        ".....KyygK......",
        ".....KGGGK......",
        "......KKK.......",
    };

    private static readonly Dictionary<Speaker, Portrait> Cache = new Dictionary<Speaker, Portrait>();
    private static DialoguePortraitSet set;
    private static bool setLoaded;
    private static Texture2D questionMark;

    public static Texture2D QuestionMarkTexture =>
        questionMark != null ? questionMark : questionMark = InventoryPixelArt.Build(QuestionMark, "dialogue_question_mark");

    public static string DisplayName(Speaker speaker)
    {
        switch (speaker)
        {
            case Speaker.Player: return StoryDirector.PlayerName.ToUpperInvariant();
            case Speaker.AlCachud: return "AL CACHUD";
            case Speaker.None: return "";
            default: return speaker.ToString().ToUpperInvariant();
        }
    }

    public static Portrait Get(Speaker speaker)
    {
        if (speaker == Speaker.None) return default;
        if (Cache.TryGetValue(speaker, out Portrait cached) && cached.IsValid) return cached;
        Portrait portrait = Build(speaker);
        if (portrait.IsValid)
        {
            portrait.Texture.filterMode = FilterMode.Point;
            portrait.UV = Trim(portrait.Texture, portrait.UV);
            Cache[speaker] = portrait;
        }
        return portrait;
    }

    private static Portrait Build(Speaker speaker)
    {
        if (!setLoaded)
        {
            set = Resources.Load<DialoguePortraitSet>(SetResource);
            setLoaded = true;
        }
        DialoguePortraitSet.Entry entry = set != null ? set.Find(speaker) : null;
        if (entry != null && entry.ghostHead != null)
            return new Portrait { Texture = entry.ghostHead, UV = new Rect(0f, 0f, 1f, 1f) };
        if (entry != null && entry.sprite != null) return FromSprite(entry.sprite, entry.spriteFacesLeft);

        switch (speaker)
        {
            case Speaker.Player:
                PlayerController player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
                Sprite body = player != null ? player.FrontIdle : null;
                if (body == null && player != null && player.TryGetComponent(out SpriteRenderer drawn)) body = drawn.sprite;
                return body != null ? FromSprite(body, false) : default;
            case Speaker.AlCachud:
                CachudCallout cachud = UnityEngine.Object.FindAnyObjectByType<CachudCallout>();
                return cachud != null && cachud.TryGetComponent(out SpriteRenderer suit) && suit.sprite != null
                    ? FromSprite(suit.sprite, false) : default;
            case Speaker.Orca:
                return TopFrame("TitleScreen/Orca Sprite", true);
            case Speaker.Panther:
                return TopFrame("ShellGame/panther", false);
            case Speaker.Magician:
                return new Portrait { Texture = QuestionMarkTexture, UV = new Rect(0f, 0f, 1f, 1f) };
            default:
                return default;
        }
    }

    private static Portrait FromSprite(Sprite sprite, bool facesLeft)
    {
        Texture2D texture = sprite.texture;
        Rect r = sprite.textureRect;
        return new Portrait
        {
            Texture = texture,
            UV = new Rect(r.x / texture.width, r.y / texture.height, r.width / texture.width, r.height / texture.height),
            FacesLeft = facesLeft,
        };
    }

    // Two frames stacked in one texture: the upper one.
    private static Portrait TopFrame(string resource, bool facesLeft)
    {
        Texture2D texture = Resources.Load<Texture2D>(resource);
        return texture == null ? default :
            new Portrait { Texture = texture, UV = new Rect(0f, 0.5f, 1f, 0.5f), FacesLeft = facesLeft };
    }

    // Shrinks uv to the drawn (non-transparent) part, so a small figure in a big empty
    // canvas still fills its portrait.
    private static Rect Trim(Texture2D texture, Rect uv)
    {
        try
        {
            int width = texture.width;
            int height = texture.height;
            Color32[] pixels;
            if (texture.isReadable) pixels = texture.GetPixels32();
            else
            {
                RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                RenderTexture previous = RenderTexture.active;
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                copy.Apply(false);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                pixels = copy.GetPixels32();
                UnityEngine.Object.DestroyImmediate(copy);
            }
            int x0 = Mathf.FloorToInt(uv.x * width), y0 = Mathf.FloorToInt(uv.y * height);
            int x1 = Mathf.CeilToInt(uv.xMax * width), y1 = Mathf.CeilToInt(uv.yMax * height);
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                if (pixels[y * width + x].a < 24) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            if (maxX < 0) return uv;
            return new Rect(minX / (float)width, minY / (float)height,
                (maxX + 1 - minX) / (float)width, (maxY + 1 - minY) / (float)height);
        }
        catch (Exception)
        {
            return uv;
        }
    }
}

// The one dialogue box every conversation uses: the NPC's portrait on the left, the player's
// (with Orca's beside it while he travels along) on the right, and a wide box across the bottom of
// the screen drawn over them. Whoever is talking bobs while their line types. Enter or a click
// finishes the line, then moves on; a line can end in a choice or in typing the player's name.
// The owner ticks it with input and calls Draw from OnGUI; what lies behind is the owner's.
public sealed class DialogueRunner
{
    public enum State { Idle, Typing, Waiting, Choosing, Naming }

    public const float CharactersPerSecond = 40f;
    public const int MaxNameLength = 12;
    public const float AppearDuration = 0.25f;

    private const int UiPixelSize = 3;
    private const int Margin = 4;
    private const int PadX = 10;
    private const int PadTop = 8;
    private const int PadBottom = 12;
    private const int MinTextLines = 2;
    // The words are drawn this much bigger than the box's art pixels (5 screen pixels a font
    // pixel at 720p, against 3 for the frame), so the box isn't mostly empty parchment.
    private const float TextScale = 5f / 3f;

    private static readonly Color Ink = new Color32(59, 36, 22, 255);
    private static readonly Color Gold = new Color32(242, 211, 107, 255);
    private static readonly Color TagFrame = new Color32(26, 15, 10, 255);
    private static readonly Color TagWood = new Color32(92, 58, 33, 255);
    private static readonly Color Dim = new Color(0.5f, 0.5f, 0.56f, 1f);

    private static GUIStyle boxStyle;

    private readonly List<DialogueLine> lines = new List<DialogueLine>();
    private readonly List<Speaker> right = new List<Speaker>();
    private string[] choices;
    private Action<int> onChoice;
    private Action onDone;
    private int index;
    private float typed;
    private float time;
    private float appear;
    private bool chaining;
    private Rect[] choiceRects = new Rect[0];

    public State CurrentState { get; private set; }
    public bool IsActive => CurrentState != State.Idle;
    public bool IsTyping => CurrentState == State.Typing;
    // The line is out in full: waiting for Enter, a choice or a name.
    public bool IsWaiting => IsActive && !IsTyping;
    public bool Escapable { get; set; } = true;
    public Speaker Npc { get; private set; }
    public IReadOnlyList<Speaker> RightSide => right;
    public int Index => index;
    public int LineCount => lines.Count;
    public DialogueLine Current => IsActive && index < lines.Count ? lines[index] : default;
    public Speaker CurrentSpeaker => Current.Speaker;
    public string RawText => Current.Text;
    public string Text => IsActive ? Substitute(Current.Text) : "";
    public int VisibleCharacters { get; private set; }
    public int Selected { get; private set; }
    public IReadOnlyList<string> Choices => choices ?? Array.Empty<string>();
    public string NameDraft { get; private set; } = "";

    public static string Substitute(string text) => (text ?? "").Replace("{NAME}", StoryDirector.PlayerName);

    // npc: the character on the left (None for nobody). done runs after the last line; with
    // options, the last line instead ends in a choice and chose gets the picked index.
    public void Play(Speaker npc, IList<DialogueLine> script, Action done = null, string[] options = null,
        Action<int> chose = null)
    {
        bool keepAppearance = chaining || IsActive;
        lines.Clear();
        if (script != null) lines.AddRange(script);
        choices = options != null && options.Length > 0 ? options : null;
        onChoice = chose;
        onDone = done;
        if (lines.Count == 0)
        {
            CurrentState = State.Idle;
            Action then = choices == null ? done : null;
            choices = null;
            onChoice = null;
            onDone = null;
            then?.Invoke();
            return;
        }
        Npc = npc;
        right.Clear();
        right.Add(Speaker.Player);
        bool orca = StoryDirector.OrcaFollowing;
        foreach (DialogueLine line in lines) if (line.Speaker == Speaker.Orca) orca = true;
        if (orca && npc != Speaker.Orca) right.Insert(0, Speaker.Orca);
        if (!keepAppearance) appear = 0f;
        StartLine(0);
    }

    public void Play(Speaker npc, params DialogueLine[] script) => Play(npc, script, null);

    private void StartLine(int line)
    {
        index = line;
        typed = 0f;
        VisibleCharacters = 0;
        Selected = 0;
        NameDraft = "";
        CurrentState = State.Typing;
    }

    // Ends the conversation without running what follows it.
    public void Stop()
    {
        CurrentState = State.Idle;
        lines.Clear();
        choices = null;
        onChoice = null;
        onDone = null;
    }

    public void Tick(float deltaTime, DialogueInput input)
    {
        if (!IsActive) return;
        float dt = Mathf.Max(0f, deltaTime);
        time += dt;
        appear = Mathf.Min(1f, appear + dt / AppearDuration);
        switch (CurrentState)
        {
            case State.Typing:
                string text = Text;
                typed = input.Advance || input.Click ? text.Length : Mathf.Min(text.Length, typed + dt * CharactersPerSecond);
                VisibleCharacters = Mathf.FloorToInt(typed);
                if (VisibleCharacters >= text.Length)
                    CurrentState = Current.AsksName ? State.Naming :
                        index == lines.Count - 1 && choices != null ? State.Choosing : State.Waiting;
                break;
            case State.Waiting:
                if (input.Advance || input.Click) Next();
                break;
            case State.Choosing:
                int count = choices.Length;
                if (input.Move != 0) Selected = (Selected + input.Move + count) % count;
                int hit = -1;
                for (int i = 0; i < choiceRects.Length && i < count; i++)
                    if (choiceRects[i].Contains(input.Pointer)) hit = i;
                if (input.Choose >= 0 && input.Choose < count) Choose(input.Choose);
                else if (input.Click && hit >= 0) Choose(hit);
                else if (input.Advance) Choose(Selected);
                break;
            case State.Naming:
                if (input.Backspace && NameDraft.Length > 0) NameDraft = NameDraft.Substring(0, NameDraft.Length - 1);
                if (!string.IsNullOrEmpty(input.Typed))
                    foreach (char c in input.Typed)
                        if (NameDraft.Length < MaxNameLength && PixelFont.HasGlyph(c) && (c != ' ' || NameDraft.Length > 0))
                            NameDraft += c;
                if (input.Advance) SubmitName(NameDraft);
                break;
        }
    }

    public void Next()
    {
        if (!IsActive) return;
        if (index + 1 < lines.Count) StartLine(index + 1);
        else
        {
            Action then = onDone;
            Stop();
            Run(then);
        }
    }

    public bool Choose(int option)
    {
        if (CurrentState != State.Choosing || option < 0 || option >= choices.Length) return false;
        Action<int> then = onChoice;
        Stop();
        chaining = true;
        try { then?.Invoke(option); }
        finally { chaining = false; }
        return true;
    }

    public bool SubmitName(string name)
    {
        if (CurrentState != State.Naming) return false;
        StoryDirector.SetPlayerName(name);
        Next();
        return true;
    }

    private void Run(Action then)
    {
        chaining = true;
        try { then?.Invoke(); }
        finally { chaining = false; }
    }

    public void Draw()
    {
        if (!IsActive || Event.current == null || Event.current.type != EventType.Repaint) return;
        int p = Mathf.Max(1, Mathf.RoundToInt(UiPixelSize * InteractPrompt.UIScale()));
        int width = Mathf.CeilToInt(Screen.width / (float)p);
        int height = Mathf.CeilToInt(Screen.height / (float)p);
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.matrix = Matrix4x4.Scale(new Vector3(p, p, 1f));

        float ease = 1f - Mathf.Pow(1f - appear, 3f);
        // Text pixels: whole screen pixels, k art pixels each.
        int tp = Mathf.Max(p + 1, Mathf.RoundToInt(p * TextScale));
        float k = tp / (float)p;
        int boxWidth = width - Margin * 2;
        string text = Text;
        string wrapped = string.Join("\n", PixelFont.Wrap(text, Mathf.Max(1, Mathf.FloorToInt((boxWidth - PadX * 2) / k))));
        int textHeight = Mathf.CeilToInt(Mathf.Max(PixelFont.MeasureHeight(wrapped), MinTextLines * PixelFont.LineHeight - 1) * k);
        int choiceHeight = Mathf.CeilToInt((PixelFont.LineHeight + 3) * k);
        int extra = CurrentState == State.Choosing ? 4 + choices.Length * choiceHeight :
            CurrentState == State.Naming ? Mathf.CeilToInt(16 * k) : 0;
        int boxHeight = PadTop + textHeight + extra + PadBottom;
        int boxX = Margin;
        int boxY = height - Margin - boxHeight;

        // Portraits stand behind the box, their lower part hidden by it.
        int size = Mathf.Clamp(Mathf.RoundToInt(height * 0.42f), 56, 150);
        int bottom = boxY + Mathf.Min(26, boxHeight / 2);
        int slide = Mathf.RoundToInt((1f - ease) * (size + 16));
        if (Npc != Speaker.None) DrawPortrait(Npc, 8 - slide, bottom, size, true);
        int x = width - 8 + slide;
        for (int i = right.Count - 1; i >= 0; i--)
        {
            Portrait portrait = DialoguePortraits.Get(right[i]);
            int w = PortraitWidth(portrait, size);
            DrawPortrait(right[i], x - w, bottom, size, false);
            x -= w + (i > 0 && right[i - 1] == Speaker.Orca ? -Mathf.RoundToInt(size * 0.12f) : 4);
        }

        GUI.color = new Color(1f, 1f, 1f, ease);
        Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);
        DrawBox(box, ease);
        DrawTag(box, ease);
        DrawText(boxX + PadX, boxY + PadTop, wrapped.Substring(0, Mathf.Clamp(VisibleCharacters, 0, wrapped.Length)),
            Fade(Ink, ease), p, tp);

        float footer = boxY + PadTop + textHeight + 4;
        if (CurrentState == State.Choosing) DrawChoices(boxX, footer, boxWidth, choiceHeight, p, tp, ease);
        else if (CurrentState == State.Naming) DrawNaming(boxX, footer, p, tp, ease);

        if (IsWaiting)
        {
            Texture2D key = InventoryPixelArt.EnterKeyTexture;
            int bob = Mathf.FloorToInt(time / 0.4f) % 2;
            float keyX = boxX + boxWidth - PadX - key.width;
            float keyY = boxY + boxHeight - key.height - 3 + bob;
            GUI.color = new Color(1f, 1f, 1f, ease);
            GUI.DrawTexture(new Rect(keyX, keyY, key.width, key.height), key);
            string hint = CurrentState == State.Naming ? "TYPE YOUR NAME" :
                CurrentState == State.Choosing ? "[UP/DOWN] PICK" : Escapable ? "[ESC] LEAVE" : "";
            if (hint.Length > 0)
                PixelFont.Draw(keyX - 4 - PixelFont.Measure(hint), keyY + 2, hint, Fade(Ink, ease * 0.6f));
        }

        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }

    private static int PortraitWidth(Portrait portrait, int size)
    {
        if (!portrait.IsValid) return 0;
        float aspect = portrait.UV.width * portrait.Texture.width / Mathf.Max(1f, portrait.UV.height * portrait.Texture.height);
        return Mathf.RoundToInt(size * Mathf.Min(aspect, 1.25f));
    }

    private void DrawPortrait(Speaker speaker, int x, int bottom, int size, bool leftSide)
    {
        Portrait portrait = DialoguePortraits.Get(speaker);
        if (!portrait.IsValid) return;
        int w = PortraitWidth(portrait, size);
        float aspect = portrait.UV.width * portrait.Texture.width / Mathf.Max(1f, portrait.UV.height * portrait.Texture.height);
        int h = Mathf.RoundToInt(w / Mathf.Max(0.01f, aspect));
        if (h > size)
        {
            h = size;
            w = Mathf.RoundToInt(h * aspect);
        }
        bool talking = speaker == CurrentSpeaker;
        int bob = talking && IsTyping ? -Mathf.RoundToInt(Mathf.Abs(Mathf.Sin(time * 11f)) * 3f) : 0;
        Rect uv = portrait.UV;
        if (leftSide == portrait.FacesLeft) uv = new Rect(uv.xMax, uv.y, -uv.width, uv.height);
        GUI.color = talking || CurrentSpeaker == Speaker.None ? Color.white : Dim;
        GUI.DrawTextureWithTexCoords(new Rect(x, bottom - h + bob, w, h), portrait.Texture, uv);
    }

    private static void DrawBox(Rect box, float alpha)
    {
        if (boxStyle == null || boxStyle.normal.background == null)
            boxStyle = InventoryPixelArt.NineSlice(InventoryPixelArt.SpeechBubbleTexture, InventoryPixelArt.SpeechBubbleBorder);
        GUI.color = new Color(0f, 0f, 0f, 0.35f * alpha);
        boxStyle.Draw(new Rect(box.x + 1, box.y + 1, box.width, box.height), false, false, false, false);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        boxStyle.Draw(box, false, false, false, false);
    }

    // The speaker's name on a wooden tag over the box's top edge, on their side of the screen.
    private void DrawTag(Rect box, float alpha)
    {
        string name = DialoguePortraits.DisplayName(CurrentSpeaker);
        if (name.Length == 0) return;
        int w = PixelFont.Measure(name) + 10;
        bool leftSide = CurrentSpeaker == Npc;
        float x = leftSide ? box.x + 8 : box.xMax - 8 - w;
        float y = box.y - 9;
        Fill(new Rect(x, y, w, 12), Fade(TagFrame, alpha));
        Fill(new Rect(x + 1, y + 1, w - 2, 10), Fade(TagWood, alpha));
        PixelFont.Draw(x + 5, y + 3, name, Fade(Gold, alpha));
    }

    // Text at the bigger text pixel size; x and y in the box's art pixels.
    private static void DrawText(float x, float y, string text, Color color, int p, int tp)
    {
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(tp, tp, 1f));
        PixelFont.Draw(x * p / tp, y * p / tp, text, color);
        GUI.matrix = previous;
    }

    private void DrawChoices(float boxX, float top, int boxWidth, int choiceHeight, int p, int tp, float alpha)
    {
        float k = tp / (float)p;
        if (choiceRects.Length != choices.Length) choiceRects = new Rect[choices.Length];
        for (int i = 0; i < choices.Length; i++)
        {
            var row = new Rect(boxX + PadX - 3, top + i * choiceHeight, boxWidth - PadX * 2 + 6, choiceHeight - 1);
            if (i == Selected) Fill(row, new Color(Ink.r, Ink.g, Ink.b, 0.16f * alpha));
            string label = (i == Selected ? "> " : "  ") + (i + 1) + ". " + Substitute(choices[i]);
            DrawText(row.x + 3, row.y + 2 * k, label, Fade(Ink, alpha), p, tp);
            choiceRects[i] = new Rect(row.x * p, row.y * p, row.width * p, row.height * p);
        }
    }

    private void DrawNaming(float boxX, float top, int p, int tp, float alpha)
    {
        float k = tp / (float)p;
        var field = new Rect(boxX + PadX - 3, top, 110 * k, 12 * k);
        Fill(field, new Color(Ink.r, Ink.g, Ink.b, 0.16f * alpha));
        bool empty = NameDraft.Length == 0;
        string shown = empty ? StoryDirector.DefaultName : NameDraft;
        DrawText(field.x + 3, field.y + 3 * k, shown, Fade(Ink, alpha * (empty ? 0.4f : 1f)), p, tp);
        if (Mathf.FloorToInt(time / 0.45f) % 2 == 0)
            Fill(new Rect(field.x + 3 + (empty ? 0 : (PixelFont.Measure(NameDraft) + 1) * k), field.y + 2 * k, k, 8 * k), Fade(Ink, alpha));
    }

    private static Color Fade(Color color, float alpha) => new Color(color.r, color.g, color.b, color.a * alpha);

    private static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
