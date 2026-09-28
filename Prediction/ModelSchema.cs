using Microsoft.ML.Data;

namespace CryptoPredictor.Prediction;

public class ModelInput
{
    public float BtcReturn1d { get; set; }
    public float BtcReturn3d { get; set; }
    public float BtcReturn7d { get; set; }
    public float BtcReturn30d { get; set; }

    public float BtcDrawdownFrom30dHigh { get; set; }
    public float BtcDrawdownFromAth { get; set; }

    public float RealizedVolatility7d { get; set; }
    public float RealizedVolatility30d { get; set; }

    public float VolumeVs30dAverage { get; set; }

    public float OiChange1d { get; set; }
    public float OiChange7d { get; set; }
    public float OiZScore30d { get; set; }

    public float FundingZScore30d { get; set; }
    public float FundingZScore90d { get; set; }

    public float DvolZScore30d { get; set; }
    public float EtfFlow7d { get; set; }
    public float EtfFlowZScore30d { get; set; }

    public float FearGreedIndex { get; set; }
    public float CoinbasePremium { get; set; }

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
