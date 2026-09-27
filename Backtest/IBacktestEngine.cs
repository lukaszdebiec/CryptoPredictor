using CryptoPredictor.Data;
using CryptoPredictor.Features;
using CryptoPredictor.Prediction;

namespace CryptoPredictor.Backtest;

public interface IBacktestEngine
{
    BacktestReport RunSimulation(
        IReadOnlyList<DailyMarketData> marketData,
        IReadOnlyList<DailyFeatures> features,
        IPredictorService predictor,
        BacktestSettings? settings = null);
}
