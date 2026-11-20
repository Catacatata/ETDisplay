namespace ETDisplay.Core;

public static class SpeechCompletionPolicy
{
    /// <summary>
    /// System.Speech can set Cancelled=true for ANY prompt exception, not just explicit cancellation.
    /// Classify Error instead of using that flag to hide genuine device/synthesis failures.
    /// </summary>
    public static bool IsBackendFailure(bool cancelled, Exception? error)
    {
        _ = cancelled; // Deliberately not authoritative in System.Speech 10.x.
        return error is not null and not OperationCanceledException;
    }
}
