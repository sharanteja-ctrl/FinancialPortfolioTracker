// ============================================================
//  Core/RecommendationEngine.cs
//  Combines ML signals + analytics to generate recommendations
// ============================================================

using FinancialPortfolioTracker.Core.Models;
using FinancialPortfolioTracker.Data.Repositories;
using FinancialPortfolioTracker.ML;
using Microsoft.Extensions.Logging;

namespace FinancialPortfolioTracker.Core;

/// <summary>
/// Synthesises ML price predictions, anomaly signals, cluster analysis,
/// and portfolio analytics into actionable investment recommendations.
/// </summary>
public class RecommendationEngine
{
    private readonly PricePredictionEngine _predictor;
    private readonly AnomalyDetectionEngine _anomalyEngine;
    private readonly ClusteringEngine _clusterEngine;
    private readonly ILogger<RecommendationEngine> _logger;

    // Configurable thresholds
    private const double BuyThreshold    = 0.02;   // +2% predicted gain
    private const double SellThreshold   = -0.02;  // -2% predicted loss
    private const double HighVolThreshold = 0.30;  // >30% annualised vol = high risk
    private const double LowSharpe        = 0.5;   // Sharpe < 0.5 → review holding

    public RecommendationEngine(
        PricePredictionEngine predictor,
        AnomalyDetectionEngine anomalyEngine,
        ClusteringEngine clusterEngine,
        ILogger<RecommendationEngine> logger)
    {
        _predictor     = predictor;
        _anomalyEngine = anomalyEngine;
        _clusterEngine = clusterEngine;
        _logger        = logger;
    }

    // ──────────────────────────────────────────────────────────
    //  Generate all recommendations for a portfolio
    // ──────────────────────────────────────────────────────────
    public async Task<List<Recommendation>> GenerateAsync(
        Portfolio portfolio, PortfolioAnalytics analytics)
    {
        var recommendations = new List<Recommendation>();

        // ── 1. ML price-prediction signals ─────────────────────
        var predictionTasks = portfolio.Investments
            .Select(inv => GeneratePriceRecommendationAsync(inv))
            .ToList();

        var priceRecs = await Task.WhenAll(predictionTasks);
        recommendations.AddRange(priceRecs.Where(r => r != null)!);

        // ── 2. Anomaly signals ─────────────────────────────────
        foreach (var inv in portfolio.Investments)
        {
            var spikes = await _anomalyEngine.DetectSpikesAsync(inv.Symbol, 90);
            var recent = spikes.Where(a => a.Date >= DateTime.UtcNow.AddDays(-7)).ToList();
            if (recent.Any(a => a.Severity == AnomalySeverity.High))
            {
                recommendations.Add(new Recommendation
                {
                    Symbol          = inv.Symbol,
                    Type            = RecommendationType.RiskAlert,
                    Title           = $"Anomalous price movement detected in {inv.Symbol}",
                    Description     = $"A significant price spike/dip was detected within the last 7 days. " +
                                      $"Monitor closely before adding to position.",
                    ConfidenceScore = 0.85,
                    CurrentPrice    = inv.CurrentPrice,
                    TargetPrice     = inv.CurrentPrice,
                    RiskLevel       = "High",
                    TimeHorizon     = "Immediate",
                    Reasoning       = new List<string>
                    {
                        $"High-severity anomaly on {recent.First().Date:yyyy-MM-dd}",
                        $"Anomaly score: {recent.First().Score:F2}",
                        "Consider reducing position or placing stop-loss"
                    }
                });
            }
        }

        // ── 3. Clustering – diversification warnings ───────────
        if (portfolio.Investments.Count >= 4)
        {
            var symbols  = portfolio.Investments.Select(i => i.Symbol);
            var clusters = await _clusterEngine.ClusterHoldingsAsync(symbols);

            foreach (var group in clusters.ConcentratedGroups)
            {
                recommendations.Add(new Recommendation
                {
                    Symbol          = string.Join(", ", group),
                    Type            = RecommendationType.DiversifyWarning,
                    Title           = "Concentration Risk: Highly Correlated Holdings",
                    Description     = $"Holdings [{string.Join(", ", group)}] exhibit very similar " +
                                      $"return/risk profiles. In a downturn, these will fall together.",
                    ConfidenceScore = 0.75,
                    CurrentPrice    = 0,
                    TargetPrice     = 0,
                    RiskLevel       = "Medium",
                    TimeHorizon     = "Long-term",
                    Reasoning       = new List<string>
                    {
                        "K-Means cluster analysis detected high behavioural similarity",
                        $"Group size: {group.Count} holdings in same cluster",
                        "Consider replacing one holding with a lower-correlation asset"
                    }
                });
            }
        }

        // ── 4. Portfolio-level recommendations ─────────────────
        if (analytics.SharpeRatio < LowSharpe)
        {
            recommendations.Add(new Recommendation
            {
                Symbol          = "PORTFOLIO",
                Type            = RecommendationType.Rebalance,
                Title           = $"Low Sharpe Ratio ({analytics.SharpeRatio:F2}) — Portfolio Rebalancing Needed",
                Description     = "Your portfolio's risk-adjusted returns are below the acceptable threshold. " +
                                  "Consider rebalancing to higher-quality assets or reducing volatility.",
                ConfidenceScore = 0.80,
                RiskLevel       = analytics.RiskTier,
                TimeHorizon     = "1-3 months",
                Reasoning       = BuildPortfolioReasons(analytics)
            });
        }

        if (analytics.HerfindahlIndex > 0.25)
        {
            recommendations.Add(new Recommendation
            {
                Symbol          = "PORTFOLIO",
                Type            = RecommendationType.DiversifyWarning,
                Title           = "Portfolio Concentration Risk (HHI > 0.25)",
                Description     = $"The Herfindahl-Hirschman Index is {analytics.HerfindahlIndex:F3}. " +
                                  $"Your portfolio is insufficiently diversified.",
                ConfidenceScore = 0.90,
                RiskLevel       = "High",
                TimeHorizon     = "Immediate",
                Reasoning       = new List<string>
                {
                    $"HHI = {analytics.HerfindahlIndex:F3} (ideal < 0.1 for 10 holdings)",
                    $"Effective diversification ≈ {analytics.EffectiveDiversification:F1} stocks",
                    "Add 3–5 uncorrelated assets to reduce concentration"
                }
            });
        }

        if (analytics.MaxDrawdown > 0.20)
        {
            recommendations.Add(new Recommendation
            {
                Symbol          = "PORTFOLIO",
                Type            = RecommendationType.RiskAlert,
                Title           = $"High Historical Max Drawdown ({analytics.MaxDrawdown:P0})",
                Description     = "The portfolio experienced a significant historical drawdown. " +
                                  "Consider defensive hedges or stop-loss strategies.",
                ConfidenceScore = 0.70,
                RiskLevel       = "High",
                TimeHorizon     = "Short-term",
                Reasoning       = new List<string>
                {
                    $"Max Drawdown: {analytics.MaxDrawdown:P1}",
                    $"Volatility: {analytics.Volatility:P1} annualised",
                    "Consider adding bond ETFs or defensive sectors"
                }
            });
        }

        // Sort by confidence descending
        recommendations = recommendations
            .OrderByDescending(r => r.ConfidenceScore)
            .ThenBy(r => r.Symbol)
            .ToList();

        _logger.LogInformation("Generated {Count} recommendations", recommendations.Count);
        return recommendations;
    }

    // ──────────────────────────────────────────────────────────
    //  Per-holding price-prediction recommendation
    // ──────────────────────────────────────────────────────────
    private async Task<Recommendation?> GeneratePriceRecommendationAsync(Investment inv)
    {
        try
        {
            var forecast = await _predictor.PredictAsync(inv.Symbol);
            if (forecast == null) return null;

            double change = forecast.ChangePercent / 100.0;
            var type = change switch
            {
                > 0.05  => RecommendationType.StrongBuy,
                > BuyThreshold  => RecommendationType.Buy,
                < -0.05 => RecommendationType.StrongSell,
                < SellThreshold => RecommendationType.Sell,
                _               => RecommendationType.Hold
            };

            double confidence = Math.Min(0.95, 0.50 + Math.Abs(change) * 5.0);
            double stopLoss   = forecast.PredictedPrice * 0.95;  // 5% below predicted
            double target     = forecast.PredictedPrice;

            return new Recommendation
            {
                Symbol          = inv.Symbol,
                Type            = type,
                Title           = $"{type} Signal: {inv.Symbol} ({forecast.ChangePercent:+0.00;-0.00}% predicted)",
                Description     = $"ML model predicts {inv.Symbol} will move from ${forecast.CurrentPrice:N2} " +
                                  $"to ${forecast.PredictedPrice:N2} ({forecast.ChangePercent:+0.00;-0.00}%) " +
                                  $"by {forecast.PredictionDate:yyyy-MM-dd}.",
                ConfidenceScore = confidence,
                PredictedPrice  = forecast.PredictedPrice,
                CurrentPrice    = forecast.CurrentPrice,
                TargetPrice     = target,
                StopLoss        = stopLoss,
                RiskLevel       = Math.Abs(change) > 0.05 ? "High" : "Moderate",
                TimeHorizon     = "1 day",
                Reasoning       = new List<string>
                {
                    $"FastTree regression model: R² trained on {forecast.ConfidenceNote}",
                    $"Predicted change: {forecast.ChangePercent:+0.00;-0.00}%",
                    $"Stop-loss set at ${stopLoss:N2} (5% below predicted)",
                    $"Current holding P&L: {inv.ReturnPercent:+0.00;-0.00}%"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Could not generate price recommendation for {Symbol}: {Msg}", inv.Symbol, ex.Message);
            return null;
        }
    }

    private static List<string> BuildPortfolioReasons(PortfolioAnalytics a) =>
        new()
        {
            $"Sharpe Ratio: {a.SharpeRatio:F2} (target > 1.0)",
            $"Volatility: {a.Volatility:P1} annualised",
            $"Max Drawdown: {a.MaxDrawdown:P1}",
            $"Consider shifting allocation toward lower-beta, dividend-paying assets",
            $"Benchmark ({a.BenchmarkSymbol}) return: {a.BenchmarkReturn:F2}%"
        };
}
