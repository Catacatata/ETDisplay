namespace ETDisplay.Core;

public sealed record SsAlertSnapshot(bool IsVisible)
{
    public string Text => IsVisible ? "SS出现！" : string.Empty;
}

/// <summary>
/// Thread-safe alert state with real-time durations and a fixed ten-second cooldown.
/// Callers supply monotonic elapsed times from the same epoch to TryTrigger and Update.
/// Backward observations are clamped; no wall clock or retained chat/UI objects are used.
/// A duration is captured only on acceptance, so configuration edits affect future events only.
/// </summary>
public sealed class SsAlertSession
{
    private readonly object gate = new();
    private SsAlertSnapshot state = new(false);
    private TimeSpan? lastTrigger;
    private TimeSpan? lastObserved;
    private TimeSpan displayDuration;
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);

    public SsAlertSnapshot Snapshot { get { lock (gate) return state; } }

    /// <summary>
    /// Accepts an event unless the last accepted event was less than ten real seconds ago.
    /// Rejected duplicates change neither the accepted duration nor the cooldown origin.
    /// </summary>
    public bool TryTrigger(TimeSpan elapsed, TimeSpan duration)
    {
        if (duration < TimeSpan.FromSeconds(1) || duration > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be between 1 and 30 real seconds.");
        lock (gate)
        {
            elapsed = Observe(elapsed);
            if (lastTrigger is { } previous && elapsed - previous < Cooldown) return false;
            lastTrigger = elapsed;
            displayDuration = duration;
            state = new(true);
            return true;
        }
    }

    public void Update(TimeSpan elapsed)
    {
        lock (gate) Observe(elapsed);
    }

    private TimeSpan Observe(TimeSpan elapsed)
    {
        if (lastObserved is { } previous && elapsed < previous) elapsed = previous;
        lastObserved = elapsed;
        if (state.IsVisible && lastTrigger is { } started && elapsed - started >= displayDuration)
            state = new(false);
        return elapsed;
    }

    /// <summary>Hides the alert and resets the cooldown and elapsed-time observations.</summary>
    public void Clear()
    {
        lock (gate)
        {
            state = new(false);
            lastTrigger = null;
            lastObserved = null;
            displayDuration = default;
        }
    }
}
