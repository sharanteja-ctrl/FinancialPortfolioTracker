// ============================================================
//  Core/Models/PortfolioAnalytics.cs
//  Risk metrics and performance analytics for a portfolio
// ============================================================

namespace FinancialPortfolioTracker.Core.Models;

/// <summary>
/// Comprehensive analytics computed from historical price data.
/// </summary>
public class PortfolioAnalytics
{
    // ── Return Metrics ───────────────────────────────────────
    public double TotalReturnPercent  { get; set; }
    public double AnnualizedReturn    { get; set; }
    public double DailyReturn         { get; set; }
    public double WeeklyReturn        { get; set; }
    public double MonthlyReturn       { get; set; }
    public double YTDReturn           { get; set; }

    // ── Risk Metrics ─────────────────────────────────────────
    public double Volatility          { get; set; }   // Annualised std dev
    public double SharpeRatio         { get; set; }
    public double SortinoRatio        { get; set; }
    public double MaxDrawdown         { get; set; }
    public double ValueAtRisk95       { get; set; }   // 95% VaR (1-day)
    public double Beta                { get; set; }
    public double Alpha               { get; set; }
    public double InformationRatio    { get; set; }
    public double TreynorRatio        { get; set; }

    // ── Diversity Metrics ────────────────────────────────────
    public double HerfindahlIndex     { get; set; }   // Concentration measure
    public int    NumberOfHoldings    { get; set; }
    public double EffectiveDiversification { get; set; }

    // ── Benchmark Comparison ─────────────────────────────────
    public string BenchmarkSymbol     { get; set; } = "SPY";
    public double BenchmarkReturn     { get; set; }
    public double ExcessReturn        { get; set; }   // Alpha over benchmark

    // ── Sector / Asset-Type Weights ──────────────────────────
    public Dictionary<string, double> SectorWeights    { get; set; } = new();
    public Dictionary<string, double> AssetTypeWeights { get; set; } = new();

    // ── Per-Holding Contributions ────────────────────────────
    public List<HoldingContribution> HoldingContributions { get; set; } = new();

    // ── Computed at ─────────────────────────────────────────
    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Convenience risk tier based on Sharpe & Volatility.</summary>
    public string RiskTier => (Volatility, SharpeRatio) switch
    {
        (< 0.10, > 1.0)  => "Conservative",
        (< 0.20, > 0.5)  => "Moderate",
        (< 0.30, > 0.0)  => "Aggressive",
        _                => "High Risk"
    };
}

/// <summary>Per-holding contribution to portfolio return and risk.</summary>
public class HoldingContribution
{
    public string Symbol            { get; set; } = string.Empty;
    public double WeightPercent     { get; set; }
    public double ReturnPercent     { get; set; }
    public double ReturnContribution { get; set; }  // weight × return
    public double RiskContribution  { get; set; }
    public double CorrelationToBenchmark { get; set; }
}
