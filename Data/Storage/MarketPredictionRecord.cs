namespace CryptoPredictor.Data.Storage;

public class MarketPredictionRecord
{
    public DateOnly Date { get; set; }
    public decimal BtcClose { get; set; }
    public string Signal { get; set; } = default!;
    public decimal BullishProbability { get; set; }
    public string MarketRegime { get; set; } = default!;
    public string Confidence { get; set; } = default!;
    public string KeyDriversJson { get; set; } = "[]";
    public string? EnsembleJson { get; set; }

    public decimal TargetPrice { get; set; }
    public decimal InvalidationPrice { get; set; }
    public decimal TargetChangePercent { get; set; }
    public decimal InvalidationChangePercent { get; set; }
    public decimal RiskRewardRatio { get; set; }

    public string OutcomeStatus { get; set; } = "PENDING"; // PENDING, TARGET_HIT, INVALIDATED, EXPIRED_PROFIT, EXPIRED_LOSS
    public decimal? RealizedReturnPercent { get; set; }
    public DateOnly? ResolvedDate { get; set; }
    public decimal? ResolvedPrice { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
