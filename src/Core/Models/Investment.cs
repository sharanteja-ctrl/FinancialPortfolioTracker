// ============================================================
//  Core/Models/Investment.cs
//  Domain model representing a single investment holding
// ============================================================

namespace FinancialPortfolioTracker.Core.Models;

/// <summary>
/// Represents a single investment position in the portfolio.
/// </summary>
public class Investment
{
    public int    Id           { get; set; }
    public string Symbol       { get; set; } = string.Empty;
    public string Name         { get; set; } = string.Empty;
    public string AssetType    { get; set; } = AssetTypes.Stock;
    public string Sector       { get; set; } = string.Empty;
    public double Quantity     { get; set; }
    public double PurchasePrice { get; set; }
    public double CurrentPrice  { get; set; }
    public DateTime PurchaseDate { get; set; }
    public DateTime LastUpdated  { get; set; }
    public string Currency       { get; set; } = "USD";
    public string Notes          { get; set; } = string.Empty;

    // ── Computed Properties ─────────────────────────────────
    public double TotalCost        => Quantity * PurchasePrice;
    public double MarketValue      => Quantity * CurrentPrice;
    public double UnrealizedPnL    => MarketValue - TotalCost;
    public double ReturnPercent    => TotalCost == 0 ? 0 : (UnrealizedPnL / TotalCost) * 100.0;
    public double DaysHeld         => (DateTime.UtcNow - PurchaseDate).TotalDays;
    public double AnnualizedReturn => DaysHeld > 0
        ? (Math.Pow(MarketValue / TotalCost, 365.0 / DaysHeld) - 1.0) * 100.0
        : 0.0;

    public override string ToString() =>
        $"[{Symbol}] {Name} | Qty: {Quantity:F2} | Cost: ${TotalCost:N2} | Value: ${MarketValue:N2} | PnL: {UnrealizedPnL:+$#,##0.00;-$#,##0.00}";
}

/// <summary>Well-known asset type constants.</summary>
public static class AssetTypes
{
    public const string Stock  = "Stock";
    public const string ETF    = "ETF";
    public const string Bond   = "Bond";
    public const string Crypto = "Crypto";
    public const string REIT   = "REIT";
    public const string MutualFund = "MutualFund";
    public const string Commodity  = "Commodity";
}
