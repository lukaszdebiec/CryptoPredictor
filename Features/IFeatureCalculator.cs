using CryptoPredictor.Data;

namespace CryptoPredictor.Features;

public interface IFeatureCalculator
{
    /// <summary>
    /// Calculates engineered features for each day in chronological order from historical market data.
    /// </summary>
    /// <param name="history">List of daily market observations (does not have to be pre-sorted).</param>
    /// <returns>List of calculated daily features ordered by date ascending.</returns>
    IReadOnlyList<DailyFeatures> Calculate(IEnumerable<DailyMarketData> history);
}
