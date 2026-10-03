using System.Text.RegularExpressions;

namespace SearchTermAnalyzer;

/// <summary>
/// Turns raw search term rows into three action lists:
/// negatives to add, search terms to harvest as exact keywords, and bid changes per target.
/// </summary>
public static partial class Analyzer
{
    /// <summary>Lowest bid Amazon accepts for Sponsored Products in most marketplaces.</summary>
    public const decimal MinimumBid = 0.02m;

    [GeneratedRegex("^b0[a-z0-9]{8}$", RegexOptions.IgnoreCase)]
    private static partial Regex AsinPattern();

    [GeneratedRegex("^asin(?:-expanded)?=\"([^\"]+)\"$", RegexOptions.IgnoreCase)]
    private static partial Regex AsinTargetPattern();

    public static bool IsAsin(string term) => AsinPattern().IsMatch(term.Trim());

    /// <summary>
    /// Comparable form of a keyword or product target: lower-case, and
    /// <c>asin="B0XXXXXXXX"</c> reduced to the bare ASIN so it matches the search term Amazon reports.
    /// </summary>
    public static string TargetKey(string targeting)
    {
        var t = targeting.Trim();
        var m = AsinTargetPattern().Match(t);
        return (m.Success ? m.Groups[1].Value : t).ToLowerInvariant();
    }

    public static AnalysisResult Analyze(IReadOnlyList<SearchTermRow> rows, AnalyzerOptions options)
    {
        var total = Metrics.Sum(rows);
        var averageOrderValue = total.Orders > 0 ? total.Sales / total.Orders : 0;

        return new AnalysisResult(
            total,
            averageOrderValue,
            FindNegatives(rows, options, averageOrderValue),
            FindHarvests(rows, options),
            SuggestBids(rows, options));
    }

    /// <summary>
    /// A search term is a negative candidate when it has no orders and either
    /// (a) it reached the click threshold, or
    /// (b) its spend is already above the target cost per order (AOV × target ACoS).
    /// Terms that are identical to the keyword they matched are left to the bid rules instead.
    /// </summary>
    internal static List<NegativeCandidate> FindNegatives(
        IReadOnlyList<SearchTermRow> rows, AnalyzerOptions options, decimal averageOrderValue)
    {
        var targetCpa = averageOrderValue * options.TargetAcos;
        var result = new List<NegativeCandidate>();

        var groups = rows
            .Where(r => r.SearchTerm.Length > 0 && !Same(r.SearchTerm, r.Targeting))
            .GroupBy(r => (r.Campaign, r.AdGroup, Term: r.SearchTerm.ToLowerInvariant()));

        foreach (var g in groups)
        {
            var m = Metrics.Sum(g);
            if (m.Orders > 0) continue;

            NegativeReason? reason =
                m.Clicks >= options.MinClicksForNegative ? NegativeReason.ClicksWithoutOrders :
                targetCpa > 0 && m.Spend >= targetCpa && m.Clicks >= 3 ? NegativeReason.SpendAboveTargetCpa :
                null;

            if (reason is { } r)
                result.Add(new NegativeCandidate(g.Key.Campaign, g.Key.AdGroup, g.First().SearchTerm, m, r));
        }

        return result.OrderByDescending(n => n.Metrics.Spend).ToList();
    }

    /// <summary>
    /// A search term is worth harvesting when it converts at or below target ACoS
    /// and is not yet targeted as an exact keyword anywhere in the account.
    /// </summary>
    internal static List<HarvestCandidate> FindHarvests(IReadOnlyList<SearchTermRow> rows, AnalyzerOptions options)
    {
        // Exact keywords and ASIN product targets that already exist in the account.
        var alreadyTargeted = rows
            .Where(r => r.MatchType.Equals("exact", StringComparison.OrdinalIgnoreCase) || IsAsin(TargetKey(r.Targeting)))
            .Select(r => TargetKey(r.Targeting))
            .ToHashSet();

        var result = new List<HarvestCandidate>();

        foreach (var g in rows.Where(r => r.SearchTerm.Length > 0).GroupBy(r => r.SearchTerm.Trim().ToLowerInvariant()))
        {
            if (alreadyTargeted.Contains(g.Key)) continue;

            var m = Metrics.Sum(g);
            if (m.Orders < options.MinOrdersForHarvest || m.Acos is not { } acos || acos > options.TargetAcos) continue;

            // Credit the ad group that produced the most orders for this term.
            var source = g.GroupBy(r => (r.Campaign, r.AdGroup))
                          .OrderByDescending(x => x.Sum(r => r.Orders))
                          .First().Key;

            var multiplier = Math.Min(options.TargetAcos / acos, 1 + options.MaxBidChange);
            var bid = RoundBid(m.Cpc * multiplier);

            result.Add(new HarvestCandidate(source.Campaign, source.AdGroup, g.First().SearchTerm.Trim(), IsAsin(g.Key), m, bid));
        }

        return result.OrderByDescending(h => h.Metrics.Orders).ThenBy(h => h.Metrics.Acos).ToList();
    }

    /// <summary>
    /// Bid rules per target (keyword, product or auto target), once it has enough clicks:
    /// no orders → lower by the maximum step; ACoS above target → scale down toward target;
    /// ACoS well below target → scale up to buy more traffic. Every change is capped at ±MaxBidChange.
    /// </summary>
    internal static List<BidSuggestion> SuggestBids(IReadOnlyList<SearchTermRow> rows, AnalyzerOptions options)
    {
        var result = new List<BidSuggestion>();
        var floor = 1 - options.MaxBidChange;
        var ceiling = 1 + options.MaxBidChange;

        foreach (var g in rows.Where(r => r.Targeting.Length > 0)
                              .GroupBy(r => (r.Campaign, r.AdGroup, r.Targeting, r.MatchType)))
        {
            var m = Metrics.Sum(g);
            if (m.Clicks < options.MinClicksForBidChange || m.Cpc <= 0) continue;

            decimal multiplier;
            string reason;

            if (m.Acos is not { } acos)
            {
                multiplier = floor;
                reason = $"No orders after {m.Clicks} clicks";
            }
            else if (acos > options.TargetAcos * 1.10m)
            {
                multiplier = Math.Max(options.TargetAcos / acos, floor);
                reason = $"ACoS {acos * 100:0}% above {options.TargetAcos * 100:0}% target";
            }
            else if (acos < options.TargetAcos * 0.80m)
            {
                multiplier = Math.Min(options.TargetAcos / acos, ceiling);
                reason = $"ACoS {acos * 100:0}% well below target, room to scale";
            }
            else continue; // within ±10–20% of target: leave it alone

            var currentCpc = RoundBid(m.Cpc);
            var bid = RoundBid(currentCpc * multiplier);
            if (bid == currentCpc) continue;

            result.Add(new BidSuggestion(g.Key.Campaign, g.Key.AdGroup, g.Key.Targeting, g.Key.MatchType,
                m, currentCpc, bid, reason));
        }

        return result.OrderByDescending(b => b.Metrics.Spend).ToList();
    }

    internal static decimal RoundBid(decimal bid) =>
        Math.Max(MinimumBid, Math.Round(bid, 2, MidpointRounding.AwayFromZero));

    private static bool Same(string searchTerm, string targeting) =>
        searchTerm.Trim().ToLowerInvariant() == TargetKey(targeting);
}
