// ============================================================
//  Data/DatabaseInitializer.cs
//  Creates SQLite schema on first run
// ============================================================

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace FinancialPortfolioTracker.Data;

/// <summary>
/// Initializes the SQLite database schema for the portfolio tracker.
/// </summary>
public class DatabaseInitializer
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(string connectionString, ILogger<DatabaseInitializer> logger)
    {
        _connectionString = connectionString;
        _logger           = logger;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing database...");
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // ── Portfolios ──────────────────────────────────────
        await ExecAsync(connection, @"
            CREATE TABLE IF NOT EXISTS Portfolios (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Name        TEXT    NOT NULL,
                Description TEXT,
                Currency    TEXT    DEFAULT 'USD',
                CreatedAt   TEXT    NOT NULL,
                LastUpdated TEXT    NOT NULL
            );");

        // ── Investments ─────────────────────────────────────
        await ExecAsync(connection, @"
            CREATE TABLE IF NOT EXISTS Investments (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                PortfolioId   INTEGER NOT NULL REFERENCES Portfolios(Id),
                Symbol        TEXT    NOT NULL,
                Name          TEXT    NOT NULL,
                AssetType     TEXT    NOT NULL DEFAULT 'Stock',
                Sector        TEXT,
                Quantity      REAL    NOT NULL,
                PurchasePrice REAL    NOT NULL,
                CurrentPrice  REAL    NOT NULL DEFAULT 0,
                PurchaseDate  TEXT    NOT NULL,
                LastUpdated   TEXT    NOT NULL,
                Currency      TEXT    DEFAULT 'USD',
                Notes         TEXT
            );");

        // ── StockPrices ─────────────────────────────────────
        await ExecAsync(connection, @"
            CREATE TABLE IF NOT EXISTS StockPrices (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Symbol      TEXT    NOT NULL,
                Date        TEXT    NOT NULL,
                Open        REAL    NOT NULL,
                High        REAL    NOT NULL,
                Low         REAL    NOT NULL,
                Close       REAL    NOT NULL,
                Volume      REAL    NOT NULL,
                DailyReturn REAL,
                MA5         REAL,
                MA20        REAL,
                MA50        REAL,
                RSI14       REAL,
                MACD        REAL,
                ATR14       REAL,
                VolumeMA20  REAL,
                PriceToMA20 REAL,
                UNIQUE(Symbol, Date)
            );");

        // ── Recommendations ─────────────────────────────────
        await ExecAsync(connection, @"
            CREATE TABLE IF NOT EXISTS Recommendations (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                Symbol          TEXT    NOT NULL,
                Type            TEXT    NOT NULL,
                Title           TEXT    NOT NULL,
                Description     TEXT,
                ConfidenceScore REAL,
                PredictedPrice  REAL,
                CurrentPrice    REAL,
                TargetPrice     REAL,
                StopLoss        REAL,
                RiskLevel       TEXT,
                TimeHorizon     TEXT,
                Reasoning       TEXT,
                GeneratedAt     TEXT    NOT NULL
            );");

        // ── PortfolioSnapshots (daily NAV) ───────────────────
        await ExecAsync(connection, @"
            CREATE TABLE IF NOT EXISTS PortfolioSnapshots (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                PortfolioId  INTEGER NOT NULL REFERENCES Portfolios(Id),
                Date         TEXT    NOT NULL,
                TotalValue   REAL    NOT NULL,
                TotalCost    REAL    NOT NULL,
                DailyReturn  REAL,
                UNIQUE(PortfolioId, Date)
            );");

        // ── Indexes ─────────────────────────────────────────
        await ExecAsync(connection, "CREATE INDEX IF NOT EXISTS idx_prices_symbol ON StockPrices(Symbol);");
        await ExecAsync(connection, "CREATE INDEX IF NOT EXISTS idx_prices_date   ON StockPrices(Date);");
        await ExecAsync(connection, "CREATE INDEX IF NOT EXISTS idx_inv_portfolio  ON Investments(PortfolioId);");

        _logger.LogInformation("Database initialized successfully.");
    }

    private static async Task ExecAsync(SqliteConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
