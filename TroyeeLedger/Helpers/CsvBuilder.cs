using System.Text;

namespace TroyeeLedger.Helpers;

public class CsvBuilder
{
    private readonly StringBuilder _sb = new();

    public CsvBuilder Row(params object?[] cells)
    {
        _sb.AppendLine(string.Join(",", cells.Select(Escape)));
        return this;
    }

    private static string Escape(object? cell)
    {
        var s = cell switch
        {
            null => "",
            decimal d => d.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("yyyy-MM-dd"),
            _ => cell.ToString() ?? ""
        };
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            s = "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    public byte[] ToBytes() => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(_sb.ToString())).ToArray();
}
