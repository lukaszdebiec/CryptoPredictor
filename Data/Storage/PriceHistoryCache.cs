using System.Text.Json;
using CryptoPredictor.Data.Ingestion;
using CryptoPredictor.Prediction.Trajectory;

namespace CryptoPredictor.Data.Storage;

public interface IPriceHistoryCache
{
    PriceHistorySource Source { get; }
    Task<PriceHistorySeries?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(PriceHistorySeries series, CancellationToken ct = default);
}

/// <summary>Dedicated source-tagged OHLCV cache; never overwrites enriched MarketData with zero placeholders.</summary>
public sealed class PriceHistoryCache : IPriceHistoryCache
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    public PriceHistorySource Source { get; }
    public PriceHistoryCache(IHostEnvironment host, IConfiguration config)
    {
        string source = config["PriceHistory:Source"] ?? "BinanceUS";
        if (!Enum.TryParse<PriceHistorySource>(source, true, out var parsed) || !Enum.IsDefined(parsed))
            throw new ArgumentException("PriceHistory:Source must be BinanceUS or Binance.");
        Source = parsed;
        _path = Path.Combine(host.ContentRootPath, "Data", "storage", "price-history", $"{Source}_BTCUSDT_1d.json");
    }
    public async Task<PriceHistorySeries?> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_path)) return null;
            using var stream = File.OpenRead(_path);
            var series = await JsonSerializer.DeserializeAsync<PriceHistorySeries>(stream, cancellationToken: ct)
                ?? throw new InvalidDataException("Invalid price-history cache.");
            Validate(series);
            return series;
        }
        finally { _lock.Release(); }
    }
    public async Task SaveAsync(PriceHistorySeries series, CancellationToken ct = default)
    {
        Validate(series);
        if (series.Candles.Count == 0) throw new InvalidDataException("An empty download cannot replace price history.");
        await _lock.WaitAsync(ct);
        string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            // Same-market refreshes extend history without discarding an older backfill.
            if (File.Exists(_path))
            {
                await using var previousStream = File.OpenRead(_path);
                var previous = await JsonSerializer.DeserializeAsync<PriceHistorySeries>(previousStream, cancellationToken: ct)
                    ?? throw new InvalidDataException("Invalid existing price-history cache.");
                Validate(previous);
                var merged = previous.Candles.ToDictionary(b => b.Date);
                foreach (var bar in series.Candles) merged[bar.Date] = bar;
                var from = previous.RequestedFrom < series.RequestedFrom ? previous.RequestedFrom : series.RequestedFrom;
                var to = previous.RequestedToExclusive > series.RequestedToExclusive ? previous.RequestedToExclusive : series.RequestedToExclusive;
                series = series with { RequestedFrom = from, RequestedToExclusive = to,
                    Candles = merged.Values.OrderBy(b => b.Date).ToList(),
                    MissingDates = Enumerable.Range(from.DayNumber, to.DayNumber - from.DayNumber).Select(DateOnly.FromDayNumber).Where(d => !merged.ContainsKey(d)).ToList() };
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, series, cancellationToken: ct);
            File.Move(temp, _path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); _lock.Release(); }
    }
    private void Validate(PriceHistorySeries series)
    {
        if (series.Source != Source.ToString() || series.Symbol != "BTCUSDT" || series.Interval != "1d")
            throw new InvalidDataException("Price-history source, symbol or interval does not match the configured market.");
        int days = series.RequestedToExclusive.DayNumber - series.RequestedFrom.DayNumber;
        if (days < 1 || days > 20000 || series.RequestedToExclusive > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new InvalidDataException("Invalid cache date range.");
        if (series.Candles.Any(b => !PriceTrajectoryService.Valid(b) || b.Date < series.RequestedFrom || b.Date >= series.RequestedToExclusive) ||
            series.Candles.Select(b => b.Date).Distinct().Count() != series.Candles.Count)
            throw new InvalidDataException("Invalid or duplicate cached candles.");
        var dates = series.Candles.Select(b => b.Date).ToHashSet();
        var missing = Enumerable.Range(series.RequestedFrom.DayNumber, series.RequestedToExclusive.DayNumber - series.RequestedFrom.DayNumber)
            .Select(DateOnly.FromDayNumber).Where(d => !dates.Contains(d));
        if (!missing.SequenceEqual(series.MissingDates)) throw new InvalidDataException("Incorrect price-history coverage metadata.");
    }
}
