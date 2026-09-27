namespace CryptoPredictor.Features;

public class FeatureCalculator
{
    public double CalculateSimpleMovingAverage(IEnumerable<double> prices)
    {
        var priceList = prices.ToList();
        return priceList.Count > 0 ? priceList.Average() : 0.0;
    }
}
