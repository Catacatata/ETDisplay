namespace ETDisplay.Core;

public sealed record ChatProbeSample(
    ushort LogKind, int OriginalLength, int CurrentLength,
    bool ProbeMarkerFound, bool OriginalParsed, bool CurrentParsed, bool Accepted,
    bool SsTextMatched = false, bool SsAccepted = false);

/// <summary>Opt-in, bounded, in-memory metadata only. Never retains a message body or sender.</summary>
public sealed class ChatProbe
{
    public const string Marker = "ETD_ECHO_PROBE";
    public const int Capacity = 16;
    private readonly object gate = new();
    private readonly Queue<ChatProbeSample> samples = new();
    private DateTimeOffset expiresAt = DateTimeOffset.MinValue;

    public void Arm(DateTimeOffset now)
    {
        lock (gate)
        {
            samples.Clear();
            expiresAt = now.AddSeconds(60);
        }
    }

    public bool IsActive(DateTimeOffset now)
    {
        lock (gate) return now < expiresAt;
    }

    public void Stop()
    {
        lock (gate) expiresAt = DateTimeOffset.MinValue;
    }

    public void Observe(ushort logKind, string original, string current, bool accepted, DateTimeOffset now,
        bool ssTextMatched = false, bool ssAccepted = false)
    {
        lock (gate)
        {
            if (now >= expiresAt || original.StartsWith("[ETDisplay诊断]", StringComparison.Ordinal) ||
                current.StartsWith("[ETDisplay诊断]", StringComparison.Ordinal)) return;
            if (samples.Count == Capacity) samples.Dequeue();
            samples.Enqueue(new ChatProbeSample(logKind, original.Length, current.Length,
                original.Contains(Marker, StringComparison.Ordinal) || current.Contains(Marker, StringComparison.Ordinal),
                HuntMessageParser.TryParse(original, out _), HuntMessageParser.TryParse(current, out _), accepted, ssTextMatched, ssAccepted));
        }
    }

    public ChatProbeSample[] Snapshot()
    {
        lock (gate) return samples.ToArray();
    }
}
