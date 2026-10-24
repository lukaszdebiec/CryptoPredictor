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
        // Try global Binance first; if geo-blocked (HTTP 451) in US datacenters (e.g. GCP us-central1), fallback to Binance.US
        var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval=1d&limit={limit}";
        var response = await _httpClient.GetAsync(url, ct);

        if ((int)response.StatusCode == 451 || !response.IsSuccessStatusCode)
        {
            response.Dispose();
            var usUrl = $"https://api.binance.us/api/v3/klines?symbol={symbol}&interval=1d&limit={limit}";
            response = await _httpClient.GetAsync(usUrl, ct);
        }

        response.EnsureSuccessStatusCode();

        using (response)
        {
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
    }

    public async Task<IReadOnlyList<BinanceIntradayKline>> GetIntradayKlinesAsync(string symbol = "BTCUSDT", string interval = "4h", int limit = 500, CancellationToken ct = default)
    {
        var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
        var response = await _httpClient.GetAsync(url, ct);

        if ((int)response.StatusCode == 451 || !response.IsSuccessStatusCode)
        {
            response.Dispose();
            var usUrl = $"https://api.binance.us/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
            response = await _httpClient.GetAsync(usUrl, ct);
        }

        response.EnsureSuccessStatusCode();

        using (response)
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var jsonDoc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var list = new List<BinanceIntradayKline>();

            foreach (var item in jsonDoc.RootElement.EnumerateArray())
            {
                var openTimeMs = item[0].GetInt64();
                var openTime = DateTimeOffset.FromUnixTimeMilliseconds(openTimeMs);
                var open = decimal.Parse(item[1].GetString()!, CultureInfo.InvariantCulture);
                var high = decimal.Parse(item[2].GetString()!, CultureInfo.InvariantCulture);
                var low = decimal.Parse(item[3].GetString()!, CultureInfo.InvariantCulture);
                var close = decimal.Parse(item[4].GetString()!, CultureInfo.InvariantCulture);
                var volume = decimal.Parse(item[5].GetString()!, CultureInfo.InvariantCulture);

                list.Add(new BinanceIntradayKline(openTime, open, high, low, close, volume));
            }

            return list;
        }
    }

    public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyFundingRatesAsync(string symbol = "BTCUSDT", int limit = 1000, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://fapi.binance.com/fapi/v1/fundingRate?symbol={symbol}&limit={limit}";
            using var response = await _httpClient.GetAsync(url, ct);

            if ((int)response.StatusCode == 451 || !response.IsSuccessStatusCode)
            {
                // Fallback to Deribit public perpetual funding rates (not geo-blocked in US)
                return await GetDeribitFundingRatesAsync(limit, ct);
            }

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
        catch
        {
            return await GetDeribitFundingRatesAsync(limit, ct);
        }
    }

    public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyOpenInterestAsync(string symbol = "BTCUSDT", int limit = 500, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://fapi.binance.com/futures/data/openInterestHist?symbol={symbol}&period=1d&limit={limit}";
            using var response = await _httpClient.GetAsync(url, ct);

            if ((int)response.StatusCode == 451 || !response.IsSuccessStatusCode)
            {
                return await GetDeribitOpenInterestAsync(ct);
            }

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
        catch
        {
            return await GetDeribitOpenInterestAsync(ct);
        }
    }

    private async Task<IReadOnlyDictionary<DateOnly, decimal>> GetDeribitFundingRatesAsync(int days, CancellationToken ct)
    {
        try
        {
            var endMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var startMs = DateTimeOffset.UtcNow.AddDays(-Math.Max(days, 60)).ToUnixTimeMilliseconds();
            var url = $"https://www.deribit.com/api/v2/public/get_funding_rate_history?instrument_name=BTC-PERPETUAL&start_timestamp={startMs}&end_timestamp={endMs}";

            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return new Dictionary<DateOnly, decimal>();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var jsonDoc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!jsonDoc.RootElement.TryGetProperty("result", out var resultElem) || resultElem.ValueKind != JsonValueKind.Array)
                return new Dictionary<DateOnly, decimal>();

            var dailyRates = new Dictionary<DateOnly, List<decimal>>();

            foreach (var item in resultElem.EnumerateArray())
            {
                var timestampMs = item.GetProperty("timestamp").GetInt64();
                var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).UtcDateTime.Date);

                decimal rate = 0m;
                if (item.TryGetProperty("interest_8h", out var interest8h))
                {
                    rate = interest8h.GetDecimal();
                }

                if (!dailyRates.TryGetValue(date, out var list))
                {
                    list = new List<decimal>();
                    dailyRates[date] = list;
                }
                list.Add(rate);
            }

            return dailyRates.ToDictionary(k => k.Key, v => v.Value.Count > 0 ? v.Value.Average() * 3m : 0m);
        }
        catch
        {
            return new Dictionary<DateOnly, decimal>();
        }
    }

    private async Task<IReadOnlyDictionary<DateOnly, decimal>> GetDeribitOpenInterestAsync(CancellationToken ct)
    {
        try
        {
            var url = "https://www.deribit.com/api/v2/public/get_book_summary_by_instrument?instrument_name=BTC-PERPETUAL";
            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return new Dictionary<DateOnly, decimal>();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var jsonDoc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (jsonDoc.RootElement.TryGetProperty("result", out var resultArr) && resultArr.GetArrayLength() > 0)
            {
                var first = resultArr[0];
                if (first.TryGetProperty("open_interest", out var oiProp))
                {
                    var oi = oiProp.GetDecimal();
                    var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
                    return new Dictionary<DateOnly, decimal> { [today] = oi };
                }
            }
            return new Dictionary<DateOnly, decimal>();
        }
        catch
        {
            return new Dictionary<DateOnly, decimal>();
        }
    }
}
