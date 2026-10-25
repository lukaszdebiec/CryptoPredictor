using CryptoPredictor.Data.Storage;

namespace CryptoPredictor.Data.Ingestion;

public class MarketDataAggregator : IMarketDataAggregator
{
    private readonly IBinanceDataService _binanceDataService;
    private readonly ISentimentDataService _sentimentDataService;
    private readonly ICoinbaseDataService _coinbaseDataService;
    private readonly IDeribitDataService _deribitDataService;
    private readonly IEtfDataService _etfDataService;
    private readonly IMacroDataService _macroDataService;
    private readonly IMarketDataStorage _storage;
    private readonly ILogger<MarketDataAggregator> _logger;

    public MarketDataAggregator(
        IBinanceDataService binanceDataService,
        ISentimentDataService sentimentDataService,
        ICoinbaseDataService coinbaseDataService,
        IDeribitDataService deribitDataService,
        IEtfDataService etfDataService,
        IMacroDataService macroDataService,
        IMarketDataStorage storage,
        ILogger<MarketDataAggregator> logger)
    {
        _binanceDataService = binanceDataService;
        _sentimentDataService = sentimentDataService;
        _coinbaseDataService = coinbaseDataService;
        _deribitDataService = deribitDataService;
        _etfDataService = etfDataService;
        _macroDataService = macroDataService;
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
        var macroTask = _macroDataService.GetMacroHistoryAsync("1y", ct);

        await Task.WhenAll(klinesTask, fundingTask, oiTask, fngTask, coinbaseTask, dvolTask, etfTask, macroTask);

        var klines = await klinesTask;
        var fundingMap = await fundingTask;
        var oiMap = await oiTask;
        var fngMap = await fngTask;
        var coinbaseMap = await coinbaseTask;
        var dvolMap = await dvolTask;
        var etfMap = await etfTask;
        var macro = await macroTask;

        _logger.LogInformation("Fetched {KlinesCount} candles, {FundingCount} funding dates, {OiCount} OI dates, {FngCount} F&G dates, {EtfCount} ETF dates, {DxyCount} DXY dates.",
            klines.Count, fundingMap.Count, oiMap.Count, fngMap.Count, etfMap.Count, macro.Dxy.Count);

        var aggregated = new List<DailyMarketData>();
        var sortedKlines = klines.OrderBy(k => k.Date).ToList();

        // Seed initial forward-fill states from the earliest available prior macro date
        var firstDate = sortedKlines.FirstOrDefault()?.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        decimal lastDxy = macro.Dxy.Where(kvp => kvp.Key <= firstDate).OrderByDescending(kvp => kvp.Key).Select(kvp => kvp.Value).FirstOrDefault();

        decimal lastUs10y = macro.Us10YearYield.Where(kvp => kvp.Key <= firstDate).OrderByDescending(kvp => kvp.Key).Select(kvp => kvp.Value).FirstOrDefault();

        decimal lastSp500 = macro.Sp500.Where(kvp => kvp.Key <= firstDate).OrderByDescending(kvp => kvp.Key).Select(kvp => kvp.Value).FirstOrDefault();

        decimal lastNasdaq = macro.Nasdaq.Where(kvp => kvp.Key <= firstDate).OrderByDescending(kvp => kvp.Key).Select(kvp => kvp.Value).FirstOrDefault();

        foreach (var kline in sortedKlines)
        {
            var date = kline.Date;

            fundingMap.TryGetValue(date, out var fundingRate);
            oiMap.TryGetValue(date, out var openInterest);
            fngMap.TryGetValue(date, out var fearGreed);
            coinbaseMap.TryGetValue(date, out var coinbaseClose);
            dvolMap.TryGetValue(date, out var dvol);
            etfMap.TryGetValue(date, out var etfFlow);

            // Forward-fill TradFi calendar gaps (weekends and market holidays)
            if (macro.Dxy.TryGetValue(date, out var dxyVal) && dxyVal > 0m) lastDxy = dxyVal;
            if (macro.Us10YearYield.TryGetValue(date, out var us10yVal) && us10yVal > 0m) lastUs10y = us10yVal;
            if (macro.Sp500.TryGetValue(date, out var sp500Val) && sp500Val > 0m) lastSp500 = sp500Val;
            if (macro.Nasdaq.TryGetValue(date, out var nasdaqVal) && nasdaqVal > 0m) lastNasdaq = nasdaqVal;

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
                BitcoinEtfNetFlowUsd = etfFlow,
                Dxy = lastDxy,
                Us10YearYield = lastUs10y,
                Sp500Close = lastSp500,
                NasdaqClose = lastNasdaq
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
