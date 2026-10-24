namespace CryptoPredictor.Data.Ingestion;

public sealed record MacroHistoryResult(
    IReadOnlyDictionary<DateOnly, decimal> Dxy,
    IReadOnlyDictionary<DateOnly, decimal> Us10YearYield,
    IReadOnlyDictionary<DateOnly, decimal> Sp500,
    IReadOnlyDictionary<DateOnly, decimal> Nasdaq);

public interface IMacroDataService
{
    Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailySeriesAsync(string symbol, string range = "1y", CancellationToken ct = default);
    Task<MacroHistoryResult> GetMacroHistoryAsync(string range = "1y", CancellationToken ct = default);
}
