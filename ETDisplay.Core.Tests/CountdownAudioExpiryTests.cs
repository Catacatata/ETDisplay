using Xunit;

namespace ETDisplay.Core.Tests;

public sealed class CountdownAudioExpiryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DueTickInvalidatesDelayedNumberEvenWhenDuplicateStartReachedZeroFirst(bool duplicateFirst)
    {
        var timer = new CountdownSession();
        var epoch = DateTimeOffset.UnixEpoch;
        timer.Start(new(0, 1), epoch, TimeSpan.Zero);
        Assert.Equal(1, timer.Tick(TimeSpan.FromSeconds(2.5), true).Readout);
        var generation = 0;
        var queuedGeneration = generation;
        if (duplicateFirst)
            Assert.False(timer.Start(new(0, 1), epoch.AddSeconds(3), TimeSpan.FromSeconds(3)));
        var due = timer.Tick(TimeSpan.FromSeconds(3), true);
        Assert.True(due.CancelPendingReadout);
        if (due.CancelPendingReadout) generation++;
        var submitted = false;
        SpeechSubmission.Run(() => queuedGeneration == generation, () => submitted = true, () => { });
        Assert.False(submitted);
        Assert.Equal(CountdownPhase.Reached, timer.Snapshot.Phase);
    }

    [Fact]
    public void CountdownExpiryCannotCancelAnnouncementOrTest()
    {
        Assert.False(SpeechPriority.CanCancelCountdown(null, null, SpeechKind.EtTime));
        Assert.False(SpeechPriority.CanCancelCountdown(SpeechKind.EtTime, SpeechKind.Countdown, SpeechKind.Countdown));
        Assert.False(SpeechPriority.CanCancelCountdown(null, SpeechKind.Test, SpeechKind.Countdown));
        Assert.True(SpeechPriority.CanCancelCountdown(SpeechKind.Countdown, null, null));
        Assert.True(SpeechPriority.CanCancelCountdown(null, SpeechKind.Countdown, null));
        Assert.True(SpeechPriority.CanCancelCountdown(null, null, SpeechKind.Countdown));
        Assert.False(SpeechPriority.CanCancelCountdown(null, null, null));
    }

    [Fact]
    public void LateCalibrationCannotResurrectATimerBeforeItsNextFrameworkTick()
    {
        var timer = new CountdownSession();
        timer.Start(new(0, 1), DateTimeOffset.UnixEpoch, TimeSpan.Zero);
        timer.AdjustCalibration(20, TimeSpan.FromSeconds(3));
        Assert.Equal(CountdownPhase.Reached, timer.Snapshot.Phase);
        Assert.True(timer.Tick(TimeSpan.FromSeconds(3), true).CancelPendingReadout);
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(10, "10")]
    public void CountdownSpeechContainsOnlyTheCurrentNumber(int number, string expected)
    {
        Assert.Equal(expected, SpeechText.ForCountdown(number));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void InvalidReadoutNeverFormatsAsSpeech(int number)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpeechText.ForCountdown(number));
    }
}
