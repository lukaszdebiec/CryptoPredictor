using System.Security.Cryptography;
using System.Text.Json;
using CryptoPredictor.Data;
using CryptoPredictor.Data.Ingestion;
using CryptoPredictor.Prediction.Trajectory;

if (args.Length < 2 || args[0] is not ("download" or "evaluate"))
{
    Console.Error.WriteLine("Usage: download <output.json> [--days=1825] [--source=BinanceUS] | evaluate <input.json> [--step=30]");
    return 2;
}
string Option(string name, string fallback) => args.FirstOrDefault(a => a.StartsWith("--" + name + "=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? fallback;
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
if (args[0] == "download")
{
    if (!int.TryParse(Option("days", "1825"), out int days) || days < 1 || days > 3650 ||
        !Enum.TryParse<PriceHistorySource>(Option("source", "BinanceUS"), true, out var source) || !Enum.IsDefined(source))
    { Console.Error.WriteLine("Invalid source or days (1–3650)."); return 2; }
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var end = DateOnly.FromDateTime(DateTime.UtcNow);
        var downloaded = await new PriceHistoryService(client).DownloadAsync(source, end.AddDays(-days), end);
        if (downloaded.Candles.Count == 0) throw new InvalidDataException("The exchange returned no closed candles.");
        string output = Path.GetFullPath(args[1]), temp = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(downloaded, jsonOptions)); File.Move(temp, output, overwrite: true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        Console.Error.WriteLine($"Saved {downloaded.Candles.Count} candles from {downloaded.Source}; missing dates: {downloaded.MissingDates.Count}. No application database was modified.");
        return 0;
    }
    catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or TaskCanceledException or JsonException or FormatException)
    { Console.Error.WriteLine($"Price history download failed: {ex.Message}"); return 1; }
}

if (!int.TryParse(Option("step", "30"), out int step) || step < 7 || step > 365)
{ Console.Error.WriteLine("Evaluation step must be between 7 and 365 days."); return 2; }
var bytes = await File.ReadAllBytesAsync(args[1]);
using var document = JsonDocument.Parse(bytes);
var root = document.RootElement;
JsonElement Field(string name) => root.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
var array = root.ValueKind == JsonValueKind.Array ? root : Field("Candles");
var raw = array.Deserialize<List<DailyMarketData>>(jsonOptions) ?? throw new InvalidDataException("Expected a daily candle array or a price-history export.");
string sourceLabel = root.ValueKind == JsonValueKind.Array ? "UserFile" : Field("Source").ValueKind == JsonValueKind.String ? Field("Source").GetString()! : "UserFile";
string datasetKind = root.ValueKind == JsonValueKind.Array ? "UserProvided" : Field("DatasetKind").ValueKind == JsonValueKind.String ? Field("DatasetKind").GetString()! : "UserProvided";
var today = DateOnly.FromDateTime(DateTime.UtcNow);
var data = raw.Where(b => b.Date < today).OrderBy(b => b.Date).ToList();
var model = new PriceTrajectoryService();
var latest = model.Forecast(data, sourceLabel, "BTCUSDT");
var outcomes = Enumerable.Range(1, 7).ToDictionary(h => h, _ => new List<(double Error, double Baseline, double SquaredError, double Pinball, bool Covered)>());
var origins = new List<string>();
if (latest.Points.Count == 8)
{
    int first = TrajectoryPolicy.Warmup - 1 + TrajectoryPolicy.MinimumSamples - 1 + 7;
    for (int i = first; i + 7 < data.Count; i += step)
    {
        var forecast = model.Forecast(data, sourceLabel, "BTCUSDT", data[i].Date);
        if (forecast.Points.Count != 8) continue;
        origins.Add(data[i].Date.ToString("yyyy-MM-dd"));
        foreach (var point in forecast.Points.Skip(1))
        {
            double actual = Math.Log((double)data[i + point.HorizonDays].BtcClose / (double)data[i].BtcClose);
            double low = Math.Log((double)point.LowerPrice / (double)data[i].BtcClose), high = Math.Log((double)point.UpperPrice / (double)data[i].BtcClose);
            double error = actual - point.MedianLogReturn;
            double loss = (PriceTrajectoryService.Pinball(actual, low, .1) + PriceTrajectoryService.Pinball(actual, point.MedianLogReturn, .5) + PriceTrajectoryService.Pinball(actual, high, .9)) / 3;
            outcomes[point.HorizonDays].Add((Math.Abs(error), Math.Abs(actual), error * error, loss, actual >= low && actual <= high));
        }
    }
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    ModelVersion = TrajectoryPolicy.Version, DatasetKind = datasetKind, SourceDeclaredByInput = sourceLabel,
    SnapshotSha256 = Convert.ToHexString(SHA256.HashData(bytes)), ClosedObservations = data.Count, StepDays = step,
    LatestForecast = latest, RollingOrigins = origins,
    RollingEvaluation = outcomes.Select(pair => new
    {
        HorizonDays = pair.Key, Count = pair.Value.Count,
        MaeLogReturn = pair.Value.Count > 0 ? pair.Value.Average(v => v.Error) : (double?)null,
        NoChangeMaeLogReturn = pair.Value.Count > 0 ? pair.Value.Average(v => v.Baseline) : (double?)null,
        RmseLogReturn = pair.Value.Count > 0 ? Math.Sqrt(pair.Value.Average(v => v.SquaredError)) : (double?)null,
        MeanPinballLoss = pair.Value.Count > 0 ? pair.Value.Average(v => v.Pinball) : (double?)null,
        IntervalCoverage = pair.Value.Count > 0 ? pair.Value.Average(v => v.Covered ? 1d : 0d) : (double?)null
    }),
    Limitations = new[] { "Synthetic or previously inspected inputs cannot establish real-world predictive advantage.",
        "Each rolling forecast was trained and calibrated only on prices known at its origin. Periods overlap in their training history.",
        "Intervals are marginal per horizon, not a simultaneous band for the entire path; uncertainty coverage can change across regimes.",
        "Raw trajectory estimates, including those without holdout advantage, are evaluated. No trading profitability is inferred." }
}, jsonOptions));
return 0;
