using CryptoPredictor.Data.Intraday;
using CryptoPredictor.Features.Intraday;

namespace CryptoPredictor.Prediction.Intraday;

public interface IIntradayPredictorService
{
    IntradayPrediction Predict(IntradayFeatures4h feat, MarketCandle4h candle);
    IntradayJournalResponse GenerateIntradayJournal(IReadOnlyList<MarketCandle4h> candles, IReadOnlyList<IntradayFeatures4h> features);
}

public class IntradayPredictorService : IIntradayPredictorService
{
    public IntradayPrediction Predict(IntradayFeatures4h feat, MarketCandle4h candle)
    {
        var drivers = new List<string>();
        decimal prob = 0.50m;

        // 1. Orderflow & Taker Aggression
        string takerAggression;
        if (feat.TakerBuySellRatio >= 1.15m || feat.TakerAggressionZScore >= 1.2m)
        {
            prob += 0.14m;
            takerAggression = $"Aggressive Market Buying (Ratio: {feat.TakerBuySellRatio:F2}, Z: +{feat.TakerAggressionZScore:F1}σ)";
            drivers.Add($"Taker Orderflow: Aggressive market buyers dominate volume (Ratio: {feat.TakerBuySellRatio:F2}, +{feat.TakerAggressionZScore:F1}σ above mean).");
        }
        else if (feat.TakerBuySellRatio <= 0.88m || feat.TakerAggressionZScore <= -1.2m)
        {
            prob -= 0.14m;
            takerAggression = $"Aggressive Market Selling (Ratio: {feat.TakerBuySellRatio:F2}, Z: {feat.TakerAggressionZScore:F1}σ)";
            drivers.Add($"Taker Orderflow: Aggressive market sellers dominate volume (Ratio: {feat.TakerBuySellRatio:F2}, {feat.TakerAggressionZScore:F1}σ below mean).");
        }
        else
        {
            takerAggression = $"Balanced Orderflow (Ratio: {feat.TakerBuySellRatio:F2})";
        }

        // 2. Wyckoff SFP (Liquidity Sweeps)
        if (feat.IsSfpLowSwept)
        {
            prob += 0.16m;
            drivers.Add("Wyckoff 4H Spring: Price swept below previous 24h-48h swing low, flushed stop-losses and aggressively closed back inside the range.");
        }
        else if (feat.IsSfpHighSwept)
        {
            prob -= 0.16m;
            drivers.Add("Wyckoff 4H Upthrust: Price wicked above previous 24h-48h swing high, trapped breakout buyers and immediately rejected back into the range.");
        }

        // 3. Technical Momentum & Extremes
        if (feat.Rsi14 <= 32m)
        {
            prob += 0.08m;
            drivers.Add($"4H RSI is oversold ({feat.Rsi14:F1}), signaling potential mean-reversion exhaustion.");
        }
        else if (feat.Rsi14 >= 68m)
        {
            prob -= 0.08m;
            drivers.Add($"4H RSI is overbought ({feat.Rsi14:F1}), warning of overextended tactical momentum.");
        }

        // 4. Open Interest & Funding Dynamics
        if (feat.OiChange24hPct > 0.05m && feat.Return24h > 0.02m)
        {
            prob += 0.06m;
            drivers.Add($"Open Interest expanded by +{feat.OiChange24hPct:P1} over 24h alongside positive price action, indicating fresh capital entering longs.");
        }
        else if (feat.OiChange24hPct > 0.05m && feat.Return24h < -0.02m)
        {
            prob -= 0.06m;
            drivers.Add($"Open Interest expanded by +{feat.OiChange24hPct:P1} during downward impulse, confirming aggressive short build-up.");
        }

        // 5. Session Context
        string sessionInfo;
        switch (feat.Session)
        {
            case "London":
                sessionInfo = "London Session (08:00 - 13:00 UTC) - High European institutional liquidity";
                break;
            case "NewYork":
                sessionInfo = "New York Session (13:30 - 21:00 UTC) - Core Wall Street cash market volatility";
                break;
            case "Asia":
                sessionInfo = "Asian Session (00:00 - 08:00 UTC) - Range-bound liquidity accumulation";
                break;
            default:
                sessionInfo = "Weekend Low Liquidity - Caution advised on breakout validation";
                if (Math.Abs(prob - 0.50m) > 0.10m)
                    prob = 0.50m + (prob - 0.50m) * 0.70m; // Tame weekend conviction
                break;
        }

        // 6. Low Efficiency Chop Filter
        bool isChop = feat.KaufmanEfficiencyRatio6 < 0.20m && !feat.IsSfpLowSwept && !feat.IsSfpHighSwept;
        if (isChop)
        {
            prob = 0.50m;
            drivers.Insert(0, $"4H Consolidation Filter: Kaufman Efficiency Ratio ({feat.KaufmanEfficiencyRatio6:F2}) confirms choppy sideways compression. Tactical Engine mandates Cash Hold.");
        }

        prob = Math.Clamp(prob, 0.10m, 0.90m);

        // Determine Signal & Confidence
        string signal;
        string confidence;
        if (prob >= 0.62m)
        {
            signal = "BULLISH_TACTICAL";
            confidence = prob >= 0.72m ? "High" : "Moderate";
        }
        else if (prob <= 0.38m)
        {
            signal = "BEARISH_TACTICAL";
            confidence = prob <= 0.28m ? "High" : "Moderate";
        }
        else
        {
            signal = "NEUTRAL_CHOP";
            confidence = "Neutral";
        }

        // 7. Tactical Target & Invalidation (12h - 24h horizon)
        // Scaled to 4h volatility (ATR)
        decimal targetPct = Math.Clamp(feat.Atr14Pct * 1.35m, 0.016m, 0.028m); // ~1.6% - 2.8%
        decimal invalidationPct = Math.Clamp(feat.Atr14Pct * 0.95m, 0.012m, 0.018m); // ~1.2% - 1.8%
        decimal rrRatio = invalidationPct > 0m ? Math.Round(targetPct / invalidationPct, 2) : 1.4m;

        decimal targetPrice;
        decimal invalidationPrice;

        if (signal == "BEARISH_TACTICAL")
        {
            targetPrice = Math.Round(candle.Close * (1m - targetPct), 2);
            invalidationPrice = Math.Round(candle.Close * (1m + invalidationPct), 2);
        }
        else
        {
            targetPrice = Math.Round(candle.Close * (1m + targetPct), 2);
            invalidationPrice = Math.Round(candle.Close * (1m - invalidationPct), 2);
        }

        return new IntradayPrediction
        {
            Timestamp = candle.Timestamp,
            BtcClose = candle.Close,
            Signal = signal,
            TacticalProbability = Math.Round(prob, 4),
            Confidence = confidence,
            TargetPrice = targetPrice,
            InvalidationPrice = invalidationPrice,
            TargetChangePercent = targetPct,
            InvalidationChangePercent = invalidationPct,
            RiskRewardRatio = rrRatio,
            HorizonHours = 24,
            Session = sessionInfo,
            RawSession = feat.Session,
            TakerAggression = takerAggression,
            TakerBuySellRatio = Math.Round(feat.TakerBuySellRatio, 3),
            TakerAggressionZScore = Math.Round(feat.TakerAggressionZScore, 2),
            OpenInterestUsd = candle.OpenInterestUsd,
            OiChange24hPct = Math.Round(feat.OiChange24hPct, 4),
            Rsi14 = Math.Round(feat.Rsi14, 1),
            KaufmanEfficiencyRatio = Math.Round(feat.KaufmanEfficiencyRatio6, 2),
            IsSfpHighSwept = feat.IsSfpHighSwept,
            IsSfpLowSwept = feat.IsSfpLowSwept,
            KeyDrivers = drivers
        };
    }

    public IntradayJournalResponse GenerateIntradayJournal(IReadOnlyList<MarketCandle4h> candles, IReadOnlyList<IntradayFeatures4h> features)
    {
        if (candles == null || candles.Count < 20 || features == null || features.Count == 0)
        {
            return new IntradayJournalResponse();
        }

        var sortedCandles = candles.OrderBy(c => c.Timestamp).ToList();
        var featuresMap = features.ToDictionary(f => f.Timestamp);
        var lastTs = sortedCandles[^1].Timestamp;

        var items = new List<IntradayJournalItem>();

        for (int i = 0; i < sortedCandles.Count; i++)
        {
            var c = sortedCandles[i];
            if (!featuresMap.TryGetValue(c.Timestamp, out var feat))
                continue;

            var pred = Predict(feat, c);

            // Forward evaluation across next 6 candles (24 hours)
            var forwardCandles = sortedCandles.Skip(i + 1).Take(6).ToList();
            int elapsedCandles = forwardCandles.Count;

            string outcome = "PENDING";
            decimal? realizedRet = null;
            DateTimeOffset? resolvedTs = null;
            decimal? resolvedPrice = null;

            if (pred.Signal == "BULLISH_TACTICAL")
            {
                foreach (var fwd in forwardCandles)
                {
                    if (fwd.High >= pred.TargetPrice)
                    {
                        outcome = "TARGET_HIT";
                        realizedRet = pred.TargetChangePercent;
                        resolvedTs = fwd.Timestamp;
                        resolvedPrice = pred.TargetPrice;
                        break;
                    }
                    if (fwd.Low <= pred.InvalidationPrice)
                    {
                        outcome = "INVALIDATED";
                        realizedRet = -pred.InvalidationChangePercent;
                        resolvedTs = fwd.Timestamp;
                        resolvedPrice = pred.InvalidationPrice;
                        break;
                    }
                }

                if (outcome == "PENDING" && elapsedCandles >= 6 && forwardCandles.Count > 0)
                {
                    var lastFwd = forwardCandles[^1];
                    decimal ret = (lastFwd.Close - pred.BtcClose) / pred.BtcClose;
                    outcome = ret >= 0 ? "EXPIRED_PROFIT" : "EXPIRED_LOSS";
                    realizedRet = Math.Round(ret, 4);
                    resolvedTs = lastFwd.Timestamp;
                    resolvedPrice = lastFwd.Close;
                }
            }
            else if (pred.Signal == "BEARISH_TACTICAL")
            {
                foreach (var fwd in forwardCandles)
                {
                    if (fwd.Low <= pred.TargetPrice)
                    {
                        outcome = "TARGET_HIT";
                        realizedRet = pred.TargetChangePercent;
                        resolvedTs = fwd.Timestamp;
                        resolvedPrice = pred.TargetPrice;
                        break;
                    }
                    if (fwd.High >= pred.InvalidationPrice)
                    {
                        outcome = "INVALIDATED";
                        realizedRet = -pred.InvalidationChangePercent;
                        resolvedTs = fwd.Timestamp;
                        resolvedPrice = pred.InvalidationPrice;
                        break;
                    }
                }

                if (outcome == "PENDING" && elapsedCandles >= 6 && forwardCandles.Count > 0)
                {
                    var lastFwd = forwardCandles[^1];
                    decimal ret = (pred.BtcClose - lastFwd.Close) / pred.BtcClose;
                    outcome = ret >= 0 ? "EXPIRED_PROFIT" : "EXPIRED_LOSS";
                    realizedRet = Math.Round(ret, 4);
                    resolvedTs = lastFwd.Timestamp;
                    resolvedPrice = lastFwd.Close;
                }
            }

            items.Add(new IntradayJournalItem
            {
                Timestamp = c.Timestamp,
                BtcClose = c.Close,
                Signal = pred.Signal,
                TacticalProbability = pred.TacticalProbability,
                TargetPrice = pred.TargetPrice,
                InvalidationPrice = pred.InvalidationPrice,
                TargetChangePercent = pred.TargetChangePercent,
                InvalidationChangePercent = pred.InvalidationChangePercent,
                Session = feat.Session,
                OutcomeStatus = outcome,
                RealizedReturnPercent = realizedRet,
                ResolvedTimestamp = resolvedTs,
                ResolvedPrice = resolvedPrice,
                ElapsedCandles = elapsedCandles
            });
        }

        var resolved = items.Where(i => i.OutcomeStatus != "PENDING" && i.Signal != "NEUTRAL_CHOP").ToList();
        int tp = resolved.Count(i => i.OutcomeStatus == "TARGET_HIT");
        int sl = resolved.Count(i => i.OutcomeStatus == "INVALIDATED");
        int expW = resolved.Count(i => i.OutcomeStatus == "EXPIRED_PROFIT");
        int wins = tp + expW;

        decimal wr = resolved.Count > 0 ? Math.Round((decimal)wins / resolved.Count * 100m, 1) : 0m;
        decimal tpr = resolved.Count > 0 ? Math.Round((decimal)tp / resolved.Count * 100m, 1) : 0m;
        decimal cumPnl = Math.Round(resolved.Sum(i => (i.RealizedReturnPercent ?? 0m) * 100m), 1);

        return new IntradayJournalResponse
        {
            TotalSignals = items.Count,
            ResolvedSignals = resolved.Count,
            TargetHits = tp,
            Invalidations = sl,
            WinRatePercent = wr,
            TargetHitRatePercent = tpr,
            CumulativeReturnPercent = cumPnl,
            Items = items.OrderByDescending(i => i.Timestamp).ToList()
        };
    }
}
