namespace CryptoPredictor.Data;

public sealed record DailyFeatures
{
    public DateOnly Date { get; init; }

    public decimal BtcReturn1d { get; init; }
    public decimal BtcReturn3d { get; init; }
    public decimal BtcReturn7d { get; init; }
    public decimal BtcReturn30d { get; init; }

    public decimal BtcDrawdownFrom30dHigh { get; init; }
    public decimal BtcDrawdownFrom90dHigh { get; init; }
    public decimal BtcDrawdownFromAth { get; init; }

    public decimal RealizedVolatility7d { get; init; }
    public decimal RealizedVolatility30d { get; init; }

    public decimal VolumeVs30dAverage { get; init; }

    public decimal OiChange1d { get; init; }
    public decimal OiChange7d { get; init; }
    public decimal OiZScore30d { get; init; }

    public decimal FundingZScore30d { get; init; }
    public decimal FundingZScore90d { get; init; }

    public decimal LiquidationsZScore30d { get; init; }

    public decimal EtfFlow7d { get; init; }
    public decimal EtfFlow30d { get; init; }
    public decimal EtfFlowZScore30d { get; init; }

    public decimal DvolZScore30d { get; init; }
    public decimal SkewZScore30d { get; init; }

    // etc...
}