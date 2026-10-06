// ============================================================
//  ML/PricePredictionEngine.cs
//  ML.NET regression model: predicts next-day closing price
//  Uses FastTree + LightGBM ensemble
// ============================================================

using FinancialPortfolioTracker.Core.Models;
using FinancialPortfolioTracker.Data.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree;

namespace FinancialPortfolioTracker.ML;

/// <summary>
/// Trains and runs an ML.NET regression model for price prediction.
/// Saves trained models to disk and reloads on subsequent runs.
/// </summary>
public class PricePredictionEngine
{
    private readonly MLContext _mlContext;
    private readonly IStockPriceRepository _priceRepo;
    private readonly ILogger<PricePredictionEngine> _logger;
    private readonly string _modelBasePath;
    private readonly int _minTrainingRows;

    // Cache: symbol → loaded transformer
    private readonly Dictionary<string, ITransformer> _modelCache = new();

    public PricePredictionEngine(
        IStockPriceRepository priceRepo,
        ILogger<PricePredictionEngine> logger,
        string modelBasePath = "Models",
        int minTrainingRows  = 100)
    {
        _mlContext       = new MLContext(seed: 42);
        _priceRepo       = priceRepo;
        _logger          = logger;
        _modelBasePath   = modelBasePath;
        _minTrainingRows = minTrainingRows;

        Directory.CreateDirectory(_modelBasePath);
    }

    // ──────────────────────────────────────────────────────────
    //  Training
    // ──────────────────────────────────────────────────────────
    /// <summary>
    /// Trains a FastTree regression model for a single symbol.
    /// </summary>
    public async Task<ModelTrainingResult> TrainAsync(string symbol, int historyDays = 730)
    {
        _logger.LogInformation("Training model for {Symbol}...", symbol);
        var prices = (await _priceRepo.GetPricesAsync(symbol, historyDays)).ToList();

        if (prices.Count < _minTrainingRows)
        {
            _logger.LogWarning("Insufficient data for {Symbol}: {Count} rows", symbol, prices.Count);
            return new ModelTrainingResult { Symbol = symbol, Success = false,
                                             Message = $"Need ≥{_minTrainingRows} rows, got {prices.Count}" };
        }

        // ── Build labelled dataset (predict NEXT day's close) ─
        var samples = BuildTrainingSamples(prices);
        var dataView = _mlContext.Data.LoadFromEnumerable(samples);

        // ── Train / Evaluation split ──────────────────────────
        var split = _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2);

        // ── Pipeline ─────────────────────────────────────────
        var pipeline = BuildPipeline();

        // ── Fit ──────────────────────────────────────────────
        var sw    = System.Diagnostics.Stopwatch.StartNew();
        var model = pipeline.Fit(split.TrainSet);
        sw.Stop();

        // ── Evaluate ─────────────────────────────────────────
        var predictions = model.Transform(split.TestSet);
        var metrics     = _mlContext.Regression.Evaluate(predictions, labelColumnName: "Label");

        _logger.LogInformation(
            "{Symbol} training done in {Ms}ms | R²={R2:F4} | RMSE={RMSE:F4} | MAE={MAE:F4}",
            symbol, sw.ElapsedMilliseconds, metrics.RSquared, metrics.RootMeanSquaredError, metrics.MeanAbsoluteError);

        // ── Save model to disk ────────────────────────────────
        var modelPath = GetModelPath(symbol);
        _mlContext.Model.Save(model, dataView.Schema, modelPath);
        _modelCache[symbol] = model;
        _logger.LogInformation("Model saved to {Path}", modelPath);

        return new ModelTrainingResult
        {
            Symbol     = symbol,
            Success    = true,
            RSquared   = metrics.RSquared,
            RMSE       = metrics.RootMeanSquaredError,
            MAE        = metrics.MeanAbsoluteError,
            TrainRows  = prices.Count,
            TrainTimeMs = sw.ElapsedMilliseconds,
            Message    = "Training successful"
        };
    }

    // ──────────────────────────────────────────────────────────
    //  Prediction
    // ──────────────────────────────────────────────────────────
    public async Task<PriceForecast?> PredictAsync(string symbol)
    {
        var model = await GetOrLoadModelAsync(symbol);
        if (model == null) return null;

        var prices = (await _priceRepo.GetPricesAsync(symbol, 60)).ToList();
        if (!prices.Any()) return null;

        var latest = prices.Last();
        var input  = MapToInput(latest);

        var engine = _mlContext.Model.CreatePredictionEngine<PricePredictionInput, PricePredictionOutput>(model);
        var result = engine.Predict(input);

        return new PriceForecast
        {
            Symbol         = symbol,
            CurrentPrice   = (double)latest.Close,
            PredictedPrice = (double)result.PredictedPrice,
            PredictionDate = DateTime.UtcNow.AddDays(1),
            ConfidenceNote = "FastTree Regression"
        };
    }

    // ──────────────────────────────────────────────────────────
    //  Parallel Training for multiple symbols
    // ──────────────────────────────────────────────────────────
    public async Task<List<ModelTrainingResult>> TrainAllAsync(
        IEnumerable<string> symbols, int maxParallelism = 4)
    {
        var results  = new System.Collections.Concurrent.ConcurrentBag<ModelTrainingResult>();
        var options  = new ParallelOptions { MaxDegreeOfParallelism = maxParallelism };

        await Parallel.ForEachAsync(symbols, options, async (sym, _) =>
        {
            var r = await TrainAsync(sym);
            results.Add(r);
        });

        return results.OrderBy(r => r.Symbol).ToList();
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────
    private IEstimator<ITransformer> BuildPipeline()
    {
        string[] features = new[]
        {
            "Open", "High", "Low", "Close", "Volume",
            "MA5", "MA20", "MA50", "RSI14", "MACD",
            "ATR14", "PriceToMA20", "DailyReturn"
        };

        return _mlContext.Transforms
            .Concatenate("Features", features)
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.Regression.Trainers.FastTree(
                labelColumnName: "Label",
                featureColumnName: "Features",
                numberOfLeaves: 31,
                numberOfTrees: 100,
                minimumExampleCountPerLeaf: 10,
                learningRate: 0.05));
    }

    private static List<LabelledSample> BuildTrainingSamples(List<StockPrice> prices)
    {
        var samples = new List<LabelledSample>();
        for (int i = 0; i < prices.Count - 1; i++)
        {
            var curr = prices[i];
            var next = prices[i + 1];
            samples.Add(new LabelledSample
            {
                Open        = curr.Open,
                High        = curr.High,
                Low         = curr.Low,
                Close       = curr.Close,
                Volume      = curr.Volume,
                MA5         = curr.MA5,
                MA20        = curr.MA20,
                MA50        = curr.MA50,
                RSI14       = curr.RSI14,
                MACD        = curr.MACD,
                ATR14       = curr.ATR14,
                PriceToMA20 = curr.PriceToMA20,
                DailyReturn = curr.DailyReturn,
                Label       = next.Close          // ← supervise on next day's price
            });
        }
        return samples;
    }

    private static PricePredictionInput MapToInput(StockPrice p) => new()
    {
        Open = p.Open, High = p.High, Low = p.Low, Close = p.Close, Volume = p.Volume,
        MA5 = p.MA5, MA20 = p.MA20, MA50 = p.MA50, RSI14 = p.RSI14,
        MACD = p.MACD, ATR14 = p.ATR14, PriceToMA20 = p.PriceToMA20, DailyReturn = p.DailyReturn
    };

    private Task<ITransformer?> GetOrLoadModelAsync(string symbol)
    {
        if (_modelCache.TryGetValue(symbol, out var cached)) return Task.FromResult<ITransformer?>(cached);
        var path = GetModelPath(symbol);
        if (!File.Exists(path))
        {
            _logger.LogWarning("No trained model for {Symbol}. Train first.", symbol);
            return Task.FromResult<ITransformer?>(null);
        }
        var model = _mlContext.Model.Load(path, out _);
        _modelCache[symbol] = model;
        return Task.FromResult<ITransformer?>(model);
    }

    private string GetModelPath(string symbol) =>
        Path.Combine(_modelBasePath, $"{symbol.ToUpperInvariant()}_model.zip");

    // ── Inner labelled sample class for training ─────────────
    private class LabelledSample : PricePredictionInput
    {
        public float Label { get; set; }
    }
}

// ──────────────────────────────────────────────────────────
//  Result DTOs
// ──────────────────────────────────────────────────────────
public class ModelTrainingResult
{
    public string Symbol      { get; set; } = string.Empty;
    public bool   Success     { get; set; }
    public double RSquared    { get; set; }
    public double RMSE        { get; set; }
    public double MAE         { get; set; }
    public int    TrainRows   { get; set; }
    public long   TrainTimeMs { get; set; }
    public string Message     { get; set; } = string.Empty;
}

public class PriceForecast
{
    public string   Symbol         { get; set; } = string.Empty;
    public double   CurrentPrice   { get; set; }
    public double   PredictedPrice { get; set; }
    public DateTime PredictionDate { get; set; }
    public string   ConfidenceNote { get; set; } = string.Empty;
    public double   ChangePercent  =>
        CurrentPrice == 0 ? 0 : (PredictedPrice - CurrentPrice) / CurrentPrice * 100.0;
}
