namespace ETDisplay.Core;

public enum ChatChannel { Other, Shout, Say, Yell, Echo, SystemMessage }

public sealed record DisplaySnapshot(bool Enabled, EorzeaTime? Target)
{
    public bool IsVisible => Enabled && Target.HasValue;
    public string Text => Target is { } target ? $"ET {target}" : string.Empty;
}

/// <summary>
/// Small thread-safe state boundary. Chat callbacks never mutate ImGui/Window objects.
/// Clear, disable and message acceptance are serialized; no retained chat API objects.
/// </summary>
public sealed class DisplaySession(bool enabled = true)
{
    private readonly object gate = new();
    private DisplaySnapshot state = new(enabled, null);

    public DisplaySnapshot Snapshot { get { lock (gate) return state; } }

    public bool Receive(ChatChannel channel, string? body) => Receive(channel, body, out _);

    public bool Receive(ChatChannel channel, string? body, out EorzeaTime target)
    {
        target = default;
        if (channel is not (ChatChannel.Say or ChatChannel.Yell or ChatChannel.Shout or ChatChannel.Echo)) return false;
        lock (gate)
        {
            if (!state.Enabled || !HuntMessageParser.TryParse(body, out target)) return false;
            state = state with { Target = target };
            return true;
        }
    }

    public void Clear()
    {
        lock (gate) state = state with { Target = null };
    }

    public void SetEnabled(bool enabled)
    {
        lock (gate) state = new DisplaySnapshot(enabled, null);
    }
}
