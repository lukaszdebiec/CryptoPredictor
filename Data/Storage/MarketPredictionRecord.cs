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
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
