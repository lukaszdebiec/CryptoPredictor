using CryptoPredictor.Data;

namespace CryptoPredictor.Features;

public class FeatureCalculator : IFeatureCalculator
{
    private const double TradingDaysPerYear = 365.0; // Crypto trades 365 days a year
    private const int MinPopulatedWindowSamples = 15;
    private readonly ILiquidityEngine _liquidityEngine;

    public FeatureCalculator(ILiquidityEngine? liquidityEngine = null)
    {
        _liquidityEngine = liquidityEngine ?? new LiquidityEngine();
    }

    public IReadOnlyList<DailyFeatures> Calculate(IEnumerable<DailyMarketData> history)
    {
        if (history == null)
            return Array.Empty<DailyFeatures>();

        var sorted = history.OrderBy(d => d.Date).ToList();
        if (sorted.Count == 0)
            return Array.Empty<DailyFeatures>();

        var results = new List<DailyFeatures>(sorted.Count);

        // Precompute daily simple returns for realized volatility
        var dailyReturns = new decimal[sorted.Count];
        for (int i = 1; i < sorted.Count; i++)
        {
            var prevClose = sorted[i - 1].BtcClose;
            dailyReturns[i] = prevClose > 0 ? (sorted[i].BtcClose - prevClose) / prevClose : 0m;
        }

        // Precompute rolling 7-day ETF flows (eliminates 5/7 TradFi weekend zero-distortion)
        // and relative Coinbase spot premium (% of BTC close, scale-invariant across price regimes)
        var etfFlows7d = new decimal[sorted.Count];
        var coinbasePremiumRatio = new decimal[sorted.Count];
        for (int j = 0; j < sorted.Count; j++)
        {
            etfFlows7d[j] = GetRollingSum(sorted, j, 7, d => d.BitcoinEtfNetFlowUsd);
            coinbasePremiumRatio[j] = sorted[j].BtcClose > 0m ? sorted[j].CoinbasePremium / sorted[j].BtcClose : 0m;
        }

        int consecutivePriceDays = 0;

        for (int i = 0; i < sorted.Count; i++)
        {
            var current = sorted[i];
            consecutivePriceDays = current.BtcClose <= 0m ? 0
                : i > 0 && sorted[i - 1].Date.AddDays(1) == current.Date ? consecutivePriceDays + 1 : 1;

            // 1. Returns & 30d Swing Drawdown
            var ret1d = CalculateReturn(sorted, i, 1);
            var ret3d = CalculateReturn(sorted, i, 3);
            var ret7d = CalculateReturn(sorted, i, 7);
            var ret30d = CalculateReturn(sorted, i, 30);

            var high30d = GetRollingMax(sorted, i, 30, d => d.BtcHigh);
            var dd30d = high30d > 0 ? (current.BtcClose - high30d) / high30d : 0m;

            // 2. Realized Volatility (Annualized) & Volume Climax Ratio
            var vol7d = CalculateAnnualizedRealizedVolatility(dailyReturns, i, 7);
            var vol30d = CalculateAnnualizedRealizedVolatility(dailyReturns, i, 30);

            var avgVol30d = GetRollingAverage(sorted, i, 30, d => d.BtcVolume);
            var volVs30d = avgVol30d > 0 ? current.BtcVolume / avgVol30d : 1.0m;

            // 3. Positioning, Institutional Flows & Sentiment (guarded against missing-prefix zero spikes)
            var oiZ30d = CalculatePositiveFeedZScore(sorted, i, 30, d => d.FuturesOpenInterestUsd);
            var fundingZ30d = CalculateSparseSignedFeedZScore(sorted, i, 30, d => d.FundingRate);
            var coinbaseZ30d = CalculateSparseSignedFeedZScore(coinbasePremiumRatio, i, 30);

            var etfFlow7d = etfFlows7d[i];
            var etfFlow30d = GetRollingSum(sorted, i, 30, d => d.BitcoinEtfNetFlowUsd);
            var etfFlowZ30d = CalculateSparseSignedFeedZScore(etfFlows7d, i, 30);

            var dvolZ30d = CalculatePositiveFeedZScore(sorted, i, 30, d => d.BtcDvol);
            var fngCentered = current.FearGreedIndex > 0
                ? Math.Clamp((current.FearGreedIndex - 50m) / 50m, -1m, 1m)
                : 0m;

            // 4. Fibonacci Retracement & Golden Pocket (30-day swing)
            var low30d = GetRollingMin(sorted, i, 30, d => d.BtcLow);
            var swingRange30d = high30d - low30d;
            decimal fibRatio30d = 0.5m;
            decimal distGoldenPocket = 0.118m;
            bool inGoldenPocket = false;

            if (swingRange30d > 0m)
            {
                fibRatio30d = Math.Clamp((high30d - current.BtcClose) / swingRange30d, 0m, 1.5m);
                distGoldenPocket = Math.Abs(fibRatio30d - 0.618m);
                inGoldenPocket = fibRatio30d >= 0.60m && fibRatio30d <= 0.66m;
            }

            // 5. Kaufman Efficiency Ratio (unsigned [0..1] for UI + signed [-1..+1] for directional models)
            var (ker10d, signedKer10d) = CalculateKaufmanEfficiencyRatios(sorted, i, 10);

            // 6. Weekly Trend Bias (50-day / ~7-week macro trend average)
            var avg50d = GetRollingAverage(sorted, i, 50, d => d.BtcClose);
            var weeklyTrendBias = avg50d > 0m ? (current.BtcClose - avg50d) / avg50d : 0m;

            // 7. Global Macro Dynamics (DXY, US10Y, S&P 500)
            var dxyRet7d = CalculateRelativeChange(sorted, i, 7, d => d.Dxy);
            var dxyZ30d = CalculatePositiveFeedZScore(sorted, i, 30, d => d.Dxy);
            var us10yChg7d = CalculateAbsoluteDifference(sorted, i, 7, d => d.Us10YearYield);
            var sp500Ret7d = CalculateRelativeChange(sorted, i, 7, d => d.Sp500Close);
            var sp500Ret30d = CalculateRelativeChange(sorted, i, 30, d => d.Sp500Close);

            // 8. Liquidity Pools & Liquidation Walls (Sweep Engine)
            var liq = _liquidityEngine.CalculateLiquidityLevels(sorted, i);

            results.Add(new DailyFeatures
            {
                Date = current.Date,
                ConsecutivePriceDays = consecutivePriceDays,
                BtcReturn1d = ret1d,
                BtcReturn3d = ret3d,
                BtcReturn7d = ret7d,
                BtcReturn30d = ret30d,
                BtcDrawdownFrom30dHigh = dd30d,
                RealizedVolatility7d = vol7d,
                RealizedVolatility30d = vol30d,
                VolumeVs30dAverage = volVs30d,
                OiZScore30d = oiZ30d,
                FundingZScore30d = fundingZ30d,
                CoinbasePremiumZScore30d = coinbaseZ30d,
                EtfFlow7d = etfFlow7d,
                EtfFlow30d = etfFlow30d,
                EtfFlowZScore30d = etfFlowZ30d,
                DvolZScore30d = dvolZ30d,
                FearGreedCentered = fngCentered,
                FibRetracementRatio30d = fibRatio30d,
                DistToGoldenPocket30d = distGoldenPocket,
                IsInGoldenPocket30d = inGoldenPocket,
                KaufmanEfficiencyRatio10d = ker10d,
                SignedKaufmanEfficiencyRatio10d = signedKer10d,
                WeeklyTrendBias = weeklyTrendBias,
                DxyReturn7d = dxyRet7d,
                DxyZScore30d = dxyZ30d,
                Us10YYieldChange7d = us10yChg7d,
                Sp500Return7d = sp500Ret7d,
                Sp500Return30d = sp500Ret30d,
                DistToLowerLiquidityWall = liq.DistToLowerWallPercent,
                DistToUpperLiquidityWall = liq.DistToUpperWallPercent,
                IsLongLiquiditySwept = liq.IsLongLiquiditySwept,
                IsShortLiquiditySwept = liq.IsShortLiquiditySwept
            });
        }

        return results;
    }

    #region Helper Calculation Methods

    private static decimal CalculateReturn(List<DailyMarketData> list, int currentIndex, int lookbackDays)
    {
        var targetIndex = currentIndex - lookbackDays;
        if (targetIndex < 0)
            return 0m;

        var basePrice = list[targetIndex].BtcClose;
        if (basePrice <= 0)
            return 0m;

        return (list[currentIndex].BtcClose - basePrice) / basePrice;
    }

    private static decimal CalculateRelativeChange(List<DailyMarketData> list, int currentIndex, int lookbackDays, Func<DailyMarketData, decimal> selector)
    {
        var targetIndex = currentIndex - lookbackDays;
        if (targetIndex < 0)
            return 0m;

        var baseVal = selector(list[targetIndex]);
        if (baseVal == 0m)
            return 0m;

        return (selector(list[currentIndex]) - baseVal) / Math.Abs(baseVal);
    }

    private static decimal CalculateAbsoluteDifference(List<DailyMarketData> list, int currentIndex, int lookbackDays, Func<DailyMarketData, decimal> selector)
    {
        var targetIndex = currentIndex - lookbackDays;
        if (targetIndex < 0)
            return 0m;

        var baseVal = selector(list[targetIndex]);
        if (baseVal == 0m)
            return 0m;

        return selector(list[currentIndex]) - baseVal;
    }

    private static decimal GetRollingMax(List<DailyMarketData> list, int currentIndex, int windowSize, Func<DailyMarketData, decimal> selector)
    {
        var startIndex = Math.Max(0, currentIndex - windowSize + 1);
        decimal maxVal = selector(list[startIndex]);

        for (int i = startIndex + 1; i <= currentIndex; i++)
        {
            var val = selector(list[i]);
            if (val > maxVal)
                maxVal = val;
        }

        return maxVal;
    }

    private static decimal GetRollingMin(List<DailyMarketData> list, int currentIndex, int windowSize, Func<DailyMarketData, decimal> selector)
    {
        var startIndex = Math.Max(0, currentIndex - windowSize + 1);
        decimal minVal = selector(list[startIndex]);

        for (int i = startIndex + 1; i <= currentIndex; i++)
        {
            var val = selector(list[i]);
            if (val < minVal)
                minVal = val;
        }

        return minVal;
    }

    private static (decimal UnsignedKer, decimal SignedKer) CalculateKaufmanEfficiencyRatios(
        List<DailyMarketData> list, int currentIndex, int lookbackDays)
    {
        var startIndex = currentIndex - lookbackDays;
        if (startIndex < 0)
            return (0.5m, 0m);

        var signedDisplacement = list[currentIndex].BtcClose - list[startIndex].BtcClose;

        decimal totalPath = 0m;
        for (int k = startIndex + 1; k <= currentIndex; k++)
        {
            totalPath += Math.Abs(list[k].BtcClose - list[k - 1].BtcClose);
        }

        if (totalPath == 0m)
            return (0m, 0m);

        var signedRatio = Math.Clamp(signedDisplacement / totalPath, -1.0m, 1.0m);
        return (Math.Abs(signedRatio), signedRatio);
    }

    private static decimal GetRollingAverage(List<DailyMarketData> list, int currentIndex, int windowSize, Func<DailyMarketData, decimal> selector)
    {
        var startIndex = Math.Max(0, currentIndex - windowSize + 1);
        decimal sum = 0m;
        int count = 0;

        for (int i = startIndex; i <= currentIndex; i++)
        {
            sum += selector(list[i]);
            count++;
        }

        return count > 0 ? sum / count : 0m;
    }

    private static decimal GetRollingSum(List<DailyMarketData> list, int currentIndex, int windowSize, Func<DailyMarketData, decimal> selector)
    {
        var startIndex = Math.Max(0, currentIndex - windowSize + 1);
        decimal sum = 0m;

        for (int i = startIndex; i <= currentIndex; i++)
        {
            sum += selector(list[i]);
        }

        return sum;
    }

    /// <summary>
    /// Rolling Z-score for strictly positive metrics (DVOL, DXY, Open Interest) where &lt;= 0 denotes missing data.
    /// Prevents artificial +5.3 sigma spikes at the start of partial feed coverage.
    /// </summary>
    private static decimal CalculatePositiveFeedZScore(
        List<DailyMarketData> list, int currentIndex, int windowSize, Func<DailyMarketData, decimal> selector)
    {
        var currentVal = selector(list[currentIndex]);
        if (currentVal <= 0m)
            return 0m;

        var startIndex = Math.Max(0, currentIndex - windowSize + 1);
        Span<double> valid = stackalloc double[windowSize];
        int count = 0;
        double sum = 0.0;

        for (int i = startIndex; i <= currentIndex; i++)
        {
            var v = selector(list[i]);
            if (v > 0m)
            {
                double dv = (double)v;
                valid[count++] = dv;
                sum += dv;
            }
        }

        int minRequired = Math.Min(MinPopulatedWindowSamples, currentIndex - startIndex + 1);
        if (count < 2 || count < minRequired)
            return 0m;

        return ComputeZScore(valid[..count], sum, (double)currentVal);
    }

    /// <summary>
    /// Rolling Z-score for signed external feeds (Funding Rate, Coinbase Premium, 7d ETF Flow)
    /// where a leading run of exact zeros indicates unpopulated history prior to feed start.
    /// </summary>
    private static decimal CalculateSparseSignedFeedZScore(
        List<DailyMarketData> list, int currentIndex, int windowSize, Func<DailyMarketData, decimal> selector)
    {
        var startIndex = Math.Max(0, currentIndex - windowSize + 1);
        Span<decimal> window = stackalloc decimal[windowSize];
        int len = 0;
        for (int i = startIndex; i <= currentIndex; i++)
            window[len++] = selector(list[i]);
        return ComputeSparseSignedZScore(window[..len]);
    }

    private static decimal CalculateSparseSignedFeedZScore(decimal[] array, int currentIndex, int windowSize)
    {
        var startIndex = Math.Max(0, currentIndex - windowSize + 1);
        int len = currentIndex - startIndex + 1;
        return ComputeSparseSignedZScore(array.AsSpan(startIndex, len));
    }

    private static decimal ComputeSparseSignedZScore(ReadOnlySpan<decimal> window)
    {
        if (window.Length < 2)
            return 0m;

        int firstNonZero = -1;
        int nonZeroCount = 0;
        for (int i = 0; i < window.Length; i++)
        {
            if (window[i] != 0m)
            {
                if (firstNonZero < 0)
                    firstNonZero = i;
                nonZeroCount++;
            }
        }

        int minRequired = Math.Min(MinPopulatedWindowSamples, window.Length);
        if (firstNonZero < 0 || nonZeroCount < minRequired)
            return 0m;

        // Exclude leading unpopulated zeros before the feed started while preserving interior zeros.
        var active = window[firstNonZero..];
        if (active.Length < 2)
            return 0m;

        Span<double> values = stackalloc double[active.Length];
        double sum = 0.0;
        for (int i = 0; i < active.Length; i++)
        {
            double dv = (double)active[i];
            values[i] = dv;
            sum += dv;
        }

        return ComputeZScore(values, sum, (double)active[^1]);
    }

    private static decimal ComputeZScore(ReadOnlySpan<double> values, double sum, double currentVal)
    {
        double mean = sum / values.Length;
        double sumSquaredDiff = 0.0;
        for (int i = 0; i < values.Length; i++)
        {
            double diff = values[i] - mean;
            sumSquaredDiff += diff * diff;
        }

        double sampleVariance = sumSquaredDiff / (values.Length - 1);
        double stdDev = Math.Sqrt(sampleVariance);
        if (stdDev <= 1e-12)
            return 0m;

        return (decimal)((currentVal - mean) / stdDev);
    }

    private static decimal CalculateAnnualizedRealizedVolatility(decimal[] dailyReturns, int currentIndex, int windowSize)
    {
        // We need returns for the last `windowSize` days ending at currentIndex
        var startIndex = Math.Max(1, currentIndex - windowSize + 1);
        int count = currentIndex - startIndex + 1;

        if (count < 2)
            return 0m;

        double sum = 0.0;
        for (int i = startIndex; i <= currentIndex; i++)
        {
            sum += (double)dailyReturns[i];
        }

        double mean = sum / count;

        double sumSquaredDiff = 0.0;
        for (int i = startIndex; i <= currentIndex; i++)
        {
            double diff = (double)dailyReturns[i] - mean;
            sumSquaredDiff += diff * diff;
        }

        double sampleVariance = sumSquaredDiff / (count - 1);
        double dailyStdDev = Math.Sqrt(sampleVariance);

        // Annualize: sigma_daily * sqrt(365)
        double annualizedVol = dailyStdDev * Math.Sqrt(TradingDaysPerYear);

        return (decimal)annualizedVol;
    }

    #endregion
}
