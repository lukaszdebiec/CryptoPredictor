using System.Text.Json;

namespace CryptoPredictor.Data.Ingestion;

public class CoinbaseDataService : ICoinbaseDataService
{
    private readonly HttpClient _httpClient;

    public CoinbaseDataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CryptoPredictor");
    }

    public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyClosePricesAsync(CancellationToken ct = default)
    {
        // 86400 is 1 day in seconds
        var url = "https://api.exchange.coinbase.com/products/BTC-USD/candles?granularity=86400";
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var jsonDoc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = new Dictionary<DateOnly, decimal>();

        // Format: [ [ time, low, high, open, close, volume ], ... ]
        foreach (var item in jsonDoc.RootElement.EnumerateArray())
        {
            var timeSec = item[0].GetInt64();
            var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timeSec).UtcDateTime.Date);
            var close = item[4].GetDecimal();

            result[date] = close;
        }

        return result;
    }
}
