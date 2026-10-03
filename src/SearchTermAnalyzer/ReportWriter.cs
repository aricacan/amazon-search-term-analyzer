using System.Globalization;
using System.Text;

namespace SearchTermAnalyzer;

/// <summary>Writes the analysis as three action CSVs plus a Markdown summary.</summary>
public static class ReportWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static IReadOnlyList<string> WriteAll(AnalysisResult result, AnalyzerOptions options, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var files = new List<string>
        {
            Write(outputDirectory, "negatives.csv", NegativesCsv(result)),
            Write(outputDirectory, "harvest.csv", HarvestCsv(result)),
            Write(outputDirectory, "bid_changes.csv", BidsCsv(result)),
            Write(outputDirectory, "summary.md", Summary(result, options), withBom: false),
        };
        return files;
    }

    internal static string NegativesCsv(AnalysisResult r)
    {
        var sb = new StringBuilder("Campaign,Ad Group,Search Term,Suggested Negative Type,Clicks,Spend,Reason\n");
        foreach (var n in r.Negatives)
            sb.AppendLine(Row(n.Campaign, n.AdGroup, n.SearchTerm,
                Analyzer.IsAsin(n.SearchTerm) ? "Negative product target" : "Negative exact",
                n.Metrics.Clicks.ToString(Inv), Money(n.Metrics.Spend), Describe(n.Reason)));
        return sb.ToString();
    }

    internal static string HarvestCsv(AnalysisResult r)
    {
        var sb = new StringBuilder("Source Campaign,Source Ad Group,Search Term,Add As,Orders,Sales,Spend,ACoS,CPC,Suggested Bid\n");
        foreach (var h in r.Harvests)
            sb.AppendLine(Row(h.Campaign, h.AdGroup, h.SearchTerm,
                h.IsAsin ? "Product target (ASIN)" : "Exact keyword",
                h.Metrics.Orders.ToString(Inv), Money(h.Metrics.Sales), Money(h.Metrics.Spend),
                Percent(h.Metrics.Acos), Money(h.Metrics.Cpc), Money(h.SuggestedBid)));
        return sb.ToString();
    }

    internal static string BidsCsv(AnalysisResult r)
    {
        var sb = new StringBuilder("Campaign,Ad Group,Targeting,Match Type,Clicks,Orders,Spend,ACoS,Current CPC,Suggested Bid,Change,Reason\n");
        foreach (var b in r.Bids)
            sb.AppendLine(Row(b.Campaign, b.AdGroup, b.Targeting, b.MatchType,
                b.Metrics.Clicks.ToString(Inv), b.Metrics.Orders.ToString(Inv), Money(b.Metrics.Spend),
                Percent(b.Metrics.Acos), Money(b.CurrentCpc), Money(b.SuggestedBid),
                (b.ChangePercent >= 0 ? "+" : "") + Percent(b.ChangePercent), b.Reason));
        return sb.ToString();
    }

    public static string Summary(AnalysisResult r, AnalyzerOptions o)
    {
        var t = r.Total;
        var wastedShare = t.Spend > 0 ? r.WastedSpend / t.Spend : 0;
        var sb = new StringBuilder();

        sb.AppendLine("# Search Term Analysis");
        sb.AppendLine();
        sb.AppendLine($"Target ACoS: **{Percent(o.TargetAcos)}** · Negative after **{o.MinClicksForNegative}** clicks without an order · Harvest at **{o.MinOrdersForHarvest}+** orders");
        sb.AppendLine();
        sb.AppendLine("## Account snapshot");
        sb.AppendLine();
        sb.AppendLine("| Spend | Sales | ACoS | Orders | Clicks | CPC | CVR | AOV |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|");
        sb.AppendLine($"| {Money(t.Spend)} | {Money(t.Sales)} | {Percent(t.Acos)} | {t.Orders} | {t.Clicks} | {Money(t.Cpc)} | {Percent(t.ConversionRate)} | {Money(r.AverageOrderValue)} |");
        sb.AppendLine();
        sb.AppendLine("## Actions");
        sb.AppendLine();
        sb.AppendLine($"- **{r.Negatives.Count} negatives** to add, covering **{Money(r.WastedSpend)}** of spend with zero orders ({Percent(wastedShare)} of total).");
        sb.AppendLine($"- **{r.Harvests.Count} search terms** to harvest as exact keywords or product targets.");
        sb.AppendLine($"- **{r.Bids.Count} bid changes**: {r.Bids.Count(b => b.SuggestedBid > b.CurrentCpc)} up, {r.Bids.Count(b => b.SuggestedBid < b.CurrentCpc)} down.");

        AppendTop(sb, "Top wasted search terms", "| Search term | Campaign | Clicks | Spend |", "|---|---|---:|---:|",
            r.Negatives.Take(10).Select(n => $"| {Md(n.SearchTerm)} | {Md(n.Campaign)} | {n.Metrics.Clicks} | {Money(n.Metrics.Spend)} |"));

        AppendTop(sb, "Top harvest opportunities", "| Search term | Orders | ACoS | Suggested bid |", "|---|---:|---:|---:|",
            r.Harvests.Take(10).Select(h => $"| {Md(h.SearchTerm)} | {h.Metrics.Orders} | {Percent(h.Metrics.Acos)} | {Money(h.SuggestedBid)} |"));

        AppendTop(sb, "Largest bid changes", "| Target | Match | Current CPC | Suggested | Reason |", "|---|---|---:|---:|---|",
            r.Bids.OrderByDescending(b => Math.Abs(b.ChangePercent) * b.Metrics.Spend).Take(10)
             .Select(b => $"| {Md(b.Targeting)} | {b.MatchType} | {Money(b.CurrentCpc)} | {Money(b.SuggestedBid)} | {b.Reason} |"));

        return sb.ToString();
    }

    private static void AppendTop(StringBuilder sb, string title, string header, string divider, IEnumerable<string> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        sb.AppendLine(header);
        sb.AppendLine(divider);
        list.ForEach(l => sb.AppendLine(l));
    }

    private static string Describe(NegativeReason reason) => reason switch
    {
        NegativeReason.ClicksWithoutOrders => "Click threshold reached with no orders",
        NegativeReason.SpendAboveTargetCpa => "Spend above target cost per order",
        _ => reason.ToString(),
    };

    internal static string Money(decimal value) => value.ToString("0.00", Inv);

    internal static string Percent(decimal? value) =>
        value is { } v ? (v * 100).ToString("0.0", Inv) + "%" : "–";

    private static string Row(params string[] cells) => string.Join(",", cells.Select(Escape));

    private static string Escape(string cell) =>
        cell.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell;

    /// <summary>Markdown table cell: pipes in campaign names would otherwise split the column.</summary>
    private static string Md(string text) => text.Replace("|", "\\|");

    // CSVs get a BOM so Excel opens £/€ and accents correctly.
    private static string Write(string dir, string name, string content, bool withBom = true)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom));
        return path;
    }
}
