using System.Globalization;
using System.Text;

namespace Arqon.Core.Usage;

/// <summary>The usage history as a spreadsheet file: one row per day and connection, bytes downloaded and uploaded.</summary>
public static class UsageCsv
{
    public static string Build(IReadOnlyList<LinkUsage> usage)
    {
        var text = new StringBuilder("date,connection,downloaded_bytes,uploaded_bytes\r\n");
        var rows = usage
            .SelectMany(link => link.Days.Where(d => d.Down + d.Up > 0).Select(d => (d.Date, link.Name, d.Down, d.Up)))
            .OrderBy(r => r.Date).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var (date, name, down, up) in rows)
        {
            text.Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',').Append(Quote(name)).Append(',')
                .Append(down.ToString(CultureInfo.InvariantCulture)).Append(',').Append(up.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        }
        return text.ToString();
    }

    /// <summary>A field with a comma, a quote or a line break is wrapped in quotes, and quotes inside are doubled; a leading = + - @ is defused so a spreadsheet does not run it as a formula.</summary>
    private static string Quote(string field)
    {
        if (field.Length > 0 && field[0] is '=' or '+' or '-' or '@') field = "'" + field;
        return field.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
    }
}
