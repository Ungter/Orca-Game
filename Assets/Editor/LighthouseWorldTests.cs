using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LighthouseWorldTests
{
    private Scene scene;
    private PlayerController player;
    private Transform map;
    private LighthouseStacker lighthouse;
    private OpeningVoyage voyage;
    private LighthouseFerry ferry;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp]
    public void SetUp()
    {
        scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.TryGetComponent(out PlayerController p)) player = p;
            if (root.TryGetComponent(out LighthouseStacker s)) lighthouse = s;
            if (root.name == "IslanddemoV3") map = root.transform;
        }
        Assert.IsNotNull(lighthouse);
        typeof(PlayerController).GetMethod("Awake", Private).Invoke(player, null);
        Assert.IsTrue(OpeningVoyage.TryCreate(map, player, out voyage));
        voyage.Dispose();
        ferry = voyage.Boat.GetComponent<LighthouseFerry>();
        Assert.IsNotNull(ferry);
        Assert.IsTrue(ferry.IsReady);
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        lighthouse?.Close();
        ferry?.Cancel();
        voyage?.Dispose();
        if (voyage != null && voyage.Boat != null) Object.DestroyImmediate(voyage.Boat);
        if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void BoatFitsTheVisiblePlayerUniformlyAndFittingIsIdempotent(float size)
    {
        player.transform.localScale = Vector3.one * size;
        BoatFit fit = BoatFit.Fit(voyage.Boat, player);
        Vector3 scale = voyage.Boat.transform.localScale;
        Assert.GreaterOrEqual(fit.RidingWidth, BoatFit.PlayerWidth(player) + 0.599f);
        Assert.AreEqual(scale.x, scale.y, 0.0001f);
        Assert.AreEqual(scale.x, scale.z, 0.0001f);
        BoatFit.Fit(voyage.Boat, player);
        Assert.Less(Vector3.Distance(scale, voyage.Boat.transform.localScale), 0.0001f);
        Renderer mast = voyage.Boat.GetComponentsInChildren<Renderer>().Single(r => r.name == "pCylinder1");
        Bounds localMast = BoatFit.LocalBounds(voyage.Boat.transform, mast);
        float radius = player.GetComponent<CharacterController>().radius * size / scale.x;
        Assert.Less(fit.RidePoint.z + radius, localMast.min.z, "Stand clear of the mast.");
    }

    [Test]
    public void FerryLandingAndLighthouseEntranceAreOnSolidWalkableSurfaces()
    {
        foreach (Vector3 point in new[] { lighthouse.Entrance, lighthouse.PierLanding })
        {
            Assert.IsTrue(Physics.Raycast(point + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 0.6f),
                "No walkable surface at " + point);
            Assert.Greater(hit.normal.y, 0.65f);
            Assert.AreEqual(point.y, hit.point.y, 0.03f);
            Assert.IsTrue(hit.collider.transform.IsChildOf(lighthouse.transform));
            Assert.IsFalse(Physics.CheckCapsule(point + Vector3.up * 0.6f, point + Vector3.up * 1.35f, 0.42f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "No room for the player at " + point);
        }
    }

    [Test]
    public void FerryGoesAroundTheMainIslandAndStaysInsideTheOcean()
    {
        Renderer ground = map.GetComponentsInChildren<Renderer>().Single(r => r.name == OpeningVoyage.GroundName);
        Renderer ocean = map.GetComponentsInChildren<Renderer>().Single(r => r.name == OpeningVoyage.OceanName);
        float clearance = ferry.Fit.Length * 0.5f;
        Bounds land = ground.bounds;
        land.Expand(new Vector3(clearance * 2f, 0f, clearance * 2f));
        for (int i = 0; i <= 200; i++)
        {
            Vector3 point = ferry.PointOnRoute(i / 200f);
            Assert.IsFalse(point.x > land.min.x && point.x < land.max.x && point.z > land.min.z && point.z < land.max.z,
                "The boat cuts through land at " + point);
            Assert.That(point.x, Is.InRange(ocean.bounds.min.x + clearance, ocean.bounds.max.x - clearance));
            Assert.That(point.z, Is.InRange(ocean.bounds.min.z + clearance, ocean.bounds.max.z - clearance));
        }
    }

    [Test]
    public void FerryMakesARoundTripAndCancellationRestoresTheDepartureDock()
    {
        LighthouseTestProgress.Solve(lighthouse.gameObject);
        Assert.Less(ferry.InteractDistanceSqr(), float.MaxValue);
        Cross();
        Assert.IsTrue(ferry.AtLighthouse);
        Assert.Less(Flat(player.transform.position - lighthouse.PierLanding), 0.001f);
        Assert.IsTrue(ferry.Begin(Camera.main));
        Assert.IsTrue(Interactables.Busy);
        Assert.IsTrue(Interactables.BlocksMovement);
        ferry.Tick(1f);
        ferry.Tick(ferry.CrossingDuration * 0.5f);
        ferry.Cancel();
        Assert.IsTrue(ferry.AtLighthouse);
        Assert.Less(Flat(player.transform.position - lighthouse.PierLanding), 0.001f);
        Assert.IsTrue(player.enabled);
        Assert.IsTrue(player.GetComponent<CharacterController>().enabled);
        Assert.IsFalse(Interactables.Busy);
        Cross();
        Assert.IsFalse(ferry.AtLighthouse);
        Assert.Less(Flat(player.transform.position - ferry.MainLanding), 0.001f);
    }

    [Test]
    public void StackerAwardsOneWinContinuesPastTenAndRestoresTheCameraOnExit()
    {
        LighthouseTestProgress.Solve(lighthouse.gameObject);
        Camera camera = Camera.main;
        Vector3 position = camera.transform.position;
        Quaternion rotation = camera.transform.rotation;
        float fov = camera.fieldOfView;
        int wins = 0;
        lighthouse.Won += () => wins++;
        Assert.IsTrue(lighthouse.Begin(player.transform, camera));
        Assert.IsTrue(Interactables.BlocksMovement);
        for (int i = 0; i < 1000; i++)
        {
            lighthouse.Tick(-lighthouse.Match.Offset / lighthouse.Match.Speed);
            lighthouse.Tick(0f, true);
            Assert.IsFalse(lighthouse.Match.Failed, "Section " + i);
            Assert.LessOrEqual(lighthouse.View.SectionCount, LighthouseStackView.VisibleSections);
            Assert.LessOrEqual(lighthouse.View.Moving.localPosition.y, LighthouseStackView.VisibleSections * LighthouseStackView.SectionHeight + 0.01f);
        }
        Assert.AreEqual(1000, lighthouse.Match.Score);
        Assert.IsTrue(lighthouse.HasWon);
        Assert.AreEqual(1, wins);
        lighthouse.Tick(0f, true); // New piece is outside the tower: a complete miss.
        Assert.IsTrue(lighthouse.Match.Failed);
        lighthouse.Tick(0f, false, true);
        Assert.AreEqual(0, lighthouse.Match.Score);
        Assert.IsTrue(lighthouse.HasWon, "Retrying cannot erase the earned win.");
        Assert.AreEqual(1, wins);
        lighthouse.Close();
        Assert.IsFalse(LighthouseStacker.IsOpen);
        Assert.IsFalse(Interactables.Busy);
        Assert.AreEqual(position, camera.transform.position);
        Assert.Less(Quaternion.Angle(rotation, camera.transform.rotation), 0.001f);
        Assert.AreEqual(fov, camera.fieldOfView);
    }

    [Test]
    public void DisablingEitherActivityRestoresControlsAndDoesNotEnableSomeoneElsesController()
    {
        LighthouseTestProgress.Solve(lighthouse.gameObject);
        player.enabled = false;
        player.GetComponent<CharacterController>().enabled = false;
        Assert.IsTrue(ferry.Begin(Camera.main));
        ferry.enabled = false;
        typeof(LighthouseFerry).GetMethod("OnDisable", Private).Invoke(ferry, null);
        Assert.IsFalse(LighthouseFerry.IsSailing);
        Assert.IsFalse(player.enabled);
        Assert.IsFalse(player.GetComponent<CharacterController>().enabled);
        Assert.IsTrue(lighthouse.Begin(player.transform, Camera.main));
        lighthouse.enabled = false;
        typeof(LighthouseStacker).GetMethod("OnDisable", Private).Invoke(lighthouse, null);
        Assert.IsFalse(LighthouseStacker.IsOpen);
        Assert.IsFalse(Interactables.Busy);
    }

    [Test]
    public void LighthouseFerryAndStackerUnlockOnlyAfterTheActualAnagramIsSolved()
    {
        Assert.IsFalse(lighthouse.IsUnlocked);
        Assert.AreEqual(float.MaxValue, ferry.InteractDistanceSqr());
        Assert.IsFalse(ferry.Begin(Camera.main));
        player.PlaceOnGround(lighthouse.Entrance);
        Assert.AreEqual(float.MaxValue, lighthouse.InteractDistanceSqr());
        Assert.IsFalse(lighthouse.Begin(player.transform, Camera.main), "Walking to the island cannot bypass the anagram.");
        Assert.IsFalse(Interactables.Busy);
        DolphinWordPuzzle puzzle = LighthouseTestProgress.Solve(lighthouse.gameObject);
        Assert.IsTrue(lighthouse.IsUnlocked);
        Assert.Less(lighthouse.InteractDistanceSqr(), float.MaxValue);
        Assert.IsTrue(lighthouse.Begin(player.transform, Camera.main));
        lighthouse.Close();
        puzzle.gameObject.SetActive(false);
        Assert.IsTrue(lighthouse.IsUnlocked, "Closing or hiding the solved puzzle must keep access unlocked.");
        player.PlaceOnGround(ferry.MainLanding);
        Assert.IsTrue(ferry.Begin(Camera.main));
        ferry.Cancel();
    }

    private void Cross()
    {
        Assert.IsTrue(ferry.Begin(Camera.main));
        Assert.IsFalse(player.GetComponent<CharacterController>().enabled);
        ferry.Tick(1f);
        Assert.AreEqual(LighthouseFerry.Phase.Sailing, ferry.CurrentPhase);
        ferry.Tick(ferry.CrossingDuration + 0.1f);
        ferry.Tick(1f);
        Assert.AreEqual(LighthouseFerry.Phase.Moored, ferry.CurrentPhase);
        Assert.IsFalse(LighthouseFerry.IsSailing);
        Assert.IsTrue(player.GetComponent<CharacterController>().enabled);
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}
