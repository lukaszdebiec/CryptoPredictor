using CryptoPredictor.Backtest;
using CryptoPredictor.Data.Ingestion;
using CryptoPredictor.Data.Storage;
using CryptoPredictor.Features;
using CryptoPredictor.Prediction;

namespace CryptoPredictor.Background;

public class DailyMarketDataWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DailyMarketDataWorker> _logger;

    public DailyMarketDataWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DailyMarketDataWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool enabled = _configuration.GetValue("Worker:Enabled", true);
        if (!enabled)
        {
            _logger.LogInformation("[DailyMarketDataWorker] Background worker is disabled via configuration.");
            return;
        }

        bool runOnStartup = _configuration.GetValue("Worker:RunOnStartup", true);
        string targetTimeStr = _configuration.GetValue("Worker:TargetTimeUtc", "00:05:00")!;
        if (!TimeSpan.TryParse(targetTimeStr, out var targetTimeUtc))
        {
            targetTimeUtc = new TimeSpan(0, 5, 0); // fallback: 00:05:00 UTC
        }

        _logger.LogInformation("[DailyMarketDataWorker] Worker initialized. Daily schedule configured for {TargetTime} UTC.", targetTimeUtc);

        // Optional run on startup after a short warm-up delay
        if (runOnStartup)
        {
            _logger.LogInformation("[DailyMarketDataWorker] Initial startup cycle scheduled in 5 seconds...");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                await ExecuteDailyCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DailyMarketDataWorker] Error during initial startup cycle. Proceeding to scheduled loop.");
            }
        }

        // Main scheduling loop
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = CalculateDelayUntilNextUtc(targetTimeUtc);
            var nextRunUtc = DateTime.UtcNow.Add(delay);

            _logger.LogInformation("[DailyMarketDataWorker] Next daily execution scheduled at {NextRunUtc:yyyy-MM-dd HH:mm:ss} UTC (in {Hours:F1} hours).",
                nextRunUtc, delay.TotalHours);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await ExecuteDailyCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DailyMarketDataWorker] Error executing daily cycle. Will retry in 15 minutes.");
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("[DailyMarketDataWorker] Background worker stopped.");
    }

    public async Task ExecuteDailyCycleAsync(CancellationToken ct)
    {
        _logger.LogInformation("============================================================");
        _logger.LogInformation("[DailyMarketDataWorker] EXECUTING DAILY MARKET UPDATE CYCLE");
        _logger.LogInformation("============================================================");

        using var scope = _scopeFactory.CreateScope();
        var aggregator = scope.ServiceProvider.GetRequiredService<IMarketDataAggregator>();
        var storage = scope.ServiceProvider.GetRequiredService<IMarketDataStorage>();
        var featureCalculator = scope.ServiceProvider.GetRequiredService<IFeatureCalculator>();
        var turningPointDetector = scope.ServiceProvider.GetRequiredService<ITurningPointDetector>();
        var predictor = scope.ServiceProvider.GetRequiredService<IPredictorService>();

        int daysToSync = _configuration.GetValue("Worker:DaysToSync", 60);

        // 1. Fetch & aggregate latest market data
        _logger.LogInformation("[DailyMarketDataWorker] Fetching market data from all sources (Binance, Deribit, Coinbase, Farside ETF, F&G)...");
        var aggregated = await aggregator.FetchAndAggregateAsync(days: daysToSync, saveToStorage: true, ct);
        _logger.LogInformation("[DailyMarketDataWorker] Synchronized and saved {Count} daily records to SQLite.", aggregated.Count);

        // 2. Load complete history from SQLite
        var allData = await storage.LoadAsync(ct);
        if (allData.Count < 10)
        {
            _logger.LogWarning("[DailyMarketDataWorker] Insufficient market data in storage ({Count} records). Aborting cycle.", allData.Count);
            return;
        }

        // 3. Compute quantitative features
        var features = featureCalculator.Calculate(allData);

        // 4. Retrain FastTree ML model with updated history
        var trainingDataset = turningPointDetector.BuildTrainingDataset(allData, features, lookaheadDays: 7, bullishThreshold: 0.03m);
        if (trainingDataset.Count >= 20)
        {
            var metrics = predictor.TrainModel(trainingDataset);
            _logger.LogInformation("[DailyMarketDataWorker] FastTree ML model retrained (Samples: {Count}, AUC: {Auc:F3}, Precision: {Precision:P1}).",
                metrics.TrainingSampleCount, metrics.AreaUnderRocCurve, metrics.PositivePrecision);
        }

        // 5. Predict for latest closed candle
        var latestFeatures = features.Last();
        var latestMarket = allData.Last();
        var prediction = predictor.Predict(latestFeatures, latestMarket);

        // 6. Persist prediction in SQLite
        await storage.SavePredictionAsync(prediction, ct);

        // 7. Output formatted summary
        _logger.LogInformation("------------------------------------------------------------");
        _logger.LogInformation("DATE:        {Date:yyyy-MM-dd}", prediction.Date);
        _logger.LogInformation("BTC CLOSE:   ${Close:N2}", prediction.BtcClose);
        _logger.LogInformation("SIGNAL:      {Signal} (Bullish Probability: {Prob:P1})", prediction.Signal, prediction.BullishProbability);
        _logger.LogInformation("REGIME:      {Regime} [Confidence: {Confidence}]", prediction.MarketRegime, prediction.Confidence);
        _logger.LogInformation("KEY DRIVERS:");
        foreach (var driver in prediction.KeyDrivers)
        {
            _logger.LogInformation("  * {Driver}", driver);
        }
        _logger.LogInformation("============================================================");
    }

    private static TimeSpan CalculateDelayUntilNextUtc(TimeSpan targetTimeUtc)
    {
        var now = DateTime.UtcNow;
        var next = now.Date.Add(targetTimeUtc);
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        return next - now;
    }
}
