using CryptoPredictor.Backtest;
using CryptoPredictor.Data;
using CryptoPredictor.Features;
using CryptoPredictor.Prediction;
using Xunit;

namespace CryptoPredictor.Tests;

public class PredictionTests
{
    internal static List<DailyMarketData> History(int count = 240)
    {
        var random = new Random(1234);
        decimal price = 100m;
        var bars = new List<DailyMarketData>();
        for (int i = 0; i < count; i++)
        {
            decimal open = price;
            price *= 1m + (decimal)(random.NextDouble() * 0.08 - 0.04);
            bars.Add(new DailyMarketData
            {
                Date = new DateOnly(2020, 1, 1).AddDays(i), BtcOpen = open, BtcClose = price,
                BtcHigh = Math.Max(open, price) * 1.01m, BtcLow = Math.Min(open, price) * 0.99m,
                BtcVolume = 1000m + (decimal)random.NextDouble() * 1000m
            });
        }
        return bars;
    }
    private readonly FeatureCalculator _calculator = new();
    private readonly TurningPointDetector _detector = new();

    [Fact]
    public void LabelsMeanDirectionRatherThanBarrierOrThreePercentGain()
    {
        var bars = History(80).Select((x, i) => x with { BtcClose = 100m + i * 0.1m, BtcHigh = 120m, BtcLow = 80m }).ToList();
        var samples = _detector.BuildTrainingDataset(bars, _calculator.Calculate(bars));
        Assert.All(samples, s => { Assert.True(s.ForwardReturn > 0m); Assert.True(s.ForwardReturn < .03m); Assert.Equal(s.Date.AddDays(7), s.OutcomeDate); });
        Assert.Equal(14, samples.Count);
    }

    [Fact]
    public void LabelHorizonUsesCalendarDaysAndRejectsGaps()
    {
        var bars = History(120);
        var features = _calculator.Calculate(bars);
        Assert.Equal(60, _detector.BuildTrainingDataset(bars, features, 1).Count);
        Assert.Equal(31, _detector.BuildTrainingDataset(bars, features, 30).Count);
        bars.RemoveAt(75);
        var samples = _detector.BuildTrainingDataset(bars, _calculator.Calculate(bars));
        Assert.DoesNotContain(samples, s => s.Date >= bars[0].Date.AddDays(69));
        Assert.Throws<ArgumentException>(() => _detector.BuildTrainingDataset(bars, features, 7, .03m));
    }

    [Fact]
    public void FeaturesDoNotChangeWhenFutureCandlesAreAppended()
    {
        var bars = History();
        var prefix = _calculator.Calculate(bars.Take(150));
        var all = _calculator.Calculate(bars);
        Assert.Equal(prefix[^1], all[149]);
    }

    [Fact]
    public void NoModelDoesNotPretendToBeFourModelsOrHighConfidence()
    {
        var bars = History();
        var f = _calculator.Calculate(bars)[^1] with { FundingZScore30d = -3m, IsLongLiquiditySwept = true };
        var result = new PredictorService().Predict(f, bars[^1] with { FearGreedIndex = 10 });
        Assert.Equal("NEUTRAL_HOLD", result.Signal);
        Assert.Equal("INSUFFICIENT_DATA", result.PredictionStatus);
        Assert.Null(result.Ensemble);
        Assert.Null(result.Validation);
        Assert.Equal(0m, result.TargetPrice);
        Assert.False(result.ProbabilityIsCalibrated);
    }

    [Fact]
    public void AnUnfinishedUtcDayCannotBeUsedForADailyPrediction()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Throws<ArgumentException>(() => new PredictorService().Predict(new DailyFeatures { Date = date }, new DailyMarketData { Date = date, BtcClose = 100m }));
    }

    [Fact]
    public void ChronologicalValidationPurgesOverlappingLabelsAndMeasuresBaseline()
    {
        var bars = History();
        var samples = _detector.BuildTrainingDataset(bars, _calculator.Calculate(bars));
        var predictor = new PredictorService();
        var metrics = predictor.TrainModel(samples);
        Assert.True(metrics.TrainingOutcomeEnd < metrics.ValidationStart);
        Assert.True(metrics.TrainingEnd < metrics.ValidationStart.AddDays(-7));
        Assert.True(metrics.ValidationSampleCount >= 20);
        Assert.Equal(bars[^1].Date, metrics.DataThroughDate);
        var validation = samples.Where(s => s.Date >= metrics.ValidationStart).ToList();
        Assert.Equal(validation.Count(s => s.ForwardReturn > 0m) / (double)validation.Count, metrics.AlwaysUpAccuracy);
        Assert.InRange(metrics.Accuracy, 0d, 1d);
        Assert.InRange(metrics.BrierScore, 0d, 1d);
        Assert.True(double.IsFinite(metrics.LogLoss));
        Assert.Null(metrics.FastTreeAuc);
        Assert.Null(metrics.FastForestAuc);
    }

    [Fact]
    public void UndefinedAucIsNullAndCannotPassValidation()
    {
        var bars = History();
        var samples = _detector.BuildTrainingDataset(bars, _calculator.Calculate(bars)).ToList();
        int split = (int)(samples.Count * .8);
        for (int i = split; i < samples.Count; i++) samples[i] = samples[i] with { ForwardReturn = .01m };
        var metrics = new PredictorService().TrainModel(samples);
        Assert.Null(metrics.AreaUnderRocCurve);
        Assert.False(metrics.PassesValidation);
    }

    [Fact]
    public void ModelTrainedWithFutureOutcomesCannotPredictPastDates()
    {
        var bars = History();
        var features = _calculator.Calculate(bars);
        var predictor = new PredictorService();
        predictor.TrainModel(_detector.BuildTrainingDataset(bars, features));
        Assert.Throws<InvalidOperationException>(() => predictor.Predict(features[170], bars[170]));
    }

    [Fact]
    public void CorrectedTrainingValuesInvalidateTheModelEvenWhenLastDateDoesNotChange()
    {
        var bars = History();
        var predictor = new PredictorService();
        PredictorService.PrepareForLatest(predictor, bars, _calculator.Calculate(bars), _detector);
        var original = predictor.LastTrainingMetrics!.TrainingDataFingerprint;
        bars[100] = bars[100] with { BtcClose = bars[100].BtcClose * 1.001m };
        PredictorService.PrepareForLatest(predictor, bars, _calculator.Calculate(bars), _detector);
        Assert.NotEqual(original, predictor.LastTrainingMetrics!.TrainingDataFingerprint);
    }

    [Fact]
    public void ExternalFeedValuesDoNotChangeDirectionalEstimate()
    {
        var bars = History();
        var features = _calculator.Calculate(bars);
        var predictor = new PredictorService();
        predictor.TrainModel(_detector.BuildTrainingDataset(bars, features));
        var first = predictor.Predict(features[^1], bars[^1]);
        var changed = predictor.Predict(features[^1] with { FundingZScore30d = -20m, OiZScore30d = 20m, IsLongLiquiditySwept = true }, bars[^1] with { FearGreedIndex = 1, BtcDvol = 200m });
        Assert.Equal(first.BullishProbability, changed.BullishProbability);
        Assert.Equal(first.Signal, changed.Signal);
        Assert.Equal(first.TargetPrice, changed.TargetPrice);
    }

    [Fact]
    public void SignedKaufmanEfficiencyDistinguishesUptrendsFromCrashesWhileUiKerRemainsUnsigned()
    {
        var upBars = Enumerable.Range(0, 25).Select(i => new DailyMarketData
        {
            Date = new DateOnly(2024, 1, 1).AddDays(i),
            BtcOpen = 100m + i * 2m, BtcClose = 102m + i * 2m, BtcHigh = 103m + i * 2m, BtcLow = 99m + i * 2m, BtcVolume = 100m
        }).ToList();
        var downBars = Enumerable.Range(0, 25).Select(i => new DailyMarketData
        {
            Date = new DateOnly(2024, 1, 1).AddDays(i),
            BtcOpen = 200m - i * 2m, BtcClose = 198m - i * 2m, BtcHigh = 201m - i * 2m, BtcLow = 197m - i * 2m, BtcVolume = 100m
        }).ToList();
        var chopBars = Enumerable.Range(0, 25).Select(i =>
        {
            decimal c = i % 2 == 0 ? 100m : 105m;
            return new DailyMarketData
            {
                Date = new DateOnly(2024, 1, 1).AddDays(i),
                BtcOpen = c, BtcClose = c, BtcHigh = c + 1m, BtcLow = c - 1m, BtcVolume = 100m
            };
        }).ToList();

        var upFeat = _calculator.Calculate(upBars)[^1];
        var downFeat = _calculator.Calculate(downBars)[^1];
        var chopFeat = _calculator.Calculate(chopBars)[^1];

        Assert.Equal(1m, upFeat.KaufmanEfficiencyRatio10d);
        Assert.Equal(1m, upFeat.SignedKaufmanEfficiencyRatio10d);
        Assert.Equal(1m, downFeat.KaufmanEfficiencyRatio10d);
        Assert.Equal(-1m, downFeat.SignedKaufmanEfficiencyRatio10d);
        Assert.Equal(0m, chopFeat.KaufmanEfficiencyRatio10d);
        Assert.Equal(0m, chopFeat.SignedKaufmanEfficiencyRatio10d);
    }

    [Fact]
    public void SparseFeedStartDoesNotSpikeZScoreAndOiIsExcludedFromModel()
    {
        var rng = new Random(99);
        var bars = History(260).Select((b, i) => b with
        {
            // Simulate OI starting abruptly on day 200 (like a 30-day API cap) and populated Coinbase/Funding/F&G/DXY
            FuturesOpenInterestUsd = i >= 200 ? 10_000_000_000m + i * 5_000_000m : 0m,
            CoinbasePremium = i >= 50 ? (decimal)(rng.NextDouble() * 80 - 40) : 0m,
            FundingRate = i >= 50 ? (decimal)(rng.NextDouble() * 0.001 - 0.0004) : 0m,
            FearGreedIndex = 20 + (i % 65),
            Dxy = 100m + (decimal)Math.Sin(i * 0.1) * 3m
        }).ToList();

        var features = _calculator.Calculate(bars);

        // On day 200 (first non-zero OI day after 200 zeros), Z-score must stay 0 during warmup rather than spiking to +5.29 sigma.
        Assert.Equal(0m, features[200].OiZScore30d);
        Assert.Equal(0m, features[50].CoinbasePremiumZScore30d);
        Assert.Equal(0m, features[50].FundingZScore30d);
        Assert.NotEqual(0m, features[^1].CoinbasePremiumZScore30d);
        Assert.NotEqual(0m, features[^1].FundingZScore30d);

        var predictor = new PredictorService();
        predictor.TrainModel(_detector.BuildTrainingDataset(bars, features));
        var baseline = predictor.Predict(features[^1], bars[^1]);
        var oiPerturbed = predictor.Predict(features[^1] with { OiZScore30d = 999m }, bars[^1]);
        var flowPerturbed = predictor.Predict(features[^1] with { CoinbasePremiumZScore30d = features[^1].CoinbasePremiumZScore30d + 4m }, bars[^1]);

        Assert.Equal(baseline.BullishProbability, oiPerturbed.BullishProbability);
        Assert.NotEqual(baseline.BullishProbability, flowPerturbed.BullishProbability);
    }

    [Fact]
    public void MissingRecentPriceHistoryForcesAbstention()
    {
        var bars = History();
        var f = _calculator.Calculate(bars);
        var predictor = new PredictorService();
        predictor.TrainModel(_detector.BuildTrainingDataset(bars, f));
        var result = predictor.Predict(f[^1] with { ConsecutivePriceDays = 5 }, bars[^1]);
        Assert.Equal("DATA_UNAVAILABLE", result.PredictionStatus);
        Assert.Equal("NEUTRAL_HOLD", result.Signal);
    }

    [Fact]
    public void AnnualizedVolatilityIsNotAnnualizedTwiceAndBandsStayPositive()
    {
        var result = PredictorService.CalculateProjectionAndTargets(100m, new DateOnly(2020, 1, 1), "BULLISH_OPPORTUNITY", .8m, null, .6m, 0m, 0m);
        Assert.InRange(result.cone[^1].LowerBand, 90m, 100m);
        Assert.InRange(result.cone[^1].UpperBand, 100m, 110m);
        Assert.All(result.cone, p => Assert.Equal(100m, p.ExpectedPrice));
        Assert.Equal(1.5m, result.rrRatio);
        Assert.Equal(1.5m, result.targetPct / Math.Abs(result.invalidationPct));
    }
}
