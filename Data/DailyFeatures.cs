namespace CryptoPredictor.Data;

public sealed record DailyFeatures
{
    public DateOnly Date { get; init; } // available

    public decimal BtcReturn1d { get; init; } // available (wyliczane z BtcClose)
    public decimal BtcReturn3d { get; init; } // available (wyliczane z BtcClose)
    public decimal BtcReturn7d { get; init; } // available (wyliczane z BtcClose)
    public decimal BtcReturn30d { get; init; } // available (wyliczane z BtcClose)

    public decimal BtcDrawdownFrom30dHigh { get; init; } // available (wyliczane z BtcHigh / BtcClose)
    public decimal BtcDrawdownFrom90dHigh { get; init; } // available (wyliczane z BtcHigh / BtcClose)
    public decimal BtcDrawdownFromAth { get; init; } // available (wyliczane z BtcHigh / BtcClose)

    public decimal RealizedVolatility7d { get; init; } // available (wyliczane z odchylenia standardowego stóp zwrotu)
    public decimal RealizedVolatility30d { get; init; } // available (wyliczane z odchylenia standardowego stóp zwrotu)

    public decimal VolumeVs30dAverage { get; init; } // available (wyliczane z BtcVolume)

    public decimal OiChange1d { get; init; } // available (wyliczane z FuturesOpenInterestUsd)
    public decimal OiChange7d { get; init; } // available (wyliczane z FuturesOpenInterestUsd)
    public decimal OiZScore30d { get; init; } // available (wyliczane z FuturesOpenInterestUsd)

    public decimal FundingZScore30d { get; init; } // available (wyliczane z FundingRate)
    public decimal FundingZScore90d { get; init; } // available (wyliczane z FundingRate)

    public decimal LiquidationsZScore30d { get; init; } // not available (wymaga płatnych danych historycznych likwidacji)

    public decimal EtfFlow7d { get; init; } // available (wyliczane z BitcoinEtfNetFlowUsd)
    public decimal EtfFlow30d { get; init; } // available (wyliczane z BitcoinEtfNetFlowUsd)
    public decimal EtfFlowZScore30d { get; init; } // available (wyliczane z BitcoinEtfNetFlowUsd)

    public decimal DvolZScore30d { get; init; } // available (wyliczane z BtcDvol)
    public decimal SkewZScore30d { get; init; } // not available (wymaga płatnych danych historycznych 25d skew)

    // etc...
}