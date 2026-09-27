namespace CryptoPredictor.Data.Ingestion;

public interface ICoinbaseDataService
{
    Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyClosePricesAsync(CancellationToken ct = default);
}
