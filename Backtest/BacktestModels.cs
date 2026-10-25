namespace CryptoPredictor.Backtest;

public sealed record BacktestSettings
{
    public decimal InitialCapital { get; init; } = 10_000m;
    public decimal EntryProbabilityThreshold { get; init; } = 0.60m; // Buy when probability >= 60%
    public int HoldingPeriodDays { get; init; } = 7;
    public decimal? StopLossPercent { get; init; } // null: use the forecast's risk policy
    public decimal? TakeProfitPercent { get; init; }
    public decimal FeePerSide { get; init; } = 0.0005m;
    public decimal SlippagePerSide { get; init; } = 0.0005m;
    public bool AllowShorts { get; init; } = true;
}

public sealed record TradeRecord
{
    public string Side { get; init; } = "LONG";
    public DateOnly EntryDate { get; init; }
    public decimal EntryPrice { get; init; }
    public DateOnly ExitDate { get; init; }
    public decimal ExitPrice { get; init; }
    public decimal ReturnPercent { get; init; }
    public decimal PnLUsd { get; init; }
    public int HoldingDays { get; init; }
    public string ExitReason { get; init; } = default!; // "HOLDING_EXPIRED", "TAKE_PROFIT", "STOP_LOSS"
    public bool IsWin => ReturnPercent > 0;
}

public sealed record BacktestReport
{
    public string ModelVersion { get; init; } = CryptoPredictor.Prediction.PredictionPolicy.Version;
    public DateOnly? EvaluationStart { get; init; }
    public DateOnly? EvaluationEnd { get; init; }
    public decimal? ProfitFactorValue { get; init; }
    public IReadOnlyList<string> Assumptions { get; init; } = Array.Empty<string>();
    public decimal InitialCapital { get; init; }
    public decimal FinalCapital { get; init; }
    public decimal StrategyTotalReturnPercent { get; init; }
    public decimal BuyAndHoldReturnPercent { get; init; }

    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public decimal WinRatePercent { get; init; }

    public decimal ProfitFactor { get; init; }
    public decimal StrategyMaxDrawdownPercent { get; init; }
    public decimal BuyAndHoldMaxDrawdownPercent { get; init; }
    public decimal SharpeRatio { get; init; }

    public IReadOnlyList<TradeRecord> Trades { get; init; } = Array.Empty<TradeRecord>();
}
