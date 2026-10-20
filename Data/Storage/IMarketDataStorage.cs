using CryptoPredictor.Prediction;

namespace CryptoPredictor.Data.Storage;

public interface IMarketDataStorage
{
    Task SaveAsync(IReadOnlyList<DailyMarketData> data, CancellationToken ct = default);
    Task<IReadOnlyList<DailyMarketData>> LoadAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DailyMarketData>> LoadRecentAsync(int days, CancellationToken ct = default);
    bool Exists();

    Task SavePredictionAsync(MarketPrediction prediction, CancellationToken ct = default);
    Task<MarketPrediction?> GetLatestPredictionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MarketPrediction>> GetPredictionHistoryAsync(int limit = 30, CancellationToken ct = default);
}
