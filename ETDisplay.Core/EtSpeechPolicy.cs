namespace ETDisplay.Core;

/// <summary>Short-window duplicate suppression; accepts a monotonic elapsed time, not wall-clock time.</summary>
public sealed class EtSpeechPolicy
{
    public static readonly TimeSpan DuplicateCooldown = TimeSpan.FromSeconds(10);
    private readonly object gate = new();
    private EorzeaTime? lastTarget;
    private TimeSpan lastAnnouncement;

    public bool TryAnnounce(EorzeaTime target, TimeSpan elapsed, bool enabled)
    {
        if (!enabled) return false;
        lock (gate)
        {
            if (lastTarget == target && elapsed >= lastAnnouncement &&
                elapsed - lastAnnouncement < DuplicateCooldown) return false;
            lastTarget = target;
            lastAnnouncement = elapsed;
            return true;
        }
    }

    public void Reset()
    {
        lock (gate) lastTarget = null;
    }
}
