using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

public class WindowLampTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<Object> created = new List<Object>();
    private GameObject head;
    private Material lampGlow;
    private Material wall;
    private Material glass;
    private Material bars;

    [SetUp]
    public void SetUp()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        lampGlow = Keep(new Material(lit) { name = WindowLamp.LampMaterialName });
        wall = Keep(new Material(lit) { name = "Wall", color = new Color(0.35f, 0.22f, 0.12f) });
        glass = Keep(new Material(lit) { name = "Glass", color = new Color(0.85f, 0.75f, 0.2f) });
        bars = Keep(new Material(lit) { name = "Bars", color = new Color(0.05f, 0.04f, 0.03f) });
        // A lamp head wearing the lamps' glow, as the map import leaves them.
        head = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
        head.name = "maplamp:pCube1";
        head.transform.position = new Vector3(0f, 0f, 40f);
        head.GetComponent<Renderer>().sharedMaterial = lampGlow;
    }

    [TearDown]
    public void TearDown()
    {
        // Edit mode never calls OnDestroy; it puts the materials back and removes the lights.
        foreach (WindowLamp lamp in Object.FindObjectsByType<WindowLamp>())
            typeof(WindowLamp).GetMethod("OnDestroy", Private).Invoke(lamp, null);
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null) Object.DestroyImmediate(created[i]);
        }
        created.Clear();
    }

    [Test]
    public void WindowsAreFoundByTheirNames()
    {
        foreach (string name in new[] { "magHW1", "alHW2", "chudHW10", "glowWindow1", "glowWindow2" })
            Assert.IsTrue(WindowLamp.IsWindow(name), name);
        foreach (string name in new[] { "pCube79", "maplamp:pCube1", "glowWindow3", "MagHW1" })
            Assert.IsFalse(WindowLamp.IsWindow(name), name);
    }

    [Test]
    public void TheGlassGlowsLikeTheLampHeadsAndALightShinesOutOfTheHouse()
    {
        GameObject window = House("magHW3");
        Assert.AreEqual(1, WindowLamp.Install(SceneManager.GetActiveScene()));
        WindowLamp lamp = window.GetComponent<WindowLamp>();
        Assert.IsNotNull(lamp);
        Call(lamp, "Start");

        Material[] now = window.GetComponent<Renderer>().sharedMaterials;
        Assert.AreEqual(WindowLamp.GlassGlow(), now[0], "The glass glows.");
        Assert.AreEqual(lampGlow.shader, now[0].shader, "Like the lamp heads, from their material.");
        Assert.IsTrue(now[0].IsKeywordEnabled("_EMISSION"));
        Color emission = now[0].GetColor("_EmissionColor");
        Assert.Greater(emission.r, emission.g * 1.4f, "Warmer than the lamp heads' pale yellow.");
        Assert.Greater(emission.g, emission.b);
        Assert.Less(emission.maxColorComponent, 2.7f * 0.6f, "Softer than the lamp heads, so a whole pane isn't blinding.");
        Assert.AreEqual(bars, now[1], "The dark bars across the panes stay dark.");
        Assert.AreEqual(wall, now[2], "The frame, shared with the walls, stays as it is.");
        Assert.IsNotNull(lamp.Glow);
        Assert.AreEqual(LightType.Spot, lamp.Glow.type);
        Assert.AreEqual(LightShadows.None, lamp.Glow.shadows);
        Assert.Greater(lamp.Glow.intensity, 0f);
        Assert.Greater(Vector3.Dot(lamp.Outward, Vector3.right), 0.99f, "It shines out of the house, not into it.");
        Assert.Greater(Vector3.Dot(lamp.Glow.transform.forward, Vector3.right), 0.9f);
        Assert.Greater(lamp.Glow.transform.position.x, lamp.FaceCentre.x, "The light sits just outside the glass.");

        // The light spills onto the wall round the window, without a real light's hot spot.
        MeshRenderer spill = lamp.Spill;
        Assert.IsNotNull(spill);
        Assert.AreEqual(WindowLamp.SpillShaderName, spill.sharedMaterial.shader.name);
        Assert.IsTrue(spill.sharedMaterial.shader.isSupported);
        Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, spill.shadowCastingMode);
        Assert.AreEqual(0, spill.GetComponentsInChildren<Light>().Length, "Not a light, so nothing to reflect.");
        Vector3 offset = spill.transform.position - lamp.FaceCentre;
        Assert.Greater(offset.x, 0f, "Just out from the glass, so the wall doesn't hide it.");
        Assert.Less(offset.x, 0.1f, "Lying on the wall, not floating out in front of it.");
        Assert.Less(new Vector2(offset.y, offset.z).magnitude, 1e-3f, "Centred on the window.");
        Assert.Greater(Vector3.Dot(spill.transform.forward, Vector3.right), 0.99f, "Facing out of the house.");
        // The window is 1.2 high and 1.4 wide; the glow reaches SpillMargin x 1.2 past each edge.
        Bounds card = spill.bounds;
        float margin = 1.2f * WindowLamp.SpillMargin;
        Assert.AreEqual(1.2f + margin * 2f, card.size.y, 1e-3f);
        Assert.AreEqual(1.4f + margin * 2f, card.size.z, 1e-3f);
        Call(lamp, "OnDisable");
        Assert.IsFalse(spill.gameObject.activeInHierarchy, "Hidden with the window's light.");
        Call(lamp, "OnEnable");
        Assert.IsTrue(spill.gameObject.activeInHierarchy);

        Call(lamp, "OnDestroy");
        CollectionAssert.AreEqual(new[] { glass, bars, wall }, window.GetComponent<Renderer>().sharedMaterials,
            "The window gets its own materials back.");
        Assert.IsTrue(lamp.Glow == null);
        Assert.IsTrue(spill == null, "It goes with the window's light.");
    }

    // alHWRound... are glass drums, not panes: a flat card and a one-way spot came out wrong.
    [Test]
    public void ARoundGlassDrumShinesOutAllRoundWithARingOfSpill()
    {
        var tower = Keep(new GameObject("Test tower"));
        GameObject walls = GameObject.CreatePrimitive(PrimitiveType.Cube);
        walls.name = "Walls";
        walls.transform.SetParent(tower.transform, false);
        walls.transform.localScale = new Vector3(3f, 4f, 3f);
        walls.GetComponent<Renderer>().sharedMaterial = wall;
        GameObject drum = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        drum.name = "alHWRound2";
        drum.transform.SetParent(tower.transform, false);
        drum.transform.localPosition = new Vector3(0f, 3.5f, 0f);
        // Unity's cylinder is 1 wide and 2 tall: this drum is 3 wide and 3 tall.
        drum.transform.localScale = new Vector3(3f, 1.5f, 3f);
        drum.GetComponent<Renderer>().sharedMaterials = new[] { glass };
        WindowLamp.Install(SceneManager.GetActiveScene());
        WindowLamp lamp = drum.GetComponent<WindowLamp>();
        Call(lamp, "Start");

        Assert.IsTrue(lamp.IsRound);
        Assert.AreEqual(WindowLamp.GlassGlow(), drum.GetComponent<Renderer>().sharedMaterials[0], "The glass glows.");
        Assert.AreEqual(LightType.Point, lamp.Glow.type, "Out all round, not one way.");
        Assert.AreEqual(LightShadows.None, lamp.Glow.shadows, "The glass round it doesn't stop it.");
        Assert.Less(Vector3.Distance(lamp.Glow.transform.position, drum.transform.position), 1e-3f, "From the middle of the drum.");
        Assert.Greater(lamp.Glow.range, 1.5f, "Reaching out past the glass.");
        Assert.AreEqual(Vector3.zero, lamp.Outward);

        Bounds ring = lamp.Spill.bounds;
        Assert.Less(Vector3.Distance(ring.center, drum.transform.position), 1e-3f, "Centred on the drum.");
        Assert.Greater(ring.size.x, 3f, "Just outside the glass all round...");
        Assert.Less(ring.size.x, 3.3f, "...not out in front of it.");
        Assert.AreEqual(ring.size.x, ring.size.z, 1e-2f, "Round, not a flat card.");
        Assert.AreEqual(3f + 2f * 3f * WindowLamp.SpillMargin, ring.size.y, 1e-3f, "Reaching above and below the drum.");

        Call(lamp, "OnDestroy");
        Assert.IsTrue(lamp.Glow == null);
    }

    [Test]
    public void OnlyGlassDrumsCountAsRound()
    {
        Assert.IsTrue(WindowLamp.IsRoundWindow(new[] { new Vector3(3f, 0f, 0f), new Vector3(0f, 3f, 0f), new Vector3(0f, 0f, 3f) }));
        Assert.IsFalse(WindowLamp.IsRoundWindow(new[] { new Vector3(0.1f, 0f, 0f), new Vector3(0f, 1.2f, 0f), new Vector3(0f, 0f, 1.4f) }),
            "A pane in a wall.");
        Assert.IsFalse(WindowLamp.IsRoundWindow(new[] { new Vector3(2f, 0f, 0f), new Vector3(0f, 0.05f, 0f), new Vector3(0f, 0f, 2f) }),
            "A flat skylight.");
    }

    [Test]
    public void StripedRoundWindowsKeepTheirStripesAndOneMaterialWindowsGlowWhole()
    {
        GameObject round = House("alHW4");
        round.GetComponent<Renderer>().sharedMaterials = new[] { bars, glass };
        GameObject plain = House("chudHW2");
        plain.GetComponent<Renderer>().sharedMaterials = new[] { glass };
        WindowLamp.Install(SceneManager.GetActiveScene());
        Call(round.GetComponent<WindowLamp>(), "Start");
        Call(plain.GetComponent<WindowLamp>(), "Start");
        CollectionAssert.AreEqual(new[] { bars, WindowLamp.GlassGlow() }, round.GetComponent<Renderer>().sharedMaterials);
        CollectionAssert.AreEqual(new[] { WindowLamp.GlassGlow() }, plain.GetComponent<Renderer>().sharedMaterials);
    }

    [Test]
    public void GlassPaintedWithATextureKeepsItsPatternInTheGlow()
    {
        var stripes = Keep(new Texture2D(4, 4));
        var painted = Keep(new Material(glass) { name = "Painted glass" });
        painted.SetTexture("_BaseMap", stripes);
        GameObject window = House("magHW9");
        window.GetComponent<Renderer>().sharedMaterials = new[] { painted, wall };
        WindowLamp.Install(SceneManager.GetActiveScene());
        Call(window.GetComponent<WindowLamp>(), "Start");
        Material glow = window.GetComponent<Renderer>().sharedMaterials[0];
        Assert.AreNotEqual(painted, glow);
        Assert.AreEqual(stripes, glow.GetTexture("_EmissionMap"), "The painted stripes shape the glow.");
        Assert.AreEqual(WindowLamp.GlassGlow().GetColor("_EmissionColor"), glow.GetColor("_EmissionColor"));
        Keep(glow);
    }

    [Test]
    public void GlowWindowsSwapTheirRaysForTheLampGlow()
    {
        GameObject window = House("glowWindow1");
        WindowGlow rays = window.AddComponent<WindowGlow>();
        WindowLamp.Install(SceneManager.GetActiveScene());
        Assert.IsFalse(rays.enabled, "Its own rays and delayed switch-on make way for the lamp glow.");
        Assert.IsNotNull(window.GetComponent<WindowLamp>());
    }

    [Test]
    public void InstallingTwiceGivesAWindowOneLamp()
    {
        GameObject window = House("alHW1");
        WindowLamp.Install(SceneManager.GetActiveScene());
        WindowLamp.Install(SceneManager.GetActiveScene());
        Assert.AreEqual(1, window.GetComponents<WindowLamp>().Length);
    }

    [Test]
    public void WithoutALampHeadAboutTheGlassStillGlowsWarmYellow()
    {
        Object.DestroyImmediate(head);
        GameObject window = House("chudHW1");
        WindowLamp.Install(SceneManager.GetActiveScene());
        WindowLamp lamp = window.GetComponent<WindowLamp>();
        Call(lamp, "Start");
        Material glow = window.GetComponent<Renderer>().sharedMaterials[0];
        Assert.AreNotEqual(glass, glow);
        Assert.IsTrue(glow.IsKeywordEnabled("_EMISSION"));
        Color emission = glow.GetColor("_EmissionColor");
        Assert.Greater(emission.r, emission.b, "Warm yellow, like the lamps.");
    }

    // A house block with a thin window on its +x face: the window's own yellow glass and dark
    // bars, and its frame in the walls' material.
    private GameObject House(string windowName)
    {
        var house = Keep(new GameObject("Test house"));
        GameObject walls = GameObject.CreatePrimitive(PrimitiveType.Cube);
        walls.name = "Walls";
        walls.transform.SetParent(house.transform, false);
        walls.transform.localScale = new Vector3(6f, 4f, 6f);
        walls.GetComponent<Renderer>().sharedMaterial = wall;
        GameObject window = GameObject.CreatePrimitive(PrimitiveType.Cube);
        window.name = windowName;
        window.transform.SetParent(house.transform, false);
        window.transform.localPosition = new Vector3(3.05f, 0.5f, 0f);
        window.transform.localScale = new Vector3(0.1f, 1.2f, 1.4f);
        window.GetComponent<Renderer>().sharedMaterials = new[] { glass, bars, wall };
        return window;
    }

    private T Keep<T>(T item) where T : Object
    {
        created.Add(item);
        return item;
    }

    private static void Call(object target, string method) =>
        target.GetType().GetMethod(method, Private).Invoke(target, null);
}
