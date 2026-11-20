using Xunit;

namespace ETDisplay.Core.Tests;

public sealed class ChatProbeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public void DisabledByDefaultAndExpiresAfter60Seconds()
    {
        var probe = new ChatProbe();
        Assert.False(probe.IsActive(Now));
        probe.Observe(56, "ET1230", "ET1230", true, Now);
        Assert.Empty(probe.Snapshot());
        probe.Arm(Now);
        Assert.True(probe.IsActive(Now.AddSeconds(59)));
        Assert.False(probe.IsActive(Now.AddSeconds(60)));
        probe.Observe(56, "ET1230", "ET1230", true, Now.AddSeconds(60));
        Assert.Empty(probe.Snapshot());
    }

    [Fact]
    public void RecordsMetadataAndDifferenceBetweenOriginalAndCurrent()
    {
        var probe = new ChatProbe();
        probe.Arm(Now);
        probe.Observe(56, "", "ET1230 " + ChatProbe.Marker, false, Now);
        var sample = Assert.Single(probe.Snapshot());
        Assert.True(sample.ProbeMarkerFound);
        Assert.False(sample.OriginalParsed);
        Assert.True(sample.CurrentParsed);
        Assert.False(sample.Accepted);
        Assert.Equal(0, sample.OriginalLength);
    }

    [Fact]
    public void CapacityIsBoundedAndRearmingClearsPreviousSamples()
    {
        var probe = new ChatProbe();
        probe.Arm(Now);
        for (ushort index = 0; index < 100; index++)
            probe.Observe(index, "ET1230", "ET1230", true, Now);
        var samples = probe.Snapshot();
        Assert.Equal(ChatProbe.Capacity, samples.Length);
        Assert.Equal((ushort)84, samples[0].LogKind);
        probe.Arm(Now);
        Assert.Empty(probe.Snapshot());
    }

    [Fact]
    public void OwnDiagnosticOutputIsExcluded()
    {
        var probe = new ChatProbe();
        probe.Arm(Now);
        probe.Observe(56, "[ETDisplay诊断] ET1230", "[ETDisplay诊断] ET1230", false, Now);
        Assert.Empty(probe.Snapshot());
    }

    [Fact]
    public void SsTextMatchAndAcceptanceAreSeparateMetadata()
    {
        var probe = new ChatProbe();
        probe.Arm(Now);
        probe.Observe(56, "某怪开始了侦察活动", "某怪开始了侦察活动", false, Now, ssTextMatched: true, ssAccepted: false);
        probe.Observe(57, "某怪开始了侦察活动", "某怪开始了侦察活动", false, Now, ssTextMatched: true, ssAccepted: true);
        var samples = probe.Snapshot();
        Assert.Equal(2, samples.Length);
        Assert.True(samples[0].SsTextMatched);
        Assert.False(samples[0].SsAccepted);
        Assert.True(samples[1].SsAccepted);
    }

    [Fact]
    public void ProbeSamplesCannotRetainTextOrSenderProperties()
    {
        Assert.DoesNotContain(typeof(ChatProbeSample).GetProperties(), property => property.PropertyType == typeof(string));
    }

    [Fact]
    public void StopRetainsMetadataButStopsCapturing()
    {
        var probe = new ChatProbe();
        probe.Arm(Now);
        probe.Observe(56, "ET1230", "ET1230", true, Now);
        probe.Stop();
        Assert.False(probe.IsActive(Now));
        probe.Observe(56, "ET1300", "ET1300", true, Now);
        Assert.Single(probe.Snapshot());
    }
}
