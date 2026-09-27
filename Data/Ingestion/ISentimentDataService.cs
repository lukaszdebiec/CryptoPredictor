namespace CryptoPredictor.Data.Ingestion;

public interface ISentimentDataService
{
    Task<IReadOnlyDictionary<DateOnly, int>> GetFearAndGreedHistoryAsync(int limit = 0, CancellationToken ct = default);
}
