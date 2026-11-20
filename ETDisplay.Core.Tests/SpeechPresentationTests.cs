using Xunit;

namespace ETDisplay.Core.Tests;

public sealed class SpeechPresentationTests
{
    [Theory]
    [InlineData(SpeechReadiness.Idle, "待命")]
    [InlineData(SpeechReadiness.Initializing, "初始化中")]
    [InlineData(SpeechReadiness.Ready, "可用")]
    [InlineData(SpeechReadiness.Unavailable, "不可用")]
    public void StatusUsesShortUiLabels(SpeechReadiness readiness, string expected)
    {
        Assert.Equal(expected, new SpeechPresentation(readiness, "Microsoft Test").StatusText);
    }

    [Fact]
    public void UnavailableDoesNotExposeVoiceOrBackendErrors()
    {
        var presentation = new SpeechPresentation(SpeechReadiness.Unavailable, "old voice");
        Assert.Equal("不可用", presentation.StatusText);
        Assert.Equal("—", presentation.DisplayVoice);
        Assert.DoesNotContain("安装", presentation.StatusText);
        Assert.DoesNotContain("重试", presentation.StatusText);
    }

    [Theory]
    [InlineData(SpeechReadiness.Idle)]
    [InlineData(SpeechReadiness.Initializing)]
    public void DoesNotPretendVoiceIsReadyBeforeInitialization(SpeechReadiness readiness)
    {
        Assert.Equal("—", new SpeechPresentation(readiness, "stale voice").DisplayVoice);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReadyWithMissingVoiceUsesPlaceholder(string? voice)
    {
        Assert.Equal("—", new SpeechPresentation(SpeechReadiness.Ready, voice).DisplayVoice);
    }

    [Fact]
    public void ReadyVoiceIsShownWithoutParsingDiagnosticText()
    {
        const string voice = "Microsoft Test (zh-CN)";
        Assert.Equal(voice, new SpeechPresentation(SpeechReadiness.Ready, voice).DisplayVoice);
    }
}
