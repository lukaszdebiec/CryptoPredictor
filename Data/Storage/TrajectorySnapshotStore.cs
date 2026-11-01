using System.Text.Json;
using CryptoPredictor.Prediction.Trajectory;
using Microsoft.EntityFrameworkCore;

namespace CryptoPredictor.Data.Storage;

public interface ITrajectorySnapshotStore
{
    Task<TrajectoryForecast?> LoadAsync(DateOnly date, string source, string symbol, CancellationToken ct = default);
    Task<TrajectoryForecast> SaveAsync(TrajectoryForecast forecast, CancellationToken ct = default);
}

/// <summary>Independent immutable snapshots, keyed by origin, market and trajectory specification.</summary>
public sealed class TrajectorySnapshotStore(IDbContextFactory<CryptoPredictorDbContext> factory) : ITrajectorySnapshotStore
{
    private const string CreateSql = """
        CREATE TABLE IF NOT EXISTS TrajectorySnapshots (
          Date TEXT NOT NULL, Source TEXT NOT NULL, Symbol TEXT NOT NULL, ModelVersion TEXT NOT NULL,
          SnapshotJson TEXT NOT NULL, PRIMARY KEY(Date, Source, Symbol, ModelVersion)
        );
        """;

    public async Task<TrajectoryForecast?> LoadAsync(DateOnly date, string source, string symbol, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.ExecuteSqlRawAsync(CreateSql, ct);
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT SnapshotJson FROM TrajectorySnapshots WHERE Date=@date AND Source=@source AND Symbol=@symbol AND ModelVersion=@version";
        foreach (var (name, value) in new[] { ("@date", date.ToString("yyyy-MM-dd")), ("@source", source), ("@symbol", symbol), ("@version", TrajectoryPolicy.Version) })
        { var p = command.CreateParameter(); p.ParameterName = name; p.Value = value; command.Parameters.Add(p); }
        var json = await command.ExecuteScalarAsync(ct) as string;
        return json == null ? null : JsonSerializer.Deserialize<TrajectoryForecast>(json) ?? throw new InvalidDataException("Invalid trajectory snapshot.");
    }

    public async Task<TrajectoryForecast> SaveAsync(TrajectoryForecast forecast, CancellationToken ct = default)
    {
        // A missing-history response is not an issued model forecast. Permit training after backfill on the same origin.
        if (forecast.AsOfDate == null || forecast.Points.Count != 8) return forecast;
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.ExecuteSqlRawAsync(CreateSql, ct);
        string date = forecast.AsOfDate.Value.ToString("yyyy-MM-dd"), json = JsonSerializer.Serialize(forecast);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO TrajectorySnapshots(Date,Source,Symbol,ModelVersion,SnapshotJson) VALUES ({date},{forecast.Source},{forecast.Symbol},{forecast.ModelVersion},{json})", ct);
        return await LoadAsync(forecast.AsOfDate.Value, forecast.Source, forecast.Symbol, ct)
            ?? throw new InvalidDataException("Trajectory snapshot save could not be confirmed.");
    }
}
