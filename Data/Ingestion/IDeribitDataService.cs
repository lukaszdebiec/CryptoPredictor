namespace CryptoPredictor.Data.Ingestion;

public interface IDeribitDataService
{
    Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyDvolHistoryAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct = default);
}
