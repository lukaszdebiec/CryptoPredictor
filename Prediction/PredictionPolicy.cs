namespace CryptoPredictor.Prediction;

/// <summary>Versioned research defaults, fixed before inspecting validation results.</summary>
public static class PredictionPolicy
{
    public const string Version = "direction-v1";
    public const string ProbabilityMeaning = "P(BTC daily close in 7 calendar days > signal-day close)";
    public const int HorizonDays = 7;
    public const int WarmupDays = 60;
    public const int MinimumDatasetSamples = 100;
    public const int MinimumFitSamples = 60;
    public const decimal EntryThreshold = 0.60m;
    public const decimal FeePerSide = 0.0005m;
    public const decimal SlippagePerSide = 0.0005m;
}
