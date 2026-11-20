namespace ETDisplay.Core;

public static class SanctuaryMessage
{
    /// <summary>ACT 0039 is the SystemMessage path. Never match this text in player/echo channels.</summary>
    public static bool IsEntry(ChatChannel channel, string? body)
    {
        if (channel != ChatChannel.SystemMessage || string.IsNullOrWhiteSpace(body)) return false;
        var text = body.Trim();
        return text.Contains("进入了休息区", StringComparison.Ordinal) ||
               text.Equals("You have entered a sanctuary.", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("You have entered a sanctuary", StringComparison.OrdinalIgnoreCase);
    }
}
