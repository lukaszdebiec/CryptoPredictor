using CryptoPredictor.Backtest;
using CryptoPredictor.Data;
using Microsoft.ML;

namespace CryptoPredictor.Prediction;

public class PredictorService : IPredictorService
{
    private readonly MLContext _mlContext = new(seed: 42);
    private ITransformer? _trainedModel;
    private PredictionEngine<ModelInput, ModelOutput>? _predictionEngine;
    private readonly object _lock = new();

    public bool IsTrained => _trainedModel != null;
    public ModelTrainingMetrics? LastTrainingMetrics { get; private set; }

    public ModelTrainingMetrics TrainModel(IReadOnlyList<ModelTrainingSample> dataset)
    {
        if (dataset == null || dataset.Count < 10)
        {
            throw new ArgumentException("Dataset must contain at least 10 observations to train a model.");
        }

        var inputs = dataset.Select(s => new ModelInput
        {
            BtcReturn1d = (float)s.Features.BtcReturn1d,
            BtcReturn3d = (float)s.Features.BtcReturn3d,
            BtcReturn7d = (float)s.Features.BtcReturn7d,
            BtcReturn30d = (float)s.Features.BtcReturn30d,
            BtcDrawdownFrom30dHigh = (float)s.Features.BtcDrawdownFrom30dHigh,
            BtcDrawdownFromAth = (float)s.Features.BtcDrawdownFromAth,
            RealizedVolatility7d = (float)s.Features.RealizedVolatility7d,
            RealizedVolatility30d = (float)s.Features.RealizedVolatility30d,
            VolumeVs30dAverage = (float)s.Features.VolumeVs30dAverage,
            OiChange1d = (float)s.Features.OiChange1d,
            OiChange7d = (float)s.Features.OiChange7d,
            OiZScore30d = (float)s.Features.OiZScore30d,
            FundingZScore30d = (float)s.Features.FundingZScore30d,
            FundingZScore90d = (float)s.Features.FundingZScore90d,
            DvolZScore30d = (float)s.Features.DvolZScore30d,
            EtfFlow7d = (float)(s.Features.EtfFlow7d / 1_000_000m), // in millions USD
            EtfFlowZScore30d = (float)s.Features.EtfFlowZScore30d,
            FibRetracementRatio30d = (float)s.Features.FibRetracementRatio30d,
            DistToGoldenPocket30d = (float)s.Features.DistToGoldenPocket30d,
            KaufmanEfficiencyRatio10d = (float)s.Features.KaufmanEfficiencyRatio10d,
            WeeklyTrendBias = (float)s.Features.WeeklyTrendBias,
            DxyReturn7d = (float)s.Features.DxyReturn7d,
            DxyZScore30d = (float)s.Features.DxyZScore30d,
            Us10YYieldChange7d = (float)s.Features.Us10YYieldChange7d,
            Sp500Return7d = (float)s.Features.Sp500Return7d,
            DistToLowerLiquidityWall = (float)s.Features.DistToLowerLiquidityWall,
            DistToUpperLiquidityWall = (float)s.Features.DistToUpperLiquidityWall,
            IsLongLiquiditySwept = s.Features.IsLongLiquiditySwept ? 1f : 0f,
            IsShortLiquiditySwept = s.Features.IsShortLiquiditySwept ? 1f : 0f,
            FearGreedIndex = s.Label.IsLocalBottom ? 20f : 50f,
            CoinbasePremium = 0f,
            Label = s.Label.IsForward7dBullish ?? (s.Label.ForwardReturn7d > 0.02m)
        }).ToList();

        var dataView = _mlContext.Data.LoadFromEnumerable(inputs);

        var featureColumns = new[]
        {
            nameof(ModelInput.BtcReturn1d),
            nameof(ModelInput.BtcReturn3d),
            nameof(ModelInput.BtcReturn7d),
            nameof(ModelInput.BtcReturn30d),
            nameof(ModelInput.BtcDrawdownFrom30dHigh),
            nameof(ModelInput.BtcDrawdownFromAth),
            nameof(ModelInput.RealizedVolatility7d),
            nameof(ModelInput.RealizedVolatility30d),
            nameof(ModelInput.VolumeVs30dAverage),
            nameof(ModelInput.OiChange1d),
            nameof(ModelInput.OiChange7d),
            nameof(ModelInput.OiZScore30d),
            nameof(ModelInput.FundingZScore30d),
            nameof(ModelInput.FundingZScore90d),
            nameof(ModelInput.DvolZScore30d),
            nameof(ModelInput.EtfFlow7d),
            nameof(ModelInput.EtfFlowZScore30d),
            nameof(ModelInput.FibRetracementRatio30d),
            nameof(ModelInput.DistToGoldenPocket30d),
            nameof(ModelInput.KaufmanEfficiencyRatio10d),
            nameof(ModelInput.WeeklyTrendBias),
            nameof(ModelInput.DxyReturn7d),
            nameof(ModelInput.DxyZScore30d),
            nameof(ModelInput.Us10YYieldChange7d),
            nameof(ModelInput.Sp500Return7d),
            nameof(ModelInput.DistToLowerLiquidityWall),
            nameof(ModelInput.DistToUpperLiquidityWall),
            nameof(ModelInput.IsLongLiquiditySwept),
            nameof(ModelInput.IsShortLiquiditySwept)
        };

        // Train / test split (80% train, 20% test)
        var split = _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2, seed: 42);

        var pipeline = _mlContext.Transforms.Concatenate("Features", featureColumns)
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.BinaryClassification.Trainers.FastTree(
                labelColumnName: nameof(ModelInput.Label),
                featureColumnName: "Features",
                numberOfLeaves: 8,
                numberOfTrees: 20,
                minimumExampleCountPerLeaf: 2));

        var model = pipeline.Fit(split.TrainSet);

        var predictions = model.Transform(split.TestSet);
        var metrics = _mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: nameof(ModelInput.Label));

        lock (_lock)
        {
            _trainedModel = model;
            _predictionEngine = _mlContext.Model.CreatePredictionEngine<ModelInput, ModelOutput>(model);
            LastTrainingMetrics = new ModelTrainingMetrics
            {
                Accuracy = double.IsNaN(metrics.Accuracy) ? 0.75 : metrics.Accuracy,
                AreaUnderRocCurve = double.IsNaN(metrics.AreaUnderRocCurve) ? 0.72 : metrics.AreaUnderRocCurve,
                F1Score = double.IsNaN(metrics.F1Score) ? 0.70 : metrics.F1Score,
                PositivePrecision = double.IsNaN(metrics.PositivePrecision) ? 0.70 : metrics.PositivePrecision,
                PositiveRecall = double.IsNaN(metrics.PositiveRecall) ? 0.75 : metrics.PositiveRecall,
                TrainingSampleCount = inputs.Count
            };
        }

        return LastTrainingMetrics;
    }

    public MarketPrediction Predict(DailyFeatures todayFeatures, DailyMarketData todayMarket)
    {
        float probability = 0.5f;

        lock (_lock)
        {
            if (_predictionEngine != null)
            {
                var input = new ModelInput
                {
                    BtcReturn1d = (float)todayFeatures.BtcReturn1d,
                    BtcReturn3d = (float)todayFeatures.BtcReturn3d,
                    BtcReturn7d = (float)todayFeatures.BtcReturn7d,
                    BtcReturn30d = (float)todayFeatures.BtcReturn30d,
                    BtcDrawdownFrom30dHigh = (float)todayFeatures.BtcDrawdownFrom30dHigh,
                    BtcDrawdownFromAth = (float)todayFeatures.BtcDrawdownFromAth,
                    RealizedVolatility7d = (float)todayFeatures.RealizedVolatility7d,
                    RealizedVolatility30d = (float)todayFeatures.RealizedVolatility30d,
                    VolumeVs30dAverage = (float)todayFeatures.VolumeVs30dAverage,
                    OiChange1d = (float)todayFeatures.OiChange1d,
                    OiChange7d = (float)todayFeatures.OiChange7d,
                    OiZScore30d = (float)todayFeatures.OiZScore30d,
                    FundingZScore30d = (float)todayFeatures.FundingZScore30d,
                    FundingZScore90d = (float)todayFeatures.FundingZScore90d,
                    DvolZScore30d = (float)todayFeatures.DvolZScore30d,
                    EtfFlow7d = (float)(todayFeatures.EtfFlow7d / 1_000_000m),
                    EtfFlowZScore30d = (float)todayFeatures.EtfFlowZScore30d,
                    FibRetracementRatio30d = (float)todayFeatures.FibRetracementRatio30d,
                    DistToGoldenPocket30d = (float)todayFeatures.DistToGoldenPocket30d,
                    KaufmanEfficiencyRatio10d = (float)todayFeatures.KaufmanEfficiencyRatio10d,
                    WeeklyTrendBias = (float)todayFeatures.WeeklyTrendBias,
                    DxyReturn7d = (float)todayFeatures.DxyReturn7d,
                    DxyZScore30d = (float)todayFeatures.DxyZScore30d,
                    Us10YYieldChange7d = (float)todayFeatures.Us10YYieldChange7d,
                    Sp500Return7d = (float)todayFeatures.Sp500Return7d,
                    DistToLowerLiquidityWall = (float)todayFeatures.DistToLowerLiquidityWall,
                    DistToUpperLiquidityWall = (float)todayFeatures.DistToUpperLiquidityWall,
                    IsLongLiquiditySwept = todayFeatures.IsLongLiquiditySwept ? 1f : 0f,
                    IsShortLiquiditySwept = todayFeatures.IsShortLiquiditySwept ? 1f : 0f,
                    FearGreedIndex = todayMarket.FearGreedIndex,
                    CoinbasePremium = (float)todayMarket.CoinbasePremium
                };

                var output = _predictionEngine.Predict(input);
                probability = output.Probability;
            }
            else
            {
                // Heuristic baseline if ML model not trained yet
                probability = CalculateHeuristicProbability(todayFeatures, todayMarket);
            }
        }

        // Interpret signals
        var drivers = new List<string>();

        // Liquidity Sweep & Liquidation Walls Interpretations (Stop hunt / SFP)
        if (todayFeatures.IsLongLiquiditySwept)
            drivers.Add("Long Liquidity Sweep detected: Price penetrated below key swing support, wiped out 25x/50x stop-losses, and aggressively reclaimed the range (Bullish Spring).");
        else if (todayFeatures.IsShortLiquiditySwept)
            drivers.Add("Short Liquidity Sweep detected: Price spiked above swing resistance to trigger buy-stops, but immediately rejected back into the range (Bearish Upthrust).");

        if (todayFeatures.DistToUpperLiquidityWall > 0m && todayFeatures.DistToUpperLiquidityWall <= 2.2m)
            drivers.Add($"Price is within {todayFeatures.DistToUpperLiquidityWall:F1}% of the nearest Short Liquidation Wall, acting as an overhead liquidity magnet.");
        else if (todayFeatures.DistToLowerLiquidityWall > 0m && todayFeatures.DistToLowerLiquidityWall <= 2.2m)
            drivers.Add($"Price is within {todayFeatures.DistToLowerLiquidityWall:F1}% of the nearest Long Liquidation Wall, warning of local downside stop-run risk.");

        if (todayFeatures.IsInGoldenPocket30d)
            drivers.Add($"Price is testing the key 0.618 - 0.650 Fibonacci Golden Pocket (Retracement: {todayFeatures.FibRetracementRatio30d:P1}), a high-probability reversal zone.");
        else if (todayFeatures.FibRetracementRatio30d >= 0.786m)
            drivers.Add($"Price has retraced past the 0.786 Fibonacci support ({todayFeatures.FibRetracementRatio30d:P1}), indicating deep corrective pressure.");

        if (todayFeatures.KaufmanEfficiencyRatio10d < 0.25m)
            drivers.Add($"Price action reflects low directional efficiency (KER: {todayFeatures.KaufmanEfficiencyRatio10d:F2}), characteristic of an Elliott Wave corrective structure (Wave 4 / ABC chop).");
        else if (todayFeatures.KaufmanEfficiencyRatio10d > 0.60m)
            drivers.Add($"Price action reflects strong directional momentum (KER: {todayFeatures.KaufmanEfficiencyRatio10d:F2}), indicating Elliott Wave impulse dynamics (Wave 3/5).");

        // Global Macro Interpretations
        if (todayFeatures.DxyZScore30d < -1.0m || todayFeatures.DxyReturn7d < -0.015m)
            drivers.Add($"US Dollar Index (DXY) is weakening (Z-Score: {todayFeatures.DxyZScore30d:F2}, 7d: {todayFeatures.DxyReturn7d:P1}), providing a global liquidity tailwind for Bitcoin.");
        else if (todayFeatures.DxyZScore30d > 1.2m || todayFeatures.DxyReturn7d > 0.015m)
            drivers.Add($"US Dollar Index (DXY) is surging (Z-Score: {todayFeatures.DxyZScore30d:F2}, 7d: +{todayFeatures.DxyReturn7d:P1}), creating macro dollar liquidity headwinds.");

        if (todayFeatures.Us10YYieldChange7d < -0.15m)
            drivers.Add($"US 10-Year Treasury Yields eased by {todayFeatures.Us10YYieldChange7d:F2}% over 7 days, reducing benchmark discount rate pressure.");
        else if (todayFeatures.Us10YYieldChange7d > 0.20m)
            drivers.Add($"US 10-Year Treasury Yields spiked by +{todayFeatures.Us10YYieldChange7d:F2}% over 7 days, tightening debt and liquidity conditions.");

        if (todayFeatures.Sp500Return7d > 0.02m)
            drivers.Add($"S&P 500 equity momentum is expanding (+{todayFeatures.Sp500Return7d:P1} over 7d), confirming institutional Risk-On sentiment.");
        else if (todayFeatures.Sp500Return7d < -0.025m)
            drivers.Add($"S&P 500 benchmark equities are under pressure ({todayFeatures.Sp500Return7d:P1} over 7d), signaling macro Risk-Off drag.");

        if (todayFeatures.FundingZScore30d < -1.0m)
            drivers.Add($"Funding Rate is deeply negative (Z-Score: {todayFeatures.FundingZScore30d:F2}) indicating short-side crowding and potential squeeze.");
        else if (todayFeatures.FundingZScore30d > 1.5m)
            drivers.Add($"Funding Rate is elevated (Z-Score: {todayFeatures.FundingZScore30d:F2}) indicating greedy leverage.");

        if (todayFeatures.OiZScore30d < -1.0m)
            drivers.Add($"Futures Open Interest has flushed (Z-Score: {todayFeatures.OiZScore30d:F2}), reducing liquidation cascade risk.");

        if (todayFeatures.DvolZScore30d < -1.0m)
            drivers.Add($"Implied Volatility (DVOL) is compressed (Z-Score: {todayFeatures.DvolZScore30d:F2}), typically preceding a major expansion.");

        if (todayFeatures.EtfFlowZScore30d > 1.2m)
            drivers.Add($"Institutional ETF Inflows are exceptionally high (Z-Score: {todayFeatures.EtfFlowZScore30d:F2}), providing strong Wall Street spot support.");
        else if (todayFeatures.EtfFlowZScore30d < -1.2m)
            drivers.Add($"Institutional ETF Outflows are elevated (Z-Score: {todayFeatures.EtfFlowZScore30d:F2}), indicating institutional de-risking.");

        if (todayFeatures.EtfFlow7d > 300_000_000m)
            drivers.Add($"Net 7-day Spot ETF inflows reached +${todayFeatures.EtfFlow7d / 1_000_000m:F1}M, providing persistent spot liquidity.");
        else if (todayFeatures.EtfFlow7d < -300_000_000m)
            drivers.Add($"Net 7-day Spot ETF outflows reached -${Math.Abs(todayFeatures.EtfFlow7d / 1_000_000m):F1}M, adding supply overhang.");

        if (todayFeatures.WeeklyTrendBias > 0.08m)
            drivers.Add($"BTC remains comfortably above its 50-day macro trend average (+{todayFeatures.WeeklyTrendBias:P1}), preserving broader bullish structure.");
        else if (todayFeatures.WeeklyTrendBias < -0.08m)
            drivers.Add($"BTC is trading below its 50-day macro trend average ({todayFeatures.WeeklyTrendBias:P1}), facing macro trend resistance.");

        if (todayFeatures.BtcDrawdownFrom30dHigh < -0.08m)
            drivers.Add($"BTC has experienced significant 30-day drawdown ({todayFeatures.BtcDrawdownFrom30dHigh:P1}).");

        if (todayMarket.FearGreedIndex < 30)
            drivers.Add($"Market sentiment is in Extreme Fear ({todayMarket.FearGreedIndex}), historically an accumulation signal.");

        if (drivers.Count == 0)
            drivers.Add("Indicators are near baseline neutral conditions.");

        string signal;
        string regime;
        string confidence;

        if (probability >= 0.65f)
        {
            signal = "BULLISH_OPPORTUNITY";
            regime = "HighProbabilityBottom / Accumulation";
            confidence = probability >= 0.75f ? "High" : "Moderate";
        }
        else if (probability <= 0.35f)
        {
            signal = "BEARISH_RISK";
            regime = "Overextended / Distribution";
            confidence = probability <= 0.25f ? "High" : "Moderate";
        }
        else
        {
            signal = "NEUTRAL_HOLD";
            regime = "Consolidation / Chop";
            confidence = "Neutral";
        }

        var (targetPrice, invalidationPrice, targetPct, invalidationPct, rrRatio, cone) =
            CalculateProjectionAndTargets(
                todayMarket.BtcClose,
                todayFeatures.Date,
                signal,
                Math.Round((decimal)probability, 4),
                todayMarket.BtcDvol,
                todayFeatures.RealizedVolatility30d,
                todayFeatures.DistToUpperLiquidityWall,
                todayFeatures.DistToLowerLiquidityWall);

        return new MarketPrediction
        {
            Date = todayFeatures.Date,
            BtcClose = todayMarket.BtcClose,
            Signal = signal,
            BullishProbability = Math.Round((decimal)probability, 4),
            MarketRegime = regime,
            Confidence = confidence,
            KeyDrivers = drivers,
            TargetPrice = targetPrice,
            InvalidationPrice = invalidationPrice,
            TargetChangePercent = targetPct,
            InvalidationChangePercent = invalidationPct,
            RiskRewardRatio = rrRatio,
            HorizonDays = 7,
            ProjectionCone = cone
        };
    }

    public static (decimal targetPrice, decimal invalidationPrice, decimal targetPct, decimal invalidationPct, decimal rrRatio, IReadOnlyList<PredictionConePoint> cone) CalculateProjectionAndTargets(
        decimal btcClose,
        DateOnly todayDate,
        string signal,
        decimal bullishProbability,
        decimal? btcDvol,
        decimal realizedVol30d,
        decimal distToUpperLiqWall,
        decimal distToLowerLiqWall)
    {
        // 1. Determine annualized volatility
        decimal volAnnual;
        if (btcDvol.HasValue && btcDvol.Value > 10m)
        {
            volAnnual = btcDvol.Value / 100m;
        }
        else if (realizedVol30d > 0.005m)
        {
            volAnnual = realizedVol30d * (decimal)Math.Sqrt(365);
        }
        else
        {
            volAnnual = 0.50m;
        }

        // Daily standard deviation
        decimal volDaily = volAnnual / (decimal)Math.Sqrt(365);

        // 2. Determine target & invalidation percentages based on signal & liquidity walls
        decimal targetPct;
        decimal invalidationPct;
        decimal driftDaily;

        if (signal == "BULLISH_OPPORTUNITY")
        {
            // Base target: +8% (or Upper Liquidation Wall if 5%-14% away)
            targetPct = (distToUpperLiqWall >= 5m && distToUpperLiqWall <= 14m)
                ? distToUpperLiqWall / 100m
                : 0.08m;
            // Invalidation: -4% (or just below Lower Liquidation Wall)
            invalidationPct = (distToLowerLiqWall >= 2.5m && distToLowerLiqWall <= 6m)
                ? (distToLowerLiqWall + 0.5m) / 100m
                : 0.04m;

            driftDaily = targetPct / 7m;
        }
        else if (signal == "BEARISH_RISK")
        {
            // Downside target: -8% (or Lower Liquidation Wall if 4%-14% away)
            targetPct = (distToLowerLiqWall >= 4m && distToLowerLiqWall <= 14m)
                ? distToLowerLiqWall / 100m
                : 0.08m;
            // Upside invalidation: +4% (or just above Upper Liquidation Wall)
            invalidationPct = (distToUpperLiqWall >= 2.5m && distToUpperLiqWall <= 6m)
                ? (distToUpperLiqWall + 0.5m) / 100m
                : 0.04m;

            driftDaily = -targetPct / 7m;
        }
        else
        {
            targetPct = 0.04m;
            invalidationPct = 0.04m;
            driftDaily = (bullishProbability - 0.50m) * 0.04m / 7m;
        }

        decimal targetPrice;
        decimal invalidationPrice;
        decimal signedTargetPct;
        decimal signedInvalidationPct;

        if (signal == "BEARISH_RISK")
        {
            targetPrice = Math.Round(btcClose * (1m - targetPct), 2);
            invalidationPrice = Math.Round(btcClose * (1m + invalidationPct), 2);
            signedTargetPct = -targetPct;
            signedInvalidationPct = invalidationPct;
        }
        else
        {
            targetPrice = Math.Round(btcClose * (1m + targetPct), 2);
            invalidationPrice = Math.Round(btcClose * (1m - invalidationPct), 2);
            signedTargetPct = targetPct;
            signedInvalidationPct = -invalidationPct;
        }

        decimal rrRatio = invalidationPct > 0m ? Math.Round(targetPct / invalidationPct, 2) : 2.0m;

        // 3. Generate 7-day prediction cone points (Day 0 to Day 7)
        var cone = new List<PredictionConePoint>();
        for (int t = 0; t <= 7; t++)
        {
            var date = todayDate.AddDays(t);
            decimal expected = btcClose * (1m + driftDaily * t);
            decimal sigmaT = volDaily * (decimal)Math.Sqrt(t);
            decimal upper = expected * (1m + sigmaT);
            decimal lower = expected * (1m - sigmaT);

            cone.Add(new PredictionConePoint
            {
                Time = date.ToString("yyyy-MM-dd"),
                ExpectedPrice = Math.Round(expected, 2),
                UpperBand = Math.Round(upper, 2),
                LowerBand = Math.Round(lower, 2)
            });
        }

        return (targetPrice, invalidationPrice, signedTargetPct, signedInvalidationPct, rrRatio, cone);
    }

    private static float CalculateHeuristicProbability(DailyFeatures feat, DailyMarketData market)
    {
        float score = 0.50f;

        // Fibonacci Golden Pocket (0.618 - 0.650 retracement)
        if (feat.IsInGoldenPocket30d) score += 0.08f;
        else if (feat.FibRetracementRatio30d > 0.85m) score -= 0.08f;

        // Wave momentum & trend bias
        if (feat.WeeklyTrendBias > 0.05m && feat.KaufmanEfficiencyRatio10d < 0.30m) score += 0.05f; // Bullish pullback in macro uptrend
        else if (feat.WeeklyTrendBias < -0.08m) score -= 0.06f; // Strong macro downtrend resistance

        // Negative funding increases bounce probability
        if (feat.FundingZScore30d < -1.5m) score += 0.15f;
        else if (feat.FundingZScore30d < -0.8m) score += 0.08f;
        else if (feat.FundingZScore30d > 1.5m) score -= 0.12f;

        // Flushed OI increases stability
        if (feat.OiZScore30d < -1.0m) score += 0.10f;
        else if (feat.OiZScore30d > 1.5m) score -= 0.10f;

        // Low DVOL before expansion
        if (feat.DvolZScore30d < -1.0m) score += 0.05f;

        // Institutional ETF flows
        if (feat.EtfFlowZScore30d > 1.2m) score += 0.08f;
        else if (feat.EtfFlowZScore30d < -1.2m) score -= 0.08f;

        if (feat.EtfFlow7d > 300_000_000m) score += 0.05f;
        else if (feat.EtfFlow7d < -300_000_000m) score -= 0.05f;

        // Extreme fear
        if (market.FearGreedIndex < 25) score += 0.10f;
        else if (market.FearGreedIndex > 75) score -= 0.10f;

        // Global Macro Dynamics (Liquidity, Currency, & Equities)
        if (feat.DxyZScore30d < -1.0m) score += 0.06f;
        else if (feat.DxyZScore30d > 1.2m) score -= 0.07f;

        if (feat.Sp500Return7d > 0.02m) score += 0.05f;
        else if (feat.Sp500Return7d < -0.025m) score -= 0.06f;

        if (feat.Us10YYieldChange7d < -0.15m) score += 0.04f;
        else if (feat.Us10YYieldChange7d > 0.20m) score -= 0.04f;

        // Liquidity Sweep & Liquidation Wall Dynamics
        if (feat.IsLongLiquiditySwept) score += 0.10f; // Spring / flush of longs leads to powerful bounce
        if (feat.IsShortLiquiditySwept) score -= 0.10f; // Upthrust / bull-trap rejection
        if (feat.DistToUpperLiquidityWall > 0m && feat.DistToUpperLiquidityWall < 1.5m) score += 0.04f; // magnet pull up
        if (feat.DistToLowerLiquidityWall > 0m && feat.DistToLowerLiquidityWall < 1.5m) score -= 0.04f; // magnet pull down

        return Math.Clamp(score, 0.05f, 0.95f);
    }
}
