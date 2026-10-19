using Arqon.Core.Usage;

namespace Arqon.Core.Tests.Usage;

public class UsageCsvTests
{
    private static DateOnly D(int day) => new(2026, 10, day);

    [Fact]
    public void Days_without_traffic_are_left_out_and_rows_are_ordered_by_date_then_name()
    {
        var csv = UsageCsv.Build(
        [
            new LinkUsage("wifi", "Wi-Fi", [new DayUsage(D(18), 100, 10), new DayUsage(D(19), 0, 0), new DayUsage(D(20), 300, 30)]),
            new LinkUsage("phone", "Phone", [new DayUsage(D(18), 5, 1), new DayUsage(D(19), 0, 0), new DayUsage(D(20), 0, 0)]),
        ]);

        Assert.Equal(
            "date,connection,downloaded_bytes,uploaded_bytes\r\n" +
            "2026-10-18,Phone,5,1\r\n" +
            "2026-10-18,Wi-Fi,100,10\r\n" +
            "2026-10-20,Wi-Fi,300,30\r\n", csv);
    }

    [Fact]
    public void An_empty_history_is_just_the_header()
    {
        Assert.Equal("date,connection,downloaded_bytes,uploaded_bytes\r\n", UsageCsv.Build([]));
    }

    [Fact]
    public void A_name_with_a_comma_or_a_quote_is_quoted()
    {
        var csv = UsageCsv.Build([new LinkUsage("x", "Home, \"main\"", [new DayUsage(D(18), 1, 2)])]);

        Assert.Contains("2026-10-18,\"Home, \"\"main\"\"\",1,2", csv);
    }

    [Fact]
    public void A_name_that_a_spreadsheet_would_run_as_a_formula_is_defused()
    {
        var csv = UsageCsv.Build([new LinkUsage("x", "=HYPERLINK(\"http://evil\")", [new DayUsage(D(18), 1, 2)])]);

        Assert.DoesNotContain(",=HYPERLINK", csv);
        Assert.Contains("'=HYPERLINK", csv);
    }

    [Fact]
    public void Numbers_do_not_depend_on_the_culture()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
            var csv = UsageCsv.Build([new LinkUsage("x", "A", [new DayUsage(D(18), 1234567, 89)])]);
            Assert.Contains("2026-10-18,A,1234567,89", csv);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }
}
