using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MouseTrapWorldTests
{
    private Scene scene;
    private LighthouseMouseTrap trap;
    private LighthouseStacker stacker;
    private PlayerController player;
    private Camera view;

    [SetUp]
    public void SetUp()
    {
        scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (trap == null) trap = root.GetComponentInChildren<LighthouseMouseTrap>(true);
            if (root.TryGetComponent(out PlayerController p)) player = p;
            if (root.TryGetComponent(out LighthouseStacker s)) stacker = s;
            foreach (Camera c in root.GetComponentsInChildren<Camera>(true))
                if (c.CompareTag("MainCamera")) view = c;
        }
        Assert.IsNotNull(trap, "Tools > Orca > Set Up Mouse Trap has been run on the scene.");
        Assert.IsNotNull(player);
        Assert.IsNotNull(view);
        trap.Seed = 12;
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        if (trap != null) trap.Abort();
        if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
    }

    private void Unlock() => LighthouseTestProgress.Solve(trap.gameObject);

    // Begins and runs the fade, the split and the settling until the player can aim.
    private void Play()
    {
        Unlock();
        Assert.IsTrue(trap.Begin(player.transform, view));
        for (int i = 0; i < 80 && trap.CurrentPhase != LighthouseMouseTrap.Phase.Aiming; i++) trap.Tick(0.1f);
        Assert.AreEqual(LighthouseMouseTrap.Phase.Aiming, trap.CurrentPhase);
    }

    private void RunUntil(LighthouseMouseTrap.Phase phase, float limit = 20f)
    {
        for (float t = 0f; t < limit && trap.CurrentPhase != phase; t += 0.1f) trap.Tick(0.1f);
        Assert.AreEqual(phase, trap.CurrentPhase);
    }

    private Hex OpenAwayFromMouse()
    {
        Hex best = default;
        int far = -1;
        foreach (Hex cell in trap.Board.Cells)
        {
            if (!trap.Board.CanStrike(cell)) continue;
            int distance = Hex.Distance(cell, trap.Board.Mouse);
            if (distance > far) { far = distance; best = cell; }
        }
        return best;
    }

    [Test]
    public void TheGameIsLockedUntilTheAnagramIsSolvedAndItsStairDoesNotCrowdTheStacker()
    {
        Assert.AreEqual("Lighthouse-aiStandardSurface6 12", trap.Trigger.name);
        Assert.AreEqual("pCylinder3", trap.Lookout.name);
        player.transform.position = trap.Entrance.position + Vector3.up;
        Assert.IsFalse(trap.IsUnlocked);
        Assert.AreEqual(float.MaxValue, trap.InteractDistanceSqr(), "No prompt before the anagram.");
        Assert.IsFalse(trap.Begin(player.transform, view));

        Unlock();
        Assert.Less(trap.InteractDistanceSqr(), 1f, "At the stair's foot, E starts it.");
        // Edit mode doesn't run OnEnable, which registers them in play.
        Interactables.Add(trap);
        Interactables.Add(stacker);
        try
        {
            Assert.IsTrue(Interactables.IsNearest(trap));
            player.transform.position = stacker.Entrance + Vector3.up;
            Assert.AreEqual(float.MaxValue, trap.InteractDistanceSqr(), "At the stacker's ring, the stacker answers E.");
            Assert.IsTrue(Interactables.IsNearest(stacker));
        }
        finally
        {
            Interactables.Remove(trap);
            Interactables.Remove(stacker);
        }
    }

    [Test]
    public void BeginningFadesToBlackThenCarriesThePlayerIntoTheLanternWithTheLighthouseLighting()
    {
        Unlock();
        Light moon = RenderSettings.sun;
        if (moon == null)
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Light l in root.GetComponentsInChildren<Light>(true))
                    if (l.type == LightType.Directional) moon = l;
        float fog = RenderSettings.fogDensity;
        LighthouseBeam beam = trap.Lookout.parent.GetComponentInChildren<LighthouseBeam>(true);
        Assert.IsNotNull(beam);
        Assert.IsTrue(trap.Begin(player.transform, view));
        Assert.IsTrue(LighthouseMouseTrap.IsOpen);
        Assert.IsTrue(Interactables.Busy && Interactables.BlocksMovement);
        trap.Tick(0.3f);
        Assert.AreEqual(LighthouseMouseTrap.Phase.Entering, trap.CurrentPhase);
        Assert.AreEqual(0.5f, trap.Fade, 0.01f, "Fading to black first.");
        Assert.IsNull(trap.Island, "Nothing changes until the screen is black.");
        trap.Tick(0.4f);
        Assert.AreEqual(LighthouseMouseTrap.Phase.Splitting, trap.CurrentPhase);
        Assert.IsNotNull(trap.Island);

        Bounds lantern = trap.Lookout.GetComponent<Renderer>().bounds;
        Assert.IsTrue(lantern.Contains(view.transform.position), "The view is from inside the lantern.");
        Assert.IsTrue(lantern.Contains(player.transform.position + Vector3.up), "The player is up there too.");
        foreach (Renderer r in player.GetComponentsInChildren<Renderer>(true)) Assert.IsFalse(r.enabled, "First person: the player isn't seen.");
        Assert.IsFalse(beam.gameObject.activeInHierarchy, "The turning beam is out.");
        Assert.Less(RenderSettings.fogDensity, fog, "The fog thins so the island reads from up there.");
        Vector3 towardsIsland = trap.Island.BoardBounds.center - trap.LanternPosition;
        Vector3 fall = moon.transform.forward;
        Assert.Greater(Vector2.Dot(new Vector2(fall.x, fall.z).normalized, new Vector2(towardsIsland.x, towardsIsland.z).normalized), 0.99f,
            "The moonlight falls from the lighthouse.");
        Assert.Less(fall.y, 0f);

        for (int i = 0; i < 60 && trap.CurrentPhase != LighthouseMouseTrap.Phase.Aiming; i++) trap.Tick(0.1f);
        Assert.AreEqual(LighthouseMouseTrap.Phase.Aiming, trap.CurrentPhase);
        Assert.AreEqual(0f, trap.Fade);
        Assert.IsTrue(trap.AimBeam.gameObject.activeInHierarchy, "The beam shines on the aim.");
        Assert.IsTrue(trap.AimedLight.gameObject.activeInHierarchy);
        Assert.AreEqual(trap.LanternPosition, trap.AimBeam.GetComponent<MeshFilter>().sharedMesh.vertices[0], "From the lantern.");
        AssertLit(trap.Aim, 0.01f);
    }

    // The beam lights exactly this hex: its six corners, on whatever it falls on.
    private void AssertLit(Hex hex, float tolerance)
    {
        for (int i = 0; i < 6; i++)
        {
            Vector2 corner = trap.Island.Grid.Corner(hex, i);
            Vector3 lit = trap.LitHex[i];
            Assert.AreEqual(corner.x, lit.x, tolerance, "Corner " + i + " of " + hex);
            Assert.AreEqual(corner.y, lit.z, tolerance, "Corner " + i + " of " + hex);
            Assert.AreEqual(trap.Island.LitHeight(lit.x, lit.z), lit.y, 0.01f, "On the ground or the water.");
        }
    }

    private Vector2 LitMiddle()
    {
        Vector2 sum = Vector2.zero;
        foreach (Vector3 corner in trap.LitHex) sum += new Vector2(corner.x, corner.z);
        return sum / 6f;
    }

    [Test]
    public void TheHousesAreWallsAndTheVillainStartsInTheHeartOfTheIsland()
    {
        Play();
        Assert.Greater(trap.Board.Cells.Count, 150);
        var houses = new Dictionary<string, Vector3>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "building1" && t.parent != null && t.parent.name == "world") houses["magician"] = Centre(t);
                if (t.name == "building3" && t.parent == null) houses["alchemist"] = Centre(t);
                if (t.name == "Roof" && t.parent != null && t.parent.name == "world") houses["cachud"] = Centre(t);
            }
        Assert.AreEqual(3, houses.Count);
        foreach (var house in houses)
        {
            Hex under = trap.Island.Grid.At(house.Value.x, house.Value.z);
            Assert.IsTrue(trap.Board.IsWall(under), "The " + house.Key + "'s house stands on a wall.");
        }
        Assert.IsTrue(trap.Board.IsOpen(trap.Board.Mouse));
        CollectionAssert.Contains(trap.Board.CentralCells(), trap.Board.Mouse, "He starts towards the middle.");
        Assert.AreEqual(trap.Island.Surface(trap.Board.Mouse).y + LighthouseMouseTrap.MouseSize * 0.5f, trap.Mouse.position.y, 0.05f,
            "He stands on his hex.");
    }

    private static Vector3 Centre(Transform t)
    {
        Renderer[] all = t.GetComponentsInChildren<Renderer>(true);
        Bounds b = all[0].bounds;
        foreach (Renderer r in all) b.Encapsulate(r.bounds);
        return b.center;
    }

    [Test]
    public void AStruckHexSinksIntoTheSeaAndThenTheVillainTakesAStepTowardsTheShore()
    {
        Play();
        Hex target = OpenAwayFromMouse();
        float top = trap.Island.Surface(target).y;
        Hex before = trap.Board.Mouse;
        int distance = trap.Board.ShoreDistances()[before];
        Assert.IsTrue(trap.AimAt(target));
        Assert.IsTrue(trap.TryStrike());
        Assert.AreEqual(LighthouseMouseTrap.Phase.Striking, trap.CurrentPhase);
        trap.Tick(0.05f);
        Assert.IsTrue(trap.StrikeLight.enabled, "The strike flashes from the lantern.");
        Assert.AreEqual(trap.LanternPosition, trap.StrikeLight.transform.position);
        RunUntil(LighthouseMouseTrap.Phase.Aiming);
        Assert.IsTrue(trap.Board.IsSunk(target));
        Assert.AreEqual(1, Hex.Distance(before, trap.Board.Mouse), "He moved one hex.");
        Assert.AreEqual(distance - 1, trap.Board.ShoreDistances()[trap.Board.Mouse], "Nearer the shore.");
        for (int i = 0; i < 40; i++) trap.Tick(0.1f);
        Assert.AreEqual(1f, trap.Island.Sunk(target), 1e-3f, "Gone under.");
        Assert.Less(trap.Island.Surface(target).y, trap.Island.SeaLevel - 2f, "Below the water.");
        Assert.Greater(top, trap.Island.SeaLevel);

        Hex wall = default;
        foreach (Hex w in trap.Board.Walls) { wall = w; break; }
        Assert.IsTrue(trap.AimAt(wall));
        Assert.IsFalse(trap.TryStrike(), "A house can't be struck.");
        Assert.AreEqual(LighthouseMouseTrap.Phase.Aiming, trap.CurrentPhase);
    }

    [Test]
    public void AimingMovesHexByHexTheWayPressedOnScreen()
    {
        Play();
        for (int i = 0; i < 30; i++) trap.Tick(0.1f);
        Hex start = trap.Aim;
        Vector3 here = view.WorldToScreenPoint(trap.Island.Surface(start));
        foreach (var (key, axis, sign) in new[] { (Vector2Int.right, 0, 1f), (Vector2Int.left, 0, -1f), (Vector2Int.up, 1, 1f), (Vector2Int.down, 1, -1f) })
        {
            Assert.IsTrue(trap.AimAt(start));
            if (!trap.MoveAim(key)) continue;
            Assert.AreEqual(1, Hex.Distance(start, trap.Aim), "One hex at a time.");
            Vector3 there = view.WorldToScreenPoint(trap.Island.Surface(trap.Aim));
            Assert.Greater((there[axis] - here[axis]) * sign, 0f, "It moved the way pressed (" + key + ").");
        }
    }

    // No outline: the lighthouse's beam lights exactly the aimed hex (no round spot smearing
    // across the ground at the lantern's low angle) and swings across, quickly but smoothly, when
    // the aim moves.
    [Test]
    public void TheLighthouseBeamLightsExactlyTheAimedHexAndSwingsSmoothlyToTheNext()
    {
        Play();
        for (int i = 0; i < 10; i++) trap.Tick(0.1f);
        Transform beam = trap.AimBeam;
        Assert.IsNotNull(beam);
        Assert.IsTrue(beam.gameObject.activeInHierarchy, "The beam shines while aiming.");
        Assert.AreEqual("Orca/Hex Beam", beam.GetComponent<Renderer>().sharedMaterial.shader.name, "A six-sided shaft, not a soft cone.");
        Vector3[] shaft = beam.GetComponent<MeshFilter>().sharedMesh.vertices;
        Assert.AreEqual(trap.LanternPosition, shaft[0], "From the lantern.");
        for (int i = 0; i < 6; i++)
        {
            Vector3 along = (shaft[1 + i] - shaft[0]).normalized;
            Assert.Greater(Vector3.Dot(along, (trap.LitHex[i] - shaft[0]).normalized), 0.99999f, "Its edges run through the hex's corners.");
        }
        AssertLit(trap.Aim, 0.01f);
        // Where it is drawn, not just the numbers it was built from.
        Bounds drawn = trap.AimedLight.GetComponent<Renderer>().bounds;
        Vector2 middle = trap.Island.Grid.Centre(trap.Aim);
        Assert.AreEqual(middle.x, drawn.center.x, 0.5f, "The lit hex is drawn on the aimed hex.");
        Assert.AreEqual(middle.y, drawn.center.z, 0.5f);
        Assert.AreEqual(trap.Island.Grid.Width * 0.5f, drawn.extents.x, 0.5f);
        Assert.Less(Mathf.Abs(drawn.center.y - trap.Island.Top(trap.Aim).y), 6f, "On the ground.");
        Bounds shaftDrawn = beam.GetComponent<Renderer>().bounds;
        Assert.IsTrue(shaftDrawn.Contains(trap.LanternPosition + (trap.Island.Top(trap.Aim) - trap.LanternPosition) * 0.5f),
            "The shaft is drawn between the lantern and the hex.");
        foreach (Transform t in trap.Island.Root.GetComponentsInChildren<Transform>(true))
            Assert.IsFalse(t.name.Contains("utline"), "No outline any more.");
        foreach (Light l in beam.parent.GetComponentsInChildren<Light>(true))
            Assert.IsFalse(l.enabled, l.name + ": no round light spilling past the hex while aiming.");

        Hex from = trap.Aim, to = from;
        for (int d = 0; d < 6 && to == from; d++)
            if (trap.Board.Contains(from.Neighbour(d))) to = from.Neighbour(d);
        Assert.IsTrue(trap.AimAt(to));
        float apart = Vector2.Distance(trap.Island.Grid.Centre(from), trap.Island.Grid.Centre(to));
        trap.Tick(0.03f);
        float left = Vector2.Distance(LitMiddle(), trap.Island.Grid.Centre(to));
        Assert.Greater(left, apart * 0.2f, "It doesn't jump straight there...");
        Assert.Less(left, apart * 0.9f, "...but is already on its way.");
        for (int i = 0; i < 8; i++) trap.Tick(0.03f);
        Assert.Less(Vector2.Distance(LitMiddle(), trap.Island.Grid.Centre(to)), apart * 0.05f, "There within a quarter of a second.");
        for (int i = 0; i < 10; i++) trap.Tick(0.03f);
        AssertLit(to, 0.05f);
    }

    // The clue letter from the alchemist's hunt comes up into the corner with the board on its map:
    // turned to match the view from the lantern, showing the aimed hex, the sunk ones and the cube.
    [Test]
    public void TheClueLetterComesUpWithTheBoardOnItsMapInStepWithTheGame()
    {
        Play();
        ClueMinigame letter = trap.Letter;
        Assert.IsNotNull(letter, "The player's clue letter is in the scene.");
        Assert.IsTrue(letter.ShowingBoard, "Up in the corner with the board on it.");
        Assert.IsNotNull(trap.LetterBoard);
        ClueMapArt.Frame frame = letter.BoardFrame;
        Assert.IsTrue(frame.Turned, "Turned round to match the view from the lantern.");
        Assert.Greater(frame.ToLetter(trap.LanternPosition).y, frame.ToLetter(trap.Island.BoardBounds.center).y,
            "The lighthouse's side of the island is at the bottom of the map, like the view.");

        // Off the middle (where the cube is drawn when he stands there), well inside the rim.
        Color32 Pixel(Hex hex, bool offMiddle = true)
        {
            Vector2 c = trap.Island.Grid.Centre(hex);
            if (offMiddle) c = Vector2.Lerp(c, trap.Island.Grid.Corner(hex, 0), 0.6f);
            Vector2 p = frame.ToLetter(new Vector3(c.x, 0f, c.y));
            return trap.LetterBoard.Pixels[ClueMapArt.Index(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y))];
        }
        bool Gold(Color32 c) => c.a > 100 && c.r > 200 && c.g > 140 && c.b < 140;
        // How much of a hex's patch of the map is gold.
        float GoldShare(Hex hex)
        {
            int inside = 0, gold = 0;
            RectInt area = ClueMapArt.MapArea;
            for (int y = area.yMin; y < area.yMax; y++)
                for (int x = area.xMin; x < area.xMax; x++)
                {
                    if (trap.LetterBoard.HexAt(x + 0.5f, y + 0.5f) != hex) continue;
                    inside++;
                    if (Gold(trap.LetterBoard.Pixels[ClueMapArt.Index(x, y)])) gold++;
                }
            return inside > 0 ? gold / (float)inside : 0f;
        }
        bool Seawater(Color32 c) => c.a > 200 && c.b > c.r + 20;
        bool Cube(Color32 c) => c.a == 255 && c.r > 150 && c.g < 80 && c.b < 80;

        Assert.Greater(GoldShare(trap.Aim), 0.25f, "The aimed hex is gold on the map.");
        Assert.IsTrue(Cube(Pixel(trap.Board.Mouse, false)), "The cube is where he stands.");
        Assert.IsNotEmpty(trap.Board.Walls);
        foreach (Hex wall in trap.Board.Walls)
            if (wall != trap.Aim)
                Assert.IsTrue(Seawater(Pixel(wall)), "The houses' hexes are filled in light blue like the sunk ones: " + wall);

        // Pressing W moves the gold hex up the map, as it moves away in the view.
        Hex before = trap.Aim;
        float beforeY = frame.ToLetter(trap.Island.Grid.Centre(before, 0f)).y;
        if (trap.MoveAim(Vector2Int.up))
        {
            trap.Tick(0.05f);
            Assert.Less(frame.ToLetter(trap.Island.Grid.Centre(trap.Aim, 0f)).y, beforeY, "W is up on the map too.");
            Assert.Greater(GoldShare(trap.Aim), 0.25f, "Now this hex is gold (even with the cube on it).");
            Assert.Less(GoldShare(before), 0.05f, "The old aim is no longer gold.");
        }

        Hex target = OpenAwayFromMouse();
        Assert.IsTrue(trap.AimAt(target));
        Assert.IsTrue(trap.TryStrike());
        RunUntil(LighthouseMouseTrap.Phase.Aiming);
        Hex elsewhere = default;
        foreach (Hex cell in trap.Board.Cells)
            if (trap.Board.CanStrike(cell) && Hex.Distance(cell, target) > 2) { elsewhere = cell; break; }
        Assert.IsTrue(trap.AimAt(elsewhere));
        trap.Tick(0.05f);
        Assert.IsTrue(Seawater(Pixel(target)), "A sunk hex is sea on the map.");
        Assert.IsTrue(Cube(Pixel(trap.Board.Mouse, false)), "The cube moved with him.");

        trap.Board.PlaceMouse(trap.Board.Mouse);
        Hex shore = default;
        foreach (Hex cell in trap.Board.Cells)
            if (trap.Board.IsOpen(cell) && trap.Board.IsShore(cell) && cell != trap.Aim) { shore = cell; break; }
        trap.Board.PlaceMouse(shore);
        Assert.IsTrue(trap.AimAt(OpenAwayFromMouse()));
        Assert.IsTrue(trap.TryStrike());
        RunUntil(LighthouseMouseTrap.Phase.Ending);
        Assert.IsFalse(letter.ShowingBoard, "Put away for the ending.");
    }

    // After a strike the aim stays on the struck hex, and the beam keeps lighting that hex (now
    // water) rather than following it down, which made it land on the hex in front.
    [Test]
    public void OnAStruckHexTheBeamKeepsLightingThatHexNotTheOneInFront()
    {
        Play();
        Hex target = OpenAwayFromMouse();
        Assert.IsTrue(trap.AimAt(target));
        for (int i = 0; i < 10; i++) trap.Tick(0.1f);
        Vector3 top = trap.Island.Top(target);
        Assert.IsTrue(trap.TryStrike());
        RunUntil(LighthouseMouseTrap.Phase.Aiming);
        for (int i = 0; i < 40; i++) trap.Tick(0.1f);
        Assert.AreEqual(target, trap.Aim);
        Assert.AreEqual(1f, trap.Island.Sunk(target), 1e-3f);
        Assert.AreEqual(top, trap.Island.Top(target), "Aimed at where its top was.");
        AssertLit(target, 0.01f);
        foreach (Vector3 corner in trap.LitHex)
            Assert.GreaterOrEqual(corner.y, trap.Island.SeaLevel - 0.01f, "Lit on the water, not down in the sea.");
    }

    // Part-hexes along the shore are on the board too: they can be aimed at and struck, and the
    // villain can stand on their land.
    [Test]
    public void PartHexesAlongTheShoreCanBeSelected()
    {
        Play();
        int parts = 0;
        Hex part = default;
        foreach (Hex cell in trap.Board.Cells)
        {
            Vector2 middle = trap.Island.Grid.Centre(cell);
            if (trap.Island.GroundHeight(middle.x, middle.y) > trap.Island.SeaLevel + 0.3f) continue;
            parts++;
            if (trap.Board.CanStrike(cell)) part = cell;
            Assert.IsTrue(trap.Board.IsShore(cell), "A part-hex is on the shore.");
            Vector2 stand = trap.Island.Stand(cell);
            Assert.Greater(trap.Island.GroundHeight(stand.x, stand.y), trap.Island.SeaLevel + 0.3f, "Its stand point is on its land.");
            Assert.AreEqual(cell, trap.Island.Grid.At(stand.x, stand.y), "And inside it.");
        }
        Assert.Greater(parts, 10, "The shore's part-hexes, whose middles are in the sea, are on the board.");
        trap.Board.PlaceMouse(part);
        trap.Tick(0.1f);
        Assert.Greater(trap.Mouse.position.y - LighthouseMouseTrap.MouseSize * 0.5f, trap.Island.SeaLevel, "He can stand on one.");
        Hex other = default;
        foreach (Hex cell in trap.Board.Cells)
            if (cell != part && trap.Board.CanStrike(cell) && trap.Board.IsShore(cell))
            {
                Vector2 m = trap.Island.Grid.Centre(cell);
                if (trap.Island.GroundHeight(m.x, m.y) <= trap.Island.SeaLevel + 0.3f) { other = cell; break; }
            }
        Assert.IsTrue(trap.AimAt(other));
        Assert.IsTrue(trap.TryStrike(), "A part-hex can be struck.");
    }

    [Test]
    public void FencesTheVillainRunsThroughFallFlatAwayFromHim()
    {
        Play();
        Transform fence = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "fence9") fence = t;
        Assert.IsNotNull(fence);
        Transform picket = null;
        foreach (Transform t in fence.GetComponentsInChildren<Transform>(true))
            if (t.name == "Fence Picket") { picket = t; break; }
        Assert.IsNotNull(picket);
        Bounds b = picket.GetComponent<Renderer>().bounds;
        Assert.Greater(b.size.y, b.size.z * 2f, "Standing up to begin with.");
        // fence9 runs east-west at z = -72.9: a step from south to north crosses it.
        var from = new Vector3(b.center.x, 0f, b.center.z - 8f);
        var to = new Vector3(b.center.x, 0f, b.center.z + 8f);
        Assert.Greater(trap.Island.KnockDown(from, to), 0);
        for (int i = 0; i < 10; i++) trap.Tick(0.1f);
        Bounds fallen = picket.GetComponent<Renderer>().bounds;
        Assert.Less(fallen.size.y, b.size.y * 0.5f, "The picket lies flat.");
        Assert.Less(fallen.center.y, b.center.y - 1f);
        Assert.Greater(fallen.center.z, b.center.z + 1f, "It fell north, the way he ran.");
        Assert.AreEqual(0, trap.Island.KnockDown(from, to), "It only falls once.");
    }

    [Test]
    public void AConeOfViewOpensWhenSomethingStandsBetweenTheLanternAndTheVillain()
    {
        Play();
        bool foundHidden = false, foundOpen = false;
        foreach (Hex cell in trap.Board.Cells)
        {
            if (!trap.Board.IsOpen(cell)) continue;
            trap.Board.PlaceMouse(cell);
            for (int i = 0; i < 4; i++) trap.Tick(0.1f);
            if (trap.Sightline.Blocked && !foundHidden)
            {
                foundHidden = true;
                Assert.AreEqual(1f, trap.Sightline.Opacity, 1e-3f);
                Assert.Greater(trap.Sightline.Cleared, 0, "What is in the way is cleared from the cone.");
                Assert.IsTrue(trap.Sightline.View.enabled);
                Assert.IsFalse((trap.Sightline.View.cullingMask & (1 << MouseTrapSightline.HiddenLayer)) != 0);
                Assert.IsTrue((view.cullingMask & (1 << MouseTrapSightline.HiddenLayer)) != 0, "The player's own view still draws it.");
            }
            if (!trap.Sightline.Blocked && foundHidden && !foundOpen)
            {
                for (int i = 0; i < 4; i++) trap.Tick(0.1f);
                if (trap.Sightline.Blocked) continue;
                foundOpen = true;
                Assert.AreEqual(0f, trap.Sightline.Opacity, "In plain view, no cone.");
                Assert.IsFalse(trap.Sightline.View.enabled);
                Assert.AreEqual(0, trap.Sightline.Cleared);
            }
            if (foundHidden && foundOpen) break;
        }
        Assert.IsTrue(foundHidden, "Some hex lies behind a house or tree from the lantern.");
        Assert.IsTrue(foundOpen, "And some in plain view.");
    }

    [Test]
    public void ReachingTheShoreHeEscapesOntoTheWaterAndTheEndingSaysTheVillainGotAway()
    {
        Play();
        Hex shore = default;
        foreach (Hex cell in trap.Board.Cells)
            if (trap.Board.IsOpen(cell) && trap.Board.IsShore(cell)) { shore = cell; break; }
        trap.Board.PlaceMouse(shore);
        Assert.IsTrue(trap.AimAt(OpenAwayFromMouse()));
        Assert.IsTrue(trap.TryStrike());
        RunUntil(LighthouseMouseTrap.Phase.Escaped);
        for (int i = 0; i < 15; i++) trap.Tick(0.1f);
        Assert.AreEqual(trap.Island.SeaLevel, trap.Mouse.position.y, 1.5f, "Out on the water.");
        Assert.IsFalse(trap.Board.Contains(trap.Island.Grid.At(trap.Mouse.position.x, trap.Mouse.position.z)), "Off the island.");
        RunUntil(LighthouseMouseTrap.Phase.Ending);
        Assert.AreEqual(LighthouseMouseTrap.LoseText, trap.Ending);
        Assert.AreEqual("You solved the mystery, but the villain got away...", trap.Ending);
        trap.Tick(1.5f);
        Assert.Greater(trap.Fade, 0f);
        Assert.Less(trap.Fade, 1f, "The fade to black is slow.");
        RunUntil(LighthouseMouseTrap.Phase.Home);
    }

    [Test]
    public void ShutInHeIsCaughtAndTheEndingSaysTheVillainWasCaptured()
    {
        Play();
        Hex mouse = trap.Board.Mouse;
        var open = new List<Hex>();
        for (int d = 0; d < 6; d++)
            if (trap.Board.IsOpen(mouse.Neighbour(d))) open.Add(mouse.Neighbour(d));
        Assert.IsNotEmpty(open);
        for (int i = 0; i < open.Count - 1; i++) Assert.IsTrue(trap.Board.Strike(open[i]));
        Assert.IsTrue(trap.AimAt(open[open.Count - 1]));
        Assert.IsTrue(trap.TryStrike());
        RunUntil(LighthouseMouseTrap.Phase.Caught);
        Assert.AreEqual(mouse, trap.Board.Mouse, "He has nowhere to go.");
        RunUntil(LighthouseMouseTrap.Phase.Ending);
        Assert.AreEqual("You solved the mystery and captured the villain", trap.Ending);
        RunUntil(LighthouseMouseTrap.Phase.Home);
    }

    // The ending reloads the scene: whatever the game made has to go with it. Objects marked
    // DontSave would outlive the reload and haunt the next game (they did: magenta strips over the
    // island once their scene's materials were gone).
    [Test]
    public void EverythingTheGameMakesBelongsToItsSceneSoTheEndingsReloadClearsIt()
    {
        Play();
        trap.Board.PlaceMouse(trap.Board.Mouse);
        var made = new List<GameObject> { trap.Island.Root.gameObject, trap.Mouse.gameObject, trap.AimBeam.gameObject, trap.AimedLight.gameObject, trap.Sightline.View.gameObject };
        foreach (Transform t in trap.Island.Root.GetComponentsInChildren<Transform>(true)) made.Add(t.gameObject);
        foreach (GameObject o in made)
        {
            Assert.AreEqual(scene, o.scene, o.name + " is in the game's scene.");
            Assert.AreEqual(HideFlags.None, o.hideFlags, o.name + " is not kept across scene loads.");
        }
    }

    [Test]
    public void LeavingWithoutAnEndingPutsTheIslandAndTheViewBack()
    {
        GameObject ground = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == MouseTrapIsland.GroundName) ground = t.gameObject;
        Vector3 cameraAt = view.transform.position;
        float fog = RenderSettings.fogDensity;
        Vector3 playerAt = player.transform.position;
        var drawn = new Dictionary<Renderer, bool>();
        foreach (Renderer r in player.GetComponentsInChildren<Renderer>(true)) drawn[r] = r.enabled;
        Play();
        Assert.IsFalse(ground.GetComponent<Renderer>().enabled, "The ground is drawn in pieces while the game runs.");
        trap.Abort();
        Assert.IsFalse(LighthouseMouseTrap.IsOpen);
        Assert.IsTrue(ground.GetComponent<Renderer>().enabled);
        Assert.AreEqual(cameraAt, view.transform.position);
        Assert.AreEqual(fog, RenderSettings.fogDensity);
        Assert.AreEqual(playerAt, player.transform.position);
        foreach (var pair in drawn) Assert.AreEqual(pair.Value, pair.Key.enabled, pair.Key.name + " is drawn as before.");
    }
}
