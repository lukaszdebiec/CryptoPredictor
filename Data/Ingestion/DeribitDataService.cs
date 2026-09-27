using System.Text.Json;

namespace CryptoPredictor.Data.Ingestion;

public class DeribitDataService : IDeribitDataService
{
    private readonly HttpClient _httpClient;

    public DeribitDataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CryptoPredictor");
    }

    public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyDvolHistoryAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct = default)
    {
        var startMs = new DateTimeOffset(startUtc).ToUnixTimeMilliseconds();
        var endMs = new DateTimeOffset(endUtc).ToUnixTimeMilliseconds();

        var url = $"https://www.deribit.com/api/v2/public/get_volatility_index_data?currency=BTC&resolution=1D&start_timestamp={startMs}&end_timestamp={endMs}";
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var jsonDoc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = new Dictionary<DateOnly, decimal>();

        if (jsonDoc.RootElement.TryGetProperty("result", out var resultObj) &&
            resultObj.TryGetProperty("data", out var dataArray))
        {
            foreach (var item in dataArray.EnumerateArray())
            {
                var timestampMs = item[0].GetInt64();
                var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).UtcDateTime.Date);
                var closeDvol = item[4].GetDecimal();

                result[date] = closeDvol;
            }
        }

        return result;
    }
}
