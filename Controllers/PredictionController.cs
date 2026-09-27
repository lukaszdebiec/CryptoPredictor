using CryptoPredictor.Backtest;
using CryptoPredictor.Data.Ingestion;
using CryptoPredictor.Data.Storage;
using CryptoPredictor.Features;
using CryptoPredictor.Prediction;
using Microsoft.AspNetCore.Mvc;

namespace CryptoPredictor.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PredictionController : ControllerBase
{
    private readonly IPredictorService _predictor;
    private readonly ITurningPointDetector _turningPointDetector;
    private readonly IFeatureCalculator _featureCalculator;
    private readonly IMarketDataStorage _storage;
    private readonly IMarketDataAggregator _aggregator;

    public PredictionController(
        IPredictorService predictor,
        ITurningPointDetector turningPointDetector,
        IFeatureCalculator featureCalculator,
        IMarketDataStorage storage,
        IMarketDataAggregator aggregator)
    {
        _predictor = predictor;
        _turningPointDetector = turningPointDetector;
        _featureCalculator = featureCalculator;
        _storage = storage;
        _aggregator = aggregator;
    }

    /// <summary>
    /// Trains the ML.NET Gradient Boosted Tree (FastTree) model using historical features and forward outcomes.
    /// </summary>
    [HttpPost("train")]
    public async Task<ActionResult<ModelTrainingMetrics>> TrainModel(
        [FromQuery] int days = 365,
        [FromQuery] int lookaheadDays = 7,
        [FromQuery] decimal bullishThresholdPercent = 0.03m,
        CancellationToken ct = default)
    {
        var marketData = _storage.Exists()
            ? await _storage.LoadAsync(ct)
            : await _aggregator.FetchAndAggregateAsync(days, saveToStorage: true, ct);

        if (marketData.Count < 20)
        {
            marketData = await _aggregator.FetchAndAggregateAsync(days, saveToStorage: true, ct);
        }

        var features = _featureCalculator.Calculate(marketData);
        var trainingDataset = _turningPointDetector.BuildTrainingDataset(marketData, features, lookaheadDays, bullishThresholdPercent);

        if (trainingDataset.Count < 10)
        {
            return BadRequest($"Not enough labeled samples ({trainingDataset.Count}). Try increasing days to sync more history.");
        }

        var metrics = _predictor.TrainModel(trainingDataset);
        return Ok(metrics);
    }

    /// <summary>
    /// Evaluates today's real market conditions and returns the model's probabilistic directional signal and regime analysis.
    /// </summary>
    [HttpGet("today")]
    public async Task<ActionResult<MarketPrediction>> GetTodayPrediction(CancellationToken ct = default)
    {
        var marketData = _storage.Exists()
            ? await _storage.LoadAsync(ct)
            : await _aggregator.FetchAndAggregateAsync(180, saveToStorage: true, ct);

        if (marketData.Count == 0)
            return StatusCode(500, "No market data available.");

        var features = _featureCalculator.Calculate(marketData);

        var todayFeatures = features.Last();
        var todayMarket = marketData.Last();

        var prediction = _predictor.Predict(todayFeatures, todayMarket);
        return Ok(prediction);
    }

    /// <summary>
    /// Returns historical turning points (local bottoms and tops) detected across the market dataset.
    /// </summary>
    [HttpGet("turning-points")]
    public async Task<ActionResult<IReadOnlyList<LabeledMarketDay>>> GetHistoricalTurningPoints(
        [FromQuery] int window = 5,
        [FromQuery] decimal minReversalPercent = 0.03m,
        CancellationToken ct = default)
    {
        var marketData = _storage.Exists()
            ? await _storage.LoadAsync(ct)
            : await _aggregator.FetchAndAggregateAsync(180, saveToStorage: true, ct);

        var labeled = _turningPointDetector.DetectTurningPoints(marketData, window, minReversalPercent);
        return Ok(labeled);
    }

    /// <summary>
    /// Evaluates what the model would have predicted on a specific historical date (Point-in-Time Prediction)
    /// and compares it with the actual forward market outcome (3d, 7d, 14d returns).
    /// </summary>
    [HttpGet("historical")]
    public async Task<IActionResult> GetHistoricalDatePrediction(
        [FromQuery] DateOnly date,
        CancellationToken ct = default)
    {
        var marketData = _storage.Exists()
            ? await _storage.LoadAsync(ct)
            : await _aggregator.FetchAndAggregateAsync(365, saveToStorage: true, ct);

        var sortedMarket = marketData.OrderBy(d => d.Date).ToList();
        var targetMarket = sortedMarket.FirstOrDefault(d => d.Date == date);
        if (targetMarket == null)
        {
            return NotFound($"No market data found for date {date}. Available range: {sortedMarket.First().Date} to {sortedMarket.Last().Date}.");
        }

        var features = _featureCalculator.Calculate(sortedMarket);
        var targetFeatures = features.FirstOrDefault(f => f.Date == date);
        if (targetFeatures == null)
        {
            return NotFound($"No features calculated for date {date}.");
        }

        // Ensure model is trained
        if (!_predictor.IsTrained)
        {
            var trainingDataset = _turningPointDetector.BuildTrainingDataset(sortedMarket, features, lookaheadDays: 7);
            if (trainingDataset.Count >= 10)
            {
                _predictor.TrainModel(trainingDataset);
            }
        }

        var prediction = _predictor.Predict(targetFeatures, targetMarket);
        var labels = _turningPointDetector.DetectTurningPoints(sortedMarket);
        var targetLabel = labels.FirstOrDefault(l => l.Date == date);

        return Ok(new
        {
            Date = date,
            BtcClose = targetMarket.BtcClose,
            Prediction = prediction,
            ActualHistoricalOutcome = new
            {
                ForwardReturn3d = targetLabel?.ForwardReturn3d,
                ForwardReturn7d = targetLabel?.ForwardReturn7d,
                ForwardReturn14d = targetLabel?.ForwardReturn14d,
                TurningPoint = targetLabel?.TurningPoint.ToString(),
                WasBullishPredictionAccurate = prediction.Signal == "BULLISH_OPPORTUNITY" ? (targetLabel?.ForwardReturn7d > 0.02m) : (bool?)null,
                WasBearishPredictionAccurate = prediction.Signal == "BEARISH_RISK" ? (targetLabel?.ForwardReturn7d < 0m) : (bool?)null
            }
        });
    }
}
