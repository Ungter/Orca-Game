using UnityEngine;
using UnityEngine.UI;

// uGUI text drawn with the hand-made PixelFont, for use on any Canvas in place of
// UnityEngine.UI.Text. Font pixels are snapped to whole screen pixels so the
// glyphs stay crisp at any canvas scale.
//
// Text inside square brackets - "[E] Restart" - can be tinted with keyColor so
// key hints stand out from their labels.
[RequireComponent(typeof(CanvasRenderer))]
public class PixelText : MaskableGraphic
{
    [SerializeField, TextArea]
    private string m_Text = "";

    // Height of a capital letter in canvas units. Rounded so each font pixel is
    // a whole number of screen pixels.
    [SerializeField]
    private float m_FontSize = 15f;

    [SerializeField]
    private TextAnchor m_Alignment = TextAnchor.MiddleCenter;

    [SerializeField]
    private Color m_ShadowColor = new Color(0f, 0f, 0f, 0f);

    [SerializeField]
    private bool m_HighlightBrackets = false;

    [SerializeField]
    private Color m_KeyColor = new Color32(242, 211, 107, 255);

    [SerializeField]
    private bool m_Uppercase = false;

    private float lastScaleFactor;

    public string text
    {
        get => m_Text;
        set
        {
            if (m_Text == value) return;
            m_Text = value ?? "";
            SetVerticesDirty();
        }
    }

    public float fontSize
    {
        get => m_FontSize;
        set
        {
            if (Mathf.Approximately(m_FontSize, value)) return;
            m_FontSize = value;
            SetVerticesDirty();
        }
    }

    public TextAnchor alignment
    {
        get => m_Alignment;
        set { m_Alignment = value; SetVerticesDirty(); }
    }

    public Color shadowColor
    {
        get => m_ShadowColor;
        set { m_ShadowColor = value; SetVerticesDirty(); }
    }

    public bool highlightBrackets
    {
        get => m_HighlightBrackets;
        set { m_HighlightBrackets = value; SetVerticesDirty(); }
    }

    public Color keyColor
    {
        get => m_KeyColor;
        set { m_KeyColor = value; SetVerticesDirty(); }
    }

    public bool uppercase
    {
        get => m_Uppercase;
        set { m_Uppercase = value; SetVerticesDirty(); }
    }

    public override Texture mainTexture => PixelFont.Atlas;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    // Canvas scale changes (window resize) don't always rebuild graphics; re-snap when they do.
    private void Update()
    {
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        if (!Mathf.Approximately(scale, lastScaleFactor))
        {
            lastScaleFactor = scale;
            SetVerticesDirty();
        }
    }

    // Size of one font pixel in canvas units.
    public float PixelSize()
    {
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        float screenPixels = Mathf.Max(1f, Mathf.Round(m_FontSize / PixelFont.CapHeight * scale));
        return screenPixels / scale;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (string.IsNullOrEmpty(m_Text))
        {
            return;
        }

        string source = m_Uppercase ? m_Text.ToUpperInvariant() : m_Text;
        string[] lines = source.Split('\n');
        float px = PixelSize();
        Rect rect = rectTransform.rect;
        Texture atlas = PixelFont.Atlas;
        float atlasWidth = atlas.width;
        float atlasHeight = atlas.height;

        float blockHeight = ((lines.Length - 1) * PixelFont.LineHeight + PixelFont.CapHeight) * px;
        float top;
        switch (m_Alignment)
        {
            case TextAnchor.UpperLeft:
            case TextAnchor.UpperCenter:
            case TextAnchor.UpperRight:
                top = rect.yMax;
                break;
            case TextAnchor.LowerLeft:
            case TextAnchor.LowerCenter:
            case TextAnchor.LowerRight:
                top = rect.yMin + blockHeight;
                break;
            default:
                top = rect.center.y + blockHeight * 0.5f;
                break;
        }

        bool hasShadow = m_ShadowColor.a > 0f;
        for (int pass = hasShadow ? 0 : 1; pass < 2; pass++)
        {
            bool shadow = pass == 0;
            bool inKey = false;
            for (int line = 0; line < lines.Length; line++)
            {
                string lineText = lines[line];
                float width = PixelFont.Measure(lineText) * px;
                float left;
                switch (m_Alignment)
                {
                    case TextAnchor.UpperLeft:
                    case TextAnchor.MiddleLeft:
                    case TextAnchor.LowerLeft:
                        left = rect.xMin;
                        break;
                    case TextAnchor.UpperRight:
                    case TextAnchor.MiddleRight:
                    case TextAnchor.LowerRight:
                        left = rect.xMax - width;
                        break;
                    default:
                        left = rect.center.x - width * 0.5f;
                        break;
                }

                Vector2 origin = Snap(new Vector2(left, top - line * PixelFont.LineHeight * px));
                if (shadow)
                {
                    origin += new Vector2(px, -px);
                }

                float penX = origin.x;
                foreach (char c in lineText)
                {
                    if (m_HighlightBrackets && c == '[') inKey = true;
                    RectInt glyph = PixelFont.GetGlyph(c);
                    Color tint = shadow ? m_ShadowColor : (inKey ? m_KeyColor : Color.white);
                    if (!shadow) tint *= color;
                    else tint.a *= color.a;
                    AddQuad(vh, new Rect(penX, origin.y - glyph.height * px, glyph.width * px, glyph.height * px),
                        new Rect(glyph.x / atlasWidth, 0f, glyph.width / atlasWidth, glyph.height / atlasHeight), tint);
                    penX += (glyph.width + PixelFont.Spacing) * px;
                    if (m_HighlightBrackets && c == ']') inKey = false;
                }
            }
        }
    }

    // Moves a local point onto the screen-pixel grid of the canvas.
    private Vector2 Snap(Vector2 local)
    {
        Canvas root = canvas != null ? canvas.rootCanvas : null;
        if (root == null)
        {
            return local;
        }

        float scale = root.scaleFactor;
        Vector3 inRoot = root.transform.InverseTransformPoint(transform.TransformPoint(local));
        Vector3 snapped = new Vector3(Mathf.Round(inRoot.x * scale) / scale, Mathf.Round(inRoot.y * scale) / scale, inRoot.z);
        Vector3 back = transform.InverseTransformPoint(root.transform.TransformPoint(snapped));
        return new Vector2(back.x, back.y);
    }

    private static void AddQuad(VertexHelper vh, Rect position, Rect uv, Color32 tint)
    {
        int start = vh.currentVertCount;
        vh.AddVert(new Vector3(position.xMin, position.yMin), tint, new Vector2(uv.xMin, uv.yMin));
        vh.AddVert(new Vector3(position.xMin, position.yMax), tint, new Vector2(uv.xMin, uv.yMax));
        vh.AddVert(new Vector3(position.xMax, position.yMax), tint, new Vector2(uv.xMax, uv.yMax));
        vh.AddVert(new Vector3(position.xMax, position.yMin), tint, new Vector2(uv.xMax, uv.yMin));
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start + 2, start + 3, start);
    }
}
