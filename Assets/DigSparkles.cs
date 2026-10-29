using UnityEngine;

// Faint gold glints twinkling over a dig site, lit by one soft point light. Each glint
// rises a little while it plays a dot -> plus -> star -> plus -> dot cycle, then reappears
// somewhere else. Visible fades the whole effect in and out.
public class DigSparkles : MonoBehaviour
{
    private const int Count = 7;
    private const float FadeTime = 1.2f;
    private const float PixelsPerUnit = 32f;
    private const float Rise = 0.35f;

    private static readonly string[][] Frames =
    {
        new[] { "w" },
        new[] { ".Y.", "YwY", ".Y." },
        new[] { "..y..", "..Y..", "yYwYy", "..Y..", "..y.." },
    };
    // Frame shown at each step of a glint's life.
    private static readonly int[] Cycle = { 0, 1, 2, 2, 1, 0 };
    private static Sprite[] sprites;

    private struct Glint
    {
        public SpriteRenderer renderer;
        public Vector3 origin;
        public float age;
        public float life;
        public float delay;
    }

    private readonly Glint[] glints = new Glint[Count];
    private Light glow;
    private Vector3 centre;
    private float radius;
    private Color color;
    private float lightIntensity;
    private float opacity;

    public bool Visible { get; set; }

    // centre: top-centre of the dig site. radius: how far from it glints may appear.
    public void Configure(Vector3 centre, float radius, Color color, float lightIntensity, float lightRange)
    {
        this.centre = centre;
        this.radius = radius;
        this.color = color;
        this.lightIntensity = lightIntensity;
        transform.position = centre;

        glow = gameObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = color;
        glow.range = lightRange;
        glow.intensity = 0f;
        glow.shadows = LightShadows.None;
        glow.enabled = false;

        Sprite[] frames = Sprites();
        for (int i = 0; i < Count; i++)
        {
            var glintObject = new GameObject("Glint");
            glintObject.transform.SetParent(transform, false);
            var glintRenderer = glintObject.AddComponent<SpriteRenderer>();
            glintRenderer.sprite = frames[0];
            glintRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glintRenderer.receiveShadows = false;
            glintRenderer.enabled = false;
            glints[i].renderer = glintRenderer;
            // Staggered so they never all twinkle together.
            Respawn(ref glints[i], Random.Range(0f, 2f));
        }
    }

    private static Sprite[] Sprites()
    {
        if (sprites != null && sprites[0] != null)
        {
            return sprites;
        }
        sprites = new Sprite[Frames.Length];
        for (int i = 0; i < Frames.Length; i++)
        {
            Texture2D texture = InventoryPixelArt.Build(Frames[i], "dig_sparkle_" + i);
            sprites[i] = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), PixelsPerUnit);
            sprites[i].hideFlags = HideFlags.HideAndDontSave;
        }
        return sprites;
    }

    private void Respawn(ref Glint glint, float delay)
    {
        Vector2 spot = Random.insideUnitCircle * radius;
        glint.origin = centre + new Vector3(spot.x, Random.Range(0.1f, 0.6f), spot.y);
        glint.age = 0f;
        glint.life = Random.Range(0.9f, 1.4f);
        glint.delay = delay;
    }

    private void Update()
    {
        opacity = Mathf.MoveTowards(opacity, Visible ? 1f : 0f, Time.deltaTime / FadeTime);
        bool shown = opacity > 0f;
        if (glow != null)
        {
            glow.enabled = shown;
            // A slow breath with a little shimmer on top.
            float breath = 0.8f + 0.2f * Mathf.Sin(Time.time * 1.7f) + 0.06f * Mathf.Sin(Time.time * 9.3f);
            glow.intensity = lightIntensity * opacity * breath;
        }

        Sprite[] frames = Sprites();
        Quaternion facing = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;
        for (int i = 0; i < Count; i++)
        {
            ref Glint glint = ref glints[i];
            if (glint.renderer == null)
            {
                continue;
            }
            if (glint.delay > 0f)
            {
                glint.delay -= Time.deltaTime;
                glint.renderer.enabled = false;
                continue;
            }

            glint.age += Time.deltaTime;
            float t = glint.age / glint.life;
            if (t >= 1f)
            {
                Respawn(ref glint, Random.Range(0.2f, 1.2f));
                glint.renderer.enabled = false;
                continue;
            }

            glint.renderer.enabled = shown;
            glint.renderer.sprite = frames[Cycle[Mathf.Min(Cycle.Length - 1, Mathf.FloorToInt(t * Cycle.Length))]];
            glint.renderer.color = new Color(color.r, color.g, color.b, 0.85f * opacity);
            Transform glintTransform = glint.renderer.transform;
            glintTransform.position = glint.origin + Vector3.up * (Rise * t);
            glintTransform.rotation = facing;
        }
    }
}
