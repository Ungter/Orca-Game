using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class CachudCalloutTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private CachudCallout callout;
    private Transform player;
    private Camera view;
    private CameraFollow follow;
    private InventorySystem inventory;
    private InventorySystem previousInventory;
    private Vector3 originalPosition;
    private Quaternion originalRotation;

    // No map here, so there is no door: knocking starts the conversation where he stands.
    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Cachud test");
        var actor = new GameObject("Actor");
        actor.transform.SetParent(root.transform);
        callout = actor.AddComponent<CachudCallout>();
        actor.transform.position = Vector3.up * 1000f;
        player = new GameObject("Test player").transform;
        player.SetParent(root.transform);
        player.position = callout.transform.position + Vector3.forward * 2f;
        previousInventory = InventorySystem.instance;
        inventory = player.gameObject.AddComponent<InventorySystem>();
        Call(inventory, "Awake");
        var cameraObject = new GameObject("Test camera");
        cameraObject.transform.SetParent(root.transform);
        view = cameraObject.AddComponent<Camera>();
        follow = cameraObject.AddComponent<CameraFollow>();
        Set(follow, "target", player);
        Call(follow, "Awake");
        Call(follow, "LateUpdate");
        originalPosition = view.transform.position;
        originalRotation = view.transform.rotation;
        Set(callout, "player", player);
        Set(callout, "view", view);
    }

    [TearDown]
    public void TearDown()
    {
        callout.Cancel();
        Object.DestroyImmediate(root);
        InventorySystem.instance = previousInventory;
    }

    [Test]
    public void KnockingNeedsThePlayerCloseByAndOnTheSameLevel()
    {
        player.position = callout.transform.position + Vector3.forward * 3.1f;
        Assert.IsFalse(callout.TryKnock());
        player.position = callout.transform.position + Vector3.up * 4f;
        Assert.IsFalse(callout.TryKnock());
        player.position = callout.transform.position + Vector3.forward * 2.9f;
        Assert.IsTrue(callout.TryKnock());
    }

    [Test]
    public void AnotherConversationDefersTheMeeting()
    {
        var talk = root.AddComponent<TalkInteractable>();
        try
        {
            talk.Begin();
            Assert.IsFalse(callout.TryKnock());
        }
        finally { talk.Cancel(); }
        Assert.IsTrue(callout.TryKnock());
    }

    [Test]
    public void MovementAndInteractionsStayLockedUntilCameraHasReturned()
    {
        Assert.IsTrue(callout.TryKnock());
        Assert.IsTrue(Interactables.Busy);
        Assert.IsTrue(Interactables.BlocksMovement);
        Assert.IsFalse(follow.enabled);
        callout.Tick(CachudCallout.PanDuration);
        Call(callout, "LateUpdate");
        Assert.AreEqual(CachudCallout.Phase.Typing, callout.CurrentPhase);
        Assert.AreEqual(1f, callout.BarsAmount);
        Assert.IsTrue(Interactables.BlocksMovement);
        FinishMissingDiceConversation();
        Assert.AreEqual(CachudCallout.Phase.Returning, callout.CurrentPhase);
        Assert.IsTrue(follow.enabled);
        Assert.IsTrue(Interactables.BlocksMovement);
        callout.Tick(CachudCallout.ReturnDuration * 0.5f);
        Assert.IsTrue(Interactables.BlocksMovement);
        Assert.AreEqual(0.5f, callout.BarsAmount, 0.001f);
        callout.Tick(CachudCallout.ReturnDuration);
        Assert.AreEqual(CachudCallout.Phase.Complete, callout.CurrentPhase);
        Assert.IsFalse(CachudCallout.IsOpen);
        Assert.IsFalse(Interactables.Busy);
        Assert.IsFalse(Interactables.BlocksMovement);
        Assert.IsTrue(callout.HasComeOut);
        Assert.IsFalse(callout.TryKnock(), "He only comes out once.");
    }

    [Test]
    public void PanStartsContinuouslyAndFramesTheCharacter()
    {
        Assert.IsTrue(callout.TryKnock());
        Call(callout, "LateUpdate");
        Assert.Less(Vector3.Distance(originalPosition, view.transform.position), 0.001f);
        Assert.Less(Quaternion.Angle(originalRotation, view.transform.rotation), 0.001f);
        callout.Tick(CachudCallout.PanDuration);
        Call(callout, "LateUpdate");
        Vector3 head = view.WorldToViewportPoint(callout.transform.position + Vector3.up * 2f);
        Vector3 feet = view.WorldToViewportPoint(callout.transform.position);
        Assert.Greater(head.z, 0f);
        Assert.That(head.x, Is.InRange(0.2f, 0.8f));
        Assert.That(head.y, Is.InRange(0.15f, 0.85f));
        Assert.That(feet.y, Is.InRange(0.15f, 0.85f));
    }

    [Test]
    public void CloseConversationKeepsTheSpeakerInFrontOfThePlayer()
    {
        Assert.IsTrue(callout.TryTalk());
        callout.Tick(CachudCallout.PanDuration);
        Call(callout, "LateUpdate");
        Vector3 speaker = view.WorldToViewportPoint(callout.transform.position + Vector3.up * 1.1f);
        Vector3 visitor = view.WorldToViewportPoint(player.position + Vector3.up * 1.1f);
        Assert.Greater(visitor.z, speaker.z + 0.2f, "The nearby player's sprite must not hide the speaker.");
        Assert.That(speaker.x, Is.InRange(0.2f, 0.8f));
        Assert.That(speaker.y, Is.InRange(0.2f, 0.8f));
    }

    [Test]
    public void ReturnBlendsToTheLiveFollowViewWithoutASnap()
    {
        Assert.IsTrue(callout.TryKnock());
        callout.Tick(CachudCallout.PanDuration);
        Call(callout, "LateUpdate");
        Vector3 shot = view.transform.position;
        FinishMissingDiceConversation();
        Call(follow, "LateUpdate");
        Call(callout, "LateUpdate");
        Assert.Less(Vector3.Distance(shot, view.transform.position), 0.001f);
        callout.Tick(CachudCallout.ReturnDuration * 0.999f);
        Call(follow, "LateUpdate");
        Vector3 destination = view.transform.position;
        Call(callout, "LateUpdate");
        Assert.Less(Vector3.Distance(destination, view.transform.position), 0.001f);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void CancellationRestoresTheOriginalCameraAndEnabledState(bool wasEnabled)
    {
        follow.enabled = wasEnabled;
        Assert.IsTrue(callout.TryKnock());
        callout.Tick(CachudCallout.PanDuration);
        Call(callout, "LateUpdate");
        callout.Cancel();
        Assert.AreEqual(wasEnabled, follow.enabled);
        Assert.IsFalse(Interactables.BlocksMovement);
        Assert.Less(Vector3.Distance(originalPosition, view.transform.position), 0.001f);
        Assert.Less(Quaternion.Angle(originalRotation, view.transform.rotation), 0.001f);
    }

    [Test]
    public void SceneCharacterUsesRequestedArtworkAndWaitsIndoorsUntilTheDoorIsKnockedOn()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            CachudCallout actor = null;
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                if (sceneRoot.TryGetComponent(out CachudCallout found)) actor = found;
            Assert.IsNotNull(actor);
            SpriteRenderer sprite = actor.GetComponent<SpriteRenderer>();
            Assert.AreEqual("Assets/Sprites/al_cachud.png", AssetDatabase.GetAssetPath(sprite.sprite));
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Sprites/al_cachud.png");
            Assert.AreEqual(FilterMode.Point, importer.filterMode);
            Assert.IsTrue(importer.alphaIsTransparency);
            Call(actor, "Start");
            Assert.IsFalse(sprite.enabled, "He keeps out of sight until the player knocks.");
            foreach (Collider body in actor.GetComponentsInChildren<Collider>(true))
                Assert.IsFalse(body.enabled, "Nor is he in the way while he is indoors.");
            Assert.IsFalse(actor.HasComeOut);
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    [Test]
    public void GreetingAndMissingDiceLeadToExactlyOneQuestionAndTheFountainClue()
    {
        int games = 0;
        callout.GameRequested += () => games++;
        Assert.IsTrue(callout.TryKnock());
        callout.Tick(CachudCallout.PanDuration);
        Assert.AreEqual(CachudCallout.Greeting, callout.CurrentLine);
        Assert.AreEqual(0, callout.VisibleCharacters);
        ReadAndAdvance();
        Assert.AreEqual(CachudCallout.MissingDice, callout.CurrentLine);
        ReadAndAdvance();
        Assert.IsTrue(callout.HasRequestedDice);
        Assert.AreEqual(CachudCallout.Phase.Questions, callout.CurrentPhase);
        CollectionAssert.AreEqual(new[] { "Where were you last?" }, CachudCallout.QuestionOptions);
        Assert.IsFalse(callout.ChooseQuestion(-1));
        Assert.IsFalse(callout.ChooseQuestion(1));
        Assert.IsTrue(callout.ChooseQuestion(0));
        Assert.AreEqual("At the fountain, throwing coins.", callout.CurrentLine);
        Assert.IsTrue(callout.HasLearnedLocation);
        ReadAndAdvance();
        callout.Tick(CachudCallout.ReturnDuration);
        Assert.IsFalse(callout.IsReadyForGame);
        Assert.AreEqual(0, games);
        Assert.IsFalse(Interactables.Busy);
    }

    [Test]
    public void FirstPressFinishesTypingWithoutSkippingTheGameInvitation()
    {
        Assert.IsTrue(callout.TryKnock());
        callout.Tick(CachudCallout.PanDuration, true);
        Assert.AreEqual(CachudCallout.Phase.Typing, callout.CurrentPhase);
        Assert.AreEqual(0, callout.VisibleCharacters);
        callout.Tick(0f, true);
        Assert.AreEqual(CachudCallout.Phase.Waiting, callout.CurrentPhase);
        Assert.AreEqual(CachudCallout.Greeting, callout.CurrentLine);
        callout.Tick(0f, true);
        Assert.AreEqual(CachudCallout.MissingDice, callout.CurrentLine);
    }

    [Test]
    public void LeavingTheQuestionListAllowsTalkingAgainWithoutAnotherEntrance()
    {
        BeginAndReadRequest();
        callout.Tick(0f, false, false, true);
        callout.Tick(CachudCallout.ReturnDuration);
        Assert.IsFalse(callout.HasLearnedLocation);
        Assert.IsFalse(callout.TryKnock());
        player.position = callout.transform.position + Vector3.forward * 8f;
        Assert.IsFalse(callout.TryTalk(), "A remote E must not start the conversation.");
        player.position = callout.transform.position + Vector3.forward * 2f;
        Assert.IsTrue(callout.TryTalk());
        callout.Tick(CachudCallout.PanDuration);
        Assert.AreEqual(CachudCallout.MissingDice, callout.CurrentLine);
        ReadAndAdvance();
        Assert.IsTrue(callout.ChooseQuestion(0));
        Assert.AreEqual(CachudCallout.FountainClue, callout.CurrentLine);
    }

    [Test]
    public void ReturningTheDiceConsumesOnlyTheDiceAndRequestsTheFutureGameAfterCameraReturn()
    {
        VisitWithoutDice();
        inventory.TryAdd("Other clue");
        inventory.TryAdd(CachudCallout.DiceItemName, AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Dice.png"));
        inventory.TryAdd("Another clue");
        int games = 0;
        callout.GameRequested += () =>
        {
            games++;
            Assert.IsFalse(CachudCallout.IsOpen);
            Assert.IsTrue(follow.enabled);
        };
        player.position = callout.transform.position + Vector3.forward * 2f;
        Assert.IsTrue(callout.TryTalk());
        callout.Tick(CachudCallout.PanDuration);
        Assert.AreEqual(CachudCallout.Thanks, callout.CurrentLine);
        Assert.IsTrue(inventory.Contains(CachudCallout.DiceItemName));
        ReadAndAdvance();
        Assert.IsFalse(inventory.Contains(CachudCallout.DiceItemName));
        Assert.AreEqual(2, inventory.ItemCount());
        Assert.AreEqual("Other clue", inventory.GetItem(0));
        Assert.AreEqual("Another clue", inventory.GetItem(1));
        Assert.IsTrue(callout.IsReadyForGame);
        Assert.AreEqual(CachudCallout.Ready, callout.CurrentLine);
        ReadAndAdvance();
        Assert.AreEqual(0, games);
        callout.Tick(CachudCallout.ReturnDuration * 0.5f);
        Assert.AreEqual(0, games);
        callout.Tick(CachudCallout.ReturnDuration);
        Assert.AreEqual(1, games);
        callout.Tick(10f);
        Assert.AreEqual(1, games, "The future game must be requested once, not every frame.");
    }

    [Test]
    public void DiceHandInUsesTheSharedInventoryEvenWhenItIsNotOnThePlayer()
    {
        Object.DestroyImmediate(inventory);
        var bag = new GameObject("Central inventory");
        bag.transform.SetParent(root.transform);
        inventory = bag.AddComponent<InventorySystem>();
        Call(inventory, "Awake");
        Assert.IsNull(player.GetComponent<InventorySystem>());
        inventory.TryAdd(CachudCallout.DiceItemName);
        Assert.IsTrue(callout.TryKnock());
        callout.Tick(CachudCallout.PanDuration);
        Assert.AreEqual(CachudCallout.Thanks, callout.CurrentLine);
        ReadAndAdvance();
        Assert.IsTrue(callout.IsReadyForGame);
        Assert.IsFalse(inventory.Contains(CachudCallout.DiceItemName));
    }

    [Test]
    public void FindingDiceBeforeTheFirstMeetingStillWorks()
    {
        inventory.TryAdd(CachudCallout.DiceItemName);
        Assert.IsTrue(callout.TryKnock());
        callout.Tick(CachudCallout.PanDuration);
        Assert.AreEqual(CachudCallout.Thanks, callout.CurrentLine);
        ReadAndAdvance();
        Assert.IsTrue(callout.HasDice);
        Assert.AreEqual(CachudCallout.Ready, callout.CurrentLine);
    }

    [Test]
    public void EscapingBeforeAcknowledgingTheHandInKeepsDiceInInventory()
    {
        inventory.TryAdd(CachudCallout.DiceItemName);
        Assert.IsTrue(callout.TryKnock());
        callout.Tick(CachudCallout.PanDuration);
        callout.Tick(0f, false, false, true);
        callout.Tick(CachudCallout.ReturnDuration);
        Assert.IsTrue(inventory.Contains(CachudCallout.DiceItemName));
        Assert.IsFalse(callout.IsReadyForGame);
    }

    [Test]
    public void CancellingTheGameHandoffKeepsReturnedDiceAndAllowsALaterRequest()
    {
        inventory.TryAdd(CachudCallout.DiceItemName);
        int games = 0;
        callout.GameRequested += () => games++;
        Assert.IsTrue(callout.TryKnock());
        callout.Tick(CachudCallout.PanDuration);
        ReadAndAdvance();
        ReadAndAdvance();
        callout.Cancel();
        Assert.IsTrue(callout.IsReadyForGame);
        Assert.AreEqual(0, games);
        player.position = callout.transform.position + Vector3.forward * 2f;
        Assert.IsTrue(callout.TryTalk());
        callout.Tick(CachudCallout.PanDuration);
        Assert.AreEqual(CachudCallout.Ready, callout.CurrentLine);
        ReadAndAdvance();
        callout.Tick(CachudCallout.ReturnDuration);
        Assert.AreEqual(1, games);
        Assert.AreEqual(0, inventory.ItemCount(), "No duplicate dice or extra hand-in.");
    }

    [Test]
    public void FullInventoryDoesNotPretendTheDiceWereCollected()
    {
        for (int i = 0; i < inventory.SlotCount(); i++) Assert.IsTrue(inventory.TryAdd("Clue " + i));
        Assert.IsFalse(inventory.TryAdd(CachudCallout.DiceItemName));
        BeginAndReadRequest();
        Assert.IsFalse(callout.IsReadyForGame);
        Assert.AreEqual(CachudCallout.Phase.Questions, callout.CurrentPhase);
    }

    [Test]
    public void SceneDiceUseTransparentPixelArtAndSitInsideTheFountainWithinReachOfTheRim()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            PickupItem dice = null;
            FountainWater water = null;
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                if (sceneRoot.name == "Lost Dice") dice = sceneRoot.GetComponent<PickupItem>();
                FountainWater found = sceneRoot.GetComponentInChildren<FountainWater>();
                if (found != null) water = found;
            }
            Assert.IsNotNull(dice);
            Assert.IsNotNull(water);
            var data = new SerializedObject(dice);
            Assert.AreEqual(CachudCallout.DiceItemName, data.FindProperty("itemName").stringValue);
            Assert.AreEqual("Assets/Sprites/Dice.png", AssetDatabase.GetAssetPath(dice.GetComponent<SpriteRenderer>().sprite));
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Sprites/Dice.png");
            Assert.AreEqual(FilterMode.Point, importer.filterMode);
            Assert.IsTrue(importer.alphaIsTransparency);
            Assert.IsFalse(importer.mipmapEnabled);
            var waterData = new SerializedObject(water);
            float scale = water.transform.lossyScale.y;
            float waterY = water.transform.position.y - waterData.FindProperty("drop").floatValue * scale;
            float wall = waterData.FindProperty("wallRadius").floatValue * scale;
            float column = waterData.FindProperty("columnRadius").floatValue * scale;
            Vector3 offset = dice.transform.position - water.transform.position;
            offset.y = 0f;
            Assert.That(offset.magnitude, Is.InRange(column + 0.5f, wall - 0.2f));
            Assert.Greater(dice.transform.position.y - 0.5f - data.FindProperty("bobHeight").floatValue, waterY);
            Assert.Greater(data.FindProperty("pickupRadius").floatValue, wall + 1.5f - offset.magnitude);
            Assert.IsTrue(data.FindProperty("glow").boolValue);
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    private void BeginAndReadRequest()
    {
        Assert.IsTrue(callout.TryKnock());
        callout.Tick(CachudCallout.PanDuration);
        ReadAndAdvance();
        ReadAndAdvance();
        Assert.AreEqual(CachudCallout.Phase.Questions, callout.CurrentPhase);
    }

    private void VisitWithoutDice()
    {
        BeginAndReadRequest();
        Assert.IsTrue(callout.ChooseQuestion(0));
        ReadAndAdvance();
        callout.Tick(CachudCallout.ReturnDuration);
    }

    private void FinishMissingDiceConversation()
    {
        ReadAndAdvance();
        ReadAndAdvance();
        Assert.IsTrue(callout.ChooseQuestion(0));
        ReadAndAdvance();
    }

    private void ReadAndAdvance()
    {
        if (callout.CurrentPhase == CachudCallout.Phase.Typing) callout.Tick(0f, true);
        Assert.AreEqual(CachudCallout.Phase.Waiting, callout.CurrentPhase);
        callout.Tick(0f, true);
    }

    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, PrivateInstance).SetValue(target, value);

    private static void Call(object target, string method) =>
        target.GetType().GetMethod(method, PrivateInstance).Invoke(target, null);
}
