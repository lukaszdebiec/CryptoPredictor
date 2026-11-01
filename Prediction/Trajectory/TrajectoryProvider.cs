using System.Text.Json;
using CryptoPredictor.Data.Ingestion;
using CryptoPredictor.Data.Storage;
using Microsoft.Extensions.Logging;

namespace CryptoPredictor.Prediction.Trajectory;

public interface ITrajectoryProvider
{
    Task<TrajectoryForecast> GetAsync(DateOnly? asOfDate = null, CancellationToken ct = default);
    Task<bool> TryRefreshCachedHistoryAsync(CancellationToken ct = default);
}

/// <summary>Chart, API and time-machine share the same market cache and issued forecast.</summary>
public sealed class TrajectoryProvider(
    IPriceHistoryCache cache,
    IPriceTrajectoryService model,
    ITrajectorySnapshotStore snapshots,
    IPriceHistoryService? downloader = null,
    ILogger<TrajectoryProvider>? logger = null) : ITrajectoryProvider
{
    public async Task<TrajectoryForecast> GetAsync(DateOnly? asOfDate = null, CancellationToken ct = default)
    {
        var series = await cache.LoadAsync(ct);
        var cutoff = asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var bars = series?.Candles.Where(c => c.Date <= cutoff).OrderBy(c => c.Date).ToList() ?? [];
        if (asOfDate == null && bars.Count > 0) cutoff = bars[^1].Date;
        var saved = await snapshots.LoadAsync(cutoff, cache.Source.ToString(), "BTCUSDT", ct);
        if (saved != null) return saved;
        ct.ThrowIfCancellationRequested();
        var forecast = model.Forecast(bars, cache.Source.ToString(), "BTCUSDT", bars.Count > 0 || asOfDate.HasValue ? cutoff : null);
        return series?.Candles.LastOrDefault()?.Date == cutoff ? await snapshots.SaveAsync(forecast, ct) : forecast;
    }

    public async Task<bool> TryRefreshCachedHistoryAsync(CancellationToken ct = default)
    {
        if (downloader == null) return false;
        var cached = await cache.LoadAsync(ct);
        if (cached == null || cached.Candles.Count == 0) return false;

        try
        {
            var end = DateOnly.FromDateTime(DateTime.UtcNow);
            int span = Math.Clamp(end.DayNumber - cached.Candles[^1].Date.DayNumber + 7, 1, 3650);
            var refreshed = await downloader.DownloadAsync(cache.Source, end.AddDays(-span), end, ct);
            if (refreshed.Candles.Count == 0) return false;
            await cache.SaveAsync(refreshed, ct);
            await GetAsync(ct: ct);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException or FormatException
            || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger?.LogWarning(ex, "Price-trajectory history refresh failed; retaining the existing market cache.");
            return false;
        }
    }
}
