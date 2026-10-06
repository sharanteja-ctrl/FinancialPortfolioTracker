// ============================================================
//  ML/ClusteringEngine.cs
//  ML.NET k-means clustering to group similar assets
//  Used to improve portfolio diversification recommendations
// ============================================================

using FinancialPortfolioTracker.Core.Models;
using FinancialPortfolioTracker.Data.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace FinancialPortfolioTracker.ML;

/// <summary>
/// Groups portfolio holdings by price behaviour (k-means clustering).
/// Identifies highly correlated clusters to warn about concentration risk.
/// </summary>
public class ClusteringEngine
{
    private readonly MLContext _ml;
    private readonly IStockPriceRepository _priceRepo;
    private readonly ILogger<ClusteringEngine> _logger;

    public ClusteringEngine(IStockPriceRepository priceRepo, ILogger<ClusteringEngine> logger)
    {
        _ml        = new MLContext(seed: 42);
        _priceRepo = priceRepo;
        _logger    = logger;
    }

    public async Task<ClusteringResult> ClusterHoldingsAsync(
        IEnumerable<string> symbols, int clusters = 4)
    {
        var featureMap = new Dictionary<string, float[]>();

        foreach (var sym in symbols)
        {
            var prices = (await _priceRepo.GetPricesAsync(sym, 252)).ToList();
            if (prices.Count < 20) continue;

            // Feature vector: [annualizedReturn, volatility, avgRSI, momentum]
            var returns    = prices.Skip(1).Select(p => (double)p.DailyReturn).ToArray();
            double mean    = returns.Average();
            double variance= returns.Select(r => (r - mean) * (r - mean)).Average();
            double vol     = Math.Sqrt(variance * 252);
            double annRet  = mean * 252;
            double avgRSI  = prices.Average(p => p.RSI14);
            double momentum= prices.Count > 20
                ? (prices.Last().Close - prices[^20].Close) / prices[^20].Close
                : 0;

            featureMap[sym] = new[] { (float)annRet, (float)vol, (float)(avgRSI / 100.0), (float)momentum };
        }

        return ClusterHoldingsCore(featureMap, clusters);
    }

    private ClusteringResult ClusterHoldingsCore(Dictionary<string, float[]> featureMap, int clusters)
    {
        if (featureMap.Count == 0)
        {
            return new ClusteringResult();
        }

        if (featureMap.Count < 3)
        {
            return new ClusteringResult
            {
                Clusters = new Dictionary<int, List<string>> { [0] = featureMap.Keys.ToList() },
                SymbolToCluster = featureMap.Keys.ToDictionary(k => k, _ => 0)
            };
        }

        clusters = Math.Max(2, Math.Min(clusters, featureMap.Count - 1));

        var samples = featureMap.Select(kv => new ClusterSample
        {
            Symbol = kv.Key,
            AnnualizedReturn  = kv.Value[0],
            Volatility        = kv.Value[1],
            NormalizedRSI     = kv.Value[2],
            Momentum          = kv.Value[3]
        }).ToList();

        try
        {
            var dataView = _ml.Data.LoadFromEnumerable(samples);

            var pipeline = _ml.Transforms
                .Concatenate("Features", "AnnualizedReturn", "Volatility", "NormalizedRSI", "Momentum")
                .Append(_ml.Transforms.NormalizeMinMax("Features"))
                .Append(_ml.Clustering.Trainers.KMeans("Features", numberOfClusters: clusters));

            var model        = pipeline.Fit(dataView);
            var transformed  = model.Transform(dataView);

            // Read cluster assignments
            var clusterAssignments = new Dictionary<string, uint>();
            var cursor     = transformed.GetRowCursor(transformed.Schema);
            var predGetter = cursor.GetGetter<uint>(transformed.Schema["PredictedLabel"]);
            var symIdx     = 0;

            var symbolList = samples.Select(s => s.Symbol).ToList();
            while (cursor.MoveNext())
            {
                uint clusterId = 0;
                predGetter(ref clusterId);
                if (symIdx < symbolList.Count)
                    clusterAssignments[symbolList[symIdx++]] = clusterId;
            }

            // Group by cluster
            var clusterGroups = clusterAssignments
                .GroupBy(kv => kv.Value)
                .ToDictionary(
                    g => (int)g.Key,
                    g => g.Select(kv => kv.Key).ToList());

            _logger.LogInformation("Clustering complete: {K} clusters for {N} holdings",
                clusterGroups.Count, featureMap.Count);

            return new ClusteringResult
            {
                Clusters          = clusterGroups,
                SymbolToCluster   = clusterAssignments.ToDictionary(kv => kv.Key, kv => (int)kv.Value),
                ConcentratedGroups = clusterGroups.Where(g => g.Value.Count > 3)
                                                  .Select(g => g.Value).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ML clustering fallback to rule-based group due to: {Msg}", ex.Message);
            double medianVol = samples.Select(s => (double)s.Volatility).OrderBy(v => v).Skip(samples.Count / 2).FirstOrDefault();
            var fallbackGroups = samples.GroupBy(s => s.Volatility >= medianVol ? 0 : 1)
                .ToDictionary(g => g.Key, g => g.Select(s => s.Symbol).ToList());

            return new ClusteringResult
            {
                Clusters = fallbackGroups,
                SymbolToCluster = samples.ToDictionary(s => s.Symbol, s => s.Volatility >= medianVol ? 0 : 1)
            };
        }
    }
}

// ── Internal ML schema ──────────────────────────────────────
internal class ClusterSample
{
    public string Symbol            { get; set; } = string.Empty;
    public float  AnnualizedReturn  { get; set; }
    public float  Volatility        { get; set; }
    public float  NormalizedRSI     { get; set; }
    public float  Momentum          { get; set; }
}

// ── Result ────────────────────────────────────────────────
public class ClusteringResult
{
    public Dictionary<int, List<string>>  Clusters           { get; set; } = new();
    public Dictionary<string, int>        SymbolToCluster    { get; set; } = new();
    public List<List<string>>             ConcentratedGroups { get; set; } = new();
}
