using System.Globalization;
using System.Text.Json;

namespace CryptoPredictor.Data.Ingestion;

public class CoinbaseDataService : ICoinbaseDataService
{
    private const int MaxCandlesPerChunk = 290; // Coinbase Exchange caps /candles responses at 300 bars
    private readonly HttpClient _httpClient;

    public CoinbaseDataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CryptoPredictor");
    }

    public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyClosePricesAsync(int days = 300, CancellationToken ct = default)
    {
        int targetDays = Math.Clamp(days, 1, 2000);
        var todayUtcDate = DateTimeOffset.UtcNow.UtcDateTime.Date;
        var todayOnly = DateOnly.FromDateTime(todayUtcDate);
        var result = new Dictionary<DateOnly, decimal>();

        for (var chunkStart = todayUtcDate.AddDays(-targetDays); chunkStart < todayUtcDate; chunkStart = chunkStart.AddDays(MaxCandlesPerChunk))
        {
            var chunkEnd = chunkStart.AddDays(MaxCandlesPerChunk);
            if (chunkEnd > todayUtcDate)
                chunkEnd = todayUtcDate;

            var startIso = chunkStart.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var endIso = chunkEnd.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var url = $"https://api.exchange.coinbase.com/products/BTC-USD/candles?granularity=86400&start={startIso}&end={endIso}";

            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                if (result.Count == 0)
                    response.EnsureSuccessStatusCode();
                break;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var jsonDoc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            // Format: [ [ time, low, high, open, close, volume ], ... ]
            foreach (var item in jsonDoc.RootElement.EnumerateArray())
            {
                var timeSec = item[0].GetInt64();
                var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timeSec).UtcDateTime.Date);
                if (date >= todayOnly)
                    continue; // Exclude unfinished intraday UTC candle

                var close = item[4].GetDecimal();
                if (close > 0m)
                    result[date] = close;
            }
        }

        return result;
    }
}
