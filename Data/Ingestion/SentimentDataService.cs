using System.Globalization;
using System.Text.Json;

namespace CryptoPredictor.Data.Ingestion;

public class SentimentDataService : ISentimentDataService
{
    private readonly HttpClient _httpClient;

    public SentimentDataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CryptoPredictor");
    }

    public async Task<IReadOnlyDictionary<DateOnly, int>> GetFearAndGreedHistoryAsync(int limit = 0, CancellationToken ct = default)
    {
        var url = $"https://api.alternative.me/fng/?limit={limit}";
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var jsonDoc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = new Dictionary<DateOnly, int>();

        if (jsonDoc.RootElement.TryGetProperty("data", out var dataArray))
        {
            foreach (var item in dataArray.EnumerateArray())
            {
                var timestampStr = item.GetProperty("timestamp").GetString();
                if (long.TryParse(timestampStr, out var timestampSec))
                {
                    var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestampSec).UtcDateTime.Date);
                    var valueStr = item.GetProperty("value").GetString();
                    if (int.TryParse(valueStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    {
                        result[date] = value;
                    }
                }
            }
        }

        return result;
    }
}
