using Xunit;

namespace ETDisplay.Core.Tests;

public sealed class SpeechCompletionPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuccessfulCompletionIsNotBackendFailure(bool cancelled)
    {
        Assert.False(SpeechCompletionPolicy.IsBackendFailure(cancelled, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitCancellationIsNotBackendFailure(bool cancelled)
    {
        Assert.False(SpeechCompletionPolicy.IsBackendFailure(cancelled, new OperationCanceledException()));
        Assert.False(SpeechCompletionPolicy.IsBackendFailure(cancelled, new TaskCanceledException()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealErrorIsNotHiddenByCancelledFlag(bool cancelled)
    {
        Assert.True(SpeechCompletionPolicy.IsBackendFailure(cancelled, new InvalidOperationException("audio device failed")));
    }
}
