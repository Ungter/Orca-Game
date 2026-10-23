using UnityEngine;
using UnityEngine.ParticleSystemJobs;
using UnityEngine.Rendering;

// Water for the plaza fountain, put on its spout tip by IslandLampLights when the map
// imports. A high-volume spray of single pixel-art droplets bursts out of the spout like a
// firework: air drag slows them into a dome over the top, then they drift down in a curtain
// round the column into the pool, some splashing as they land. Every droplet is launched
// so it comes down inside the basin, clear of the column. They are real particles in the
// world, so the column hides the ones behind it. A flat pool fills the basin.
[DisallowMultipleComponent]
public class FountainWater : MonoBehaviour
{
    // Air drag on the droplets, per second, in proportion to their speed: it turns the burst
    // into a dome that hangs, and sets how fast they fall (gravity / Drag).
    private const float Drag = 1.8f;
    // Range of upward launch speeds, m/s. The fastest top out about 2.5 m over the spout.
    private const float MaxRise = 13f;
    private const float MaxSink = 2f;
    // Where droplets come down: this far clear of the column and the wall, in metres...
    private const float ColumnClearance = 0.25f;
    private const float WallClearance = 0.4f;
    // ...packed towards the column (above 1), so the curtain covers it.
    private const float LandingBias = 2.6f;
    private const float NozzleRadius = 0.1f;
    // Share of droplets that splash when they land.
    private const float SplashChance = 0.15f;
    private const float SplashLifetime = 0.4f;
    private const float PoolFramesPerSecond = 10f;
    private const int PoolSegments = 96;

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

    [Tooltip("Droplets launched per second. About three seconds' worth are in the air at once.")]
    [SerializeField] private float dropletsPerSecond = 3500f;

    // Measured from the fountain mesh by the importer, in this object's local units.
    [SerializeField] private float drop;
    [SerializeField] private float columnRadius;
    [SerializeField] private float wallRadius;
    // Lit, alpha-clipped material for the droplets and splashes; their sheets replace its base map.
    [SerializeField] private Material sprayMaterial;
    // Lit material for the pool; its base map is replaced by the animated water.
    [SerializeField] private Material poolMaterial;

    private ParticleSystem spray;
    private ParticleSystem.EmitParams launch;
    private float owed;
    // In metres: spout to water, and the range of radii droplets come down at.
    private float height;
    private float innerLanding;
    private float outerLanding;
    private float gravity;
    private Texture2D[] textures;
    private Texture2D[] poolFrames;
    private MeshRenderer pool;
    private Mesh poolMesh;
    private MaterialPropertyBlock poolBlock;

    // drop: spout tip down to the water. columnRadius: the column at the water line, its
    // widest above the water. wallRadius: the basin's inner wall. All measured from the axis.
    public void Configure(float drop, float columnRadius, float wallRadius, Material sprayMaterial, Material poolMaterial)
    {
        this.drop = drop;
        this.columnRadius = columnRadius;
        this.wallRadius = wallRadius;
        this.sprayMaterial = sprayMaterial;
        this.poolMaterial = poolMaterial;
    }

    private void Start()
    {
        // Local units to metres.
        float metres = transform.lossyScale.y;
        height = drop * metres;
        innerLanding = columnRadius * metres + ColumnClearance;
        outerLanding = Mathf.Max(innerLanding, wallRadius * metres - WallClearance);
        gravity = -Physics.gravity.y;

        Transform water = AddPool(metres);
        if (sprayMaterial != null)
        {
            spray = AddSpray(water);
        }
    }

    // The droplets, on this object so OnParticleUpdateJobScheduled reaches it: emitted from
    // Update with hand-picked velocities, slowed by DragJob, and killed by the water plane,
    // which also spawns the splashes.
    private ParticleSystem AddSpray(Transform water)
    {
        Texture2D droplets = FountainWaterArt.DropletSheet();
        Texture2D splashSheet = FountainWaterArt.SplashSheet();
        textures = new[] { droplets, splashSheet };
        float cellMetres = FountainWaterArt.CellSize / FountainWaterArt.PixelsPerMetre;

        ParticleSystem system = SetUpSystem(gameObject, droplets, FountainWaterArt.DropletStages, FountainWaterArt.DropletVariants);
        ParticleSystem.MainModule main = system.main;
        main.maxParticles = Mathf.CeilToInt(dropletsPerSecond * 4f);
        main.gravityModifier = 1f;

        ParticleSystem.CollisionModule collision = system.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.Planes;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.SetPlane(0, water);
        collision.lifetimeLoss = 1f;
        collision.bounce = 0f;
        collision.radiusScale = 0.01f;

        // Each droplet keeps one variant (a random row) and plays its stages over its flight.
        ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
        sheet.animation = ParticleSystemAnimationType.SingleRow;
        sheet.rowMode = ParticleSystemAnimationRowMode.Random;

        var splashHolder = new GameObject("Splashes");
        splashHolder.transform.SetParent(transform, false);
        ParticleSystem splashes = SetUpSystem(splashHolder, splashSheet, FountainWaterArt.SplashFrames, 1);
        ParticleSystem.MainModule splashMain = splashes.main;
        splashMain.maxParticles = Mathf.CeilToInt(dropletsPerSecond * SplashChance * SplashLifetime * 2f);
        splashMain.startLifetime = SplashLifetime;
        splashMain.startSize = cellMetres;
        ParticleSystem.EmissionModule burst = splashes.emission;
        burst.enabled = true;
        burst.rateOverTime = 0f;
        burst.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        ParticleSystem.SubEmittersModule subEmitters = system.subEmitters;
        subEmitters.enabled = true;
        subEmitters.AddSubEmitter(splashes, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing, SplashChance);

        launch = new ParticleSystem.EmitParams { startSize = cellMetres };
        system.Play();
        return system;
    }

    // A world-space particle system of pixel-art billboards that plays a texture sheet over
    // each particle's life, lit by the scene like the ground.
    private ParticleSystem SetUpSystem(GameObject holder, Texture2D texture, int columns, int rows)
    {
        var system = holder.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // The fountain is scaled up a lot; keep sizes and speeds in plain metres.
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;
        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
        sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = columns;
        sheet.numTilesY = rows;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        sheet.cycleCount = 1;

        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = sprayMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var block = new MaterialPropertyBlock();
        block.SetTexture(BaseMapId, texture);
        block.SetVector(BaseMapStId, new Vector4(1f, 1f, 0f, 0f));
        renderer.SetPropertyBlock(block);
        return system;
    }

    // A flat ring at the water line from well inside the column (which is a little off
    // centre) to just inside the wall, its texture laid flat over the basin at the same
    // pixel size as the droplets. Returns the water line, which the droplets land on.
    private Transform AddPool(float metres)
    {
        var surface = new GameObject("Pool");
        surface.transform.SetParent(transform, false);
        surface.transform.localPosition = Vector3.down * drop;
        if (poolMaterial == null)
        {
            return surface.transform;
        }

        float half = wallRadius * 1.04f;
        int size = Mathf.Max(8, Mathf.RoundToInt(2f * half * metres * FountainWaterArt.PixelsPerMetre));
        float toTexture = size / (2f * half);
        // Most droplets come down near the inner end of their range.
        float landing = Mathf.Lerp(innerLanding, outerLanding, Mathf.Pow(0.5f, LandingBias)) / metres;
        poolFrames = FountainWaterArt.Pool(size, columnRadius * toTexture, landing * toTexture, wallRadius * toTexture);
        poolMesh = BuildRing(columnRadius * 0.75f, wallRadius * 1.02f, half);

        surface.AddComponent<MeshFilter>().sharedMesh = poolMesh;
        pool = surface.AddComponent<MeshRenderer>();
        pool.sharedMaterial = poolMaterial;
        pool.shadowCastingMode = ShadowCastingMode.Off;
        poolBlock = new MaterialPropertyBlock();
        poolBlock.SetVector(BaseMapStId, new Vector4(1f, 1f, 0f, 0f));
        return surface.transform;
    }

    // Flat ring facing up; UVs map [-half, half] on X and Z to [0, 1].
    private static Mesh BuildRing(float inner, float outer, float half)
    {
        var vertices = new Vector3[(PoolSegments + 1) * 2];
        var uvs = new Vector2[vertices.Length];
        var normals = new Vector3[vertices.Length];
        var triangles = new int[PoolSegments * 6];
        for (int i = 0; i <= PoolSegments; i++)
        {
            float angle = i / (float)PoolSegments * 2f * Mathf.PI;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices[i * 2] = direction * inner;
            vertices[i * 2 + 1] = direction * outer;
            for (int j = 0; j < 2; j++)
            {
                Vector3 v = vertices[i * 2 + j];
                uvs[i * 2 + j] = new Vector2(0.5f + v.x / (2f * half), 0.5f + v.z / (2f * half));
                normals[i * 2 + j] = Vector3.up;
            }
            if (i < PoolSegments)
            {
                int a = i * 2;
                // Clockwise seen from above, so the ring faces up.
                triangles[i * 6] = a;
                triangles[i * 6 + 1] = a + 2;
                triangles[i * 6 + 2] = a + 1;
                triangles[i * 6 + 3] = a + 2;
                triangles[i * 6 + 4] = a + 3;
                triangles[i * 6 + 5] = a + 1;
            }
        }
        var mesh = new Mesh { name = "Fountain Pool", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.normals = normals;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private void Update()
    {
        if (spray != null)
        {
            owed += dropletsPerSecond * Time.deltaTime;
            int count = (int)owed;
            owed -= count;
            Vector3 tip = transform.position;
            for (int i = 0; i < count; i++)
            {
                Launch(tip);
            }
        }
        if (pool != null)
        {
            int frame = (int)(Time.time * PoolFramesPerSecond) % FountainWaterArt.FrameCount;
            poolBlock.SetTexture(BaseMapId, poolFrames[frame]);
            pool.SetPropertyBlock(poolBlock);
        }
    }

    // One droplet, in a random direction round the spout. It picks where to come down and
    // how hard to go up; the sideways speed then follows from the drag. With linear drag
    // the sideways distance levels off at speed / Drag, almost reached by the time it lands.
    private void Launch(Vector3 tip)
    {
        float angle = Random.value * 2f * Mathf.PI;
        var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        float landing = Mathf.Lerp(innerLanding, outerLanding, Mathf.Pow(Random.value, LandingBias));
        float rise = Mathf.Lerp(-MaxSink, MaxRise, Mathf.Pow(Random.value, 0.9f));
        float flight = FlightTime(rise);
        float sideways = Drag * landing / (1f - Mathf.Exp(-Drag * flight));
        Vector2 nozzle = Random.insideUnitCircle * NozzleRadius;

        launch.position = tip + new Vector3(nozzle.x, 0f, nozzle.y);
        launch.velocity = outward * sideways + Vector3.up * rise;
        // The water plane ends it; the extra time is only slack.
        launch.startLifetime = flight + 0.25f;
        spray.Emit(launch, 1);
    }

    // Unity's own particle drag takes the same amount off every droplet's speed, however fast
    // it's going, so the fast ones fly far past the basin. This takes off a share instead
    // (linear drag), which is what Launch and FlightTime aim with. Runs after each step.
    private void OnParticleUpdateJobScheduled()
    {
        if (spray != null)
        {
            new DragJob { keep = Mathf.Exp(-Drag * Time.deltaTime) }.Schedule(spray, 1024);
        }
    }

    private struct DragJob : IJobParticleSystemParallelFor
    {
        public float keep;

        public void Execute(ParticleSystemJobData particles, int i)
        {
            ParticleSystemNativeArray3 velocities = particles.velocities;
            velocities.x[i] *= keep;
            velocities.y[i] *= keep;
            velocities.z[i] *= keep;
        }
    }

    // Seconds for a droplet launched upward at rise m/s to fall to the water. With linear
    // drag k its height is y(t) = (rise + g/k)(1 - e^-kt)/k - g t/k; Newton's method on
    // y(t) = -height settles in a few steps from a guess of falling the whole way at g/k.
    private float FlightTime(float rise)
    {
        float terminal = gravity / Drag;
        float a = rise + terminal;
        float t = 0.5f + height / terminal;
        for (int i = 0; i < 4; i++)
        {
            float e = Mathf.Exp(-Drag * t);
            float y = a * (1f - e) / Drag - terminal * t + height;
            t -= y / (a * e - terminal);
        }
        return t;
    }

    private void OnDestroy()
    {
        DestroyAll(textures);
        DestroyAll(poolFrames);
        if (poolMesh != null)
        {
            Destroy(poolMesh);
        }
    }

    private static void DestroyAll(Object[] objects)
    {
        if (objects == null)
        {
            return;
        }
        foreach (Object item in objects)
        {
            if (item != null)
            {
                Destroy(item);
            }
        }
    }
}
