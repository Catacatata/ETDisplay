namespace ETDisplay.Core;

/// <summary>Validated ET time of day. No local timezone or countdown semantics.</summary>
public readonly record struct EorzeaTime
{
    public int Hour { get; }
    public int Minute { get; }

    public EorzeaTime(int hour, int minute)
    {
        if (hour is < 0 or > 23) throw new ArgumentOutOfRangeException(nameof(hour));
        if (minute is < 0 or > 59) throw new ArgumentOutOfRangeException(nameof(minute));
        Hour = hour;
        Minute = minute;
    }

    public override string ToString() => FormattableString.Invariant($"{Hour:D2}:{Minute:D2}");
}
