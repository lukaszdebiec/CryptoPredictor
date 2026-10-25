using System.Text.Json;
using CryptoPredictor.Data;
using CryptoPredictor.Prediction;
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

            // Ensure MarketPredictions table exists even on pre-existing database files
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""MarketPredictions"" (
                    ""Date"" TEXT NOT NULL CONSTRAINT ""PK_MarketPredictions"" PRIMARY KEY,
                    ""BtcClose"" TEXT NOT NULL,
                    ""Signal"" TEXT NOT NULL,
                    ""BullishProbability"" TEXT NOT NULL,
                    ""MarketRegime"" TEXT NOT NULL,
                    ""Confidence"" TEXT NOT NULL,
                    ""KeyDriversJson"" TEXT NOT NULL,
                    ""CreatedAtUtc"" TEXT NOT NULL
                );", ct);

            // Ensure EnsembleJson column exists on MarketPredictions table
            try
            {
                await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""MarketPredictions"" ADD COLUMN ""EnsembleJson"" TEXT NULL;", ct);
            }
            catch
            {
                // Column already exists, safe to ignore
            }

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

    public async Task SavePredictionAsync(MarketPrediction prediction, CancellationToken ct = default)
    {
        if (prediction == null)
            return;

        await EnsureInitializedAsync(ct);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var existing = await db.Predictions.FirstOrDefaultAsync(p => p.Date == prediction.Date, ct);
        var driversJson = JsonSerializer.Serialize(prediction.KeyDrivers);
        var ensembleJson = prediction.Ensemble != null ? JsonSerializer.Serialize(prediction.Ensemble) : null;

        if (existing != null)
        {
            existing.BtcClose = prediction.BtcClose;
            existing.Signal = prediction.Signal;
            existing.BullishProbability = prediction.BullishProbability;
            existing.MarketRegime = prediction.MarketRegime;
            existing.Confidence = prediction.Confidence;
            existing.KeyDriversJson = driversJson;
            existing.EnsembleJson = ensembleJson;
            existing.CreatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            db.Predictions.Add(new MarketPredictionRecord
            {
                Date = prediction.Date,
                BtcClose = prediction.BtcClose,
                Signal = prediction.Signal,
                BullishProbability = prediction.BullishProbability,
                MarketRegime = prediction.MarketRegime,
                Confidence = prediction.Confidence,
                KeyDriversJson = driversJson,
                EnsembleJson = ensembleJson,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Saved market prediction for {Date} to SQLite (Signal: {Signal}, Prob: {Prob:P1}).",
            prediction.Date, prediction.Signal, prediction.BullishProbability);
    }

    public async Task<MarketPrediction?> GetLatestPredictionAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var record = await db.Predictions
            .AsNoTracking()
            .OrderByDescending(p => p.Date)
            .FirstOrDefaultAsync(ct);

        return record == null ? null : MapToPrediction(record);
    }

    public async Task<IReadOnlyList<MarketPrediction>> GetPredictionHistoryAsync(int limit = 30, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var records = await db.Predictions
            .AsNoTracking()
            .OrderByDescending(p => p.Date)
            .Take(limit)
            .ToListAsync(ct);

        return records.Select(MapToPrediction).ToList();
    }

    private static MarketPrediction MapToPrediction(MarketPredictionRecord r)
    {
        IReadOnlyList<string> drivers;
        try
        {
            drivers = JsonSerializer.Deserialize<List<string>>(r.KeyDriversJson) ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch
        {
            drivers = Array.Empty<string>();
        }

        EnsembleConsensusDetails? ensemble = null;
        if (!string.IsNullOrWhiteSpace(r.EnsembleJson))
        {
            try
            {
                ensemble = JsonSerializer.Deserialize<EnsembleConsensusDetails>(r.EnsembleJson);
            }
            catch
            {
                ensemble = null;
            }
        }

        return new MarketPrediction
        {
            Date = r.Date,
            BtcClose = r.BtcClose,
            Signal = r.Signal,
            BullishProbability = r.BullishProbability,
            MarketRegime = r.MarketRegime,
            Confidence = r.Confidence,
            KeyDrivers = drivers,
            Ensemble = ensemble
        };
    }
}

