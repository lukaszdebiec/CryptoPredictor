using CryptoPredictor.Data;
using CryptoPredictor.Features;
using CryptoPredictor.Prediction;

namespace CryptoPredictor.Backtest;

public class BacktestEngine : IBacktestEngine
{
    public BacktestReport RunSimulation(
        IReadOnlyList<DailyMarketData> marketData,
        IReadOnlyList<DailyFeatures> features,
        IPredictorService predictor,
        BacktestSettings? settings = null)
    {
        settings = new BacktestSettings
        {
            InitialCapital = settings?.InitialCapital > 0 ? settings.InitialCapital : 10_000m,
            EntryProbabilityThreshold = settings?.EntryProbabilityThreshold > 0 ? settings.EntryProbabilityThreshold : 0.60m,
            HoldingPeriodDays = settings?.HoldingPeriodDays > 0 ? settings.HoldingPeriodDays : 7,
            StopLossPercent = settings?.StopLossPercent is > 0 ? settings.StopLossPercent : 0.04m,
            TakeProfitPercent = settings?.TakeProfitPercent is > 0 ? settings.TakeProfitPercent : 0.08m
        };

        if (marketData == null || marketData.Count < 10 || features == null || features.Count < 10)
        {
            return new BacktestReport
            {
                InitialCapital = settings.InitialCapital,
                FinalCapital = settings.InitialCapital
            };
        }

        var sortedMarket = marketData.OrderBy(d => d.Date).ToList();
        var featureByDate = features.ToDictionary(f => f.Date);

        // Buy & Hold benchmark
        var firstClose = sortedMarket.First().BtcClose;
        var lastClose = sortedMarket.Last().BtcClose;
        var buyAndHoldReturn = firstClose > 0 ? (lastClose - firstClose) / firstClose : 0m;

        // Buy & Hold Max Drawdown
        decimal bnhPeak = firstClose;
        decimal bnhMaxDd = 0m;
        foreach (var day in sortedMarket)
        {
            if (day.BtcHigh > bnhPeak) bnhPeak = day.BtcHigh;
            var dd = bnhPeak > 0 ? (day.BtcLow - bnhPeak) / bnhPeak : 0m;
            if (dd < bnhMaxDd) bnhMaxDd = dd;
        }

        var trades = new List<TradeRecord>();
        decimal currentCapital = settings.InitialCapital;
        decimal strategyPeak = currentCapital;
        decimal strategyMaxDd = 0m;

        int i = 0;
        while (i < sortedMarket.Count - 1)
        {
            var day = sortedMarket[i];

            if (!featureByDate.TryGetValue(day.Date, out var feat))
            {
                i++;
                continue;
            }

            var prediction = predictor.Predict(feat, day);

            // Signal to enter long trade
            if (prediction.BullishProbability >= settings.EntryProbabilityThreshold)
            {
                var entryPrice = day.BtcClose;
                var entryDate = day.Date;
                var entryIndex = i;

                // Walk forward to find exit
                int exitIndex = i + 1;
                decimal exitPrice = sortedMarket[exitIndex].BtcClose;
                string exitReason = "HOLDING_EXPIRED";

                for (int j = i + 1; j < sortedMarket.Count; j++)
                {
                    var forwardDay = sortedMarket[j];
                    int holdingDays = j - entryIndex;

                    // 1. Check Stop Loss
                    if (settings.StopLossPercent.HasValue)
                    {
                        var lossPercent = (forwardDay.BtcLow - entryPrice) / entryPrice;
                        if (lossPercent <= -settings.StopLossPercent.Value)
                        {
                            exitPrice = entryPrice * (1m - settings.StopLossPercent.Value);
                            exitIndex = j;
                            exitReason = "STOP_LOSS";
                            break;
                        }
                    }

                    // 2. Check Take Profit
                    if (settings.TakeProfitPercent.HasValue)
                    {
                        var gainPercent = (forwardDay.BtcHigh - entryPrice) / entryPrice;
                        if (gainPercent >= settings.TakeProfitPercent.Value)
                        {
                            exitPrice = entryPrice * (1m + settings.TakeProfitPercent.Value);
                            exitIndex = j;
                            exitReason = "TAKE_PROFIT";
                            break;
                        }
                    }

                    // 3. Check holding period expiry
                    if (holdingDays >= settings.HoldingPeriodDays)
                    {
                        exitPrice = forwardDay.BtcClose;
                        exitIndex = j;
                        exitReason = "HOLDING_EXPIRED";
                        break;
                    }

                    exitPrice = forwardDay.BtcClose;
                    exitIndex = j;
                }

                // Record trade result
                var returnPercent = entryPrice > 0 ? (exitPrice - entryPrice) / entryPrice : 0m;
                var pnl = currentCapital * returnPercent;
                currentCapital += pnl;

                // Drawdown tracking
                if (currentCapital > strategyPeak)
                    strategyPeak = currentCapital;

                var stratDd = strategyPeak > 0 ? (currentCapital - strategyPeak) / strategyPeak : 0m;
                if (stratDd < strategyMaxDd)
                    strategyMaxDd = stratDd;

                trades.Add(new TradeRecord
                {
                    EntryDate = entryDate,
                    EntryPrice = entryPrice,
                    ExitDate = sortedMarket[exitIndex].Date,
                    ExitPrice = Math.Round(exitPrice, 2),
                    ReturnPercent = Math.Round(returnPercent, 4),
                    PnLUsd = Math.Round(pnl, 2),
                    HoldingDays = exitIndex - entryIndex,
                    ExitReason = exitReason
                });

                // Advance past this trade
                i = exitIndex;
            }
            else
            {
                i++;
            }
        }

        // Summary metrics
        int totalTrades = trades.Count;
        int winTrades = trades.Count(t => t.IsWin);
        int lossTrades = trades.Count(t => !t.IsWin);
        decimal winRate = totalTrades > 0 ? (decimal)winTrades / totalTrades * 100m : 0m;

        decimal grossProfit = trades.Where(t => t.PnLUsd > 0).Sum(t => t.PnLUsd);
        decimal grossLoss = Math.Abs(trades.Where(t => t.PnLUsd < 0).Sum(t => t.PnLUsd));
        decimal profitFactor = grossLoss > 0 ? grossProfit / grossLoss : (grossProfit > 0 ? 99m : 1m);

        decimal strategyReturn = settings.InitialCapital > 0 
            ? (currentCapital - settings.InitialCapital) / settings.InitialCapital * 100m 
            : 0m;

        // Sharpe Ratio (simplified annualized on trade returns)
        decimal sharpeRatio = 0m;
        if (trades.Count > 1)
        {
            var avgReturn = trades.Average(t => (double)t.ReturnPercent);
            var stdReturn = Math.Sqrt(trades.Sum(t => Math.Pow((double)t.ReturnPercent - avgReturn, 2)) / (trades.Count - 1));
            if (stdReturn > 1e-6)
            {
                sharpeRatio = (decimal)(avgReturn / stdReturn * Math.Sqrt(365.0 / Math.Max(1, settings.HoldingPeriodDays)));
            }
        }

        return new BacktestReport
        {
            InitialCapital = settings.InitialCapital,
            FinalCapital = Math.Round(currentCapital, 2),
            StrategyTotalReturnPercent = Math.Round(strategyReturn, 2),
            BuyAndHoldReturnPercent = Math.Round(buyAndHoldReturn * 100m, 2),
            TotalTrades = totalTrades,
            WinningTrades = winTrades,
            LosingTrades = lossTrades,
            WinRatePercent = Math.Round(winRate, 2),
            ProfitFactor = Math.Round(profitFactor, 2),
            StrategyMaxDrawdownPercent = Math.Round(Math.Abs(strategyMaxDd) * 100m, 2),
            BuyAndHoldMaxDrawdownPercent = Math.Round(Math.Abs(bnhMaxDd) * 100m, 2),
            SharpeRatio = Math.Round(sharpeRatio, 2),
            Trades = trades
        };
    }
}
