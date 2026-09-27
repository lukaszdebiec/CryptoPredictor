namespace CryptoPredictor.Backtest;

public sealed record BacktestSettings
{
    public decimal InitialCapital { get; init; } = 10_000m;
    public decimal EntryProbabilityThreshold { get; init; } = 0.60m; // Buy when probability >= 60%
    public int HoldingPeriodDays { get; init; } = 7;
    public decimal? StopLossPercent { get; init; } = 0.04m; // Optional stop-loss: -4%
    public decimal? TakeProfitPercent { get; init; } = 0.08m; // Optional take-profit: +8%
}

public sealed record TradeRecord
{
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
