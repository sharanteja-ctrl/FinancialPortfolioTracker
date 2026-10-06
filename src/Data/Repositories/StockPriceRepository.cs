// ============================================================
//  Data/Repositories/StockPriceRepository.cs
//  Persistence for historical price data (bulk-insert capable)
// ============================================================

using Dapper;
using FinancialPortfolioTracker.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace FinancialPortfolioTracker.Data.Repositories;

public interface IStockPriceRepository
{
    Task BulkInsertAsync(IEnumerable<StockPrice> prices);
    Task<IEnumerable<StockPrice>> GetPricesAsync(string symbol, int days = 365);
    Task<IEnumerable<StockPrice>> GetLatestPricesAsync(IEnumerable<string> symbols);
    Task<double>                  GetLatestCloseAsync(string symbol);
    Task<int>                     GetPriceCountAsync(string symbol);
}

public class StockPriceRepository : IStockPriceRepository
{
    private readonly string _connectionString;
    private readonly ILogger<StockPriceRepository> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1); // Serialise bulk writes

    public StockPriceRepository(string connectionString, ILogger<StockPriceRepository> logger)
    {
        _connectionString = connectionString;
        _logger           = logger;
    }

    private SqliteConnection CreateConnection() =>
        new SqliteConnection(_connectionString);

    /// <summary>
    /// Thread-safe bulk insert using a single SQLite transaction.
    /// Called from multi-threaded preprocessing workers.
    /// </summary>
    public async Task BulkInsertAsync(IEnumerable<StockPrice> prices)
    {
        var priceList = prices.ToList();
        if (!priceList.Any()) return;

        await _writeLock.WaitAsync();
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            await using var tx   = await conn.BeginTransactionAsync();

            const string sql = @"
                INSERT OR REPLACE INTO StockPrices
                  (Symbol, Date, Open, High, Low, Close, Volume,
                   DailyReturn, MA5, MA20, MA50, RSI14, MACD, ATR14, VolumeMA20, PriceToMA20)
                VALUES
                  (@Symbol, @Date, @Open, @High, @Low, @Close, @Volume,
                   @DailyReturn, @MA5, @MA20, @MA50, @RSI14, @MACD, @ATR14, @VolumeMA20, @PriceToMA20)";

            foreach (var p in priceList)
            {
                await conn.ExecuteAsync(sql, new
                {
                    p.Symbol,
                    Date        = p.Date.ToString("yyyy-MM-dd"),
                    p.Open, p.High, p.Low, p.Close, p.Volume,
                    p.DailyReturn, p.MA5, p.MA20, p.MA50,
                    p.RSI14, p.MACD, p.ATR14, p.VolumeMA20, p.PriceToMA20
                }, transaction: (SqliteTransaction)tx);
            }
            await tx.CommitAsync();
            _logger.LogDebug("Bulk-inserted {Count} price records", priceList.Count);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IEnumerable<StockPrice>> GetPricesAsync(string symbol, int days = 365)
    {
        await using var conn = CreateConnection();
        var rows   = await conn.QueryAsync(@"
            SELECT * FROM (
                SELECT * FROM StockPrices
                WHERE Symbol=@Symbol
                ORDER BY Date DESC
                LIMIT @Limit
            )
            ORDER BY Date ASC",
            new { Symbol = symbol, Limit = days });

        return rows.Select(MapRow);
    }

    public async Task<IEnumerable<StockPrice>> GetLatestPricesAsync(IEnumerable<string> symbols)
    {
        await using var conn = CreateConnection();
        var list   = symbols.ToList();
        var result = new List<StockPrice>();
        foreach (var sym in list)
        {
            var row = await conn.QueryFirstOrDefaultAsync(@"
                SELECT * FROM StockPrices WHERE Symbol=@Sym ORDER BY Date DESC LIMIT 1",
                new { Sym = sym });
            if (row != null) result.Add(MapRow(row));
        }
        return result;
    }

    public async Task<double> GetLatestCloseAsync(string symbol)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<double>(@"
            SELECT Close FROM StockPrices WHERE Symbol=@Sym ORDER BY Date DESC LIMIT 1",
            new { Sym = symbol });
    }

    public async Task<int> GetPriceCountAsync(string symbol)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM StockPrices WHERE Symbol=@Sym",
            new { Sym = symbol });
    }

    private static StockPrice MapRow(dynamic r) => new()
    {
        Symbol      = r.Symbol,
        Date        = DateTime.Parse(r.Date),
        Open        = (float)r.Open,
        High        = (float)r.High,
        Low         = (float)r.Low,
        Close       = (float)r.Close,
        Volume      = (float)r.Volume,
        DailyReturn = r.DailyReturn != null ? (float)r.DailyReturn : 0f,
        MA5         = r.MA5         != null ? (float)r.MA5         : 0f,
        MA20        = r.MA20        != null ? (float)r.MA20        : 0f,
        MA50        = r.MA50        != null ? (float)r.MA50        : 0f,
        RSI14       = r.RSI14       != null ? (float)r.RSI14       : 50f,
        MACD        = r.MACD        != null ? (float)r.MACD        : 0f,
        ATR14       = r.ATR14       != null ? (float)r.ATR14       : 0f,
        VolumeMA20  = r.VolumeMA20  != null ? (float)r.VolumeMA20  : 0f,
        PriceToMA20 = r.PriceToMA20 != null ? (float)r.PriceToMA20 : 1f
    };
}
