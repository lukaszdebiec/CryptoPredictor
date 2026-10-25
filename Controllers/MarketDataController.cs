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
public class MarketDataController : ControllerBase
{
    private readonly IMarketDataAggregator _aggregator;
    private readonly IBinanceDataService _binanceData;
    private readonly IMarketDataStorage _storage;
    private readonly IFeatureCalculator _featureCalculator;
    private readonly ITurningPointDetector _turningPointDetector;
    private readonly ILiquidityEngine _liquidityEngine;
    private readonly IPredictorService _predictor;

    private static IReadOnlyList<BinanceIntradayKline>? _cached4hKlines;
    private static DateTimeOffset _cache4hExpiry = DateTimeOffset.MinValue;
    private static readonly object _cache4hLock = new();

    public MarketDataController(
        IMarketDataAggregator aggregator,
        IBinanceDataService binanceData,
        IMarketDataStorage storage,
        IFeatureCalculator featureCalculator,
        ITurningPointDetector turningPointDetector,
        ILiquidityEngine liquidityEngine,
        IPredictorService predictor)
    {
        _aggregator = aggregator;
        _binanceData = binanceData;
        _storage = storage;
        _featureCalculator = featureCalculator;
        _turningPointDetector = turningPointDetector;
        _liquidityEngine = liquidityEngine;
        _predictor = predictor;
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
    /// Supports daily ("1d") and intraday 4-hour ("4h") multi-timeframe resolution.
    /// </summary>
    /// <param name="days">Number of recent days to display (default: 180, 0 or null for all).</param>
    /// <param name="interval">Timeframe resolution: "1d" (default) or "4h".</param>
    /// <param name="timeframe">Alias for interval.</param>
    [HttpGet("chart")]
    public async Task<ActionResult<MarketChartResponse>> GetChartData(
        [FromQuery] int? days = 180,
        [FromQuery] string interval = "1d",
        [FromQuery] string? timeframe = null,
        CancellationToken ct = default)
    {
        var tf = (timeframe ?? interval).Trim().ToLowerInvariant();
        if (tf == "4h")
        {
            return await Get4hChartData(days, ct);
        }

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
                Sp500Return7d = feat?.Sp500Return7d,
                DistToLowerLiquidityWall = feat?.DistToLowerLiquidityWall,
                DistToUpperLiquidityWall = feat?.DistToUpperLiquidityWall
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
                var waveRegime = ker > 0.55m ? "Directional movement" : (ker < 0.25m ? "Low directional efficiency" : "Consolidation / Transitory");

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

        // Compute active Liquidity Pools & Liquidation Walls
        var latestLiq = _liquidityEngine.CalculateLiquidityLevels(sortedData, sortedData.Count - 1);
        var liqSummary = new LiquiditySummary
        {
            UpperWall50x = latestLiq.UpperWall50x,
            UpperWall25x = latestLiq.UpperWall25x,
            UpperWall10x = latestLiq.UpperWall10x,
            LowerWall50x = latestLiq.LowerWall50x,
            LowerWall25x = latestLiq.LowerWall25x,
            LowerWall10x = latestLiq.LowerWall10x,
            NearestUpperWall = latestLiq.NearestUpperWall,
            NearestLowerWall = latestLiq.NearestLowerWall,
            DistToUpperPercent = latestLiq.DistToUpperWallPercent,
            DistToLowerPercent = latestLiq.DistToLowerWallPercent,
            RecentLongSweep = latestLiq.IsLongLiquiditySwept,
            RecentShortSweep = latestLiq.IsShortLiquiditySwept,
            DominantSide = latestLiq.DominantLiquiditySide
        };

        // Compute active Prediction Cone & Target/Invalidation levels
        featureMap.TryGetValue(latest.Date, out var latestFeatForPred);
        PredictionSummary? predSummary = null;
        if (latestFeatForPred != null)
        {
            var readyPredictor = PredictorService.PrepareForLatest(_predictor, sortedData, features, _turningPointDetector);
            var pred = readyPredictor.Predict(latestFeatForPred, latest);
            predSummary = new PredictionSummary
            {
                Signal = pred.Signal,
                BullishProbability = pred.BullishProbability,
                MarketRegime = pred.MarketRegime,
                Confidence = pred.Confidence,
                TargetPrice = pred.TargetPrice,
                InvalidationPrice = pred.InvalidationPrice,
                TargetChangePercent = pred.TargetChangePercent,
                InvalidationChangePercent = pred.InvalidationChangePercent,
                RiskRewardRatio = pred.RiskRewardRatio,
                HorizonDays = pred.HorizonDays,
                ProjectionCone = pred.ProjectionCone
            };
        }

        return Ok(new MarketChartResponse
        {
            Candles = candles,
            Markers = markers,
            Indicators = indicators,
            Summary = summary,
            Fibonacci = fibSummary,
            Liquidity = liqSummary,
            Prediction = predSummary
        });
    }

    private async Task<ActionResult<MarketChartResponse>> Get4hChartData(int? days, CancellationToken ct)
    {
        IReadOnlyList<BinanceIntradayKline> allKlines;
        lock (_cache4hLock)
        {
            if (_cached4hKlines != null && DateTimeOffset.UtcNow < _cache4hExpiry)
            {
                allKlines = _cached4hKlines;
            }
            else
            {
                allKlines = null!;
            }
        }

        if (allKlines == null)
        {
            try
            {
                allKlines = await _binanceData.GetIntradayKlinesAsync("BTCUSDT", "4h", 500, ct);
                lock (_cache4hLock)
                {
                    _cached4hKlines = allKlines;
                    _cache4hExpiry = DateTimeOffset.UtcNow.AddMinutes(1);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(502, $"Failed to fetch 4H data from Binance: {ex.Message}");
            }
        }

        if (allKlines.Count == 0)
            return NotFound("No 4H market data available.");

        // Filter by requested days or candle count
        int candleCount = days.HasValue && days.Value > 0 ? Math.Max(30, days.Value * 6) : allKlines.Count;
        var dataSubset = allKlines.TakeLast(candleCount).ToList();

        // Load daily features & market data for background macro/sentiment mapping
        IReadOnlyList<DailyMarketData> dailyData = _storage.Exists()
            ? await _storage.LoadAsync(ct)
            : Array.Empty<DailyMarketData>();

        var dailyMap = dailyData.ToDictionary(d => d.Date);
        var dailyFeatures = dailyData.Count > 0 ? _featureCalculator.Calculate(dailyData) : Array.Empty<DailyFeatures>();
        var featureMap = dailyFeatures.ToDictionary(f => f.Date);

        var candles = new List<CandleDataPoint>(dataSubset.Count);
        var markers = new List<ChartMarkerPoint>();
        var indicators = new List<IndicatorDataPoint>(dataSubset.Count);

        // 1. Detect 4H Turning Points (window = 6 bars, min reversal = 1.5%)
        int n = dataSubset.Count;
        const int tpWindow = 6;
        for (int i = 0; i < n; i++)
        {
            var k = dataSubset[i];
            var timeStr = k.UnixTimeSeconds.ToString();

            candles.Add(new CandleDataPoint
            {
                Time = timeStr,
                Open = k.Open,
                High = k.High,
                Low = k.Low,
                Close = k.Close,
                Volume = k.Volume
            });

            if (i >= tpWindow && i + tpWindow < n)
            {
                bool isMin = true;
                bool isMax = true;
                for (int j = i - tpWindow; j <= i + tpWindow; j++)
                {
                    if (j == i) continue;
                    if (dataSubset[j].Low < k.Low) isMin = false;
                    if (dataSubset[j].High > k.High) isMax = false;
                }

                if (isMin)
                {
                    decimal maxFwd = k.High;
                    for (int j = i + 1; j <= i + tpWindow; j++)
                    {
                        if (dataSubset[j].High > maxFwd) maxFwd = dataSubset[j].High;
                    }
                    if (k.Low > 0 && (maxFwd - k.Low) / k.Low >= 0.015m)
                    {
                        markers.Add(new ChartMarkerPoint
                        {
                            Time = timeStr,
                            Position = "belowBar",
                            Color = "#10b981",
                            Shape = "arrowUp",
                            Text = "4H Bottom"
                        });
                    }
                }
                else if (isMax)
                {
                    decimal minFwd = k.Low;
                    for (int j = i + 1; j <= i + tpWindow; j++)
                    {
                        if (dataSubset[j].Low < minFwd) minFwd = dataSubset[j].Low;
                    }
                    if (k.High > 0 && (k.High - minFwd) / k.High >= 0.015m)
                    {
                        markers.Add(new ChartMarkerPoint
                        {
                            Time = timeStr,
                            Position = "aboveBar",
                            Color = "#f43f5e",
                            Shape = "arrowDown",
                            Text = "4H Top"
                        });
                    }
                }
            }

            // Kaufman Efficiency Ratio for this 4H bar (10 periods = 40 hours)
            decimal barKer = 0m;
            if (i >= 10)
            {
                decimal netChange = Math.Abs(k.Close - dataSubset[i - 10].Close);
                decimal path = 0m;
                for (int p = i - 9; p <= i; p++)
                {
                    path += Math.Abs(dataSubset[p].Close - dataSubset[p - 1].Close);
                }
                barKer = path > 0 ? netChange / path : 0m;
            }

            // Map corresponding daily feature data
            var barDate = DateOnly.FromDateTime(k.OpenTime.UtcDateTime);
            featureMap.TryGetValue(barDate, out var feat);
            dailyMap.TryGetValue(barDate, out var dayMarket);

            // Compute local 4H swing distance for liquidity indicator
            decimal localHigh = dataSubset.Skip(Math.Max(0, i - 20)).Take(Math.Min(21, i + 1)).Max(b => b.High);
            decimal localLow = dataSubset.Skip(Math.Max(0, i - 20)).Take(Math.Min(21, i + 1)).Min(b => b.Low);
            decimal dUpper = localHigh > k.Close && k.Close > 0 ? ((localHigh * 1.018m - k.Close) / k.Close) * 100m : 0m;
            decimal dLower = k.Close > localLow && k.Close > 0 ? ((k.Close - localLow * 0.982m) / k.Close) * 100m : 0m;

            indicators.Add(new IndicatorDataPoint
            {
                Time = timeStr,
                FundingZScore30d = feat?.FundingZScore30d,
                EtfFlow7dUsd = feat?.EtfFlow7d,
                EtfFlowDailyUsd = dayMarket?.BitcoinEtfNetFlowUsd,
                EtfFlowZScore30d = feat?.EtfFlowZScore30d,
                OiZScore30d = feat?.OiZScore30d,
                DvolZScore30d = feat?.DvolZScore30d,
                FearGreedIndex = dayMarket?.FearGreedIndex,
                FibRetracementRatio30d = feat?.FibRetracementRatio30d,
                DistToGoldenPocket30d = feat?.DistToGoldenPocket30d,
                KaufmanEfficiencyRatio10d = Math.Round(barKer, 4),
                WeeklyTrendBias = feat?.WeeklyTrendBias,
                Dxy = dayMarket?.Dxy > 0 ? dayMarket.Dxy : null,
                DxyZScore30d = feat?.DxyZScore30d,
                Us10YearYield = dayMarket?.Us10YearYield > 0 ? dayMarket.Us10YearYield : null,
                Sp500Close = dayMarket?.Sp500Close > 0 ? dayMarket.Sp500Close : null,
                Sp500Return7d = feat?.Sp500Return7d,
                DistToLowerLiquidityWall = Math.Round(dLower, 2),
                DistToUpperLiquidityWall = Math.Round(dUpper, 2)
            });
        }

        var latest = dataSubset.Last();
        var prev24h = dataSubset.Count >= 7 ? dataSubset[^7] : dataSubset[0];
        var priceChange24h = prev24h.Close > 0 ? ((latest.Close - prev24h.Close) / prev24h.Close) * 100m : 0m;

        var summary = new ChartSummary
        {
            LatestDate = latest.OpenTime.ToString("yyyy-MM-dd HH:mm UTC"),
            LatestClose = latest.Close,
            PriceChange24hPercent = Math.Round(priceChange24h, 2),
            HighPeriod = dataSubset.Max(k => k.High),
            LowPeriod = dataSubset.Min(k => k.Low),
            TotalDays = (int)Math.Ceiling(dataSubset.Count / 6.0)
        };

        // 2. 4H Fibonacci Levels & Current Kaufman Wave Ratio
        FibLevelsSummary? fibSummary = null;
        var swingHigh = dataSubset.Max(k => k.High);
        var swingLow = dataSubset.Min(k => k.Low);
        var fibRange = swingHigh - swingLow;

        if (fibRange > 0)
        {
            var currentRatio = (swingHigh - latest.Close) / fibRange;
            var inGoldenPocket = currentRatio >= 0.60m && currentRatio <= 0.66m;

            // Current 4H KER
            decimal latestKer = 0m;
            if (dataSubset.Count >= 10)
            {
                decimal netChange = Math.Abs(latest.Close - dataSubset[^10].Close);
                decimal path = 0m;
                for (int p = dataSubset.Count - 9; p < dataSubset.Count; p++)
                {
                    path += Math.Abs(dataSubset[p].Close - dataSubset[p - 1].Close);
                }
                latestKer = path > 0 ? netChange / path : 0m;
            }

            var waveRegime = latestKer > 0.55m ? "Directional movement" : (latestKer < 0.25m ? "Low directional efficiency" : "Consolidation / Transitory");

            fibSummary = new FibLevelsSummary
            {
                SwingHigh = swingHigh,
                SwingLow = swingLow,
                Level0 = swingHigh,
                Level236 = Math.Round(swingHigh - (0.236m * fibRange), 2),
                Level382 = Math.Round(swingHigh - (0.382m * fibRange), 2),
                Level500 = Math.Round(swingHigh - (0.500m * fibRange), 2),
                Level618 = Math.Round(swingHigh - (0.618m * fibRange), 2),
                Level650 = Math.Round(swingHigh - (0.650m * fibRange), 2),
                Level786 = Math.Round(swingHigh - (0.786m * fibRange), 2),
                Level1000 = swingLow,
                CurrentRatio = Math.Round(currentRatio, 4),
                InGoldenPocket = inGoldenPocket,
                KaufmanEfficiencyRatio = Math.Round(latestKer, 4),
                WaveRegime = waveRegime
            };
        }

        // 3. 4H Liquidity Walls & Sweep Detection (Pivots from recent 4H bars)
        const decimal MMR_BTC = 0.005m;
        int liqLookback = Math.Min(40, dataSubset.Count);
        var liqWindow = dataSubset.TakeLast(liqLookback).ToList();
        decimal pHigh = liqWindow.Max(k => k.High);
        decimal pLow = liqWindow.Min(k => k.Low);

        // Find nearest 4H pivot above and below current price
        for (int k = 2; k < liqWindow.Count - 2; k++)
        {
            var bar = liqWindow[k];
            bool isPivotHigh = bar.High > liqWindow[k - 1].High && bar.High > liqWindow[k - 2].High && bar.High > liqWindow[k + 1].High && bar.High > liqWindow[k + 2].High;
            bool isPivotLow = bar.Low < liqWindow[k - 1].Low && bar.Low < liqWindow[k - 2].Low && bar.Low < liqWindow[k + 1].Low && bar.Low < liqWindow[k + 2].Low;

            if (isPivotHigh && bar.High > latest.Close && bar.High < pHigh) pHigh = bar.High;
            if (isPivotLow && bar.Low < latest.Close && bar.Low > pLow) pLow = bar.Low;
        }

        decimal upperBase = Math.Max(latest.Close, pHigh);
        decimal lowerBase = Math.Min(latest.Close, pLow);

        decimal upper50x = Math.Round(upperBase * (1.0m + (1.0m / 50.0m) - MMR_BTC), 2);
        decimal upper25x = Math.Round(upperBase * (1.0m + (1.0m / 25.0m) - MMR_BTC), 2);
        decimal upper10x = Math.Round(upperBase * (1.0m + (1.0m / 10.0m) - MMR_BTC), 2);

        decimal lower50x = Math.Round(lowerBase * (1.0m - (1.0m / 50.0m) + MMR_BTC), 2);
        decimal lower25x = Math.Round(lowerBase * (1.0m - (1.0m / 25.0m) + MMR_BTC), 2);
        decimal lower10x = Math.Round(lowerBase * (1.0m - (1.0m / 10.0m) + MMR_BTC), 2);

        decimal nearestUpper = latest.Close < upper50x ? upper50x : upper25x;
        decimal nearestLower = latest.Close > lower50x ? lower50x : lower25x;
        decimal distUpperPct = nearestUpper > latest.Close ? Math.Round(((nearestUpper - latest.Close) / latest.Close) * 100m, 2) : 0m;
        decimal distLowerPct = latest.Close > nearestLower ? Math.Round(((latest.Close - nearestLower) / latest.Close) * 100m, 2) : 0m;

        bool longSweep = false;
        bool shortSweep = false;
        if (dataSubset.Count >= 2)
        {
            var prevBar = dataSubset[^2];
            decimal priorLow = dataSubset.Skip(Math.Max(0, dataSubset.Count - 16)).Take(Math.Max(1, dataSubset.Count - 2 - Math.Max(0, dataSubset.Count - 16))).Min(k => k.Low);
            decimal priorHigh = dataSubset.Skip(Math.Max(0, dataSubset.Count - 16)).Take(Math.Max(1, dataSubset.Count - 2 - Math.Max(0, dataSubset.Count - 16))).Max(k => k.High);

            if ((latest.Low < priorLow || latest.Low < lower50x) && latest.Close > priorLow)
                longSweep = true;
            else if (prevBar.Low < lower50x && latest.Close > lower50x && latest.Close > prevBar.Close)
                longSweep = true;

            if ((latest.High > priorHigh || latest.High > upper50x) && latest.Close < priorHigh)
                shortSweep = true;
            else if (prevBar.High > upper50x && latest.Close < upper50x && latest.Close < prevBar.Close)
                shortSweep = true;
        }

        var liqSummary = new LiquiditySummary
        {
            UpperWall50x = upper50x,
            UpperWall25x = upper25x,
            UpperWall10x = upper10x,
            LowerWall50x = lower50x,
            LowerWall25x = lower25x,
            LowerWall10x = lower10x,
            NearestUpperWall = nearestUpper,
            NearestLowerWall = nearestLower,
            DistToUpperPercent = distUpperPct,
            DistToLowerPercent = distLowerPct,
            RecentLongSweep = longSweep,
            RecentShortSweep = shortSweep,
            DominantSide = distUpperPct < distLowerPct ? "Short Liquidation Cluster (Upper Magnet)" : "Long Liquidation Cluster (Lower Magnet)"
        };

        // 4. Prediction Summary & 4H Projected Volatility Cone
        PredictionSummary? predSummary = null;
        var latestDaily = dailyData.LastOrDefault();
        var latestDailyFeat = dailyFeatures.LastOrDefault();
        if (latestDailyFeat != null && latestDaily != null)
        {
            var readyPredictor = PredictorService.PrepareForLatest(_predictor, dailyData, dailyFeatures, _turningPointDetector);
            var pred = readyPredictor.Predict(latestDailyFeat, latestDaily);

            // The direction model remains daily. This intraday overlay only illustrates volatility.
            decimal annualVol = Math.Clamp(latestDailyFeat.RealizedVolatility30d > 0m ? latestDailyFeat.RealizedVolatility30d : 0.5m, 0.01m, 3m);
            decimal sigmaDaily = annualVol / (decimal)Math.Sqrt(365d);

            long latestTimeSec = latest.UnixTimeSeconds;
            var conePoints = new List<PredictionConePoint>(42);

            for (int step = 1; step <= 42; step++)
            {
                decimal tDays = (step * 4.0m) / 24.0m;
                decimal expPrice = latest.Close;
                decimal volBand = sigmaDaily * (decimal)Math.Sqrt((double)tDays);
                decimal upper = Math.Round(expPrice * (decimal)Math.Exp((double)volBand), 2);
                decimal lower = Math.Round(expPrice * (decimal)Math.Exp(-(double)volBand), 2);

                conePoints.Add(new PredictionConePoint
                {
                    Time = (latestTimeSec + (step * 14400)).ToString(),
                    ExpectedPrice = expPrice,
                    UpperBand = upper,
                    LowerBand = lower
                });
            }

            predSummary = new PredictionSummary
            {
                Signal = pred.Signal,
                BullishProbability = pred.BullishProbability,
                MarketRegime = pred.MarketRegime,
                Confidence = pred.Confidence,
                TargetPrice = pred.TargetPrice,
                InvalidationPrice = pred.InvalidationPrice,
                TargetChangePercent = pred.TargetChangePercent,
                InvalidationChangePercent = pred.InvalidationChangePercent,
                RiskRewardRatio = pred.RiskRewardRatio,
                HorizonDays = 7,
                ProjectionCone = conePoints
            };
        }

        return Ok(new MarketChartResponse
        {
            Candles = candles,
            Markers = markers,
            Indicators = indicators,
            Summary = summary,
            Fibonacci = fibSummary,
            Liquidity = liqSummary,
            Prediction = predSummary
        });
    }
}

