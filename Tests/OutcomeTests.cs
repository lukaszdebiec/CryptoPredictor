using CryptoPredictor.Data;
using CryptoPredictor.Prediction;
using Xunit;

namespace CryptoPredictor.Tests;

public class OutcomeTests
{
    internal static readonly DateOnly Day = new(2020, 1, 1);
    internal static MarketPrediction Forecast(string signal = "BULLISH_OPPORTUNITY") => new()
    {
        Date = Day, BtcClose = 100m, Signal = signal, ModelVersion = PredictionPolicy.Version,
        PredictionStatus = "MODEL_ESTIMATE", BullishProbability = signal == "BEARISH_RISK" ? .2m : .8m,
        TargetPrice = signal == "BEARISH_RISK" ? 94m : 106m,
        InvalidationPrice = signal == "BEARISH_RISK" ? 104m : 96m,
        TargetChangePercent = signal == "BEARISH_RISK" ? -.06m : .06m,
        InvalidationChangePercent = signal == "BEARISH_RISK" ? .04m : -.04m,
        RiskRewardRatio = 1.5m, HorizonDays = 7, MarketRegime = "test", Confidence = "Uncalibrated"
    };
    internal static DailyMarketData Bar(int day, decimal high = 101m, decimal low = 99m, decimal close = 100m, decimal open = 100m)
        => new() { Date = Day.AddDays(day), BtcOpen = open, BtcHigh = high, BtcLow = low, BtcClose = close, BtcVolume = 1m };

    [Theory]
    [InlineData("BULLISH_OPPORTUNITY")]
    [InlineData("BEARISH_RISK")]
    public void SameCandleBothBarriersAlwaysResolvesStopFirstWithCosts(string signal)
    {
        var item = PredictionOutcomeResolver.Resolve(Forecast(signal), [Bar(1, 110m, 90m)]);
        Assert.Equal("INVALIDATED", item.OutcomeStatus);
        Assert.Equal("BOTH_BARRIERS_STOP_FIRST", item.ExitReason);
        Assert.Equal(-.042m, item.RealizedReturnPercent);
    }

    [Fact]
    public void AdverseOpeningGapDoesNotFillAtUnreachableStop()
    {
        var item = PredictionOutcomeResolver.Resolve(Forecast(), [Bar(1, 92m, 89m, 91m, 90m)]);
        Assert.Equal(90m, item.ResolvedPrice);
        Assert.Equal(-.102m, item.RealizedReturnPercent);
    }

    [Fact]
    public void PartialHorizonIsPendingAndMissingCandlesAreNotInvented()
    {
        Assert.Equal("PENDING", PredictionOutcomeResolver.Resolve(Forecast(), [Bar(1), Bar(2)]).OutcomeStatus);
        Assert.Equal("DATA_UNAVAILABLE", PredictionOutcomeResolver.Resolve(Forecast(), [Bar(2, 110m)]).OutcomeStatus);
        Assert.Equal("EXPIRED_LOSS", PredictionOutcomeResolver.Resolve(Forecast(), Enumerable.Range(1, 7).Select(i => Bar(i)).ToList()).OutcomeStatus);
    }

    [Fact]
    public void CashHoldIsNeitherAWinNorBtcExposure()
    {
        var item = PredictionOutcomeResolver.Resolve(Forecast("NEUTRAL_HOLD"), [Bar(7, 150m, 99m, 140m)]);
        Assert.Equal("NO_TRADE", item.OutcomeStatus);
        Assert.Equal(0m, item.RealizedReturnPercent);
        var journal = PredictionOutcomeResolver.Journal([item], "historical");
        Assert.Equal(0, journal.Stats.ResolvedPredictions);
        Assert.Equal(1, journal.Stats.NoTradePredictions);
        Assert.Null(journal.Stats.ProfitFactorValue);
    }

    [Fact]
    public void DirectionAccuracyAndTradeProfitAreSeparateMeasurements()
    {
        var bars = Enumerable.Range(1, 7).Select(i => Bar(i, 105m, 95m, 101m)).ToList();
        var item = PredictionOutcomeResolver.Resolve(Forecast(), bars);
        Assert.True(item.DirectionCorrect);
        Assert.Equal("INVALIDATED", item.OutcomeStatus);
        Assert.True(item.RealizedReturnPercent < 0m);
    }
}
