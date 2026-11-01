namespace CryptoPredictor.Data;

public sealed record DailyFeatures
{
    public int ConsecutivePriceDays { get; init; }
    public DateOnly Date { get; init; }

    public decimal BtcReturn1d { get; init; }
    public decimal BtcReturn3d { get; init; }
    public decimal BtcReturn7d { get; init; }
    public decimal BtcReturn30d { get; init; }

    public decimal BtcDrawdownFrom30dHigh { get; init; }

    public decimal RealizedVolatility7d { get; init; }
    public decimal RealizedVolatility30d { get; init; }

    public decimal VolumeVs30dAverage { get; init; }

    // Retained only for diagnostic UI display; excluded from models due to 30-day upstream API horizon cap
    public decimal OiZScore30d { get; init; }

    public decimal FundingZScore30d { get; init; }
    public decimal CoinbasePremiumZScore30d { get; init; }

    public decimal EtfFlow7d { get; init; }
    public decimal EtfFlow30d { get; init; }
    public decimal EtfFlowZScore30d { get; init; }

    public decimal DvolZScore30d { get; init; }
    public decimal FearGreedCentered { get; init; } // [-1..+1] around neutral 50 (0 when missing)

    // Fibonacci & Wave Dynamics
    public decimal FibRetracementRatio30d { get; init; }
    public decimal DistToGoldenPocket30d { get; init; }
    public bool IsInGoldenPocket30d { get; init; }
    public decimal KaufmanEfficiencyRatio10d { get; init; } // [0..1] unsigned wave efficiency for UI subchart
    public decimal SignedKaufmanEfficiencyRatio10d { get; init; } // [-1..+1] directional impulse efficiency for models
    public decimal WeeklyTrendBias { get; init; } // Deviation from 50-day macro trend average

    // Global Macro Dynamics (Liquidity, Currency, & Benchmark Equities)
    public decimal DxyReturn7d { get; init; }
    public decimal DxyZScore30d { get; init; }
    public decimal Us10YYieldChange7d { get; init; }
    public decimal Sp500Return7d { get; init; }
    public decimal Sp500Return30d { get; init; }

    // Liquidity Pools & Liquidation Walls (Leverage sweeps & magnetic walls)
    public decimal DistToLowerLiquidityWall { get; init; }
    public decimal DistToUpperLiquidityWall { get; init; }
    public bool IsLongLiquiditySwept { get; init; }
    public bool IsShortLiquiditySwept { get; init; }
}
