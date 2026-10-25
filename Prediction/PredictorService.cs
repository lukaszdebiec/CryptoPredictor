using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CryptoPredictor.Backtest;
using CryptoPredictor.Data;
using Microsoft.ML;

namespace CryptoPredictor.Prediction;

/// <summary>
/// A deliberately small directional baseline. Optional external feeds remain available to the
/// dashboard but cannot enter this model until their point-in-time coverage has been validated.
/// </summary>
public class PredictorService : IPredictorService, IDisposable
{
    private readonly object _lock = new();
    private readonly object _trainingLock = new();
    private PredictionEngine<ModelInput, ModelOutput>? _engine;
    private ModelTrainingMetrics? _metrics;
    private static readonly object CacheLock = new();
    private static (string Fingerprint, IReadOnlyList<MarketPrediction> Predictions)? _cachedWalkForward;

    public bool IsTrained { get { lock (_lock) return _engine != null; } }
    public ModelTrainingMetrics? LastTrainingMetrics { get { lock (_lock) return _metrics; } }

    private static readonly string[] FeatureColumns =
    [
        nameof(ModelInput.BtcReturn1d), nameof(ModelInput.BtcReturn3d), nameof(ModelInput.BtcReturn7d),
        nameof(ModelInput.BtcReturn30d), nameof(ModelInput.BtcDrawdownFrom30dHigh),
        nameof(ModelInput.RealizedVolatility7d), nameof(ModelInput.RealizedVolatility30d),
        nameof(ModelInput.VolumeVs30dAverage), nameof(ModelInput.KaufmanEfficiencyRatio10d),
        nameof(ModelInput.WeeklyTrendBias)
    ];

    private static ModelInput Input(DailyFeatures f, bool label = false) => new()
    {
        BtcReturn1d = (float)f.BtcReturn1d, BtcReturn3d = (float)f.BtcReturn3d,
        BtcReturn7d = (float)f.BtcReturn7d, BtcReturn30d = (float)f.BtcReturn30d,
        BtcDrawdownFrom30dHigh = (float)f.BtcDrawdownFrom30dHigh,
        RealizedVolatility7d = (float)f.RealizedVolatility7d, RealizedVolatility30d = (float)f.RealizedVolatility30d,
        VolumeVs30dAverage = (float)f.VolumeVs30dAverage,
        KaufmanEfficiencyRatio10d = (float)f.KaufmanEfficiencyRatio10d,
        WeeklyTrendBias = (float)f.WeeklyTrendBias, Label = label
    };

    public ModelTrainingMetrics TrainModel(IReadOnlyList<ModelTrainingSample> dataset)
    {
        lock (_trainingLock)
        {
            var sorted = dataset.OrderBy(s => s.Date).ToList();
            if (sorted.Count < PredictionPolicy.MinimumDatasetSamples)
                throw new ArgumentException($"At least {PredictionPolicy.MinimumDatasetSamples} eligible observations are required after warmup and labeling.");
            if (sorted.Select(s => s.Date).Distinct().Count() != sorted.Count ||
                sorted.Any(s => s.HorizonDays != PredictionPolicy.HorizonDays || s.OutcomeDate != s.Date.AddDays(PredictionPolicy.HorizonDays)))
                throw new ArgumentException("Training samples must be unique, dated 7-day directional outcomes.");

            int splitIndex = (int)(sorted.Count * 0.8);
            var validation = sorted.Skip(splitIndex).ToList();
            // Purge labels whose outcomes touch or cross the beginning of validation.
            var fit = sorted.Take(splitIndex).Where(s => s.OutcomeDate < validation[0].Date).ToList();
            if (fit.Count < PredictionPolicy.MinimumFitSamples || fit.Select(s => s.ForwardReturn > 0m).Distinct().Count() < 2)
                throw new ArgumentException("Insufficient chronological training samples or only one training class.");

            var ml = new MLContext(seed: 42);
            // A fixed regularized model; no model selection or threshold optimization on this holdout.
            var pipeline = ml.Transforms.Concatenate("Features", FeatureColumns)
                .Append(ml.Transforms.NormalizeMeanVariance("Features"))
                .Append(ml.BinaryClassification.Trainers.LbfgsLogisticRegression(
                    labelColumnName: nameof(ModelInput.Label), featureColumnName: "Features",
                    l1Regularization: 0.01f, l2Regularization: 1f));
            var candidate = pipeline.Fit(ml.Data.LoadFromEnumerable(fit.Select(s => Input(s.Features, s.ForwardReturn > 0m))));
            using var validationEngine = ml.Model.CreatePredictionEngine<ModelInput, ModelOutput>(candidate);
            var outcomes = validation.Select(s => s.ForwardReturn > 0m).ToArray();
            var probabilities = validation.Select(s => (double)validationEngine.Predict(Input(s.Features)).Probability).ToArray();
            if (probabilities.Any(p => !double.IsFinite(p) || p < 0d || p > 1d))
                throw new InvalidOperationException("The model produced an invalid probability during validation.");
            double baseRate = fit.Count(s => s.ForwardReturn > 0m) / (double)fit.Count;
            var metrics = Evaluate(probabilities, outcomes, baseRate) with
            {
                TrainingSampleCount = fit.Count, ValidationSampleCount = validation.Count, RefitSampleCount = sorted.Count,
                TrainingDataFingerprint = TrainingFingerprint(sorted),
                TrainingStart = fit[0].Date, TrainingEnd = fit[^1].Date, TrainingOutcomeEnd = fit.Max(s => s.OutcomeDate),
                ValidationStart = validation[0].Date, ValidationEnd = validation[^1].Date,
                DataThroughDate = sorted.Max(s => s.OutcomeDate)
            };
            // The holdout describes the earlier candidate. Refit the fixed specification on all
            // currently known labels for inference; do not describe this refit's in-sample fit as validation.
            var finalModel = pipeline.Fit(ml.Data.LoadFromEnumerable(sorted.Select(s => Input(s.Features, s.ForwardReturn > 0m))));
            var finalEngine = ml.Model.CreatePredictionEngine<ModelInput, ModelOutput>(finalModel);
            lock (_lock)
            {
                _engine?.Dispose();
                _engine = finalEngine;
                _metrics = metrics;
            }
            return metrics;
        }
    }

    private static ModelTrainingMetrics Evaluate(double[] p, bool[] labels, double baseRate)
    {
        int n = p.Length, tp = 0, fp = 0, fn = 0;
        int correct = 0, majorityCorrect = 0;
        double brier = 0d, logLoss = 0d, baselineBrier = 0d, baselineLogLoss = 0d;
        baseRate = Math.Clamp(baseRate, 1e-6, 1d - 1e-6);
        for (int i = 0; i < n; i++)
        {
            bool guess = p[i] >= 0.5d;
            double y = labels[i] ? 1d : 0d, prob = Math.Clamp(p[i], 1e-6, 1d - 1e-6);
            if (guess == labels[i]) correct++;
            if ((baseRate >= 0.5d) == labels[i]) majorityCorrect++;
            if (guess && labels[i]) tp++;
            if (guess && !labels[i]) fp++;
            if (!guess && labels[i]) fn++;
            brier += Math.Pow(p[i] - y, 2);
            logLoss -= y * Math.Log(prob) + (1d - y) * Math.Log(1d - prob);
            baselineBrier += Math.Pow(baseRate - y, 2);
            baselineLogLoss -= y * Math.Log(baseRate) + (1d - y) * Math.Log(1d - baseRate);
        }
        var positives = Enumerable.Range(0, n).Where(i => labels[i]).ToList();
        var negatives = Enumerable.Range(0, n).Where(i => !labels[i]).ToList();
        double? auc = positives.Count > 0 && negatives.Count > 0
            ? positives.Sum(i => negatives.Sum(j => p[i] > p[j] ? 1d : p[i] == p[j] ? 0.5d : 0d)) / (positives.Count * (double)negatives.Count)
            : null;
        return new ModelTrainingMetrics
        {
            Accuracy = correct / (double)n,
            PositivePrecision = tp + fp > 0 ? tp / (double)(tp + fp) : 0d,
            PositiveRecall = tp + fn > 0 ? tp / (double)(tp + fn) : 0d,
            F1Score = 2 * tp + fp + fn > 0 ? 2d * tp / (2 * tp + fp + fn) : 0d,
            AreaUnderRocCurve = auc, LogisticAuc = auc,
            BrierScore = brier / n, LogLoss = logLoss / n,
            BaselineBrierScore = baselineBrier / n, BaselineLogLoss = baselineLogLoss / n,
            AlwaysUpAccuracy = positives.Count / (double)n, MajorityBaselineAccuracy = majorityCorrect / (double)n,
            // A conservative research gate, not proof of significance or calibration.
            PassesValidation = auc.HasValue && brier < baselineBrier && logLoss < baselineLogLoss && correct > majorityCorrect
        };
    }

    public MarketPrediction Predict(DailyFeatures todayFeatures, DailyMarketData todayMarket)
    {
        if (todayFeatures.Date != todayMarket.Date || todayMarket.BtcClose <= 0m || todayMarket.Date >= DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("Features and a positive closing price must refer to the same closed UTC day.");
        decimal probability = 0.5m;
        ModelTrainingMetrics? metrics;
        bool trained;
        lock (_lock)
        {
            metrics = _metrics;
            if (metrics != null && metrics.DataThroughDate > todayMarket.Date)
                throw new InvalidOperationException("This model has seen outcomes after the requested prediction date. Use walk-forward evaluation.");
            trained = _engine != null;
            if (_engine != null)
            {
                float p = _engine.Predict(Input(todayFeatures)).Probability;
                if (!float.IsFinite(p) || p < 0f || p > 1f)
                    throw new InvalidOperationException("The directional model produced an invalid probability.");
                probability = (decimal)p;
            }
        }
        string status = todayFeatures.ConsecutivePriceDays < PredictionPolicy.WarmupDays ? "DATA_UNAVAILABLE"
            : !trained ? "INSUFFICIENT_DATA" : metrics?.PassesValidation == true ? "MODEL_ESTIMATE" : "VALIDATION_FAILED";
        bool allowed = status == "MODEL_ESTIMATE";
        string signal = allowed && probability >= PredictionPolicy.EntryThreshold ? "BULLISH_OPPORTUNITY"
            : allowed && probability <= 1m - PredictionPolicy.EntryThreshold ? "BEARISH_RISK" : "NEUTRAL_HOLD";
        var (target, stop, targetPct, stopPct, rr, cone) = CalculateProjectionAndTargets(
            todayMarket.BtcClose, todayMarket.Date, signal, probability, todayMarket.BtcDvol,
            todayFeatures.RealizedVolatility30d, 0m, 0m);
        return new MarketPrediction
        {
            Date = todayMarket.Date, BtcClose = todayMarket.BtcClose, ModelVersion = PredictionPolicy.Version,
            GeneratedAtUtc = DateTime.UtcNow, PredictionStatus = status, ProbabilityIsCalibrated = false,
            BullishProbability = Math.Round(probability, 6), Signal = signal,
            MarketRegime = status == "DATA_UNAVAILABLE" ? "Incomplete recent daily price history"
                : status == "INSUFFICIENT_DATA" ? "Insufficient training history" : status == "VALIDATION_FAILED"
                ? "No demonstrated validation advantage" : "7-day direction estimate",
            Confidence = "Uncalibrated", Validation = metrics, Ensemble = null,
            KeyDrivers =
            [
                PredictionPolicy.ProbabilityMeaning,
                "Regularized logistic baseline using 10 price/volume features; external feeds and rule overrides are excluded.",
                status == "DATA_UNAVAILABLE" ? "Recent price observations are incomplete; no trade is issued."
                    : !trained ? "No trained model: no trade is issued." : metrics?.PassesValidation != true
                    ? "The chronological holdout did not beat the training-base-rate benchmark; no trade is issued."
                    : "The candidate beat its baseline on a chronological holdout. This is not evidence of calibrated probabilities or future profitability.",
                "Targets are a separate risk policy (reward/risk 1.5); volatility bands illustrate uncertainty and do not forecast a price path."
            ],
            FeatureSnapshotJson = JsonSerializer.Serialize(todayFeatures),
            TargetPrice = target, InvalidationPrice = stop, TargetChangePercent = targetPct,
            InvalidationChangePercent = stopPct, RiskRewardRatio = rr,
            HorizonDays = PredictionPolicy.HorizonDays, ProjectionCone = cone
        };
    }

    public static (decimal targetPrice, decimal invalidationPrice, decimal targetPct, decimal invalidationPct,
        decimal rrRatio, IReadOnlyList<PredictionConePoint> cone) CalculateProjectionAndTargets(
        decimal btcClose, DateOnly todayDate, string signal, decimal bullishProbability,
        decimal? btcDvol, decimal realizedVol30d, decimal distToUpperLiqWall, decimal distToLowerLiqWall)
    {
        if (btcClose <= 0m) throw new ArgumentOutOfRangeException(nameof(btcClose));
        // RealizedVolatility30d is already annualized. Prefer the model's reproducible price series
        // over an optional external feed whose historical availability is not versioned.
        decimal annualVol = realizedVol30d > 0m ? realizedVol30d : btcDvol is > 0m ? btcDvol.Value / 100m : 0.5m;
        annualVol = Math.Clamp(annualVol, 0.01m, 3m);
        decimal dailyVol = annualVol / (decimal)Math.Sqrt(365d);
        decimal stopPct = Math.Clamp(dailyVol * (decimal)Math.Sqrt(7d) * 0.65m, 0.02m, 0.06m);
        decimal targetPct = stopPct * 1.5m;
        bool bearish = signal == "BEARISH_RISK", neutral = signal == "NEUTRAL_HOLD";
        decimal signedTarget = bearish ? -targetPct : targetPct, signedStop = bearish ? stopPct : -stopPct;
        var cone = Enumerable.Range(0, 8).Select(t =>
        {
            double sigma = (double)dailyVol * Math.Sqrt(t);
            return new PredictionConePoint
            {
                Time = todayDate.AddDays(t).ToString("yyyy-MM-dd"), ExpectedPrice = btcClose,
                UpperBand = Math.Round(btcClose * (decimal)Math.Exp(sigma), 2),
                LowerBand = Math.Round(btcClose * (decimal)Math.Exp(-sigma), 2)
            };
        }).ToList();
        return neutral ? (0m, 0m, 0m, 0m, 0m, cone)
            : (Math.Round(btcClose * (1m + signedTarget), 2), Math.Round(btcClose * (1m + signedStop), 2),
                signedTarget, signedStop, 1.5m, cone);
    }

    public IReadOnlyList<MarketPrediction> GenerateWalkForwardPredictions(
        IReadOnlyList<DailyMarketData> marketData, IReadOnlyList<DailyFeatures> features,
        ITurningPointDetector turningPointDetector)
    {
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        var sorted = marketData.Where(d => d.Date < todayUtc).OrderBy(d => d.Date).ToList();
        var featureMap = features.ToDictionary(f => f.Date);
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            PredictionPolicy.Version + JsonSerializer.Serialize(sorted) + JsonSerializer.Serialize(features))));
        lock (CacheLock)
            if (_cachedWalkForward is { } cached && cached.Fingerprint == fingerprint)
                return cached.Predictions;
        var predictions = new List<MarketPrediction>();
        for (int i = PredictionPolicy.WarmupDays - 1; i < sorted.Count; i++)
        {
            var day = sorted[i];
            if (!featureMap.TryGetValue(day.Date, out var feature)) continue;
            var available = sorted.Take(i + 1).ToList();
            var historicalFeatures = features.Where(f => f.Date <= day.Date).ToList();
            var dataset = turningPointDetector.BuildTrainingDataset(available, historicalFeatures);
            using var predictor = new PredictorService();
            if (CanTrain(dataset)) predictor.TrainModel(dataset);
            predictions.Add(predictor.Predict(feature, day));
        }
        var result = predictions.AsReadOnly();
        lock (CacheLock) _cachedWalkForward = (fingerprint, result);
        return result;
    }

    public static bool CanTrain(IReadOnlyList<ModelTrainingSample> dataset)
    {
        if (dataset.Count < PredictionPolicy.MinimumDatasetSamples) return false;
        var sorted = dataset.OrderBy(s => s.Date).ToList();
        int split = (int)(sorted.Count * 0.8);
        var fit = sorted.Take(split).Where(s => s.OutcomeDate < sorted[split].Date).ToList();
        return fit.Count >= PredictionPolicy.MinimumFitSamples && fit.Select(s => s.ForwardReturn > 0m).Distinct().Count() == 2;
    }

    public static IPredictorService PrepareForLatest(IPredictorService predictor,
        IReadOnlyList<DailyMarketData> data, IReadOnlyList<DailyFeatures> features, ITurningPointDetector detector)
    {
        var dataset = detector.BuildTrainingDataset(data, features);
        if (!CanTrain(dataset)) return new PredictorService();
        var throughDate = dataset.Max(s => s.OutcomeDate);
        if (predictor.LastTrainingMetrics?.DataThroughDate != throughDate ||
            predictor.LastTrainingMetrics.TrainingDataFingerprint != TrainingFingerprint(dataset.OrderBy(s => s.Date).ToList()))
            predictor.TrainModel(dataset);
        return predictor;
    }

    private static string TrainingFingerprint(IReadOnlyList<ModelTrainingSample> samples) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            samples.Select(s => new { s.Date, s.OutcomeDate, s.ForwardReturn, Input = Input(s.Features) })))));

    public void Dispose()
    {
        lock (_lock)
        {
            _engine?.Dispose();
            _engine = null;
            _metrics = null;
        }
    }

    public PredictionJournalResponse GenerateWalkForwardJournal(
        IReadOnlyList<DailyMarketData> marketData, IReadOnlyList<DailyFeatures> features, ITurningPointDetector turningPointDetector)
    {
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        var closed = marketData.Where(d => d.Date < todayUtc).ToList();
        return PredictionOutcomeResolver.Journal(
            GenerateWalkForwardPredictions(closed, features, turningPointDetector)
                .Select(p => PredictionOutcomeResolver.Resolve(p, closed)), "historical");
    }
}
