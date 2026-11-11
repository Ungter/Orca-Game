using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class ClueMinigameTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<GameObject> created = new List<GameObject>();
    private Collider ground;
    private GameObject inventoryObject;
    private InventorySystem inventory;
    private ClueMinigame game;
    private GameObject player;

    [SetUp]
    public void SetUp()
    {
        // A 120 x 60 m island (5 m cells, 24 x 12 of them) with its top at y = 0.
        ground = Box(ClueMinigame.GroundName, new Vector3(0f, -0.5f, 0f), new Vector3(120f, 1f, 60f));
        player = new GameObject("Player") { tag = "Player" };
        player.transform.position = new Vector3(0f, 1.1f, -20f);
        CapsuleCollider body = player.AddComponent<CapsuleCollider>();
        body.height = 1.8f;
        created.Add(player);
        inventoryObject = new GameObject("Clue hunt test");
        created.Add(inventoryObject);
        inventory = inventoryObject.AddComponent<InventorySystem>();
        typeof(InventorySystem).GetMethod("Awake", Private).Invoke(inventory, null);
        game = inventoryObject.AddComponent<ClueMinigame>();
        typeof(ClueMinigame).GetMethod("Awake", Private).Invoke(game, null);
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        // Edit mode never calls OnDestroy; it clears the potions, walls and textures.
        typeof(ClueMinigame).GetMethod("OnDestroy", Private).Invoke(game, null);
        foreach (GameObject item in created)
        {
            if (item != null) Object.DestroyImmediate(item);
        }
        created.Clear();
    }

    [Test]
    public void SurveySeesBuildingsAndWallsAndRoutesWalkAroundThem()
    {
        Box("building9", new Vector3(20f, 3f, 0f), new Vector3(10f, 6f, 10f));
        // A fence across the island with a gap at its +z end.
        Box("Fence", new Vector3(-20f, 1f, -5f), new Vector3(0.3f, 2f, 50f));
        ClueMap map = Survey();

        int inside = map.SampleAt(new Vector3(20f, 0f, 0f));
        Assert.AreEqual(ClueMap.Kind.Building, map.Kinds[inside]);
        Assert.IsFalse(map.Walkable[inside]);
        int open = map.SampleAt(new Vector3(1f, 0f, 1f));
        Assert.AreEqual(ClueMap.Kind.Ground, map.Kinds[open]);
        Assert.IsTrue(map.Walkable[open] && map.Open[open] && map.Roomy[open]);
        Assert.IsTrue(map.Walkable[map.SampleAt(player.transform.position)], "The player's own collider is ignored.");

        var distances = new int[map.SampleCount];
        map.Distances(open, distances);
        int across = map.SampleAt(new Vector3(13f, 0f, 9f));
        Vector2Int a = map.CellOf(open), b = map.CellOf(across);
        Assert.AreEqual(Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y), distances[across], "Open ground costs one step per cell crossed.");

        int left = map.SampleAt(new Vector3(-23f, 0f, -12f));
        int right = map.SampleAt(new Vector3(-17f, 0f, -12f));
        map.Distances(left, distances);
        Assert.AreNotEqual(ClueMap.Unreachable, distances[right]);
        Assert.Greater(distances[right], 8, "The walk goes round the fence's end, not through it.");
    }

    [Test]
    public void HuntPlacesFivePotionsInOpenCellsOfTheirOwnAndBudgetsTheBestRoute()
    {
        Box("building9", new Vector3(20f, 3f, 0f), new Vector3(10f, 6f, 10f));
        ClueMap map = Survey();
        int start = map.NearestWalkable(new Vector3(0f, 0f, -20f), 3);
        var potions = new List<int>();
        Assert.IsTrue(map.PlanHunt(start, ClueMinigame.PotionCount, new System.Random(7), potions, out int budget));
        Assert.AreEqual(ClueMinigame.PotionCount, potions.Count);

        var cells = new HashSet<Vector2Int> { map.CellOf(start) };
        var house = new Bounds(new Vector3(20f, 3f, 0f), new Vector3(12f, 6f, 12f));
        var fields = new List<int[]>();
        var fromStart = new int[map.SampleCount];
        map.Distances(start, fromStart);
        fields.Add(fromStart);
        foreach (int potion in potions)
        {
            Assert.IsTrue(map.IsPotionSpot(potion));
            Assert.IsTrue(cells.Add(map.CellOf(potion)), "Every potion has a cell of its own, away from the start.");
            Vector3 at = map.SamplePosition(potion);
            Assert.IsFalse(house.Contains(new Vector3(at.x, 1f, at.z)), "No potion stands in or against the building.");
            var field = new int[map.SampleCount];
            map.Distances(potion, field);
            fields.Add(field);
        }
        int best = ClueMap.ShortestRoute(fields, potions);
        Assert.Greater(best, 0);
        Assert.GreaterOrEqual(budget, best + 4, "The budget leaves room over the best route.");
    }

    [Test]
    public void CrossingCellsCostsStepsAndFindingAllFivePotionsLetsTheAlchemistWriteTheClue()
    {
        inventory.TryAdd("Clue Letter");
        Vector3 start = player.transform.position;
        Assert.IsTrue(game.StartHunt(start));
        Assert.AreEqual(ClueMinigame.HuntState.Playing, game.State);
        Assert.AreEqual(game.StepBudget, game.StepsLeft);
        Assert.AreEqual(ClueMinigame.PotionCount, game.PotionPositions.Count);

        float cell = game.Map.CellSize;
        game.Track(start + new Vector3(cell * 0.5f, 0f, 0f));
        int steps = game.StepsLeft;
        game.Track(start + new Vector3(cell * 1.6f, 0f, 0f));
        Assert.AreEqual(steps - 1, game.StepsLeft, "Leaving a cell costs one step.");
        game.Track(start + new Vector3(cell * 1.6f, 0f, cell * 0.4f));
        Assert.AreEqual(steps - 1, game.StepsLeft, "Walking about inside a cell is free.");

        typeof(ClueMinigame).GetProperty("StepsLeft").SetValue(game, 10000);
        foreach (Vector3 potion in game.PotionPositions) game.Track(potion + Vector3.up * 1.1f);
        Assert.AreEqual(0, game.PotionsLeft);
        Assert.IsTrue(game.IsFound);
        Assert.IsTrue(game.Deliver());
        Assert.IsTrue(game.IsDelivered);
        Assert.IsTrue(inventory.HasRevealedWord(ClueMinigame.ClueWord));
        Assert.IsFalse(game.Deliver(), "The clue is written once.");
    }

    [Test]
    public void RunningOutOfStepsClearsTheHuntAndItStartsAgainOnlyOnTheIsland()
    {
        Vector3 start = player.transform.position;
        Assert.IsTrue(game.StartHunt(start));
        typeof(ClueMinigame).GetProperty("StepsLeft").SetValue(game, 1);
        float cell = game.Map.CellSize;
        game.Track(start + new Vector3(cell * 3.5f, 0f, 0f));
        Assert.AreEqual(ClueMinigame.HuntState.Idle, game.State);
        Assert.AreEqual(0, game.PotionsLeft);
        Assert.IsNull(GameObject.Find("Clue potion (0)"));

        Assert.IsFalse(game.StartHunt(new Vector3(500f, 1f, 0f)), "Off the island there is nowhere to start.");
        Assert.IsTrue(game.StartHunt(new Vector3(-40f, 1.1f, 15f)));
        Assert.AreEqual(ClueMinigame.PotionCount, game.PotionsLeft);
    }

    [Test]
    public void TheLetterOffersEToStartTheHuntAndUnderThePotionCountEToStopIt()
    {
        inventory.TryAdd("Clue Letter");
        Assert.IsTrue(game.SetHeld(true));
        game.SurveyNow();
        Assert.AreEqual("[E] START THE HUNT", Label("statusLabel"));
        Assert.AreEqual("", Label("promptLabel"));

        Assert.IsTrue(game.StartHunt(player.transform.position));
        Assert.AreEqual("POTIONS 0/" + ClueMinigame.PotionCount, Label("statusLabel"));
        Assert.AreEqual("[E] STOP THE HUNT", Label("promptLabel"));
        RectTransform counter = ((PixelText)typeof(ClueMinigame).GetField("statusLabel", Private).GetValue(game)).rectTransform;
        RectTransform prompt = ((PixelText)typeof(ClueMinigame).GetField("promptLabel", Private).GetValue(game)).rectTransform;
        Assert.Less(prompt.anchorMax.y, counter.anchorMin.y + 1e-4f, "The stop prompt sits under the potion count.");
    }

    [Test]
    public void StoppingTheHuntClearsItReadyToStartAgain()
    {
        Assert.IsFalse(game.StopHunt(), "No hunt to stop yet.");
        Assert.IsTrue(game.StartHunt(player.transform.position));
        Assert.IsTrue(game.StopHunt());
        Assert.AreEqual(ClueMinigame.HuntState.Idle, game.State);
        Assert.AreEqual(0, game.PotionsLeft);
        Assert.IsNull(GameObject.Find("Clue potion (0)"));
        Assert.IsTrue(game.StartHunt(player.transform.position));
    }

    [Test]
    public void ThePlayerWalksFasterOnlyWhileHunting()
    {
        PlayerController walker = player.AddComponent<PlayerController>();
        Assert.IsTrue(game.StartHunt(player.transform.position));
        typeof(ClueMinigame).GetMethod("Update", Private).Invoke(game, null);
        Assert.AreEqual(1.6f, walker.SpeedMultiplier, 1e-4f);
        typeof(ClueMinigame).GetMethod("ResetHunt", Private).Invoke(game, null);
        typeof(ClueMinigame).GetMethod("Update", Private).Invoke(game, null);
        Assert.AreEqual(1f, walker.SpeedMultiplier, 1e-4f);
    }

    [Test]
    public void DebugCompletionClearsAPartialHuntRestoresSpeedAndWritesTheClueOnce()
    {
        PlayerController walker = player.AddComponent<PlayerController>();
        inventory.TryAdd("Clue Letter");
        Assert.IsTrue(game.StartHunt(player.transform.position));
        typeof(ClueMinigame).GetMethod("Update", Private).Invoke(game, null);
        Assert.AreEqual(1.6f, walker.SpeedMultiplier, 1e-4f);
        Vector3 first = game.PotionPositions[0];
        game.Track(first + Vector3.up * 1.1f);
        Assert.Greater(game.PotionsLeft, 0);
        Assert.IsTrue(game.DebugComplete());
        Assert.IsTrue(game.IsDelivered);
        Assert.IsFalse(ClueMinigame.HuntUnderway);
        Assert.AreEqual(0, game.PotionsLeft);
        Assert.AreEqual(1f, walker.SpeedMultiplier, 1e-4f);
        Assert.IsTrue(inventory.HasRevealedWord(ClueMinigame.ClueWord));
        Assert.IsTrue(game.DebugComplete());
        Assert.AreEqual(1, inventory.RevealedWords.Count);
    }

    [Test]
    public void TheMapFillsItsFrameOnTheLetterAndLeavesThePaperAroundIt()
    {
        ClueMap map = Survey();
        var frame = new ClueMapArt.Frame(ground.bounds, ClueMapArt.MapArea);
        var point = new Vector3(13f, 0f, -21f);
        Vector3 back = frame.ToWorld(frame.ToLetter(point).x, frame.ToLetter(point).y);
        Assert.AreEqual(point.x, back.x, 1e-3f);
        Assert.AreEqual(point.z, back.z, 1e-3f);
        Assert.Greater(frame.ToLetter(new Vector3(0f, 0f, -10f)).x, frame.ToLetter(Vector3.zero).x, "Map right is world -z.");
        Assert.Less(frame.ToLetter(new Vector3(10f, 0f, 0f)).y, frame.ToLetter(Vector3.zero).y, "Map up is world +x.");

        Color32[] pixels = ClueMapArt.DrawMap(map, frame, null, null);
        Vector2 centre = frame.ToLetter(ground.bounds.center);
        Assert.Greater(pixels[ClueMapArt.Index((int)centre.x, (int)centre.y)].a, 200);
        Assert.AreEqual(0, pixels[ClueMapArt.Index(2, 2)].a, "Outside the frame the letter's paper shows.");
    }

    [Test]
    public void TheClueWordHasEveryCursiveGlyph()
    {
        var glyphs = (System.Collections.IDictionary)typeof(CursiveWriter)
            .GetField("Glyphs", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        foreach (char c in ClueMinigame.ClueWord) Assert.IsTrue(glyphs.Contains(c), "No cursive glyph for '" + c + "'");
    }

    // What one of the held letter's lines says.
    private string Label(string field)
    {
        typeof(ClueMinigame).GetMethod("UpdateLabels", Private).Invoke(game, null);
        return ((PixelText)typeof(ClueMinigame).GetField(field, Private).GetValue(game)).text;
    }

    private ClueMap Survey()
    {
        Physics.SyncTransforms();
        var map = new ClueMap(ground, null, player.GetComponentsInChildren<Collider>());
        Assert.IsTrue(map.Advance(int.MaxValue));
        return map;
    }

    private Collider Box(string name, Vector3 centre, Vector3 size)
    {
        var box = new GameObject(name);
        box.transform.position = centre;
        BoxCollider collider = box.AddComponent<BoxCollider>();
        collider.size = size;
        created.Add(box);
        Physics.SyncTransforms();
        return collider;
    }
}
