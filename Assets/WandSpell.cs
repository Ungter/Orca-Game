using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The magician's reveal: his wand writes THIEF in the air in glittering particles (a Unity
// ParticleSystem), then sketches the Orca statue under it. A spark leads the stroke like the tip
// of the wand. The writing goes up in the open beside the player (preferring the camera's right,
// never inside the house: a clear, visible spot is searched for), the camera pans across to it,
// it holds and fades, and the camera pans back; the player can't move meanwhile.
[DisallowMultipleComponent]
public class WandSpell : MonoBehaviour
{
    public const string Word = "THIEF";
    public const float PanDuration = 0.9f;
    public const float DrawDuration = 3.6f;
    public const float HoldDuration = 2.4f;
    public const float FadeDuration = 1f;
    public const float Distance = 7f;
    // The writing's half size (sideways, up, deep), for checking a spot is clear, and how high its
    // middle floats over the ground.
    private static readonly Vector3 HalfSize = new Vector3(2.1f, 1.7f, 0.6f);
    private const float MiddleHeight = 1.95f;
    // Bearings (degrees right of the camera's heading) and distances from the player tried in turn.
    private static readonly float[] Bearings = { 40f, 55f, 70f, 85f, 25f, 100f, 115f, 10f, -25f, -40f, -55f, -70f };
    private static readonly float[] Distances = { 7f, 9f, 5.5f, 11f, 13f };
    private const float LetterCell = 0.2f;
    private const float SketchCell = 0.1f;

    // A rough sketch of the statue: an orca leaping over its plinth.
    private static readonly string[] Statue =
    {
        "..........#..............",
        ".........##..............",
        "........###..............",
        "...######.#####..........",
        ".##...........##.........",
        "#..##...........#........",
        "#.####...........#.......",
        "#..##.............#......",
        ".#................#......",
        "..###....####.....#......",
        ".....####....##..#.......",
        ".............#.##........",
        "............#..#.........",
        "...........#....#........",
        "......############.......",
        "......#..........#.......",
        "......#..........#.......",
        "......#..........#.......",
        ".....##############......",
    };

    private static readonly string[] Spark =
    {
        "..y..",
        ".yYy.",
        "yYwYy",
        ".yYy.",
        "..y..",
    };

    private static WandSpell active;
    private static Material sparkMaterial;

    public static bool IsCasting => active != null;
    public static WandSpell Active => active;

    public float Time { get; private set; }
    public int Emitted { get; private set; }
    public int PointCount => points.Count;
    // The first LetterPoints points spell the word; the rest sketch the statue.
    public int LetterPoints { get; private set; }
    public bool IsFinished => Time >= PanDuration + DrawDuration + HoldDuration + FadeDuration;
    // Seconds into the writing itself, after the camera has panned across.
    private float Writing => Time - PanDuration;
    public ParticleSystem Particles => particles;

    private readonly List<Vector3> points = new List<Vector3>();
    private ParticleSystem particles;
    private ParticleSystem.Particle[] buffer;
    private Light glow;
    private Action onDone;
    private Camera view;
    private CameraFollow follow;
    private bool followWasEnabled;
    private Quaternion viewFrom;
    private Quaternion viewTo;

    // Writes in the open beside player (Distance in front of the camera without one), facing the
    // camera, which pans across to it and back. done runs once it has faded.
    public static WandSpell Cast(Camera view, Transform player, Action done)
    {
        if (active != null) active.Finish();
        var root = new GameObject("Wand spell");
        Scene scene = view != null ? view.gameObject.scene : SceneManager.GetActiveScene();
        if (scene.IsValid()) SceneManager.MoveGameObjectToScene(root, scene);
        WandSpell spell = root.AddComponent<WandSpell>();
        if (view != null)
        {
            Vector3 eye = view.transform.position;
            Vector3 middle = FindSpot(view, player, out bool found);
            if (!found) middle = eye + view.transform.forward * Distance;
            Vector3 facing = middle - eye;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.01f) facing = view.transform.forward;
            root.transform.SetPositionAndRotation(middle, Quaternion.LookRotation(facing, Vector3.up));
            spell.view = view;
            spell.viewFrom = view.transform.rotation;
            spell.viewTo = Quaternion.LookRotation(middle - eye, Vector3.up);
            spell.follow = view.GetComponent<CameraFollow>();
            spell.followWasEnabled = spell.follow != null && spell.follow.enabled;
            if (spell.follow != null) spell.follow.enabled = false;
        }
        spell.Init();
        spell.onDone = done;
        active = spell;
        return spell;
    }

    // A spot in the open near the player, clear of houses, trees and props, that the camera can
    // see from where it is: the middle of the writing, MiddleHeight over the ground.
    public static Vector3 FindSpot(Camera view, Transform player, out bool found)
    {
        found = false;
        Vector3 eye = view.transform.position;
        Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 origin = player != null ? player.position : eye + forward * 4f;
        foreach (float bearing in Bearings)
        foreach (float distance in Distances)
        {
            Vector3 direction = Quaternion.AngleAxis(bearing, Vector3.up) * forward;
            Vector3 spot = origin + direction * distance;
            if (!GroundAt(spot, player, out float ground)) continue;
            Vector3 middle = new Vector3(spot.x, ground + MiddleHeight, spot.z);
            Vector3 facing = middle - eye;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1f) continue;
            Quaternion turn = Quaternion.LookRotation(facing, Vector3.up);
            if (Blocked(Physics.OverlapBox(middle, HalfSize, turn, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), player)) continue;
            if (!InSight(eye, middle, player)) continue;
            found = true;
            return middle;
        }
        return origin;
    }

    private static bool GroundAt(Vector3 spot, Transform player, out float ground)
    {
        ground = 0f;
        float best = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(spot + Vector3.up * 6f, Vector3.down, 14f,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (player != null && hit.collider.transform.IsChildOf(player)) continue;
            // The ground, not a roof: within a few metres of where the player stands.
            if (hit.point.y > spot.y + 2f || hit.point.y < spot.y - 4f) continue;
            if (hit.point.y > best) best = hit.point.y;
        }
        if (float.IsNegativeInfinity(best)) return false;
        ground = best;
        return true;
    }

    private static bool Blocked(Collider[] hits, Transform player)
    {
        foreach (Collider hit in hits)
            if (player == null || !hit.transform.IsChildOf(player)) return true;
        return false;
    }

    // The camera sees the middle and both ends of the word.
    private static bool InSight(Vector3 eye, Vector3 middle, Transform player)
    {
        Vector3 across = Vector3.Cross(Vector3.up, middle - eye).normalized * (HalfSize.x * 0.8f);
        foreach (Vector3 point in new[] { middle, middle + across, middle - across, middle + Vector3.up * 1.2f })
        {
            foreach (RaycastHit hit in Physics.RaycastAll(eye, point - eye, Vector3.Distance(eye, point),
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (player == null || !hit.collider.transform.IsChildOf(player)) return false;
        }
        return true;
    }

    private void Awake() => Init();

    // Also run from Cast, since edit-mode components get no Awake.
    private void Init()
    {
        if (particles != null) return;
        BuildPoints();
        particles = gameObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = PanDuration + DrawDuration + HoldDuration + FadeDuration;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.maxParticles = 8000;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = false;
        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.04f;
        noise.frequency = 1.5f;
        noise.scrollSpeed = 0.6f;
        var drawn = GetComponent<ParticleSystemRenderer>();
        drawn.renderMode = ParticleSystemRenderMode.Billboard;
        drawn.sharedMaterial = SparkMaterial();
        drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        drawn.receiveShadows = false;
        particles.Play();

        glow = gameObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.85f, 0.45f);
        glow.range = 9f;
        glow.intensity = 0f;
        glow.shadows = LightShadows.None;
    }

    private static Material SparkMaterial()
    {
        if (sparkMaterial != null) return sparkMaterial;
        Shader shader = Shader.Find("Sprites/Default");
        sparkMaterial = new Material(shader) { name = "Wand spark", hideFlags = HideFlags.HideAndDontSave };
        sparkMaterial.mainTexture = InventoryPixelArt.Build(Spark, "wand_spark");
        return sparkMaterial;
    }

    // THIEF from the pixel font's capitals, the statue sketch under it, each in reading order
    // (left to right), so the stroke runs the way a hand would write.
    private void BuildPoints()
    {
        points.Clear();
        int columns = 0;
        foreach (char c in Word) columns += PixelFont.GetRows(c)[0].Length + PixelFont.Spacing;
        columns -= PixelFont.Spacing;
        float left = -columns * LetterCell * 0.5f;
        float top = 1.35f;
        int column = 0;
        foreach (char c in Word)
        {
            string[] rows = PixelFont.GetRows(c);
            int width = rows[0].Length;
            for (int x = 0; x < width; x++)
            for (int y = 0; y < PixelFont.CapHeight && y < rows.Length; y++)
                if (x < rows[y].Length && rows[y][x] == '#')
                    points.Add(new Vector3(left + (column + x) * LetterCell, top - y * LetterCell, 0f));
            column += width + PixelFont.Spacing;
        }
        LetterPoints = points.Count;

        float sketchLeft = -Statue[0].Length * SketchCell * 0.5f;
        float sketchTop = 0.15f;
        for (int x = 0; x < Statue[0].Length; x++)
        for (int y = 0; y < Statue.Length; y++)
            if (Statue[y][x] == '#')
                points.Add(new Vector3(sketchLeft + x * SketchCell, sketchTop - y * SketchCell, 0f));
    }

    private void Update() => Tick(UnityEngine.Time.unscaledDeltaTime);

    public void Tick(float deltaTime)
    {
        if (IsFinished) return;
        Time += Mathf.Max(0f, deltaTime);
        float total = DrawDuration + HoldDuration + FadeDuration;
        PanCamera();
        int target = Writing <= 0f ? 0 :
            Mathf.Min(points.Count, Mathf.CeilToInt(points.Count * Mathf.Clamp01(Writing / DrawDuration)));
        for (; Emitted < target; Emitted++)
        {
            bool letter = Emitted < LetterPoints;
            var spark = new ParticleSystem.EmitParams
            {
                position = transform.TransformPoint(points[Emitted]),
                startSize = letter ? LetterCell * 1.35f : SketchCell * 1.6f,
                startLifetime = total - Writing + 0.2f,
                startColor = letter ? new Color(1f, 0.86f, 0.42f) : new Color(0.75f, 0.92f, 1f),
                velocity = Vector3.zero,
            };
            particles.Emit(spark, 1);
            // Loose glitter falling off the wand's tip.
            var glitter = new ParticleSystem.EmitParams
            {
                position = spark.position,
                startSize = 0.07f,
                startLifetime = 0.6f,
                startColor = Color.white,
                velocity = UnityEngine.Random.insideUnitSphere * 0.6f + Vector3.down * 0.4f,
            };
            particles.Emit(glitter, 2);
        }

        float fade = 1f - Mathf.Clamp01((Writing - DrawDuration - HoldDuration) / FadeDuration);
        glow.intensity = 2.2f * Mathf.Clamp01(Writing / 0.5f) * fade;
        if (Writing > DrawDuration + HoldDuration) FadeParticles(fade);
        if (IsFinished) Finish();
    }

    // Across to the writing first, back as it fades.
    private void PanCamera()
    {
        if (view == null) return;
        float there = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Time / PanDuration));
        float back = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Writing - DrawDuration - HoldDuration) / FadeDuration));
        view.transform.rotation = Quaternion.Slerp(viewFrom, viewTo, there * (1f - back));
    }

    private void RestoreCamera()
    {
        if (view == null) return;
        view.transform.rotation = viewFrom;
        if (follow != null) follow.enabled = followWasEnabled;
        view = null;
    }

    private void FadeParticles(float alpha)
    {
        int count = particles.particleCount;
        if (buffer == null || buffer.Length < count) buffer = new ParticleSystem.Particle[Mathf.Max(count, 256)];
        count = particles.GetParticles(buffer);
        for (int i = 0; i < count; i++)
        {
            Color32 color = buffer[i].startColor;
            color.a = (byte)Mathf.RoundToInt(255f * alpha);
            buffer[i].startColor = color;
        }
        particles.SetParticles(buffer, count);
    }

    // Ends at once (also when skipped), running what follows.
    public void Finish()
    {
        RestoreCamera();
        if (active == this) active = null;
        Action done = onDone;
        onDone = null;
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
        done?.Invoke();
    }

    private void OnDestroy()
    {
        RestoreCamera();
        if (active == this) active = null;
    }
}
