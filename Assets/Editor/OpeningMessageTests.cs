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
    [TestCase(3.7f)]
    public void MessageWaitsUntilThePlayerHasWalkedOut(float time)
    {
        Assert.AreEqual(0f, MessageOpacity(time));
        Assert.AreEqual(1f, BarsAmount(time));
    }

    [Test]
    public void MessageRemainsReadableBeforeTheBarsStartToFade()
    {
        float duration = Constant("MessageDuration");
        Assert.GreaterOrEqual(duration, 2.5f);
        foreach (float fraction in new[] { 0.15f, 0.5f, 0.85f })
        {
            float time = OpeningWalkout.Duration + duration * fraction;
            Assert.AreEqual(1f, MessageOpacity(time), 0.001f);
            Assert.AreEqual(1f, BarsAmount(time));
        }
    }

    [Test]
    public void MessageFadesOutBeforeTheBarsAndCameraHandoff()
    {
        float start = Constant("HandoffStart");
        float fade = Constant("MessageFadeDuration");
        Assert.AreEqual(0.5f, MessageOpacity(start - fade * 0.5f), 0.001f);
        Assert.AreEqual(1f, BarsAmount(start - fade * 0.5f));
        Assert.AreEqual(0f, MessageOpacity(start), 0.001f);
        Assert.AreEqual(1f, BarsAmount(start));
        float halfway = start + Constant("HandoffDuration") * 0.5f;
        Assert.AreEqual(0f, MessageOpacity(halfway));
        Assert.AreEqual(0.5f, BarsAmount(halfway), 0.001f);
        Assert.AreEqual(0f, BarsAmount(start + Constant("HandoffDuration")));
    }

    private static float Constant(string name)
    {
        return (float)typeof(TitleScreen).GetField(name, PrivateStatic).GetRawConstantValue();
    }

    private static float MessageOpacity(float time) => Invoke("OpeningMessageOpacity", time);
    private static float BarsAmount(float time) => Invoke("CinematicBarsAmount", time);

    private static float Invoke(string method, float time)
    {
        return (float)typeof(TitleScreen).GetMethod(method, PrivateStatic).Invoke(null, new object[] { time });
    }
}
