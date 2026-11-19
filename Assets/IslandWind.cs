using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The island's wind. It blows one way (slowly wavering) as a faint breeze, and every so often a
// gust comes through. Around the player:
// - pixel leaves fall from the trees now and then and drift off downwind, many more in a gust;
// - little coloured pixel petals fly off the flowers;
// - grass, ferns and flowers lean with it and flutter (the scenery's baked "Ground Cover" meshes,
//   bent on the CPU near the player: how far a vertex moves comes from where it sits on the
//   palette, which runs from a plant's foot to its tip).
// Leaves come from the scenery's tree meshes (their leaf-coloured vertices) and the model's
// canopies; petals from the flower heads in the ground cover. It sets itself up in gameplay scenes.
[DisallowMultipleComponent]
public class IslandWind : MonoBehaviour
{
    // The scenery palette: 16 rows of 4 shades (IslandScenery's ramps).
    public const int RampCount = 16;
    private const int LeafFirst = 0, LeafLast = 3, GrassRamp = 6, FernRamp = 7, PetalFirst = 8, PetalLast = 11;

    public const float Breeze = 0.12f;
    // Ground cover tips move this far (metres) in a full gust, and flutter this much on top.
    public const float SwayAmplitude = 0.22f;
    public const float FlutterAmplitude = 0.035f;
    // Only plants and trees this close to the player take part.
    public const float SwayRadius = 40f;
    public const float LeafRadius = 32f;
    public const float PetalRadius = 22f;
    // Leaves and petals let go per second, in a calm and in a full gust.
    public const float CalmLeaves = 0.35f, GustLeaves = 6f;
    public const float CalmPetals = 0.12f, GustPetals = 3f;
    private const float LeafFall = 0.75f, PetalFall = 0.3f;
    private const float BucketSize = 16f;

    // Lighter than the crowns' own shades, so a falling leaf catches the moonlight at night.
    private static readonly Color32[] LeafColours =
    {
        new Color32(104, 168, 64, 255), new Color32(132, 190, 78, 255), new Color32(150, 176, 70, 255),
        new Color32(90, 150, 70, 255), new Color32(170, 200, 92, 255),
        // A few autumn ones among the green.
        new Color32(224, 176, 64, 255), new Color32(222, 124, 52, 255),
    };
    private static readonly Color32[] PetalColours =
    {
        new Color32(240, 236, 218, 255), new Color32(240, 210, 90, 255),
        new Color32(154, 136, 218, 255), new Color32(229, 144, 160, 255),
    };

    // A leaf tumbling: flat, turning, flipped, edge-on. '#' white, '+' the midrib.
    private static readonly string[][] LeafFrames =
    {
        new[] { "....##", "..##+#", ".#+##.", "#+##..", ".##...", "+....." },
        new[] { ".....#", "....#+", "...#+.", "..#+..", ".#+...", "+....." },
        new[] { "##....", "#+##..", ".##+#.", "..##+#", "...##.", ".....+" },
        new[] { "..#...", "..+#..", "...+..", "...#+.", "....+.", "....#." },
    };
    private static readonly string[][] PetalFrames =
    {
        new[] { ".#.", "###", ".+." },
        new[] { "##.", "#+#", ".#." },
    };

    public static IslandWind Current { get; private set; }
    // 0 (still) to 1 (a full gust); never below the breeze.
    public float Strength { get; private set; } = Breeze;
    // Which way it blows, on the ground.
    public Vector3 Direction { get; private set; } = Vector3.right;
    public bool InGust => Strength > 0.45f;
    public int SwayingPatches => swaying.Count;
    public ParticleSystem Leaves => leaves;
    public ParticleSystem Petals => petals;

    private sealed class Patch
    {
        public MeshFilter Filter;
        public Mesh Original;
        public Mesh Bent;
        public Vector3[] Rest;
        public Vector3[] Moved;
        public int[] Swaying;
        public float[] Weights;
        public float[] Phases;
        public Bounds WorldBounds;
    }

    private readonly List<Patch> patches = new List<Patch>();
    private readonly List<Patch> swaying = new List<Patch>();
    private readonly Dictionary<Vector2Int, List<Vector3>> leafSpots = new Dictionary<Vector2Int, List<Vector3>>();
    private readonly Dictionary<Vector2Int, List<(Vector3 at, int colour)>> flowerSpots = new Dictionary<Vector2Int, List<(Vector3, int)>>();
    private readonly List<Vector3> nearLeaves = new List<Vector3>();
    private readonly List<(Vector3 at, int colour)> nearFlowers = new List<(Vector3, int)>();
    private Transform player;
    private Collider ground;
    private ParticleSystem leaves;
    private ParticleSystem petals;
    private float time;
    private float nextGust;
    private float gustStart = -100f;
    private float gustLength;
    private float gustPeak;
    private float leafOwed;
    private float petalOwed;
    private float nextGather;
    private float baseAngle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Current = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) Bootstrap();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<CameraFollow>() != null && FindAnyObjectByType<IslandWind>() == null)
            new GameObject("Island wind", typeof(IslandWind));
    }

    // How much a ground cover vertex moves (0 to 1), from its palette coordinate: grass and fern
    // shades run from 0.12 at a blade's foot to 0.95 at its tip; a flower's petals sit on its tip.
    public static float SwayWeight(Vector2 uv)
    {
        int ramp = Mathf.FloorToInt(uv.y * RampCount);
        if (ramp >= PetalFirst && ramp <= PetalLast) return 1f;
        if (ramp != GrassRamp && ramp != FernRamp) return 0f;
        float along = Mathf.Clamp01((uv.x - 0.12f) / 0.83f);
        return along * Mathf.Sqrt(along);
    }

    // Where a vertex resting at rest goes: pushed downwind by the strength, fluttering, and dipping
    // a little as it bends.
    public static Vector3 Bend(Vector3 rest, float weight, Vector3 direction, float strength, float time, float phase)
    {
        if (weight <= 0f) return rest;
        float push = strength * SwayAmplitude + Mathf.Sin(time * (4f + 3f * strength) + phase) * FlutterAmplitude * (0.4f + strength);
        float reach = push * weight;
        return rest + direction * reach + Vector3.down * (Mathf.Abs(reach) * 0.35f);
    }

    // The gust envelope: up over 1 s, held, down over 1.5 s.
    public static float Gust(float sinceStart, float length, float peak)
    {
        if (sinceStart < 0f || sinceStart > length) return 0f;
        float rise = Mathf.SmoothStep(0f, 1f, sinceStart / 1f);
        float fall = Mathf.SmoothStep(0f, 1f, (length - sinceStart) / 1.5f);
        return peak * Mathf.Min(rise, fall);
    }

    private void Awake()
    {
        Current = this;
        baseAngle = Random.Range(0f, 360f);
        nextGust = Random.Range(4f, 9f);
    }

    // Also run by tests, which get no Start in edit mode.
    public void Start()
    {
        if (leaves != null) return;
        player = Interactables.FindPlayer();
        GameObject groundObject = GameObject.Find(OpeningVoyage.GroundName);
        if (groundObject != null) ground = groundObject.GetComponent<Collider>();
        Gather();
        leaves = MakeParticles("Falling leaves", LeafFrames, 4, 400);
        petals = MakeParticles("Flying petals", PetalFrames, 2, 200);
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
        foreach (Patch patch in patches)
        {
            if (patch.Filter != null && patch.Bent != null) patch.Filter.sharedMesh = patch.Original;
            if (patch.Bent != null) Destroy(patch.Bent);
        }
    }

    // The ground cover patches, the trees' leaves and the flower heads.
    private void Gather()
    {
        foreach (MeshFilter filter in FindObjectsByType<MeshFilter>())
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            string name = filter.name;
            if (name.StartsWith("Ground Cover", System.StringComparison.Ordinal)) GatherCover(filter, mesh);
            else if (name.StartsWith("Trees ", System.StringComparison.Ordinal)) GatherTrees(filter, mesh);
        }
        // The model's own canopies can't be read: their leaves come from inside their bounds.
        foreach (MeshRenderer renderer in FindObjectsByType<MeshRenderer>())
        {
            if (renderer.name != "Leaf Clump") continue;
            Bounds bounds = renderer.bounds;
            var random = new System.Random(bounds.center.GetHashCode());
            for (int i = 0; i < 12; i++)
            {
                Vector3 at = new Vector3(
                    Mathf.Lerp(bounds.min.x, bounds.max.x, 0.2f + 0.6f * (float)random.NextDouble()),
                    Mathf.Lerp(bounds.min.y, bounds.max.y, 0.25f + 0.5f * (float)random.NextDouble()),
                    Mathf.Lerp(bounds.min.z, bounds.max.z, 0.2f + 0.6f * (float)random.NextDouble()));
                AddLeafSpot(at);
            }
        }
    }

    private void GatherCover(MeshFilter filter, Mesh mesh)
    {
        Vector3[] rest = mesh.vertices;
        var uvs = new List<Vector2>();
        mesh.GetUVs(0, uvs);
        if (uvs.Count != rest.Length) return;
        var indices = new List<int>();
        var weights = new List<float>();
        var phases = new List<float>();
        Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
        for (int i = 0; i < rest.Length; i++)
        {
            float weight = SwayWeight(uvs[i]);
            if (weight <= 0f) continue;
            indices.Add(i);
            weights.Add(weight);
            Vector3 world = toWorld.MultiplyPoint3x4(rest[i]);
            // Neighbouring plants sway a little out of step.
            phases.Add(world.x * 0.9f + world.z * 1.3f);
            int ramp = Mathf.FloorToInt(uvs[i].y * RampCount);
            // A flower head's tip: a place petals fly from (one per head is plenty).
            if (ramp >= PetalFirst && ramp <= PetalLast && uvs[i].x > 0.9f && (i % 4) == 0)
                Add(flowerSpots, world, (world, ramp - PetalFirst));
        }
        if (indices.Count == 0) return;
        patches.Add(new Patch
        {
            Filter = filter,
            Original = mesh,
            Rest = rest,
            Swaying = indices.ToArray(),
            Weights = weights.ToArray(),
            Phases = phases.ToArray(),
            WorldBounds = filter.GetComponent<Renderer>() != null ? filter.GetComponent<Renderer>().bounds : new Bounds(toWorld.MultiplyPoint3x4(mesh.bounds.center), mesh.bounds.size),
        });
    }

    private void GatherTrees(MeshFilter filter, Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        var uvs = new List<Vector2>();
        mesh.GetUVs(0, uvs);
        if (uvs.Count != vertices.Length) return;
        Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
        for (int i = 0; i < vertices.Length; i += 23)
        {
            int ramp = Mathf.FloorToInt(uvs[i].y * RampCount);
            if (ramp < LeafFirst || ramp > LeafLast) continue;
            AddLeafSpot(toWorld.MultiplyPoint3x4(vertices[i]));
        }
    }

    private void AddLeafSpot(Vector3 at) => Add(leafSpots, at, at);

    private static void Add<T>(Dictionary<Vector2Int, List<T>> buckets, Vector3 at, T item)
    {
        var key = new Vector2Int(Mathf.FloorToInt(at.x / BucketSize), Mathf.FloorToInt(at.z / BucketSize));
        if (!buckets.TryGetValue(key, out List<T> list)) buckets.Add(key, list = new List<T>());
        list.Add(item);
    }

    private static void Near<T>(Dictionary<Vector2Int, List<T>> buckets, Vector3 centre, float radius, List<T> into, System.Func<T, Vector3> position)
    {
        into.Clear();
        int reach = Mathf.CeilToInt(radius / BucketSize);
        var middle = new Vector2Int(Mathf.FloorToInt(centre.x / BucketSize), Mathf.FloorToInt(centre.z / BucketSize));
        for (int dz = -reach; dz <= reach; dz++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            if (!buckets.TryGetValue(middle + new Vector2Int(dx, dz), out List<T> list)) continue;
            foreach (T item in list)
            {
                Vector3 offset = position(item) - centre;
                if (offset.x * offset.x + offset.z * offset.z <= radius * radius) into.Add(item);
            }
        }
    }

    private void Update() => Step(Time.deltaTime);

    // Starts a gust now (the debug tools and tests); it comes and goes like any other.
    public void StartGust(float length = 5f, float peak = 1f)
    {
        gustStart = time;
        gustLength = Mathf.Max(2.6f, length);
        gustPeak = Mathf.Clamp01(peak);
        nextGust = time + gustLength + Random.Range(7f, 16f);
    }

    // Moves the wind on by dt (paused: dt is 0 and nothing moves).
    public void Step(float dt)
    {
        if (dt <= 0f) return;
        time += dt;
        if (player == null) player = Interactables.FindPlayer();

        // The wind's way wavers slowly about its own heading.
        float angle = baseAngle + (Mathf.PerlinNoise(time * 0.02f, 3.1f) - 0.5f) * 40f;
        Direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
        if (time >= nextGust) StartGust(Random.Range(3.5f, 6.5f), Random.Range(0.7f, 1f));
        float flicker = (Mathf.PerlinNoise(time * 0.7f, 8.3f) - 0.5f) * 0.08f;
        Strength = Mathf.Clamp01(Mathf.Max(Breeze + flicker, Gust(time - gustStart, gustLength, gustPeak)));

        if (player == null) return;
        Vector3 here = player.position;
        if (time >= nextGather)
        {
            nextGather = time + 0.5f;
            Near(leafSpots, here, LeafRadius, nearLeaves, p => p);
            Near(flowerSpots, here, PetalRadius, nearFlowers, f => f.at);
            ChooseSwaying(here);
        }
        Sway();
        Drift();
        Shed(dt);
    }

    // Patches near enough bend; those that drift out of reach stand straight again.
    private void ChooseSwaying(Vector3 here)
    {
        foreach (Patch patch in patches)
        {
            Vector3 nearest = patch.WorldBounds.ClosestPoint(here);
            Vector3 offset = nearest - here;
            bool near = offset.x * offset.x + offset.z * offset.z <= SwayRadius * SwayRadius;
            bool on = swaying.Contains(patch);
            if (near && !on)
            {
                if (patch.Bent == null)
                {
                    patch.Bent = Instantiate(patch.Original);
                    patch.Bent.name = patch.Original.name + " (wind)";
                    patch.Bent.MarkDynamic();
                    Bounds bounds = patch.Original.bounds;
                    bounds.Expand(SwayAmplitude * 3f);
                    patch.Bent.bounds = bounds;
                    patch.Moved = (Vector3[])patch.Rest.Clone();
                }
                patch.Filter.sharedMesh = patch.Bent;
                swaying.Add(patch);
            }
            else if (!near && on)
            {
                patch.Filter.sharedMesh = patch.Original;
                swaying.Remove(patch);
            }
        }
    }

    private void Sway()
    {
        foreach (Patch patch in swaying)
        {
            if (patch.Filter == null) continue;
            Vector3 local = patch.Filter.transform.InverseTransformDirection(Direction);
            Vector3[] moved = patch.Moved;
            for (int k = 0; k < patch.Swaying.Length; k++)
            {
                int i = patch.Swaying[k];
                moved[i] = Bend(patch.Rest[i], patch.Weights[k], local, Strength, time, patch.Phases[k]);
            }
            patch.Bent.SetVertices(moved);
            Bounds bounds = patch.Original.bounds;
            bounds.Expand(SwayAmplitude * 3f);
            patch.Bent.bounds = bounds;
        }
    }

    // Everything already in the air goes with the wind as it is now.
    private void Drift()
    {
        Vector3 blow = Direction * (0.6f + 4.2f * Strength);
        SetDrift(leaves, blow, -LeafFall);
        SetDrift(petals, blow * 1.2f, -PetalFall);
    }

    private static void SetDrift(ParticleSystem system, Vector3 blow, float fall)
    {
        if (system == null) return;
        ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
        velocity.x = new ParticleSystem.MinMaxCurve(blow.x);
        velocity.y = new ParticleSystem.MinMaxCurve(fall);
        velocity.z = new ParticleSystem.MinMaxCurve(blow.z);
    }

    // Lets go of leaves and petals near the player, a few in a calm and many in a gust.
    private void Shed(float dt)
    {
        float gust = Mathf.InverseLerp(Breeze, 1f, Strength);
        leafOwed += Mathf.Lerp(CalmLeaves, GustLeaves, gust) * dt;
        petalOwed += Mathf.Lerp(CalmPetals, GustPetals, gust) * dt;
        while (leafOwed >= 1f)
        {
            leafOwed -= 1f;
            if (nearLeaves.Count > 0) EmitLeaf(nearLeaves[Random.Range(0, nearLeaves.Count)]);
        }
        while (petalOwed >= 1f)
        {
            petalOwed -= 1f;
            if (nearFlowers.Count > 0)
            {
                (Vector3 at, int colour) = nearFlowers[Random.Range(0, nearFlowers.Count)];
                EmitPetal(at, colour);
            }
        }
    }

    public void EmitLeaf(Vector3 at)
    {
        if (leaves == null) return;
        float height = HeightAboveGround(at);
        var leaf = new ParticleSystem.EmitParams
        {
            position = at + Random.insideUnitSphere * 0.6f,
            velocity = Vector3.zero,
            startSize = Random.Range(0.3f, 0.4f),
            // Down to the ground, falling at LeafFall (the size shrinks away just before).
            startLifetime = Mathf.Clamp(height / LeafFall, 1.5f, 14f),
            startColor = LeafColours[Random.Range(0, LeafColours.Length)],
        };
        leaves.Emit(leaf, 1);
    }

    public void EmitPetal(Vector3 at, int colour)
    {
        if (petals == null) return;
        var petal = new ParticleSystem.EmitParams
        {
            position = at + Random.insideUnitSphere * 0.08f,
            // A little hop up as it lets go.
            velocity = Vector3.up * Random.Range(0.4f, 0.9f),
            startSize = Random.Range(0.1f, 0.13f),
            startLifetime = Random.Range(2.5f, 4.5f),
            startColor = PetalColours[Mathf.Clamp(colour, 0, PetalColours.Length - 1)],
        };
        petals.Emit(petal, 1);
    }

    private float HeightAboveGround(Vector3 at)
    {
        var down = new Ray(at + Vector3.up * 0.5f, Vector3.down);
        if (ground != null && ground.Raycast(down, out RaycastHit onGround, 60f)) return Mathf.Max(0.5f, at.y - onGround.point.y);
        return 6f;
    }

    private ParticleSystem MakeParticles(string name, string[][] frames, int tiles, int max)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(transform, false);
        ParticleSystem system = holder.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = system.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;
        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = velocity.y = velocity.z = new ParticleSystem.MinMaxCurve(0f);
        // Fluttering about on the way down.
        ParticleSystem.NoiseModule noise = system.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 0.5f;
        noise.scrollSpeed = 0.4f;
        noise.quality = ParticleSystemNoiseQuality.Low;
        // Shrinks away at the very end instead of popping out.
        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.88f, 1f), new Keyframe(1f, 0f)));
        // Tumbling through its pixel frames.
        ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
        sheet.enabled = true;
        sheet.numTilesX = tiles;
        sheet.numTilesY = 1;
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, tiles - 1);
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        sheet.cycleCount = tiles == 4 ? 10 : 6;

        var drawn = holder.GetComponent<ParticleSystemRenderer>();
        drawn.renderMode = ParticleSystemRenderMode.Billboard;
        drawn.sharedMaterial = ParticleMaterial(Sheet(frames, name));
        drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        drawn.receiveShadows = false;
        system.Play();
        return system;
    }

    // Lit like the scenery (the night and the lamps shade them), cut out to the pixels.
    private static Material ParticleMaterial(Texture2D texture)
    {
        Material template = Resources.Load<Material>("Wind/WindParticles");
        Shader shader = template != null ? template.shader : Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var material = template != null && template.shader == shader ? new Material(template) : new Material(shader);
        material.name = texture.name;
        material.mainTexture = texture;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
        }
        return material;
    }

    // The frames side by side, white and grey, for the particle colour to tint.
    private static Texture2D Sheet(string[][] frames, string name)
    {
        int w = frames[0][0].Length, h = frames[0].Length;
        var texture = new Texture2D(w * frames.Length, h, TextureFormat.RGBA32, false)
        {
            name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color32[w * frames.Length * h];
        for (int f = 0; f < frames.Length; f++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            char c = frames[f][y][x];
            Color32 colour = c == '#' ? new Color32(255, 255, 255, 255) : c == '+' ? new Color32(178, 178, 178, 255) : new Color32(0, 0, 0, 0);
            pixels[(h - 1 - y) * w * frames.Length + f * w + x] = colour;
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
