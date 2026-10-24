namespace CryptoPredictor.Features;

public sealed record LiquidityLevels
{
    public decimal UpperWall50x { get; init; }
    public decimal UpperWall25x { get; init; }
    public decimal UpperWall10x { get; init; }
    public decimal LowerWall50x { get; init; }
    public decimal LowerWall25x { get; init; }
    public decimal LowerWall10x { get; init; }
    public decimal NearestUpperWall { get; init; }
    public decimal NearestLowerWall { get; init; }
    public decimal DistToUpperWallPercent { get; init; }
    public decimal DistToLowerWallPercent { get; init; }
    public bool IsLongLiquiditySwept { get; init; }
    public bool IsShortLiquiditySwept { get; init; }
    public string DominantLiquiditySide { get; init; } = "";
}

public interface ILiquidityEngine
{
    LiquidityLevels CalculateLiquidityLevels(IReadOnlyList<CryptoPredictor.Data.DailyMarketData> history, int currentIndex);
    IReadOnlyList<LiquidityLevels> CalculateHistory(IReadOnlyList<CryptoPredictor.Data.DailyMarketData> history);
}
