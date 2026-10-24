using CryptoPredictor.Data;

namespace CryptoPredictor.Features;

public class LiquidityEngine : ILiquidityEngine
{
    private const decimal MMR_BTC = 0.005m; // Binance 0.5% maintenance margin rate for BTC
    private const int LookbackWindow = 30;

    public LiquidityLevels CalculateLiquidityLevels(IReadOnlyList<DailyMarketData> history, int currentIndex)
    {
        if (history == null || history.Count == 0 || currentIndex < 0 || currentIndex >= history.Count)
        {
            return new LiquidityLevels();
        }

        var current = history[currentIndex];
        var currentPrice = current.BtcClose;
        if (currentPrice <= 0m)
            return new LiquidityLevels();

        var startIndex = Math.Max(0, currentIndex - LookbackWindow);
        var window = history.Skip(startIndex).Take(currentIndex - startIndex + 1).ToList();

        // 1. Identify key structural swing extremes across lookback
        decimal swingHigh = window.Max(d => d.BtcHigh);
        decimal swingLow = window.Min(d => d.BtcLow);

        // Also identify the nearest local pivot above and below the current price
        decimal nearestPivotHigh = swingHigh;
        decimal nearestPivotLow = swingLow;

        // Scan for recent 5-day local pivots inside lookback
        for (int k = 2; k < window.Count - 2; k++)
        {
            var bar = window[k];
            bool isPivotHigh = bar.BtcHigh > window[k - 1].BtcHigh &&
                               bar.BtcHigh > window[k - 2].BtcHigh &&
                               bar.BtcHigh > window[k + 1].BtcHigh &&
                               bar.BtcHigh > window[k + 2].BtcHigh;

            bool isPivotLow = bar.BtcLow < window[k - 1].BtcLow &&
                              bar.BtcLow < window[k - 2].BtcLow &&
                              bar.BtcLow < window[k + 1].BtcLow &&
                              bar.BtcLow < window[k + 2].BtcLow;

            if (isPivotHigh && bar.BtcHigh > currentPrice && bar.BtcHigh < nearestPivotHigh)
            {
                nearestPivotHigh = bar.BtcHigh;
            }

            if (isPivotLow && bar.BtcLow < currentPrice && bar.BtcLow > nearestPivotLow)
            {
                nearestPivotLow = bar.BtcLow;
            }
        }

        // 2. Project standard leverage liquidation bands from the reference pivots
        // Short liquidations sit above the pivot highs: P_short_liq = P * (1 + 1/L - MMR)
        // 50x: ~1.8%, 25x: ~3.8%, 10x: ~9.5%
        decimal upperBase = Math.Max(currentPrice, nearestPivotHigh);
        decimal upper50x = Math.Round(upperBase * (1.0m + (1.0m / 50.0m) - MMR_BTC), 2);
        decimal upper25x = Math.Round(upperBase * (1.0m + (1.0m / 25.0m) - MMR_BTC), 2);
        decimal upper10x = Math.Round(upperBase * (1.0m + (1.0m / 10.0m) - MMR_BTC), 2);

        // Long liquidations sit below the pivot lows: P_long_liq = P * (1 - 1/L + MMR)
        decimal lowerBase = Math.Min(currentPrice, nearestPivotLow);
        decimal lower50x = Math.Round(lowerBase * (1.0m - (1.0m / 50.0m) + MMR_BTC), 2);
        decimal lower25x = Math.Round(lowerBase * (1.0m - (1.0m / 25.0m) + MMR_BTC), 2);
        decimal lower10x = Math.Round(lowerBase * (1.0m - (1.0m / 10.0m) + MMR_BTC), 2);

        // Nearest walls
        decimal nearestUpperWall = currentPrice < upper50x ? upper50x : upper25x;
        decimal nearestLowerWall = currentPrice > lower50x ? lower50x : lower25x;

        decimal distToUpper = nearestUpperWall > currentPrice ? (nearestUpperWall - currentPrice) / currentPrice : 0m;
        decimal distToLower = currentPrice > nearestLowerWall ? (currentPrice - nearestLowerWall) / currentPrice : 0m;

        // 3. Liquidity Sweep Detection (Stop Hunt / SFP - Swing Failure Pattern)
        // Check if current bar (or previous bar) spiked beyond a recent swing/liq level and closed back inside
        bool longSweep = false;
        bool shortSweep = false;

        if (currentIndex >= 1)
        {
            var prev = history[currentIndex - 1];
            // Look at previous swing low prior to the current bar
            decimal priorSwingLow = history.Skip(Math.Max(0, currentIndex - 15)).Take(Math.Max(1, currentIndex - Math.Max(0, currentIndex - 15))).Min(d => d.BtcLow);
            decimal priorSwingHigh = history.Skip(Math.Max(0, currentIndex - 15)).Take(Math.Max(1, currentIndex - Math.Max(0, currentIndex - 15))).Max(d => d.BtcHigh);

            // Long sweep: Low penetrated below prior swing low or 50x liq band, but Close reclaimed back above it
            if ((current.BtcLow < priorSwingLow || current.BtcLow < lower50x) && current.BtcClose > priorSwingLow)
            {
                longSweep = true;
            }
            else if (prev.BtcLow < lower50x && current.BtcClose > lower50x && current.BtcClose > prev.BtcClose)
            {
                longSweep = true;
            }

            // Short sweep: High pushed above prior swing high or 50x liq band, but Close fell back below it
            if ((current.BtcHigh > priorSwingHigh || current.BtcHigh > upper50x) && current.BtcClose < priorSwingHigh)
            {
                shortSweep = true;
            }
            else if (prev.BtcHigh > upper50x && current.BtcClose < upper50x && current.BtcClose < prev.BtcClose)
            {
                shortSweep = true;
            }
        }

        string dominantSide;
        if (distToUpper < distToLower)
        {
            dominantSide = "Short Squeeze Magnet (Upside Liquidity)";
        }
        else
        {
            dominantSide = "Long Flush Magnet (Downside Liquidity)";
        }

        return new LiquidityLevels
        {
            UpperWall50x = upper50x,
            UpperWall25x = upper25x,
            UpperWall10x = upper10x,
            LowerWall50x = lower50x,
            LowerWall25x = lower25x,
            LowerWall10x = lower10x,
            NearestUpperWall = nearestUpperWall,
            NearestLowerWall = nearestLowerWall,
            DistToUpperWallPercent = Math.Round(distToUpper * 100m, 2),
            DistToLowerWallPercent = Math.Round(distToLower * 100m, 2),
            IsLongLiquiditySwept = longSweep,
            IsShortLiquiditySwept = shortSweep,
            DominantLiquiditySide = dominantSide
        };
    }

    public IReadOnlyList<LiquidityLevels> CalculateHistory(IReadOnlyList<DailyMarketData> history)
    {
        if (history == null || history.Count == 0)
            return Array.Empty<LiquidityLevels>();

        var list = new List<LiquidityLevels>(history.Count);
        for (int i = 0; i < history.Count; i++)
        {
            list.Add(CalculateLiquidityLevels(history, i));
        }

        return list;
    }
}
