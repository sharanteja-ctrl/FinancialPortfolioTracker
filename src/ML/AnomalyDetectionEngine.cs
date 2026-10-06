// ============================================================
//  ML/AnomalyDetectionEngine.cs
//  ML.NET-based anomaly detection for price series
//  Supports IID spike/change-point detection with statistical fallback
// ============================================================

using FinancialPortfolioTracker.Core.Models;
using FinancialPortfolioTracker.Data.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.TimeSeries;

namespace FinancialPortfolioTracker.ML;

/// <summary>
/// Detects anomalous price movements in historical data using
/// ML.NET's TimeSeries algorithms (IID Spike & ChangePoint Detection)
/// with adaptive statistical Z-score fallback.
/// </summary>
public class AnomalyDetectionEngine
{
    private readonly MLContext _ml;
    private readonly IStockPriceRepository _priceRepo;
    private readonly ILogger<AnomalyDetectionEngine> _logger;

    public AnomalyDetectionEngine(
        IStockPriceRepository priceRepo,
        ILogger<AnomalyDetectionEngine> logger)
    {
        _ml        = new MLContext(seed: 42);
        _priceRepo = priceRepo;
        _logger    = logger;
    }

    // ──────────────────────────────────────────────────────────
    //  Spike Detection
    // ──────────────────────────────────────────────────────────
    public async Task<List<AnomalyPoint>> DetectSpikesAsync(string symbol, int days = 365)
    {
        var prices = (await _priceRepo.GetPricesAsync(symbol, days)).ToList();
        return DetectSpikesCore(prices, symbol);
    }

    private List<AnomalyPoint> DetectSpikesCore(List<StockPrice> prices, string symbol)
    {
        if (prices.Count < 20)
        {
            _logger.LogWarning("Not enough data for spike detection on {Symbol}", symbol);
            return new List<AnomalyPoint>();
        }

        try
        {
            var series   = prices.Select(p => new TimeSeriesValue { Value = p.Close }).ToList();
            var dataView = _ml.Data.LoadFromEnumerable(series);

            int pvalHistory = Math.Min(30, Math.Max(5, prices.Count / 4));

            var pipeline = _ml.Transforms.DetectIidSpike(
                outputColumnName:    "Prediction",
                inputColumnName:     "Value",
                confidence:          95.0,
                pvalueHistoryLength: pvalHistory);

            var model  = pipeline.Fit(dataView);
            var result = model.Transform(dataView);

            var anomalies = new List<AnomalyPoint>();
            var cursor    = result.GetRowCursor(result.Schema);
            var predCol   = result.Schema["Prediction"];
            var getter    = cursor.GetGetter<Microsoft.ML.Data.VBuffer<double>>(predCol);

            int idx = 0;
            while (cursor.MoveNext())
            {
                var buffer = default(Microsoft.ML.Data.VBuffer<double>);
                getter(ref buffer);
                var values = buffer.GetValues();
                // values[0] = alert flag (1 = anomaly), values[1] = raw score, values[2] = p-value
                if (values.Length >= 3 && values[0] == 1.0)
                {
                    anomalies.Add(new AnomalyPoint
                    {
                        Symbol     = symbol,
                        Date       = prices[idx].Date,
                        Price      = (double)prices[idx].Close,
                        Score      = values[1],
                        PValue     = values[2],
                        Type       = prices[idx].DailyReturn > 0 ? AnomalyType.Spike : AnomalyType.Dip,
                        Severity   = Math.Abs(values[1]) > 5 ? AnomalySeverity.High
                                   : Math.Abs(values[1]) > 2 ? AnomalySeverity.Medium
                                   : AnomalySeverity.Low
                    });
                }
                idx++;
            }

            _logger.LogInformation("Spike detection for {Symbol}: found {Count} anomalies", symbol, anomalies.Count);
            return anomalies;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ML spike detection fallback to Z-score for {Symbol}: {Msg}", symbol, ex.Message);
            return StatisticalSpikeDetection(prices, symbol);
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Change-Point Detection
    // ──────────────────────────────────────────────────────────
    public async Task<List<AnomalyPoint>> DetectChangePointsAsync(string symbol, int days = 365)
    {
        var prices = (await _priceRepo.GetPricesAsync(symbol, days)).ToList();
        return DetectChangePointsCore(prices, symbol);
    }

    private List<AnomalyPoint> DetectChangePointsCore(List<StockPrice> prices, string symbol)
    {
        if (prices.Count < 20) return new List<AnomalyPoint>();

        try
        {
            var series   = prices.Select(p => new TimeSeriesValue { Value = p.Close }).ToList();
            var dataView  = _ml.Data.LoadFromEnumerable(series);

            int histLength = Math.Min(30, Math.Max(5, prices.Count / 4));

            var pipeline = _ml.Transforms.DetectIidChangePoint(
                outputColumnName:    "Prediction",
                inputColumnName:     "Value",
                confidence:          95.0,
                changeHistoryLength: histLength);

            var model  = pipeline.Fit(dataView);
            var result = model.Transform(dataView);

            var changePoints = new List<AnomalyPoint>();
            var cursor   = result.GetRowCursor(result.Schema);
            var predCol  = result.Schema["Prediction"];
            var getter   = cursor.GetGetter<Microsoft.ML.Data.VBuffer<double>>(predCol);

            int idx = 0;
            while (cursor.MoveNext())
            {
                var buffer = default(Microsoft.ML.Data.VBuffer<double>);
                getter(ref buffer);
                var values = buffer.GetValues();
                if (values.Length >= 3 && values[0] == 1.0)
                {
                    changePoints.Add(new AnomalyPoint
                    {
                        Symbol   = symbol,
                        Date     = prices[idx].Date,
                        Price    = (double)prices[idx].Close,
                        Score    = values[1],
                        PValue   = values[2],
                        Type     = AnomalyType.ChangePoint,
                        Severity = AnomalySeverity.Medium
                    });
                }
                idx++;
            }

            _logger.LogInformation("Change-point detection for {Symbol}: found {Count}", symbol, changePoints.Count);
            return changePoints;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ML changepoint fallback to rolling mean shift for {Symbol}: {Msg}", symbol, ex.Message);
            return StatisticalChangePointDetection(prices, symbol);
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Statistical Fallbacks (Pure C#)
    // ──────────────────────────────────────────────────────────
    private static List<AnomalyPoint> StatisticalSpikeDetection(List<StockPrice> prices, string symbol)
    {
        var anomalies = new List<AnomalyPoint>();
        int window = Math.Min(20, prices.Count / 2);

        for (int i = window; i < prices.Count; i++)
        {
            var slice = prices.Skip(i - window).Take(window).Select(p => (double)p.Close).ToList();
            double mean = slice.Average();
            double std = Math.Sqrt(slice.Select(x => (x - mean) * (x - mean)).Average());
            if (std <= 0.0001) continue;

            double z = ((double)prices[i].Close - mean) / std;
            if (Math.Abs(z) >= 2.0)
            {
                anomalies.Add(new AnomalyPoint
                {
                    Symbol   = symbol,
                    Date     = prices[i].Date,
                    Price    = (double)prices[i].Close,
                    Score    = z,
                    PValue   = 1.0 / (1.0 + Math.Abs(z)),
                    Type     = z > 0 ? AnomalyType.Spike : AnomalyType.Dip,
                    Severity = Math.Abs(z) > 3.0 ? AnomalySeverity.High : AnomalySeverity.Medium
                });
            }
        }
        return anomalies;
    }

    private static List<AnomalyPoint> StatisticalChangePointDetection(List<StockPrice> prices, string symbol)
    {
        var changePoints = new List<AnomalyPoint>();
        int window = Math.Min(15, prices.Count / 3);

        for (int i = window * 2; i < prices.Count; i += window)
        {
            var prior = prices.Skip(i - window * 2).Take(window).Select(p => (double)p.Close).Average();
            var curr  = prices.Skip(i - window).Take(window).Select(p => (double)p.Close).Average();
            double shift = prior > 0 ? Math.Abs((curr - prior) / prior) : 0;

            if (shift >= 0.08) // ≥ 8% shift in regime
            {
                changePoints.Add(new AnomalyPoint
                {
                    Symbol   = symbol,
                    Date     = prices[i].Date,
                    Price    = (double)prices[i].Close,
                    Score    = shift * 10,
                    PValue   = 0.05,
                    Type     = AnomalyType.ChangePoint,
                    Severity = shift >= 0.15 ? AnomalySeverity.High : AnomalySeverity.Medium
                });
            }
        }
        return changePoints;
    }
}

// ── Input schema for MLContext ──────────────────────────────
internal class TimeSeriesValue
{
    public float Value { get; set; }
}

// ── Anomaly result types ────────────────────────────────────
public class AnomalyPoint
{
    public string         Symbol   { get; set; } = string.Empty;
    public DateTime       Date     { get; set; }
    public double         Price    { get; set; }
    public double         Score    { get; set; }
    public double         PValue   { get; set; }
    public AnomalyType    Type     { get; set; }
    public AnomalySeverity Severity { get; set; }
}

public enum AnomalyType    { Spike, Dip, ChangePoint }
public enum AnomalySeverity { Low, Medium, High }
