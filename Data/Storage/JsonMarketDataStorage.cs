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
}
