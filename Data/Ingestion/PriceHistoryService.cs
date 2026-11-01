using System.Globalization;
using System.Net;
using System.Text.Json;
using CryptoPredictor.Prediction.Trajectory;

namespace CryptoPredictor.Data.Ingestion;

public enum PriceHistorySource { BinanceUS, Binance }

public sealed record PriceHistorySeries
{
    public required string Source { get; init; }
    public string Symbol { get; init; } = "BTCUSDT";
    public string Interval { get; init; } = "1d";
    public DateOnly RequestedFrom { get; init; }
    public DateOnly RequestedToExclusive { get; init; }
    public DateTime RetrievedAtUtc { get; init; } = DateTime.UtcNow;
    public IReadOnlyList<DailyMarketData> Candles { get; init; } = Array.Empty<DailyMarketData>();
    public IReadOnlyList<DateOnly> MissingDates { get; init; } = Array.Empty<DateOnly>();
    public bool IsComplete => MissingDates.Count == 0 && Candles.Count > 0;
}

public interface IPriceHistoryService
{
    Task<PriceHistorySeries> DownloadAsync(PriceHistorySource source, DateOnly from, DateOnly toExclusive, CancellationToken ct = default);
}

/// <summary>Paginated public candles from one explicitly selected market. No per-page exchange fallback.</summary>
public sealed class PriceHistoryService(HttpClient client) : IPriceHistoryService
{
    public async Task<PriceHistorySeries> DownloadAsync(PriceHistorySource source, DateOnly from, DateOnly toExclusive, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!Enum.IsDefined(source) || toExclusive > today || from >= toExclusive || toExclusive.DayNumber - from.DayNumber > 3650)
            throw new ArgumentException("Choose one supported source and between 1 and 3650 completed UTC days.");
        string host = source == PriceHistorySource.BinanceUS ? "api.binance.us" : "api.binance.com";
        long cursor = Epoch(from), end = Epoch(toExclusive) - 1;
        var candles = new SortedDictionary<DateOnly, DailyMarketData>();
        while (cursor <= end)
        {
            string url = $"https://{host}/api/v3/klines?symbol=BTCUSDT&interval=1d&startTime={cursor}&endTime={end}&limit=1000";
            using var response = await GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (json.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Expected an array of exchange candles.");
            if (json.RootElement.GetArrayLength() == 0) break;
            long next = cursor;
            foreach (var item in json.RootElement.EnumerateArray())
            {
                long openMs = item[0].GetInt64(), closeMs = item[6].GetInt64();
                if (openMs % 86_400_000 != 0 || closeMs != openMs + 86_400_000 - 1)
                    throw new InvalidDataException("Exchange daily candle timestamps are not aligned to UTC.");
                next = Math.Max(next, openMs + 86_400_000);
                var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(openMs).UtcDateTime);
                if (date < from || date >= toExclusive || closeMs >= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) continue;
                decimal Number(int index) => decimal.Parse(item[index].GetString()!, CultureInfo.InvariantCulture);
                var bar = new DailyMarketData { Date = date, BtcOpen = Number(1), BtcHigh = Number(2), BtcLow = Number(3), BtcClose = Number(4), BtcVolume = Number(5) };
                if (!PriceTrajectoryService.Valid(bar)) throw new InvalidDataException($"Invalid OHLCV on {date}.");
                if (candles.TryGetValue(date, out var prior) && prior != bar)
                    throw new InvalidDataException($"Conflicting duplicate daily candle on {date}.");
                candles[date] = bar;
            }
            if (next <= cursor) throw new InvalidDataException("Exchange pagination did not advance; partial history was not accepted.");
            cursor = next;
        }
        return new PriceHistorySeries
        {
            Source = source.ToString(), RequestedFrom = from, RequestedToExclusive = toExclusive,
            Candles = candles.Values.ToList(),
            MissingDates = Enumerable.Range(from.DayNumber, toExclusive.DayNumber - from.DayNumber)
                .Select(DateOnly.FromDayNumber).Where(d => !candles.ContainsKey(d)).ToList()
        };
    }

    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            var response = await client.GetAsync(url, ct);
            bool retry = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            if (!retry || attempt >= 2) return response;
            var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(attempt + 1);
            // Never override a server's longer rate-limit delay by issuing an earlier retry.
            if (delay > TimeSpan.FromSeconds(30)) return response;
            response.Dispose();
            await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, ct);
        }
    }
    private static long Epoch(DateOnly d) => new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}
