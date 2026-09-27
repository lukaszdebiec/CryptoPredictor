using CryptoPredictor.Data;

namespace CryptoPredictor.Backtest;

public enum TurningPointType
{
    None = 0,
    LocalBottom = 1,
    LocalTop = 2
}

public sealed record LabeledMarketDay
{
    public DateOnly Date { get; init; }
    public decimal BtcClose { get; init; }
    public TurningPointType TurningPoint { get; init; }

    public bool IsLocalBottom => TurningPoint == TurningPointType.LocalBottom;
    public bool IsLocalTop => TurningPoint == TurningPointType.LocalTop;

    public decimal? ForwardReturn3d { get; init; }
    public decimal? ForwardReturn7d { get; init; }
    public decimal? ForwardReturn14d { get; init; }

    public bool? IsForward7dBullish { get; init; }
}

public sealed record ModelTrainingSample
{
    public DateOnly Date { get; init; }
    public DailyFeatures Features { get; init; } = default!;
    public LabeledMarketDay Label { get; init; } = default!;
}
