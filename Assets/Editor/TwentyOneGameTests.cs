using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public class TwentyOneGameTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private Transform player;
    private CachudCallout actor;
    private TwentyOneGame game;
    private InventorySystem inventory;
    private InventorySystem previousInventory;
    private CameraFollow follow;
    private Camera testCamera;
    private bool followWasEnabled;
    private Random.State randomState;
    private readonly string[] animals = { "Orca", "Panther", "Gazelle", "Iguana" };

    [SetUp]
    public void SetUp()
    {
        randomState = Random.state;
        Random.InitState(21);
        root = new GameObject("Twenty-one test");
        player = new GameObject("Test player").transform;
        player.SetParent(root.transform);
        previousInventory = InventorySystem.instance;
        inventory = root.AddComponent<InventorySystem>();
        Call(inventory, "Awake");
        var npc = new GameObject("Al Cachud test");
        npc.transform.SetParent(root.transform);
        npc.transform.position = Vector3.up * 1000f;
        actor = npc.AddComponent<CachudCallout>();
        actor.GetComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/al_cachud.png");
        Set(actor, "<HasDice>k__BackingField", true);
        Set(actor, "player", player);
        player.position = npc.transform.position + Vector3.back * 2f;
        game = npc.AddComponent<TwentyOneGame>();
        var sprites = new Sprite[4];
        for (int i = 0; i < sprites.Length; i++)
            sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/" + animals[i] + "_tile_mono.png");
        Set(game, "tileSprites", sprites);
        var cameraObject = new GameObject("Test camera");
        cameraObject.transform.SetParent(root.transform);
        testCamera = cameraObject.AddComponent<Camera>();
        follow = cameraObject.AddComponent<CameraFollow>();
        Set(follow, "target", player);
        Call(follow, "Awake");
        Set(actor, "view", testCamera);
        followWasEnabled = follow.enabled;
    }

    [TearDown]
    public void TearDown()
    {
        game.Close();
        actor.Cancel();
        Object.DestroyImmediate(root);
        InventorySystem.instance = previousInventory;
        if (follow != null) follow.enabled = followWasEnabled;
        Random.state = randomState;
    }

    [Test]
    public void GameWaitsForDiceAndAsksPermissionBeforeAnyRoll()
    {
        Set(actor, "<HasDice>k__BackingField", false);
        Assert.IsFalse(game.Begin(actor, player, testCamera));
        Set(actor, "<HasDice>k__BackingField", true);
        Assert.IsTrue(game.Begin(actor, player, testCamera));
        Assert.AreEqual(TwentyOneGame.Phase.ChoosingOrder, game.CurrentPhase);
        Assert.IsFalse(game.Match.Started);
        Assert.IsTrue(Interactables.Busy);
        Assert.IsTrue(Interactables.BlocksMovement);
        Assert.AreEqual(0, game.SelectedOrder);
        game.Tick(10f, true);
        Assert.AreEqual(0, game.Match.Total);
        Assert.IsTrue(game.ChooseOrder(true));
        Assert.AreEqual(TwentyOneMatch.Side.Player, game.Match.Turn);
        Assert.IsFalse(game.ChooseOrder(false));
    }

    [Test]
    public void RefusingLetsThePlayerGoSecond()
    {
        Assert.IsTrue(game.Begin(actor, player, testCamera));
        Assert.IsTrue(game.ChooseOrder(false));
        Assert.AreEqual(TwentyOneMatch.Side.Opponent, game.Match.Turn);
        game.Tick(TwentyOneGame.RulesDuration);
        Assert.AreEqual(TwentyOneGame.Phase.OpponentRolling, game.CurrentPhase);
    }

    [Test]
    public void SpaceStopsTheDisplayedFaceAndCannotStealTheOpponentsTurn()
    {
        Start(true, 21);
        game.Tick(TwentyOneGame.FaceDuration);
        Assert.AreEqual(2, game.Face);
        game.Tick(TwentyOneGame.FaceDuration, true);
        Assert.AreEqual(2, game.Match.LastRoll);
        Assert.AreEqual(2, game.Match.Total);
        game.Tick(0.01f, true);
        Assert.AreEqual(2, game.Match.Total);
        Until(TwentyOneGame.Phase.OpponentRolling);
        game.Tick(0.01f, true);
        Assert.AreEqual(2, game.Match.Total);
        Until(TwentyOneGame.Phase.TakingTiles);
        Assert.AreEqual(4, game.Match.Total);
        Assert.AreEqual(2, game.Match.LastRoll);
    }

    [Test]
    public void EachDieNumberLeavesHalfASecondToReactBeforeCycling()
    {
        Start(true);
        for (int expected = 1; expected <= 3; expected++)
        {
            Assert.AreEqual(expected, game.Face);
            game.Tick(0.5f);
            Assert.AreEqual(expected, game.Face, "The number changed before the reaction window ended.");
            game.Tick(0.11f);
        }
        Assert.AreEqual(1, game.Face, "The die should cycle back to one.");
    }

    [Test]
    public void LosingGivesNoKeywordAndRetryAsksForTurnOrderAgain()
    {
        Play(false);
        Assert.AreEqual(TwentyOneGame.Phase.Lost, game.CurrentPhase);
        Assert.IsFalse(inventory.HasRevealedWord(TwentyOneGame.Keyword));
        game.Tick(0f, false, true);
        Assert.AreEqual(TwentyOneGame.Phase.ChoosingOrder, game.CurrentPhase);
        Assert.IsFalse(game.Match.Started);
        Assert.AreEqual(0, game.Match.Total);
        Assert.IsTrue(game.View.IsVisible);
    }

    [Test]
    public void EveryNewMatchDrawsItsLastTileFromTwentyToThirty()
    {
        var drawn = new HashSet<int>();
        int previous = 0;
        for (int i = 0; i < 300; i++)
        {
            Assert.IsTrue(game.Begin(actor, player, testCamera));
            Assert.AreEqual(TwentyOneGame.Phase.ChoosingOrder, game.CurrentPhase);
            Assert.AreNotEqual(previous, game.Match.Limit, "Coming back dealt the same last tile.");
            previous = game.Match.Limit;
            drawn.Add(previous);
            game.Close();
        }
        CollectionAssert.AreEquivalent(new[] { 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30 }, drawn);
    }

    [Test]
    public void ANewLastTileIsAnyOfTheOtherTenAndNeverThePreviousOne()
    {
        for (int previous = TwentyOneMatch.MinLimit; previous <= TwentyOneMatch.MaxLimit; previous++)
        {
            var drawn = new HashSet<int>();
            for (int i = 0; i < 1000; i++)
            {
                int limit = TwentyOneGame.NextLimit(previous);
                Assert.AreNotEqual(previous, limit);
                drawn.Add(limit);
            }
            Assert.That(drawn, Is.All.InRange(TwentyOneMatch.MinLimit, TwentyOneMatch.MaxLimit));
            Assert.AreEqual(10, drawn.Count, "After " + previous);
        }
        var first = new HashSet<int>();
        for (int i = 0; i < 1000; i++) first.Add(TwentyOneGame.NextLimit(0));
        Assert.AreEqual(11, first.Count, "The first match can be any last tile.");
    }

    [Test]
    public void EachRetryAfterALossDealsADifferentLastTile()
    {
        for (int retry = 0; retry < 30; retry++)
        {
            Play(false);
            Assert.AreEqual(TwentyOneGame.Phase.Lost, game.CurrentPhase);
            TwentyOneMatch lost = game.Match;
            game.Tick(0f, false, true);
            Assert.AreEqual(TwentyOneGame.Phase.ChoosingOrder, game.CurrentPhase);
            Assert.AreNotSame(lost, game.Match);
            Assert.AreNotEqual(lost.Limit, game.Match.Limit, "Try again kept the last tile.");
            Assert.That(game.Match.Limit, Is.InRange(20, 30));
            Assert.AreEqual(game.Match.Limit, game.View.Table.ShownTiles, "The table kept the previous match's tiles.");
        }
    }

    [Test]
    public void TheTopLeftRulesPanelShowsOnlyWhileHeAsksToGoSecond()
    {
        Assert.IsTrue(game.Begin(actor, player, testCamera));
        GameObject rules = ViewField<GameObject>("rulesPanel");
        Assert.IsTrue(rules.activeSelf);
        Assert.IsTrue(game.ChooseOrder(true));
        Assert.IsFalse(rules.activeSelf, "The rules panel should go once the game starts.");
        game.Tick(TwentyOneGame.RulesDuration);
        Assert.IsFalse(rules.activeSelf);
        Assert.IsTrue(ViewField<GameObject>("diePanel").activeSelf);
        game.Close();
        Play(false);
        Assert.AreEqual(TwentyOneGame.Phase.Lost, game.CurrentPhase);
        Assert.IsFalse(rules.activeSelf);
        game.Tick(0f, false, true);
        Assert.IsTrue(rules.activeSelf, "Each new round's rules show while he asks again.");
        Assert.AreEqual("COUNT 0 / " + game.Match.Limit, ViewField<PixelText>("count").text);
    }
    [Test]
    public void TheRightAnswerToCanIGoSecondDependsOnTheLastTile(
        [NUnit.Framework.Range(TwentyOneMatch.MinLimit, TwentyOneMatch.MaxLimit)] int limit)
    {
        // Yes is right exactly when whoever rolls first can force a win.
        bool yesIsRight = TwentyOneMatchTests.Solve(limit)[0];
        Play(true, limit);
        Assert.AreEqual(TwentyOneGame.Phase.Drat, game.CurrentPhase, "The right answer and rolls should win.");
        Assert.AreEqual(yesIsRight, game.OpponentGoesSecond);
        game.Close();
        Play(false, limit);
        Assert.AreEqual(TwentyOneGame.Phase.Lost, game.CurrentPhase, "The wrong answer should lose even with the best rolls.");
        Assert.AreEqual(!yesIsRight, game.OpponentGoesSecond);
        Assert.AreEqual("YOU TOOK TILE " + limit + ". YOU LOSE!", ViewField<PixelText>("normalSpeech").text);
    }

    [TestCase(20, "TWENTY")]
    [TestCase(21, "TWENTY-ONE")]
    [TestCase(22, "TWENTY-TWO")]
    [TestCase(23, "TWENTY-THREE")]
    [TestCase(24, "TWENTY-FOUR")]
    [TestCase(25, "TWENTY-FIVE")]
    [TestCase(26, "TWENTY-SIX")]
    [TestCase(27, "TWENTY-SEVEN")]
    [TestCase(28, "TWENTY-EIGHT")]
    [TestCase(29, "TWENTY-NINE")]
    [TestCase(30, "THIRTY")]
    public void TheTableShowsEveryTileUpToTheLastOneOnThePlayersSideOfTheFelt(int limit, string name)
    {
        Assert.IsTrue(game.Begin(actor, player, testCamera));
        UseLimit(limit);
        Assert.AreEqual(name, ViewField<PixelText>("title").text);
        Assert.AreEqual("TAKE 1-3 TILES.\nDON'T TAKE TILE " + limit + ".", ViewField<PixelText>("rules").text);
        Assert.AreEqual("COUNT 0 / " + limit, ViewField<PixelText>("count").text);
        TwentyOneTable table = game.View.Table;
        Transform room = table.Room;
        float half = table.TileSize * 0.5f;
        Canvas.ForceUpdateCanvases();
        Rect[] blockers =
        {
            ScreenRect(ViewField<GameObject>("diePanel").GetComponent<RectTransform>()),
            ScreenRect(ViewField<GameObject>("rulesPanel").GetComponent<RectTransform>()),
            ScreenRect(ViewField<PixelText>("controls").rectTransform)
        };
        Assert.AreEqual(limit, table.ShownTiles);
        for (int i = 0; i < limit; i++)
        {
            Vector3 local = room.InverseTransformPoint(table.Tile(i).position);
            Assert.AreEqual(TwentyOneTable.Surface, local.y, 0.001f, "Tile " + (i + 1) + " is not on the felt.");
            Assert.LessOrEqual(Mathf.Abs(local.x) + half, TwentyOneTable.FeltHalfWidth, "Tile " + (i + 1));
            Assert.Less(local.z + half, 0f, "Tile " + (i + 1) + " is not on the player's side of the table.");
            Assert.Greater(local.z - half, -TwentyOneTable.FeltHalfDepth, "Tile " + (i + 1));
            Assert.Greater(Vector3.Dot(table.TileArt(i).transform.forward, Vector3.down), 0.99f, "Tile " + (i + 1) + " is not flat.");
            Assert.AreEqual(i == limit - 1, table.FrameColor(i) == TwentyOneTable.Red, "Only the last tile is red.");
            Rect onScreen = TileOnScreen(table, i);
            Assert.IsTrue(testCamera.pixelRect.Contains(onScreen.min) && testCamera.pixelRect.Contains(onScreen.max),
                "Tile " + (i + 1) + " is off screen.");
            foreach (Rect blocker in blockers) Assert.IsFalse(onScreen.Overlaps(blocker), "Tile " + (i + 1) + " is covered.");
            for (int j = 0; j < i; j++)
            {
                Vector3 other = room.InverseTransformPoint(table.Tile(j).position);
                Assert.IsTrue(Mathf.Abs(other.x - local.x) >= half * 2f || Mathf.Abs(other.z - local.z) >= half * 2f,
                    "Tiles " + (j + 1) + " and " + (i + 1) + " overlap.");
            }
            if (i == 0) continue;
            Vector3 step = local - room.InverseTransformPoint(table.Tile(i - 1).position);
            Assert.IsTrue((Mathf.Abs(step.x) < 0.001f) != (Mathf.Abs(step.z) < 0.001f) &&
                step.magnitude < half * 2f * 1.3f, "The queue breaks before tile " + (i + 1));
        }
    }

    [Test]
    public void AlCachudSitsAcrossTheTableAndTheCameraSeesOnlyTheRoom()
    {
        Assert.IsTrue(game.Begin(actor, player, testCamera));
        TwentyOneTable table = game.View.Table;
        Transform room = table.Room;
        Assert.IsTrue(table.IsVisible);
        Assert.AreEqual(1 << TwentyOneTable.Layer, testCamera.cullingMask);
        Assert.AreEqual(CameraClearFlags.SolidColor, testCamera.clearFlags);
        Assert.AreEqual(table.SeatPosition, testCamera.transform.position);
        Assert.Less(room.InverseTransformPoint(testCamera.transform.position).z, -TwentyOneTable.FeltHalfDepth);
        SpriteRenderer opponent = table.Opponent;
        Assert.AreSame(actor.GetComponent<SpriteRenderer>().sprite, opponent.sprite);
        Vector3 seat = room.InverseTransformPoint(opponent.transform.position);
        Assert.Greater(seat.z, TwentyOneTable.FeltHalfDepth, "He should sit across the table.");
        Assert.AreEqual(0f, seat.x, 0.01f);
        Bounds body = opponent.bounds;
        Assert.Greater(body.max.y - room.position.y, TwentyOneTable.TableTop + 0.9f, "He should show above the table.");
        Vector3 head = testCamera.WorldToViewportPoint(new Vector3(body.center.x, body.max.y - body.size.y * 0.15f, body.center.z));
        Assert.That(head.x, Is.InRange(0.3f, 0.7f));
        Assert.That(head.y, Is.InRange(0.5f, 0.92f), "His face should be in the upper part of the view.");
        Assert.Greater(head.z, 0f);
        // Nothing in the room collides with the world or is lit by its lights.
        Assert.AreEqual(0, room.GetComponentsInChildren<Collider>(true).Length);
        foreach (Renderer part in room.GetComponentsInChildren<Renderer>(true))
        {
            Assert.AreEqual(TwentyOneTable.Layer, part.gameObject.layer, part.name);
            if (part is MeshRenderer) Assert.AreEqual(1u << TwentyOneTable.LightLayer, part.renderingLayerMask, part.name);
        }
        Light[] lamps = room.GetComponentsInChildren<Light>(true);
        Assert.AreEqual(1, lamps.Length);
        Assert.AreEqual(1 << TwentyOneTable.Layer, lamps[0].cullingMask);
    }

    [Test]
    public void StoppingTheDieThrowsItAcrossTheTableThenTheCylinderHopsAlongTheTakenTiles()
    {
        Start(true, 21);
        TwentyOneTable table = game.View.Table;
        Transform room = table.Room;
        Assert.IsFalse(table.Die.gameObject.activeSelf, "No die is on the table before the first roll.");
        Assert.AreEqual(PrimitiveType.Cylinder.ToString(), table.Token.GetComponent<MeshFilter>().sharedMesh.name);
        Vector3 start = room.TransformPoint(table.Spot(0));
        AssertOnSpot(table.Token, start);
        game.Tick(TwentyOneGame.FaceDuration);
        Assert.AreEqual(2, game.Face);
        Assert.IsTrue(ViewField<GameObject>("diePanel").activeSelf, "The cycling die still shows.");
        Assert.AreEqual("Counting die 2", ViewField<Image>("die").sprite.texture.name);
        game.Tick(0f, true);
        Assert.AreEqual(TwentyOneGame.Phase.Throwing, game.CurrentPhase);
        Assert.AreEqual(2, game.Match.Total);
        Assert.AreEqual(0, game.VisibleTotal);
        Assert.IsTrue(table.Die.gameObject.activeSelf);
        Vector3 thrownFrom = room.TransformPoint(TwentyOneTable.DieStart(true));
        Assert.Less(Vector3.Distance(thrownFrom, table.Die.position), 0.001f);
        Vector3 rest = room.TransformPoint(table.DieRest(game.Match.TurnCount, true));
        game.Tick(TwentyOneGame.ThrowDuration * 0.3f);
        Vector3 flying = table.Die.position;
        Quaternion spin = table.Die.rotation;
        Assert.Greater(Vector3.Distance(flying, thrownFrom), 0.1f, "The die did not leave the player's hand.");
        Assert.Greater(flying.y, rest.y + 0.05f, "The die should be in the air.");
        game.Tick(TwentyOneGame.ThrowDuration * 0.15f);
        Assert.Greater(Quaternion.Angle(spin, table.Die.rotation), 20f, "The die should tumble.");
        AssertOnSpot(table.Token, start);
        game.Tick(TwentyOneGame.ThrowDuration * 0.55f + 0.001f);
        Assert.AreEqual(TwentyOneGame.Phase.TakingTiles, game.CurrentPhase);
        Assert.Less(Vector3.Distance(rest, table.Die.position), 0.001f);
        Assert.AreEqual(2, table.DieUpFace, "The die should land on the roll.");
        Vector3 landed = room.InverseTransformPoint(table.Die.position);
        Assert.Less(Mathf.Abs(landed.x), TwentyOneTable.FeltHalfWidth);
        Assert.Greater(landed.z - TwentyOneTable.DieSize, room.InverseTransformPoint(table.Tile(0).position).z + table.TileSize * 0.5f,
            "The die should land clear of the tiles.");
        game.Tick(TwentyOneGame.TileDuration * 0.5f);
        Vector3 hop = table.Token.position;
        Assert.Greater(hop.y, start.y + 0.04f, "The cylinder should hop.");
        game.Tick(TwentyOneGame.TileDuration * 0.5f + 0.001f);
        Assert.AreEqual(1, game.VisibleTotal);
        AssertOnSpot(table.Token, table.Tile(0).position);
        game.Tick(TwentyOneGame.TileDuration);
        Assert.AreEqual(2, game.VisibleTotal);
        AssertOnSpot(table.Token, table.Tile(1).position);
        Until(TwentyOneGame.Phase.OpponentRolling);
        Until(TwentyOneGame.Phase.Throwing);
        Assert.Greater(room.InverseTransformPoint(table.Die.position).z, TwentyOneTable.FeltHalfDepth,
            "Al Cachud throws from his side.");
        Until(TwentyOneGame.Phase.TakingTiles);
        Assert.AreEqual(game.Match.LastRoll, table.DieUpFace);
        Until(TwentyOneGame.Phase.PlayerRolling);
        AssertOnSpot(table.Token, table.Tile(game.Match.Total - 1).position);
    }

    [Test]
    public void LeavingTheTableGivesTheCameraBackExactly()
    {
        testCamera.transform.SetPositionAndRotation(new Vector3(3f, 1004f, 5f), Quaternion.Euler(10f, 20f, 0f));
        testCamera.fieldOfView = 70f;
        testCamera.nearClipPlane = 0.3f;
        testCamera.cullingMask = ~(1 << 4);
        testCamera.clearFlags = CameraClearFlags.Skybox;
        testCamera.backgroundColor = Color.cyan;
        for (int visit = 0; visit < 2; visit++)
        {
            Assert.IsTrue(game.Begin(actor, player, testCamera));
            Assert.AreEqual(TwentyOneTable.FieldOfViewFor(testCamera.aspect), testCamera.fieldOfView, 0.001f);
            Assert.GreaterOrEqual(testCamera.fieldOfView, TwentyOneTable.FieldOfView);
            Play(visit == 0);
            game.Close();
            Assert.IsFalse(game.View.Table.IsVisible);
            Assert.AreEqual(new Vector3(3f, 1004f, 5f), testCamera.transform.position);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(10f, 20f, 0f), testCamera.transform.rotation), 0.01f);
            Assert.AreEqual(70f, testCamera.fieldOfView);
            Assert.AreEqual(0.3f, testCamera.nearClipPlane);
            Assert.AreEqual(~(1 << 4), testCamera.cullingMask);
            Assert.AreEqual(CameraClearFlags.Skybox, testCamera.clearFlags);
            Assert.AreEqual(Color.cyan, testCamera.backgroundColor);
        }
    }
    [Test]
    public void VictorySaysDratThenHighlightsAndRevealsJerkWithoutRemovingOtherWords()
    {
        inventory.RevealClueWords("FELL");
        Play(true);
        Assert.AreEqual(TwentyOneGame.Phase.Drat, game.CurrentPhase);
        Assert.IsTrue(game.IsSolved);
        Assert.IsFalse(inventory.HasRevealedWord(TwentyOneGame.Keyword));
        game.Tick(0f, false, true);
        Assert.AreEqual(TwentyOneGame.Phase.Drat, game.CurrentPhase, "First press only finishes typing.");
        game.Tick(0f, false, true);
        Assert.AreEqual(TwentyOneGame.Phase.Insult, game.CurrentPhase);
        game.Tick(0.1f);
        Assert.IsFalse(inventory.HasRevealedWord(TwentyOneGame.Keyword));
        game.Tick(10f);
        Assert.AreEqual("You're a JERK!", game.Speech);
        Assert.AreEqual("JERK", game.View.KeywordLabel.text);
        Assert.AreEqual((Color)new Color32(242, 211, 107, 255), game.View.KeywordLabel.color);
        Assert.IsTrue(inventory.HasRevealedWord("JERK"));
        Assert.IsTrue(inventory.HasRevealedWord("FELL"));
        Assert.AreEqual(2, inventory.RevealedWords.Count);
        game.Tick(10f);
        Assert.AreEqual(2, inventory.RevealedWords.Count);
        game.Tick(0f, false, true);
        Assert.IsFalse(TwentyOneGame.IsOpen);
        Assert.IsFalse(Interactables.BlocksMovement);
    }

    [TestCase(0.75f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void VictoryTextUsesTheSnappedFontWidthAtEachCanvasScale(float scale)
    {
        Play(true);
        game.Tick(0f, false, true);
        game.Tick(0f, false, true);
        game.Tick(10f);
        var keyword = game.View.KeywordLabel;
        var canvas = keyword.canvas;
        canvas.GetComponent<CanvasScaler>().enabled = false;
        canvas.scaleFactor = scale;
        game.Tick(0f);
        var prefix = ViewField<PixelText>("prefix");
        var suffix = ViewField<PixelText>("suffix");
        Assert.AreEqual("You're a JERK!", prefix.text + keyword.text + suffix.text);
        float pixel = prefix.PixelSize();
        Assert.AreEqual(PixelFont.Measure(prefix.text) * pixel, prefix.rectTransform.rect.width, 0.001f);
        Assert.AreEqual(PixelFont.Measure(keyword.text) * pixel, keyword.rectTransform.rect.width, 0.001f);
        Assert.AreEqual(PixelFont.Measure(suffix.text) * pixel, suffix.rectTransform.rect.width, 0.001f);
        float prefixRight = prefix.rectTransform.anchoredPosition.x + prefix.rectTransform.rect.xMax;
        float wordLeft = keyword.rectTransform.anchoredPosition.x + keyword.rectTransform.rect.xMin;
        float wordRight = keyword.rectTransform.anchoredPosition.x + keyword.rectTransform.rect.xMax;
        float suffixLeft = suffix.rectTransform.anchoredPosition.x + suffix.rectTransform.rect.xMin;
        Assert.AreEqual(PixelFont.Spacing * pixel, wordLeft - prefixRight, 0.001f);
        Assert.AreEqual(PixelFont.Spacing * pixel, suffixLeft - wordRight, 0.001f);
        Assert.AreEqual(TextAnchor.MiddleLeft, prefix.alignment);
        Assert.AreEqual(TextAnchor.MiddleLeft, keyword.alignment);
    }

    [Test]
    public void LeavingDuringVictoryKeepsTheEarnedClue()
    {
        Play(true);
        game.Close();
        Assert.IsTrue(inventory.HasRevealedWord(TwentyOneGame.Keyword));
        Assert.IsFalse(TwentyOneGame.IsOpen);
        Assert.IsFalse(game.View.IsVisible);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ClosingRestoresTheOriginalCameraEnabledState(bool enabled)
    {
        Assert.IsNotNull(follow);
        follow.enabled = enabled;
        Assert.IsTrue(game.Begin(actor, player, testCamera));
        Assert.IsFalse(follow.enabled);
        game.enabled = false;
        // EditMode fixtures do not receive the Play Mode lifecycle callback.
        Call(game, "OnDisable");
        Assert.IsFalse(TwentyOneGame.IsOpen);
        Assert.AreEqual(enabled, follow.enabled);
        Assert.IsFalse(inventory.HasRevealedWord(TwentyOneGame.Keyword));
    }

    [Test]
    public void BothOrderChoicesHaveMouseHitAreasAndPixelArtDoesNotModifyImportedTextures()
    {
        var source = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Orca_tile_mono.png");
        FilterMode original = source.texture.filterMode;
        Assert.IsTrue(game.Begin(actor, player, testCamera));
        Canvas.ForceUpdateCanvases();
        Assert.AreEqual(0, game.View.OrderChoiceAt(game.View.OrderChoiceCentre(0)));
        Assert.AreEqual(1, game.View.OrderChoiceAt(game.View.OrderChoiceCentre(1)));
        Assert.AreEqual(-1, game.View.OrderChoiceAt(new Vector2(-10f, -10f)));
        Assert.AreEqual(original, source.texture.filterMode);
        Assert.IsNotNull(game.View.Table.Tile(TwentyOneMatch.MaxLimit - 1));
        Texture tile = game.View.Table.TileArt(0).sharedMaterial.mainTexture;
        Assert.AreEqual(FilterMode.Point, tile.filterMode);
        Assert.AreNotSame(source.texture, tile);
    }

    [Test]
    public void ExistingDiceReturnConversationStartsTheConfiguredGameAfterCameraHandoff()
    {
        Set(actor, "diceGame", game);
        Assert.IsTrue(actor.TryTalk());
        actor.Tick(CachudCallout.PanDuration);
        Assert.AreEqual(CachudCallout.Ready, actor.CurrentLine);
        actor.Tick(0f, true);
        actor.Tick(0f, true);
        Assert.IsFalse(TwentyOneGame.IsOpen);
        actor.Tick(CachudCallout.ReturnDuration);
        Assert.IsFalse(CachudCallout.IsOpen);
        Assert.IsTrue(TwentyOneGame.IsOpen);
        Assert.AreEqual(TwentyOneGame.Phase.ChoosingOrder, game.CurrentPhase);
    }

    [Test]
    public void SceneConnectsTheReturnedDiceAndAllFourSuppliedAnimalTiles()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            TwentyOneGame found = null;
            CachudCallout npc = null;
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                if (sceneRoot.TryGetComponent(out CachudCallout candidate))
                {
                    npc = candidate;
                    found = sceneRoot.GetComponent<TwentyOneGame>();
                }
            Assert.IsNotNull(found);
            Assert.AreSame(found, new SerializedObject(npc).FindProperty("diceGame").objectReferenceValue);
            var art = new SerializedObject(found).FindProperty("tileSprites");
            Assert.AreEqual(4, art.arraySize);
            for (int i = 0; i < animals.Length; i++)
                Assert.AreEqual("Assets/Sprites/" + animals[i] + "_tile_mono.png",
                    AssetDatabase.GetAssetPath(art.GetArrayElementAtIndex(i).objectReferenceValue));
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    private void Start(bool opponentSecond, int limit = 0)
    {
        Assert.IsTrue(game.Begin(actor, player, testCamera));
        if (limit != 0) UseLimit(limit);
        Assert.IsTrue(game.ChooseOrder(opponentSecond));
        game.Tick(TwentyOneGame.RulesDuration);
    }

    // Answers the order question rightly or wrongly for the drawn last tile, then always stops
    // the die on a winning roll when there is one.
    private void Play(bool playerWins, int limit = 0)
    {
        if (!TwentyOneGame.IsOpen) Assert.IsTrue(game.Begin(actor, player, testCamera));
        if (limit != 0) UseLimit(limit);
        bool[] wins = TwentyOneMatchTests.Solve(game.Match.Limit);
        Assert.IsTrue(game.ChooseOrder(playerWins == wins[0]));
        game.Tick(TwentyOneGame.RulesDuration);
        for (int i = 0; i < 5000; i++)
        {
            if (game.CurrentPhase == TwentyOneGame.Phase.Lost || game.CurrentPhase == TwentyOneGame.Phase.Drat) return;
            int best = TwentyOneMatchTests.WinningRoll(wins, game.Match.Total);
            int desired = best > 0 ? best : 1;
            bool stop = game.CurrentPhase == TwentyOneGame.Phase.PlayerRolling && game.Face == desired;
            game.Tick(stop ? 0f : 0.04f, stop);
        }
        Assert.Fail("The game did not end.");
    }

    // Replaces the drawn last tile while the order question is still up.
    private void UseLimit(int limit)
    {
        Assert.AreEqual(TwentyOneGame.Phase.ChoosingOrder, game.CurrentPhase);
        Set(game, "<Match>k__BackingField", new TwentyOneMatch(limit));
        game.Tick(0f);
    }

    private void Until(TwentyOneGame.Phase phase)
    {
        for (int i = 0; i < 200 && game.CurrentPhase != phase; i++) game.Tick(0.04f);
        Assert.AreEqual(phase, game.CurrentPhase);
    }

    private static void AssertOnSpot(Transform token, Vector3 spot)
    {
        Vector3 offset = token.position - spot;
        Assert.Less(new Vector2(offset.x, offset.z).magnitude, 0.01f, "The cylinder is not on its spot.");
        Assert.That(offset.y, Is.InRange(0f, 0.07f), "The cylinder should stand on the table.");
    }

    // A tile's outline on screen, from its four corners.
    private Rect TileOnScreen(TwentyOneTable table, int index)
    {
        Transform tile = table.Tile(index);
        float half = table.TileSize * 0.5f;
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        for (int x = -1; x <= 1; x += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 point = testCamera.WorldToScreenPoint(tile.position + new Vector3(x * half, 0f, z * half));
            Assert.Greater(point.z, 0f);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    // Overlay canvas corners are already in screen pixels.
    private static Rect ScreenRect(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    private T ViewField<T>(string name) => (T)typeof(TwentyOneView).GetField(name, Private).GetValue(game.View);

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Private).SetValue(target, value);

    private static void Call(object target, string name) =>
        target.GetType().GetMethod(name, Private).Invoke(target, null);
}
