// ============================================================
//  Tests/PortfolioAnalyzerTests.cs
//  Tests for risk/return analytics computations
// ============================================================

using FinancialPortfolioTracker.Core;
using FinancialPortfolioTracker.Core.Models;
using FinancialPortfolioTracker.Data.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinancialPortfolioTracker.Tests;

public class PortfolioAnalyzerTests
{
    /// <summary>Builds a deterministic daily return series with given trend.</summary>
    private static List<StockPrice> BuildPriceSeries(string symbol, int days, double dailyReturn = 0.001)
    {
        var prices = new List<StockPrice>();
        double price = 100.0;
        var start = DateTime.UtcNow.AddDays(-days);
        for (int i = 0; i < days; i++)
        {
            double prev = price;
            price *= (1 + dailyReturn + (i % 5 == 0 ? -0.002 : 0)); // slight noise
            prices.Add(new StockPrice
            {
                Symbol      = symbol,
                Date        = start.AddDays(i),
                Open        = (float)prev,
                High        = (float)(price * 1.01),
                Low         = (float)(price * 0.99),
                Close       = (float)price,
                Volume      = 1000000,
                DailyReturn = (float)((price - prev) / prev),
                MA20        = (float)price,
                RSI14       = 55f
            });
        }
        return prices;
    }

    private static Portfolio BuildPortfolio()
    {
        Investment Inv(string sym, double qty, double buy, double curr) => new()
        {
            Symbol = sym, Name = sym, AssetType = AssetTypes.Stock,
            Quantity = qty, PurchasePrice = buy, CurrentPrice = curr,
            PurchaseDate = DateTime.UtcNow.AddDays(-200), Sector = "Technology"
        };

        return new Portfolio
        {
            Name        = "Test Portfolio",
            Investments = new()
            {
                Inv("AAPL", 10, 130, 150),
                Inv("MSFT",  5, 240, 270),
                Inv("GOOGL", 8,  90, 105),
            }
        };
    }

    [Fact]
    public async Task Analyze_EmptyPortfolio_ReturnsZeroMetrics()
    {
        var mockRepo = new Mock<IStockPriceRepository>();
        var analyzer = new PortfolioAnalyzer(mockRepo.Object,
            NullLogger<PortfolioAnalyzer>.Instance);

        var analytics = await analyzer.AnalyzeAsync(new Portfolio());

        Assert.Equal(0, analytics.NumberOfHoldings);
    }

    [Fact]
    public async Task Analyze_PositiveReturn_SharpeIsPositive()
    {
        var mockRepo = new Mock<IStockPriceRepository>();

        // AAPL, MSFT, GOOGL all trending up
        mockRepo.Setup(r => r.GetPricesAsync("AAPL",  It.IsAny<int>()))
                .ReturnsAsync(BuildPriceSeries("AAPL",  250, 0.001));
        mockRepo.Setup(r => r.GetPricesAsync("MSFT",  It.IsAny<int>()))
                .ReturnsAsync(BuildPriceSeries("MSFT",  250, 0.001));
        mockRepo.Setup(r => r.GetPricesAsync("GOOGL", It.IsAny<int>()))
                .ReturnsAsync(BuildPriceSeries("GOOGL", 250, 0.001));
        mockRepo.Setup(r => r.GetPricesAsync("SPY",   It.IsAny<int>()))
                .ReturnsAsync(BuildPriceSeries("SPY",   250, 0.0005));

        var analyzer  = new PortfolioAnalyzer(mockRepo.Object,
            NullLogger<PortfolioAnalyzer>.Instance, riskFreeRate: 0.02);
        var analytics = await analyzer.AnalyzeAsync(BuildPortfolio());

        Assert.True(analytics.AnnualizedReturn > 0, "Annualized return should be positive");
        Assert.True(analytics.SharpeRatio > 0,       "Sharpe ratio should be positive");
        Assert.True(analytics.Volatility > 0,         "Volatility should be positive");
        Assert.InRange(analytics.MaxDrawdown, 0.0, 1.0);
    }

    [Fact]
    public async Task Analyze_HHI_IsCorrectForEqualWeights()
    {
        // With 3 equal-weight holdings: HHI ≈ 3 × (1/3)² = 0.333
        var mockRepo = new Mock<IStockPriceRepository>();
        mockRepo.Setup(r => r.GetPricesAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(BuildPriceSeries("X", 250, 0.001));

        var portfolio = new Portfolio
        {
            Investments = new()
            {
                new Investment { Symbol="A", Quantity=1, PurchasePrice=100, CurrentPrice=100, PurchaseDate=DateTime.UtcNow.AddDays(-50), AssetType=AssetTypes.Stock },
                new Investment { Symbol="B", Quantity=1, PurchasePrice=100, CurrentPrice=100, PurchaseDate=DateTime.UtcNow.AddDays(-50), AssetType=AssetTypes.Stock },
                new Investment { Symbol="C", Quantity=1, PurchasePrice=100, CurrentPrice=100, PurchaseDate=DateTime.UtcNow.AddDays(-50), AssetType=AssetTypes.Stock },
            }
        };

        var analyzer  = new PortfolioAnalyzer(mockRepo.Object, NullLogger<PortfolioAnalyzer>.Instance);
        var analytics = await analyzer.AnalyzeAsync(portfolio);

        Assert.InRange(analytics.HerfindahlIndex, 0.30, 0.36);
    }

    [Fact]
    public async Task Analyze_NumberOfHoldings_IsCorrect()
    {
        var mockRepo = new Mock<IStockPriceRepository>();
        mockRepo.Setup(r => r.GetPricesAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(BuildPriceSeries("X", 250, 0.001));

        var analyzer  = new PortfolioAnalyzer(mockRepo.Object, NullLogger<PortfolioAnalyzer>.Instance);
        var analytics = await analyzer.AnalyzeAsync(BuildPortfolio());

        Assert.Equal(3, analytics.NumberOfHoldings);
    }
}
