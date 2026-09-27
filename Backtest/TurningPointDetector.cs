using CryptoPredictor.Data;

namespace CryptoPredictor.Backtest;

public class TurningPointDetector : ITurningPointDetector
{
    public IReadOnlyList<LabeledMarketDay> DetectTurningPoints(
        IReadOnlyList<DailyMarketData> data,
        int window = 5,
        decimal minReversalPercent = 0.03m)
    {
        if (data == null || data.Count == 0)
            return Array.Empty<LabeledMarketDay>();

        var sorted = data.OrderBy(d => d.Date).ToList();
        int n = sorted.Count;
        var results = new List<LabeledMarketDay>(n);

        for (int i = 0; i < n; i++)
        {
            var current = sorted[i];

            // 1. Forward returns
            decimal? ret3d = i + 3 < n && current.BtcClose > 0 
                ? (sorted[i + 3].BtcClose - current.BtcClose) / current.BtcClose 
                : null;

            decimal? ret7d = i + 7 < n && current.BtcClose > 0 
                ? (sorted[i + 7].BtcClose - current.BtcClose) / current.BtcClose 
                : null;

            decimal? ret14d = i + 14 < n && current.BtcClose > 0 
                ? (sorted[i + 14].BtcClose - current.BtcClose) / current.BtcClose 
                : null;

            bool? isBullish7d = ret7d.HasValue ? ret7d.Value >= minReversalPercent : null;

            // 2. Turning points (peaks and troughs)
            var type = TurningPointType.None;

            if (i >= window && i + window < n)
            {
                bool isMin = true;
                bool isMax = true;

                for (int j = i - window; j <= i + window; j++)
                {
                    if (j == i) continue;

                    if (sorted[j].BtcLow < current.BtcLow)
                        isMin = false;

                    if (sorted[j].BtcHigh > current.BtcHigh)
                        isMax = false;
                }

                if (isMin)
                {
                    // Check if price bounced up by at least minReversalPercent in the forward window
                    decimal maxForwardHigh = current.BtcHigh;
                    for (int j = i + 1; j <= i + window; j++)
                    {
                        if (sorted[j].BtcHigh > maxForwardHigh)
                            maxForwardHigh = sorted[j].BtcHigh;
                    }

                    if (current.BtcLow > 0 && (maxForwardHigh - current.BtcLow) / current.BtcLow >= minReversalPercent)
                    {
                        type = TurningPointType.LocalBottom;
                    }
                }
                else if (isMax)
                {
                    // Check if price dropped by at least minReversalPercent in the forward window
                    decimal minForwardLow = current.BtcLow;
                    for (int j = i + 1; j <= i + window; j++)
                    {
                        if (sorted[j].BtcLow < minForwardLow)
                            minForwardLow = sorted[j].BtcLow;
                    }

                    if (current.BtcHigh > 0 && (current.BtcHigh - minForwardLow) / current.BtcHigh >= minReversalPercent)
                    {
                        type = TurningPointType.LocalTop;
                    }
                }
            }

            results.Add(new LabeledMarketDay
            {
                Date = current.Date,
                BtcClose = current.BtcClose,
                TurningPoint = type,
                ForwardReturn3d = ret3d,
                ForwardReturn7d = ret7d,
                ForwardReturn14d = ret14d,
                IsForward7dBullish = isBullish7d
            });
        }

        return results;
    }

    public IReadOnlyList<ModelTrainingSample> BuildTrainingDataset(
        IReadOnlyList<DailyMarketData> data,
        IReadOnlyList<DailyFeatures> features,
        int lookaheadDays = 7,
        decimal bullishThreshold = 0.03m)
    {
        var labels = DetectTurningPoints(data, window: 5, minReversalPercent: bullishThreshold);
        var labelByDate = labels.ToDictionary(l => l.Date);

        var samples = new List<ModelTrainingSample>();

        foreach (var feat in features.OrderBy(f => f.Date))
        {
            if (labelByDate.TryGetValue(feat.Date, out var label))
            {
                // We only include samples where the forward outcome is fully known (avoids lookahead leakage)
                if (label.ForwardReturn7d.HasValue)
                {
                    samples.Add(new ModelTrainingSample
                    {
                        Date = feat.Date,
                        Features = feat,
                        Label = label
                    });
                }
            }
        }

        return samples;
    }
}
