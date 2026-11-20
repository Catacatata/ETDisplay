using Xunit;

namespace ETDisplay.Core.Tests;

public sealed class SpeechPriorityTests
{
    [Fact]
    public void CountdownCannotInterruptPendingProcessingOrPlayingAnnouncement()
    {
        Assert.False(SpeechPriority.CanQueue(SpeechKind.Countdown, SpeechKind.EtTime, null, null));
        Assert.False(SpeechPriority.CanQueue(SpeechKind.Countdown, null, SpeechKind.EtTime, null));
        Assert.False(SpeechPriority.CanQueue(SpeechKind.Countdown, null, null, SpeechKind.EtTime));
        Assert.False(SpeechPriority.CanQueue(SpeechKind.Countdown, null, null, SpeechKind.Test));
    }

    [Fact]
    public void NewCountdownReplacesOldNumbersWithoutBacklog()
    {
        Assert.True(SpeechPriority.CanQueue(SpeechKind.Countdown, SpeechKind.Countdown, SpeechKind.Countdown, SpeechKind.Countdown));
        Assert.True(SpeechPriority.CanQueue(SpeechKind.Countdown, null, null, null));
    }

    [Fact]
    public void EtAnnouncementAndExplicitTestCanPreemptCountdown()
    {
        Assert.True(SpeechPriority.CanQueue(SpeechKind.EtTime, SpeechKind.Countdown, null, SpeechKind.Countdown));
        Assert.True(SpeechPriority.CanQueue(SpeechKind.Test, null, null, SpeechKind.EtTime));
    }
}
