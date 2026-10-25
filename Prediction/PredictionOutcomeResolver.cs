using CryptoPredictor.Data;

namespace CryptoPredictor.Prediction;

/// <summary>One execution policy for historical, live and point-in-time journals.</summary>
public static class PredictionOutcomeResolver
{
    public static PredictionJournalItem Resolve(
        MarketPrediction prediction, IReadOnlyList<DailyMarketData> candles,
        decimal feePerSide = PredictionPolicy.FeePerSide,
        decimal slippagePerSide = PredictionPolicy.SlippagePerSide)
    {
        if (feePerSide < 0 || slippagePerSide < 0 || feePerSide + slippagePerSide >= 0.1m)
            throw new ArgumentOutOfRangeException(nameof(feePerSide));
        var date = prediction.Date;
        var end = date.AddDays(prediction.HorizonDays);
        var sorted = candles.Where(c => c.Date > date && c.Date <= end).OrderBy(c => c.Date).ToList();
        var latestDate = candles.Count > 0 ? candles.Max(c => c.Date) : date;
        var elapsed = Math.Clamp(latestDate.DayNumber - date.DayNumber, 0, prediction.HorizonDays);
        var final = sorted.FirstOrDefault(c => c.Date == end);
        decimal? forwardReturn = final != null && prediction.BtcClose > 0m ? final.BtcClose / prediction.BtcClose - 1m : null;
        bool? directionCorrect = forwardReturn.HasValue && prediction.Signal != "NEUTRAL_HOLD"
            ? prediction.Signal == "BULLISH_OPPORTUNITY" ? forwardReturn > 0m : forwardReturn < 0m
            : null;
        var item = new PredictionJournalItem
        {
            Date = date, BtcClose = prediction.BtcClose, Signal = prediction.Signal,
            ModelVersion = prediction.ModelVersion, PredictionStatus = prediction.PredictionStatus,
            BullishProbability = prediction.BullishProbability, MarketRegime = prediction.MarketRegime,
            Confidence = prediction.Confidence, TargetPrice = prediction.TargetPrice,
            InvalidationPrice = prediction.InvalidationPrice, TargetChangePercent = prediction.TargetChangePercent,
            InvalidationChangePercent = prediction.InvalidationChangePercent, RiskRewardRatio = prediction.RiskRewardRatio,
            HorizonDays = prediction.HorizonDays, ElapsedDays = elapsed,
            ForwardReturn7d = prediction.HorizonDays == 7 ? forwardReturn : null, DirectionCorrect = directionCorrect,
            ConsensusVerdict = prediction.Ensemble?.ConsensusVerdict,
            ConsensusAgreementPercent = prediction.Ensemble?.ConsensusAgreementPercent
        };
        if (prediction.Signal == "NEUTRAL_HOLD")
            return item with { OutcomeStatus = "NO_TRADE", RealizedReturnPercent = 0m };
        if (prediction.BtcClose <= 0 || prediction.TargetPrice <= 0 || prediction.InvalidationPrice <= 0)
            return item with { OutcomeStatus = "DATA_UNAVAILABLE" };

        bool bearish = prediction.Signal == "BEARISH_RISK";
        decimal costs = 2m * (feePerSide + slippagePerSide);
        decimal Return(decimal price) => (bearish ? prediction.BtcClose - price : price - prediction.BtcClose) / prediction.BtcClose - costs;
        DateOnly expectedDate = date.AddDays(1);
        foreach (var bar in sorted)
        {
            // A missing preceding candle could have hit a barrier. Never invent its outcome.
            if (bar.Date != expectedDate || bar.BtcClose <= 0m || bar.BtcLow <= 0m || bar.BtcHigh < bar.BtcLow)
                return item with { OutcomeStatus = "DATA_UNAVAILABLE", DirectionCorrect = null };
            expectedDate = expectedDate.AddDays(1);
            bool stop = bearish ? bar.BtcHigh >= prediction.InvalidationPrice : bar.BtcLow <= prediction.InvalidationPrice;
            bool target = bearish ? bar.BtcLow <= prediction.TargetPrice : bar.BtcHigh >= prediction.TargetPrice;
            // Daily OHLC cannot establish intrabar order: stop first, including adverse opening gaps.
            if (stop)
            {
                decimal price = bearish ? Math.Max(bar.BtcOpen, prediction.InvalidationPrice)
                    : bar.BtcOpen > 0m ? Math.Min(bar.BtcOpen, prediction.InvalidationPrice) : prediction.InvalidationPrice;
                return item with
                {
                    OutcomeStatus = "INVALIDATED", ExitReason = target ? "BOTH_BARRIERS_STOP_FIRST" : "STOP_LOSS",
                    RealizedReturnPercent = Return(price), ResolvedDate = bar.Date, ResolvedPrice = price
                };
            }
            if (target)
                return item with
                {
                    OutcomeStatus = "TARGET_HIT", ExitReason = "TAKE_PROFIT",
                    RealizedReturnPercent = Return(prediction.TargetPrice), ResolvedDate = bar.Date, ResolvedPrice = prediction.TargetPrice
                };
        }
        if (final != null)
        {
            var net = Return(final.BtcClose);
            return item with
            {
                OutcomeStatus = net > 0m ? "EXPIRED_PROFIT" : "EXPIRED_LOSS", ExitReason = "HOLDING_EXPIRED",
                RealizedReturnPercent = net, ResolvedDate = final.Date, ResolvedPrice = final.BtcClose
            };
        }
        if (latestDate >= end)
            return item with { OutcomeStatus = "DATA_UNAVAILABLE", DirectionCorrect = null };
        return item with
        {
            OutcomeStatus = "PENDING",
            UnrealizedReturnPercent = sorted.Count > 0 ? Return(sorted[^1].BtcClose) : null
        };
    }

    public static PredictionJournalResponse Journal(IEnumerable<PredictionJournalItem> source, string mode)
    {
        var items = source.OrderByDescending(x => x.Date).ToList();
        var active = items.Where(x => x.Signal != "NEUTRAL_HOLD").ToList();
        var resolved = active.Where(x => x.OutcomeStatus is "TARGET_HIT" or "INVALIDATED" or "EXPIRED_PROFIT" or "EXPIRED_LOSS").ToList();
        var directions = active.Where(x => x.DirectionCorrect.HasValue).ToList();
        decimal gains = resolved.Sum(x => Math.Max(0m, x.RealizedReturnPercent ?? 0m));
        decimal losses = -resolved.Sum(x => Math.Min(0m, x.RealizedReturnPercent ?? 0m));
        decimal? factor = losses > 0m ? gains / losses : null;
        decimal sum = resolved.Sum(x => x.RealizedReturnPercent ?? 0m) * 100m;
        return new PredictionJournalResponse
        {
            Mode = mode, Items = items,
            // This ledger contains overlapping signals, not a portfolio equity curve.
            EquityCurve = Array.Empty<JournalEquityPoint>(),
            Stats = new LiveJournalStats
            {
                TotalPredictions = items.Count, NoTradePredictions = items.Count - active.Count,
                UnavailableOutcomes = active.Count(x => x.OutcomeStatus == "DATA_UNAVAILABLE"),
                ResolvedPredictions = resolved.Count, PendingPredictions = active.Count(x => x.OutcomeStatus == "PENDING"),
                TargetHits = resolved.Count(x => x.OutcomeStatus == "TARGET_HIT"),
                Invalidations = resolved.Count(x => x.OutcomeStatus == "INVALIDATED"),
                ExpiredProfits = resolved.Count(x => x.OutcomeStatus == "EXPIRED_PROFIT"),
                ExpiredLosses = resolved.Count(x => x.OutcomeStatus == "EXPIRED_LOSS"),
                WinRatePercent = resolved.Count > 0 ? 100m * resolved.Count(x => x.RealizedReturnPercent > 0m) / resolved.Count : 0m,
                TargetHitRatePercent = resolved.Count > 0 ? 100m * resolved.Count(x => x.OutcomeStatus == "TARGET_HIT") / resolved.Count : 0m,
                ProfitFactor = factor ?? 0m, ProfitFactorValue = factor,
                CumulativeReturnPercent = sum, AverageReturnPercent = resolved.Count > 0 ? sum / resolved.Count : 0m,
                DirectionEvaluatedCount = directions.Count,
                DirectionAccuracyPercent = directions.Count > 0 ? 100m * directions.Count(x => x.DirectionCorrect == true) / directions.Count : null,
                AlwaysUpAccuracyPercent = directions.Count > 0 ? 100m * directions.Count(x => x.ForwardReturn7d > 0m) / directions.Count : null
            }
        };
    }
}
