namespace CryptoPredictor.Prediction.Confluence;

public static class ConfluencePolicy
{
    public const string Version = "confluence-v1.0";
    public const int WarmupDays = 50;
    public const double DriftScale7d = 0.55;
    public const double DriftScale30d = 0.65;
    public const double BandZ80 = 1.2815515655446004; // Nominal 10%–90% normal quantile
}

public sealed record ConfluencePillarScore
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public bool IsAvailable { get; init; }
    public double RawScore { get; init; } // [-1..+1]
    public double NominalWeight { get; init; }
    public double EffectiveWeight { get; init; }
    public double WeightedPoints { get; init; } // Contribution to [-100..+100]
    public required string Summary { get; init; }
}

public sealed record ConfluencePoint
{
    public required string Time { get; init; }
    public int HorizonDays { get; init; }
    public decimal ProjectedPrice { get; init; }
    public decimal LowerBandPrice { get; init; }
    public decimal UpperBandPrice { get; init; }
    public double ProjectedReturnPercent { get; init; }
}

public sealed record CycleAnalogPoint
{
    public required string Time { get; init; }
    public int HorizonDays { get; init; }
    public decimal AnalogPrice { get; init; }
    public double AnalogReturnPercent { get; init; }
}

public sealed record ConfluenceCycleAnalog
{
    public bool Available { get; init; }
    public required string CyclePhaseLabel { get; init; }
    public IReadOnlyList<int> AnalogYears { get; init; } = [];
    public IReadOnlyList<int> MatchedCycleYears { get; init; } = [];
    public double ExpectedReturnPercent { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<CycleAnalogPoint> Points { get; init; } = [];
}

public sealed record ConfluenceForecast
{
    public string ModelVersion { get; init; } = ConfluencePolicy.Version;
    public required string Status { get; init; } // READY | INSUFFICIENT_DATA
    public required string Message { get; init; }
    public DateOnly? AsOfDate { get; init; }
    public int HorizonDays { get; init; }
    public decimal? ReferenceClose { get; init; }
    public double ConfluenceScore { get; init; } // [-100..+100]
    public required string Bias { get; init; } // BULLISH | BEARISH | NEUTRAL
    public required string BiasLabel { get; init; }
    public double DailyVolatility { get; init; }
    public double ProjectedChangePercent { get; init; }
    public decimal? ProjectedEndPrice { get; init; }
    public IReadOnlyList<ConfluencePillarScore> Pillars { get; init; } = [];
    public IReadOnlyList<ConfluencePoint> Points { get; init; } = [];
    public ConfluenceCycleAnalog? CycleAnalog { get; init; }
}

