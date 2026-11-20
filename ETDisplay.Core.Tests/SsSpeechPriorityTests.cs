using Xunit;

namespace ETDisplay.Core.Tests;

public sealed class SsSpeechPriorityTests
{
    [Fact]
    public void SsPreemptsAnyStageOfLowerPrioritySpeech()
    {
        Assert.True(SpeechPriority.CanQueue(SpeechKind.SsAlert, SpeechKind.EtTime, SpeechKind.Test, SpeechKind.Countdown));
        Assert.True(SpeechPriority.CanQueue(SpeechKind.SsAlert, SpeechKind.SsAlert, SpeechKind.SsAlert, SpeechKind.SsAlert));
    }

    [Theory]
    [InlineData(SpeechKind.Countdown)]
    [InlineData(SpeechKind.EtTime)]
    [InlineData(SpeechKind.Test)]
    public void LowerPriorityCannotInterruptPendingProcessingOrPlayingSs(SpeechKind kind)
    {
        Assert.False(SpeechPriority.CanQueue(kind, SpeechKind.SsAlert, null, null));
        Assert.False(SpeechPriority.CanQueue(kind, null, SpeechKind.SsAlert, null));
        Assert.False(SpeechPriority.CanQueue(kind, null, null, SpeechKind.SsAlert));
    }

    [Fact]
    public void ExhaustiveQueueMatrixUsesMaximumPriorityOfAllThreeStages()
    {
        SpeechKind?[] options = [null, SpeechKind.Countdown, SpeechKind.EtTime, SpeechKind.Test, SpeechKind.SsAlert];
        static int Level(SpeechKind? kind) => kind switch { null => -1, SpeechKind.Countdown => 0, SpeechKind.SsAlert => 2, _ => 1 };
        foreach (var candidate in Enum.GetValues<SpeechKind>())
            foreach (var pending in options)
                foreach (var processing in options)
                    foreach (var active in options)
                        Assert.Equal(Level(candidate) >= Math.Max(Level(pending), Math.Max(Level(processing), Level(active))),
                            SpeechPriority.CanQueue(candidate, pending, processing, active));
    }

    [Fact]
    public void CountdownExpiryAndSilentDisplayTestCannotCancelSs()
    {
        Assert.False(SpeechPriority.CanCancelCountdown(SpeechKind.SsAlert, SpeechKind.Countdown, null));
        Assert.False(SpeechPriority.CanCancelCountdown(null, null, SpeechKind.SsAlert));
        Assert.False(SpeechPriority.CanCancelBelowSs(SpeechKind.SsAlert, null, null));
        Assert.False(SpeechPriority.CanCancelBelowSs(null, SpeechKind.SsAlert, null));
        Assert.False(SpeechPriority.CanCancelBelowSs(null, null, SpeechKind.SsAlert));
        Assert.True(SpeechPriority.CanCancelBelowSs(SpeechKind.Test, SpeechKind.EtTime, SpeechKind.Countdown));
    }

    [Fact]
    public void SsOnlyCancellationHasNoEffectWhenNoSsIsInFlight()
    {
        Assert.False(SpeechPriority.CanCancelSsAlert(SpeechKind.EtTime, SpeechKind.Test, SpeechKind.Countdown));
        Assert.False(SpeechPriority.CanCancelSsAlert(null, null, null));
        Assert.True(SpeechPriority.CanCancelSsAlert(SpeechKind.SsAlert, null, null));
        Assert.True(SpeechPriority.CanCancelSsAlert(null, SpeechKind.SsAlert, null));
        Assert.True(SpeechPriority.CanCancelSsAlert(null, null, SpeechKind.SsAlert));
    }

    [Fact]
    public void CancellingQueuedSsInvalidatesAlreadyCapturedSubmission()
    {
        var generation = 5;
        var captured = generation;
        if (SpeechPriority.CanCancelSsAlert(null, SpeechKind.SsAlert, null)) generation++;
        var submitted = false;
        SpeechSubmission.Run(() => generation == captured, () => submitted = true, () => { });
        Assert.False(submitted);
    }

    [Fact]
    public void NewSsInvalidatesOlderEtBeforeSubmit()
    {
        var generation = 1;
        var olderEtTicket = generation;
        if (SpeechPriority.CanQueue(SpeechKind.SsAlert, null, SpeechKind.EtTime, null)) generation++;
        var submitted = false;
        SpeechSubmission.Run(() => generation == olderEtTicket, () => submitted = true, () => { });
        Assert.False(submitted);
    }

    [Fact]
    public void UsesLanguageAppropriateSsSpeechAndRejectsUnknownRequestKinds()
    {
        Assert.Equal("SS出现", SpeechText.ForSsAlert(true));
        Assert.Equal("S S has appeared", SpeechText.ForSsAlert(false));
        Assert.False(SpeechPriority.CanQueue((SpeechKind)99, null, null, null));
    }
}
