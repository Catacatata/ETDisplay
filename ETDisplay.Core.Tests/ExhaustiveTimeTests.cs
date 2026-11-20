using Xunit;

namespace ETDisplay.Core.Tests;

public sealed class ExhaustiveTimeTests
{
    [Fact]
    public void EveryMinuteOfDayRoundTripsAcrossSupportedFormats()
    {
        // 1440 times x 5 forms; complements the explicit syntax/error examples.
        for (var hour = 0; hour < 24; hour++)
        for (var minute = 0; minute < 60; minute++)
        {
            var expected = new EorzeaTime(hour, minute);
            string[] messages =
            [
                $"ET {hour:D2}:{minute:D2}",
                $"ET {hour:D2}：{minute:D2}",
                $"ET {hour:D2}.{minute:D2}",
                $"ET {hour:D2}{minute:D2}",
                $"ET {hour}{minute:D2}",
            ];
            foreach (var message in messages)
            {
                Assert.True(HuntMessageParser.TryParse(message, out var actual), message);
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void StateCanBeReadAndChangedConcurrentlyWithoutInvalidSnapshots()
    {
        var session = new DisplaySession();
        Parallel.For(0, 5000, index =>
        {
            switch (index % 4)
            {
                case 0: session.Receive(ChatChannel.Shout, "ET1230"); break;
                case 1: session.Clear(); break;
                case 2: session.SetEnabled(index % 8 == 2); break;
                default:
                    var snapshot = session.Snapshot;
                    Assert.True(snapshot.Enabled || snapshot.Target is null);
                    Assert.Equal(snapshot.Enabled && snapshot.Target.HasValue, snapshot.IsVisible);
                    break;
            }
        });
        session.SetEnabled(false);
        Assert.False(session.Snapshot.IsVisible);
        Assert.False(session.Receive(ChatChannel.Shout, "ET1230"));
    }
}
