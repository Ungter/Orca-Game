using System.Reflection;
using NUnit.Framework;

public class OpeningMessageTests
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    [Test]
    public void OpeningMessageUsesTheRequestedText()
    {
        Assert.AreEqual("SOMEONE'S BEEN MURDERED!",
            typeof(TitleScreen).GetField("OpeningMessage", PrivateStatic).GetRawConstantValue());
    }

    [TestCase(-1f)]
    [TestCase(0f)]
    [TestCase(1f)]
    public void MessageWaitsUntilTheBoatIsUnderWay(float time)
    {
        Assert.AreEqual(0f, MessageOpacity(time));
    }

    [Test]
    public void MessageStaysReadableAWhileThenFadesOut()
    {
        float start = Constant("MessageStart");
        float duration = Constant("MessageDuration");
        float fade = Constant("MessageFadeDuration");
        Assert.GreaterOrEqual(duration, 2.5f);
        foreach (float fraction in new[] { 0.15f, 0.5f, 0.85f })
            Assert.AreEqual(1f, MessageOpacity(start + duration * fraction), 0.001f);
        Assert.AreEqual(0.5f, MessageOpacity(start + duration - fade * 0.5f), 0.001f);
        Assert.AreEqual(0f, MessageOpacity(start + duration), 0.001f);
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

    private static float MessageOpacity(float time) => Invoke("OpeningMessageOpacity", time);
    private static float BarsAmount(float handoff) => Invoke("CinematicBarsAmount", handoff);

    private static float Invoke(string method, float time)
    {
        return (float)typeof(TitleScreen).GetMethod(method, PrivateStatic).Invoke(null, new object[] { time });
    }
}
