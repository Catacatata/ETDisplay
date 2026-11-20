namespace ETDisplay.Core;

public static class EorzeaClock
{
    public const double SecondsPerDay = 4200;
    public const double SecondsPerMinute = SecondsPerDay / 1440;

    private static double Phase(DateTimeOffset utc)
    {
        var seconds = utc.ToUnixTimeMilliseconds() / 1000.0;
        return ((seconds % SecondsPerDay) + SecondsPerDay) % SecondsPerDay;
    }

    public static EorzeaTime At(DateTimeOffset utc)
    {
        var minute = Math.Min(1439, (int)Math.Floor(Phase(utc) / SecondsPerMinute));
        return new EorzeaTime(minute / 60, minute % 60);
    }

    /// <summary>Nearest upcoming occurrence of HH:mm. An exactly matching instant is due now.</summary>
    public static TimeSpan Until(DateTimeOffset utc, EorzeaTime target)
    {
        var targetPhase = (target.Hour * 60 + target.Minute) * SecondsPerMinute;
        var difference = targetPhase - Phase(utc);
        if (difference < -0.000001) difference += SecondsPerDay;
        return TimeSpan.FromSeconds(Math.Max(0, difference));
    }
}
