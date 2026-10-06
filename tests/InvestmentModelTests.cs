// ============================================================
//  Tests/InvestmentModelTests.cs
//  Unit tests for Investment domain model computed properties
// ============================================================

using FinancialPortfolioTracker.Core.Models;
using Xunit;

namespace FinancialPortfolioTracker.Tests;

public class InvestmentModelTests
{
    private Investment MakeInvestment(double qty, double buy, double curr,
                                      DateTime? date = null) => new()
    {
        Symbol        = "TEST",
        Name          = "Test Asset",
        AssetType     = AssetTypes.Stock,
        Quantity      = qty,
        PurchasePrice = buy,
        CurrentPrice  = curr,
        PurchaseDate  = date ?? DateTime.UtcNow.AddDays(-365)
    };

    [Fact]
    public void TotalCost_IsQtyTimesBuyPrice()
    {
        var inv = MakeInvestment(10, 100, 150);
        Assert.Equal(1000.0, inv.TotalCost, precision: 2);
    }

    [Fact]
    public void MarketValue_IsQtyTimesCurrentPrice()
    {
        var inv = MakeInvestment(10, 100, 150);
        Assert.Equal(1500.0, inv.MarketValue, precision: 2);
    }

    [Fact]
    public void UnrealizedPnL_PositiveWhenPriceRises()
    {
        var inv = MakeInvestment(10, 100, 120);
        Assert.Equal(200.0, inv.UnrealizedPnL, precision: 2);
    }

    [Fact]
    public void UnrealizedPnL_NegativeWhenPriceFalls()
    {
        var inv = MakeInvestment(10, 100, 80);
        Assert.Equal(-200.0, inv.UnrealizedPnL, precision: 2);
    }

    [Fact]
    public void ReturnPercent_CorrectCalculation()
    {
        var inv = MakeInvestment(10, 100, 110);
        Assert.Equal(10.0, inv.ReturnPercent, precision: 4);
    }

    [Fact]
    public void ReturnPercent_ZeroWhenCostIsZero()
    {
        var inv = MakeInvestment(0, 0, 100);
        Assert.Equal(0.0, inv.ReturnPercent, precision: 4);
    }

    [Theory]
    [InlineData(10, 100, 150, 1000, 1500,  500)]
    [InlineData(5,  200,  180, 1000,  900, -100)]
    [InlineData(1,   50,   50,   50,   50,    0)]
    public void PnL_Calculations_AreCorrect(
        double qty, double buy, double curr,
        double expCost, double expValue, double expPnl)
    {
        var inv = MakeInvestment(qty, buy, curr);
        Assert.Equal(expCost,  inv.TotalCost,      precision: 2);
        Assert.Equal(expValue, inv.MarketValue,    precision: 2);
        Assert.Equal(expPnl,   inv.UnrealizedPnL,  precision: 2);
    }

    [Fact]
    public void AnnualizedReturn_IsReasonableForOneYear()
    {
        // Buy at 100, now 110, held 365 days → ~10% annualised
        var inv = MakeInvestment(1, 100, 110, DateTime.UtcNow.AddDays(-365));
        Assert.InRange(inv.AnnualizedReturn, 8.0, 12.0);
    }

    [Fact]
    public void ToString_ContainsSymbol()
    {
        var inv = MakeInvestment(1, 100, 100);
        Assert.Contains("TEST", inv.ToString());
    }
}
