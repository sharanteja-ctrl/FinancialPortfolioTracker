// ============================================================
//  Core/Models/StockPrice.cs
//  Historical OHLCV price record (used by ML pipelines)
// ============================================================

namespace FinancialPortfolioTracker.Core.Models;

/// <summary>
/// OHLCV price bar for a single trading day.
/// Used as raw training data for ML.NET models.
/// </summary>
public class StockPrice
{
    public int      Id     { get; set; }
    public string   Symbol { get; set; } = string.Empty;
    public DateTime Date   { get; set; }
    public float    Open   { get; set; }
    public float    High   { get; set; }
    public float    Low    { get; set; }
    public float    Close  { get; set; }
    public float    Volume { get; set; }

    // ── Derived fields populated during preprocessing ─────────
    public float DailyReturn     { get; set; }
    public float MA5             { get; set; }   // 5-day moving average
    public float MA20            { get; set; }   // 20-day moving average
    public float MA50            { get; set; }   // 50-day moving average
    public float RSI14           { get; set; }   // Relative Strength Index
    public float MACD            { get; set; }   // MACD line
    public float BollingerUpper  { get; set; }
    public float BollingerLower  { get; set; }
    public float ATR14           { get; set; }   // Average True Range
    public float VolumeMA20      { get; set; }
    public float PriceToMA20     { get; set; }   // Close / MA20 ratio
}

/// <summary>
/// ML.NET input schema for price prediction models.
/// All features must be float (ML.NET requirement).
/// </summary>
public class PricePredictionInput
{
    [Microsoft.ML.Data.ColumnName("Open")]
    public float Open         { get; set; }

    [Microsoft.ML.Data.ColumnName("High")]
    public float High         { get; set; }

    [Microsoft.ML.Data.ColumnName("Low")]
    public float Low          { get; set; }

    [Microsoft.ML.Data.ColumnName("Close")]
    public float Close        { get; set; }

    [Microsoft.ML.Data.ColumnName("Volume")]
    public float Volume       { get; set; }

    [Microsoft.ML.Data.ColumnName("MA5")]
    public float MA5          { get; set; }

    [Microsoft.ML.Data.ColumnName("MA20")]
    public float MA20         { get; set; }

    [Microsoft.ML.Data.ColumnName("MA50")]
    public float MA50         { get; set; }

    [Microsoft.ML.Data.ColumnName("RSI14")]
    public float RSI14        { get; set; }

    [Microsoft.ML.Data.ColumnName("MACD")]
    public float MACD         { get; set; }

    [Microsoft.ML.Data.ColumnName("ATR14")]
    public float ATR14        { get; set; }

    [Microsoft.ML.Data.ColumnName("PriceToMA20")]
    public float PriceToMA20  { get; set; }

    [Microsoft.ML.Data.ColumnName("DailyReturn")]
    public float DailyReturn  { get; set; }
}

/// <summary>ML.NET output schema for regression price prediction.</summary>
public class PricePredictionOutput
{
    [Microsoft.ML.Data.ColumnName("Score")]
    public float PredictedPrice { get; set; }
}

/// <summary>ML.NET output schema for anomaly detection.</summary>
public class AnomalyOutput
{
    [Microsoft.ML.Data.ColumnName("Prediction")]
    public double[] Prediction { get; set; } = Array.Empty<double>();
}
