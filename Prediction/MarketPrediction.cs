namespace CryptoPredictor.Prediction;

public sealed record ModelTrainingMetrics
{
    public double Accuracy { get; init; }
    public double? AreaUnderRocCurve { get; init; }
    public double F1Score { get; init; }
    public double PositivePrecision { get; init; }
    public double PositiveRecall { get; init; }
    public int TrainingSampleCount { get; init; }
    public double? FastTreeAuc { get; init; }
    public double? FastForestAuc { get; init; }
    public double? LogisticAuc { get; init; }
    public int ValidationSampleCount { get; init; }
    public int RefitSampleCount { get; init; }
    public string TrainingDataFingerprint { get; init; } = "";
    public double BrierScore { get; init; }
    public double LogLoss { get; init; }
    public double BaselineBrierScore { get; init; }
    public double BaselineLogLoss { get; init; }
    public double AlwaysUpAccuracy { get; init; }
    public double MajorityBaselineAccuracy { get; init; }
    public bool PassesValidation { get; init; }
    public DateOnly TrainingStart { get; init; }
    public DateOnly TrainingEnd { get; init; }
    public DateOnly TrainingOutcomeEnd { get; init; }
    public DateOnly ValidationStart { get; init; }
    public DateOnly ValidationEnd { get; init; }
    public DateOnly DataThroughDate { get; init; }
    public string ModelVersion { get; init; } = PredictionPolicy.Version;
}

public sealed record PredictionConePoint
{
    public string Time { get; init; } = "";
    public decimal ExpectedPrice { get; init; }
    public decimal UpperBand { get; init; }
    public decimal LowerBand { get; init; }
}

public sealed record EnsembleModelVote
{
    public string ModelName { get; init; } = default!;
    public string ModelFamily { get; init; } = default!;
    public decimal Probability { get; init; }
    public decimal Weight { get; init; }
    public string Signal { get; init; } = default!;
}

public sealed record EnsembleConsensusDetails
{
    public decimal FastTreeProb { get; init; }
    public decimal FastForestProb { get; init; }
    public decimal LogisticProb { get; init; }
    public decimal ExpertProb { get; init; }

    public decimal FastTreeWeight { get; init; }
    public decimal FastForestWeight { get; init; }
    public decimal LogisticWeight { get; init; }
    public decimal ExpertWeight { get; init; }

    public decimal ConsensusAgreementPercent { get; init; }
    public string ConsensusVerdict { get; init; } = default!;
    public IReadOnlyList<EnsembleModelVote> Votes { get; init; } = Array.Empty<EnsembleModelVote>();
}

public sealed record MarketPrediction
{
    public string ModelVersion { get; init; } = "legacy";
    public DateTime GeneratedAtUtc { get; init; }
    public string PredictionStatus { get; init; } = "LEGACY";
    public string ProbabilityMeaning { get; init; } = PredictionPolicy.ProbabilityMeaning;
    public bool ProbabilityIsCalibrated { get; init; }
    public ModelTrainingMetrics? Validation { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? FeatureSnapshotJson { get; init; }
    public DateOnly Date { get; init; }
    public decimal BtcClose { get; init; }

    public string Signal { get; init; } = default!; // "BULLISH_OPPORTUNITY", "NEUTRAL_HOLD", "BEARISH_RISK"
    public decimal BullishProbability { get; init; }
    public string MarketRegime { get; init; } = default!;
    public string Confidence { get; init; } = default!;

    public IReadOnlyList<string> KeyDrivers { get; init; } = Array.Empty<string>();

    public decimal TargetPrice { get; init; }
    public decimal InvalidationPrice { get; init; }
    public decimal TargetChangePercent { get; init; }
    public decimal InvalidationChangePercent { get; init; }
    public decimal RiskRewardRatio { get; init; }
    public int HorizonDays { get; init; } = 7;
    public IReadOnlyList<PredictionConePoint> ProjectionCone { get; init; } = Array.Empty<PredictionConePoint>();

    public EnsembleConsensusDetails? Ensemble { get; init; }

    public string OutcomeStatus { get; init; } = "PENDING";
    public decimal? RealizedReturnPercent { get; init; }
    public DateOnly? ResolvedDate { get; init; }
    public decimal? ResolvedPrice { get; init; }
}

public sealed record PredictionJournalItem
{
    public string ModelVersion { get; init; } = "legacy";
    public string PredictionStatus { get; init; } = "LEGACY";
    public DateOnly Date { get; init; }
    public decimal BtcClose { get; init; }
    public string Signal { get; init; } = default!;
    public decimal BullishProbability { get; init; }
    public string MarketRegime { get; init; } = default!;
    public string Confidence { get; init; } = default!;
    public decimal TargetPrice { get; init; }
    public decimal InvalidationPrice { get; init; }
    public decimal TargetChangePercent { get; init; }
    public decimal InvalidationChangePercent { get; init; }
    public decimal RiskRewardRatio { get; init; }
    public int HorizonDays { get; init; } = 7;

    public string? ConsensusVerdict { get; init; }
    public decimal? ConsensusAgreementPercent { get; init; }

    // Outcome tracking
    public string OutcomeStatus { get; init; } = "PENDING"; // PENDING, TARGET_HIT, INVALIDATED, EXPIRED_PROFIT, EXPIRED_LOSS
    public decimal? RealizedReturnPercent { get; init; }
    public decimal? UnrealizedReturnPercent { get; init; }
    public DateOnly? ResolvedDate { get; init; }
    public decimal? ResolvedPrice { get; init; }
    public int ElapsedDays { get; init; }
    public decimal? ForwardReturn7d { get; init; }
    public bool? DirectionCorrect { get; init; }
    public string? ExitReason { get; init; }
}

public sealed record LiveJournalStats
{
    public int NoTradePredictions { get; init; }
    public int UnavailableOutcomes { get; init; }
    public int DirectionEvaluatedCount { get; init; }
    public decimal? DirectionAccuracyPercent { get; init; }
    public decimal? AlwaysUpAccuracyPercent { get; init; }
    public decimal? ProfitFactorValue { get; init; }
    public int TotalPredictions { get; init; }
    public int ResolvedPredictions { get; init; }
    public int PendingPredictions { get; init; }
    public int TargetHits { get; init; }
    public int Invalidations { get; init; }
    public int ExpiredProfits { get; init; }
    public int ExpiredLosses { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal TargetHitRatePercent { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal CumulativeReturnPercent { get; init; }
    public decimal AverageReturnPercent { get; init; }
}

public sealed record JournalEquityPoint
{
    public DateOnly Date { get; init; }
    public decimal CumulativeReturnPercent { get; init; }
    public decimal DrawdownPercent { get; init; }
}

public sealed record PredictionJournalResponse
{
    public string Mode { get; init; } = "historical"; // "historical" or "live"
    public LiveJournalStats Stats { get; init; } = default!;
    public IReadOnlyList<PredictionJournalItem> Items { get; init; } = Array.Empty<PredictionJournalItem>();
    public IReadOnlyList<JournalEquityPoint> EquityCurve { get; init; } = Array.Empty<JournalEquityPoint>();
}

