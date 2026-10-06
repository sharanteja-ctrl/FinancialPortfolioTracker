// ============================================================
//  UI/ConsoleUI.cs
//  Rich console interface with tables, charts, and colour coding
// ============================================================

using FinancialPortfolioTracker.Core.Models;
using FinancialPortfolioTracker.ML;

namespace FinancialPortfolioTracker.UI;

/// <summary>
/// Console UI helper: renders tables, charts, and styled output.
/// </summary>
public static class ConsoleUI
{
    // ── Colour helpers ────────────────────────────────────────
    private static int SafeWidth => GetSafeWidth();
    private static int GetSafeWidth()
    {
        try { return Console.IsOutputRedirected ? 80 : Math.Max(20, Math.Min(Console.WindowWidth - 1, 80)); }
        catch { return 80; }
    }

    public static void PrintHeader(string text)
    {
        Console.WriteLine();
        var line = new string('═', SafeWidth);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(line);
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"  {text}");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(line);
        Console.ResetColor();
    }

    public static void PrintSection(string text)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  ▶  {text}");
        Console.ResetColor();
        Console.WriteLine(new string('─', SafeWidth));
    }

    public static void PrintSuccess(string text) =>
        PrintColoured($"  ✔  {text}", ConsoleColor.Green);

    public static void PrintWarning(string text) =>
        PrintColoured($"  ⚠  {text}", ConsoleColor.Yellow);

    public static void PrintError(string text) =>
        PrintColoured($"  ✖  {text}", ConsoleColor.Red);

    public static void PrintInfo(string text) =>
        PrintColoured($"  ℹ  {text}", ConsoleColor.Gray);

    private static void PrintColoured(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    // ── Portfolio Summary ─────────────────────────────────────
    public static void PrintPortfolioSummary(Portfolio portfolio)
    {
        PrintHeader($"Portfolio: {portfolio.Name}");

        var pnlColor = portfolio.TotalUnrealizedPnL >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
        var pnlSign  = portfolio.TotalUnrealizedPnL >= 0 ? "+" : "";

        Console.WriteLine($"  Holdings    : {portfolio.Investments.Count}");
        Console.WriteLine($"  Total Cost  : ${portfolio.TotalCost:N2}");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"  Market Value: ${portfolio.TotalMarketValue:N2}");
        Console.ForegroundColor = pnlColor;
        Console.WriteLine($"  Unrealised PnL : {pnlSign}${portfolio.TotalUnrealizedPnL:N2} ({pnlSign}{portfolio.TotalReturnPercent:F2}%)");
        Console.ResetColor();
    }

    // ── Holdings Table ────────────────────────────────────────
    public static void PrintHoldingsTable(IEnumerable<Investment> investments)
    {
        PrintSection("Holdings");

        var header = $"  {"Symbol",-8} {"Name",-22} {"Type",-10} {"Qty":>8} {"Buy $":>10} {"Curr $":>10} {"Value $":>12} {"P&L":>12} {"Ret%":>8}";
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length));
        Console.ResetColor();

        foreach (var inv in investments.OrderByDescending(i => i.MarketValue))
        {
            var color = inv.UnrealizedPnL >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
            Console.ForegroundColor = color;
            var pnlStr = inv.UnrealizedPnL >= 0
                ? $"+${inv.UnrealizedPnL:N2}"
                : $"-${Math.Abs(inv.UnrealizedPnL):N2}";
            Console.WriteLine(
                $"  {inv.Symbol,-8} {inv.Name,-22} {inv.AssetType,-10} " +
                $"{inv.Quantity,8:F2} {inv.PurchasePrice,10:F2} {inv.CurrentPrice,10:F2} " +
                $"{inv.MarketValue,12:N2} {pnlStr,12} {inv.ReturnPercent,7:+0.00;-0.00}%");
            Console.ResetColor();
        }
    }

    // ── Analytics Table ───────────────────────────────────────
    public static void PrintAnalytics(PortfolioAnalytics a)
    {
        PrintSection("Risk & Return Analytics");

        void Metric(string label, string value, ConsoleColor? color = null)
        {
            Console.Write($"  {label,-35}: ");
            if (color.HasValue) Console.ForegroundColor = color.Value;
            Console.WriteLine(value);
            Console.ResetColor();
        }

        Metric("Annualised Return",    $"{a.AnnualizedReturn:+0.00;-0.00}%",
               a.AnnualizedReturn >= 0 ? ConsoleColor.Green : ConsoleColor.Red);
        Metric("YTD Return",           $"{a.YTDReturn:+0.00;-0.00}%");
        Metric("Volatility (Annual)",  $"{a.Volatility:P2}");
        Metric("Sharpe Ratio",         $"{a.SharpeRatio:F2}",
               a.SharpeRatio >= 1 ? ConsoleColor.Green : a.SharpeRatio >= 0.5 ? ConsoleColor.Yellow : ConsoleColor.Red);
        Metric("Sortino Ratio",        $"{a.SortinoRatio:F2}");
        Metric("Beta",                 $"{a.Beta:F2}");
        Metric("Alpha (Jensen's)",     $"{a.Alpha:+0.00;-0.00}%",
               a.Alpha >= 0 ? ConsoleColor.Green : ConsoleColor.Red);
        Metric("Treynor Ratio",        $"{a.TreynorRatio:F4}");
        Metric("Information Ratio",    $"{a.InformationRatio:F2}");
        Metric("Max Drawdown",         $"{a.MaxDrawdown:P2}",
               a.MaxDrawdown < 0.10 ? ConsoleColor.Green :
               a.MaxDrawdown < 0.20 ? ConsoleColor.Yellow : ConsoleColor.Red);
        Metric("Value at Risk (95%)",  $"${a.ValueAtRisk95:N2}");
        Metric("Herfindahl Index",     $"{a.HerfindahlIndex:F4}");
        Metric("Effective Diversif.",  $"{a.EffectiveDiversification:F1} equivalent stocks");
        Metric("Risk Tier",            a.RiskTier);
        Metric($"vs {a.BenchmarkSymbol}",
               $"Benchmark: {a.BenchmarkReturn:F2}% | Excess: {a.ExcessReturn:+0.00;-0.00}%",
               a.ExcessReturn >= 0 ? ConsoleColor.Green : ConsoleColor.Red);
    }

    // ── Allocation Bar Chart ──────────────────────────────────
    public static void PrintAllocationChart(string title, Dictionary<string, double> allocations)
    {
        PrintSection(title);
        const int barWidth = 40;
        foreach (var (label, pct) in allocations.OrderByDescending(kv => kv.Value))
        {
            int filled = (int)Math.Round(pct / 100.0 * barWidth);
            string bar  = new string('█', filled) + new string('░', barWidth - filled);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"  {label,-18} ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"{bar}  {pct:F1}%");
            Console.ResetColor();
        }
    }

    // ── Recommendations ───────────────────────────────────────
    public static void PrintRecommendations(IEnumerable<Recommendation> recs)
    {
        PrintSection("ML Recommendations");

        int idx = 1;
        foreach (var r in recs)
        {
            var color = r.Type switch
            {
                RecommendationType.StrongBuy or RecommendationType.Buy         => ConsoleColor.Green,
                RecommendationType.StrongSell or RecommendationType.Sell        => ConsoleColor.Red,
                RecommendationType.RiskAlert or RecommendationType.DiversifyWarning => ConsoleColor.Yellow,
                RecommendationType.Rebalance                                     => ConsoleColor.Magenta,
                _                                                                => ConsoleColor.White
            };

            Console.ForegroundColor = color;
            Console.WriteLine($"\n  [{idx++}] [{r.Type}] {r.Symbol}: {r.Title}");
            Console.ResetColor();
            Console.WriteLine($"       {r.Description}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"       Confidence: {r.ConfidenceScore:P0} | Risk: {r.RiskLevel} | Horizon: {r.TimeHorizon}");
            if (r.TargetPrice > 0)
                Console.WriteLine($"       Target: ${r.TargetPrice:N2} | Stop-Loss: ${r.StopLoss:N2} | Gain: {r.PotentialGainPercent:+0.00;-0.00}%");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            foreach (var reason in r.Reasoning)
                Console.WriteLine($"        • {reason}");
            Console.ResetColor();
        }

        if (!recs.Any())
            PrintInfo("No recommendations at this time. Portfolio looks healthy.");
    }

    // ── Model Training Results ────────────────────────────────
    public static void PrintTrainingResults(IEnumerable<ModelTrainingResult> results)
    {
        PrintSection("ML Model Training Results");
        var header = $"  {"Symbol",-8} {"Status",-10} {"R²":>8} {"RMSE":>10} {"MAE":>10} {"Rows":>8} {"Time(ms)":>10}";
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length));
        Console.ResetColor();

        foreach (var r in results)
        {
            var color = r.Success
                ? (r.RSquared >= 0.7 ? ConsoleColor.Green : ConsoleColor.Yellow)
                : ConsoleColor.Red;
            Console.ForegroundColor = color;
            Console.WriteLine(
                $"  {r.Symbol,-8} {(r.Success ? "✔ OK" : "✖ FAIL"),-10} " +
                $"{r.RSquared,8:F4} {r.RMSE,10:F4} {r.MAE,10:F4} {r.TrainRows,8} {r.TrainTimeMs,10}");
            Console.ResetColor();
        }
    }

    // ── Main Menu ─────────────────────────────────────────────
    public static string PrintMenu()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ╔══════════════════════════════════════════╗");
        Console.WriteLine("  ║     Financial Portfolio Tracker v1.0     ║");
        Console.WriteLine("  ╠══════════════════════════════════════════╣");
        Console.ResetColor();
        var options = new[]
        {
            "1. View Portfolio Summary",
            "2. Add Investment",
            "3. Update Prices from DB",
            "4. Load Historical Data (CSV)",
            "5. Train ML Models",
            "6. Run Full Analysis",
            "7. View Recommendations",
            "8. Anomaly Detection",
            "9. Cluster Analysis",
            "0. Exit"
        };
        foreach (var opt in options)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  ║  {opt,-40}║");
        }
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ╚══════════════════════════════════════════╝");
        Console.ResetColor();
        Console.Write("\n  Select option: ");
        return Console.ReadLine()?.Trim() ?? "0";
    }
}
