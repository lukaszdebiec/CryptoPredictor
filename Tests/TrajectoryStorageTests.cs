using System.Net;
using CryptoPredictor.Controllers;
using CryptoPredictor.Data;
using CryptoPredictor.Data.Ingestion;
using CryptoPredictor.Data.Storage;
using CryptoPredictor.Prediction.Trajectory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CryptoPredictor.Tests;

public class TrajectoryStorageTests
{
    [Fact]
    public async Task SameSourceRefreshExtendsHistoryWithoutDiscardingBackfill()
    {
        using var temp = new TemporaryStore();
        var cache = temp.Cache;
        var bars = PredictionTests.History(100);
        await cache.SaveAsync(Series(bars.Take(80).ToList()));
        await cache.SaveAsync(Series(bars.Skip(70).ToList()));
        var loaded = await cache.LoadAsync();
        Assert.Equal(100, loaded!.Candles.Count);
        Assert.True(loaded.IsComplete);
        Assert.Equal(bars[0].Date, loaded.RequestedFrom);
        Assert.Equal(bars[^1].Date.AddDays(1), loaded.RequestedToExclusive);
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.SaveAsync(Series(bars) with { Source = "Binance" }));
        Assert.Equal(100, (await cache.LoadAsync())!.Candles.Count);
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.SaveAsync(Series(bars) with { MissingDates = [bars[0].Date] }));
    }

    [Fact]
    public async Task FailedDownloadPreservesExistingCacheAndReturnsActionableHttpFailure()
    {
        using var temp = new TemporaryStore();
        await temp.Cache.SaveAsync(Series(PredictionTests.History(100)));
        var controller = new TrajectoryController(null!, new DeniedDownloader(), temp.Cache);
        var result = Assert.IsType<ObjectResult>(await controller.SyncHistory(ct: default));
        Assert.Equal(502, result.StatusCode);
        Assert.Equal(100, (await temp.Cache.LoadAsync())!.Candles.Count);
    }

    [Fact]
    public async Task TryRefreshCachedHistoryExtendsExistingBackfillAndSkipsWhenEmpty()
    {
        using var temp = new TemporaryStore();
        var bars = PredictionTests.History(805);
        var tailDownloader = new TailDownloader(bars.Skip(790).ToList());
        var snapshots = new TrajectorySnapshotStore(temp.Factory);
        var provider = new TrajectoryProvider(temp.Cache, new PriceTrajectoryService(), snapshots, tailDownloader);

        Assert.False(await provider.TryRefreshCachedHistoryAsync());
        Assert.Null(await temp.Cache.LoadAsync());

        await temp.Cache.SaveAsync(Series(bars.Take(800).ToList()));
        Assert.True(await provider.TryRefreshCachedHistoryAsync());
        var refreshed = await temp.Cache.LoadAsync();
        Assert.Equal(805, refreshed!.Candles.Count);
        Assert.NotNull(await snapshots.LoadAsync(bars[^1].Date, "BinanceUS", "BTCUSDT"));

        var failingProvider = new TrajectoryProvider(temp.Cache, new PriceTrajectoryService(), snapshots, new DeniedDownloader());
        Assert.False(await failingProvider.TryRefreshCachedHistoryAsync());
        Assert.Equal(805, (await temp.Cache.LoadAsync())!.Candles.Count);
    }

    [Fact]
    public async Task IssuedTrajectoryIsImmutableAndDoesNotAlterDirectionalSnapshots()
    {
        using var temp = new TemporaryStore();
        var bars = PredictionTests.History(800);
        var model = new PriceTrajectoryService();
        var first = model.Forecast(bars, "BinanceUS", "BTCUSDT");
        var store = new TrajectorySnapshotStore(temp.Factory);
        var unavailable = first with { Status = "INSUFFICIENT_DATA", Points = [] };
        await store.SaveAsync(unavailable);
        Assert.Null(await store.LoadAsync(first.AsOfDate!.Value, first.Source, first.Symbol));
        await store.SaveAsync(first);
        var canonical = await store.SaveAsync(first with { DataFingerprint = "corrected after issue", Message = "replace me" });
        Assert.Equal(first.DataFingerprint, canonical.DataFingerprint);
        Assert.Equal(first.Points, canonical.Points);
        await using var db = temp.Factory.CreateDbContext();
        Assert.Equal(0, await db.Predictions.CountAsync());
        Assert.Equal(0, await db.MarketData.CountAsync());
    }

    [Fact]
    public async Task ProviderAndChartUseTheSameIssuedForecastAndShowMissingHistoryExplicitly()
    {
        using var temp = new TemporaryStore();
        var provider = new TrajectoryProvider(temp.Cache, new PriceTrajectoryService(), new TrajectorySnapshotStore(temp.Factory));
        var empty = await provider.GetAsync();
        Assert.Equal("INSUFFICIENT_DATA", empty.Status);
        Assert.Empty(empty.Points);
        await temp.Cache.SaveAsync(Series(PredictionTests.History(800)));
        var issued = await provider.GetAsync();
        var controller = new TrajectoryController(provider, null!, temp.Cache);
        var action = await controller.Chart(30);
        var chart = Assert.IsType<MarketChartResponse>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal(30, chart.Candles.Count);
        Assert.Equal(issued.GeneratedAtUtc, chart.Trajectory!.GeneratedAtUtc);
        Assert.Equal(issued.Points, chart.Trajectory.Points);
        Assert.Null(chart.Prediction); // Does not invent a directional/trading signal.
    }

    private static PriceHistorySeries Series(List<DailyMarketData> bars) => new()
    {
        Source = "BinanceUS", RequestedFrom = bars[0].Date, RequestedToExclusive = bars[^1].Date.AddDays(1), Candles = bars
    };
    private sealed class TailDownloader(List<DailyMarketData> bars) : IPriceHistoryService
    {
        public Task<PriceHistorySeries> DownloadAsync(PriceHistorySource source, DateOnly from, DateOnly toExclusive, CancellationToken ct = default)
            => Task.FromResult(Series(bars));
    }
    private sealed class DeniedDownloader : IPriceHistoryService
    {
        public Task<PriceHistorySeries> DownloadAsync(PriceHistorySource source, DateOnly from, DateOnly toExclusive, CancellationToken ct = default)
            => throw new HttpRequestException("Proxy refused public API access.", null, HttpStatusCode.Forbidden);
    }
    private sealed class TemporaryStore : IDisposable, IHostEnvironment
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "trajectory-tests-" + Guid.NewGuid().ToString("N"));
        public PriceHistoryCache Cache { get; }
        public FactoryImpl Factory { get; }
        public TemporaryStore()
        {
            Directory.CreateDirectory(_root);
            Cache = new PriceHistoryCache(this, new ConfigurationBuilder().Build());
            var options = new DbContextOptionsBuilder<CryptoPredictorDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "test.db")};Pooling=False").Options;
            Factory = new FactoryImpl(options);
            using var db = Factory.CreateDbContext(); db.Database.EnsureCreated();
        }
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "CryptoPredictor.Tests";
        public string ContentRootPath { get => _root; set => throw new NotSupportedException(); }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
    private sealed class FactoryImpl(DbContextOptions<CryptoPredictorDbContext> options) : IDbContextFactory<CryptoPredictorDbContext>
    {
        public CryptoPredictorDbContext CreateDbContext() => new(options);
    }
}
