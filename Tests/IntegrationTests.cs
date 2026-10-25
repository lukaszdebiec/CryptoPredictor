using System.Net;
using System.Text;
using System.Text.Json;
using CryptoPredictor.Backtest;
using CryptoPredictor.Data;
using CryptoPredictor.Data.Ingestion;
using CryptoPredictor.Data.Storage;
using CryptoPredictor.Features;
using CryptoPredictor.Prediction;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CryptoPredictor.Tests;

public class IntegrationTests
{
    [Fact]
    public async Task BinanceDailyIngestionExcludesTheOpenCandle()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string payload = JsonSerializer.Serialize(new object[][]
        {
            [now - 172800000, "100", "101", "99", "100", "1000", now - 86400001],
            [now - 1000, "100", "101", "99", "100", "1000", now + 86400000]
        });
        var client = new HttpClient(new FixedHandler(payload));
        var bars = await new BinanceDataService(client).GetDailyKlinesAsync();
        Assert.Single(bars);
        Assert.True(bars[0].Date < DateOnly.FromDateTime(DateTime.UtcNow));
    }

    [Fact]
    public async Task OldSqliteSchemaMigratesAdditivelyAndFirstForecastIsImmutable()
    {
        var root = Path.Combine(Path.GetTempPath(), "crypto-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Data", "storage"));
        string path = Path.Combine(root, "Data", "storage", "market_data.db");
        var options = new DbContextOptionsBuilder<CryptoPredictorDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        try
        {
            await using (var db = new CryptoPredictorDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                // Simulate the actual pre-refactor schema without the new snapshot columns.
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE MarketPredictions DROP COLUMN PredictionJson");
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE MarketPredictions DROP COLUMN FeatureSnapshotJson");
            }
            var storage = new SqliteMarketDataStorage(new Factory(options), new TestHost(root), NullLogger<SqliteMarketDataStorage>.Instance);
            var first = OutcomeTests.Forecast() with { GeneratedAtUtc = new DateTime(2020, 1, 2, 0, 5, 0, DateTimeKind.Utc), FeatureSnapshotJson = "{\"recorded\":true}" };
            await storage.SavePredictionAsync(first);
            var canonical = await storage.SavePredictionAsync(first with { BullishProbability = .99m, Signal = "BEARISH_RISK" });
            Assert.Equal(first.BullishProbability, canonical.BullishProbability);
            var loaded = await storage.GetLatestPredictionAsync();
            Assert.NotNull(loaded);
            Assert.Equal(first.Signal, loaded.Signal);
            Assert.Equal(first.BullishProbability, loaded.BullishProbability);
            Assert.Equal(first.GeneratedAtUtc, loaded.GeneratedAtUtc);
            Assert.Equal(first.FeatureSnapshotJson, loaded.FeatureSnapshotJson);
            await storage.SaveAsync([OutcomeTests.Bar(0), OutcomeTests.Bar(1), OutcomeTests.Bar(2)]);
            var journal = await storage.GetPredictionJournalAsync();
            Assert.Single(journal.Items);
            Assert.Equal("PENDING", journal.Items[0].OutcomeStatus);
            Assert.Equal(PredictionOutcomeResolver.Resolve(first, [OutcomeTests.Bar(0), OutcomeTests.Bar(1), OutcomeTests.Bar(2)]), journal.Items[0]);
            Assert.Equal(first.BullishProbability, (await storage.GetLatestPredictionAsync())!.BullishProbability);
            await using (var db = new CryptoPredictorDbContext(options))
            {
                db.Predictions.Add(new MarketPredictionRecord { Date = first.Date.AddDays(-1), BtcClose = 100m,
                    Signal = "NEUTRAL_HOLD", Confidence = "legacy", MarketRegime = "legacy" });
                await db.SaveChangesAsync();
            }
            var legacy = await storage.SavePredictionAsync(first with { Date = first.Date.AddDays(-1) });
            Assert.Equal("legacy", legacy.ModelVersion);
            Assert.Equal(2, (await storage.GetPredictionHistoryAsync()).Count);
            Assert.Single((await storage.GetPredictionJournalAsync()).Items);
            await storage.SaveAsync([OutcomeTests.Bar(0) with { Date = DateOnly.FromDateTime(DateTime.UtcNow) }]);
            Assert.Equal(3, (await storage.LoadAsync()).Count);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void WalkForwardIsInvariantToFutureDataAndCacheDetectsCorrections()
    {
        var history = PredictionTests.History(175);
        var calc = new FeatureCalculator();
        var detector = new TurningPointDetector();
        var predictor = new PredictorService();
        var first = predictor.GenerateWalkForwardPredictions(history, calc.Calculate(history), detector);
        var longer = PredictionTests.History(180);
        longer[^1] = longer[^1] with { BtcClose = longer[^1].BtcClose * 1.2m };
        var second = predictor.GenerateWalkForwardPredictions(longer, calc.Calculate(longer), detector);
        Assert.Equal(first[^1].BullishProbability, second.Single(p => p.Date == history[^1].Date).BullishProbability);
        Assert.Equal(first[^1].Validation, second.Single(p => p.Date == history[^1].Date).Validation);
        var corrected = history.ToList();
        corrected[^1] = corrected[^1] with { BtcClose = corrected[^1].BtcClose * 1.01m };
        var third = predictor.GenerateWalkForwardPredictions(corrected, calc.Calculate(corrected), detector);
        Assert.NotSame(first, third);
        Assert.NotEqual(first[^1].BtcClose, third[^1].BtcClose);
    }

    [Fact]
    public void BacktestUsesWalkForwardForecastsAndActualExitDates()
    {
        var bars = Enumerable.Range(0, 10).Select(i => OutcomeTests.Bar(i, i is 1 or 2 ? 107m : 101m)).ToList();
        var fake = new ForecastProvider([
            OutcomeTests.Forecast(), OutcomeTests.Forecast() with { Date = OutcomeTests.Day.AddDays(1) }
        ]);
        var report = new BacktestEngine().RunSimulation(bars, [], fake,
            new BacktestSettings { FeePerSide = 0m, SlippagePerSide = 0m });
        Assert.Equal(2, report.TotalTrades);
        Assert.Equal(report.Trades[0].ExitDate, report.Trades[1].EntryDate);
        Assert.Equal(11236m, report.FinalCapital);
        Assert.Equal("TAKE_PROFIT", report.Trades[0].ExitReason);
    }

    [Fact]
    public void BacktestTracksInterimDrawdownAndExplicitEndOfDataExit()
    {
        var bars = Enumerable.Range(0, 5).Select(i => OutcomeTests.Bar(i, 102m, i == 2 ? 89m : 99m, i == 2 ? 90m : 100m)).ToList();
        var fake = new ForecastProvider([OutcomeTests.Forecast()]);
        var report = new BacktestEngine().RunSimulation(bars, [], fake,
            new BacktestSettings { StopLossPercent = .3m, TakeProfitPercent = .5m });
        Assert.Single(report.Trades);
        Assert.Equal("END_OF_DATA", report.Trades[0].ExitReason);
        Assert.True(report.StrategyMaxDrawdownPercent >= 10m);
        Assert.Equal(9980m, report.FinalCapital);
    }

    private sealed class FixedHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") });
    }
    private sealed class Factory(DbContextOptions<CryptoPredictorDbContext> options) : IDbContextFactory<CryptoPredictorDbContext>
    {
        public CryptoPredictorDbContext CreateDbContext() => new(options);
    }
    private sealed class TestHost(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "CryptoPredictor.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class ForecastProvider(IReadOnlyList<MarketPrediction> forecasts) : IPredictorService
    {
        public bool IsTrained => true;
        public ModelTrainingMetrics? LastTrainingMetrics => null;
        public ModelTrainingMetrics TrainModel(IReadOnlyList<ModelTrainingSample> d) => throw new InvalidOperationException("The simulation must not train/reuse a full-history singleton.");
        public MarketPrediction Predict(DailyFeatures f, DailyMarketData m) => throw new InvalidOperationException("Full-history prediction must not be used by backtesting.");
        public IReadOnlyList<MarketPrediction> GenerateWalkForwardPredictions(IReadOnlyList<DailyMarketData> m, IReadOnlyList<DailyFeatures> f, ITurningPointDetector d) => forecasts;
        public PredictionJournalResponse GenerateWalkForwardJournal(IReadOnlyList<DailyMarketData> m, IReadOnlyList<DailyFeatures> f, ITurningPointDetector d) => throw new NotSupportedException();
    }
}
