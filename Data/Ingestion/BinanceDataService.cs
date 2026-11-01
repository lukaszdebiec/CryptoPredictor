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
                if (item[6].GetInt64() >= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                    continue; // Only final daily candles may enter features or labels.
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

    public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyFundingRatesAsync(string symbol = "BTCUSDT", int days = 365, CancellationToken ct = default)
    {
        int targetDays = Math.Clamp(days, 1, 2000);
        var todayUtcDate = DateTimeOffset.UtcNow.UtcDateTime.Date;
        var todayOnly = DateOnly.FromDateTime(todayUtcDate);
        long cursorMs = new DateTimeOffset(todayUtcDate.AddDays(-targetDays), TimeSpan.Zero).ToUnixTimeMilliseconds();
        long endMs = new DateTimeOffset(todayUtcDate, TimeSpan.Zero).ToUnixTimeMilliseconds() - 1;

        try
        {
            var dailyRates = new Dictionary<DateOnly, List<decimal>>();
            const int pageLimit = 1000;
            const int maxPages = 12;

            for (int page = 0; page < maxPages && cursorMs <= endMs; page++)
            {
                var url = $"https://fapi.binance.com/fapi/v1/fundingRate?symbol={symbol}&startTime={cursorMs}&endTime={endMs}&limit={pageLimit}";
                using var response = await _httpClient.GetAsync(url, ct);

                if ((int)response.StatusCode == 451 || !response.IsSuccessStatusCode)
                {
                    if (dailyRates.Count == 0)
                    {
                        // Fallback to Deribit public perpetual funding rates (not geo-blocked in US)
                        return await GetDeribitFundingRatesAsync(targetDays, ct);
                    }
                    break;
                }

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var jsonDoc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                int count = 0;
                long maxFundingTimeMs = cursorMs;

                foreach (var item in jsonDoc.RootElement.EnumerateArray())
                {
                    count++;
                    var fundingTimeMs = item.GetProperty("fundingTime").GetInt64();
                    if (fundingTimeMs > maxFundingTimeMs)
                        maxFundingTimeMs = fundingTimeMs;

                    var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(fundingTimeMs).UtcDateTime.Date);
                    if (date >= todayOnly)
                        continue;

                    var rate = decimal.Parse(item.GetProperty("fundingRate").GetString()!, CultureInfo.InvariantCulture);

                    if (!dailyRates.TryGetValue(date, out var list))
                    {
                        list = new List<decimal>(3);
                        dailyRates[date] = list;
                    }
                    list.Add(rate);
                }

                if (count < pageLimit || maxFundingTimeMs <= cursorMs)
                    break;

                cursorMs = maxFundingTimeMs + 1;
            }

            // 24h funding rate = sum of the 8h periods in that closed UTC day
            return dailyRates.ToDictionary(k => k.Key, v => v.Value.Sum());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await GetDeribitFundingRatesAsync(targetDays, ct);
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
            int targetDays = Math.Clamp(days, 1, 2000);
            var todayUtcDate = DateTimeOffset.UtcNow.UtcDateTime.Date;
            var todayOnly = DateOnly.FromDateTime(todayUtcDate);
            var dailyRates = new Dictionary<DateOnly, List<decimal>>();

            // Deribit returns hourly entries (~720 rows per 30 days); paginate in 30-day windows.
            for (var chunkStart = todayUtcDate.AddDays(-targetDays); chunkStart < todayUtcDate; chunkStart = chunkStart.AddDays(30))
            {
                var chunkEnd = chunkStart.AddDays(30);
                if (chunkEnd > todayUtcDate)
                    chunkEnd = todayUtcDate;

                var startMs = new DateTimeOffset(chunkStart, TimeSpan.Zero).ToUnixTimeMilliseconds();
                var endMs = new DateTimeOffset(chunkEnd, TimeSpan.Zero).ToUnixTimeMilliseconds() - 1;
                var url = $"https://www.deribit.com/api/v2/public/get_funding_rate_history?instrument_name=BTC-PERPETUAL&start_timestamp={startMs}&end_timestamp={endMs}";

                using var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                    break;

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var jsonDoc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                if (!jsonDoc.RootElement.TryGetProperty("result", out var resultElem) || resultElem.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in resultElem.EnumerateArray())
                {
                    var timestampMs = item.GetProperty("timestamp").GetInt64();
                    var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).UtcDateTime.Date);
                    if (date >= todayOnly)
                        continue;

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
            }

            return dailyRates.ToDictionary(k => k.Key, v => v.Value.Count > 0 ? v.Value.Average() * 3m : 0m);
        }
        catch (OperationCanceledException)
        {
            throw;
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
