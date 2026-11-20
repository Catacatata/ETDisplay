namespace ETDisplay.Core;

public enum SpeechKind { Countdown, EtTime, Test, SsAlert }

public static class SpeechPriority
{
    private static int Rank(SpeechKind kind) => kind switch
    {
        SpeechKind.Countdown => 0,
        SpeechKind.EtTime or SpeechKind.Test => 1,
        SpeechKind.SsAlert => 2,
        _ => -1,
    };

    public static bool IsHigh(SpeechKind? kind) => kind.HasValue && Rank(kind.Value) > 0;
    public static bool IsSsAlert(SpeechKind? kind) => kind == SpeechKind.SsAlert;

    public static bool CanCancelCountdown(SpeechKind? pending, SpeechKind? processing, SpeechKind? active) =>
        !IsHigh(pending) && !IsHigh(processing) && !IsHigh(active) &&
        (pending == SpeechKind.Countdown || processing == SpeechKind.Countdown || active == SpeechKind.Countdown);

    public static bool CanCancelSsAlert(SpeechKind? pending, SpeechKind? processing, SpeechKind? active) =>
        IsSsAlert(pending) || IsSsAlert(processing) || IsSsAlert(active);

    public static bool CanCancelBelowSs(SpeechKind? pending, SpeechKind? processing, SpeechKind? active) =>
        !IsSsAlert(pending) && !IsSsAlert(processing) && !IsSsAlert(active);

    public static bool CanQueue(SpeechKind candidate, SpeechKind? pending, SpeechKind? processing, SpeechKind? active)
    {
        var priority = Rank(candidate);
        if (priority < 0) return false;
        return !Higher(pending) && !Higher(processing) && !Higher(active);
        bool Higher(SpeechKind? existing) => existing.HasValue && Rank(existing.Value) > priority;
    }
}
