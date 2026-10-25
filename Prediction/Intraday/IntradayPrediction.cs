namespace CryptoPredictor.Prediction.Intraday;

public sealed record IntradayPrediction
{
    public DateTimeOffset Timestamp { get; init; }
    public decimal BtcClose { get; init; }
    public string Signal { get; init; } = default!; // "BULLISH_TACTICAL", "BEARISH_TACTICAL", "NEUTRAL_CHOP"
    public decimal TacticalProbability { get; init; }
    public string Confidence { get; init; } = "Moderate"; // "High", "Moderate", "Neutral"

    public decimal TargetPrice { get; init; }
    public decimal InvalidationPrice { get; init; }
    public decimal TargetChangePercent { get; init; }
    public decimal InvalidationChangePercent { get; init; }
    public decimal RiskRewardRatio { get; init; }

    public int HorizonHours { get; init; } = 24; // 6 świec 4h
    public string Session { get; init; } = default!;
    public string RawSession { get; init; } = default!;
    public string TakerAggression { get; init; } = default!;
    public decimal TakerBuySellRatio { get; init; }
    public decimal TakerAggressionZScore { get; init; }
    public decimal? OpenInterestUsd { get; init; }
    public decimal OiChange24hPct { get; init; }
    public decimal Rsi14 { get; init; }
    public decimal KaufmanEfficiencyRatio { get; init; }
    public bool IsSfpHighSwept { get; init; }
    public bool IsSfpLowSwept { get; init; }
    public IReadOnlyList<string> KeyDrivers { get; init; } = Array.Empty<string>();
}

public sealed record IntradayJournalItem
{
    public DateTimeOffset Timestamp { get; init; }
    public decimal BtcClose { get; init; }
    public string Signal { get; init; } = default!;
    public decimal TacticalProbability { get; init; }
    public decimal TargetPrice { get; init; }
    public decimal InvalidationPrice { get; init; }
    public decimal TargetChangePercent { get; init; }
    public decimal InvalidationChangePercent { get; init; }
    public string Session { get; init; } = default!;
    public string OutcomeStatus { get; init; } = "PENDING"; // "TARGET_HIT", "INVALIDATED", "EXPIRED_PROFIT", "EXPIRED_LOSS", "PENDING"
    public decimal? RealizedReturnPercent { get; init; }
    public DateTimeOffset? ResolvedTimestamp { get; init; }
    public decimal? ResolvedPrice { get; init; }
    public int ElapsedCandles { get; init; }
}

public sealed record IntradayJournalResponse
{
    public int TotalSignals { get; init; }
    public int ResolvedSignals { get; init; }
    public int TargetHits { get; init; }
    public int Invalidations { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal TargetHitRatePercent { get; init; }
    public decimal CumulativeReturnPercent { get; init; }
    public IReadOnlyList<IntradayJournalItem> Items { get; init; } = Array.Empty<IntradayJournalItem>();
}
