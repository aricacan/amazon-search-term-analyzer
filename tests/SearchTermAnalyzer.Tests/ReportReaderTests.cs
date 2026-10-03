namespace SearchTermAnalyzer.Tests;

public class ReportReaderTests
{
    [Fact]
    public void Reads_amazon_uk_export_with_currency_symbols()
    {
        const string csv = """
            Campaign Name,Ad Group Name,Targeting,Match Type,Customer Search Term,Impressions,Clicks,Spend,7 Day Total Sales ,Total Advertising Cost of Sales (ACoS) ,7 Day Total Orders (#)
            SP | Board,Core,bamboo board,BROAD,bamboo cutting board,"1,204",31,£24.80,"£1,049.65",2.36%,35
            """;

        var row = Assert.Single(ReportReader.Read(new StringReader(csv)));

        Assert.Equal("SP | Board", row.Campaign);
        Assert.Equal("bamboo cutting board", row.SearchTerm);
        Assert.Equal(1204, row.Impressions);
        Assert.Equal(31, row.Clicks);
        Assert.Equal(24.80m, row.Spend);
        Assert.Equal(1049.65m, row.Sales);
        Assert.Equal(35, row.Orders);
    }

    [Fact]
    public void Reads_semicolon_export_with_european_decimals()
    {
        const string csv = """
            Campaign Name;Ad Group Name;Targeting;Match Type;Customer Search Term;Impressions;Clicks;Spend;14 Day Total Sales (€);14 Day Total Orders (#)
            SP DE;Core;schneidebrett;EXACT;schneidebrett holz;2.310;18;12,60 €;1.099,90 €;3
            """;

        var row = Assert.Single(ReportReader.Read(new StringReader(csv)));

        Assert.Equal(2310, row.Impressions);
        Assert.Equal(12.60m, row.Spend);
        Assert.Equal(1099.90m, row.Sales);
        Assert.Equal(3, row.Orders);
    }

    [Fact]
    public void Does_not_mistake_acos_or_advertised_sku_sales_for_total_sales()
    {
        const string csv = """
            Campaign Name,Ad Group Name,Customer Search Term,Clicks,Spend,Total Advertising Cost of Sales (ACoS),7 Day Advertised SKU Sales,7 Day Total Sales,7 Day Total Orders (#)
            C,A,term,5,4.00,10%,30.00,40.00,1
            """;

        var row = Assert.Single(ReportReader.Read(new StringReader(csv)));

        Assert.Equal(40.00m, row.Sales);
    }

    [Fact]
    public void Missing_required_column_gives_a_clear_error()
    {
        const string csv = "Campaign Name,Ad Group Name,Customer Search Term,Clicks,Spend\nC,A,term,1,1.00";

        var ex = Assert.Throws<InvalidDataException>(() => ReportReader.Read(new StringReader(csv)));

        Assert.Contains("Sales", ex.Message);
        Assert.Contains("Orders", ex.Message);
    }

    [Theory]
    [InlineData("£1,234.56", false, 1234.56)]
    [InlineData("1.234,56 €", true, 1234.56)]
    [InlineData("$0.45", false, 0.45)]
    [InlineData("", false, 0)]
    [InlineData("-", false, 0)]
    public void Parses_numbers_in_marketplace_formats(string raw, bool commaIsDecimal, double expected)
    {
        Assert.Equal((decimal)expected, ReportReader.ParseNumber(raw, commaIsDecimal));
    }

    [Fact]
    public void Splits_quoted_cells_with_commas_and_escaped_quotes()
    {
        var cells = ReportReader.SplitLine("a,\"b, c\",\"asin=\"\"B0TEST1234\"\"\",d", ',');

        Assert.Equal(new[] { "a", "b, c", "asin=\"B0TEST1234\"", "d" }, cells);
    }
}
