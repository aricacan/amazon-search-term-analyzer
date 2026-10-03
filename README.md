# Amazon Search Term Analyzer

[![CI](https://github.com/aricacan/amazon-search-term-analyzer/actions/workflows/ci.yml/badge.svg)](https://github.com/aricacan/amazon-search-term-analyzer/actions/workflows/ci.yml)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

A small .NET command-line tool that reads an **Amazon Sponsored Products search term report** and turns it into three action lists:

1. **Negatives:** search terms that keep spending without converting
2. **Harvest:** converting search terms that should become their own exact keyword or ASIN target
3. **Bid changes:** keyword, product and auto targets whose bids should move toward target ACoS

## Why I built it

Search term reviews are the most repetitive job in Amazon PPC. Every week someone opens the report in Excel, filters by clicks, sorts by spend, checks ACoS and copies terms into negative lists. On a catalog with thousands of SKUs this takes hours, and it is easy to get wrong.

The rules themselves are simple and fit on one page, so they belong in code. They are written down, testable, and give the same result every time. This tool does that first pass in seconds, so the time goes into the judgement calls instead.

## Quick start

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/aricacan/amazon-search-term-analyzer.git
cd amazon-search-term-analyzer

dotnet run --project src/SearchTermAnalyzer -- sample-data/sample_search_term_report.csv --target-acos 30
```

```
Read 32 rows · Spend 714.49 · Sales 2579.20 · ACoS 27.7%

  Negatives :   10   (100.13 spend with zero orders)
  Harvest   :    8   search terms ready to become exact targets
  Bid moves :   10

  → output/negatives.csv
  → output/harvest.csv
  → output/bid_changes.csv
  → output/summary.md
```

The full result for the sample report is in [`sample-output/`](sample-output/). The summary is in [`sample-output/summary.md`](sample-output/summary.md).

### Using your own report

In Seller Central or Vendor Central, go to **Advertising → Reports → Create report**. Choose **Sponsored Products → Search term**, set the time unit to **Summary**, and pick a date range (30 or 60 days works well). Download the report, save it as **CSV**, then run:

```bash
dotnet run --project src/SearchTermAnalyzer -- path/to/report.csv --target-acos 25 --out results
```

The reader accepts the header variations Amazon uses across marketplaces and report versions: `7 Day` or `14 Day` attribution, `£`, `€` and `$` values, comma- or semicolon-separated files, and European number formats such as `1.234,56`.

## Options

| Option | Default | Meaning |
|---|---:|---|
| `--target-acos` | 30 | Target ACoS in percent |
| `--negative-clicks` | 10 | Clicks without an order before a search term is flagged |
| `--harvest-orders` | 2 | Orders a search term needs before it is harvested |
| `--bid-clicks` | 8 | Clicks a target needs before its bid is changed |
| `--max-bid-change` | 30 | Largest bid move in either direction, in percent |
| `--out` | `output` | Folder for the result files |

## The rules

All thresholds are options. The logic lives in [`Analyzer.cs`](src/SearchTermAnalyzer/Analyzer.cs) and every rule has a unit test.

**Negatives.** A search term within an ad group is flagged when it has **no orders** and either:
- it reached the click threshold, or
- its spend is already above the **target cost per order** (account AOV × target ACoS), with at least 3 clicks. This catches expensive terms before they reach the threshold.

Search terms identical to their own keyword or ASIN target are skipped. Negating them would just switch the target off, so the bid rules handle them instead. ASINs are flagged as negative product targets, everything else as negative exact.

**Harvest.** Search terms are grouped across the whole account. A term is suggested when it has enough orders, its ACoS is at or below target, and it is **not already** an exact keyword or ASIN product target. The starting bid is the term's CPC scaled by `target ACoS ÷ actual ACoS`, capped at the maximum bid change.

**Bids.** Each target with enough clicks gets one of three outcomes:

| Situation | Action |
|---|---|
| No orders | Lower by the maximum step |
| ACoS more than 10% above target | Scale down by `target ÷ actual`, capped |
| ACoS more than 20% below target | Scale up by `target ÷ actual`, capped, to buy more traffic |
| Otherwise | Leave alone |

Bids never drop below Amazon's 0.02 minimum.

## Project structure

```
src/SearchTermAnalyzer/
  ReportReader.cs    CSV parsing, header detection, number formats
  Analyzer.cs        Negative, harvest and bid rules
  ReportWriter.cs    Action CSVs and the Markdown summary
  Models.cs          Report rows, metrics, options and results
  Program.cs         Command-line interface
tests/SearchTermAnalyzer.Tests/
  ReportReaderTests.cs
  AnalyzerTests.cs
sample-data/         Synthetic search term report (fictional brand)
sample-output/       Tool output for the sample report
```

Run the tests with:

```bash
dotnet test
```

The tool itself has no external dependencies, only the .NET base library. xUnit is used for tests only.

## Limitations and next steps

- Reads CSV only. Amazon downloads `.xlsx` by default, so save it as CSV first.
- Uses a single target ACoS for the whole account. Per-campaign targets are a natural next step.
- Results are recommendations, not bulk upload files. Generating an Amazon bulk operations sheet is on the roadmap.
- Does not look at search term trends over time. A weekly time unit report would allow that.

## Data

`sample-data/` is **synthetic**: a fictional home goods brand with made-up search terms, ASINs and numbers. No real account or client data is included.

## License

[MIT](LICENSE)
