// ============================================================
//  Core/Models/Portfolio.cs
//  Aggregate root containing all investments and analytics
// ============================================================

namespace FinancialPortfolioTracker.Core.Models;

/// <summary>
/// The portfolio aggregate – holds investments and computed analytics.
/// </summary>
public class Portfolio
{
    public int      Id          { get; set; }
    public string   Name        { get; set; } = "My Portfolio";
    public string   Description { get; set; } = string.Empty;
    public string   Currency    { get; set; } = "USD";
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    public List<Investment> Investments { get; set; } = new();

    // ── Computed Totals ──────────────────────────────────────
    public double TotalCost       => Investments.Sum(i => i.TotalCost);
    public double TotalMarketValue => Investments.Sum(i => i.MarketValue);
    public double TotalUnrealizedPnL => Investments.Sum(i => i.UnrealizedPnL);
    public double TotalReturnPercent  =>
        TotalCost == 0 ? 0 : (TotalUnrealizedPnL / TotalCost) * 100.0;

    // ── Analytics (populated by PortfolioAnalyzer) ───────────
    public PortfolioAnalytics? Analytics { get; set; }

    public Dictionary<string, double> AllocationByAssetType()
    {
        var total = TotalMarketValue;
        if (total == 0) return new();
        return Investments
            .GroupBy(i => i.AssetType)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(i => i.MarketValue) / total * 100.0);
    }

    public Dictionary<string, double> AllocationBySector()
    {
        var total = TotalMarketValue;
        if (total == 0) return new();
        return Investments
            .GroupBy(i => string.IsNullOrEmpty(i.Sector) ? "Unknown" : i.Sector)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(i => i.MarketValue) / total * 100.0);
    }

    public override string ToString() =>
        $"Portfolio: {Name} | Holdings: {Investments.Count} | " +
        $"Value: ${TotalMarketValue:N2} | PnL: {TotalUnrealizedPnL:+$#,##0.00;-$#,##0.00} ({TotalReturnPercent:+0.00;-0.00}%)";
}
