namespace SearchTermAnalyzer.Tests;

public class AnalyzerTests
{
    private static readonly AnalyzerOptions Options = new()
    {
        TargetAcos = 0.30m,
        MinClicksForNegative = 10,
        MinOrdersForHarvest = 2,
        MinClicksForBidChange = 8,
        MaxBidChange = 0.30m,
    };

    private static SearchTermRow Row(
        string term, long clicks, decimal spend, decimal sales = 0, int orders = 0,
        string targeting = "bamboo board", string match = "BROAD",
        string campaign = "SP | Board", string adGroup = "Core") =>
        new(campaign, adGroup, targeting, match, term, clicks * 200, clicks, spend, sales, orders);

    // ---------- Negatives ----------

    [Fact]
    public void Search_term_with_enough_clicks_and_no_orders_becomes_a_negative()
    {
        var rows = new[] { Row("cheese board", clicks: 12, spend: 6m) };

        var negative = Assert.Single(Analyzer.FindNegatives(rows, Options, averageOrderValue: 30m));

        Assert.Equal("cheese board", negative.SearchTerm);
        Assert.Equal(NegativeReason.ClicksWithoutOrders, negative.Reason);
    }

    [Fact]
    public void Expensive_search_term_is_negated_before_the_click_threshold()
    {
        // Target cost per order = £30 AOV × 30% = £9. Five clicks cost £10 with no order.
        var rows = new[] { Row("olive wood board", clicks: 5, spend: 10m) };

        var negative = Assert.Single(Analyzer.FindNegatives(rows, Options, averageOrderValue: 30m));

        Assert.Equal(NegativeReason.SpendAboveTargetCpa, negative.Reason);
    }

    [Fact]
    public void Clicks_are_summed_across_rows_for_the_same_ad_group()
    {
        var rows = new[] { Row("cheese board", 6, 3m), Row("Cheese Board", 6, 3m) };

        var negative = Assert.Single(Analyzer.FindNegatives(rows, Options, averageOrderValue: 100m));

        Assert.Equal(12, negative.Metrics.Clicks);
    }

    [Fact]
    public void Search_term_that_converted_is_never_negated()
    {
        var rows = new[] { Row("bamboo cutting board", clicks: 40, spend: 30m, sales: 30m, orders: 1) };

        Assert.Empty(Analyzer.FindNegatives(rows, Options, averageOrderValue: 30m));
    }

    [Fact]
    public void Search_term_identical_to_its_own_keyword_or_asin_target_is_left_to_bid_rules()
    {
        var rows = new[]
        {
            Row("cutting board", 20, 15m, targeting: "cutting board", match: "EXACT"),
            Row("b07nrt6m8d", 20, 15m, targeting: "asin=\"B07NRT6M8D\"", match: "-"),
        };

        Assert.Empty(Analyzer.FindNegatives(rows, Options, averageOrderValue: 30m));
    }

    // ---------- Harvest ----------

    [Fact]
    public void Profitable_search_term_from_broad_is_harvested()
    {
        var rows = new[] { Row("bamboo board with groove", clicks: 20, spend: 16m, sales: 120m, orders: 4) };

        var harvest = Assert.Single(Analyzer.FindHarvests(rows, Options));

        Assert.False(harvest.IsAsin);
        Assert.Equal(4, harvest.Metrics.Orders);
    }

    [Fact]
    public void Harvest_bid_scales_toward_target_but_is_capped()
    {
        // CPC 0.80, ACoS 13.3% → target/ACoS = 2.25×, capped at +30% → 1.04
        var rows = new[] { Row("bamboo board with groove", 20, 16m, 120m, 4) };

        Assert.Equal(1.04m, Analyzer.FindHarvests(rows, Options)[0].SuggestedBid);
    }

    [Fact]
    public void Search_term_already_targeted_as_exact_is_not_harvested()
    {
        var rows = new[]
        {
            Row("bamboo cutting board", 20, 10m, 120m, 4),
            Row("bamboo cutting board", 30, 20m, 150m, 5, targeting: "bamboo cutting board", match: "EXACT"),
        };

        Assert.Empty(Analyzer.FindHarvests(rows, Options));
    }

    [Fact]
    public void Asin_already_product_targeted_is_not_harvested()
    {
        var rows = new[]
        {
            Row("b09xyl4r1c", 20, 10m, 120m, 4, targeting: "substitutes", match: "-"),
            Row("b09xyl4r1c", 20, 10m, 120m, 4, targeting: "asin=\"B09XYL4R1C\"", match: "-"),
        };

        Assert.Empty(Analyzer.FindHarvests(rows, Options));
    }

    [Fact]
    public void Search_term_above_target_acos_is_not_harvested()
    {
        var rows = new[] { Row("chopping board", 40, 40m, 90m, 3) }; // 44% ACoS

        Assert.Empty(Analyzer.FindHarvests(rows, Options));
    }

    [Fact]
    public void Converting_asin_is_harvested_as_a_product_target()
    {
        var rows = new[] { Row("B0ABCDE123", 15, 9m, 90m, 3, targeting: "close-match", match: "-") };

        Assert.True(Assert.Single(Analyzer.FindHarvests(rows, Options)).IsAsin);
    }

    // ---------- Bids ----------

    [Fact]
    public void Target_with_no_orders_is_lowered_by_the_maximum_step()
    {
        var rows = new[] { Row("anything", clicks: 10, spend: 10m) }; // CPC 1.00

        var bid = Assert.Single(Analyzer.SuggestBids(rows, Options));

        Assert.Equal(0.70m, bid.SuggestedBid);
    }

    [Fact]
    public void Target_above_target_acos_is_scaled_down()
    {
        var rows = new[] { Row("anything", clicks: 10, spend: 10m, sales: 25m, orders: 1) }; // CPC 1.00, ACoS 40%

        var bid = Assert.Single(Analyzer.SuggestBids(rows, Options));

        Assert.Equal(0.75m, bid.SuggestedBid); // 1.00 × 30/40
    }

    [Fact]
    public void Target_well_below_target_acos_is_raised_with_a_cap()
    {
        var rows = new[] { Row("anything", clicks: 10, spend: 10m, sales: 100m, orders: 4) }; // ACoS 10%

        var bid = Assert.Single(Analyzer.SuggestBids(rows, Options));

        Assert.Equal(1.30m, bid.SuggestedBid);
        Assert.Equal(0.30m, bid.ChangePercent);
    }

    [Fact]
    public void Target_near_target_acos_is_left_alone()
    {
        var rows = new[] { Row("anything", clicks: 10, spend: 10m, sales: 33m, orders: 1) }; // ACoS 30.3%

        Assert.Empty(Analyzer.SuggestBids(rows, Options));
    }

    [Fact]
    public void Target_without_enough_clicks_is_left_alone()
    {
        var rows = new[] { Row("anything", clicks: 5, spend: 5m) };

        Assert.Empty(Analyzer.SuggestBids(rows, Options));
    }

    [Fact]
    public void Suggested_bid_never_goes_below_amazon_minimum()
    {
        var rows = new[] { Row("anything", clicks: 10, spend: 0.20m) }; // CPC 0.02

        Assert.Empty(Analyzer.SuggestBids(rows, Options)); // 0.014 would round under the floor, so no change
    }

    // ---------- Whole pipeline ----------

    [Fact]
    public void Wasted_spend_is_the_spend_on_negative_candidates()
    {
        var rows = new[]
        {
            Row("good term", 20, 10m, 100m, 4),
            Row("bad term", 15, 7.5m),
        };

        var result = Analyzer.Analyze(rows, Options);

        Assert.Equal(7.5m, result.WastedSpend);
        Assert.Equal(17.5m, result.Total.Spend);
        Assert.Equal(25m, result.AverageOrderValue);
    }
}
