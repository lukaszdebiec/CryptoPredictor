using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CryptoPredictor.Data;
using CryptoPredictor.Features;

namespace CryptoPredictor.Prediction.Trajectory;

public interface IPriceTrajectoryService
{
    TrajectoryForecast Forecast(IReadOnlyList<DailyMarketData> history, string source, string symbol, DateOnly? asOfDate = null);
}

public sealed class PriceTrajectoryService : IPriceTrajectoryService
{
    private readonly object _lock = new();
    private (string Key, TrajectoryForecast Forecast)? _cache;
    private sealed record Sample(DateOnly Date, double[] X, double DailyVolatility, double[] LogReturns);
    private sealed record Distribution(RidgeRegressor Model, double Lower, double Median, double Upper)
    {
        public double[] Predict(Sample s, int h)
        {
            double center = Model.Predict(s.X), scale = Scale(s, h);
            return [(center + Lower) * scale, (center + Median) * scale, (center + Upper) * scale];
        }
    }

    public TrajectoryForecast Forecast(IReadOnlyList<DailyMarketData> history, string source, string symbol, DateOnly? asOfDate = null)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = asOfDate ?? today.AddDays(-1);
        if (cutoff >= today) throw new ArgumentException("The trajectory origin must be a closed UTC day.");
        var bars = history.Where(b => b.Date <= cutoff && b.Date < today).OrderBy(b => b.Date).ToList();
        var template = new TrajectoryForecast
        {
            Status = "INSUFFICIENT_DATA", Message = "Synchronize a dedicated multi-year daily price history first.",
            Source = source, Symbol = symbol, AsOfDate = bars.LastOrDefault()?.Date,
            ReferenceClose = bars.LastOrDefault()?.BtcClose, Observations = bars.Count
        };
        if (bars.Count == 0) return template;
        if (asOfDate.HasValue && bars[^1].Date != cutoff)
            return template with { Status = "DATA_UNAVAILABLE", Message = "No candle exists for the requested forecast origin." };
        if (bars.Any(b => !Valid(b)) || bars.Zip(bars.Skip(1)).Any(pair => pair.First.Date.AddDays(1) != pair.Second.Date))
            return template with { Status = "DATA_UNAVAILABLE", Message = "Price history contains duplicate dates, gaps or invalid OHLCV. No interpolation was applied." };

        // Hash only immutable model inputs up to the origin. Later candles and optional feeds cannot affect the forecast.
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            bars.Select(b => new { b.Date, b.BtcOpen, b.BtcHigh, b.BtcLow, b.BtcClose, b.BtcVolume })))));
        string cacheKey = $"{TrajectoryPolicy.Version}:{source}:{symbol}:{fingerprint}";
        lock (_lock) if (_cache is { } c && c.Key == cacheKey) return c.Forecast;
        template = template with { DataFingerprint = fingerprint };
        var features = new FeatureCalculator().Calculate(bars);
        var samples = new List<Sample>();
        for (int i = TrajectoryPolicy.Warmup - 1; i + TrajectoryPolicy.Horizon < bars.Count; i++)
        {
            var returns = Enumerable.Range(1, 7).Select(h => Math.Log((double)bars[i + h].BtcClose / (double)bars[i].BtcClose)).ToArray();
            samples.Add(new Sample(bars[i].Date, Vector(features[i]), DailyVolatility(features[i]), returns));
        }
        template = template with { EligibleSamples = samples.Count };
        if (samples.Count < TrajectoryPolicy.MinimumSamples)
            return template with { Message = $"Need {TrajectoryPolicy.MinimumSamples} eligible origins after a {TrajectoryPolicy.Warmup}-day warmup and 7 matured outcome days; available {samples.Count}." };

        int calibrationIndex = (int)(samples.Count * .6), testIndex = (int)(samples.Count * .8);
        var fit = samples.Take(calibrationIndex).Where(s => s.Date.AddDays(7) < samples[calibrationIndex].Date).ToList();
        var calibration = samples.Skip(calibrationIndex).Take(testIndex - calibrationIndex)
            .Where(s => s.Date.AddDays(7) < samples[testIndex].Date).ToList();
        var test = samples.Skip(testIndex).ToList();
        var servingFit = samples.Take(testIndex).Where(s => s.Date.AddDays(7) < samples[testIndex].Date).ToList();
        var servingCalibration = test; // All of these outcomes are known at the forecast origin.
        if (fit.Count < TrajectoryPolicy.MinimumFit || calibration.Count < TrajectoryPolicy.MinimumCalibration || test.Count < TrajectoryPolicy.MinimumValidation)
            return template with { Message = "Insufficient chronological fit/calibration/validation observations after purging." };

        var latest = new Sample(bars[^1].Date, Vector(features[^1]), DailyVolatility(features[^1]), []);
        var validation = new List<HorizonValidation>();
        var points = new List<TrajectoryPoint>
        {
            new() { Time = latest.Date.ToString("yyyy-MM-dd"), HorizonDays = 0, MedianPrice = bars[^1].BtcClose, LowerPrice = bars[^1].BtcClose, UpperPrice = bars[^1].BtcClose }
        };
        for (int h = 1; h <= 7; h++)
        {
            var candidate = Train(fit, calibration, h);
            var metrics = Evaluate(candidate, fit, calibration, test, h);
            validation.Add(metrics);
            // Refit the fixed specification and recalibrate on recent, already matured labels.
            // The reported holdout evaluates the earlier candidate, not this serving refit's training fit.
            var serving = Train(servingFit, servingCalibration, h);
            var quantiles = serving.Predict(latest, h);
            var prices = quantiles.Select(r => (double)bars[^1].BtcClose * Math.Exp(r)).ToArray();
            if (prices.Any(v => !double.IsFinite(v) || v < .01d || v > (double)decimal.MaxValue) || !double.IsFinite(quantiles[1]))
                return template with { Status = "INVALID_OUTPUT", Message = "The fitted model produced an unsafe price projection; no trajectory is shown.", Validation = validation };
            points.Add(new TrajectoryPoint
            {
                Time = latest.Date.AddDays(h).ToString("yyyy-MM-dd"), HorizonDays = h,
                LowerPrice = Math.Round((decimal)prices[0], 2), MedianPrice = Math.Round((decimal)prices[1], 2),
                UpperPrice = Math.Round((decimal)prices[2], 2), MedianLogReturn = quantiles[1], HasValidationAdvantage = metrics.HasValidationAdvantage
            });
        }
        bool advantage = validation[^1].HasValidationAdvantage;
        var result = template with
        {
            Status = advantage ? "MODEL_ESTIMATE" : "NO_VALIDATION_ADVANTAGE",
            Message = advantage
                ? "The 7-day candidate beat the no-change benchmark on this chronological holdout. This is not proof of future performance."
                : "Experimental estimate: the 7-day candidate did not demonstrate an advantage over no change. This trajectory does not issue trade signals.",
            ServingFitSamples = servingFit.Count, ServingCalibrationSamples = servingCalibration.Count,
            ServingFitOutcomeEnd = servingFit[^1].Date.AddDays(7), ServingCalibrationStart = servingCalibration[0].Date,
            Points = points.AsReadOnly(), Validation = validation.AsReadOnly()
        };
        lock (_lock) _cache = (cacheKey, result);
        return result;
    }

    private static Distribution Train(List<Sample> fit, List<Sample> calibration, int h)
    {
        var model = RidgeRegressor.Fit(fit.Select(s => s.X).ToList(), fit.Select(s => s.LogReturns[h - 1] / Scale(s, h)).ToList());
        var residuals = calibration.Select(s => s.LogReturns[h - 1] / Scale(s, h) - model.Predict(s.X)).Order().ToArray();
        return new Distribution(model, Quantile(residuals, .1), Quantile(residuals, .5), Quantile(residuals, .9));
    }

    private static HorizonValidation Evaluate(Distribution model, List<Sample> fit, List<Sample> calibration, List<Sample> test, int h)
    {
        var baseline = calibration.Select(s => s.LogReturns[h - 1] / Scale(s, h)).Order().ToArray();
        double baseLow = Math.Min(0d, Quantile(baseline, .1)), baseHigh = Math.Max(0d, Quantile(baseline, .9));
        double mae = 0, mse = 0, baselineMae = 0, pinball = 0, baselinePinball = 0, width = 0;
        int covered = 0, baselineCovered = 0;
        foreach (var s in test)
        {
            double y = s.LogReturns[h - 1], scale = Scale(s, h);
            var q = model.Predict(s, h);
            double[] b = [baseLow * scale, 0d, baseHigh * scale];
            mae += Math.Abs(y - q[1]); mse += Math.Pow(y - q[1], 2); baselineMae += Math.Abs(y);
            if (y >= q[0] && y <= q[2]) covered++;
            if (y >= b[0] && y <= b[2]) baselineCovered++;
            width += q[2] - q[0];
            for (int k = 0; k < 3; k++)
            {
                double probability = k == 0 ? .1 : k == 1 ? .5 : .9;
                pinball += Pinball(y, q[k], probability); baselinePinball += Pinball(y, b[k], probability);
            }
        }
        return new HorizonValidation
        {
            HorizonDays = h, FitSamples = fit.Count, CalibrationSamples = calibration.Count, ValidationSamples = test.Count,
            FitOutcomeEnd = fit[^1].Date.AddDays(h), CalibrationStart = calibration[0].Date,
            CalibrationOutcomeEnd = calibration[^1].Date.AddDays(h), ValidationStart = test[0].Date, ValidationEnd = test[^1].Date,
            MaeLogReturn = mae / test.Count, BaselineMaeLogReturn = baselineMae / test.Count,
            RmseLogReturn = Math.Sqrt(mse / test.Count), MeanPinballLoss = pinball / (3 * test.Count),
            BaselineMeanPinballLoss = baselinePinball / (3 * test.Count), IntervalCoverage = covered / (double)test.Count,
            BaselineIntervalCoverage = baselineCovered / (double)test.Count, MeanIntervalWidthLogReturn = width / test.Count,
            HasValidationAdvantage = mae < baselineMae && pinball < baselinePinball && covered / (double)test.Count >= .70
        };
    }

    public static double Pinball(double actual, double predicted, double probability)
    {
        double error = actual - predicted;
        return Math.Max(probability * error, (probability - 1d) * error);
    }

    private static double Quantile(double[] sorted, double probability)
    {
        double index = probability * (sorted.Length - 1); int low = (int)index, high = Math.Min(sorted.Length - 1, low + 1);
        return sorted[low] + (sorted[high] - sorted[low]) * (index - low);
    }
    private static double DailyVolatility(DailyFeatures f) => Math.Clamp((double)f.RealizedVolatility30d / Math.Sqrt(365d), .002d, .20d);
    private static double Scale(Sample s, int h) => s.DailyVolatility * Math.Sqrt(h);
    private static double[] Vector(DailyFeatures f) =>
    [ (double)f.BtcReturn1d, (double)f.BtcReturn3d, (double)f.BtcReturn7d, (double)f.BtcReturn30d,
      (double)f.BtcDrawdownFrom30dHigh, (double)f.RealizedVolatility7d, (double)f.RealizedVolatility30d,
      (double)f.VolumeVs30dAverage, (double)f.KaufmanEfficiencyRatio10d, (double)f.WeeklyTrendBias ];
    public static bool Valid(DailyMarketData b) => b.BtcClose > 0m && b.BtcOpen > 0m && b.BtcLow > 0m &&
        b.BtcHigh >= Math.Max(b.BtcOpen, b.BtcClose) && b.BtcLow <= Math.Min(b.BtcOpen, b.BtcClose) && b.BtcVolume >= 0m;
}
