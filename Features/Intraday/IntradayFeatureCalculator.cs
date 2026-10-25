using CryptoPredictor.Data.Intraday;

namespace CryptoPredictor.Features.Intraday;

public interface IIntradayFeatureCalculator
{
    IReadOnlyList<IntradayFeatures4h> Calculate(IReadOnlyList<MarketCandle4h> candles);
}

public class IntradayFeatureCalculator : IIntradayFeatureCalculator
{
    public IReadOnlyList<IntradayFeatures4h> Calculate(IReadOnlyList<MarketCandle4h> candles)
    {
        if (candles == null || candles.Count < 20)
            return Array.Empty<IntradayFeatures4h>();

        var sorted = candles.OrderBy(c => c.Timestamp).ToList();
        var result = new List<IntradayFeatures4h>();

        for (int i = 14; i < sorted.Count; i++)
        {
            var curr = sorted[i];

            // 1. Returns
            decimal ret4h = sorted[i - 1].Close > 0 ? (curr.Close - sorted[i - 1].Close) / sorted[i - 1].Close : 0m;
            decimal ret12h = i >= 3 && sorted[i - 3].Close > 0 ? (curr.Close - sorted[i - 3].Close) / sorted[i - 3].Close : ret4h;
            decimal ret24h = i >= 6 && sorted[i - 6].Close > 0 ? (curr.Close - sorted[i - 6].Close) / sorted[i - 6].Close : ret12h;

            // 2. Kaufman Efficiency Ratio (6 periods = 24h)
            decimal ker = 0.5m;
            if (i >= 6)
            {
                decimal direction = Math.Abs(curr.Close - sorted[i - 6].Close);
                decimal volatility = 0m;
                for (int k = i - 5; k <= i; k++)
                {
                    volatility += Math.Abs(sorted[k].Close - sorted[k - 1].Close);
                }
                ker = volatility > 0 ? Math.Clamp(direction / volatility, 0m, 1m) : 0.5m;
            }

            // 3. RSI 14
            decimal rsi = CalculateRsi(sorted, i, 14);

            // 4. ATR 14 Pct
            decimal atr = CalculateAtr(sorted, i, 14);
            decimal atrPct = curr.Close > 0 ? (atr / curr.Close) : 0.02m;

            // 5. Taker Buy/Sell Ratio & Z-Score (30 periods)
            decimal takerRatio = curr.TakerBuySellRatio ?? 1.0m;
            int zWindow = Math.Min(30, i);
            var recentTakers = sorted.Skip(i - zWindow).Take(zWindow).Select(c => c.TakerBuySellRatio ?? 1.0m).ToList();
            decimal avgTaker = recentTakers.Average();
            decimal stdTaker = (decimal)Math.Sqrt(recentTakers.Select(t => Math.Pow((double)(t - avgTaker), 2)).Average());
            decimal takerZ = stdTaker > 0.001m ? Math.Clamp((takerRatio - avgTaker) / stdTaker, -3.0m, 3.0m) : 0m;

            // 6. OI Change 24h (6 periods)
            decimal oiChange24h = 0m;
            if (i >= 6 && curr.OpenInterestUsd.HasValue && sorted[i - 6].OpenInterestUsd.HasValue && sorted[i - 6].OpenInterestUsd!.Value > 0)
            {
                oiChange24h = (curr.OpenInterestUsd.Value - sorted[i - 6].OpenInterestUsd!.Value) / sorted[i - 6].OpenInterestUsd!.Value;
            }

            // 7. SFP Liquidity Sweeps (Wyckoff Spring & Upthrust across past 24h-48h = 6 to 12 candles)
            int lookbackSfp = Math.Min(12, i);
            var prevWindow = sorted.Skip(i - lookbackSfp).Take(lookbackSfp).ToList();
            decimal highestHigh = prevWindow.Max(c => c.High);
            decimal lowestLow = prevWindow.Min(c => c.Low);

            // High swept: pierced above previous swing high, but closed below it
            bool isSfpHigh = curr.High > highestHigh && curr.Close < highestHigh && ret4h < 0.01m;

            // Low swept: pierced below previous swing low, but closed above it
            bool isSfpLow = curr.Low < lowestLow && curr.Close > lowestLow && ret4h > -0.01m;

            result.Add(new IntradayFeatures4h
            {
                Timestamp = curr.Timestamp,
                Close = curr.Close,
                Return4h = ret4h,
                Return12h = ret12h,
                Return24h = ret24h,
                KaufmanEfficiencyRatio6 = ker,
                Rsi14 = rsi,
                Atr14Pct = atrPct,
                TakerBuySellRatio = takerRatio,
                TakerAggressionZScore = takerZ,
                OiChange24hPct = oiChange24h,
                FundingRate = curr.FundingRate ?? 0m,
                IsSfpHighSwept = isSfpHigh,
                IsSfpLowSwept = isSfpLow,
                Session = curr.Session
            });
        }

        return result;
    }

    private static decimal CalculateRsi(List<MarketCandle4h> candles, int currentIndex, int period)
    {
        if (currentIndex < period) return 50m;

        decimal gains = 0m;
        decimal losses = 0m;

        for (int i = currentIndex - period + 1; i <= currentIndex; i++)
        {
            decimal diff = candles[i].Close - candles[i - 1].Close;
            if (diff >= 0) gains += diff;
            else losses += Math.Abs(diff);
        }

        if (losses == 0m) return 100m;
        if (gains == 0m) return 0m;

        decimal rs = (gains / period) / (losses / period);
        return Math.Round(100m - (100m / (1m + rs)), 1);
    }

    private static decimal CalculateAtr(List<MarketCandle4h> candles, int currentIndex, int period)
    {
        if (currentIndex < period) return candles[currentIndex].High - candles[currentIndex].Low;

        decimal sumTr = 0m;
        for (int i = currentIndex - period + 1; i <= currentIndex; i++)
        {
            decimal tr1 = candles[i].High - candles[i].Low;
            decimal tr2 = Math.Abs(candles[i].High - candles[i - 1].Close);
            decimal tr3 = Math.Abs(candles[i].Low - candles[i - 1].Close);
            sumTr += Math.Max(tr1, Math.Max(tr2, tr3));
        }

        return sumTr / period;
    }
}
