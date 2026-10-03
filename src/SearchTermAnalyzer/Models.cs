namespace SearchTermAnalyzer;

/// <summary>One row of an Amazon Sponsored Products search term report.</summary>
public sealed record SearchTermRow(
    string Campaign,
    string AdGroup,
    string Targeting,
    string MatchType,
    string SearchTerm,
    long Impressions,
    long Clicks,
    decimal Spend,
    decimal Sales,
    int Orders);

/// <summary>Performance totals for any grouping (search term, keyword, account).</summary>
public sealed record Metrics(long Impressions, long Clicks, decimal Spend, decimal Sales, int Orders)
{
    public static Metrics Sum(IEnumerable<SearchTermRow> rows)
    {
        long impressions = 0, clicks = 0;
        decimal spend = 0, sales = 0;
        int orders = 0;
        foreach (var r in rows)
        {
            impressions += r.Impressions;
            clicks += r.Clicks;
            spend += r.Spend;
            sales += r.Sales;
            orders += r.Orders;
        }
        return new Metrics(impressions, clicks, spend, sales, orders);
    }

    /// <summary>Advertising cost of sales as a fraction (0.25 = 25%). Null when there are no sales.</summary>
    public decimal? Acos => Sales > 0 ? Spend / Sales : null;
    public decimal Cpc => Clicks > 0 ? Spend / Clicks : 0;
    public decimal ConversionRate => Clicks > 0 ? (decimal)Orders / Clicks : 0;
    public decimal Ctr => Impressions > 0 ? (decimal)Clicks / Impressions : 0;
}

public sealed record AnalyzerOptions
{
    /// <summary>Target ACoS as a fraction (0.30 = 30%).</summary>
    public decimal TargetAcos { get; init; } = 0.30m;

    /// <summary>Clicks without an order before a search term becomes a negative candidate.</summary>
    public int MinClicksForNegative { get; init; } = 10;

    /// <summary>Orders a search term needs before it is suggested as a new exact keyword.</summary>
    public int MinOrdersForHarvest { get; init; } = 2;

    /// <summary>Clicks a target needs before its bid is changed.</summary>
    public int MinClicksForBidChange { get; init; } = 8;

    /// <summary>Largest bid change in either direction, as a fraction (0.30 = ±30%).</summary>
    public decimal MaxBidChange { get; init; } = 0.30m;
}

public enum NegativeReason
{
    /// <summary>Enough clicks to expect an order, but none came.</summary>
    ClicksWithoutOrders,
    /// <summary>Spend already exceeds what one order is allowed to cost at target ACoS.</summary>
    SpendAboveTargetCpa,
}

public sealed record NegativeCandidate(
    string Campaign,
    string AdGroup,
    string SearchTerm,
    Metrics Metrics,
    NegativeReason Reason);

public sealed record HarvestCandidate(
    string Campaign,
    string AdGroup,
    string SearchTerm,
    bool IsAsin,
    Metrics Metrics,
    decimal SuggestedBid);

public sealed record BidSuggestion(
    string Campaign,
    string AdGroup,
    string Targeting,
    string MatchType,
    Metrics Metrics,
    decimal CurrentCpc,
    decimal SuggestedBid,
    string Reason)
{
    public decimal ChangePercent => CurrentCpc > 0 ? (SuggestedBid - CurrentCpc) / CurrentCpc : 0;
}

public sealed record AnalysisResult(
    Metrics Total,
    decimal AverageOrderValue,
    IReadOnlyList<NegativeCandidate> Negatives,
    IReadOnlyList<HarvestCandidate> Harvests,
    IReadOnlyList<BidSuggestion> Bids)
{
    /// <summary>Spend on search terms flagged as negatives — money that produced no orders.</summary>
    public decimal WastedSpend => Negatives.Sum(n => n.Metrics.Spend);
}
