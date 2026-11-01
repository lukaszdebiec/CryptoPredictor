using CryptoPredictor.Data;
using CryptoPredictor.Prediction.Trajectory;
using Xunit;

namespace CryptoPredictor.Tests;

public class TrajectoryModelTests
{
    [Fact]
    public void ShortHistoryHasNoInventedFlatTrajectory()
    {
        var result = new PriceTrajectoryService().Forecast(PredictionTests.History(), "UserFile", "BTCUSDT");
        Assert.Equal("INSUFFICIENT_DATA", result.Status);
        Assert.Equal(174, result.EligibleSamples);
        Assert.Empty(result.Points);
        Assert.Empty(result.Validation);
    }

    [Fact]
    public void LearnsSevenSeparateHorizonsWithPositiveOrderedQuantiles()
    {
        var bars = PredictionTests.History(800);
        var result = new PriceTrajectoryService().Forecast(bars, "UserFile", "BTCUSDT");
        Assert.Equal(8, result.Points.Count);
        Assert.Equal(7, result.Validation.Count);
        Assert.Equal(bars[^1].BtcClose, result.Points[0].MedianPrice);
        Assert.Contains(result.Points.Skip(1), p => p.MedianPrice != bars[^1].BtcClose);
        Assert.All(result.Points, p =>
        {
            Assert.True(p.LowerPrice > 0m);
            Assert.True(p.LowerPrice <= p.MedianPrice && p.MedianPrice <= p.UpperPrice);
            Assert.Equal(bars[^1].Date.AddDays(p.HorizonDays).ToString("yyyy-MM-dd"), p.Time);
        });
        Assert.True(result.ServingFitOutcomeEnd < result.ServingCalibrationStart);
    }

    [Fact]
    public void FitCalibrationAndTestAreChronologicalAndPurged()
    {
        var bars = PredictionTests.History(800);
        var result = new PriceTrajectoryService().Forecast(bars, "UserFile", "BTCUSDT");
        var prices = bars.ToDictionary(b => b.Date, b => b.BtcClose);
        Assert.All(result.Validation, v =>
        {
            Assert.True(v.FitOutcomeEnd < v.CalibrationStart);
            Assert.True(v.CalibrationOutcomeEnd < v.ValidationStart);
            Assert.True(v.ValidationEnd.AddDays(v.HorizonDays) <= result.AsOfDate);
            Assert.InRange(v.IntervalCoverage, 0d, 1d);
            Assert.True(v.MeanPinballLoss >= 0d);
            var dates = Enumerable.Range(v.ValidationStart.DayNumber, v.ValidationEnd.DayNumber - v.ValidationStart.DayNumber + 1).Select(DateOnly.FromDayNumber);
            double expectedBaseline = dates.Average(d => Math.Abs(Math.Log((double)prices[d.AddDays(v.HorizonDays)] / (double)prices[d])));
            Assert.Equal(expectedBaseline, v.BaselineMaeLogReturn, 10);
        });
    }

    [Fact]
    public void FuturePricesCannotChangeHistoricalForecastOrValidation()
    {
        var bars = PredictionTests.History(830);
        var origin = bars[799].Date;
        var first = new PriceTrajectoryService().Forecast(bars.Take(800).ToList(), "UserFile", "BTCUSDT", origin);
        bars[^1] = bars[^1] with { BtcClose = 1m }; // Deliberately invalid future OHLC must also be ignored.
        var second = new PriceTrajectoryService().Forecast(bars, "UserFile", "BTCUSDT", origin);
        Assert.Equal(first.DataFingerprint, second.DataFingerprint);
        Assert.Equal(first.Points, second.Points);
        Assert.Equal(first.Validation, second.Validation);
    }

    [Fact]
    public void GapsDuplicatesAndInvalidOhlcNeverProduceATrajectory()
    {
        var bars = PredictionTests.History(800);
        var gap = bars.ToList(); gap.RemoveAt(500);
        Assert.Equal("DATA_UNAVAILABLE", new PriceTrajectoryService().Forecast(gap, "UserFile", "BTCUSDT").Status);
        var duplicate = bars.Append(bars[^1]).ToList();
        Assert.Empty(new PriceTrajectoryService().Forecast(duplicate, "UserFile", "BTCUSDT").Points);
        bars[500] = bars[500] with { BtcHigh = 0m };
        Assert.Empty(new PriceTrajectoryService().Forecast(bars, "UserFile", "BTCUSDT").Points);
    }

    [Fact]
    public void AncientGapUsesContiguousSuffixWithoutInterpolation()
    {
        var bars = PredictionTests.History(950);
        var withAncientGap = bars.ToList();
        withAncientGap.RemoveAt(100); // Leaves 849 strictly consecutive candles ending at cutoff.
        var service = new PriceTrajectoryService();
        var fromGapped = service.Forecast(withAncientGap, "UserFile", "BTCUSDT");
        var fromSuffix = service.Forecast(bars.Skip(101).ToList(), "UserFile", "BTCUSDT");
        Assert.Equal(849, fromGapped.Observations);
        Assert.Equal(8, fromGapped.Points.Count);
        Assert.Equal(fromSuffix.DataFingerprint, fromGapped.DataFingerprint);
        Assert.Equal(fromSuffix.Points, fromGapped.Points);
    }

    [Fact]
    public void CorrectionsInvalidateCacheButOptionalFeedChangesAndHistoricalQueriesDoNot()
    {
        var bars = PredictionTests.History(820);
        var model = new PriceTrajectoryService();
        var first = model.Forecast(bars, "UserFile", "BTCUSDT");
        var historical = model.Forecast(bars, "UserFile", "BTCUSDT", bars[800].Date);
        Assert.NotSame(first, historical);
        bars[500] = bars[500] with { FundingRate = 99m, FearGreedIndex = 1, FuturesOpenInterestUsd = 123456m };
        Assert.Same(first, model.Forecast(bars, "UserFile", "BTCUSDT"));
        Assert.Same(historical, model.Forecast(bars, "UserFile", "BTCUSDT", bars[800].Date));
        bars[500] = bars[500] with { BtcClose = bars[500].BtcClose * 1.001m };
        Assert.NotEqual(first.DataFingerprint, model.Forecast(bars, "UserFile", "BTCUSDT").DataFingerprint);
    }

    [Fact]
    public void FlatDataDoesNotFalselyBeatNoChangeBenchmark()
    {
        var bars = PredictionTests.History(800).Select(b => b with { BtcClose = 100m, BtcOpen = 100m, BtcHigh = 101m, BtcLow = 99m, BtcVolume = 100m }).ToList();
        var result = new PriceTrajectoryService().Forecast(bars, "UserFile", "BTCUSDT");
        Assert.Equal("NO_VALIDATION_ADVANTAGE", result.Status);
        Assert.All(result.Validation, v => { Assert.Equal(0d, v.MaeLogReturn); Assert.False(v.HasValidationAdvantage); });
        Assert.All(result.Points, p => Assert.Equal(100m, p.MedianPrice));
    }

    [Fact]
    public void PinballLossPenalizesWrongTailsAsymmetrically()
    {
        Assert.Equal(.9d, PriceTrajectoryService.Pinball(1d, 0d, .9), 10);
        Assert.Equal(.1d, PriceTrajectoryService.Pinball(0d, 1d, .9), 10);
        Assert.Throws<ArgumentException>(() => new PriceTrajectoryService().Forecast([], "UserFile", "BTCUSDT", DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    [Fact]
    public void ConformalQuantileModelHandlesFatTailedShockWithoutCrossingIntervals()
    {
        var bars = PredictionTests.History(820);
        // Inject a severe fat-tailed crash and recovery inside the historical fit window.
        bars[250] = bars[250] with { BtcClose = bars[250].BtcClose * 0.55m, BtcLow = bars[250].BtcClose * 0.50m };
        var result = new PriceTrajectoryService().Forecast(bars, "UserFile", "BTCUSDT");
        Assert.Equal("trajectory-v2.2", result.ModelVersion);
        Assert.Equal(8, result.Points.Count);
        Assert.All(result.Points, p => Assert.True(p.LowerPrice > 0m && p.LowerPrice <= p.MedianPrice && p.MedianPrice <= p.UpperPrice));
    }
}
