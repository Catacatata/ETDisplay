using Xunit;

namespace ETDisplay.Core.Tests;

public sealed class CountdownTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    [Fact]
    public void ClockUses70MinuteDayAndWrapsAtMidnight()
    {
        Assert.Equal(new EorzeaTime(0, 0), EorzeaClock.At(Epoch));
        Assert.Equal(new EorzeaTime(12, 0), EorzeaClock.At(Epoch.AddSeconds(2100)));
        Assert.Equal(new EorzeaTime(0, 0), EorzeaClock.At(Epoch.AddSeconds(4200)));
        Assert.Equal(new EorzeaTime(23, 59), EorzeaClock.At(Epoch.AddMilliseconds(-1)));
        var remaining = EorzeaClock.Until(Epoch.AddSeconds(4200 - EorzeaClock.SecondsPerMinute), new(0, 1));
        Assert.InRange(remaining.TotalSeconds, 5.83, 5.84);
    }

    [Fact]
    public void ClockIgnoresTimezoneAndCanRepresentAlreadyPassedNextOccurrence()
    {
        var utc = Epoch.AddSeconds(10);
        Assert.Equal(EorzeaClock.At(utc), EorzeaClock.At(utc.ToOffset(TimeSpan.FromHours(8))));
        Assert.Equal(4190, EorzeaClock.Until(utc, new(0, 0)).TotalSeconds, 3);
        Assert.Equal(TimeSpan.Zero, EorzeaClock.Until(Epoch, new(0, 0)));
    }

    [Fact]
    public void RepeatedTargetNeverResetsRunningOrReachedCountdown()
    {
        var timer = new CountdownSession();
        Assert.True(timer.Start(new(0, 1), Epoch, TimeSpan.Zero));
        Assert.False(timer.Start(new(0, 1), Epoch.AddSeconds(1), TimeSpan.FromSeconds(1)));
        timer.Update(TimeSpan.FromSeconds(3), true);
        Assert.Equal(CountdownPhase.Reached, timer.Snapshot.Phase);
        Assert.False(timer.Start(new(0, 1), Epoch.AddSeconds(3), TimeSpan.FromSeconds(3)));
        Assert.Equal("剩余0秒", timer.Snapshot.Text);
    }

    [Fact]
    public void SkippingDueFrameDoesNotWrapAndZeroDoesNotReappearAfterHiding()
    {
        var timer = new CountdownSession();
        timer.Start(new(0, 1), Epoch, TimeSpan.Zero);
        timer.Update(TimeSpan.FromSeconds(2.8), true);
        timer.Update(TimeSpan.FromSeconds(4), true);
        Assert.Equal(CountdownPhase.Reached, timer.Snapshot.Phase);
        timer.Update(TimeSpan.FromSeconds(13), true);
        Assert.Equal(CountdownPhase.Expired, timer.Snapshot.Phase);
        Assert.Equal(string.Empty, timer.Snapshot.Text);
        timer.Update(TimeSpan.FromSeconds(300), true);
        Assert.Equal(string.Empty, timer.Snapshot.Text);
    }

    [Fact]
    public void LongStallPastZeroDisplayWindowExpiresImmediately()
    {
        var timer = new CountdownSession();
        timer.Start(new(0, 1), Epoch, TimeSpan.Zero);
        Assert.Null(timer.Update(TimeSpan.FromSeconds(20), true));
        Assert.Equal(CountdownPhase.Expired, timer.Snapshot.Phase);
    }

    [Fact]
    public void ReadoutHasNoDuplicatesOrBackfillAfterStall()
    {
        var timer = new CountdownSession();
        timer.Start(new(0, 6), Epoch, TimeSpan.Zero); // 17.5 real seconds.
        Assert.Null(timer.Update(TimeSpan.Zero, true));
        Assert.Equal(10, timer.Update(TimeSpan.FromSeconds(7.5), true));
        Assert.Null(timer.Update(TimeSpan.FromSeconds(7.6), true));
        Assert.Equal(6, timer.Update(TimeSpan.FromSeconds(12), true));
        Assert.Null(timer.Update(TimeSpan.FromSeconds(12.1), true));
        Assert.Null(timer.Update(TimeSpan.FromSeconds(17.5), true));
    }

    [Fact]
    public void MutingReadoutDoesNotStopProgress()
    {
        var timer = new CountdownSession();
        timer.Start(new(0, 6), Epoch, TimeSpan.Zero);
        Assert.Null(timer.Update(TimeSpan.FromSeconds(10), false));
        Assert.Equal("剩余8秒", timer.Snapshot.Text);
        Assert.Equal(8, timer.Update(TimeSpan.FromSeconds(10.1), true));
    }

    [Fact]
    public void CalibrationIsAppliedOnceAndPositiveMeansLater()
    {
        var timer = new CountdownSession();
        timer.Start(new(0, 6), Epoch, TimeSpan.Zero, 2);
        Assert.Equal(19.5, timer.Snapshot.RemainingSeconds, 4);
        timer.AdjustCalibration(4, TimeSpan.FromSeconds(1));
        Assert.Equal(20.5, timer.Snapshot.RemainingSeconds, 4);
        timer.AdjustCalibration(4, TimeSpan.FromSeconds(1));
        Assert.Equal(20.5, timer.Snapshot.RemainingSeconds, 4);
    }

    [Fact]
    public void NegativeCalibrationCanReachZeroWithoutMovingToNextDay()
    {
        var timer = new CountdownSession();
        timer.Start(new(0, 1), Epoch, TimeSpan.Zero, -4);
        Assert.Equal(CountdownPhase.Reached, timer.Snapshot.Phase);
        Assert.Equal("剩余0秒", timer.Snapshot.Text);
        timer.AdjustCalibration(10, TimeSpan.FromSeconds(1));
        Assert.Equal(CountdownPhase.Reached, timer.Snapshot.Phase);
    }

    [Fact]
    public void ClearOrNewOccurrenceAllowsSameHhmmAgain()
    {
        var timer = new CountdownSession();
        timer.Start(new(0, 1), Epoch, TimeSpan.Zero);
        timer.Update(TimeSpan.FromSeconds(14), true);
        Assert.True(timer.Start(new(0, 1), Epoch.AddSeconds(4200), TimeSpan.FromSeconds(4200)));
        Assert.InRange(timer.Snapshot.RemainingSeconds, 2.91, 2.92);
        timer.Clear();
        Assert.Equal(CountdownPhase.Idle, timer.Snapshot.Phase);
        Assert.True(timer.Start(new(0, 1), Epoch, TimeSpan.Zero));
    }

    [Fact]
    public void WallClockIsNotReadAgainAndElapsedRollbackCannotResurrect()
    {
        var timer = new CountdownSession();
        timer.Start(new(0, 6), Epoch, TimeSpan.FromSeconds(100));
        timer.Update(TimeSpan.FromSeconds(110), false);
        Assert.Equal(7.5, timer.Snapshot.RemainingSeconds, 4);
        timer.Update(TimeSpan.FromSeconds(90), false);
        Assert.Equal(7.5, timer.Snapshot.RemainingSeconds, 4);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidCalibration(double value)
    {
        var timer = new CountdownSession();
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Start(new(0, 1), Epoch, TimeSpan.Zero, value));
    }

    [Theory]
    [InlineData(3601, "剩余1时0分1秒")]
    [InlineData(61, "剩余1分1秒")]
    [InlineData(0, "剩余0秒")]
    public void FormatsRealTimeRemaining(int seconds, string expected)
    {
        Assert.Equal(expected, new CountdownSnapshot(new(0, 0), CountdownPhase.Running, seconds).Text);
    }
}
