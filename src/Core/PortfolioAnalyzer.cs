// ============================================================
//  Core/PortfolioAnalyzer.cs
//  Computes risk/return analytics using MathNet.Numerics
// ============================================================

using FinancialPortfolioTracker.Core.Models;
using FinancialPortfolioTracker.Data.Repositories;
using MathNet.Numerics.Statistics;
using Microsoft.Extensions.Logging;

namespace FinancialPortfolioTracker.Core;

/// <summary>
/// Computes comprehensive portfolio analytics:
/// Sharpe, Sortino, Beta, Alpha, VaR, MaxDrawdown, etc.
/// Uses MathNet.Numerics for statistical computations.
/// </summary>
public class PortfolioAnalyzer
{
    private readonly IStockPriceRepository _priceRepo;
    private readonly ILogger<PortfolioAnalyzer> _logger;
    private readonly double _riskFreeRate;    // annualised
    private readonly string _benchmarkSymbol;

    public PortfolioAnalyzer(
        IStockPriceRepository priceRepo,
        ILogger<PortfolioAnalyzer> logger,
        double riskFreeRate    = 0.05,
        string benchmarkSymbol = "SPY")
    {
        _priceRepo       = priceRepo;
        _logger          = logger;
        _riskFreeRate    = riskFreeRate;
        _benchmarkSymbol = benchmarkSymbol;
    }

    // ──────────────────────────────────────────────────────────
    //  Main Analysis Entry-Point
    // ──────────────────────────────────────────────────────────
    public async Task<PortfolioAnalytics> AnalyzeAsync(Portfolio portfolio, int historyDays = 365)
    {
        _logger.LogInformation("Analyzing portfolio '{Name}' ({Count} holdings)…",
            portfolio.Name, portfolio.Investments.Count);

        var analytics = new PortfolioAnalytics
        {
            NumberOfHoldings   = portfolio.Investments.Count,
            SectorWeights      = portfolio.AllocationBySector(),
            AssetTypeWeights   = portfolio.AllocationByAssetType(),
        };

        if (!portfolio.Investments.Any())
            return analytics;

        // ── Gather historical returns for each holding ─────────
        var holdingReturns = new Dictionary<string, double[]>();
        foreach (var inv in portfolio.Investments)
        {
            var prices = (await _priceRepo.GetPricesAsync(inv.Symbol, historyDays)).ToList();
            if (prices.Count > 1)
                holdingReturns[inv.Symbol] = prices.Skip(1)
                    .Select(p => (double)p.DailyReturn).ToArray();
        }

        // ── Portfolio-level weighted returns ───────────────────
        var totalValue  = portfolio.TotalMarketValue;
        var weights     = portfolio.Investments
            .Where(i => holdingReturns.ContainsKey(i.Symbol))
            .ToDictionary(
                i => i.Symbol,
                i => totalValue > 0 ? i.MarketValue / totalValue : 0.0);

        int minLen = holdingReturns.Values.Select(r => r.Length).DefaultIfEmpty(0).Min();
        if (minLen < 20)
        {
            _logger.LogWarning("Insufficient return history for analytics");
            return analytics;
        }

        // Align all return series to the same length
        var portReturns = new double[minLen];
        foreach (var (sym, rets) in holdingReturns)
        {
            if (!weights.TryGetValue(sym, out var w)) continue;
            var aligned = rets.TakeLast(minLen).ToArray();
            for (int i = 0; i < minLen; i++)
                portReturns[i] += w * aligned[i];
        }

        // ── Return metrics ─────────────────────────────────────
        analytics.DailyReturn   = portReturns.LastOrDefault();
        analytics.WeeklyReturn  = portReturns.TakeLast(5).Sum();
        analytics.MonthlyReturn = portReturns.TakeLast(21).Sum();
        analytics.AnnualizedReturn = portReturns.Mean() * 252 * 100.0;
        analytics.TotalReturnPercent = portfolio.TotalReturnPercent;

        // YTD
        var ytdStart    = new DateTime(DateTime.UtcNow.Year, 1, 1);
        var ytdDays     = (int)(DateTime.UtcNow - ytdStart).TotalDays;
        analytics.YTDReturn = portReturns.TakeLast(Math.Min(ytdDays, minLen)).Sum() * 100.0;

        // ── Risk metrics ───────────────────────────────────────
        double annualFactor   = Math.Sqrt(252);
        double dailyStd       = portReturns.StandardDeviation();
        analytics.Volatility  = dailyStd * annualFactor;

        double dailyRFR       = _riskFreeRate / 252.0;
        double excessMeanReturn = portReturns.Mean() - dailyRFR;
        analytics.SharpeRatio = dailyStd > 0
            ? excessMeanReturn / dailyStd * annualFactor
            : 0;

        // Sortino: downside deviation only
        var downside = portReturns.Where(r => r < dailyRFR).ToArray();
        double downsideStd   = downside.Any()
            ? Math.Sqrt(downside.Select(r => (r - dailyRFR) * (r - dailyRFR)).Average())
            : double.Epsilon;
        analytics.SortinoRatio = excessMeanReturn / downsideStd * annualFactor;

        // VaR (parametric, 95%)
        analytics.ValueAtRisk95 = -(portReturns.Mean() + 1.645 * dailyStd) * totalValue;

        // Max Drawdown
        analytics.MaxDrawdown = ComputeMaxDrawdown(portReturns);

        // ── Benchmark comparison ───────────────────────────────
        var benchPrices = (await _priceRepo.GetPricesAsync(_benchmarkSymbol, historyDays)).ToList();
        if (benchPrices.Count > 1)
        {
            var benchReturns = benchPrices.Skip(1)
                .Select(p => (double)p.DailyReturn)
                .TakeLast(minLen).ToArray();

            analytics.BenchmarkSymbol = _benchmarkSymbol;
            analytics.BenchmarkReturn  = benchReturns.Mean() * 252 * 100.0;

            // Beta
            analytics.Beta  = ComputeBeta(portReturns, benchReturns);
            // Alpha (Jensen's)
            analytics.Alpha = analytics.AnnualizedReturn - 100.0 * (
                _riskFreeRate + analytics.Beta * (analytics.BenchmarkReturn / 100.0 - _riskFreeRate));

            analytics.ExcessReturn = analytics.AnnualizedReturn - analytics.BenchmarkReturn;

            // Treynor Ratio
            analytics.TreynorRatio = analytics.Beta != 0
                ? (analytics.AnnualizedReturn / 100.0 - _riskFreeRate) / analytics.Beta
                : 0;

            // Information Ratio
            var trackingErr = portReturns.Zip(benchReturns, (p, b) => p - b).StandardDeviation();
            analytics.InformationRatio = trackingErr > 0
                ? (portReturns.Mean() - benchReturns.Mean()) / trackingErr * annualFactor
                : 0;
        }

        // ── Diversification (Herfindahl–Hirschman Index) ───────
        var weights2 = portfolio.Investments
            .Select(i => totalValue > 0 ? i.MarketValue / totalValue : 0.0)
            .ToArray();
        analytics.HerfindahlIndex = weights2.Sum(w => w * w);  // 1/N = perfect diversification
        analytics.EffectiveDiversification = analytics.HerfindahlIndex > 0
            ? 1.0 / analytics.HerfindahlIndex
            : 0;

        // ── Per-Holding contributions ──────────────────────────
        analytics.HoldingContributions = portfolio.Investments.Select(inv =>
        {
            var w = weights.GetValueOrDefault(inv.Symbol, 0.0);
            return new HoldingContribution
            {
                Symbol              = inv.Symbol,
                WeightPercent       = w * 100.0,
                ReturnPercent       = inv.ReturnPercent,
                ReturnContribution  = w * inv.ReturnPercent
            };
        }).OrderByDescending(c => Math.Abs(c.ReturnContribution)).ToList();

        _logger.LogInformation("Analysis complete: Sharpe={S:F2}, VaR95=${V:N0}, MaxDD={D:P1}",
            analytics.SharpeRatio, analytics.ValueAtRisk95, analytics.MaxDrawdown);

        return analytics;
    }

    // ── Statistical helpers ──────────────────────────────────
    private static double ComputeMaxDrawdown(double[] returns)
    {
        double peak    = 1.0;
        double nav     = 1.0;
        double maxDD   = 0.0;
        foreach (var r in returns)
        {
            nav  *= (1.0 + r);
            peak  = Math.Max(peak, nav);
            double dd = (peak - nav) / peak;
            maxDD = Math.Max(maxDD, dd);
        }
        return maxDD;
    }

    private static double ComputeBeta(double[] portReturns, double[] benchReturns)
    {
        int len = Math.Min(portReturns.Length, benchReturns.Length);
        if (len < 5) return 1.0;

        var p = portReturns.TakeLast(len).ToArray();
        var b = benchReturns.TakeLast(len).ToArray();

        double covPB    = p.Zip(b, (pi, bi) => (pi - p.Mean()) * (bi - b.Mean())).Average();
        double varBench = b.Variance();
        return varBench > 0 ? covPB / varBench : 1.0;
    }
}
