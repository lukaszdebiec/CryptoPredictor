namespace CryptoPredictor.Data.Storage;

public interface IMarketDataStorage
{
    Task SaveAsync(IReadOnlyList<DailyMarketData> data, CancellationToken ct = default);
    Task<IReadOnlyList<DailyMarketData>> LoadAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DailyMarketData>> LoadRecentAsync(int days, CancellationToken ct = default);
    bool Exists();
}
