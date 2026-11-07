using NUnit.Framework;
using UnityEngine;

public class TitleAtmosphereTests
{
    // The scene's night fog hid the island from the title's orbit; the title thins it.
    [TestCase(150f)]
    [TestCase(600f)]
    [TestCase(1500f)]
    public void TheIslandStaysClearFromTheTitleCamera(float distance)
    {
        const float sceneDensity = 0.009f;
        float density = TitleScreen.TitleFogDensity(sceneDensity, distance);
        float clear = Mathf.Exp(-(density * distance) * (density * distance));
        Assert.GreaterOrEqual(clear, 0.8f, "Most of the island shows through the fog.");
        Assert.LessOrEqual(density, sceneDensity, "Never thicker than the scene's own fog.");
    }

    [Test]
    public void CloseUpTheSceneFogIsUntouched()
    {
        Assert.AreEqual(0.009f, TitleScreen.TitleFogDensity(0.009f, 20f), 1e-6f);
        Assert.AreEqual(0f, TitleScreen.TitleFogDensity(0f, 600f), "No fog stays no fog.");
    }
}
