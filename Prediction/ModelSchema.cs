using Microsoft.ML.Data;

namespace CryptoPredictor.Prediction;

/// <summary>
/// Slimmed, orthogonal feature input for directional classification (direction-v1.1).
/// Redundant overlapping returns, unsigned KER, and 30-day-capped Open Interest are excluded.
/// </summary>
public class ModelInput
{
    // 1. Structure & Impulse
    public float SignedKaufmanEfficiencyRatio10d { get; set; }
    public float WeeklyTrendBias { get; set; }
    public float BtcDrawdownFrom30dHigh { get; set; }

    // 2. Volatility & Volume Exhaustion
    public float RealizedVolatility30d { get; set; }
    public float VolumeVs30dAverage { get; set; }

    // 3. Spot & Institutional Flows
    public float CoinbasePremiumZScore30d { get; set; }
    public float EtfFlowZScore30d { get; set; }

    // 4. Derivatives Crowding & Sentiment
    public float FundingZScore30d { get; set; }
    public float FearGreedCentered { get; set; }

    // 5. Macro Liquidity
    public float DxyZScore30d { get; set; }

    [ColumnName("Label")]
    public bool Label { get; set; }
}

public class ModelOutput
{
    [ColumnName("PredictedLabel")]
    public bool PredictedLabel { get; set; }

    public float Probability { get; set; }

    public float Score { get; set; }
}
