using System.Security.Cryptography;
using System.Text.Json;
using CryptoPredictor.Backtest;
using CryptoPredictor.Data;
using CryptoPredictor.Features;
using CryptoPredictor.Prediction;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/Evaluate -- <market-data.json>. Reads data only; never writes the application database.");
    return 2;
}
var bytes = await File.ReadAllBytesAsync(args[0]);
var raw = JsonSerializer.Deserialize<List<DailyMarketData>>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidDataException("Expected an array of daily market observations.");
var data = raw.Where(d => d.Date < DateOnly.FromDateTime(DateTime.UtcNow)).OrderBy(d => d.Date).ToList();
if (data.Count == 0) throw new InvalidDataException("No closed daily observations.");
var features = new FeatureCalculator().Calculate(data);
var detector = new TurningPointDetector();
using var predictor = new PredictorService();
var forecasts = predictor.GenerateWalkForwardPredictions(data, features, detector);
var journal = predictor.GenerateWalkForwardJournal(data, features, detector);
var prices = data.ToDictionary(d => d.Date, d => d.BtcClose);
var evaluated = forecasts.Where(p => p.Validation != null && prices.ContainsKey(p.Date.AddDays(7))).ToList();
int directionalCorrect = evaluated.Count(p => (p.BullishProbability > .5m) == (prices[p.Date.AddDays(7)] > p.BtcClose));
int alwaysUpCorrect = evaluated.Count(p => prices[p.Date.AddDays(7)] > p.BtcClose);
double? brier = evaluated.Count > 0 ? evaluated.Average(p => Math.Pow((double)p.BullishProbability - (prices[p.Date.AddDays(7)] > p.BtcClose ? 1d : 0d), 2)) : null;
var report = new
{
    ModelVersion = PredictionPolicy.Version, SnapshotSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
    FirstDate = data[0].Date, LastDate = data[^1].Date, ClosedObservations = data.Count,
    Signals = forecasts.GroupBy(p => p.Signal).ToDictionary(g => g.Key, g => g.Count()),
    Statuses = forecasts.GroupBy(p => p.PredictionStatus).ToDictionary(g => g.Key, g => g.Count()),
    ModelDirection = new { Count = evaluated.Count, Correct = directionalCorrect, AlwaysUpCorrect = alwaysUpCorrect, BrierScore = brier },
    ActiveSignalJournal = journal.Stats,
    Portfolio = new BacktestEngine().RunSimulation(data, features, predictor),
    LatestValidation = forecasts.LastOrDefault(p => p.Validation != null)?.Validation,
    Limitations = new[] { "Previously inspected sample; not an untouched final holdout.",
        "Daily label windows overlap. Raw directional estimates include days on which the validation gate abstains from trading.",
        "Ledger sums are not portfolio returns. Cost and execution assumptions are in Portfolio.Assumptions.",
        "No guarantee of calibrated probabilities or future predictive advantage." }
};
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return 0;
