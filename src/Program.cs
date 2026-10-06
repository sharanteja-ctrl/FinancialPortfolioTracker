// ============================================================
//  Program.cs
//  Application entry point, DI setup, and main control loop
// ============================================================

using FinancialPortfolioTracker.Core;
using FinancialPortfolioTracker.Core.Models;
using FinancialPortfolioTracker.Data;
using FinancialPortfolioTracker.Data.Repositories;
using FinancialPortfolioTracker.ML;
using FinancialPortfolioTracker.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinancialPortfolioTracker;

class Program
{
    static async Task Main(string[] args)
    {
        Console.Title = "Financial Portfolio Tracker";
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // ── Configuration ────────────────────────────────────
        var basePath = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"))
            ? Directory.GetCurrentDirectory()
            : File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
                ? AppContext.BaseDirectory
                : Path.Combine(Directory.GetCurrentDirectory(), "src");

        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var connString      = config["Database:ConnectionString"]!;
        var rawModelPath    = config["ML:ModelSavePath"]!;
        var modelPath       = Path.IsPathRooted(rawModelPath) ? rawModelPath : Path.Combine(basePath, rawModelPath);
        Directory.CreateDirectory(modelPath);

        var riskFreeRate    = double.Parse(config["Portfolio:RiskFreeRate"]!);
        var benchmarkSymbol = config["Portfolio:BenchmarkSymbol"]!;
        var maxThreads      = int.Parse(config["Processing:MaxThreads"]!);
        var minTrainRows    = int.Parse(config["ML:MinimumTrainingRows"]!);
        var trainDataConfig = config["ML:TrainingDataPath"]!;
        var trainDataPath   = ResolveExistingPath(trainDataConfig, basePath);

        // ── Logging ──────────────────────────────────────────
        using var logFactory = LoggerFactory.Create(b =>
            b.AddConsole(o => o.FormatterName = "simple")
             .SetMinimumLevel(LogLevel.Warning));  // suppress verbose ML.NET logs

        var logger = logFactory.CreateLogger<Program>();

        // ── Database ─────────────────────────────────────────
        var dbInit = new DatabaseInitializer(connString, logFactory.CreateLogger<DatabaseInitializer>());
        await dbInit.InitializeAsync();

        // ── Repositories ──────────────────────────────────────
        IPortfolioRepository  portfolioRepo  = new PortfolioRepository(connString,  logFactory.CreateLogger<PortfolioRepository>());
        IStockPriceRepository priceRepo      = new StockPriceRepository(connString, logFactory.CreateLogger<StockPriceRepository>());

        // ── Services ─────────────────────────────────────────
        var preprocessor    = new DataPreprocessor(priceRepo, logFactory.CreateLogger<DataPreprocessor>(), maxThreads);
        var predictor       = new PricePredictionEngine(priceRepo, logFactory.CreateLogger<PricePredictionEngine>(), modelPath, minTrainRows);
        var anomalyEngine   = new AnomalyDetectionEngine(priceRepo, logFactory.CreateLogger<AnomalyDetectionEngine>());
        var clusterEngine   = new ClusteringEngine(priceRepo, logFactory.CreateLogger<ClusteringEngine>());
        var analyzer        = new PortfolioAnalyzer(priceRepo, logFactory.CreateLogger<PortfolioAnalyzer>(), riskFreeRate, benchmarkSymbol);
        var recommender     = new RecommendationEngine(predictor, anomalyEngine, clusterEngine, logFactory.CreateLogger<RecommendationEngine>());

        // ── Auto-seed sample prices if database is empty ────────
        var samplePrice = await priceRepo.GetLatestCloseAsync(benchmarkSymbol);
        if (samplePrice <= 0 && File.Exists(trainDataPath))
        {
            ConsoleUI.PrintInfo("First-time setup: Seeding historical stock prices...");
            await preprocessor.LoadAndProcessCsvAsync(trainDataPath);
        }

        // ── Ensure default portfolio exists ──────────────────
        int portfolioId = await EnsureDefaultPortfolioAsync(portfolioRepo, basePath);

        // ── Main Loop ─────────────────────────────────────────
        bool running = true;
        while (running)
        {
            string choice = ConsoleUI.PrintMenu();
            Console.WriteLine();

            switch (choice)
            {
                case "1":
                    await ShowPortfolioSummary(portfolioRepo, portfolioId);
                    break;

                case "2":
                    await AddInvestmentInteractive(portfolioRepo, portfolioId);
                    break;

                case "3":
                    await UpdatePricesFromDb(portfolioRepo, priceRepo, portfolioId);
                    break;

                case "4":
                    await LoadHistoricalData(preprocessor, trainDataPath, basePath);
                    break;

                case "5":
                    await TrainModels(portfolioRepo, predictor, portfolioId, maxThreads);
                    break;

                case "6":
                    await RunFullAnalysis(portfolioRepo, analyzer, portfolioId);
                    break;

                case "7":
                    await ShowRecommendations(portfolioRepo, analyzer, recommender, portfolioId);
                    break;

                case "8":
                    await RunAnomalyDetection(portfolioRepo, anomalyEngine, portfolioId);
                    break;

                case "9":
                    await RunClusterAnalysis(portfolioRepo, clusterEngine, portfolioId);
                    break;

                case "0":
                    running = false;
                    ConsoleUI.PrintSuccess("Goodbye! Stay invested. 📈");
                    break;

                default:
                    ConsoleUI.PrintWarning("Invalid option. Please try again.");
                    break;
            }

            if (running)
            {
                if (!Console.IsInputRedirected)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("\n  Press any key to continue...");
                    Console.ResetColor();
                    Console.ReadKey(true);
                }
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Action Handlers
    // ──────────────────────────────────────────────────────────

    static async Task<int> EnsureDefaultPortfolioAsync(IPortfolioRepository repo, string basePath)
    {
        var portfolios = (await repo.GetAllPortfoliosAsync()).ToList();
        int portfolioId;
        if (portfolios.Any())
        {
            portfolioId = portfolios.First().Id;
        }
        else
        {
            ConsoleUI.PrintInfo("Creating default portfolio...");
            portfolioId = await repo.CreatePortfolioAsync(new Portfolio
            {
                Name        = "My Portfolio",
                Description = "Default investment portfolio",
                Currency    = "USD"
            });
        }

        var portfolio = await repo.GetPortfolioAsync(portfolioId);
        if (!portfolio.Investments.Any())
        {
            var sampleCsv = ResolveExistingPath("Data/sample_portfolio.csv", basePath);
            if (File.Exists(sampleCsv))
            {
                ConsoleUI.PrintInfo("Seeding default portfolio holdings from sample_portfolio.csv...");
                var lines = await File.ReadAllLinesAsync(sampleCsv);
                foreach (var line in lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)))
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 6 &&
                        double.TryParse(parts[4], System.Globalization.CultureInfo.InvariantCulture, out double qty) &&
                        double.TryParse(parts[5], System.Globalization.CultureInfo.InvariantCulture, out double buyPrice))
                    {
                        var inv = new Investment
                        {
                            Symbol        = parts[0].Trim().ToUpperInvariant(),
                            Name          = parts[1].Trim(),
                            AssetType     = parts[2].Trim(),
                            Sector        = parts[3].Trim(),
                            Quantity      = qty,
                            PurchasePrice = buyPrice,
                            CurrentPrice  = buyPrice,
                            PurchaseDate  = DateTime.TryParse(parts[6].Trim(), out var d) ? d : DateTime.UtcNow,
                            Notes         = parts.Length > 7 ? parts[7].Trim() : string.Empty
                        };
                        await repo.AddInvestmentAsync(portfolioId, inv);
                    }
                }
            }
        }

        return portfolioId;
    }

    static async Task ShowPortfolioSummary(IPortfolioRepository repo, int portfolioId)
    {
        var portfolio = await repo.GetPortfolioAsync(portfolioId);
        ConsoleUI.PrintPortfolioSummary(portfolio);

        if (portfolio.Investments.Any())
        {
            ConsoleUI.PrintHoldingsTable(portfolio.Investments);
            ConsoleUI.PrintAllocationChart("Asset Type Allocation", portfolio.AllocationByAssetType());
            ConsoleUI.PrintAllocationChart("Sector Allocation",     portfolio.AllocationBySector());
        }
        else
        {
            ConsoleUI.PrintWarning("No investments yet. Use option 2 to add some.");
        }
    }

    static async Task AddInvestmentInteractive(IPortfolioRepository repo, int portfolioId)
    {
        ConsoleUI.PrintSection("Add New Investment");

        string Prompt(string label, string? def = null)
        {
            Console.Write($"  {label}{(def != null ? $" [{def}]" : "")}: ");
            var val = Console.ReadLine()?.Trim() ?? string.Empty;
            return string.IsNullOrEmpty(val) && def != null ? def : val;
        }

        var symbol = Prompt("Symbol (e.g. AAPL)").ToUpperInvariant();
        var name   = Prompt("Name (e.g. Apple Inc.)");
        var assetType = Prompt("Asset Type (Stock/ETF/Bond/Crypto)", "Stock");
        var sector = Prompt("Sector (e.g. Technology)", "Unknown");

        if (!double.TryParse(Prompt("Quantity"), out double qty) || qty <= 0)
        { ConsoleUI.PrintError("Invalid quantity."); return; }

        if (!double.TryParse(Prompt("Purchase Price ($)"), out double buyPrice) || buyPrice <= 0)
        { ConsoleUI.PrintError("Invalid price."); return; }

        if (!DateTime.TryParse(Prompt("Purchase Date (YYYY-MM-DD)", DateTime.Today.ToString("yyyy-MM-dd")), out DateTime buyDate))
            buyDate = DateTime.Today;

        var inv = new Investment
        {
            Symbol        = symbol,
            Name          = name,
            AssetType     = assetType,
            Sector        = sector,
            Quantity      = qty,
            PurchasePrice = buyPrice,
            CurrentPrice  = buyPrice,
            PurchaseDate  = buyDate
        };

        var id = await repo.AddInvestmentAsync(portfolioId, inv);
        ConsoleUI.PrintSuccess($"Added {symbol} (ID={id}) — ${inv.TotalCost:N2} invested.");
    }

    static async Task UpdatePricesFromDb(IPortfolioRepository portfolioRepo,
                                          IStockPriceRepository priceRepo, int portfolioId)
    {
        ConsoleUI.PrintSection("Updating Prices from Database");
        var portfolio = await portfolioRepo.GetPortfolioAsync(portfolioId);
        int updated = 0;

        await Parallel.ForEachAsync(portfolio.Investments,
            new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (inv, _) =>
        {
            var price = await priceRepo.GetLatestCloseAsync(inv.Symbol);
            if (price > 0)
            {
                await portfolioRepo.UpdateInvestmentPriceAsync(inv.Symbol, price);
                Interlocked.Increment(ref updated);
                ConsoleUI.PrintInfo($"  {inv.Symbol}: ${inv.CurrentPrice:N2} → ${price:N2}");
            }
        });

        await portfolioRepo.SaveSnapshotAsync(portfolioId,
            portfolio.TotalMarketValue, portfolio.TotalCost);

        ConsoleUI.PrintSuccess($"Updated {updated}/{portfolio.Investments.Count} prices.");
    }

    static async Task LoadHistoricalData(DataPreprocessor preprocessor, string defaultPath, string basePath)
    {
        ConsoleUI.PrintSection("Load Historical Price Data");
        Console.Write($"  CSV path [{defaultPath}]: ");
        var path = Console.ReadLine()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(path)) path = defaultPath;
        path = ResolveExistingPath(path, basePath);

        ConsoleUI.PrintInfo($"Loading from: {path}");
        var sw    = System.Diagnostics.Stopwatch.StartNew();
        int count = await preprocessor.LoadAndProcessCsvAsync(path);
        sw.Stop();
        ConsoleUI.PrintSuccess($"Loaded {count:N0} records in {sw.ElapsedMilliseconds:N0} ms");
    }

    static string ResolveExistingPath(string path, string basePath)
    {
        if (File.Exists(path)) return Path.GetFullPath(path);
        var inBase = Path.Combine(basePath, path);
        if (File.Exists(inBase)) return Path.GetFullPath(inBase);
        var inSrc = Path.Combine(Directory.GetCurrentDirectory(), "src", path);
        if (File.Exists(inSrc)) return Path.GetFullPath(inSrc);
        var inAppBase = Path.Combine(AppContext.BaseDirectory, path);
        if (File.Exists(inAppBase)) return Path.GetFullPath(inAppBase);
        return Path.GetFullPath(path);
    }

    static async Task TrainModels(IPortfolioRepository portfolioRepo,
                                   PricePredictionEngine predictor,
                                   int portfolioId, int maxThreads)
    {
        ConsoleUI.PrintSection("Training ML Models");
        var portfolio = await portfolioRepo.GetPortfolioAsync(portfolioId);
        var symbols   = portfolio.Investments.Select(i => i.Symbol).ToList();

        if (!symbols.Any())
        { ConsoleUI.PrintWarning("No investments to train on."); return; }

        ConsoleUI.PrintInfo($"Training {symbols.Count} models (max {maxThreads / 2} parallel)...");
        var sw      = System.Diagnostics.Stopwatch.StartNew();
        var results = await predictor.TrainAllAsync(symbols, maxThreads / 2);
        sw.Stop();

        ConsoleUI.PrintTrainingResults(results);
        ConsoleUI.PrintSuccess($"Training complete in {sw.ElapsedMilliseconds:N0} ms. " +
                               $"{results.Count(r => r.Success)}/{results.Count} models OK.");
    }

    static async Task RunFullAnalysis(IPortfolioRepository portfolioRepo,
                                       PortfolioAnalyzer analyzer, int portfolioId)
    {
        ConsoleUI.PrintSection("Full Portfolio Analysis");
        var portfolio = await portfolioRepo.GetPortfolioAsync(portfolioId);
        var analytics = await analyzer.AnalyzeAsync(portfolio);
        portfolio.Analytics = analytics;
        ConsoleUI.PrintAnalytics(analytics);
    }

    static async Task ShowRecommendations(IPortfolioRepository portfolioRepo,
                                           PortfolioAnalyzer analyzer,
                                           RecommendationEngine recommender,
                                           int portfolioId)
    {
        ConsoleUI.PrintSection("Generating Recommendations");
        var portfolio = await portfolioRepo.GetPortfolioAsync(portfolioId);
        var analytics = await analyzer.AnalyzeAsync(portfolio);
        var recs      = await recommender.GenerateAsync(portfolio, analytics);
        ConsoleUI.PrintRecommendations(recs);
    }

    static async Task RunAnomalyDetection(IPortfolioRepository portfolioRepo,
                                           AnomalyDetectionEngine anomalyEngine, int portfolioId)
    {
        ConsoleUI.PrintSection("Anomaly Detection");
        var portfolio = await portfolioRepo.GetPortfolioAsync(portfolioId);

        foreach (var inv in portfolio.Investments)
        {
            var spikes = await anomalyEngine.DetectSpikesAsync(inv.Symbol);
            var cps    = await anomalyEngine.DetectChangePointsAsync(inv.Symbol);
            Console.WriteLine($"\n  {inv.Symbol}: {spikes.Count} spikes, {cps.Count} change-points");

            foreach (var a in spikes.Concat(cps).OrderByDescending(x => x.Date).Take(5))
            {
                var color = a.Severity == AnomalySeverity.High ? ConsoleColor.Red
                          : a.Severity == AnomalySeverity.Medium ? ConsoleColor.Yellow
                          : ConsoleColor.White;
                Console.ForegroundColor = color;
                Console.WriteLine($"    [{a.Date:yyyy-MM-dd}] {a.Type} | Price: ${a.Price:N2} | Score: {a.Score:F2} | Severity: {a.Severity}");
                Console.ResetColor();
            }
        }
    }

    static async Task RunClusterAnalysis(IPortfolioRepository portfolioRepo,
                                          ClusteringEngine clusterEngine, int portfolioId)
    {
        ConsoleUI.PrintSection("Cluster Analysis");
        var portfolio = await portfolioRepo.GetPortfolioAsync(portfolioId);
        var symbols   = portfolio.Investments.Select(i => i.Symbol).ToList();

        if (symbols.Count < 3)
        { ConsoleUI.PrintWarning("Need at least 3 holdings for clustering."); return; }

        var result = await clusterEngine.ClusterHoldingsAsync(symbols);

        Console.WriteLine($"\n  Found {result.Clusters.Count} behavioural clusters:");
        foreach (var (clusterId, members) in result.Clusters.OrderBy(g => g.Key))
        {
            Console.ForegroundColor = clusterId % 2 == 0 ? ConsoleColor.Cyan : ConsoleColor.Magenta;
            Console.WriteLine($"    Cluster {clusterId}: [{string.Join(", ", members)}]");
            Console.ResetColor();
        }

        if (result.ConcentratedGroups.Any())
        {
            ConsoleUI.PrintWarning("Concentration risk detected in these clusters:");
            foreach (var g in result.ConcentratedGroups)
                Console.WriteLine($"    → [{string.Join(", ", g)}] — consider diversifying");
        }
    }
}
