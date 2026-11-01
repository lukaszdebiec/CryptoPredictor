using CryptoPredictor.Data.Storage;

namespace CryptoPredictor.Prediction.Trajectory;

public interface ITrajectoryProvider
{
    Task<TrajectoryForecast> GetAsync(DateOnly? asOfDate = null, CancellationToken ct = default);
}

/// <summary>Chart, API and time-machine share the same market cache and issued forecast.</summary>
public sealed class TrajectoryProvider(IPriceHistoryCache cache, IPriceTrajectoryService model, ITrajectorySnapshotStore snapshots) : ITrajectoryProvider
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
}
