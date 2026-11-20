using Xunit;

namespace ETDisplay.Core.Tests;

public sealed class DisplaySessionTests
{
    [Theory]
    [InlineData(ChatChannel.Say)]
    [InlineData(ChatChannel.Yell)]
    [InlineData(ChatChannel.Shout)]
    [InlineData(ChatChannel.Echo)]
    public void AllowedChannelsCanSetTarget(ChatChannel channel)
    {
        var session = new DisplaySession();
        Assert.False(session.Snapshot.IsVisible);
        Assert.False(session.Receive(ChatChannel.Other, "ET1230"));
        Assert.True(session.Receive(channel, "ET1230"));
        Assert.True(session.Snapshot.IsVisible);
        Assert.Equal("ET 12:30", session.Snapshot.Text);
    }

    [Theory]
    [InlineData(ChatChannel.Say)]
    [InlineData(ChatChannel.Yell)]
    [InlineData(ChatChannel.Shout)]
    [InlineData(ChatChannel.Echo)]
    public void AllowedChannelsStillRespectDisableAndTimeValidation(ChatChannel channel)
    {
        var session = new DisplaySession();
        Assert.False(session.Receive(channel, "ET24:99"));
        session.SetEnabled(false);
        Assert.False(session.Receive(channel, "ET1230"));
        Assert.False(session.Snapshot.IsVisible);
    }

    [Fact]
    public void UnknownChannelDoesNotChangeExistingTarget()
    {
        var session = new DisplaySession();
        session.Receive(ChatChannel.Echo, "ET1230");
        Assert.False(session.Receive((ChatChannel)999, "ET1300"));
        Assert.Equal("ET 12:30", session.Snapshot.Text);
    }

    [Fact]
    public void InvalidAndOtherChannelMessagesDoNotEraseTarget()
    {
        var session = new DisplaySession();
        session.Receive(ChatChannel.Shout, "ET1230");
        Assert.False(session.Receive(ChatChannel.Shout, "ET9999"));
        Assert.False(session.Receive(ChatChannel.Other, "ET1300"));
        Assert.Equal("ET 12:30", session.Snapshot.Text);
    }

    [Fact]
    public void ClearAllowsSameTimeAgain()
    {
        var session = new DisplaySession();
        session.Receive(ChatChannel.Shout, "ET1230");
        session.Clear();
        Assert.False(session.Snapshot.IsVisible);
        Assert.Equal(string.Empty, session.Snapshot.Text);
        Assert.True(session.Receive(ChatChannel.Shout, "ET1230"));
    }

    [Fact]
    public void DisableClearsAndIgnoresIncomingMessages()
    {
        var session = new DisplaySession();
        session.Receive(ChatChannel.Shout, "ET1230");
        session.SetEnabled(false);
        Assert.False(session.Receive(ChatChannel.Shout, "ET1300"));
        Assert.False(session.Snapshot.IsVisible);
        session.SetEnabled(true);
        Assert.False(session.Snapshot.IsVisible);
        Assert.True(session.Receive(ChatChannel.Shout, "ET1300"));
    }

    [Fact]
    public void InitiallyDisabledSessionIgnoresMessages()
    {
        var session = new DisplaySession(false);
        Assert.False(session.Receive(ChatChannel.Shout, "ET1230"));
        Assert.False(session.Snapshot.IsVisible);
    }

    [Fact]
    public void RepeatedTargetIsStableAndSnapshotIsImmutable()
    {
        var session = new DisplaySession();
        session.Receive(ChatChannel.Shout, "ET1230");
        var before = session.Snapshot;
        session.Receive(ChatChannel.Shout, "ET1230");
        Assert.Equal(before, session.Snapshot);
        session.Clear();
        Assert.Equal("ET 12:30", before.Text);
    }
}
