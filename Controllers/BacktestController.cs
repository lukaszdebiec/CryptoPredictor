using CryptoPredictor.Backtest;
using CryptoPredictor.Data.Ingestion;
using CryptoPredictor.Data.Storage;
using CryptoPredictor.Features;
using CryptoPredictor.Prediction;
using Microsoft.AspNetCore.Mvc;

namespace CryptoPredictor.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BacktestController : ControllerBase
{
    private readonly IBacktestEngine _backtestEngine;
    private readonly IPredictorService _predictor;
    private readonly ITurningPointDetector _turningPointDetector;
    private readonly IFeatureCalculator _featureCalculator;
    private readonly IMarketDataStorage _storage;
    private readonly IMarketDataAggregator _aggregator;

    public BacktestController(
        IBacktestEngine backtestEngine,
        IPredictorService predictor,
        ITurningPointDetector turningPointDetector,
        IFeatureCalculator featureCalculator,
        IMarketDataStorage storage,
        IMarketDataAggregator aggregator)
    {
        _backtestEngine = backtestEngine;
        _predictor = predictor;
        _turningPointDetector = turningPointDetector;
        _featureCalculator = featureCalculator;
        _storage = storage;
        _aggregator = aggregator;
    }

    /// <summary>
    /// Executes a full quantitative walk-forward backtest across historical market data.
    /// Simulates buying whenever model probability exceeds the threshold, tracking Win Rate, Sharpe Ratio, and Drawdowns.
    /// </summary>
    [HttpPost("run")]
    public async Task<ActionResult<BacktestReport>> RunBacktest(
        [FromBody] BacktestSettings? settings = null,
        CancellationToken ct = default)
    {
        settings ??= new BacktestSettings();

        var marketData = _storage.Exists()
            ? await _storage.LoadAsync(ct)
            : await _aggregator.FetchAndAggregateAsync(365, saveToStorage: true, ct);

        if (marketData.Count < 20)
            return BadRequest("Insufficient market data for backtesting. Please sync data first.");

        var features = _featureCalculator.Calculate(marketData);

        // Ensure model is trained on history
        if (!_predictor.IsTrained)
        {
            var trainingDataset = _turningPointDetector.BuildTrainingDataset(marketData, features, lookaheadDays: settings.HoldingPeriodDays);
            if (trainingDataset.Count >= 10)
            {
                _predictor.TrainModel(trainingDataset);
            }
        }

        var report = _backtestEngine.RunSimulation(marketData, features, _predictor, settings);
        return Ok(report);
    }
}
