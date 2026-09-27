namespace CryptoPredictor.Data.Ingestion;

public interface IMarketDataAggregator
{
    Task<IReadOnlyList<DailyMarketData>> FetchAndAggregateAsync(int days = 180, bool saveToStorage = true, CancellationToken ct = default);
}
