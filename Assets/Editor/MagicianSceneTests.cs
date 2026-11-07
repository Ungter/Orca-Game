using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MagicianSceneTests
{
    [Test]
    public void SceneConnectsTheBuildingOneDoorwayAndReplacesTheSecondLetterWithTheWand()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            Transform map = null;
            MagicianEncounter encounter = null;
            PickupItem wand = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "IslanddemoV3") map = root.transform;
                if (root.TryGetComponent(out MagicianEncounter found)) encounter = found;
                if (root.name == "MagicWand") wand = root.GetComponent<PickupItem>();
                Assert.AreNotEqual("ClueLetter2", root.name);
            }
            Assert.IsNotNull(map);
            Transform door = MagicianEncounter.FindDoor(map.Find("world/building1"));
            Assert.IsNotNull(door);
            Assert.IsNotNull(encounter);
            Bounds bounds = door.GetComponent<Renderer>().bounds;
            Vector3 offset = encounter.transform.position - bounds.center;
            Assert.Less(new Vector2(offset.x, offset.z).magnitude, 1.5f);
            Assert.AreEqual(bounds.min.y, encounter.transform.position.y, 0.1f);
            Assert.IsNotNull(new SerializedObject(encounter).FindProperty("rewardPaper").objectReferenceValue);
            Assert.IsNotNull(new SerializedObject(encounter).FindProperty("lanternSprite").objectReferenceValue);
            Assert.IsNotNull(wand);
            Assert.AreEqual(MagicianEncounter.WandItemName,
                new SerializedObject(wand).FindProperty("itemName").stringValue);
            Assert.IsNotNull(wand.GetComponent<WandMagic>());
            Assert.IsNotNull(wand.GetComponent<GrassBillboard>());
            SpriteRenderer sprite = wand.GetComponent<SpriteRenderer>();
            Assert.AreEqual(Resources.Load<Sprite>("Magician/MagicWand"), sprite.sprite);
            Assert.IsNotNull(sprite.sharedMaterial);
            Assert.AreEqual("Sprites/Default", sprite.sharedMaterial.shader.name);
            Assert.IsTrue(new SerializedObject(wand).FindProperty("glow").boolValue);
            Assert.IsTrue(new SerializedObject(wand).FindProperty("bob").boolValue);
            var stageObject = new GameObject("Entrance placement test");
            try
            {
                ShellMinigame game = stageObject.AddComponent<ShellMinigame>();
                game.PlayInWorld(map.Find("world/building1"), encounter.transform.position);
                WorldHatStage stage = stageObject.GetComponent<WorldHatStage>();
                Vector3 front = encounter.transform.position - bounds.center;
                front.y = 0f;
                front.Normalize();
                Vector3 stageOffset = stage.Centre - encounter.transform.position;
                Vector3 viewerOffset = stage.ViewingPoint - encounter.transform.position;
                Assert.AreEqual(3f, Vector3.Dot(stageOffset, front), 0.01f);
                Assert.AreEqual(12f, Vector3.Dot(viewerOffset, front), 0.01f);
                Assert.That(stage.Centre.y, Is.InRange(bounds.min.y + 1f, bounds.max.y));
                Physics.SyncTransforms();
                Assert.IsFalse(Physics.CheckCapsule(stage.ViewingPoint + Vector3.up * 0.6f,
                    stage.ViewingPoint + Vector3.up * 1.5f, 0.45f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore), "The viewing spot must be clear of the house, lamps and fences.");
                game.Hide();
            }
            finally
            {
                Object.DestroyImmediate(stageObject);
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
