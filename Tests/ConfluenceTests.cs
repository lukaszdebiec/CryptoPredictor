using CryptoPredictor.Data;
using CryptoPredictor.Features;
using CryptoPredictor.Prediction.Confluence;
using Xunit;

namespace CryptoPredictor.Tests;

public class ConfluenceTests
{
    private readonly FeatureCalculator _calculator = new();
    private readonly ConfluenceIndicatorService _confluence = new();

    private static List<DailyMarketData> BuildSyntheticMarket(
        DateOnly startDate,
        int days,
        Func<int, DateOnly, DailyMarketData>? customize = null)
    {
        var list = new List<DailyMarketData>(days);
        decimal price = 50_000m;
        for (int i = 0; i < days; i++)
        {
            var d = startDate.AddDays(i);
            decimal step = 1m + 0.002m * (decimal)Math.Sin(i * 0.25);
            decimal open = price;
            decimal close = Math.Round(price * step, 2);
            price = close;

            var row = new DailyMarketData
            {
                Date = d,
                BtcOpen = open,
                BtcHigh = Math.Max(open, close) * 1.012m,
                BtcLow = Math.Min(open, close) * 0.988m,
                BtcClose = close,
                BtcVolume = 25_000m + (i % 7) * 1_000m,
                BtcDvol = 52m
            };

            list.Add(customize != null ? customize(i, d) with
            {
                Date = d,
                BtcOpen = customize(i, d).BtcOpen > 0 ? customize(i, d).BtcOpen : row.BtcOpen,
                BtcHigh = customize(i, d).BtcHigh > 0 ? customize(i, d).BtcHigh : row.BtcHigh,
                BtcLow = customize(i, d).BtcLow > 0 ? customize(i, d).BtcLow : row.BtcLow,
                BtcClose = customize(i, d).BtcClose > 0 ? customize(i, d).BtcClose : row.BtcClose,
                BtcVolume = customize(i, d).BtcVolume > 0 ? customize(i, d).BtcVolume : row.BtcVolume
            } : row);
        }
        return list;
    }

    [Fact]
    public void BullishConfluenceOutscoresBearishConfluenceAndTiltsProjectionUpward()
    {
        var start = new DateOnly(2026, 3, 1);
        int n = 95;

        var bullBars = new List<DailyMarketData>(n);
        var bearBars = new List<DailyMarketData>(n);
        decimal bullPrice = 60_000m;
        decimal bearPrice = 60_000m;

        for (int i = 0; i < n; i++)
        {
            var d = start.AddDays(i);
            bool isTail = i >= n - 5;

            // Bullish setup: uptrend with healthy Golden Pocket pullback + high Coinbase premium + ETF inflows + negative funding + Extreme Fear + falling DXY
            bullPrice = i < n - 6 ? bullPrice * 1.005m : bullPrice * 0.992m;
            bullBars.Add(new DailyMarketData
            {
                Date = d,
                BtcOpen = bullPrice * 1.001m,
                BtcHigh = bullPrice * 1.015m,
                BtcLow = bullPrice * 0.985m,
                BtcClose = Math.Round(bullPrice, 2),
                BtcVolume = 22_000m,
                CoinbasePremium = isTail ? 65m : 5m + (i % 5),
                BitcoinEtfNetFlowUsd = isTail ? 450_000_000m : 20_000_000m * ((i % 3) - 1),
                FundingRate = isTail ? -0.00045m : 0.00010m + 0.00001m * (i % 4),
                FearGreedIndex = isTail ? 14 : 50,
                Dxy = isTail ? 98.2m : 103.5m + 0.1m * (i % 5),
                Us10YearYield = isTail ? 3.85m : 4.25m,
                BtcDvol = 50m
            });

            // Bearish setup: negative Coinbase premium + ETF outflows + crowded positive funding + Extreme Greed + surging DXY & yields
            bearPrice = i < n - 6 ? bearPrice * 0.996m : bearPrice * 1.008m;
            bearBars.Add(new DailyMarketData
            {
                Date = d,
                BtcOpen = bearPrice * 0.999m,
                BtcHigh = bearPrice * 1.015m,
                BtcLow = bearPrice * 0.985m,
                BtcClose = Math.Round(bearPrice, 2),
                BtcVolume = isTail ? 55_000m : 22_000m,
                CoinbasePremium = isTail ? -70m : -2m + (i % 5),
                BitcoinEtfNetFlowUsd = isTail ? -480_000_000m : 20_000_000m * ((i % 3) - 1),
                FundingRate = isTail ? 0.00075m : 0.00008m + 0.00001m * (i % 4),
                FearGreedIndex = isTail ? 91 : 50,
                Dxy = isTail ? 107.8m : 102.0m + 0.1m * (i % 5),
                Us10YearYield = isTail ? 4.70m : 4.15m,
                BtcDvol = 50m
            });
        }

        var asOf = bullBars[^1].Date;
        var bullForecast = _confluence.Evaluate(bullBars, _calculator.Calculate(bullBars), 7, asOf);
        var bearForecast = _confluence.Evaluate(bearBars, _calculator.Calculate(bearBars), 7, asOf);

        Assert.Equal("READY", bullForecast.Status);
        Assert.Equal("READY", bearForecast.Status);
        Assert.Equal("BULLISH", bullForecast.Bias);
        Assert.Equal("BEARISH", bearForecast.Bias);
        Assert.True(bullForecast.ConfluenceScore >= 35.0, $"Expected strong bullish score, got {bullForecast.ConfluenceScore}");
        Assert.True(bearForecast.ConfluenceScore <= -35.0, $"Expected strong bearish score, got {bearForecast.ConfluenceScore}");
        Assert.True(bullForecast.ProjectedEndPrice > bullForecast.ReferenceClose);
        Assert.True(bearForecast.ProjectedEndPrice < bearForecast.ReferenceClose);
    }

    [Fact]
    public void MissingExternalFeedsRedistributeWeightsCleanlyWithoutBreakingBounds()
    {
        var bars = BuildSyntheticMarket(new DateOnly(2026, 1, 1), 90);
        var feats = _calculator.Calculate(bars);
        var asOf = bars[^1].Date;

        var forecast = _confluence.Evaluate(bars, feats, 7, asOf);
        Assert.Equal("READY", forecast.Status);
        Assert.InRange(forecast.ConfluenceScore, -100.0, 100.0);

        var flows = Assert.Single(forecast.Pillars, p => p.Key == "flows");
        var macro = Assert.Single(forecast.Pillars, p => p.Key == "macro");
        Assert.False(flows.IsAvailable);
        Assert.False(macro.IsAvailable);
        Assert.Equal(0d, flows.EffectiveWeight);
        Assert.Equal(0d, macro.EffectiveWeight);

        double weightSum = forecast.Pillars.Sum(p => p.EffectiveWeight);
        Assert.InRange(weightSum, 0.999, 1.001);
    }

    [Fact]
    public void ProjectionBandsRemainStrictlyOrderedAcross7dAnd30d()
    {
        var bars = BuildSyntheticMarket(new DateOnly(2026, 1, 1), 90);
        var feats = _calculator.Calculate(bars);
        var asOf = bars[^1].Date;

        foreach (int horizon in new[] { 7, 30 })
        {
            var res = _confluence.Evaluate(bars, feats, horizon, asOf);
            Assert.Equal("READY", res.Status);
            Assert.Equal(horizon + 1, res.Points.Count);
            Assert.Equal(res.ReferenceClose, res.Points[0].ProjectedPrice);

            for (int i = 0; i <= horizon; i++)
            {
                var pt = res.Points[i];
                Assert.Equal(i, pt.HorizonDays);
                Assert.Equal(asOf.AddDays(i).ToString("yyyy-MM-dd"), pt.Time);
                Assert.True(pt.LowerBandPrice > 0m);
                Assert.True(pt.LowerBandPrice <= pt.ProjectedPrice);
                Assert.True(pt.ProjectedPrice <= pt.UpperBandPrice);
            }
        }
    }

    [Fact]
    public void CycleAnalogMatchesMidtermPhaseAndPreventsFutureLookahead()
    {
        var start = new DateOnly(2020, 1, 1);
        var end = new DateOnly(2026, 9, 30);
        int totalDays = end.DayNumber - start.DayNumber + 1;
        var fullHistory = BuildSyntheticMarket(start, totalDays);

        var asOf = new DateOnly(2026, 6, 15); // 2026 % 4 == 2 (Midterm year)
        var recentSlice = fullHistory.Where(b => b.Date >= new DateOnly(2026, 1, 1) && b.Date <= asOf).ToList();
        var feats = _calculator.Calculate(recentSlice);

        var baseline = _confluence.Evaluate(recentSlice, feats, 30, asOf, fullHistory);
        Assert.NotNull(baseline.CycleAnalog);
        Assert.True(baseline.CycleAnalog!.Available);
        Assert.Equal([2020, 2021, 2022, 2023, 2024, 2025], baseline.CycleAnalog.AnalogYears);
        Assert.Equal([2022], baseline.CycleAnalog.MatchedCycleYears);
        Assert.Equal(31, baseline.CycleAnalog.Points.Count);

        // Mutate future candles strictly after asOf and verify zero lookahead leakage
        var mutatedFuture = fullHistory.Select(b => b.Date > asOf ? b with { BtcClose = b.BtcClose * 3m, BtcHigh = b.BtcHigh * 3m } : b).ToList();
        var afterMutation = _confluence.Evaluate(recentSlice, feats, 30, asOf, mutatedFuture);

        Assert.Equal(baseline.ConfluenceScore, afterMutation.ConfluenceScore);
        Assert.Equal(baseline.CycleAnalog.ExpectedReturnPercent, afterMutation.CycleAnalog!.ExpectedReturnPercent);
        Assert.Equal(
            baseline.CycleAnalog.Points.Select(p => p.AnalogPrice),
            afterMutation.CycleAnalog.Points.Select(p => p.AnalogPrice));
    }

    [Fact]
    public void RejectsOpenUtcDayOriginAndInsufficientWarmup()
    {
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        var shortBars = BuildSyntheticMarket(todayUtc.AddDays(-35), 30);
        var shortFeats = _calculator.Calculate(shortBars);

        var insufficient = _confluence.Evaluate(shortBars, shortFeats, 7, shortBars[^1].Date);
        Assert.Equal("INSUFFICIENT_DATA", insufficient.Status);
        Assert.Empty(insufficient.Points);

        Assert.Throws<ArgumentException>(() => _confluence.Evaluate(shortBars, shortFeats, 7, todayUtc));
    }
}

