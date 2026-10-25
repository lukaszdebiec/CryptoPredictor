namespace CryptoPredictor.Data.Intraday;

public sealed record MarketCandle4h
{
    public DateTimeOffset Timestamp { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }

    // Futures Orderflow & Derivatives (Binance Futures)
    public decimal? TakerBuySellRatio { get; init; }
    public decimal? TakerBuyVolume { get; init; }
    public decimal? TakerSellVolume { get; init; }
    public decimal? OpenInterestUsd { get; init; }
    public decimal? FundingRate { get; init; }

    // Session Tag: "Asia", "London", "NewYork", "Weekend"
    public string Session { get; init; } = "Unknown";
}
