namespace ETDisplay.Core;

public static class SpeechText
{
    public static string ForTime(EorzeaTime time, bool chinese) => chinese
        ? FormattableString.Invariant($"艾欧泽亚时间，{time.Hour}点{time.Minute}分")
        : FormattableString.Invariant($"Eorzea time, {time.Hour}:{time.Minute:D2}");

    public static string Test(bool chinese) => chinese ? "ET显示，语音测试" : "ET Display, speech test";
    public static string ForSsAlert(bool chinese) => chinese ? "SS出现" : "S S has appeared";

    public static string ForCountdown(int seconds)
    {
        if (seconds is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(seconds));
        return seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
