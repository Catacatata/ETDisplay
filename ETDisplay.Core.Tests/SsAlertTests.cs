using Xunit;

namespace ETDisplay.Core.Tests;

public sealed class SsAlertTests
{
    private static TimeSpan Seconds(double value) => TimeSpan.FromSeconds(value);

    // These exercise inherited candidate rules, not verified real system samples.
    [Theory]
    [InlineData("某个怪物开始了侦察活动！")]
    [InlineData("开始了侦察活动")]
    [InlineData("The creature has begun scouting.")]
    [InlineData("HAS BEGUN SCOUTING")]
    [InlineData("Has Begun Scouting!")]
    [InlineData("(has begun scouting)")]
    [InlineData("1has begun scouting2")]
    [InlineData("_has begun scouting_")]
    [InlineData("xhas begun scoutingness; has begun scouting.")]
    public void MatchesSystemCandidateWording(string body)
    {
        Assert.True(SsAlertMessage.IsScouting(ChatChannel.SystemMessage, body));
    }

    [Theory]
    [InlineData(ChatChannel.Say)]
    [InlineData(ChatChannel.Yell)]
    [InlineData(ChatChannel.Shout)]
    [InlineData(ChatChannel.Echo)]
    [InlineData(ChatChannel.Other)]
    public void RejectsPlayerAndEchoCopies(ChatChannel channel)
    {
        Assert.False(SsAlertMessage.IsScouting(channel, "开始了侦察活动"));
        Assert.False(SsAlertMessage.IsScouting(channel, "The creature has begun scouting."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("x")]
    [InlineData("scouting")]
    [InlineData("has begun scoutingness")]
    [InlineData("xhas begun scouting")]
    [InlineData("éhas begun scouting")]
    [InlineData("has begun scoutingé")]
    [InlineData("has begun scoutingx; xhas begun scouting")]
    [InlineData("has  begun scouting")]
    [InlineData("开始侦察活动")]
    public void RejectsMissingOrNonMatchingWording(string? body)
    {
        Assert.False(SsAlertMessage.IsScouting(ChatChannel.SystemMessage, body));
    }

    [Theory]
    [InlineData("开始了侦察活动")]
    [InlineData("has begun scouting")]
    public void EnforcesOriginalBodyLengthBeforeMatching(string phrase)
    {
        var maximum = phrase + new string(' ', 2048 - phrase.Length);
        Assert.True(SsAlertMessage.IsScouting(ChatChannel.SystemMessage, maximum));
        Assert.False(SsAlertMessage.IsScouting(ChatChannel.SystemMessage, maximum + " "));
        Assert.False(SsAlertMessage.IsScouting(ChatChannel.SystemMessage, new string(' ', 2049) + phrase));
    }

    [Fact]
    public void DefaultSnapshotIsHiddenAndFirstTriggerAtZeroSucceeds()
    {
        var session = new SsAlertSession();
        Assert.Equal(new SsAlertSnapshot(false), session.Snapshot);
        Assert.Equal(string.Empty, session.Snapshot.Text);
        Assert.True(session.TryTrigger(TimeSpan.Zero, Seconds(5)));
        Assert.True(session.Snapshot.IsVisible);
        Assert.Equal("SS出现！", session.Snapshot.Text);
    }

    [Fact]
    public void HidesAtExactlyFiveRealSeconds()
    {
        var session = new SsAlertSession();
        session.TryTrigger(TimeSpan.Zero, Seconds(5));
        session.Update(Seconds(5) - TimeSpan.FromTicks(1));
        Assert.True(session.Snapshot.IsVisible);
        session.Update(Seconds(5));
        Assert.False(session.Snapshot.IsVisible);
        Assert.Equal(string.Empty, session.Snapshot.Text);
    }

    [Fact]
    public void DuplicatesExtendNeitherVisibilityNorCooldown()
    {
        var session = new SsAlertSession();
        Assert.True(session.TryTrigger(TimeSpan.Zero, Seconds(5)));
        Assert.False(session.TryTrigger(Seconds(4), Seconds(30)));
        session.Update(Seconds(5));
        Assert.False(session.Snapshot.IsVisible);
        Assert.False(session.TryTrigger(Seconds(9), Seconds(30)));
        Assert.False(session.TryTrigger(Seconds(10) - TimeSpan.FromTicks(1), Seconds(30)));
        Assert.True(session.TryTrigger(Seconds(10), Seconds(5)));
        session.Update(Seconds(15));
        Assert.False(session.Snapshot.IsVisible);
    }

    [Fact]
    public void RejectedTriggerAlsoObservesExpiry()
    {
        var session = new SsAlertSession();
        session.TryTrigger(TimeSpan.Zero, Seconds(5));
        Assert.False(session.TryTrigger(Seconds(6), Seconds(20)));
        Assert.False(session.Snapshot.IsVisible);
    }

    [Fact]
    public void RetriggerAtCooldownBoundaryReplacesStillVisibleEvent()
    {
        var session = new SsAlertSession();
        session.TryTrigger(TimeSpan.Zero, Seconds(30));
        Assert.True(session.TryTrigger(Seconds(10), Seconds(1)));
        session.Update(Seconds(11));
        Assert.False(session.Snapshot.IsVisible);
        Assert.False(session.TryTrigger(Seconds(19), Seconds(5)));
        Assert.True(session.TryTrigger(Seconds(20), Seconds(5)));
    }

    [Fact]
    public void ClearHidesAndAllowsImmediateRetrigger()
    {
        var session = new SsAlertSession();
        session.TryTrigger(Seconds(100), Seconds(30));
        session.Clear();
        Assert.False(session.Snapshot.IsVisible);
        Assert.Equal(string.Empty, session.Snapshot.Text);
        Assert.True(session.TryTrigger(Seconds(100), Seconds(5)));
        session.Clear();
        session.Clear();
        Assert.True(session.TryTrigger(TimeSpan.Zero, Seconds(5)));
    }

    [Fact]
    public void UpdatesBeforeFirstTriggerDoNotCreateCooldown()
    {
        var session = new SsAlertSession();
        session.Update(TimeSpan.Zero);
        Assert.True(session.TryTrigger(TimeSpan.Zero, Seconds(5)));
    }

    [Fact]
    public void RollbackDoesNotResurrectOrExtendDisplay()
    {
        var session = new SsAlertSession();
        session.TryTrigger(Seconds(100), Seconds(5));
        session.Update(Seconds(104));
        session.Update(Seconds(90));
        Assert.True(session.Snapshot.IsVisible);
        session.Update(Seconds(105));
        Assert.False(session.Snapshot.IsVisible);
        session.Update(Seconds(101));
        Assert.False(session.Snapshot.IsVisible);
        Assert.False(session.TryTrigger(Seconds(102), Seconds(5)));
        Assert.True(session.TryTrigger(Seconds(110), Seconds(5)));
    }

    [Fact]
    public void StaleTriggerUsesLatestObservedMonotonicTime()
    {
        var session = new SsAlertSession();
        session.TryTrigger(TimeSpan.Zero, Seconds(5));
        session.Update(Seconds(20));
        Assert.True(session.TryTrigger(Seconds(10), Seconds(5)));
        session.Update(Seconds(24));
        Assert.True(session.Snapshot.IsVisible);
        session.Update(Seconds(25));
        Assert.False(session.Snapshot.IsVisible);
    }

    [Fact]
    public void LongStallExpiresWithoutReappearingOnRollback()
    {
        var session = new SsAlertSession();
        session.TryTrigger(TimeSpan.Zero, Seconds(30));
        session.Update(Seconds(1000));
        session.Update(TimeSpan.Zero);
        Assert.False(session.Snapshot.IsVisible);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.9999999)]
    [InlineData(30.0000001)]
    [InlineData(31)]
    public void InvalidDurationThrowsEvenDuringCooldownWithoutChangingState(double seconds)
    {
        var session = new SsAlertSession();
        session.TryTrigger(TimeSpan.Zero, Seconds(5));
        var snapshot = session.Snapshot;
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.TryTrigger(Seconds(1), Seconds(seconds)));
        Assert.Equal("duration", exception.ParamName);
        Assert.Same(snapshot, session.Snapshot);
        session.Update(Seconds(5));
        Assert.False(session.Snapshot.IsVisible);
        Assert.True(session.TryTrigger(Seconds(10), Seconds(5)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(30)]
    public void AcceptsInclusiveDurationRangeAndFractionalRealSeconds(double seconds)
    {
        var session = new SsAlertSession();
        var duration = Seconds(seconds);
        Assert.True(session.TryTrigger(TimeSpan.Zero, duration));
        session.Update(duration - TimeSpan.FromTicks(1));
        Assert.True(session.Snapshot.IsVisible);
        session.Update(duration);
        Assert.False(session.Snapshot.IsVisible);
    }

    [Fact]
    public void SnapshotsRemainUnchangedAfterStateTransitions()
    {
        var session = new SsAlertSession();
        var hidden = session.Snapshot;
        session.TryTrigger(TimeSpan.Zero, Seconds(5));
        var visible = session.Snapshot;
        session.Clear();
        Assert.False(hidden.IsVisible);
        Assert.Equal(string.Empty, hidden.Text);
        Assert.True(visible.IsVisible);
        Assert.Equal("SS出现！", visible.Text);
        Assert.False(session.Snapshot.IsVisible);
    }

    [Fact]
    public void ConcurrentTriggersWithDifferentDurationsHaveOneWinnerAndImmutableSnapshots()
    {
        var session = new SsAlertSession();
        var accepted = 0;
        var acceptedDuration = 0;
        var snapshots = new System.Collections.Concurrent.ConcurrentBag<SsAlertSnapshot>();
        Parallel.For(1, 31, duration =>
        {
            if (session.TryTrigger(TimeSpan.Zero, Seconds(duration)))
            {
                Interlocked.Increment(ref accepted);
                Interlocked.Exchange(ref acceptedDuration, duration);
            }
            snapshots.Add(session.Snapshot);
        });
        Assert.Equal(1, accepted);
        Assert.All(snapshots, snapshot =>
        {
            Assert.True(snapshot.IsVisible);
            Assert.Equal("SS出现！", snapshot.Text);
        });
        session.Update(Seconds(acceptedDuration) - TimeSpan.FromTicks(1));
        Assert.True(session.Snapshot.IsVisible);
        session.Update(Seconds(acceptedDuration));
        Assert.False(session.Snapshot.IsVisible);
        Assert.All(snapshots, snapshot => Assert.True(snapshot.IsVisible));
    }

    [Fact]
    public void ConcurrentClearUpdateTriggerAndSnapshotAccessRemainConsistent()
    {
        var session = new SsAlertSession();
        Parallel.For(0, 1000, index =>
        {
            switch (index % 4)
            {
                case 0: session.TryTrigger(Seconds(index), Seconds(index % 30 + 1)); break;
                case 1: session.Update(Seconds(index)); break;
                case 2: session.Clear(); break;
                default:
                    var snapshot = session.Snapshot;
                    Assert.Equal(snapshot.IsVisible ? "SS出现！" : string.Empty, snapshot.Text);
                    break;
            }
        });
        session.Clear();
        Assert.True(session.TryTrigger(TimeSpan.Zero, Seconds(5)));
        session.Update(Seconds(5));
        Assert.False(session.Snapshot.IsVisible);
    }
}
