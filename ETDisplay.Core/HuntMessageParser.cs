using System.Globalization;
using System.Text.RegularExpressions;

namespace ETDisplay.Core;

public static class HuntMessageParser
{
    public const int MaximumMessageLength = 2048;

    // Only inspect the body, never the ACT log prefix or sender name.
    // Strict separators intentionally replace ACT's unbounded \D* prose matching.
    private static readonly Regex Candidate = new(
        @"(?<![A-Za-z0-9])(?:Eorzea(?:n)?(?:[ \t]+Time)?|艾欧泽亚|艾欧|E\.T\.|E[ \t]+T|ET||)[ \t]*(?<time>[0-9]{1,2}[ \t]*[:：.][ \t]*[0-9]{2}|[0-9]{3,4})(?!\s*[\p{N}:：.])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(50));

    /// <summary>Returns the last valid marked time; malformed candidates are ignored.</summary>
    public static bool TryParse(string? text, out EorzeaTime time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumMessageLength) return false;
        var found = false;
        try
        {
            foreach (Match match in Candidate.Matches(text))
            {
                var token = match.Groups["time"].Value;
                var digits = new string(token.Where(char.IsAsciiDigit).ToArray());
                if (digits.Length is not (3 or 4)) continue;
                var hour = int.Parse(digits.AsSpan(0, digits.Length - 2), CultureInfo.InvariantCulture);
                var minute = int.Parse(digits.AsSpan(digits.Length - 2), CultureInfo.InvariantCulture);
                if (hour > 23 || minute > 59) continue;
                time = new EorzeaTime(hour, minute);
                found = true;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            time = default;
            return false;
        }
        return found;
    }
}
