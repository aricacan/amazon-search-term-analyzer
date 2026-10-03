using System.Globalization;
using SearchTermAnalyzer;

const string Usage = """
Amazon Search Term Analyzer

Usage:
  SearchTermAnalyzer <report.csv> [options]

Options:
  --target-acos <percent>     Target ACoS, e.g. 25 for 25%            (default: 30)
  --negative-clicks <n>       Clicks without an order before negating   (default: 10)
  --harvest-orders <n>        Orders before harvesting a search term    (default: 2)
  --bid-clicks <n>            Clicks before a bid is changed            (default: 8)
  --max-bid-change <percent>  Largest bid move in either direction      (default: 30)
  --out <folder>              Where to write the results                (default: ./output)
  -h, --help                  Show this help
""";

if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 1 : 0;
}

string? input = null;
var output = "output";
var options = new AnalyzerOptions();

try
{
    for (var i = 0; i < args.Length; i++)
    {
        string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");

        switch (args[i])
        {
            case "--target-acos": options = options with { TargetAcos = Percent(Next()) }; break;
            case "--negative-clicks": options = options with { MinClicksForNegative = int.Parse(Next(), CultureInfo.InvariantCulture) }; break;
            case "--harvest-orders": options = options with { MinOrdersForHarvest = int.Parse(Next(), CultureInfo.InvariantCulture) }; break;
            case "--bid-clicks": options = options with { MinClicksForBidChange = int.Parse(Next(), CultureInfo.InvariantCulture) }; break;
            case "--max-bid-change": options = options with { MaxBidChange = Percent(Next()) }; break;
            case "--out": output = Next(); break;
            default:
                if (args[i].StartsWith('-')) throw new ArgumentException($"Unknown option {args[i]}.");
                input = args[i];
                break;
        }
    }

    if (input is null) throw new ArgumentException("Please pass the path to a search term report CSV.");
    if (!File.Exists(input)) throw new FileNotFoundException($"File not found: {input}");

    var rows = ReportReader.ReadFile(input);
    var result = Analyzer.Analyze(rows, options);
    var files = ReportWriter.WriteAll(result, options, output);

    var t = result.Total;
    Console.WriteLine($"Read {rows.Count} rows · Spend {ReportWriter.Money(t.Spend)} · Sales {ReportWriter.Money(t.Sales)} · ACoS {ReportWriter.Percent(t.Acos)}");
    Console.WriteLine();
    Console.WriteLine($"  Negatives : {result.Negatives.Count,4}   ({ReportWriter.Money(result.WastedSpend)} spend with zero orders)");
    Console.WriteLine($"  Harvest   : {result.Harvests.Count,4}   search terms ready to become exact targets");
    Console.WriteLine($"  Bid moves : {result.Bids.Count,4}");
    Console.WriteLine();
    foreach (var f in files) Console.WriteLine($"  → {f}");
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or InvalidDataException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.Error.WriteLine("Run with --help for usage.");
    return 2;
}

static decimal Percent(string value)
{
    var number = decimal.Parse(value.TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture);
    if (number <= 0) throw new ArgumentException("Percentages must be greater than zero.");
    return number > 1 ? number / 100 : number; // accept both 30 and 0.30
}
