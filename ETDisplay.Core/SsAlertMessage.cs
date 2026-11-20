namespace ETDisplay.Core;

public static class SsAlertMessage
{
    public const int MaximumMessageLength = 2048;
    private const string EnglishCandidate = "has begun scouting";

    /// <summary>
    /// Candidate wording inherited from the old project, not verified against real system samples.
    /// Inspect only the body of SystemMessage events; player/echo copies are never accepted.
    /// </summary>
    public static bool IsScouting(ChatChannel channel, string? body)
    {
        if (channel != ChatChannel.SystemMessage || string.IsNullOrEmpty(body) ||
            body.Length > MaximumMessageLength) return false;
        if (body.Contains("开始了侦察活动", StringComparison.Ordinal)) return true;

        var offset = 0;
        while (offset <= body.Length - EnglishCandidate.Length)
        {
            var index = body.IndexOf(EnglishCandidate, offset, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;
            var end = index + EnglishCandidate.Length;
            if ((index == 0 || !char.IsLetter(body, index - 1)) &&
                (end == body.Length || !char.IsLetter(body, end))) return true;
            offset = end;
        }
        return false;
    }
}
