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
    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<DailyMarketData>>> GetMarketDataHistory(CancellationToken ct = default)
    {
        if (!_storage.Exists())
        {
            // Auto-fetch if not cached yet
            var freshData = await _aggregator.FetchAndAggregateAsync(180, saveToStorage: true, ct);
            return Ok(freshData);
        }

        var cached = await _storage.LoadAsync(ct);
        return Ok(cached);
    }

    /// <summary>
    /// Fetches/loads real market data and runs it through the quantitative Feature Calculator, returning real calculated features.
    /// </summary>
    [HttpGet("features")]
    public async Task<ActionResult<IReadOnlyList<DailyFeatures>>> GetCalculatedFeatures(CancellationToken ct = default)
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
        return Ok(features);
    }
}
