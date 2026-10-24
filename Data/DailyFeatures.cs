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

    // Fibonacci & Wave Dynamics (Elliott wave impulse vs correction proxy)
    public decimal FibRetracementRatio30d { get; init; } // Pozycja ceny w swingu 30d (0.0 = szczyt, 1.0 = dolek)
    public decimal DistToGoldenPocket30d { get; init; } // |FibRatio - 0.618|
    public bool IsInGoldenPocket30d { get; init; } // Czy cena znajduje sie w strefie 0.618 - 0.650
    public decimal KaufmanEfficiencyRatio10d { get; init; } // Wskaznik efektywnosci impulsu (KER > 0.55 impuls, < 0.25 korekta)
    public decimal WeeklyTrendBias { get; init; } // Odchylenie od 50-dniowej (7-tygodniowej) sredniej trendu makro

    // Global Macro Dynamics (Liquidity, Currency, & Benchmark Equities)
    public decimal DxyReturn7d { get; init; } // 7-dniowa dynamika Indeksu Dolara (DXY)
    public decimal DxyZScore30d { get; init; } // 30-dniowy Z-Score DXY (wybicie vs wyprzedanie dolara)
    public decimal Us10YYieldChange7d { get; init; } // 7-dniowa zmiana rentownosci obligacji USA 10Y (w pkt. proc.)
    public decimal Sp500Return7d { get; init; } // 7-dniowy zwrot S&P 500 (Risk-On / Risk-Off)
    public decimal Sp500Return30d { get; init; } // 30-dniowy trend S&P 500
}