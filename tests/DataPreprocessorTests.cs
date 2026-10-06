// ============================================================
//  Tests/DataPreprocessorTests.cs
//  Tests for CSV parsing, outlier removal, and feature engineering
// ============================================================

using FinancialPortfolioTracker.Core.Models;
using FinancialPortfolioTracker.Data;
using FinancialPortfolioTracker.Data.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinancialPortfolioTracker.Tests;

public class DataPreprocessorTests
{
    private const string SampleCsv = """
        Symbol,Date,Open,High,Low,Close,Volume
        AAPL,2023-01-03,130.28,130.90,124.17,125.07,112117500
        AAPL,2023-01-04,126.89,128.66,125.08,126.36,89113600
        AAPL,2023-01-05,127.13,127.77,124.76,125.02,80962700
        AAPL,2023-01-06,126.01,130.29,124.89,129.62,87754700
        AAPL,2023-01-09,130.47,133.41,129.89,130.15,70790800
        AAPL,2023-01-10,130.26,131.26,128.12,130.73,63896800
        AAPL,2023-01-11,131.25,133.51,131.22,133.49,69458900
        AAPL,2023-01-12,133.88,134.26,131.44,133.41,71379600
        AAPL,2023-01-13,132.03,132.42,129.18,134.76,57809700
        AAPL,2023-01-17,134.83,137.29,134.13,135.94,63646600
        """;

    private string WriteTempCsv(string content)
    {
        var path = Path.GetTempFileName() + ".csv";
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task LoadAndProcess_ValidCsv_ReturnsPositiveCount()
    {
        var mockRepo = new Mock<IStockPriceRepository>();
        mockRepo.Setup(r => r.BulkInsertAsync(It.IsAny<IEnumerable<StockPrice>>()))
                .Returns(Task.CompletedTask);

        var preprocessor = new DataPreprocessor(mockRepo.Object,
            NullLogger<DataPreprocessor>.Instance, maxThreads: 2);

        var path  = WriteTempCsv(SampleCsv);
        var count = await preprocessor.LoadAndProcessCsvAsync(path);

        Assert.True(count > 0);
        File.Delete(path);
    }

    [Fact]
    public async Task LoadAndProcess_CallsBulkInsert()
    {
        var mockRepo  = new Mock<IStockPriceRepository>();
        var callCount = 0;
        mockRepo.Setup(r => r.BulkInsertAsync(It.IsAny<IEnumerable<StockPrice>>()))
                .Callback(() => callCount++)
                .Returns(Task.CompletedTask);

        var preprocessor = new DataPreprocessor(mockRepo.Object,
            NullLogger<DataPreprocessor>.Instance, maxThreads: 2);

        var path = WriteTempCsv(SampleCsv);
        await preprocessor.LoadAndProcessCsvAsync(path);

        Assert.True(callCount >= 1);
        File.Delete(path);
    }

    [Fact]
    public async Task LoadAndProcess_MissingFile_ThrowsException()
    {
        var mockRepo = new Mock<IStockPriceRepository>();
        var preprocessor = new DataPreprocessor(mockRepo.Object,
            NullLogger<DataPreprocessor>.Instance);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => preprocessor.LoadAndProcessCsvAsync("/non/existent/path.csv"));
    }

    [Fact]
    public async Task LoadAndProcess_InvalidCsvFormat_ThrowsException()
    {
        var mockRepo = new Mock<IStockPriceRepository>();
        var preprocessor = new DataPreprocessor(mockRepo.Object,
            NullLogger<DataPreprocessor>.Instance);

        var path = WriteTempCsv("WrongCol1,WrongCol2\n1,2");
        await Assert.ThrowsAsync<InvalidDataException>(
            () => preprocessor.LoadAndProcessCsvAsync(path));
        File.Delete(path);
    }

    [Fact]
    public async Task LoadAndProcess_MultipleSymbols_ProcessesAll()
    {
        var insertedPrices = new List<StockPrice>();
        var mockRepo = new Mock<IStockPriceRepository>();
        mockRepo.Setup(r => r.BulkInsertAsync(It.IsAny<IEnumerable<StockPrice>>()))
                .Callback<IEnumerable<StockPrice>>(prices => insertedPrices.AddRange(prices))
                .Returns(Task.CompletedTask);

        var twoSymbolCsv = SampleCsv +
            "\nMSFT,2023-01-03,239.58,245.29,236.34,239.23,35550200" +
            "\nMSFT,2023-01-04,242.01,249.65,241.49,244.82,41567200";

        var preprocessor = new DataPreprocessor(mockRepo.Object,
            NullLogger<DataPreprocessor>.Instance, maxThreads: 2);

        var path = WriteTempCsv(twoSymbolCsv);
        await preprocessor.LoadAndProcessCsvAsync(path);

        var symbols = insertedPrices.Select(p => p.Symbol).Distinct().ToList();
        Assert.Contains("AAPL", symbols);
        Assert.Contains("MSFT", symbols);
        File.Delete(path);
    }
}
