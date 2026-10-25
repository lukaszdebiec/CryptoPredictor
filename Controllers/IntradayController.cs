using Microsoft.AspNetCore.Mvc;
using CryptoPredictor.Data.Intraday;
using CryptoPredictor.Features.Intraday;
using CryptoPredictor.Prediction.Intraday;

namespace CryptoPredictor.Controllers;

[ApiController]
[Route("api/intraday")]
public class IntradayController : ControllerBase
{
    private readonly IIntradayMarketDataService _marketDataService;
    private readonly IIntradayFeatureCalculator _featureCalculator;
    private readonly IIntradayPredictorService _predictorService;

    public IntradayController(
        IIntradayMarketDataService marketDataService,
        IIntradayFeatureCalculator featureCalculator,
        IIntradayPredictorService predictorService)
    {
        _marketDataService = marketDataService;
        _featureCalculator = featureCalculator;
        _predictorService = predictorService;
    }

    /// <summary>
    /// Returns the latest 4-Hour Tactical Prediction (Orderflow, SFP sweeps, session bias).
    /// </summary>
    [HttpGet("prediction")]
    public async Task<ActionResult<IntradayPrediction>> GetLatestPrediction(CancellationToken ct = default)
    {
        var candles = await _marketDataService.Get4hCandlesAsync(limit: 200, ct);
        if (candles == null || candles.Count < 20)
            return NotFound("Insufficient 4H candle data.");

        var features = _featureCalculator.Calculate(candles);
        if (features.Count == 0)
            return NotFound("Unable to calculate 4H features.");

        var latestCandle = candles[^1];
        var latestFeature = features[^1];

        var prediction = _predictorService.Predict(latestFeature, latestCandle);
        return Ok(prediction);
    }

    /// <summary>
    /// Returns recent 4H candles with orderflow and session metrics.
    /// </summary>
    [HttpGet("candles")]
    public async Task<ActionResult<IReadOnlyList<MarketCandle4h>>> GetCandles([FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var candles = await _marketDataService.Get4hCandlesAsync(limit: Math.Clamp(limit, 10, 500), ct);
        return Ok(candles);
    }

    /// <summary>
    /// Returns the historical 4-Hour Tactical Trade Journal (walk-forward simulation across past 4h candles).
    /// </summary>
    [HttpGet("journal")]
    public async Task<ActionResult<IntradayJournalResponse>> GetJournal(CancellationToken ct = default)
    {
        var candles = await _marketDataService.Get4hCandlesAsync(limit: 300, ct);
        if (candles == null || candles.Count < 20)
            return Ok(new IntradayJournalResponse());

        var features = _featureCalculator.Calculate(candles);
        var journal = _predictorService.GenerateIntradayJournal(candles, features);

        return Ok(journal);
    }
}
