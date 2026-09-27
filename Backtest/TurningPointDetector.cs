namespace CryptoPredictor.Backtest;

public class TurningPointDetector
{
    // Placeholder logic for detecting market turning points / reversals
    public bool IsPeak(double prev, double curr, double next) => curr > prev && curr > next;
    public bool IsTrough(double prev, double curr, double next) => curr < prev && curr < next;
}
