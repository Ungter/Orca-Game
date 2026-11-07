using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class PlayerLanternTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<Object> created = new List<Object>();
    private GameObject player;
    private InventorySystem inventory;
    private PlayerLantern lantern;

    [SetUp]
    public void SetUp()
    {
        player = new GameObject("Lantern test player");
        inventory = player.AddComponent<InventorySystem>();
        Call(inventory, "Awake");
        lantern = player.AddComponent<PlayerLantern>();
        Call(lantern, "Awake");
        Call(lantern, "OnEnable");
    }

    [TearDown]
    public void TearDown()
    {
        Call(lantern, "OnDisable");
        Call(lantern, "OnDestroy");
        Object.DestroyImmediate(player);
        foreach (Object item in created) Object.DestroyImmediate(item);
    }

    [Test]
    public void OnlyALanternInTheBagCanBeHeldAndHoldingItLightsTheWay()
    {
        Assert.AreEqual(lantern, PlayerLantern.Current);
        Assert.IsFalse(lantern.Toggle());
        Assert.IsFalse(lantern.Equipped);
        Assert.IsNull(lantern.Glow);

        inventory.TryAdd(PlayerLantern.ItemName);
        Assert.IsTrue(lantern.Toggle());
        Assert.IsTrue(lantern.Equipped);
        Assert.IsNotNull(lantern.Glow);
        Assert.IsTrue(lantern.Glow.enabled);
        Assert.AreEqual(LightType.Point, lantern.Glow.type);
        Assert.AreEqual(LightShadows.None, lantern.Glow.shadows);
        Assert.IsTrue(lantern.Glow.transform.IsChildOf(player.transform), "The light moves with the player.");

        Assert.IsTrue(lantern.Toggle());
        Assert.IsFalse(lantern.Equipped);
        Assert.IsFalse(lantern.Glow.enabled);
    }

    [Test]
    public void ThePlayerStartsWithTheLanternInTheBagOnce()
    {
        Sprite icon = MakeSprite();
        var settings = new SerializedObject(lantern);
        settings.FindProperty("itemSprite").objectReferenceValue = icon;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Assert.IsTrue(settings.FindProperty("startWithLantern").boolValue, "On by default.");

        Call(lantern, "Start");
        Assert.AreEqual(1, inventory.ItemCount());
        Assert.AreEqual(PlayerLantern.ItemName, inventory.GetItem(0));
        Assert.AreEqual(icon, inventory.GetSprite(0), "With its picture in the bag.");
        Assert.IsFalse(lantern.Equipped, "In the bag, not yet held.");

        Assert.IsFalse(lantern.GiveStartingLantern(), "Never a second one.");
        Assert.AreEqual(1, inventory.ItemCount());
    }

    [Test]
    public void StartingWithTheLanternCanBeSwitchedOff()
    {
        var settings = new SerializedObject(lantern);
        settings.FindProperty("startWithLantern").boolValue = false;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Call(lantern, "Start");
        Assert.AreEqual(0, inventory.ItemCount());
    }

    [Test]
    public void LosingTheLanternPutsItOut()
    {
        inventory.TryAdd(PlayerLantern.ItemName);
        lantern.SetEquipped(true);
        inventory.TryRemove(PlayerLantern.ItemName);
        Call(lantern, "Update");
        Assert.IsFalse(lantern.Equipped);
        Assert.IsFalse(lantern.Glow.enabled);
    }

    [Test]
    public void TheLanternsNumberHoldsItInsteadOfInspectingIt()
    {
        InventoryInspection inspection = player.AddComponent<InventoryInspection>();
        Call(inspection, "Awake");
        inventory.TryAdd("Clue");
        inventory.TryAdd(PlayerLantern.ItemName);
        MethodInfo open = typeof(InventoryInspection).GetMethod("Open", PrivateInstance);
        open.Invoke(inspection, new object[] { 1 });
        Assert.IsTrue(lantern.Equipped);
        Assert.IsFalse(InventoryInspection.IsOpen);
        open.Invoke(inspection, new object[] { 1 });
        Assert.IsFalse(lantern.Equipped);
        Assert.IsFalse(InventoryInspection.IsOpen);
    }

    [Test]
    public void ClickingTheLanternsSlotFindsThatSlot()
    {
        player.AddComponent<InventoryInspection>();
        InventoryHUD hud = player.AddComponent<InventoryHUD>();
        Call(hud, "Awake");
        var panel = (Rect)typeof(InventoryHUD).GetMethod("PanelRect", PrivateInstance).Invoke(hud, null);
        int p = (int)typeof(InventoryHUD).GetMethod("PixelScale", PrivateInstance).Invoke(hud, null);
        for (int i = 0; i < inventory.SlotCount(); i++)
        {
            var centre = new Vector2(panel.x + (i * 34 + 16) * p, panel.y + (14 + 3 + 13) * p);
            Assert.AreEqual(i, hud.SlotAt(centre));
        }
        Assert.AreEqual(-1, hud.SlotAt(new Vector2(panel.x + 4 * p, panel.y + 4 * p)), "The banner is not a slot.");
        Assert.AreEqual(-1, hud.SlotAt(new Vector2(panel.x + 32.5f * p, panel.y + 30 * p)), "Nor the gap between slots.");
    }

    [Test]
    public void HoldingTheLanternSwapsToTheLanternSpritesExceptFromBehind()
    {
        player.AddComponent<SpriteRenderer>();
        PlayerController controller = player.AddComponent<PlayerController>();
        Sprite frontIdle = MakeSprite(), backIdle = MakeSprite(), lanternIdle = MakeSprite();
        Sprite[] frontWalk = { MakeSprite() }, backWalk = { MakeSprite() }, leftWalk = { MakeSprite() }, rightWalk = { MakeSprite() };
        Sprite[] lanternFront = { MakeSprite() }, lanternLeft = { MakeSprite() }, lanternRight = { MakeSprite() };
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("frontIdle").objectReferenceValue = frontIdle;
        serialized.FindProperty("backIdle").objectReferenceValue = backIdle;
        serialized.FindProperty("lanternFrontIdle").objectReferenceValue = lanternIdle;
        SetArray(serialized, "frontWalk", frontWalk);
        SetArray(serialized, "backWalk", backWalk);
        SetArray(serialized, "leftWalk", leftWalk);
        SetArray(serialized, "rightWalk", rightWalk);
        SetArray(serialized, "lanternFrontWalk", lanternFront);
        SetArray(serialized, "lanternLeftWalk", lanternLeft);
        SetArray(serialized, "lanternRightWalk", lanternRight);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Call(controller, "Awake");
        SpriteRenderer sprite = player.GetComponent<SpriteRenderer>();

        Step(controller, Vector2.zero);
        Assert.AreEqual(frontIdle, sprite.sprite);

        inventory.TryAdd(PlayerLantern.ItemName);
        lantern.SetEquipped(true);
        Step(controller, Vector2.zero);
        Assert.AreEqual(lanternIdle, sprite.sprite);
        Assert.Less(lantern.Glow.transform.localPosition.x, 0f, "Held on the sprite's left from the front.");
        Step(controller, Vector2.down);
        Assert.AreEqual(lanternFront[0], sprite.sprite);
        Step(controller, Vector2.left);
        Assert.AreEqual(lanternLeft[0], sprite.sprite);
        Assert.Less(lantern.Glow.transform.localPosition.x, 0f);
        Step(controller, Vector2.right);
        Assert.AreEqual(lanternRight[0], sprite.sprite);
        Assert.Greater(lantern.Glow.transform.localPosition.x, 0f);
        Step(controller, Vector2.up);
        Assert.AreEqual(backWalk[0], sprite.sprite, "From behind the body hides the lantern.");
        Assert.Greater(lantern.Glow.transform.localPosition.z, 0f, "Facing away, it hangs in front of them.");
        Step(controller, Vector2.zero);
        Assert.AreEqual(backIdle, sprite.sprite);

        lantern.SetEquipped(false);
        Step(controller, Vector2.down);
        Assert.AreEqual(frontWalk[0], sprite.sprite);
    }

    private Sprite MakeSprite()
    {
        var texture = new Texture2D(4, 4);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
        created.Add(texture);
        created.Add(sprite);
        return sprite;
    }

    private static void SetArray(SerializedObject serialized, string name, Sprite[] sprites)
    {
        SerializedProperty property = serialized.FindProperty(name);
        property.arraySize = sprites.Length;
        for (int i = 0; i < sprites.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
    }

    private static void Step(PlayerController controller, Vector2 input)
    {
        typeof(PlayerController).GetMethod("UpdateSprite", PrivateInstance).Invoke(controller, new object[] { input });
    }

    private static void Call(object target, string method)
    {
        target.GetType().GetMethod(method, PrivateInstance).Invoke(target, null);
    }
}
