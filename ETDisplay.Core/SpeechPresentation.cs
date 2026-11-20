namespace ETDisplay.Core;

public enum SpeechReadiness { Idle, Initializing, Ready, Unavailable }

/// <summary>Read-only UI data; no engine calls or exception text belong on the normal voice page.</summary>
public sealed record SpeechPresentation(SpeechReadiness Readiness, string? VoiceName)
{
    public string StatusText => Readiness switch
    {
        SpeechReadiness.Ready => "可用",
        SpeechReadiness.Initializing => "初始化中",
        SpeechReadiness.Unavailable => "不可用",
        _ => "待命",
    };

    public string DisplayVoice => Readiness == SpeechReadiness.Ready && !string.IsNullOrWhiteSpace(VoiceName)
        ? VoiceName : "—";
}
