using System.Text.Json;

namespace CryptoPredictor.Data.Storage;

public class JsonMarketDataStorage : IMarketDataStorage
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public JsonMarketDataStorage(IHostEnvironment environment)
    {
        var storageDir = Path.Combine(environment.ContentRootPath, "Data", "storage");
        if (!Directory.Exists(storageDir))
        {
            Directory.CreateDirectory(storageDir);
        }

        _filePath = Path.Combine(storageDir, "market_data.json");
    }

    public bool Exists() => File.Exists(_filePath);

    public async Task SaveAsync(IReadOnlyList<DailyMarketData> data, CancellationToken ct = default)
    {
        using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, data, JsonOptions, ct);
    }

    public async Task<IReadOnlyList<DailyMarketData>> LoadAsync(CancellationToken ct = default)
    {
        if (!Exists())
            return Array.Empty<DailyMarketData>();

        using var stream = File.OpenRead(_filePath);
        var list = await JsonSerializer.DeserializeAsync<List<DailyMarketData>>(stream, JsonOptions, ct);
        return list ?? (IReadOnlyList<DailyMarketData>)Array.Empty<DailyMarketData>();
    }

    public async Task<IReadOnlyList<DailyMarketData>> LoadRecentAsync(int days, CancellationToken ct = default)
    {
        var all = await LoadAsync(ct);
        if (days <= 0 || all.Count <= days)
            return all;

        return all.TakeLast(days).ToList();
    }

    public Task SavePredictionAsync(CryptoPredictor.Prediction.MarketPrediction prediction, CancellationToken ct = default) => Task.CompletedTask;
    public Task<CryptoPredictor.Prediction.MarketPrediction?> GetLatestPredictionAsync(CancellationToken ct = default) => Task.FromResult<CryptoPredictor.Prediction.MarketPrediction?>(null);
    public Task<IReadOnlyList<CryptoPredictor.Prediction.MarketPrediction>> GetPredictionHistoryAsync(int limit = 30, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CryptoPredictor.Prediction.MarketPrediction>>(Array.Empty<CryptoPredictor.Prediction.MarketPrediction>());
    public Task<CryptoPredictor.Prediction.PredictionJournalResponse> GetPredictionJournalAsync(int limit = 50, CancellationToken ct = default) =>
        Task.FromResult(new CryptoPredictor.Prediction.PredictionJournalResponse
        {
            Stats = new CryptoPredictor.Prediction.LiveJournalStats(),
            Items = Array.Empty<CryptoPredictor.Prediction.PredictionJournalItem>()
        });
}


