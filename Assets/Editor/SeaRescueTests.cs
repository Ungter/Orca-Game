using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SeaRescueTests
{
    private Scene scene;
    private PlayerController player;
    private CharacterController body;
    private SeaRescue rescue;
    private LighthouseIsland lighthouse;
    private Transform lighthouseIsland;
    private Collider ocean;
    private Collider ground;
    private float sea;

    [SetUp]
    public void SetUp()
    {
        scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.TryGetComponent(out PlayerController p)) player = p;
            if (root.TryGetComponent(out LighthouseIsland s)) lighthouse = s;
            if (root.name == "Lighthouse island") lighthouseIsland = root.transform;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == MouseTrapIsland.GroundName) ground = t.GetComponent<Collider>();
        }
        Assert.IsNotNull(player);
        Assert.IsNotNull(lighthouse);
        typeof(PlayerController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);
        body = player.GetComponent<CharacterController>();
        rescue = player.gameObject.AddComponent<SeaRescue>();
        Renderer oceanRenderer = OceanHorizon.FindOcean(scene);
        ocean = oceanRenderer.GetComponent<Collider>();
        sea = oceanRenderer.bounds.max.y;
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
    }

    // Stands the player at a spot and lets the controller settle onto what is there.
    private void StandAt(Vector3 feet)
    {
        player.PlaceOnGround(feet);
        Physics.SyncTransforms();
        body.Move(Vector3.down * 0.2f);
        rescue.Tick(0.02f);
    }

    // Open dry ground walked a metre at a time from inland out to the shore of an island (the main
    // island's ground, or anything of the lighthouse island), and the sea just past it.
    private (List<Vector3> path, Vector3 water) WalkToSea(Vector3 inland, Transform island)
    {
        for (int turn = 0; turn < 16; turn++)
        {
            Vector3 dir = Quaternion.Euler(0f, turn * 22.5f, 0f) * Vector3.forward;
            var path = new List<Vector3>();
            for (float d = 0f; d < 300f; d += 1f)
            {
                Vector3 p = inland + dir * d;
                if (!Physics.Raycast(new Vector3(p.x, 200f, p.z), Vector3.down, out RaycastHit hit, 400f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || hit.collider.transform.IsChildOf(player.transform))
                {
                    path.Clear();
                    continue;
                }
                if (hit.collider == ocean)
                {
                    if (path.Count >= 6) return (path, new Vector3(p.x, sea, p.z));
                    break;
                }
                // Anything of the island (ground, paths, the shore rocks), as long as it is walked
                // at an even height (a tree's crown breaks the walk).
                bool land = island == null ? !hit.collider.transform.IsChildOf(lighthouseIsland) : hit.collider.transform.IsChildOf(island);
                if (!land || hit.point.y <= sea + 0.5f)
                {
                    path.Clear();
                    continue;
                }
                if (path.Count > 0 && Mathf.Abs(hit.point.y - path[path.Count - 1].y) > 1.5f) path.Clear();
                path.Add(hit.point);
            }
        }
        Assert.Fail("No open walk out to the sea from " + inland);
        return (null, default);
    }

    private void FinishRescue()
    {
        for (int i = 0; i < 100 && rescue.CurrentPhase != SeaRescue.Phase.None; i++) rescue.Tick(0.05f);
        Assert.AreEqual(SeaRescue.Phase.None, rescue.CurrentPhase);
    }

    private static float Across(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    [Test]
    public void SteppingIntoTheSeaFadesOutAndWakesThePlayerOnLandTheyJustWalked()
    {
        var (path, water) = WalkToSea(ground.bounds.center, null);
        foreach (Vector3 spot in path) StandAt(spot);
        Assert.AreEqual(SeaRescue.Phase.None, rescue.CurrentPhase, "Dry land is safe.");

        StandAt(water);
        Assert.AreEqual(SeaRescue.Phase.Sinking, rescue.CurrentPhase, "In the sea, the rescue begins.");
        Assert.IsFalse(player.enabled, "The player can't move while it happens.");
        Assert.IsTrue(SeaRescue.IsRescuing);
        Assert.IsTrue(Interactables.Busy && Interactables.BlocksMovement, "Nothing else answers meanwhile.");
        float surface = player.transform.position.y;
        rescue.Tick(SeaRescue.SinkTime * 0.6f);
        Assert.Less(player.transform.position.y, surface, "They sink...");
        Assert.Greater(rescue.Fade, 0f, "...as the screen fades to black.");
        rescue.Tick(SeaRescue.SinkTime);
        Assert.AreEqual(SeaRescue.Phase.Black, rescue.CurrentPhase);
        Assert.AreEqual(1f, rescue.Fade);

        Vector3 woke = rescue.WokeAt;
        Assert.IsTrue(path.Exists(p => Across(p, woke) < 0.3f), "Back on ground they just stood on.");
        Assert.GreaterOrEqual(Across(woke, water), 2.5f, "A few steps back from the water, not on its edge.");
        Assert.Less(Across(woke, water), 6f, "But near where they went in.");
        FinishRescue();
        Assert.IsTrue(player.enabled && body.enabled, "Free to walk again.");
        Assert.AreEqual(0f, rescue.Fade);
        Assert.IsFalse(SeaRescue.IsRescuing);
        Assert.Less(Across(player.transform.position, woke), 0.01f);
    }

    [Test]
    public void OffTheLighthouseIslandTheyWakeThereOnlyOnceTheAnagramHasOpenedIt()
    {
        var (mainPath, _) = WalkToSea(ground.bounds.center, null);
        var (lighthousePath, lighthouseWater) = WalkToSea(lighthouse.Entrance, lighthouseIsland);

        // Not yet open: back to the main island.
        foreach (Vector3 spot in mainPath) StandAt(spot);
        foreach (Vector3 spot in lighthousePath) StandAt(spot);
        Assert.IsFalse(lighthouse.IsUnlocked);
        Assert.Greater(rescue.Remembered, mainPath.Count / 2, "Places walked on land are remembered.");
        StandAt(lighthouseWater);
        Vector3 woke = SinkAndWake();
        Assert.IsTrue(mainPath.Exists(p => Across(p, woke) < 0.3f), "Not open yet: woken on the main island, not at " + woke);
        FinishRescue();

        // Open: back to the lighthouse island.
        LighthouseTestProgress.Solve(lighthouse.gameObject);
        Assert.IsTrue(lighthouse.IsUnlocked);
        foreach (Vector3 spot in lighthousePath) StandAt(spot);
        StandAt(lighthouseWater);
        woke = SinkAndWake();
        Assert.IsTrue(lighthousePath.Exists(p => Across(p, woke) < 0.3f), "Open: woken on the lighthouse island, not at " + woke);
        Assert.GreaterOrEqual(Across(woke, lighthouseWater), 2.5f);
        FinishRescue();

        // Gone in off the main island, they still come back to the main island.
        foreach (Vector3 spot in mainPath) StandAt(spot);
        var (_, mainWater) = WalkToSea(ground.bounds.center, null);
        StandAt(mainWater);
        woke = SinkAndWake();
        Assert.IsTrue(mainPath.Exists(p => Across(p, woke) < 0.3f), "Off the main island: woken there, not at " + woke);
        FinishRescue();
    }

    // Lets the player sink out of sight and returns where they were put back down.
    private Vector3 SinkAndWake()
    {
        Assert.AreEqual(SeaRescue.Phase.Sinking, rescue.CurrentPhase, "In the sea, the rescue begins.");
        rescue.Tick(SeaRescue.SinkTime + 0.01f);
        Assert.AreEqual(SeaRescue.Phase.Black, rescue.CurrentPhase);
        return rescue.WokeAt;
    }

    [Test]
    public void NothingHappensWhileThePlayerIsCarriedOrNotWalking()
    {
        var (path, water) = WalkToSea(ground.bounds.center, null);
        foreach (Vector3 spot in path) StandAt(spot);
        // On the ferry or the opening's boat, the character controller is off.
        body.enabled = false;
        player.PlaceOnGround(water);
        rescue.Tick(0.1f);
        Assert.AreEqual(SeaRescue.Phase.None, rescue.CurrentPhase, "Carried over the sea, nothing happens.");
        body.enabled = true;
        player.SetDebugFly(true);
        player.PlaceOnGround(water);
        rescue.Tick(0.1f);
        Assert.AreEqual(SeaRescue.Phase.None, rescue.CurrentPhase, "Nor while debug flying.");
        player.SetDebugFly(false);
    }
}
