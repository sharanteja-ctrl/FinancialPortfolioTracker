// ============================================================
//  Data/Repositories/PortfolioRepository.cs
//  CRUD operations for Portfolio & Investment using Dapper
// ============================================================

using Dapper;
using FinancialPortfolioTracker.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace FinancialPortfolioTracker.Data.Repositories;

public interface IPortfolioRepository
{
    Task<Portfolio>              GetPortfolioAsync(int id);
    Task<IEnumerable<Portfolio>> GetAllPortfoliosAsync();
    Task<int>                    CreatePortfolioAsync(Portfolio portfolio);
    Task                         UpdatePortfolioAsync(Portfolio portfolio);
    Task<int>                    AddInvestmentAsync(int portfolioId, Investment investment);
    Task                         UpdateInvestmentPriceAsync(string symbol, double currentPrice);
    Task                         DeleteInvestmentAsync(int investmentId);
    Task<IEnumerable<Investment>> GetInvestmentsAsync(int portfolioId);
    Task                         SaveSnapshotAsync(int portfolioId, double totalValue, double totalCost);
    Task<IEnumerable<(DateTime Date, double Value)>> GetSnapshotHistoryAsync(int portfolioId, int days);
}

public class PortfolioRepository : IPortfolioRepository
{
    private readonly string _connectionString;
    private readonly ILogger<PortfolioRepository> _logger;

    public PortfolioRepository(string connectionString, ILogger<PortfolioRepository> logger)
    {
        _connectionString = connectionString;
        _logger           = logger;
    }

    // ── Helpers ──────────────────────────────────────────────
    private SqliteConnection CreateConnection() =>
        new SqliteConnection(_connectionString);

    // ── Portfolio CRUD ───────────────────────────────────────
    public async Task<Portfolio> GetPortfolioAsync(int id)
    {
        await using var conn = CreateConnection();
        var portfolio = await conn.QuerySingleOrDefaultAsync<Portfolio>(
            "SELECT * FROM Portfolios WHERE Id = @Id", new { Id = id });

        if (portfolio == null)
            throw new KeyNotFoundException($"Portfolio {id} not found.");

        portfolio.Investments = (await GetInvestmentsAsync(id)).ToList();
        return portfolio;
    }

    public async Task<IEnumerable<Portfolio>> GetAllPortfoliosAsync()
    {
        await using var conn = CreateConnection();
        return await conn.QueryAsync<Portfolio>("SELECT * FROM Portfolios ORDER BY CreatedAt");
    }

    public async Task<int> CreatePortfolioAsync(Portfolio portfolio)
    {
        await using var conn = CreateConnection();
        var sql = @"
            INSERT INTO Portfolios (Name, Description, Currency, CreatedAt, LastUpdated)
            VALUES (@Name, @Description, @Currency, @CreatedAt, @LastUpdated);
            SELECT last_insert_rowid();";
        var newId = await conn.QuerySingleAsync<int>(sql, new
        {
            portfolio.Name,
            portfolio.Description,
            portfolio.Currency,
            CreatedAt   = portfolio.CreatedAt.ToString("O"),
            LastUpdated = portfolio.LastUpdated.ToString("O")
        });
        _logger.LogInformation("Created portfolio {Id}: {Name}", newId, portfolio.Name);
        return newId;
    }

    public async Task UpdatePortfolioAsync(Portfolio portfolio)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE Portfolios SET Name=@Name, Description=@Description,
            Currency=@Currency, LastUpdated=@LastUpdated WHERE Id=@Id",
            new { portfolio.Name, portfolio.Description, portfolio.Currency,
                  LastUpdated = DateTime.UtcNow.ToString("O"), portfolio.Id });
    }

    // ── Investment CRUD ──────────────────────────────────────
    public async Task<int> AddInvestmentAsync(int portfolioId, Investment inv)
    {
        await using var conn = CreateConnection();
        var sql = @"
            INSERT INTO Investments
              (PortfolioId, Symbol, Name, AssetType, Sector, Quantity,
               PurchasePrice, CurrentPrice, PurchaseDate, LastUpdated, Currency, Notes)
            VALUES
              (@PortfolioId, @Symbol, @Name, @AssetType, @Sector, @Quantity,
               @PurchasePrice, @CurrentPrice, @PurchaseDate, @LastUpdated, @Currency, @Notes);
            SELECT last_insert_rowid();";
        var id = await conn.QuerySingleAsync<int>(sql, new
        {
            PortfolioId    = portfolioId,
            inv.Symbol,
            inv.Name,
            inv.AssetType,
            inv.Sector,
            inv.Quantity,
            inv.PurchasePrice,
            CurrentPrice   = inv.CurrentPrice == 0 ? inv.PurchasePrice : inv.CurrentPrice,
            PurchaseDate   = inv.PurchaseDate.ToString("O"),
            LastUpdated    = DateTime.UtcNow.ToString("O"),
            inv.Currency,
            inv.Notes
        });
        _logger.LogInformation("Added investment {Symbol} to portfolio {PId}", inv.Symbol, portfolioId);
        return id;
    }

    public async Task UpdateInvestmentPriceAsync(string symbol, double currentPrice)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE Investments SET CurrentPrice=@Price, LastUpdated=@Now
            WHERE Symbol=@Symbol",
            new { Price = currentPrice, Now = DateTime.UtcNow.ToString("O"), Symbol = symbol });
    }

    public async Task DeleteInvestmentAsync(int investmentId)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM Investments WHERE Id=@Id", new { Id = investmentId });
    }

    public async Task<IEnumerable<Investment>> GetInvestmentsAsync(int portfolioId)
    {
        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync(
            "SELECT * FROM Investments WHERE PortfolioId=@PId ORDER BY Symbol",
            new { PId = portfolioId });

        return rows.Select(r => new Investment
        {
            Id            = (int)r.Id,
            Symbol        = r.Symbol,
            Name          = r.Name,
            AssetType     = r.AssetType,
            Sector        = r.Sector ?? string.Empty,
            Quantity      = r.Quantity,
            PurchasePrice = r.PurchasePrice,
            CurrentPrice  = r.CurrentPrice,
            PurchaseDate  = DateTime.Parse(r.PurchaseDate),
            LastUpdated   = DateTime.Parse(r.LastUpdated),
            Currency      = r.Currency ?? "USD",
            Notes         = r.Notes ?? string.Empty
        });
    }

    // ── Snapshots ────────────────────────────────────────────
    public async Task SaveSnapshotAsync(int portfolioId, double totalValue, double totalCost)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO PortfolioSnapshots (PortfolioId, Date, TotalValue, TotalCost)
            VALUES (@PId, @Date, @Val, @Cost)",
            new { PId = portfolioId, Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                  Val = totalValue, Cost = totalCost });
    }

    public async Task<IEnumerable<(DateTime Date, double Value)>> GetSnapshotHistoryAsync(
        int portfolioId, int days)
    {
        await using var conn = CreateConnection();
        var cutoff = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
        var rows   = await conn.QueryAsync(@"
            SELECT Date, TotalValue FROM PortfolioSnapshots
            WHERE PortfolioId=@PId AND Date >= @Cutoff ORDER BY Date",
            new { PId = portfolioId, Cutoff = cutoff });

        return rows.Select(r => (DateTime.Parse((string)r.Date), (double)r.TotalValue));
    }
}
