namespace CryptoPredictor.Prediction;

public sealed record ModelTrainingMetrics
{
    public double Accuracy { get; init; }
    public double AreaUnderRocCurve { get; init; }
    public double F1Score { get; init; }
    public double PositivePrecision { get; init; }
    public double PositiveRecall { get; init; }
    public int TrainingSampleCount { get; init; }
    public double FastTreeAuc { get; init; }
    public double FastForestAuc { get; init; }
    public double LogisticAuc { get; init; }
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
}

