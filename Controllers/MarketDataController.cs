using CryptoPredictor.Backtest;
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
    private readonly ITurningPointDetector _turningPointDetector;

    public MarketDataController(
        IMarketDataAggregator aggregator,
        IMarketDataStorage storage,
        IFeatureCalculator featureCalculator,
        ITurningPointDetector turningPointDetector)
    {
        _aggregator = aggregator;
        _storage = storage;
        _featureCalculator = featureCalculator;
        _turningPointDetector = turningPointDetector;
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
            data = days.HasValue && days.Value > 0
                ? await _storage.LoadRecentAsync(days.Value, ct)
                : await _storage.LoadAsync(ct);
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

    /// <summary>
    /// Returns structured data formatted specifically for lightweight financial candlestick charts,
    /// complete with OHLC candles, algorithmic turning point markers (local bottoms and tops), and indicators.
    /// </summary>
    /// <param name="days">Number of recent days to display (default: 180, 0 or null for all).</param>
    [HttpGet("chart")]
    public async Task<ActionResult<MarketChartResponse>> GetChartData([FromQuery] int? days = 180, CancellationToken ct = default)
    {
        IReadOnlyList<DailyMarketData> allData = _storage.Exists()
            ? await _storage.LoadAsync(ct)
            : await _aggregator.FetchAndAggregateAsync(days ?? 180, saveToStorage: true, ct);

        if (allData.Count == 0)
            return NotFound("No market data available.");

        var sortedData = allData.OrderBy(d => d.Date).ToList();

        // Calculate turning points and features across full history for accuracy
        var labeledDays = _turningPointDetector.DetectTurningPoints(sortedData, window: 5, minReversalPercent: 0.03m);
        var turningPointMap = labeledDays.ToDictionary(l => l.Date, l => l.TurningPoint);

        var features = _featureCalculator.Calculate(sortedData);
        var featureMap = features.ToDictionary(f => f.Date);

        // Filter by requested days if specified
        var dataSubset = days.HasValue && days.Value > 0
            ? sortedData.TakeLast(days.Value).ToList()
            : sortedData;

        var candles = new List<CandleDataPoint>(dataSubset.Count);
        var markers = new List<ChartMarkerPoint>();
        var indicators = new List<IndicatorDataPoint>(dataSubset.Count);

        foreach (var day in dataSubset)
        {
            var dateStr = day.Date.ToString("yyyy-MM-dd");

            candles.Add(new CandleDataPoint
            {
                Time = dateStr,
                Open = day.BtcOpen,
                High = day.BtcHigh,
                Low = day.BtcLow,
                Close = day.BtcClose,
                Volume = day.BtcVolume
            });

            // Check if this date has a detected turning point
            if (turningPointMap.TryGetValue(day.Date, out var tpType))
            {
                if (tpType == TurningPointType.LocalBottom)
                {
                    markers.Add(new ChartMarkerPoint
                    {
                        Time = dateStr,
                        Position = "belowBar",
                        Color = "#10b981", // Emerald green
                        Shape = "arrowUp",
                        Text = "Local Bottom"
                    });
                }
                else if (tpType == TurningPointType.LocalTop)
                {
                    markers.Add(new ChartMarkerPoint
                    {
                        Time = dateStr,
                        Position = "aboveBar",
                        Color = "#f43f5e", // Rose red
                        Shape = "arrowDown",
                        Text = "Local Top"
                    });
                }
            }

            // Indicator values
            featureMap.TryGetValue(day.Date, out var feat);
            indicators.Add(new IndicatorDataPoint
            {
                Time = dateStr,
                FundingZScore30d = feat?.FundingZScore30d,
                EtfFlow7dUsd = feat?.EtfFlow7d,
                EtfFlowDailyUsd = day.BitcoinEtfNetFlowUsd,
                EtfFlowZScore30d = feat?.EtfFlowZScore30d,
                OiZScore30d = feat?.OiZScore30d,
                DvolZScore30d = feat?.DvolZScore30d,
                FearGreedIndex = day.FearGreedIndex,
                FibRetracementRatio30d = feat?.FibRetracementRatio30d,
                DistToGoldenPocket30d = feat?.DistToGoldenPocket30d,
                KaufmanEfficiencyRatio10d = feat?.KaufmanEfficiencyRatio10d,
                WeeklyTrendBias = feat?.WeeklyTrendBias,
                Dxy = day.Dxy > 0m ? day.Dxy : null,
                DxyZScore30d = feat?.DxyZScore30d,
                Us10YearYield = day.Us10YearYield > 0m ? day.Us10YearYield : null,
                Sp500Close = day.Sp500Close > 0m ? day.Sp500Close : null,
                Sp500Return7d = feat?.Sp500Return7d
            });
        }

        var latest = dataSubset.Last();
        var prev = dataSubset.Count > 1 ? dataSubset[^2] : latest;
        var changePercent = prev.BtcClose > 0 ? ((latest.BtcClose - prev.BtcClose) / prev.BtcClose) * 100m : 0m;

        var summary = new ChartSummary
        {
            LatestDate = latest.Date.ToString("yyyy-MM-dd"),
            LatestClose = latest.BtcClose,
            PriceChange24hPercent = Math.Round(changePercent, 2),
            HighPeriod = dataSubset.Max(d => d.BtcHigh),
            LowPeriod = dataSubset.Min(d => d.BtcLow),
            TotalDays = dataSubset.Count
        };

        // Compute active 30-day swing Fibonacci & Wave structure
        FibLevelsSummary? fibSummary = null;
        var swing30Days = sortedData.TakeLast(30).ToList();
        if (swing30Days.Count > 0)
        {
            var swingHigh = swing30Days.Max(d => d.BtcHigh);
            var swingLow = swing30Days.Min(d => d.BtcLow);
            var range = swingHigh - swingLow;

            if (range > 0)
            {
                featureMap.TryGetValue(latest.Date, out var latestFeat);
                var ker = latestFeat?.KaufmanEfficiencyRatio10d ?? 0m;
                var currentRatio = (swingHigh - latest.BtcClose) / range;
                var inGoldenPocket = currentRatio >= 0.60m && currentRatio <= 0.66m;
                var waveRegime = ker > 0.55m ? "Impulse Wave (Wave 3/5 Momentum)" : (ker < 0.25m ? "Corrective Structure (Wave 4 / ABC Chop)" : "Consolidation / Transitory");

                fibSummary = new FibLevelsSummary
                {
                    SwingHigh = swingHigh,
                    SwingLow = swingLow,
                    Level0 = swingHigh,
                    Level236 = Math.Round(swingHigh - (0.236m * range), 2),
                    Level382 = Math.Round(swingHigh - (0.382m * range), 2),
                    Level500 = Math.Round(swingHigh - (0.500m * range), 2),
                    Level618 = Math.Round(swingHigh - (0.618m * range), 2),
                    Level650 = Math.Round(swingHigh - (0.650m * range), 2),
                    Level786 = Math.Round(swingHigh - (0.786m * range), 2),
                    Level1000 = swingLow,
                    CurrentRatio = Math.Round(currentRatio, 4),
                    InGoldenPocket = inGoldenPocket,
                    KaufmanEfficiencyRatio = Math.Round(ker, 4),
                    WaveRegime = waveRegime
                };
            }
        }

        return Ok(new MarketChartResponse
        {
            Candles = candles,
            Markers = markers,
            Indicators = indicators,
            Summary = summary,
            Fibonacci = fibSummary
        });
    }
}

