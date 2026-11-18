using System.Reflection;
using NUnit.Framework;

public class OpeningMessageTests
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    [Test]
    public void TheOpeningHasNoMessageOverTheBoatRide()
    {
        Assert.IsNull(typeof(TitleScreen).GetField("OpeningMessage", PrivateStatic));
        Assert.IsNull(typeof(TitleScreen).GetMethod("OpeningMessageOpacity", PrivateStatic));
    }

    [Test]
    public void BarsStayForTheWholeOpeningAndOpenOverTheHandoff()
    {
        float handoff = Constant("HandoffDuration");
        Assert.AreEqual(1f, BarsAmount(-1f));
        Assert.AreEqual(1f, BarsAmount(0f));
        Assert.AreEqual(0.5f, BarsAmount(handoff * 0.5f), 0.001f);
        Assert.AreEqual(0f, BarsAmount(handoff));
    }

    private static float Constant(string name)
    {
        return (float)typeof(TitleScreen).GetField(name, PrivateStatic).GetRawConstantValue();
    }

    private static float BarsAmount(float handoff) => Invoke("CinematicBarsAmount", handoff);

    private static float Invoke(string method, float time)
    {
        return (float)typeof(TitleScreen).GetMethod(method, PrivateStatic).Invoke(null, new object[] { time });
    }
}
