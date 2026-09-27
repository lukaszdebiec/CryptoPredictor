using CryptoPredictor.Data;

namespace CryptoPredictor.Backtest;

public interface ITurningPointDetector
{
    /// <summary>
    /// Identifies historical peaks (local tops) and troughs (local bottoms) using a symmetric sliding window.
    /// </summary>
    IReadOnlyList<LabeledMarketDay> DetectTurningPoints(IReadOnlyList<DailyMarketData> data, int window = 5, decimal minReversalPercent = 0.03m);

    /// <summary>
    /// Pairs historical engineered daily features (inputs X) with forward-looking labels (targets Y) for model training.
    /// </summary>
    IReadOnlyList<ModelTrainingSample> BuildTrainingDataset(
        IReadOnlyList<DailyMarketData> data,
        IReadOnlyList<DailyFeatures> features,
        int lookaheadDays = 7,
        decimal bullishThreshold = 0.03m);
}
