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
    [InlineData(0, "0", "KB/s")]
    [InlineData(500_000, "500", "KB/s")]
    [InlineData(1_540_000, "1.5", "MB/s")]
    [InlineData(15_000_000, "15.0", "MB/s")]
    [InlineData(125_000_000, "125", "MB/s")]
    [InlineData(1_000_000_000, "1.0", "GB/s")]
    [InlineData(999_999, "1.0", "MB/s")]         // rounds up to the next unit instead of "1000 KB/s"
    public void Speeds_can_be_shown_in_bytes_per_second_instead(double bytesPerSecond, string value, string unit)
    {
        var text = SpeedFormat.Of(bytesPerSecond, SpeedUnit.Bytes);

        Assert.Equal(value, text.Value);
        Assert.Equal(unit, text.Unit);
    }

    [Fact]
    public void Mega_units_are_decimal_so_80_megabit_is_exactly_10_megabyte()
    {
        Assert.Equal(80, SpeedFormat.ToMega(10_000_000, SpeedUnit.Bits));
        Assert.Equal(10, SpeedFormat.ToMega(10_000_000, SpeedUnit.Bytes));
    }

    [Theory]
    [InlineData(SpeedUnit.Bits, 25.5)]
    [InlineData(SpeedUnit.Bytes, 3.2)]
    public void Typed_values_convert_back_to_bytes_per_second(SpeedUnit unit, double typed)
    {
        Assert.Equal(typed, SpeedFormat.ToMega(SpeedFormat.FromMega(typed, unit), unit), precision: 9);
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
