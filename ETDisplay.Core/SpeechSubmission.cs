namespace ETDisplay.Core;

public static class SpeechSubmission
{
    /// <summary>
    /// Skip stale work and recheck after an asynchronous backend submission. If cancellation wins
    /// the check/submit race, request cancellation immediately on the same worker. This does not
    /// promise that hardware has emitted zero samples when a concurrent UI cancellation returns.
    /// Backend calls are deliberately made outside the caller's state lock.
    /// </summary>
    public static void Run(Func<bool> isCurrent, Action submit, Action cancelSubmitted)
    {
        if (!isCurrent()) return;
        submit();
        if (!isCurrent()) cancelSubmitted();
    }
}
