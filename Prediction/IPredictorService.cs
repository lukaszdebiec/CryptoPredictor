using CryptoPredictor.Backtest;
using CryptoPredictor.Data;

namespace CryptoPredictor.Prediction;

public interface IPredictorService
{
    bool IsTrained { get; }
    ModelTrainingMetrics? LastTrainingMetrics { get; }

    ModelTrainingMetrics TrainModel(IReadOnlyList<ModelTrainingSample> dataset);
    MarketPrediction Predict(DailyFeatures todayFeatures, DailyMarketData todayMarket);
}
