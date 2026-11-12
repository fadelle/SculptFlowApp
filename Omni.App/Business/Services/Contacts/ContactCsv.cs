using System.Text;

namespace Omni.App.Business.Services.Contacts;

/// <summary>
/// Reads and writes the contact list's CSV (QA t12-fix-05). Reading: RFC 4180 quotes, the delimiter guessed from the header
/// (comma, semicolon as Excel writes it in many locales, or tab), a byte-order mark dropped, blank lines skipped. Writing:
/// UTF-8 with a byte-order mark so Excel shows Arabic, every cell quoted, and a cell that starts like a formula (= + - @, tab,
/// carriage return) prefixed with an apostrophe so a spreadsheet never runs it.
/// </summary>
public static class ContactCsv
{
    /// <summary>The UTF-8 byte-order mark: dropped when reading, written first so Excel reads the file as UTF-8.</summary>
    public const char Bom = (char)0xFEFF;

    public static IReadOnlyList<string[]> Parse(string text) => ParseLines(text).Select(r => r.Cells).ToList();

    /// <summary>The rows with the file line each starts on (1 = the first line), so a refused row can be named by its line even
    /// after blank lines or a cell that spans lines.</summary>
    public static IReadOnlyList<(int Line, string[] Cells)> ParseLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        if (text[0] == Bom) text = text[1..];
        var delimiter = GuessDelimiter(text);
        var rows = new List<(int, string[])>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        int line = 1, rowLine = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else
                {
                    if (c == '\n') line++;
                    cell.Append(c);
                }
                continue;
            }
            if (c == '"' && cell.Length == 0) { quoted = true; continue; }
            if (c == delimiter) { row.Add(cell.ToString()); cell.Clear(); continue; }
            if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                if (row.Any(v => v.Trim().Length > 0)) rows.Add((rowLine, row.ToArray()));
                row.Clear();
                line++;
                rowLine = line;
                continue;
            }
            cell.Append(c);
        }
        row.Add(cell.ToString());
        if (row.Any(v => v.Trim().Length > 0)) rows.Add((rowLine, row.ToArray()));
        return rows;
    }

    /// <summary>The delimiter the header line uses most, outside quotes; comma when it has none.</summary>
    internal static char GuessDelimiter(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var header = end < 0 ? text : text[..end];
        var counts = new Dictionary<char, int> { [','] = 0, [';'] = 0, ['\t'] = 0 };
        var quoted = false;
        foreach (var c in header)
        {
            if (c == '"') quoted = !quoted;
            else if (!quoted && counts.ContainsKey(c)) counts[c]++;
        }
        var best = counts.MaxBy(kv => kv.Value);
        return best.Value == 0 ? ',' : best.Key;
    }

    public static string Write(IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder().Append(Bom);
        foreach (var row in rows)
        {
            sb.Append(string.Join(',', row.Select(Cell))).Append("\r\n");
        }
        return sb.ToString();
    }

    internal static string Cell(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or '\t' or '\r') v = "'" + v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }
}
