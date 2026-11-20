using Xunit;

namespace ETDisplay.Core.Tests;

public sealed class ParserTests
{
    [Theory]
    [InlineData("ET 15:30", 15, 30)]
    [InlineData("ET15：30集合", 15, 30)]
    [InlineData("ET 15.30", 15, 30)]
    [InlineData("ET1530", 15, 30)]
    [InlineData("ET 930", 9, 30)]
    [InlineData("et 09:30", 9, 30)]
    [InlineData("Et 09:30", 9, 30)]
    [InlineData("E.T. 09:30", 9, 30)]
    [InlineData("E T 09:30", 9, 30)]
    [InlineData(" 1530", 15, 30)]
    [InlineData("15:30", 15, 30)]
    [InlineData("艾欧泽亚 15:30", 15, 30)]
    [InlineData("艾欧 15:30", 15, 30)]
    [InlineData("Eorzea Time 15:30", 15, 30)]
    [InlineData("Eorzean 15:30", 15, 30)]
    [InlineData("ET 9 : 30", 9, 30)]
    [InlineData("ET 0000", 0, 0)]
    [InlineData("ET 23:59", 23, 59)]
    [InlineData("ET1200取消，改ET1230", 12, 30)]
    [InlineData("ET 24:99，改ET1230", 12, 30)]
    [InlineData("ET1200 / ET24:99", 12, 0)]
    [InlineData("ET1200，改 ET 12:30 :99", 12, 0)]
    public void ParsesLastValidMarkedTime(string body, int hour, int minute)
    {
        Assert.True(HuntMessageParser.TryParse(body, out var time));
        Assert.Equal(new EorzeaTime(hour, minute), time);
        Assert.Equal($"{hour:D2}:{minute:D2}", time.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ET 24:00")]
    [InlineData("ET 12:60")]
    [InlineData("ET 9999")]
    [InlineData("ET 12345")]
    [InlineData("PET 12:30")]
    [InlineData("Target1230")]
    [InlineData("EATB 09:30")]
    [InlineData("坐标12:30")]
    [InlineData("15:30")]
    [InlineData("ET 00 00定")]
    [InlineData("ET 15")]
    [InlineData("ET 15..30")]
    [InlineData("ET 12:300")]
    [InlineData("ET 1230:45")]
    [InlineData("ET 12:30:00")]
    [InlineData("ET还没定，先去坐标12:30")]
    [InlineData("ET １５：３０")]
    [InlineData("ET 12:30 :00")]
    [InlineData("ET 12:30 ：00")]
    [InlineData("ET 12:30 .5")]
    [InlineData("ET 12:30０")]
    [InlineData("ET 1230５")]
    [InlineData("ET 12:30　00")]
    [InlineData("ET 12:30\u00A000")]
    [InlineData("ET 12:30\t:00")]
    [InlineData("ET 12:30²")]
    public void RejectsMalformedOrUnmarkedTime(string? body)
    {
        Assert.False(HuntMessageParser.TryParse(body, out _));
    }

    [Fact]
    public void RejectsOversizedInput()
    {
        Assert.False(HuntMessageParser.TryParse(new string('x', HuntMessageParser.MaximumMessageLength) + " ET1230", out _));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(24, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 60)]
    public void TimeValueCannotBeInvalid(int hour, int minute)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EorzeaTime(hour, minute));
    }
}
