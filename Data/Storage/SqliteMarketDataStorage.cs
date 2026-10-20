using System.Text.Json;
using CryptoPredictor.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoPredictor.Data.Storage;

public class SqliteMarketDataStorage : IMarketDataStorage
{
    private readonly IDbContextFactory<CryptoPredictorDbContext> _contextFactory;
    private readonly string _storageDir;
    private readonly string _dbFilePath;
    private readonly ILogger<SqliteMarketDataStorage> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteMarketDataStorage(
        IDbContextFactory<CryptoPredictorDbContext> contextFactory,
        IHostEnvironment environment,
        ILogger<SqliteMarketDataStorage> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _storageDir = Path.Combine(environment.ContentRootPath, "Data", "storage");
        if (!Directory.Exists(_storageDir))
        {
            Directory.CreateDirectory(_storageDir);
        }

        _dbFilePath = Path.Combine(_storageDir, "market_data.db");
    }

    private async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized)
                return;

            await using var db = await _contextFactory.CreateDbContextAsync(ct);
            await db.Database.EnsureCreatedAsync(ct);

            // Auto-migration from legacy JSON cache if SQLite table is empty
            if (!await db.MarketData.AnyAsync(ct))
            {
                var jsonPath = Path.Combine(_storageDir, "market_data.json");
                if (File.Exists(jsonPath))
                {
                    _logger.LogInformation("SQLite database is empty. Auto-migrating historical records from JSON cache ({Path})...", jsonPath);
                    try
                    {
                        using var stream = File.OpenRead(jsonPath);
                        var records = await JsonSerializer.DeserializeAsync<List<DailyMarketData>>(stream, cancellationToken: ct);
                        if (records != null && records.Count > 0)
                        {
                            db.MarketData.AddRange(records);
                            await db.SaveChangesAsync(ct);
                            _logger.LogInformation("Successfully migrated {Count} historical market records to SQLite!", records.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to auto-migrate legacy JSON data into SQLite.");
                    }
                }
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public bool Exists()
    {
        if (!File.Exists(_dbFilePath))
            return false;

        try
        {
            using var db = _contextFactory.CreateDbContext();
            return db.MarketData.Any();
        }
        catch
        {
            return false;
        }
    }

    public async Task SaveAsync(IReadOnlyList<DailyMarketData> data, CancellationToken ct = default)
    {
        if (data == null || data.Count == 0)
            return;

        await EnsureInitializedAsync(ct);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var dates = data.Select(d => d.Date).ToHashSet();
        var existing = await db.MarketData
            .Where(d => dates.Contains(d.Date))
            .ToListAsync(ct);

        var existingMap = existing.ToDictionary(d => d.Date);

        foreach (var item in data)
        {
            if (existingMap.TryGetValue(item.Date, out var existingItem))
            {
                db.Entry(existingItem).CurrentValues.SetValues(item);
            }
            else
            {
                db.MarketData.Add(item);
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Persisted {Count} market data records to SQLite (market_data.db).", data.Count);
    }

    public async Task<IReadOnlyList<DailyMarketData>> LoadAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.MarketData
            .AsNoTracking()
            .OrderBy(d => d.Date)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DailyMarketData>> LoadRecentAsync(int days, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        if (days <= 0)
            return await LoadAsync(ct);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var recent = await db.MarketData
            .AsNoTracking()
            .OrderByDescending(d => d.Date)
            .Take(days)
            .ToListAsync(ct);

        recent.Reverse();
        return recent;
    }
}
