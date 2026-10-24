namespace CryptoPredictor.Data;

public sealed record MarketChartResponse
{
    public required IReadOnlyList<CandleDataPoint> Candles { get; init; }
    public required IReadOnlyList<ChartMarkerPoint> Markers { get; init; }
    public required IReadOnlyList<IndicatorDataPoint> Indicators { get; init; }
    public required ChartSummary Summary { get; init; }
    public FibLevelsSummary? Fibonacci { get; init; }
}

public sealed record CandleDataPoint
{
    public required string Time { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
}

public sealed record ChartMarkerPoint
{
    public required string Time { get; init; }
    public required string Position { get; init; } // "belowBar" | "aboveBar"
    public required string Color { get; init; }
    public required string Shape { get; init; } // "arrowUp" | "arrowDown"
    public required string Text { get; init; }
}

public sealed record IndicatorDataPoint
{
    public required string Time { get; init; }
    public decimal? FundingZScore30d { get; init; }
    public decimal? EtfFlow7dUsd { get; init; }
    public decimal? EtfFlowDailyUsd { get; init; }
    public decimal? EtfFlowZScore30d { get; init; }
    public decimal? OiZScore30d { get; init; }
    public decimal? DvolZScore30d { get; init; }
    public decimal? FearGreedIndex { get; init; }
    public decimal? FibRetracementRatio30d { get; init; }
    public decimal? DistToGoldenPocket30d { get; init; }
    public decimal? KaufmanEfficiencyRatio10d { get; init; }
    public decimal? WeeklyTrendBias { get; init; }
    public decimal? Dxy { get; init; }
    public decimal? DxyZScore30d { get; init; }
    public decimal? Us10YearYield { get; init; }
    public decimal? Sp500Close { get; init; }
    public decimal? Sp500Return7d { get; init; }
}

public sealed record FibLevelsSummary
{
    public decimal SwingHigh { get; init; }
    public decimal SwingLow { get; init; }
    public decimal Level0 { get; init; }
    public decimal Level236 { get; init; }
    public decimal Level382 { get; init; }
    public decimal Level500 { get; init; }
    public decimal Level618 { get; init; }
    public decimal Level650 { get; init; }
    public decimal Level786 { get; init; }
    public decimal Level1000 { get; init; }
    public decimal CurrentRatio { get; init; }
    public bool InGoldenPocket { get; init; }
    public decimal KaufmanEfficiencyRatio { get; init; }
    public string WaveRegime { get; init; } = "";
}

public sealed record ChartSummary
{
    public required string LatestDate { get; init; }
    public decimal LatestClose { get; init; }
    public decimal PriceChange24hPercent { get; init; }
    public decimal HighPeriod { get; init; }
    public decimal LowPeriod { get; init; }
    public int TotalDays { get; init; }
}
