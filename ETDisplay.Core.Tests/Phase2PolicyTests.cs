using Xunit;

namespace ETDisplay.Core.Tests;

public sealed class Phase2PolicyTests
{
    [Fact]
    public void SameTargetIsSuppressedUntilCooldownBoundary()
    {
        var policy = new EtSpeechPolicy();
        var target = new EorzeaTime(12, 30);
        Assert.True(policy.TryAnnounce(target, TimeSpan.Zero, true));
        Assert.False(policy.TryAnnounce(target, TimeSpan.FromSeconds(9.999), true));
        Assert.True(policy.TryAnnounce(target, TimeSpan.FromSeconds(10), true));
    }

    [Fact]
    public void DuplicateDoesNotExtendCooldown()
    {
        var policy = new EtSpeechPolicy();
        var target = new EorzeaTime(12, 30);
        policy.TryAnnounce(target, TimeSpan.Zero, true);
        Assert.False(policy.TryAnnounce(target, TimeSpan.FromSeconds(9), true));
        Assert.True(policy.TryAnnounce(target, TimeSpan.FromSeconds(10), true));
    }

    [Fact]
    public void NewTargetIsImmediateAndResetAllowsSameTime()
    {
        var policy = new EtSpeechPolicy();
        Assert.True(policy.TryAnnounce(new(12, 30), TimeSpan.Zero, true));
        Assert.True(policy.TryAnnounce(new(12, 31), TimeSpan.FromSeconds(1), true));
        policy.Reset();
        Assert.True(policy.TryAnnounce(new(12, 31), TimeSpan.FromSeconds(2), true));
    }

    [Fact]
    public void DisabledSpeechDoesNotConsumeAnnouncement()
    {
        var policy = new EtSpeechPolicy();
        var target = new EorzeaTime(0, 0);
        Assert.False(policy.TryAnnounce(target, TimeSpan.Zero, false));
        Assert.True(policy.TryAnnounce(target, TimeSpan.Zero, true));
    }

    [Fact]
    public void ClockRollbackDoesNotPermanentlyMute()
    {
        var policy = new EtSpeechPolicy();
        var target = new EorzeaTime(23, 59);
        Assert.True(policy.TryAnnounce(target, TimeSpan.FromSeconds(20), true));
        Assert.True(policy.TryAnnounce(target, TimeSpan.FromSeconds(1), true));
    }

    [Theory]
    [InlineData("进入了休息区")]
    [InlineData("你进入了休息区。")]
    [InlineData("  进入了休息区。  ")]
    [InlineData("You have entered a sanctuary.")]
    [InlineData("You have entered a sanctuary")]
    public void ActualSystemEntryTextMatches(string body)
    {
        Assert.True(SanctuaryMessage.IsEntry(ChatChannel.SystemMessage, body));
    }

    [Theory]
    [InlineData(ChatChannel.Say)]
    [InlineData(ChatChannel.Yell)]
    [InlineData(ChatChannel.Shout)]
    [InlineData(ChatChannel.Echo)]
    [InlineData(ChatChannel.Other)]
    public void PlayerMessagesCannotTriggerSanctuaryClear(ChatChannel channel)
    {
        Assert.False(SanctuaryMessage.IsEntry(channel, "进入了休息区"));
        Assert.False(SanctuaryMessage.IsEntry(channel, "You have entered a sanctuary."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("离开了休息区")]
    [InlineData("You have left the sanctuary.")]
    [InlineData("ET1230")]
    public void OtherSystemTextDoesNotClear(string? body)
    {
        Assert.False(SanctuaryMessage.IsEntry(ChatChannel.SystemMessage, body));
    }

    [Fact]
    public void SystemMessageCannotSetEtTarget()
    {
        var session = new DisplaySession();
        Assert.False(session.Receive(ChatChannel.SystemMessage, "ET1230", out _));
        Assert.True(session.Receive(ChatChannel.Echo, "ET1230", out var target));
        Assert.Equal(new EorzeaTime(12, 30), target);
    }

    [Theory]
    [InlineData(0, 0, "艾欧泽亚时间，0点0分", "Eorzea time, 0:00")]
    [InlineData(12, 34, "艾欧泽亚时间，12点34分", "Eorzea time, 12:34")]
    [InlineData(23, 59, "艾欧泽亚时间，23点59分", "Eorzea time, 23:59")]
    public void SpeechTextMatchesInstalledVoiceLanguage(int hour, int minute, string chinese, string english)
    {
        var time = new EorzeaTime(hour, minute);
        Assert.Equal(chinese, SpeechText.ForTime(time, true));
        Assert.Equal(english, SpeechText.ForTime(time, false));
    }

    [Fact]
    public void ClearPolicyResetsBothDisplayAndAnnouncement()
    {
        var session = new DisplaySession();
        var policy = new EtSpeechPolicy();
        session.Receive(ChatChannel.Echo, "ET1230", out var target);
        Assert.True(policy.TryAnnounce(target, TimeSpan.Zero, true));
        Assert.True(SanctuaryMessage.IsEntry(ChatChannel.SystemMessage, "进入了休息区"));
        session.Clear();
        policy.Reset();
        Assert.False(session.Snapshot.IsVisible);
        Assert.True(session.Receive(ChatChannel.Echo, "ET1230", out target));
        Assert.True(policy.TryAnnounce(target, TimeSpan.FromSeconds(1), true));
    }
}
