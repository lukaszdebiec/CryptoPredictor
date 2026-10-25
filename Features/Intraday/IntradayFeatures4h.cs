namespace CryptoPredictor.Features.Intraday;

public sealed record IntradayFeatures4h
{
    public DateTimeOffset Timestamp { get; init; }
    public decimal Close { get; init; }

    // Price Returns
    public decimal Return4h { get; init; }
    public decimal Return12h { get; init; }
    public decimal Return24h { get; init; }

    // Technical Indicators
    public decimal KaufmanEfficiencyRatio6 { get; init; } // 6x 4h = 24h
    public decimal Rsi14 { get; init; }
    public decimal Atr14Pct { get; init; }

    // Orderflow & Derivatives
    public decimal TakerBuySellRatio { get; init; } = 1.0m;
    public decimal TakerAggressionZScore { get; init; }
    public decimal OiChange24hPct { get; init; }
    public decimal FundingRate { get; init; }

    // Wyckoff / Liquidity Sweeps (SFP)
    public bool IsSfpHighSwept { get; init; } // Bearish Upthrust (wybicie szczytu z 24h i powrót)
    public bool IsSfpLowSwept { get; init; }  // Bullish Spring (wybicie dołka z 24h i powrót)

    // Session Context
    public string Session { get; init; } = "Unknown";
}
