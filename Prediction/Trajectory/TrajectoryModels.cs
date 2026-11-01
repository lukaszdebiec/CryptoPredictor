namespace CryptoPredictor.Prediction.Trajectory;

public static class TrajectoryPolicy
{
    public const string Version = "trajectory-v2.0";
    public const int Horizon = 7;
    public const int Warmup = 60;
    public const int MinimumSamples = 500;
    public const int MinimumFit = 250;
    public const int MinimumCalibration = 60;
    public const int MinimumValidation = 60;
    public const double RidgePenalty = 1.0;
    public const double LowerQuantile = 0.10;
    public const double UpperQuantile = 0.90;
}

public sealed record TrajectoryPoint
{
    public required string Time { get; init; }
    public int HorizonDays { get; init; }
    public decimal MedianPrice { get; init; }
    public decimal LowerPrice { get; init; }
    public decimal UpperPrice { get; init; }
    public double MedianLogReturn { get; init; }
    public bool HasValidationAdvantage { get; init; }
}

public sealed record HorizonValidation
{
    public int HorizonDays { get; init; }
    public int FitSamples { get; init; }
    public int CalibrationSamples { get; init; }
    public int ValidationSamples { get; init; }
    public DateOnly FitOutcomeEnd { get; init; }
    public DateOnly CalibrationStart { get; init; }
    public DateOnly CalibrationOutcomeEnd { get; init; }
    public DateOnly ValidationStart { get; init; }
    public DateOnly ValidationEnd { get; init; }
    public double MaeLogReturn { get; init; }
    public double BaselineMaeLogReturn { get; init; }
    public double RmseLogReturn { get; init; }
    public double MeanPinballLoss { get; init; }
    public double BaselineMeanPinballLoss { get; init; }
    public double IntervalCoverage { get; init; }
    public double BaselineIntervalCoverage { get; init; }
    public double MeanIntervalWidthLogReturn { get; init; }
    public bool HasValidationAdvantage { get; init; }
}

public sealed record TrajectoryForecast
{
    public string ModelVersion { get; init; } = TrajectoryPolicy.Version;
    public required string Status { get; init; }
    public required string Message { get; init; }
    public required string Source { get; init; }
    public required string Symbol { get; init; }
    public DateOnly? AsOfDate { get; init; }
    public int? DataAgeDays => AsOfDate.HasValue ? Math.Max(0, DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - 1 - AsOfDate.Value.DayNumber) : null;
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    public decimal? ReferenceClose { get; init; }
    public string DataFingerprint { get; init; } = "";
    public int Observations { get; init; }
    public int EligibleSamples { get; init; }
    public int RequiredSamples { get; init; } = TrajectoryPolicy.MinimumSamples;
    public double NominalMarginalCoverage { get; init; } = 0.80;
    public string IntervalMethod { get; init; } = "Volatility-scaled empirical residual quantiles on a separate chronological calibration period; no joint-path coverage guarantee.";
    public int ServingFitSamples { get; init; }
    public int ServingCalibrationSamples { get; init; }
    public DateOnly? ServingFitOutcomeEnd { get; init; }
    public DateOnly? ServingCalibrationStart { get; init; }
    public IReadOnlyList<TrajectoryPoint> Points { get; init; } = Array.Empty<TrajectoryPoint>();
    public IReadOnlyList<HorizonValidation> Validation { get; init; } = Array.Empty<HorizonValidation>();
}
