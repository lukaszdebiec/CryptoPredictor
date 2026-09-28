namespace CryptoPredictor.Data.Ingestion;

public interface IEtfDataService
{
    /// <summary>
    /// Fetches historical and latest daily net flows for US Spot Bitcoin ETFs mapped by DateOnly.
    /// Values are in USD (e.g. +655_300_000 for $655.3M inflow, -95_100_000 for $95.1M outflow).
    /// </summary>
    Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyEtfNetFlowsAsync(CancellationToken ct = default);
}
