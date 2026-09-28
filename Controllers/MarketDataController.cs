using CryptoPredictor.Data;
using CryptoPredictor.Data.Ingestion;
using CryptoPredictor.Data.Storage;
using CryptoPredictor.Features;
using Microsoft.AspNetCore.Mvc;

namespace CryptoPredictor.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MarketDataController : ControllerBase
{
    private readonly IMarketDataAggregator _aggregator;
    private readonly IMarketDataStorage _storage;
    private readonly IFeatureCalculator _featureCalculator;

    public MarketDataController(
        IMarketDataAggregator aggregator,
        IMarketDataStorage storage,
        IFeatureCalculator featureCalculator)
    {
        _aggregator = aggregator;
        _storage = storage;
        _featureCalculator = featureCalculator;
    }

    /// <summary>
    /// Synchronizes real historical data from Binance (Spot + Futures), Alternative.me, Coinbase, and Deribit, saving it to local storage.
    /// </summary>
    /// <param name="days">Number of days to synchronize (default: 180).</param>
    [HttpPost("sync")]
    public async Task<IActionResult> SyncMarketData([FromQuery] int days = 180, CancellationToken ct = default)
    {
        if (days <= 0 || days > 1000)
            return BadRequest("Days must be between 1 and 1000.");

        var data = await _aggregator.FetchAndAggregateAsync(days, saveToStorage: true, ct);
        if (data.Count == 0)
            return StatusCode(500, "No market data could be fetched.");

        var latest = data.Last();
        return Ok(new
        {
            Message = "Market data synchronized and cached successfully.",
            Count = data.Count,
            From = data.First().Date,
            To = latest.Date,
            LatestClose = latest.BtcClose,
            LatestFearAndGreed = latest.FearGreedIndex,
            LatestOpenInterestUsd = latest.FuturesOpenInterestUsd,
            LatestFundingRate = latest.FundingRate,
            LatestDvol = latest.BtcDvol
        });
    }

    /// <summary>
    /// Retrieves cached historical daily market data.
    /// </summary>
    /// <param name="days">Optional number of recent days to return (e.g. 7, 30). Leave empty to get full history.</param>
    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<DailyMarketData>>> GetMarketDataHistory([FromQuery] int? days = null, CancellationToken ct = default)
    {
        IReadOnlyList<DailyMarketData> data;

        if (!_storage.Exists())
        {
            // Auto-fetch if not cached yet
            data = await _aggregator.FetchAndAggregateAsync(180, saveToStorage: true, ct);
        }
        else
        {
            data = await _storage.LoadAsync(ct);
        }

        if (days.HasValue && days.Value > 0)
        {
            return Ok(data.TakeLast(days.Value).ToList());
        }

        return Ok(data);
    }

    /// <summary>
    /// Fetches/loads real market data and runs it through the quantitative Feature Calculator, returning real calculated features.
    /// </summary>
    /// <param name="days">Optional number of recent days to return (e.g. 7, 30). Leave empty to get full history.</param>
    [HttpGet("features")]
    public async Task<ActionResult<IReadOnlyList<DailyFeatures>>> GetCalculatedFeatures([FromQuery] int? days = null, CancellationToken ct = default)
    {
        IReadOnlyList<DailyMarketData> marketData;

        if (!_storage.Exists())
        {
            marketData = await _aggregator.FetchAndAggregateAsync(180, saveToStorage: true, ct);
        }
        else
        {
            marketData = await _storage.LoadAsync(ct);
        }

        var features = _featureCalculator.Calculate(marketData);

        if (days.HasValue && days.Value > 0)
        {
            return Ok(features.TakeLast(days.Value).ToList());
        }

        return Ok(features);
    }
}

