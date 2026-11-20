using Xunit;

namespace ETDisplay.Core.Tests;

public sealed class SpeechSubmissionTests
{
    [Fact]
    public void SupersededBeforeSubmissionDoesNotReachBackend()
    {
        var events = new List<string>();
        SpeechSubmission.Run(() => false, () => events.Add("submit"), () => events.Add("cancel"));
        Assert.Empty(events);
    }

    [Fact]
    public void CurrentWorkIsSubmittedWithoutCancellation()
    {
        var events = new List<string>();
        SpeechSubmission.Run(() => true, () => events.Add("submit"), () => events.Add("cancel"));
        Assert.Equal(["submit"], events);
    }

    [Fact]
    public void CancellationBetweenCheckAndSubmitIsHandledImmediatelyAfterSubmit()
    {
        var events = new List<string>();
        var checks = 0;
        // Simulate another thread completing Cancel just after the first successful check.
        SpeechSubmission.Run(() => ++checks == 1, () => events.Add("submit"), () => events.Add("cancel"));
        Assert.Equal(["submit", "cancel"], events);
    }

    [Fact]
    public void BackendErrorPropagatesToServiceDegradationPath()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SpeechSubmission.Run(() => true, () => throw new InvalidOperationException("device"), () => { }));
    }
}
