// ============================================================
//  Tests/PortfolioTests.cs
//  Unit tests for Portfolio aggregate computed properties
// ============================================================

using FinancialPortfolioTracker.Core.Models;
using Xunit;

namespace FinancialPortfolioTracker.Tests;

public class PortfolioTests
{
    private static Investment Stock(string sym, double qty, double buy, double curr) => new()
    {
        Symbol = sym, Name = sym, AssetType = AssetTypes.Stock,
        Sector = "Technology", Quantity = qty,
        PurchasePrice = buy, CurrentPrice = curr,
        PurchaseDate = DateTime.UtcNow.AddDays(-100)
    };

    [Fact]
    public void EmptyPortfolio_HasZeroTotals()
    {
        var p = new Portfolio();
        Assert.Equal(0.0, p.TotalCost);
        Assert.Equal(0.0, p.TotalMarketValue);
        Assert.Equal(0.0, p.TotalUnrealizedPnL);
        Assert.Equal(0.0, p.TotalReturnPercent);
    }

    [Fact]
    public void TotalMarketValue_SumsAllHoldings()
    {
        var p = new Portfolio
        {
            Investments = new()
            {
                Stock("AAPL", 10, 130, 150),  // MV=1500
                Stock("MSFT",  5, 240, 260),  // MV=1300
            }
        };
        Assert.Equal(2800.0, p.TotalMarketValue, precision: 2);
    }

    [Fact]
    public void TotalReturnPercent_IsCorrect()
    {
        var p = new Portfolio
        {
            Investments = new()
            {
                Stock("AAPL", 10, 100, 110),  // cost=1000, value=1100
                Stock("MSFT", 10, 100,  90),  // cost=1000, value=900
            }
        };
        // Net PnL = 0, so return = 0%
        Assert.Equal(0.0, p.TotalReturnPercent, precision: 4);
    }

    [Fact]
    public void AllocationByAssetType_SumsTo100Percent()
    {
        var p = new Portfolio
        {
            Investments = new()
            {
                Stock("AAPL", 10, 100, 200),
                new Investment { Symbol="TLT", AssetType=AssetTypes.Bond,
                    Quantity=5, PurchasePrice=100, CurrentPrice=100,
                    PurchaseDate=DateTime.UtcNow.AddDays(-30) }
            }
        };
        var alloc = p.AllocationByAssetType();
        Assert.True(alloc.ContainsKey(AssetTypes.Stock));
        Assert.True(alloc.ContainsKey(AssetTypes.Bond));
        Assert.InRange(alloc.Values.Sum(), 99.99, 100.01);
    }

    [Fact]
    public void AllocationBySector_HandlesUnknownSector()
    {
        var p = new Portfolio
        {
            Investments = new()
            {
                new Investment { Symbol="X", Sector="", AssetType=AssetTypes.Stock,
                    Quantity=1, PurchasePrice=100, CurrentPrice=100,
                    PurchaseDate=DateTime.UtcNow.AddDays(-10) }
            }
        };
        var alloc = p.AllocationBySector();
        Assert.True(alloc.ContainsKey("Unknown"));
    }

    [Fact]
    public void ToString_ContainsPortfolioName()
    {
        var p = new Portfolio { Name = "My Test Portfolio" };
        Assert.Contains("My Test Portfolio", p.ToString());
    }
}
