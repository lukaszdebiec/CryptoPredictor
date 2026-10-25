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

            // Ensure new columns exist on MarketPredictions table
            string[] columnsToAdd = new[]
            {
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

            foreach (var sql in columnsToAdd)
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync(sql, ct);
                }
                catch
                {
                    // Column already exists, safe to ignore
                }
            }

            // Backfill null values for legacy records
            try
            {
                await db.Database.ExecuteSqlRawAsync(@"
                    UPDATE ""MarketPredictions"" SET ""TargetPrice"" = '0' WHERE ""TargetPrice"" IS NULL;
                    UPDATE ""MarketPredictions"" SET ""InvalidationPrice"" = '0' WHERE ""InvalidationPrice"" IS NULL;
                    UPDATE ""MarketPredictions"" SET ""TargetChangePercent"" = '0' WHERE ""TargetChangePercent"" IS NULL;
                    UPDATE ""MarketPredictions"" SET ""InvalidationChangePercent"" = '0' WHERE ""InvalidationChangePercent"" IS NULL;
                    UPDATE ""MarketPredictions"" SET ""RiskRewardRatio"" = '2.0' WHERE ""RiskRewardRatio"" IS NULL;
                    UPDATE ""MarketPredictions"" SET ""OutcomeStatus"" = 'PENDING' WHERE ""OutcomeStatus"" IS NULL;
                ", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to backfill default values for MarketPredictions nullable columns.");
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

            if (prediction.TargetPrice > 0)
            {
                existing.TargetPrice = prediction.TargetPrice;
                existing.InvalidationPrice = prediction.InvalidationPrice;
                existing.TargetChangePercent = prediction.TargetChangePercent;
                existing.InvalidationChangePercent = prediction.InvalidationChangePercent;
                existing.RiskRewardRatio = prediction.RiskRewardRatio;
            }

            if (!string.IsNullOrWhiteSpace(prediction.OutcomeStatus))
            {
                existing.OutcomeStatus = prediction.OutcomeStatus;
                existing.RealizedReturnPercent = prediction.RealizedReturnPercent;
                existing.ResolvedDate = prediction.ResolvedDate;
                existing.ResolvedPrice = prediction.ResolvedPrice;
            }

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
                TargetPrice = prediction.TargetPrice,
                InvalidationPrice = prediction.InvalidationPrice,
                TargetChangePercent = prediction.TargetChangePercent,
                InvalidationChangePercent = prediction.InvalidationChangePercent,
                RiskRewardRatio = prediction.RiskRewardRatio,
                OutcomeStatus = prediction.OutcomeStatus,
                RealizedReturnPercent = prediction.RealizedReturnPercent,
                ResolvedDate = prediction.ResolvedDate,
                ResolvedPrice = prediction.ResolvedPrice,
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

    public async Task<PredictionJournalResponse> GetPredictionJournalAsync(int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var predictions = await db.Predictions
            .OrderByDescending(p => p.Date)
            .Take(limit)
            .ToListAsync(ct);

        if (predictions.Count == 0)
        {
            return new PredictionJournalResponse
            {
                Stats = new LiveJournalStats(),
                Items = Array.Empty<PredictionJournalItem>()
            };
        }

        var minDate = predictions.Min(p => p.Date);
        var allCandles = await db.MarketData
            .AsNoTracking()
            .Where(m => m.Date >= minDate)
            .OrderBy(m => m.Date)
            .ToListAsync(ct);

        var latestCandle = allCandles.LastOrDefault();
        var todayDate = latestCandle?.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        bool changesMade = false;
        var items = new List<PredictionJournalItem>();

        foreach (var pred in predictions)
        {
            // Ensure target and invalidation levels are present
            decimal targetPrice = pred.TargetPrice ?? 0m;
            decimal invalidationPrice = pred.InvalidationPrice ?? 0m;
            decimal targetChangePct = pred.TargetChangePercent ?? 0m;
            decimal invalidationChangePct = pred.InvalidationChangePercent ?? 0m;
            decimal rrRatio = (pred.RiskRewardRatio ?? 0m) > 0 ? pred.RiskRewardRatio!.Value : 2.0m;

            if (targetPrice <= 0 || invalidationPrice <= 0)
            {
                if (pred.Signal == "BEARISH_RISK")
                {
                    targetPrice = Math.Round(pred.BtcClose * 0.92m, 2);
                    invalidationPrice = Math.Round(pred.BtcClose * 1.04m, 2);
                    targetChangePct = -0.08m;
                    invalidationChangePct = 0.04m;
                }
                else
                {
                    targetPrice = Math.Round(pred.BtcClose * 1.08m, 2);
                    invalidationPrice = Math.Round(pred.BtcClose * 0.96m, 2);
                    targetChangePct = 0.08m;
                    invalidationChangePct = -0.04m;
                }
                pred.TargetPrice = targetPrice;
                pred.InvalidationPrice = invalidationPrice;
                pred.TargetChangePercent = targetChangePct;
                pred.InvalidationChangePercent = invalidationChangePct;
                pred.RiskRewardRatio = rrRatio;
                changesMade = true;
            }

            int elapsedDays = todayDate.DayNumber - pred.Date.DayNumber;
            string outcome = string.IsNullOrWhiteSpace(pred.OutcomeStatus) ? "PENDING" : pred.OutcomeStatus;
            decimal? realizedReturn = pred.RealizedReturnPercent;
            decimal? unrealizedReturn = null;
            DateOnly? resolvedDate = pred.ResolvedDate;
            decimal? resolvedPrice = pred.ResolvedPrice;

            if (outcome == "PENDING")
            {
                var forwardCandles = allCandles
                    .Where(c => c.Date > pred.Date && c.Date <= pred.Date.AddDays(7))
                    .OrderBy(c => c.Date)
                    .ToList();

                bool resolved = false;

                if (pred.Signal == "BULLISH_OPPORTUNITY")
                {
                    foreach (var c in forwardCandles)
                    {
                        if (c.BtcHigh >= targetPrice)
                        {
                            outcome = "TARGET_HIT";
                            realizedReturn = Math.Abs(targetChangePct);
                            resolvedDate = c.Date;
                            resolvedPrice = targetPrice;
                            resolved = true;
                            break;
                        }
                        if (c.BtcLow <= invalidationPrice)
                        {
                            outcome = "INVALIDATED";
                            realizedReturn = -Math.Abs(invalidationChangePct);
                            resolvedDate = c.Date;
                            resolvedPrice = invalidationPrice;
                            resolved = true;
                            break;
                        }
                    }

                    if (!resolved)
                    {
                        if (elapsedDays >= 7 && forwardCandles.Count > 0)
                        {
                            var lastC = forwardCandles.Last();
                            decimal ret = (lastC.BtcClose - pred.BtcClose) / pred.BtcClose;
                            outcome = ret >= 0 ? "EXPIRED_PROFIT" : "EXPIRED_LOSS";
                            realizedReturn = Math.Round(ret, 4);
                            resolvedDate = lastC.Date;
                            resolvedPrice = lastC.BtcClose;
                            resolved = true;
                        }
                        else if (latestCandle != null && latestCandle.Date > pred.Date)
                        {
                            unrealizedReturn = Math.Round((latestCandle.BtcClose - pred.BtcClose) / pred.BtcClose, 4);
                        }
                    }
                }
                else if (pred.Signal == "BEARISH_RISK")
                {
                    foreach (var c in forwardCandles)
                    {
                        if (c.BtcLow <= targetPrice)
                        {
                            outcome = "TARGET_HIT";
                            realizedReturn = Math.Abs(targetChangePct);
                            resolvedDate = c.Date;
                            resolvedPrice = targetPrice;
                            resolved = true;
                            break;
                        }
                        if (c.BtcHigh >= invalidationPrice)
                        {
                            outcome = "INVALIDATED";
                            realizedReturn = -Math.Abs(invalidationChangePct);
                            resolvedDate = c.Date;
                            resolvedPrice = invalidationPrice;
                            resolved = true;
                            break;
                        }
                    }

                    if (!resolved)
                    {
                        if (elapsedDays >= 7 && forwardCandles.Count > 0)
                        {
                            var lastC = forwardCandles.Last();
                            decimal ret = (pred.BtcClose - lastC.BtcClose) / pred.BtcClose;
                            outcome = ret >= 0 ? "EXPIRED_PROFIT" : "EXPIRED_LOSS";
                            realizedReturn = Math.Round(ret, 4);
                            resolvedDate = lastC.Date;
                            resolvedPrice = lastC.BtcClose;
                            resolved = true;
                        }
                        else if (latestCandle != null && latestCandle.Date > pred.Date)
                        {
                            unrealizedReturn = Math.Round((pred.BtcClose - latestCandle.BtcClose) / pred.BtcClose, 4);
                        }
                    }
                }
                else
                {
                    // NEUTRAL_HOLD
                    if (elapsedDays >= 7 && forwardCandles.Count > 0)
                    {
                        var lastC = forwardCandles.Last();
                        decimal ret = (lastC.BtcClose - pred.BtcClose) / pred.BtcClose;
                        outcome = Math.Abs(ret) <= 0.03m ? "EXPIRED_PROFIT" : "EXPIRED_LOSS";
                        realizedReturn = Math.Round(ret, 4);
                        resolvedDate = lastC.Date;
                        resolvedPrice = lastC.BtcClose;
                        resolved = true;
                    }
                }

                if (resolved)
                {
                    pred.OutcomeStatus = outcome;
                    pred.RealizedReturnPercent = realizedReturn;
                    pred.ResolvedDate = resolvedDate;
                    pred.ResolvedPrice = resolvedPrice;
                    changesMade = true;
                }
            }

            string? verdict = null;
            decimal? agreement = null;
            if (!string.IsNullOrWhiteSpace(pred.EnsembleJson))
            {
                try
                {
                    var ens = JsonSerializer.Deserialize<EnsembleConsensusDetails>(pred.EnsembleJson);
                    if (ens != null)
                    {
                        verdict = ens.ConsensusVerdict;
                        agreement = ens.ConsensusAgreementPercent;
                    }
                }
                catch { }
            }

            items.Add(new PredictionJournalItem
            {
                Date = pred.Date,
                BtcClose = pred.BtcClose,
                Signal = pred.Signal,
                BullishProbability = pred.BullishProbability,
                MarketRegime = pred.MarketRegime,
                Confidence = pred.Confidence,
                TargetPrice = targetPrice,
                InvalidationPrice = invalidationPrice,
                TargetChangePercent = targetChangePct,
                InvalidationChangePercent = invalidationChangePct,
                RiskRewardRatio = rrRatio,
                HorizonDays = 7,
                ConsensusVerdict = verdict,
                ConsensusAgreementPercent = agreement,
                OutcomeStatus = outcome,
                RealizedReturnPercent = realizedReturn,
                UnrealizedReturnPercent = unrealizedReturn,
                ResolvedDate = resolvedDate,
                ResolvedPrice = resolvedPrice,
                ElapsedDays = Math.Min(elapsedDays, 7)
            });
        }

        if (changesMade)
        {
            await db.SaveChangesAsync(ct);
        }

        var resolvedItems = items.Where(i => i.OutcomeStatus != "PENDING").ToList();
        int resolvedCount = resolvedItems.Count;
        int pendingCount = items.Count - resolvedCount;

        int targetHits = resolvedItems.Count(i => i.OutcomeStatus == "TARGET_HIT");
        int invalidations = resolvedItems.Count(i => i.OutcomeStatus == "INVALIDATED");
        int expiredProfits = resolvedItems.Count(i => i.OutcomeStatus == "EXPIRED_PROFIT");
        int expiredLosses = resolvedItems.Count(i => i.OutcomeStatus == "EXPIRED_LOSS");

        int winningTrades = targetHits + expiredProfits;
        decimal winRate = resolvedCount > 0 ? Math.Round((decimal)winningTrades / resolvedCount * 100m, 1) : 0m;
        decimal targetHitRate = resolvedCount > 0 ? Math.Round((decimal)targetHits / resolvedCount * 100m, 1) : 0m;

        decimal totalGains = resolvedItems.Where(i => (i.RealizedReturnPercent ?? 0m) > 0).Sum(i => i.RealizedReturnPercent ?? 0m);
        decimal totalLosses = Math.Abs(resolvedItems.Where(i => (i.RealizedReturnPercent ?? 0m) < 0).Sum(i => i.RealizedReturnPercent ?? 0m));

        decimal profitFactor = totalLosses > 0 ? Math.Round(totalGains / totalLosses, 2) : (totalGains > 0 ? 99m : 1m);
        decimal cumulativeReturn = Math.Round(resolvedItems.Sum(i => i.RealizedReturnPercent ?? 0m) * 100m, 1);
        decimal avgReturn = resolvedCount > 0 ? Math.Round(cumulativeReturn / resolvedCount, 2) : 0m;

        var stats = new LiveJournalStats
        {
            TotalPredictions = items.Count,
            ResolvedPredictions = resolvedCount,
            PendingPredictions = pendingCount,
            TargetHits = targetHits,
            Invalidations = invalidations,
            ExpiredProfits = expiredProfits,
            ExpiredLosses = expiredLosses,
            WinRatePercent = winRate,
            TargetHitRatePercent = targetHitRate,
            ProfitFactor = profitFactor,
            CumulativeReturnPercent = cumulativeReturn,
            AverageReturnPercent = avgReturn
        };

        return new PredictionJournalResponse
        {
            Stats = stats,
            Items = items
        };
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

