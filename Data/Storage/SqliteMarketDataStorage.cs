using System.Text.Json;
using CryptoPredictor.Data;
using CryptoPredictor.Prediction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

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

            // Ensure new columns exist on MarketPredictions table
            string[] columnsToAdd = new[]
            {
                @"ALTER TABLE ""MarketPredictions"" ADD COLUMN ""PredictionJson"" TEXT NULL;",
                @"ALTER TABLE ""MarketPredictions"" ADD COLUMN ""FeatureSnapshotJson"" TEXT NULL;",
                @"ALTER TABLE ""MarketPredictions"" ADD COLUMN ""EnsembleJson"" TEXT NULL;",
                @"ALTER TABLE ""MarketPredictions"" ADD COLUMN ""TargetPrice"" TEXT NULL;",
                @"ALTER TABLE ""MarketPredictions"" ADD COLUMN ""InvalidationPrice"" TEXT NULL;",
                @"ALTER TABLE ""MarketPredictions"" ADD COLUMN ""TargetChangePercent"" TEXT NULL;",
                @"ALTER TABLE ""MarketPredictions"" ADD COLUMN ""InvalidationChangePercent"" TEXT NULL;",
                @"ALTER TABLE ""MarketPredictions"" ADD COLUMN ""RiskRewardRatio"" TEXT NULL;",
                @"ALTER TABLE ""MarketPredictions"" ADD COLUMN ""OutcomeStatus"" TEXT NULL;",
                @"ALTER TABLE ""MarketPredictions"" ADD COLUMN ""RealizedReturnPercent"" TEXT NULL;",
                @"ALTER TABLE ""MarketPredictions"" ADD COLUMN ""ResolvedDate"" TEXT NULL;",
                @"ALTER TABLE ""MarketPredictions"" ADD COLUMN ""ResolvedPrice"" TEXT NULL;"
            };

            await db.Database.OpenConnectionAsync(ct);
            var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('MarketPredictions')";
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) existingColumns.Add(reader.GetString(1));
            }
            foreach (var sql in columnsToAdd)
                if (!existingColumns.Contains(sql.Split('"')[3]))
                    await db.Database.ExecuteSqlRawAsync(sql, ct);

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
                            var today = DateOnly.FromDateTime(DateTime.UtcNow);
                            records = records.Where(d => d.Date < today).ToList();
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
        ArgumentNullException.ThrowIfNull(data);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        data = data.Where(d => d.Date < today).ToList();
        if (data.Count == 0)
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await db.MarketData.Where(d => d.Date < today)
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var recent = await db.MarketData.Where(d => d.Date < today)
            .AsNoTracking()
            .OrderByDescending(d => d.Date)
            .Take(days)
            .ToListAsync(ct);

        recent.Reverse();
        return recent;
    }

    public async Task<MarketPrediction> SavePredictionAsync(MarketPrediction prediction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        if (prediction.Date >= DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("A daily prediction must refer to a closed UTC candle.", nameof(prediction));

        await EnsureInitializedAsync(ct);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var existing = await db.Predictions.FirstOrDefaultAsync(p => p.Date == prediction.Date, ct);
        var driversJson = JsonSerializer.Serialize(prediction.KeyDrivers);
        var ensembleJson = prediction.Ensemble != null ? JsonSerializer.Serialize(prediction.Ensemble) : null;

        if (existing != null)
        {
            _logger.LogInformation("Preserving the first issued forecast for {Date} (including legacy forecasts).", prediction.Date);
            return MapToPrediction(existing);
        }
        db.Predictions.Add(new MarketPredictionRecord
        {
            Date = prediction.Date, BtcClose = prediction.BtcClose, Signal = prediction.Signal,
            BullishProbability = prediction.BullishProbability, MarketRegime = prediction.MarketRegime,
            Confidence = prediction.Confidence, KeyDriversJson = driversJson, EnsembleJson = ensembleJson,
            PredictionJson = JsonSerializer.Serialize(prediction), FeatureSnapshotJson = prediction.FeatureSnapshotJson,
            TargetPrice = prediction.TargetPrice, InvalidationPrice = prediction.InvalidationPrice,
            TargetChangePercent = prediction.TargetChangePercent, InvalidationChangePercent = prediction.InvalidationChangePercent,
            RiskRewardRatio = prediction.RiskRewardRatio, OutcomeStatus = prediction.OutcomeStatus,
            CreatedAtUtc = prediction.GeneratedAtUtc == default ? DateTime.UtcNow : prediction.GeneratedAtUtc
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 })
        {
            _logger.LogInformation("Another request already saved the forecast for {Date}; preserving it.", prediction.Date);
            db.ChangeTracker.Clear();
            return MapToPrediction(await db.Predictions.AsNoTracking().SingleAsync(p => p.Date == prediction.Date, ct));
        }
        return prediction;
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

    public async Task<PredictionJournalResponse> GetPredictionJournalAsync(int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var records = await db.Predictions.AsNoTracking().OrderByDescending(p => p.Date).ToListAsync(ct);
        // Legacy forecasts have different label/execution semantics. Preserve them in /history,
        // but do not silently combine their performance with direction-v1.
        var predictions = records.Select(MapToPrediction).Where(p => p.ModelVersion == PredictionPolicy.Version)
            .Take(Math.Clamp(limit, 1, 1000)).ToList();
        var candles = await LoadAsync(ct);
        return PredictionOutcomeResolver.Journal(predictions.Select(p => PredictionOutcomeResolver.Resolve(p, candles)), "live");
    }

    private static MarketPrediction MapToPrediction(MarketPredictionRecord r)
    {

        if (!string.IsNullOrWhiteSpace(r.PredictionJson))
        {
            var snapshot = JsonSerializer.Deserialize<MarketPrediction>(r.PredictionJson)
                ?? throw new InvalidDataException("Stored prediction snapshot is invalid.");
            return snapshot with { FeatureSnapshotJson = r.FeatureSnapshotJson };
        }
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
            ModelVersion = "legacy", PredictionStatus = "LEGACY", GeneratedAtUtc = r.CreatedAtUtc,
            ProbabilityMeaning = "Legacy mixed barrier/return target; not comparable to direction-v1.",
            Date = r.Date,
            BtcClose = r.BtcClose,
            Signal = r.Signal,
            BullishProbability = r.BullishProbability,
            MarketRegime = r.MarketRegime,
            Confidence = r.Confidence,
            KeyDrivers = drivers,
            Ensemble = ensemble,
            TargetPrice = r.TargetPrice ?? 0m,
            InvalidationPrice = r.InvalidationPrice ?? 0m,
            TargetChangePercent = r.TargetChangePercent ?? 0m,
            InvalidationChangePercent = r.InvalidationChangePercent ?? 0m,
            RiskRewardRatio = (r.RiskRewardRatio ?? 0m) > 0 ? r.RiskRewardRatio!.Value : 2.0m,
            OutcomeStatus = string.IsNullOrWhiteSpace(r.OutcomeStatus) ? "PENDING" : r.OutcomeStatus,
            RealizedReturnPercent = r.RealizedReturnPercent,
            ResolvedDate = r.ResolvedDate,
            ResolvedPrice = r.ResolvedPrice
        };
    }
}

