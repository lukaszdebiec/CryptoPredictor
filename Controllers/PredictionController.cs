using CryptoPredictor.Backtest;
using CryptoPredictor.Data;
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

    public PredictionController(IPredictorService predictor, ITurningPointDetector turningPointDetector,
        IFeatureCalculator featureCalculator, IMarketDataStorage storage, IMarketDataAggregator aggregator)
    {
        _predictor = predictor; _turningPointDetector = turningPointDetector;
        _featureCalculator = featureCalculator; _storage = storage; _aggregator = aggregator;
    }

    private async Task<IReadOnlyList<DailyMarketData>> History(int days, CancellationToken ct)
    {
        var data = _storage.Exists() ? await _storage.LoadAsync(ct)
            : await _aggregator.FetchAndAggregateAsync(days, saveToStorage: true, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return data.Where(d => d.Date < today).OrderBy(d => d.Date).ToList();
    }

    [HttpPost("train")]
    public async Task<ActionResult<ModelTrainingMetrics>> TrainModel([FromQuery] int days = 365,
        [FromQuery] int lookaheadDays = 7, [FromQuery] decimal bullishThresholdPercent = 0m, CancellationToken ct = default)
    {
        if (days < 1 || days > 1000 || lookaheadDays != PredictionPolicy.HorizonDays || bullishThresholdPercent != 0m)
            return BadRequest("direction-v1 supports days 1–1000 and the event: 7-day closing return > 0. Other label parameters are unsupported.");
        var data = await History(days, ct);
        var dataset = _turningPointDetector.BuildTrainingDataset(data, _featureCalculator.Calculate(data));
        if (!PredictorService.CanTrain(dataset))
            return BadRequest($"Insufficient eligible history: {dataset.Count} samples after a {PredictionPolicy.WarmupDays}-day warmup. At least {PredictionPolicy.MinimumDatasetSamples} and both chronological training classes are needed.");
        return Ok(_predictor.TrainModel(dataset));
    }

    [HttpGet("today")]
    public async Task<ActionResult<MarketPrediction>> GetTodayPrediction([FromQuery] bool forceRefresh = false, CancellationToken ct = default)
    {
        var data = await History(365, ct);
        if (data.Count == 0) return StatusCode(503, "No closed daily market observations are available.");
        var saved = await _storage.GetLatestPredictionAsync(ct);
        // The first issued forecast for a signal day is immutable, including on forceRefresh.
        if (saved?.Date == data[^1].Date && saved.ModelVersion == PredictionPolicy.Version)
            return Ok(saved);
        var features = _featureCalculator.Calculate(data);
        var ready = PredictorService.PrepareForLatest(_predictor, data, features, _turningPointDetector);
        var prediction = ready.Predict(features[^1], data[^1]);
        var issued = await _storage.SavePredictionAsync(prediction, ct);
        if (issued.ModelVersion != PredictionPolicy.Version)
            return Conflict("A legacy forecast already exists for this day and was preserved. See /api/prediction/history; direction-v1 will issue its first snapshot on the next signal day.");
        return Ok(issued);
    }

    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<MarketPrediction>>> GetPredictionHistory([FromQuery] int limit = 30, CancellationToken ct = default)
        => Ok(await _storage.GetPredictionHistoryAsync(Math.Clamp(limit, 1, 1000), ct));

    [HttpGet("journal")]
    public async Task<ActionResult<PredictionJournalResponse>> GetPredictionJournal([FromQuery] string mode = "historical",
        [FromQuery] int limit = 250, CancellationToken ct = default)
    {
        if (mode == "live") return Ok(await _storage.GetPredictionJournalAsync(Math.Clamp(limit, 1, 1000), ct));
        if (mode != "historical") return BadRequest("Mode must be historical or live.");
        var data = await History(365, ct);
        var journal = _predictor.GenerateWalkForwardJournal(data, _featureCalculator.Calculate(data), _turningPointDetector);
        // Stats and items must describe the same selected rows.
        return Ok(PredictionOutcomeResolver.Journal(journal.Items.Take(Math.Clamp(limit, 1, 1000)), "historical"));
    }

    [HttpGet("turning-points")]
    public async Task<ActionResult<IReadOnlyList<LabeledMarketDay>>> GetHistoricalTurningPoints([FromQuery] int window = 5,
        [FromQuery] decimal minReversalPercent = 0.03m, CancellationToken ct = default)
    {
        if (window < 1 || window > 100 || minReversalPercent <= 0m) return BadRequest("Invalid turning-point parameters.");
        return Ok(_turningPointDetector.DetectTurningPoints(await History(365, ct), window, minReversalPercent));
    }

    [HttpGet("time-machine/range")]
    public async Task<IActionResult> GetTimeMachineRange(CancellationToken ct = default)
    {
        var data = await History(365, ct);
        if (data.Count < PredictionPolicy.WarmupDays + PredictionPolicy.HorizonDays)
            return StatusCode(503, "Insufficient closed daily history.");
        var minDate = data[PredictionPolicy.WarmupDays - 1].Date;
        var maxDate = data[^1].Date.AddDays(-PredictionPolicy.HorizonDays);
        return Ok(new
        {
            MinDate = minDate.ToString("yyyy-MM-dd"), MaxDate = maxDate.ToString("yyyy-MM-dd"),
            DefaultDate = maxDate.ToString("yyyy-MM-dd"), TotalHistoricalDays = data.Count,
            Presets = new[] { new { label = "Latest complete 7-day outcome", date = maxDate.ToString("yyyy-MM-dd"), description = "Earlier dates may have insufficient model training history." } }
        });
    }

    [HttpGet("historical")]
    public async Task<IActionResult> GetHistoricalDatePrediction([FromQuery] DateOnly date,
        [FromQuery] bool strictlyOutOfSample = true, CancellationToken ct = default)
    {
        if (!strictlyOutOfSample) return BadRequest("In-sample historical predictions are not supported. Use strictlyOutOfSample=true.");
        var data = await History(365, ct);
        var history = data.Where(d => d.Date <= date).ToList();
        if (history.Count == 0 || history[^1].Date != date) return NotFound("No closed daily candle exists for that date.");
        var features = _featureCalculator.Calculate(history);
        var dataset = _turningPointDetector.BuildTrainingDataset(history, features);
        using var predictor = new PredictorService();
        if (PredictorService.CanTrain(dataset)) predictor.TrainModel(dataset);
        var prediction = predictor.Predict(features[^1], history[^1]);
        var outcome = PredictionOutcomeResolver.Resolve(prediction, data);
        var forward = data.Where(d => d.Date > date && d.Date <= date.AddDays(7)).ToList();
        object Candle(DailyMarketData d) => new { time = d.Date.ToString("yyyy-MM-dd"), open = d.BtcOpen, high = d.BtcHigh, low = d.BtcLow, close = d.BtcClose, volume = d.BtcVolume };
        decimal? ForwardReturn(int days) => data.FirstOrDefault(d => d.Date == date.AddDays(days)) is { } d ? d.BtcClose / history[^1].BtcClose - 1m : null;
        return Ok(new
        {
            Date = date, BtcClose = history[^1].BtcClose, StrictlyOutOfSample = true,
            TrainingSamplesUsed = predictor.LastTrainingMetrics?.TrainingSampleCount ?? 0,
            HistoricalCandles = history.TakeLast(60).Select(Candle), ForwardCandles = forward.Select(Candle), Prediction = prediction,
            Resolution = new
            {
                outcome.OutcomeStatus, outcome.RealizedReturnPercent, outcome.ResolvedDate, outcome.ResolvedPrice,
                HitDayIndex = outcome.ResolvedDate.HasValue ? forward.FindIndex(d => d.Date == outcome.ResolvedDate) : (int?)null
            },
            ActualHistoricalOutcome = new
            {
                ForwardReturn3d = ForwardReturn(3), ForwardReturn7d = ForwardReturn(7), ForwardReturn14d = ForwardReturn(14),
                WasBullishPredictionAccurate = prediction.Signal == "BULLISH_OPPORTUNITY" ? outcome.DirectionCorrect : null,
                WasBearishPredictionAccurate = prediction.Signal == "BEARISH_RISK" ? outcome.DirectionCorrect : null
            }
        });
    }
}
