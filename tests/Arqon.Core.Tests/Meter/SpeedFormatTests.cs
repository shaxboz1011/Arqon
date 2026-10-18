using Arqon.Core.Meter;

namespace Arqon.Core.Tests.Meter;

public class SpeedFormatTests
{
    [Theory]
    [InlineData(0, "0", "Kb/s")]
    [InlineData(62_500, "500", "Kb/s")]          // 0.5 Mbit/s
    [InlineData(1_550_000, "12.4", "Mb/s")]
    [InlineData(15_000_000, "120", "Mb/s")]      // three digits need no decimals
    [InlineData(125_000_000, "1.0", "Gb/s")]
    public void Speeds_are_shown_in_bits_per_second_like_the_rest_of_the_app(double bytesPerSecond, string value, string unit)
    {
        var text = SpeedFormat.Of(bytesPerSecond);

        Assert.Equal(value, text.Value);
        Assert.Equal(unit, text.Unit);
    }

    [Fact]
    public void A_value_that_rounds_up_to_the_next_unit_uses_that_unit()
    {
        // 124 999 B/s is 999.992 Kb/s: "1000 Kb/s" would be the wrong way to say 1.0 Mb/s
        var text = SpeedFormat.Of(124_999);

        Assert.Equal("1.0", text.Value);
        Assert.Equal("Mb/s", text.Unit);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-5)]
    public void Garbage_reads_as_zero(double bytesPerSecond)
    {
        Assert.Equal(new SpeedText("0", "Kb/s"), SpeedFormat.Of(bytesPerSecond));
    }
}
