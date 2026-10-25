using CryptoPredictor.Data;
using CryptoPredictor.Prediction;

namespace CryptoPredictor.Backtest;

/// <summary>Daily expanding-window forecasts, one unlevered position at a time, net of specified costs.</summary>
public class BacktestEngine : IBacktestEngine
{
    public BacktestReport RunSimulation(IReadOnlyList<DailyMarketData> marketData,
        IReadOnlyList<DailyFeatures> features, IPredictorService predictor, BacktestSettings? settings = null)
    {
        settings ??= new BacktestSettings();
        if (settings.InitialCapital <= 0m || settings.EntryProbabilityThreshold < PredictionPolicy.EntryThreshold || settings.EntryProbabilityThreshold >= 1m ||
            settings.HoldingPeriodDays != PredictionPolicy.HorizonDays || settings.FeePerSide < 0m || settings.SlippagePerSide < 0m ||
            settings.FeePerSide + settings.SlippagePerSide >= 0.1m ||
            settings.StopLossPercent.HasValue != settings.TakeProfitPercent.HasValue ||
            settings.StopLossPercent is <= 0m or >= 1m || settings.TakeProfitPercent is <= 0m or >= 1m)
            throw new ArgumentException("Invalid simulation settings. Use horizon 7, threshold [0.60,1), nonnegative costs and either both positive TP/SL overrides or neither.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sorted = marketData.Where(m => m.Date < today).OrderBy(m => m.Date).ToList();
        if (sorted.Any(m => m.BtcClose <= 0m || m.BtcOpen <= 0m || m.BtcLow <= 0m || m.BtcHigh < m.BtcLow) ||
            sorted.Zip(sorted.Skip(1)).Any(pair => pair.First.Date.AddDays(1) != pair.Second.Date))
            throw new ArgumentException("Backtesting requires a complete, valid daily OHLC series.");
        var forecasts = predictor.GenerateWalkForwardPredictions(sorted, features, new TurningPointDetector())
            .Where(p => p.PredictionStatus != "INSUFFICIENT_DATA").OrderBy(p => p.Date).ToList();
        if (forecasts.Count == 0)
            return new BacktestReport { InitialCapital = settings.InitialCapital, FinalCapital = settings.InitialCapital,
                Assumptions = ["Insufficient history for a chronological directional model; no trades were evaluated."] };

        var start = forecasts[0].Date;
        var dates = sorted.Where(m => m.Date >= start).ToList();
        var marks = new Dictionary<DateOnly, decimal>();
        decimal capital = settings.InitialCapital;
        DateOnly nextEntry = start;
        var trades = new List<TradeRecord>();
        decimal sideCost = settings.FeePerSide + settings.SlippagePerSide;
        foreach (var original in forecasts)
        {
            if (original.Date < nextEntry || original.Date >= sorted[^1].Date || capital <= 0m ||
                original.PredictionStatus != "MODEL_ESTIMATE" || original.Signal == "NEUTRAL_HOLD") continue;
            bool bearish = original.Signal == "BEARISH_RISK";
            if (bearish && !settings.AllowShorts) continue;
            if ((!bearish && original.BullishProbability < settings.EntryProbabilityThreshold) ||
                (bearish && original.BullishProbability > 1m - settings.EntryProbabilityThreshold)) continue;
            var forecast = original;
            if (settings.StopLossPercent.HasValue)
            {
                decimal direction = bearish ? -1m : 1m;
                forecast = forecast with
                {
                    TargetPrice = forecast.BtcClose * (1m + direction * settings.TakeProfitPercent!.Value),
                    InvalidationPrice = forecast.BtcClose * (1m - direction * settings.StopLossPercent.Value)
                };
            }
            var outcome = PredictionOutcomeResolver.Resolve(forecast, sorted, settings.FeePerSide, settings.SlippagePerSide);
            if (outcome.OutcomeStatus == "DATA_UNAVAILABLE") throw new ArgumentException("Cannot resolve a trade with missing candles.");
            bool endOfData = outcome.OutcomeStatus == "PENDING";
            var exitDate = endOfData ? sorted[^1].Date : outcome.ResolvedDate!.Value;
            var exitPrice = endOfData ? sorted[^1].BtcClose : outcome.ResolvedPrice!.Value;
            decimal DirectionReturn(decimal price) => (bearish ? forecast.BtcClose - price : price - forecast.BtcClose) / forecast.BtcClose;
            decimal net = DirectionReturn(exitPrice) - 2m * sideCost;
            decimal initial = capital;
            marks[forecast.Date] = initial * (1m - sideCost);
            foreach (var bar in dates.Where(d => d.Date > forecast.Date && d.Date <= exitDate))
                marks[bar.Date] = initial * (1m + DirectionReturn(bar.Date == exitDate ? exitPrice : bar.BtcClose) - sideCost - (bar.Date == exitDate ? sideCost : 0m));
            capital = initial * (1m + net);
            trades.Add(new TradeRecord
            {
                Side = bearish ? "SHORT" : "LONG", EntryDate = forecast.Date, EntryPrice = forecast.BtcClose,
                ExitDate = exitDate, ExitPrice = exitPrice, HoldingDays = exitDate.DayNumber - forecast.Date.DayNumber,
                ReturnPercent = net, PnLUsd = initial * net,
                ExitReason = endOfData ? "END_OF_DATA" : outcome.ExitReason!
            });
            // Exit at/on this day's close, then the day's forecast can open a new position.
            nextEntry = exitDate;
        }

        var equity = new List<decimal> { settings.InitialCapital };
        decimal last = settings.InitialCapital;
        foreach (var bar in dates) { if (marks.TryGetValue(bar.Date, out var mark)) last = mark; equity.Add(last); }
        var dailyReturns = equity.Zip(equity.Skip(1)).Where(p => p.First > 0m)
            .Select(p => (double)(p.Second / p.First - 1m)).ToList();
        double sharpe = 0d;
        if (dailyReturns.Count > 1)
        {
            double mean = dailyReturns.Average();
            double sd = Math.Sqrt(dailyReturns.Sum(r => Math.Pow(r - mean, 2)) / (dailyReturns.Count - 1));
            if (sd > 1e-12) sharpe = mean / sd * Math.Sqrt(365d);
        }
        decimal gains = trades.Sum(t => Math.Max(0m, t.PnLUsd)), losses = -trades.Sum(t => Math.Min(0m, t.PnLUsd));
        decimal? pf = losses > 0m ? gains / losses : null;
        decimal bnh = (dates[^1].BtcClose / dates[0].BtcClose - 1m - 2m * sideCost) * 100m;
        return new BacktestReport
        {
            InitialCapital = settings.InitialCapital, FinalCapital = Math.Round(capital, 2),
            StrategyTotalReturnPercent = (capital / settings.InitialCapital - 1m) * 100m,
            BuyAndHoldReturnPercent = bnh, EvaluationStart = start, EvaluationEnd = dates[^1].Date,
            TotalTrades = trades.Count, WinningTrades = trades.Count(t => t.IsWin), LosingTrades = trades.Count(t => !t.IsWin),
            WinRatePercent = trades.Count > 0 ? 100m * trades.Count(t => t.IsWin) / trades.Count : 0m,
            ProfitFactor = pf ?? 0m, ProfitFactorValue = pf,
            StrategyMaxDrawdownPercent = Drawdown(equity),
            BuyAndHoldMaxDrawdownPercent = Drawdown(dates.Select(d => d.BtcClose).ToList()),
            SharpeRatio = (decimal)sharpe, Trades = trades,
            Assumptions =
            [
                "Daily expanding-window retraining with only matured 7-day labels; chronological purged validation.",
                "One unlevered paper position at a time; close-price entry and daily mark-to-market drawdown/Sharpe.",
                $"Costs per side: fee {settings.FeePerSide:P3}, slippage {settings.SlippagePerSide:P3}. Short borrow/funding is excluded.",
                "Stop first when both barriers occur in one daily candle. Adverse opening gaps fill at the open.",
                "Open positions are explicitly liquidated at the final data close (END_OF_DATA). No portfolio CAGR is inferred from the overlapping signal journal."
            ]
        };
    }

    private static decimal Drawdown(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0) return 0m;
        decimal peak = values[0], max = 0m;
        foreach (var v in values) { peak = Math.Max(peak, v); if (peak > 0m) max = Math.Max(max, (peak - v) / peak); }
        return max * 100m;
    }
}
