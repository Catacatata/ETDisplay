using System.Globalization;

namespace ETDisplay.Core;

public enum CountdownPhase { Idle, Running, Reached, Expired }
public readonly record struct CountdownTick(int? Readout, bool CancelPendingReadout);

public sealed record CountdownSnapshot(EorzeaTime? Target, CountdownPhase Phase, double RemainingSeconds)
{
    public bool IsVisible => Phase is CountdownPhase.Running or CountdownPhase.Reached;
    public string Text
    {
        get
        {
            if (!IsVisible) return string.Empty;
            var seconds = Math.Max(0, (int)Math.Ceiling(RemainingSeconds));
            if (seconds >= 3600) return string.Create(CultureInfo.InvariantCulture, $"剩余{seconds / 3600}时{seconds % 3600 / 60}分{seconds % 60}秒");
            if (seconds >= 60) return string.Create(CultureInfo.InvariantCulture, $"剩余{seconds / 60}分{seconds % 60}秒");
            return string.Create(CultureInfo.InvariantCulture, $"剩余{seconds}秒");
        }
    }
}

/// <summary>
/// UTC is read ONLY when accepting a new target. Progress uses a monotonic elapsed time.
/// Duplicate active targets do not change deadlines or replay seconds. Reached/Expired never wrap.
/// </summary>
public sealed class CountdownSession
{
    private readonly object gate = new();
    private CountdownSnapshot state = new(null, CountdownPhase.Idle, 0);
    private TimeSpan deadline;
    private TimeSpan lastObserved;
    private double calibration;
    private int lastSpokenSecond = -1;
    public static readonly TimeSpan ZeroDisplayDuration = TimeSpan.FromSeconds(10);

    public CountdownSnapshot Snapshot { get { lock (gate) return state; } }

    public bool Start(EorzeaTime target, DateTimeOffset utcNow, TimeSpan elapsed, double offsetSeconds = 0)
    {
        if (!double.IsFinite(offsetSeconds)) throw new ArgumentOutOfRangeException(nameof(offsetSeconds));
        lock (gate)
        {
            Evaluate(elapsed, announce: false);
            if (state.Target == target && state.Phase is CountdownPhase.Running or CountdownPhase.Reached) return false;
            calibration = offsetSeconds;
            deadline = elapsed + EorzeaClock.Until(utcNow, target) + TimeSpan.FromSeconds(offsetSeconds);
            lastObserved = elapsed;
            lastSpokenSecond = -1;
            state = new(target, CountdownPhase.Running, 0);
            Evaluate(elapsed, announce: false);
            return true;
        }
    }

    public int? Update(TimeSpan elapsed, bool announce) => Tick(elapsed, announce).Readout;

    public CountdownTick Tick(TimeSpan elapsed, bool announce)
    {
        lock (gate)
        {
            var number = Evaluate(elapsed, announce);
            // This remains true even if Start/AdjustCalibration reached zero before this tick.
            return new(number, state.Phase != CountdownPhase.Running);
        }
    }

    private int? Evaluate(TimeSpan elapsed, bool announce)
    {
        if (state.Phase is CountdownPhase.Idle or CountdownPhase.Expired) return null;
        if (elapsed < lastObserved) elapsed = lastObserved;
        lastObserved = elapsed;
        var remaining = (deadline - elapsed).TotalSeconds;
        if (remaining <= 0 || state.Phase == CountdownPhase.Reached)
        {
            state = state with
            {
                Phase = elapsed - deadline >= ZeroDisplayDuration ? CountdownPhase.Expired : CountdownPhase.Reached,
                RemainingSeconds = 0,
            };
            return null;
        }
        state = state with { Phase = CountdownPhase.Running, RemainingSeconds = remaining };
        var second = (int)Math.Ceiling(remaining);
        if (announce && second is >= 1 and <= 10 && second != lastSpokenSecond)
        {
            // Do not backfill skipped seconds after a stalled frame or blocked speech backend.
            lastSpokenSecond = second;
            return second;
        }
        return null;
    }

    public void AdjustCalibration(double offsetSeconds, TimeSpan elapsed)
    {
        if (!double.IsFinite(offsetSeconds)) throw new ArgumentOutOfRangeException(nameof(offsetSeconds));
        lock (gate)
        {
            // First observe the unadjusted deadline so a late UI edit cannot resurrect a due timer.
            Evaluate(elapsed, announce: false);
            if (state.Phase == CountdownPhase.Running)
            {
                deadline += TimeSpan.FromSeconds(offsetSeconds - calibration);
                calibration = offsetSeconds;
                Evaluate(elapsed, announce: false);
            }
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            state = new(null, CountdownPhase.Idle, 0);
            lastSpokenSecond = -1;
        }
    }
}
