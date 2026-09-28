using CryptoPredictor.Data.Storage;

namespace CryptoPredictor.Data.Ingestion;

public class MarketDataAggregator : IMarketDataAggregator
{
    private readonly IBinanceDataService _binanceDataService;
    private readonly ISentimentDataService _sentimentDataService;
    private readonly ICoinbaseDataService _coinbaseDataService;
    private readonly IDeribitDataService _deribitDataService;
    private readonly IEtfDataService _etfDataService;
    private readonly IMarketDataStorage _storage;
    private readonly ILogger<MarketDataAggregator> _logger;

    public MarketDataAggregator(
        IBinanceDataService binanceDataService,
        ISentimentDataService sentimentDataService,
        ICoinbaseDataService coinbaseDataService,
        IDeribitDataService deribitDataService,
        IEtfDataService etfDataService,
        IMarketDataStorage storage,
        ILogger<MarketDataAggregator> logger)
    {
        _binanceDataService = binanceDataService;
        _sentimentDataService = sentimentDataService;
        _coinbaseDataService = coinbaseDataService;
        _deribitDataService = deribitDataService;
        _etfDataService = etfDataService;
        _storage = storage;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DailyMarketData>> FetchAndAggregateAsync(int days = 180, bool saveToStorage = true, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting market data aggregation for {Days} days...", days);

        var endDate = DateTime.UtcNow;
        var startDate = endDate.AddDays(-days - 35); // buffer for rolling indicators

        // Run data fetching concurrently for performance
        var klinesTask = _binanceDataService.GetDailyKlinesAsync("BTCUSDT", limit: Math.Min(days + 35, 1000), ct);
        var fundingTask = _binanceDataService.GetDailyFundingRatesAsync("BTCUSDT", limit: 1000, ct);
        var oiTask = _binanceDataService.GetDailyOpenInterestAsync("BTCUSDT", limit: Math.Min(days + 35, 500), ct);
        var fngTask = _sentimentDataService.GetFearAndGreedHistoryAsync(limit: days + 35, ct);
        var coinbaseTask = _coinbaseDataService.GetDailyClosePricesAsync(ct);
        var dvolTask = _deribitDataService.GetDailyDvolHistoryAsync(startDate, endDate, ct);
        var etfTask = _etfDataService.GetDailyEtfNetFlowsAsync(ct);

        await Task.WhenAll(klinesTask, fundingTask, oiTask, fngTask, coinbaseTask, dvolTask, etfTask);

        var klines = await klinesTask;
        var fundingMap = await fundingTask;
        var oiMap = await oiTask;
        var fngMap = await fngTask;
        var coinbaseMap = await coinbaseTask;
        var dvolMap = await dvolTask;
        var etfMap = await etfTask;

        _logger.LogInformation("Fetched {KlinesCount} candles, {FundingCount} funding dates, {OiCount} OI dates, {FngCount} F&G dates, {EtfCount} ETF flow dates.",
            klines.Count, fundingMap.Count, oiMap.Count, fngMap.Count, etfMap.Count);

        var aggregated = new List<DailyMarketData>();

        foreach (var kline in klines.OrderBy(k => k.Date))
        {
            var date = kline.Date;

            fundingMap.TryGetValue(date, out var fundingRate);
            oiMap.TryGetValue(date, out var openInterest);
            fngMap.TryGetValue(date, out var fearGreed);
            coinbaseMap.TryGetValue(date, out var coinbaseClose);
            dvolMap.TryGetValue(date, out var dvol);
            etfMap.TryGetValue(date, out var etfFlow);

            decimal coinbasePremium = 0m;
            if (coinbaseClose > 0m && kline.Close > 0m)
            {
                coinbasePremium = coinbaseClose - kline.Close;
            }

            aggregated.Add(new DailyMarketData
            {
                Date = date,
                BtcOpen = kline.Open,
                BtcHigh = kline.High,
                BtcLow = kline.Low,
                BtcClose = kline.Close,
                BtcVolume = kline.Volume,
                FuturesOpenInterestUsd = openInterest,
                FundingRate = fundingRate,
                CoinbasePremium = coinbasePremium,
                BtcDvol = dvol,
                FearGreedIndex = fearGreed > 0 ? fearGreed : 50,
                BitcoinEtfNetFlowUsd = etfFlow
            });
        }

        if (saveToStorage && aggregated.Count > 0)
        {
            _logger.LogInformation("Saving {Count} aggregated daily market data records to local storage...", aggregated.Count);
            await _storage.SaveAsync(aggregated, ct);
        }

        return aggregated;
    }
}
