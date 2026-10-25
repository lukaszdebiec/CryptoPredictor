using System.Globalization;
using System.Text.Json;
using CryptoPredictor.Data.Ingestion;

namespace CryptoPredictor.Data.Intraday;

public interface IIntradayMarketDataService
{
    Task<IReadOnlyList<MarketCandle4h>> Get4hCandlesAsync(int limit = 300, CancellationToken ct = default);
}

public class IntradayMarketDataService : IIntradayMarketDataService
{
    private readonly HttpClient _httpClient;
    private readonly IBinanceDataService _binanceDataService;
    private static (DateTime CachedAt, IReadOnlyList<MarketCandle4h> Data)? _cache;
    private static readonly object _cacheLock = new();

    public IntradayMarketDataService(HttpClient httpClient, IBinanceDataService binanceDataService)
    {
        _httpClient = httpClient;
        _binanceDataService = binanceDataService;
    }

    public async Task<IReadOnlyList<MarketCandle4h>> Get4hCandlesAsync(int limit = 300, CancellationToken ct = default)
    {
        lock (_cacheLock)
        {
            if (_cache.HasValue && (DateTime.UtcNow - _cache.Value.CachedAt).TotalMinutes < 5)
            {
                return _cache.Value.Data;
            }
        }

        // 1. Fetch 4h Klines from Binance
        var klines = await _binanceDataService.GetIntradayKlinesAsync("BTCUSDT", "4h", limit, ct);

        // 2. Fetch 4h Taker Buy/Sell Ratio
        var takerMap = await FetchTakerRatioAsync(limit, ct);

        // 3. Fetch 4h Open Interest
        var oiMap = await FetchOpenInterestAsync(limit, ct);

        // 4. Fetch Funding Rates
        var fundingMap = await FetchFundingRatesAsync(limit, ct);

        // 5. Merge into MarketCandle4h list with forward-fill for in-progress candle
        var list = new List<MarketCandle4h>();
        decimal? lastTakerRatio = null;
        decimal? lastTakerBuyVol = null;
        decimal? lastTakerSellVol = null;
        decimal? lastOiUsd = null;
        decimal? lastFundingRate = null;

        foreach (var k in klines.OrderBy(x => x.OpenTime))
        {
            var tsMs = k.OpenTime.ToUnixTimeMilliseconds();
            
            // Session tagging
            var hourUtc = k.OpenTime.UtcDateTime.Hour;
            var dayOfWeek = k.OpenTime.UtcDateTime.DayOfWeek;
            string session = (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
                ? "Weekend"
                : (hourUtc >= 0 && hourUtc < 8)
                    ? "Asia"
                    : (hourUtc >= 8 && hourUtc < 13)
                        ? "London"
                        : "NewYork";

            if (takerMap.TryGetValue(tsMs, out var takerData))
            {
                lastTakerRatio = takerData.Ratio;
                lastTakerBuyVol = takerData.BuyVol;
                lastTakerSellVol = takerData.SellVol;
            }

            if (oiMap.TryGetValue(tsMs, out var oi))
            {
                lastOiUsd = oi;
            }

            if (fundingMap.TryGetValue(tsMs, out var fr))
            {
                lastFundingRate = fr;
            }

            list.Add(new MarketCandle4h
            {
                Timestamp = k.OpenTime,
                Open = k.Open,
                High = k.High,
                Low = k.Low,
                Close = k.Close,
                Volume = k.Volume,
                TakerBuySellRatio = lastTakerRatio,
                TakerBuyVolume = lastTakerBuyVol,
                TakerSellVolume = lastTakerSellVol,
                OpenInterestUsd = lastOiUsd,
                FundingRate = lastFundingRate,
                Session = session
            });
        }

        var result = list.OrderBy(c => c.Timestamp).ToList();

        lock (_cacheLock)
        {
            _cache = (DateTime.UtcNow, result);
        }

        return result;
    }

    private async Task<Dictionary<long, (decimal Ratio, decimal BuyVol, decimal SellVol)>> FetchTakerRatioAsync(int limit, CancellationToken ct)
    {
        var dict = new Dictionary<long, (decimal, decimal, decimal)>();
        try
        {
            var url = $"https://fapi.binance.com/futures/data/takerlongshortRatio?symbol=BTCUSDT&period=4h&limit={limit}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "Mozilla/5.0");
            using var resp = await _httpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return dict;

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var ts = item.GetProperty("timestamp").GetInt64();
                var ratio = decimal.Parse(item.GetProperty("buySellRatio").GetString()!, CultureInfo.InvariantCulture);
                var buyVol = decimal.Parse(item.GetProperty("buyVol").GetString()!, CultureInfo.InvariantCulture);
                var sellVol = decimal.Parse(item.GetProperty("sellVol").GetString()!, CultureInfo.InvariantCulture);
                dict[ts] = (ratio, buyVol, sellVol);
            }
        }
        catch
        {
            // Fallback gracefully if rate limited
        }
        return dict;
    }

    private async Task<Dictionary<long, decimal>> FetchOpenInterestAsync(int limit, CancellationToken ct)
    {
        var dict = new Dictionary<long, decimal>();
        try
        {
            var url = $"https://fapi.binance.com/futures/data/openInterestHist?symbol=BTCUSDT&period=4h&limit={limit}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "Mozilla/5.0");
            using var resp = await _httpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return dict;

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var ts = item.GetProperty("timestamp").GetInt64();
                var oiUsd = decimal.Parse(item.GetProperty("sumOpenInterestValue").GetString()!, CultureInfo.InvariantCulture);
                dict[ts] = oiUsd;
            }
        }
        catch
        {
            // Fallback gracefully
        }
        return dict;
    }

    private async Task<Dictionary<long, decimal>> FetchFundingRatesAsync(int limit, CancellationToken ct)
    {
        var dict = new Dictionary<long, decimal>();
        try
        {
            var url = $"https://fapi.binance.com/fapi/v1/fundingRate?symbol=BTCUSDT&limit={limit}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "Mozilla/5.0");
            using var resp = await _httpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return dict;

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var ts = item.GetProperty("fundingTime").GetInt64();
                var rate = decimal.Parse(item.GetProperty("fundingRate").GetString()!, CultureInfo.InvariantCulture);
                dict[ts] = rate;
            }
        }
        catch
        {
            // Fallback gracefully
        }
        return dict;
    }
}
