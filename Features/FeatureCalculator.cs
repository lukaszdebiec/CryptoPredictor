using CryptoPredictor.Data;

namespace CryptoPredictor.Features;

public class FeatureCalculator : IFeatureCalculator
{
    private const double TradingDaysPerYear = 365.0; // Crypto trades 365 days a year

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

        // Precompute rolling 7-day ETF flows to calculate a smooth, weekend-neutral institutional flow Z-Score
        var etfFlows7d = new decimal[sorted.Count];
        for (int j = 0; j < sorted.Count; j++)
        {
            etfFlows7d[j] = GetRollingSum(sorted, j, 7, d => d.BitcoinEtfNetFlowUsd);
        }

        decimal allTimeHigh = 0m;

        for (int i = 0; i < sorted.Count; i++)
        {
            var current = sorted[i];

            // Update ATH tracking
            if (current.BtcHigh > allTimeHigh)
                allTimeHigh = current.BtcHigh;

            // 1. Returns
            var ret1d = CalculateReturn(sorted, i, 1);
            var ret3d = CalculateReturn(sorted, i, 3);
            var ret7d = CalculateReturn(sorted, i, 7);
            var ret30d = CalculateReturn(sorted, i, 30);

            // 2. Drawdowns
            var high30d = GetRollingMax(sorted, i, 30, d => d.BtcHigh);
            var dd30d = high30d > 0 ? (current.BtcClose - high30d) / high30d : 0m;

            var high90d = GetRollingMax(sorted, i, 90, d => d.BtcHigh);
            var dd90d = high90d > 0 ? (current.BtcClose - high90d) / high90d : 0m;

            var ddAth = allTimeHigh > 0 ? (current.BtcClose - allTimeHigh) / allTimeHigh : 0m;

            // 3. Realized Volatility (Annualized)
            var vol7d = CalculateAnnualizedRealizedVolatility(dailyReturns, i, 7);
            var vol30d = CalculateAnnualizedRealizedVolatility(dailyReturns, i, 30);

            // 4. Volume vs 30d average
            var avgVol30d = GetRollingAverage(sorted, i, 30, d => d.BtcVolume);
            var volVs30d = avgVol30d > 0 ? current.BtcVolume / avgVol30d : 1.0m;

            // 5. Open Interest dynamics & Z-Scores
            var oi1d = CalculateRelativeChange(sorted, i, 1, d => d.FuturesOpenInterestUsd);
            var oi7d = CalculateRelativeChange(sorted, i, 7, d => d.FuturesOpenInterestUsd);
            var oiZ30d = CalculateRollingZScore(sorted, i, 30, d => d.FuturesOpenInterestUsd);

            // 6. Funding Z-Scores
            var fundingZ30d = CalculateRollingZScore(sorted, i, 30, d => d.FundingRate);
            var fundingZ90d = CalculateRollingZScore(sorted, i, 90, d => d.FundingRate);

            // 7. Liquidations Z-Score
            var liqZ30d = CalculateRollingZScore(sorted, i, 30, d => d.LongLiquidationsUsd + d.ShortLiquidationsUsd);

            // 8. ETF Flows (7-day cumulative window eliminates 5/7 TradFi weekend zero-distortion)
            var etfFlow7d = etfFlows7d[i];
            var etfFlow30d = GetRollingSum(sorted, i, 30, d => d.BitcoinEtfNetFlowUsd);
            var etfFlowZ30d = CalculateRollingZScore(etfFlows7d, i, 30);

            // 9. Options DVOL & Skew Z-Scores
            var dvolZ30d = CalculateRollingZScore(sorted, i, 30, d => d.BtcDvol);
            var skewZ30d = CalculateRollingZScore(sorted, i, 30, d => d.Options25DeltaSkew);

            results.Add(new DailyFeatures
            {
                Date = current.Date,
                BtcReturn1d = ret1d,
                BtcReturn3d = ret3d,
                BtcReturn7d = ret7d,
                BtcReturn30d = ret30d,
                BtcDrawdownFrom30dHigh = dd30d,
                BtcDrawdownFrom90dHigh = dd90d,
                BtcDrawdownFromAth = ddAth,
                RealizedVolatility7d = vol7d,
                RealizedVolatility30d = vol30d,
                VolumeVs30dAverage = volVs30d,
                OiChange1d = oi1d,
                OiChange7d = oi7d,
                OiZScore30d = oiZ30d,
                FundingZScore30d = fundingZ30d,
                FundingZScore90d = fundingZ90d,
                LiquidationsZScore30d = liqZ30d,
                EtfFlow7d = etfFlow7d,
                EtfFlow30d = etfFlow30d,
                EtfFlowZScore30d = etfFlowZ30d,
                DvolZScore30d = dvolZ30d,
                SkewZScore30d = skewZ30d
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

    private static decimal CalculateRollingZScore(List<DailyMarketData> list, int currentIndex, int windowSize, Func<DailyMarketData, decimal> selector)
    {
        var startIndex = Math.Max(0, currentIndex - windowSize + 1);
        int count = currentIndex - startIndex + 1;

        if (count < 2)
            return 0m;

        decimal sum = 0m;
        for (int i = startIndex; i <= currentIndex; i++)
        {
            sum += selector(list[i]);
        }

        decimal mean = sum / count;

        double sumSquaredDiff = 0.0;
        for (int i = startIndex; i <= currentIndex; i++)
        {
            double diff = (double)(selector(list[i]) - mean);
            sumSquaredDiff += diff * diff;
        }

        // Sample standard deviation (N - 1)
        double sampleVariance = sumSquaredDiff / (count - 1);
        double stdDev = Math.Sqrt(sampleVariance);

        if (stdDev <= 1e-12)
            return 0m;

        var currentVal = selector(list[currentIndex]);
        return (decimal)(((double)currentVal - (double)mean) / stdDev);
    }

    private static decimal CalculateRollingZScore(decimal[] array, int currentIndex, int windowSize)
    {
        var startIndex = Math.Max(0, currentIndex - windowSize + 1);
        int count = currentIndex - startIndex + 1;

        if (count < 2)
            return 0m;

        decimal sum = 0m;
        for (int i = startIndex; i <= currentIndex; i++)
        {
            sum += array[i];
        }

        decimal mean = sum / count;

        double sumSquaredDiff = 0.0;
        for (int i = startIndex; i <= currentIndex; i++)
        {
            double diff = (double)(array[i] - mean);
            sumSquaredDiff += diff * diff;
        }

        double sampleVariance = sumSquaredDiff / (count - 1);
        double stdDev = Math.Sqrt(sampleVariance);

        if (stdDev <= 1e-12)
            return 0m;

        return (decimal)(((double)array[currentIndex] - (double)mean) / stdDev);
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
