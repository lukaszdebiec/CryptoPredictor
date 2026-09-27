using System.Globalization;
using System.Text.Json;

namespace CryptoPredictor.Data.Ingestion;

public class BinanceDataService : IBinanceDataService
{
    private readonly HttpClient _httpClient;

    public BinanceDataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CryptoPredictor");
    }

    public async Task<IReadOnlyList<BinanceKline>> GetDailyKlinesAsync(string symbol = "BTCUSDT", int limit = 500, CancellationToken ct = default)
    {
        var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval=1d&limit={limit}";
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var jsonDoc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var list = new List<BinanceKline>();

        foreach (var item in jsonDoc.RootElement.EnumerateArray())
        {
            var openTimeMs = item[0].GetInt64();
            var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(openTimeMs).UtcDateTime.Date);
            var open = decimal.Parse(item[1].GetString()!, CultureInfo.InvariantCulture);
            var high = decimal.Parse(item[2].GetString()!, CultureInfo.InvariantCulture);
            var low = decimal.Parse(item[3].GetString()!, CultureInfo.InvariantCulture);
            var close = decimal.Parse(item[4].GetString()!, CultureInfo.InvariantCulture);
            var volume = decimal.Parse(item[5].GetString()!, CultureInfo.InvariantCulture);

            list.Add(new BinanceKline(date, open, high, low, close, volume));
        }

        return list;
    }

    public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyFundingRatesAsync(string symbol = "BTCUSDT", int limit = 1000, CancellationToken ct = default)
    {
        var url = $"https://fapi.binance.com/fapi/v1/fundingRate?symbol={symbol}&limit={limit}";
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var jsonDoc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var dailyRates = new Dictionary<DateOnly, List<decimal>>();

        foreach (var item in jsonDoc.RootElement.EnumerateArray())
        {
            var fundingTimeMs = item.GetProperty("fundingTime").GetInt64();
            var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(fundingTimeMs).UtcDateTime.Date);
            var rate = decimal.Parse(item.GetProperty("fundingRate").GetString()!, CultureInfo.InvariantCulture);

            if (!dailyRates.TryGetValue(date, out var list))
            {
                list = new List<decimal>();
                dailyRates[date] = list;
            }
            list.Add(rate);
        }

        // 24h funding rate = sum of the 8h periods in that day
        return dailyRates.ToDictionary(k => k.Key, v => v.Value.Sum());
    }

    public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyOpenInterestAsync(string symbol = "BTCUSDT", int limit = 500, CancellationToken ct = default)
    {
        var url = $"https://fapi.binance.com/futures/data/openInterestHist?symbol={symbol}&period=1d&limit={limit}";
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var jsonDoc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = new Dictionary<DateOnly, decimal>();

        foreach (var item in jsonDoc.RootElement.EnumerateArray())
        {
            var timestampMs = item.GetProperty("timestamp").GetInt64();
            var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).UtcDateTime.Date);
            var oiUsd = decimal.Parse(item.GetProperty("sumOpenInterestValue").GetString()!, CultureInfo.InvariantCulture);

            result[date] = oiUsd;
        }

        return result;
    }
}
