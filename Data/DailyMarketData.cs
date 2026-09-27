namespace CryptoPredictor.Data;

public sealed record DailyMarketData
{
    public DateOnly Date { get; init; }

    // Price
    public decimal BtcOpen { get; init; }
    public decimal BtcHigh { get; init; }
    public decimal BtcLow { get; init; }
    public decimal BtcClose { get; init; }
    public decimal BtcVolume { get; init; }

    public decimal TotalCryptoMarketCap { get; init; }
    public decimal TotalCryptoVolume { get; init; }
    public decimal BtcDominance { get; init; }

    // Futures
    public decimal FuturesOpenInterestUsd { get; init; }
    public decimal FundingRate { get; init; }
    public decimal FuturesBasis { get; init; }

    // Liquidations
    public decimal LongLiquidationsUsd { get; init; }
    public decimal ShortLiquidationsUsd { get; init; }

    // Options
    public decimal OptionsOpenInterestUsd { get; init; }
    public decimal OptionsCallOpenInterestUsd { get; init; }
    public decimal OptionsPutOpenInterestUsd { get; init; }

    public decimal OptionsVolumeUsd { get; init; }
    public decimal OptionsCallVolumeUsd { get; init; }
    public decimal OptionsPutVolumeUsd { get; init; }

    public decimal BtcDvol { get; init; }
    public decimal Options25DeltaSkew { get; init; }
    public decimal OptionsMaxPain { get; init; }

    public int DaysToNearestMajorOptionsExpiry { get; init; }
    public decimal NearestMajorExpiryOpenInterestUsd { get; init; }

    // ETF
    public decimal BitcoinEtfNetFlowUsd { get; init; }
    public decimal BitcoinEtfAssetsUsd { get; init; }

    // Flow / positioning
    public decimal CoinbasePremium { get; init; }

    // On-chain
    public decimal Mvrv { get; init; }
    public decimal Sopr { get; init; }
    public decimal SthSopr { get; init; }
    public decimal LthSopr { get; init; }
    public decimal ExchangeNetflowBtc { get; init; }
    public decimal RealizedProfitUsd { get; init; }
    public decimal RealizedLossUsd { get; init; }

    // Stablecoins
    public decimal StablecoinMarketCapUsd { get; init; }

    // Macro
    public decimal Dxy { get; init; }
    public decimal NasdaqClose { get; init; }
    public decimal Sp500Close { get; init; }
    public decimal Vix { get; init; }
    public decimal Us10YearYield { get; init; }
    public decimal? GlobalM2 { get; init; }

    // Sentiment
    public int FearGreedIndex { get; init; }

    // CFTC - weekly observations
    public decimal? CftcManagedMoneyLong { get; init; }
    public decimal? CftcManagedMoneyShort { get; init; }
    public decimal? CftcDealerLong { get; init; }
    public decimal? CftcDealerShort { get; init; }
}