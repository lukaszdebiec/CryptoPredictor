namespace CryptoPredictor.Data;

public sealed record DailyMarketData
{
    public DateOnly Date { get; init; } // available

    // Price
    public decimal BtcOpen { get; init; } // available (Binance, Coinbase, Yahoo Finance)
    public decimal BtcHigh { get; init; } // available (Binance, Coinbase, Yahoo Finance)
    public decimal BtcLow { get; init; } // available (Binance, Coinbase, Yahoo Finance)
    public decimal BtcClose { get; init; } // available (Binance, Coinbase, Yahoo Finance)
    public decimal BtcVolume { get; init; } // available (Binance, Coinbase, Yahoo Finance)

    public decimal TotalCryptoMarketCap { get; init; } // available (CoinGecko API /global)
    public decimal TotalCryptoVolume { get; init; } // available (CoinGecko API /global)
    public decimal BtcDominance { get; init; } // available (CoinGecko API /global)

    // Futures
    public decimal FuturesOpenInterestUsd { get; init; } // available (Binance Futures, Bybit, OKX darmowe API)
    public decimal FundingRate { get; init; } // available (Binance Futures, Bybit darmowe API)
    public decimal FuturesBasis { get; init; } // available (wyliczane z różnicy cen futures i spot)

    // Liquidations
    public decimal LongLiquidationsUsd { get; init; } // not available (brak darmowej historii; wymaga płatnego CoinGlass VIP / CryptoQuant)
    public decimal ShortLiquidationsUsd { get; init; } // not available (brak darmowej historii; wymaga płatnego CoinGlass VIP / CryptoQuant)

    // Options
    public decimal OptionsOpenInterestUsd { get; init; } // available (Deribit public API)
    public decimal OptionsCallOpenInterestUsd { get; init; } // available (Deribit public API)
    public decimal OptionsPutOpenInterestUsd { get; init; } // available (Deribit public API)

    public decimal OptionsVolumeUsd { get; init; } // available (Deribit public API)
    public decimal OptionsCallVolumeUsd { get; init; } // available (Deribit public API)
    public decimal OptionsPutVolumeUsd { get; init; } // available (Deribit public API)

    public decimal BtcDvol { get; init; } // available (Deribit DVOL historical API)
    public decimal Options25DeltaSkew { get; init; } // not available (brak darmowej historii time-series; wymaga płatnego Amberdata / Laevitas)
    public decimal OptionsMaxPain { get; init; } // available (wyliczane z darmowego OI Deribit per strike)

    public int DaysToNearestMajorOptionsExpiry { get; init; } // available (wyliczane z kalendarza wygasania Deribit)
    public decimal NearestMajorExpiryOpenInterestUsd { get; init; } // available (Deribit public API)

    // ETF
    public decimal BitcoinEtfNetFlowUsd { get; init; } // available (Farside Investors / SoSoValue scraping)
    public decimal BitcoinEtfAssetsUsd { get; init; } // available (raporty iShares IBIT, Farside)

    // Flow / positioning
    public decimal CoinbasePremium { get; init; } // available (wyliczane: Coinbase Spot Close - Binance Spot Close)

    // On-chain
    public decimal Mvrv { get; init; } // available (CoinMetrics Community API: CapMrktCurUSD / CapRealUSD)
    public decimal Sopr { get; init; } // not available (wymaga płatnego Glassnode Tier 2+ lub CryptoQuant)
    public decimal SthSopr { get; init; } // not available (wymaga płatnego Glassnode Tier 2+)
    public decimal LthSopr { get; init; } // not available (wymaga płatnego Glassnode Tier 2+)
    public decimal ExchangeNetflowBtc { get; init; } // not available (wymaga płatnego Glassnode / CryptoQuant; autorskie klastrowanie portfeli)
    public decimal RealizedProfitUsd { get; init; } // not available (wymaga płatnego Glassnode Tier 2+)
    public decimal RealizedLossUsd { get; init; } // not available (wymaga płatnego Glassnode Tier 2+)

    // Stablecoins
    public decimal StablecoinMarketCapUsd { get; init; } // available (DefiLlama API darmowe)

    // Macro
    public decimal Dxy { get; init; } // available (Yahoo Finance DX-Y.NYB, FRED)
    public decimal NasdaqClose { get; init; } // available (Yahoo Finance ^IXIC)
    public decimal Sp500Close { get; init; } // available (Yahoo Finance ^GSPC, FRED)
    public decimal Vix { get; init; } // available (Yahoo Finance ^VIX, FRED)
    public decimal Us10YearYield { get; init; } // available (FRED DGS10)
    public decimal? GlobalM2 { get; init; } // not available (zagregowane Global M2 wymaga płatnego Bloomberg / MacroMicro; darmowy w FRED jest tylko US M2)

    // Sentiment
    public int FearGreedIndex { get; init; } // available (Alternative.me API darmowe)

    // CFTC - weekly observations
    public decimal? CftcManagedMoneyLong { get; init; } // available (CFTC.gov Socrata Open Data API)
    public decimal? CftcManagedMoneyShort { get; init; } // available (CFTC.gov Socrata Open Data API)
    public decimal? CftcDealerLong { get; init; } // available (CFTC.gov Socrata Open Data API)
    public decimal? CftcDealerShort { get; init; } // available (CFTC.gov Socrata Open Data API)
}