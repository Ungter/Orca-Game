using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class AlchemistEncounterTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject player;
    private GameObject door;
    private PickupItem letter;
    private Texture2D texture;
    private Sprite letterSprite;
    private AlchemistEncounter encounter;
    private InventorySystem inventory;

    [SetUp]
    public void SetUp()
    {
        player = new GameObject("Player") { tag = "Player" };
        inventory = player.AddComponent<InventorySystem>();
        typeof(InventorySystem).GetMethod("Awake", PrivateInstance).Invoke(inventory, null);
        // A 1 x 2 m door standing on the ground at (10, 0, 5).
        door = GameObject.CreatePrimitive(PrimitiveType.Cube);
        door.name = AlchemistEncounter.DoorName;
        door.transform.position = new Vector3(10f, 1f, 5f);
        door.transform.localScale = new Vector3(1f, 2f, 0.2f);
        texture = new Texture2D(4, 4);
        letterSprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0f));
        var letterObject = new GameObject("Clue Letter", typeof(SpriteRenderer));
        letterObject.GetComponent<SpriteRenderer>().sprite = letterSprite;
        letter = letterObject.AddComponent<PickupItem>();
        var serialized = new SerializedObject(letter);
        serialized.FindProperty("itemName").stringValue = AlchemistEncounter.LetterItemName;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        encounter = AlchemistEncounter.Install(door, letter);
        typeof(AlchemistEncounter).GetMethod("Awake", PrivateInstance).Invoke(encounter, null);
    }

    [TearDown]
    public void TearDown()
    {
        encounter.Cancel();
        Object.DestroyImmediate(encounter.gameObject);
        Object.DestroyImmediate(letter.gameObject);
        Object.DestroyImmediate(door);
        Object.DestroyImmediate(player);
        Object.DestroyImmediate(letterSprite);
        Object.DestroyImmediate(texture);
    }

    [Test]
    public void TheEntranceStandsAtTheFootOfTheDoorAndHoldsTheLetterOutOfTheWorld()
    {
        Vector3 entrance = encounter.transform.position;
        Assert.AreEqual(10f, entrance.x, 1e-4f);
        Assert.AreEqual(0f, entrance.y, 1e-4f);
        Assert.AreEqual(5f, entrance.z, 1e-4f);
        Assert.IsFalse(letter.gameObject.activeSelf, "The letter can't be picked up before the alchemist gives it.");
        player.transform.position = new Vector3(12f, 0f, 5f);
        Assert.AreEqual(4f, encounter.InteractDistanceSqr(), 1e-3f);
        player.transform.position = new Vector3(13.1f, 0f, 5f);
        Assert.AreEqual(float.MaxValue, encounter.InteractDistanceSqr());
    }

    [Test]
    public void TheIntroductionPlaysThenTheClueLetterGoesIntoTheBag()
    {
        encounter.Begin();
        Assert.IsTrue(AlchemistEncounter.IsOpen);
        Assert.IsTrue(Interactables.Busy);
        Assert.IsTrue(Interactables.BlocksMovement);
        foreach (string line in AlchemistEncounter.Introduction)
        {
            RunUntil(AlchemistEncounter.Phase.Waiting);
            Assert.AreEqual(line, encounter.CurrentLine);
            Assert.IsFalse(inventory.Contains(AlchemistEncounter.LetterItemName));
            Advance();
        }
        Assert.AreEqual(AlchemistEncounter.LetterGift, encounter.CurrentLine);
        Assert.IsTrue(encounter.HasGivenLetter);
        Assert.AreEqual(AlchemistEncounter.LetterItemName, inventory.GetItem(0));
        Assert.AreEqual(letterSprite, inventory.GetSprite(0));
        RunUntil(AlchemistEncounter.Phase.Waiting);
        Advance();
        Assert.AreEqual(AlchemistEncounter.Phase.LeavingRoom, encounter.CurrentPhase);
        RunUntil(AlchemistEncounter.Phase.Idle);
        Assert.IsFalse(AlchemistEncounter.IsOpen);
        Assert.IsFalse(Interactables.Busy);
        Assert.IsFalse(letter.gameObject.activeSelf);

        encounter.Begin();
        RunUntil(AlchemistEncounter.Phase.Waiting);
        Assert.AreEqual(AlchemistEncounter.Farewell, encounter.CurrentLine);
        Advance();
        RunUntil(AlchemistEncounter.Phase.Idle);
        Assert.AreEqual(1, inventory.ItemCount(), "The letter is only given once.");
    }

    [Test]
    public void AFullBagKeepsTheLetterForALaterVisitWithoutReplayingTheIntroduction()
    {
        for (int i = 0; i < inventory.SlotCount(); i++) inventory.TryAdd("Filler " + i);
        HearIntroduction();
        Assert.AreEqual(AlchemistEncounter.LetterNoRoom, encounter.CurrentLine);
        Assert.IsFalse(encounter.HasGivenLetter);
        Assert.IsFalse(inventory.Contains(AlchemistEncounter.LetterItemName));
        Advance();
        RunUntil(AlchemistEncounter.Phase.Idle);

        inventory.TryRemove("Filler 0");
        encounter.Begin();
        Assert.AreEqual(AlchemistEncounter.LetterGift, encounter.CurrentLine);
        Assert.IsTrue(encounter.HasGivenLetter);
        Assert.IsTrue(inventory.Contains(AlchemistEncounter.LetterItemName));
    }

    [Test]
    public void EscapeDuringTheIntroductionLeavesWithoutTheLetterAndEnterOnlyFinishesTypingFirst()
    {
        encounter.Begin();
        encounter.Tick(AlchemistEncounter.FadeDuration, true);
        encounter.Tick(AlchemistEncounter.RoomEnterDuration, true);
        Assert.AreEqual(AlchemistEncounter.Phase.Typing, encounter.CurrentPhase);
        Assert.AreEqual(0, encounter.VisibleCharacters);
        Advance();
        Assert.AreEqual(AlchemistEncounter.Phase.Waiting, encounter.CurrentPhase);
        Assert.AreEqual(AlchemistEncounter.Introduction[0], encounter.CurrentLine);
        encounter.Tick(0.01f, false, true);
        Assert.AreEqual(AlchemistEncounter.Phase.LeavingRoom, encounter.CurrentPhase);
        RunUntil(AlchemistEncounter.Phase.Idle);
        Assert.IsFalse(AlchemistEncounter.IsOpen);
        Assert.IsFalse(encounter.HasGivenLetter);
        Assert.IsFalse(inventory.Contains(AlchemistEncounter.LetterItemName));
        encounter.Begin();
        Assert.AreEqual(AlchemistEncounter.Introduction[0], encounter.CurrentLine);
    }

    private void HearIntroduction()
    {
        encounter.Begin();
        foreach (string _ in AlchemistEncounter.Introduction)
        {
            RunUntil(AlchemistEncounter.Phase.Waiting);
            Advance();
        }
        RunUntil(AlchemistEncounter.Phase.Waiting);
    }

    private void Advance() => encounter.Tick(0.01f, true);

    private void RunUntil(AlchemistEncounter.Phase phase)
    {
        for (int i = 0; i < 1000 && encounter.CurrentPhase != phase; i++)
            encounter.Tick(1f / 60f, false);
        Assert.AreEqual(phase, encounter.CurrentPhase);
    }
}
