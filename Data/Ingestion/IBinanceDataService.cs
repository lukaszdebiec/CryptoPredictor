namespace CryptoPredictor.Data.Ingestion;

public record BinanceKline(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

public interface IBinanceDataService
{
    Task<IReadOnlyList<BinanceKline>> GetDailyKlinesAsync(string symbol = "BTCUSDT", int limit = 500, CancellationToken ct = default);
    Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyFundingRatesAsync(string symbol = "BTCUSDT", int limit = 1000, CancellationToken ct = default);
    Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyOpenInterestAsync(string symbol = "BTCUSDT", int limit = 500, CancellationToken ct = default);
}
