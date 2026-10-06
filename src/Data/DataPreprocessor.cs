// ============================================================
//  Data/DataPreprocessor.cs
//  Multi-threaded data cleaning & technical indicator computation
// ============================================================

using FinancialPortfolioTracker.Core.Models;
using FinancialPortfolioTracker.Data.Repositories;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace FinancialPortfolioTracker.Data;

/// <summary>
/// Handles data collection from CSV files, cleaning, feature engineering,
/// and parallel persistence using multiple worker threads.
/// </summary>
public class DataPreprocessor
{
    private readonly IStockPriceRepository _priceRepo;
    private readonly ILogger<DataPreprocessor> _logger;
    private readonly int _maxThreads;

    public DataPreprocessor(
        IStockPriceRepository priceRepo,
        ILogger<DataPreprocessor> logger,
        int maxThreads = 8)
    {
        _priceRepo  = priceRepo;
        _logger     = logger;
        _maxThreads = maxThreads;
    }

    // ──────────────────────────────────────────────────────────
    //  Public entry-point: load CSV → clean → engineer → persist
    // ──────────────────────────────────────────────────────────
    public async Task<int> LoadAndProcessCsvAsync(string csvPath)
    {
        _logger.LogInformation("Loading price data from: {Path}", csvPath);
        if (!File.Exists(csvPath))
            throw new FileNotFoundException($"CSV not found: {csvPath}");

        // ── Step 1: Parse CSV ──────────────────────────────
        var raw = await ParseCsvAsync(csvPath);
        _logger.LogInformation("Parsed {Count} raw price records", raw.Count);

        // ── Step 2: Clean & group by symbol ───────────────
        var bySymbol = raw
            .GroupBy(p => p.Symbol)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Date).ToList());

        _logger.LogInformation("Found {Symbols} unique symbols", bySymbol.Count);

        // ── Step 3: Parallel feature engineering ──────────
        var processed = new ConcurrentBag<StockPrice>();
        var options   = new ParallelOptions { MaxDegreeOfParallelism = _maxThreads };

        await Parallel.ForEachAsync(bySymbol, options, async (kv, ct) =>
        {
            var engineered = EngineerFeatures(kv.Key, kv.Value);
            foreach (var p in engineered) processed.Add(p);
            await Task.CompletedTask;
        });

        _logger.LogInformation("Feature engineering complete: {Count} records", processed.Count);

        // ── Step 4: Batch persist ─────────────────────────
        var batches = processed
            .OrderBy(p => p.Symbol).ThenBy(p => p.Date)
            .Chunk(1000);

        var tasks = new List<Task>();
        using var semaphore = new SemaphoreSlim(_maxThreads / 2 + 1);

        foreach (var batch in batches)
        {
            await semaphore.WaitAsync();
            tasks.Add(Task.Run(async () =>
            {
                try   { await _priceRepo.BulkInsertAsync(batch); }
                finally { semaphore.Release(); }
            }));
        }
        await Task.WhenAll(tasks);

        _logger.LogInformation("Data preprocessing complete. {Count} records persisted.", processed.Count);
        return processed.Count;
    }

    // ──────────────────────────────────────────────────────────
    //  Feature Engineering (Technical Indicators)
    // ──────────────────────────────────────────────────────────
    private List<StockPrice> EngineerFeatures(string symbol, List<StockPrice> sorted)
    {
        // Remove outliers: flag prices with >50% single-day move as suspicious
        var cleaned = RemoveOutliers(sorted);

        // Forward-fill missing days (weekends / holidays) – skipped here,
        // we simply work with trading days.

        int n = cleaned.Count;
        for (int i = 0; i < n; i++)
        {
            var p = cleaned[i];
            p.Symbol = symbol;

            // Daily return
            if (i > 0 && cleaned[i - 1].Close > 0)
                p.DailyReturn = (p.Close - cleaned[i - 1].Close) / cleaned[i - 1].Close;

            // Moving averages
            p.MA5   = SimpleMA(cleaned, i, 5);
            p.MA20  = SimpleMA(cleaned, i, 20);
            p.MA50  = SimpleMA(cleaned, i, 50);

            // RSI 14
            p.RSI14 = ComputeRSI(cleaned, i, 14);

            // MACD (12,26,9)
            p.MACD  = ComputeMACD(cleaned, i);

            // ATR 14
            p.ATR14 = ComputeATR(cleaned, i, 14);

            // Volume MA 20
            p.VolumeMA20 = VolumeMA(cleaned, i, 20);

            // Price to MA20 ratio
            p.PriceToMA20 = p.MA20 > 0 ? p.Close / p.MA20 : 1f;

            // Bollinger Bands (20, 2σ)
            ComputeBollinger(cleaned, i, 20, out float upper, out float lower);
            p.BollingerUpper = upper;
            p.BollingerLower = lower;
        }
        return cleaned;
    }

    // ── Statistical helpers ─────────────────────────────────
    private static List<StockPrice> RemoveOutliers(List<StockPrice> prices)
    {
        var result = new List<StockPrice>(prices.Count);
        for (int i = 0; i < prices.Count; i++)
        {
            if (i == 0) { result.Add(prices[i]); continue; }
            double prev  = prices[i - 1].Close;
            double curr  = prices[i].Close;
            double delta = prev > 0 ? Math.Abs((curr - prev) / prev) : 0;
            if (delta < 0.50) result.Add(prices[i]); // discard >50% daily moves
        }
        return result;
    }

    private static float SimpleMA(List<StockPrice> prices, int idx, int period)
    {
        if (idx < period - 1) return prices[idx].Close;
        double sum = 0;
        for (int j = idx - period + 1; j <= idx; j++) sum += prices[j].Close;
        return (float)(sum / period);
    }

    private static float VolumeMA(List<StockPrice> prices, int idx, int period)
    {
        if (idx < period - 1) return prices[idx].Volume;
        double sum = 0;
        for (int j = idx - period + 1; j <= idx; j++) sum += prices[j].Volume;
        return (float)(sum / period);
    }

    private static float ComputeRSI(List<StockPrice> prices, int idx, int period)
    {
        if (idx < period) return 50f;
        double gains = 0, losses = 0;
        for (int j = idx - period + 1; j <= idx; j++)
        {
            double change = prices[j].Close - prices[j - 1].Close;
            if (change > 0) gains  += change;
            else            losses -= change;
        }
        if (losses == 0) return 100f;
        double rs = gains / losses;
        return (float)(100.0 - 100.0 / (1.0 + rs));
    }

    private static float ComputeMACD(List<StockPrice> prices, int idx)
    {
        if (idx < 26) return 0f;
        double ema12 = ComputeEMA(prices, idx, 12);
        double ema26 = ComputeEMA(prices, idx, 26);
        return (float)(ema12 - ema26);
    }

    private static double ComputeEMA(List<StockPrice> prices, int idx, int period)
    {
        if (idx < period - 1) return prices[idx].Close;
        double k = 2.0 / (period + 1);
        double ema = prices[idx - period + 1].Close;
        for (int j = idx - period + 2; j <= idx; j++)
            ema = prices[j].Close * k + ema * (1 - k);
        return ema;
    }

    private static float ComputeATR(List<StockPrice> prices, int idx, int period)
    {
        if (idx < 1) return prices[idx].High - prices[idx].Low;
        int start = Math.Max(1, idx - period + 1);
        double sum = 0;
        for (int j = start; j <= idx; j++)
        {
            double tr = Math.Max(prices[j].High - prices[j].Low,
                        Math.Max(Math.Abs(prices[j].High - prices[j - 1].Close),
                                 Math.Abs(prices[j].Low  - prices[j - 1].Close)));
            sum += tr;
        }
        return (float)(sum / (idx - start + 1));
    }

    private static void ComputeBollinger(List<StockPrice> prices, int idx, int period,
                                         out float upper, out float lower)
    {
        float ma = SimpleMA(prices, idx, period);
        if (idx < period - 1) { upper = ma; lower = ma; return; }
        double variance = 0;
        for (int j = idx - period + 1; j <= idx; j++)
        {
            double diff = prices[j].Close - ma;
            variance += diff * diff;
        }
        double std = Math.Sqrt(variance / period);
        upper = (float)(ma + 2 * std);
        lower = (float)(ma - 2 * std);
    }

    // ──────────────────────────────────────────────────────────
    //  CSV Parser (manual, no external lib dependency)
    // ──────────────────────────────────────────────────────────
    private async Task<List<StockPrice>> ParseCsvAsync(string path)
    {
        var result = new List<StockPrice>();
        var lines  = await File.ReadAllLinesAsync(path);

        // Detect header columns
        if (lines.Length < 2)
            throw new InvalidDataException("CSV file is empty or has no data rows.");

        var header  = lines[0].Split(',').Select(h => h.Trim().ToUpperInvariant()).ToArray();
        int idxSym  = Array.IndexOf(header, "SYMBOL");
        int idxDate = Array.IndexOf(header, "DATE");
        int idxOpen = Array.IndexOf(header, "OPEN");
        int idxHigh = Array.IndexOf(header, "HIGH");
        int idxLow  = Array.IndexOf(header, "LOW");
        int idxClose= Array.IndexOf(header, "CLOSE");
        int idxVol  = Array.IndexOf(header, "VOLUME");

        if (idxSym < 0 || idxDate < 0 || idxClose < 0)
            throw new InvalidDataException("CSV must have at minimum: Symbol, Date, Close columns.");

        int badRows = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split(',');
            if (cols.Length <= Math.Max(idxSym, idxClose)) { badRows++; continue; }

            if (!DateTime.TryParse(SafeGet(cols, idxDate), out var date)) { badRows++; continue; }
            if (!float.TryParse(SafeGet(cols, idxClose), out var close))  { badRows++; continue; }

            result.Add(new StockPrice
            {
                Symbol = SafeGet(cols, idxSym).ToUpperInvariant(),
                Date   = date.Date,
                Open   = float.TryParse(SafeGet(cols, idxOpen),  out var o) ? o : close,
                High   = float.TryParse(SafeGet(cols, idxHigh),  out var h) ? h : close,
                Low    = float.TryParse(SafeGet(cols, idxLow),   out var l) ? l : close,
                Close  = close,
                Volume = float.TryParse(SafeGet(cols, idxVol),   out var v) ? v : 0f
            });
        }
        if (badRows > 0)
            _logger.LogWarning("Skipped {Bad} malformed rows during CSV parsing", badRows);

        return result;
    }

    private static string SafeGet(string[] arr, int idx) =>
        idx >= 0 && idx < arr.Length ? arr[idx].Trim() : string.Empty;
}
