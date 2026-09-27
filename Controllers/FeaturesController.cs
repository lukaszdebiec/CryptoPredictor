using CryptoPredictor.Data;
using CryptoPredictor.Features;
using Microsoft.AspNetCore.Mvc;

namespace CryptoPredictor.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FeaturesController : ControllerBase
{
    private readonly IFeatureCalculator _featureCalculator;

    public FeaturesController(IFeatureCalculator featureCalculator)
    {
        _featureCalculator = featureCalculator;
    }

    /// <summary>
    /// Calculates engineered quantitative features from historical market observations.
    /// </summary>
    [HttpPost("calculate")]
    public ActionResult<IReadOnlyList<DailyFeatures>> CalculateFeatures([FromBody] List<DailyMarketData> marketData)
    {
        if (marketData == null || marketData.Count == 0)
        {
            return BadRequest("Market data cannot be null or empty.");
        }

        var features = _featureCalculator.Calculate(marketData);
        return Ok(features);
    }

    /// <summary>
    /// Generates sample historical market data (45 days) and returns calculated features for demonstration in Swagger.
    /// </summary>
    [HttpGet("sample")]
    public ActionResult<IReadOnlyList<DailyFeatures>> GetSampleCalculatedFeatures()
    {
        var startDate = new DateOnly(2024, 1, 1);
        var random = new Random(42);
        var mockData = new List<DailyMarketData>();

        decimal btcPrice = 42000m;
        decimal openInterest = 12_000_000_000m;

        for (int i = 0; i < 45; i++)
        {
            var date = startDate.AddDays(i);
            var dailyChangePercent = (decimal)(random.NextDouble() * 0.08 - 0.038); // -3.8% to +4.2%
            btcPrice = Math.Round(btcPrice * (1m + dailyChangePercent), 2);
            var high = Math.Round(btcPrice * 1.02m, 2);
            var low = Math.Round(btcPrice * 0.98m, 2);
            var volume = (decimal)random.Next(20_000, 60_000) * 1000m;

            openInterest = Math.Round(openInterest * (1m + (decimal)(random.NextDouble() * 0.06 - 0.03)), 2);
            var fundingRate = Math.Round((decimal)(random.NextDouble() * 0.0006 - 0.0002), 6); // -0.02% to +0.04%
            var etfNetFlow = Math.Round((decimal)(random.NextDouble() * 600_000_000 - 200_000_000), 2);
            var dvol = Math.Round((decimal)(random.NextDouble() * 25 + 45), 2); // 45 to 70

            mockData.Add(new DailyMarketData
            {
                Date = date,
                BtcOpen = btcPrice * 0.995m,
                BtcHigh = high,
                BtcLow = low,
                BtcClose = btcPrice,
                BtcVolume = volume,
                FuturesOpenInterestUsd = openInterest,
                FundingRate = fundingRate,
                BitcoinEtfNetFlowUsd = etfNetFlow,
                BtcDvol = dvol,
                TotalCryptoMarketCap = btcPrice * 19_500_000m * 1.8m,
                FearGreedIndex = random.Next(25, 80)
            });
        }

        var features = _featureCalculator.Calculate(mockData);
        return Ok(features);
    }
}
