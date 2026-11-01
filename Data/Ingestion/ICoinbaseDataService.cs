namespace CryptoPredictor.Data.Ingestion;

public interface ICoinbaseDataService
{
    Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyClosePricesAsync(int days = 300, CancellationToken ct = default);
}
