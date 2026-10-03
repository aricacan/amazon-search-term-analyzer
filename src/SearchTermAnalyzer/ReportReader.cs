using System.Globalization;
using System.Text;

namespace SearchTermAnalyzer;

/// <summary>
/// Reads an Amazon Sponsored Products search term report exported as CSV.
/// Handles the header variations Amazon uses across marketplaces and report versions
/// ("7 Day Total Sales", "14 Day Total Sales (£)", "Sales", ...), comma or semicolon
/// delimiters, currency symbols and European number formats.
/// </summary>
public static class ReportReader
{
    private enum Column { Campaign, AdGroup, Targeting, MatchType, SearchTerm, Impressions, Clicks, Spend, Sales, Orders }

    private static readonly Column[] Required =
        { Column.Campaign, Column.AdGroup, Column.SearchTerm, Column.Clicks, Column.Spend, Column.Sales, Column.Orders };

    public static IReadOnlyList<SearchTermRow> ReadFile(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Read(reader);
    }

    public static IReadOnlyList<SearchTermRow> Read(TextReader reader)
    {
        var headerLine = reader.ReadLine() ?? throw new InvalidDataException("The report is empty.");
        var delimiter = headerLine.Count(c => c == ';') > headerLine.Count(c => c == ',') ? ';' : ',';
        var commaIsDecimal = delimiter == ';';

        var header = SplitLine(headerLine, delimiter);
        var map = MapColumns(header);

        var missing = Required.Where(c => !map.ContainsKey(c)).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException(
                $"Missing required column(s): {string.Join(", ", missing)}. Found: {string.Join(" | ", header)}");

        var rows = new List<SearchTermRow>();
        string? line;
        var lineNumber = 1;
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cells = SplitLine(line, delimiter);
            string Get(Column c) => map.TryGetValue(c, out var i) && i < cells.Count ? cells[i].Trim() : "";

            try
            {
                rows.Add(new SearchTermRow(
                    Campaign: Get(Column.Campaign),
                    AdGroup: Get(Column.AdGroup),
                    Targeting: Get(Column.Targeting),
                    MatchType: Get(Column.MatchType),
                    SearchTerm: Get(Column.SearchTerm),
                    Impressions: (long)ParseNumber(Get(Column.Impressions), commaIsDecimal),
                    Clicks: (long)ParseNumber(Get(Column.Clicks), commaIsDecimal),
                    Spend: ParseNumber(Get(Column.Spend), commaIsDecimal),
                    Sales: ParseNumber(Get(Column.Sales), commaIsDecimal),
                    Orders: (int)ParseNumber(Get(Column.Orders), commaIsDecimal)));
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException($"Line {lineNumber}: {ex.Message}", ex);
            }
        }
        return rows;
    }

    private static Dictionary<Column, int> MapColumns(IReadOnlyList<string> header)
    {
        var map = new Dictionary<Column, int>();
        for (var i = 0; i < header.Count; i++)
        {
            var column = Classify(Normalize(header[i]));
            if (column is { } c && !map.ContainsKey(c)) map[c] = i;
        }
        return map;
    }

    /// <summary>Lower-case letters and digits only: "7 Day Total Sales (£) " → "7daytotalsales".</summary>
    internal static string Normalize(string header) =>
        new(header.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static Column? Classify(string h) => h switch
    {
        "campaignname" or "campaign" => Column.Campaign,
        "adgroupname" or "adgroup" => Column.AdGroup,
        "targeting" or "keyword" or "keywordtext" => Column.Targeting,
        "matchtype" => Column.MatchType,
        "customersearchterm" or "searchterm" => Column.SearchTerm,
        "impressions" => Column.Impressions,
        "clicks" => Column.Clicks,
        "spend" or "cost" => Column.Spend,
        _ when h == "sales" || (h.Contains("totalsales") && !h.Contains("other") && !h.Contains("advertised")) => Column.Sales,
        _ when h == "orders" || h.Contains("totalorders") => Column.Orders,
        _ => null,
    };

    /// <summary>Parses "£1,234.56", "1.234,56 €", "12%", "" (→ 0).</summary>
    internal static decimal ParseNumber(string raw, bool commaIsDecimal)
    {
        var s = new string(raw.Where(c => char.IsDigit(c) || c is '.' or ',' or '-').ToArray());
        if (s.Length == 0 || s == "-") return 0;

        s = commaIsDecimal
            ? s.Replace(".", "").Replace(',', '.')
            : s.Replace(",", "");

        if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Could not read '{raw}' as a number.");
        return value;
    }

    /// <summary>Splits one CSV line, honouring double-quoted cells and escaped quotes ("").</summary>
    internal static List<string> SplitLine(string line, char delimiter)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else current.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == delimiter) { cells.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        cells.Add(current.ToString());
        return cells;
    }
}
