namespace CryptoPredictor.Prediction.Trajectory;

/// <summary>Small deterministic ridge regression. All moments are fitted on training inputs only.</summary>
internal sealed class RidgeRegressor
{
    private readonly double[] _mean, _sd, _weights;
    private RidgeRegressor(double[] mean, double[] sd, double[] weights) => (_mean, _sd, _weights) = (mean, sd, weights);

    public static RidgeRegressor Fit(IReadOnlyList<double[]> inputs, IReadOnlyList<double> targets)
    {
        int n = inputs.Count, p = inputs[0].Length;
        if (n != targets.Count || n == 0 || inputs.Any(x => x.Length != p || x.Any(v => !double.IsFinite(v))) || targets.Any(v => !double.IsFinite(v)))
            throw new ArgumentException("Regression requires finite, aligned inputs and targets.");
        var mean = Enumerable.Range(0, p).Select(j => inputs.Average(x => x[j])).ToArray();
        var sd = Enumerable.Range(0, p).Select(j => Math.Max(1e-8, Math.Sqrt(inputs.Average(x => Math.Pow(x[j] - mean[j], 2))))).ToArray();
        var a = new double[p + 1, p + 2];
        for (int k = 0; k < n; k++)
        {
            var x = new double[p + 1]; x[0] = 1d;
            for (int j = 0; j < p; j++) x[j + 1] = Math.Clamp((inputs[k][j] - mean[j]) / sd[j], -5d, 5d);
            for (int i = 0; i <= p; i++)
            {
                for (int j = 0; j <= p; j++) a[i, j] += x[i] * x[j] / n;
                a[i, p + 1] += x[i] * targets[k] / n;
            }
        }
        // The intercept is not penalized. Objective: mean squared error + lambda * ||weights||².
        for (int j = 1; j <= p; j++) a[j, j] += TrajectoryPolicy.RidgePenalty;
        for (int j = 0; j <= p; j++)
        {
            int pivot = j;
            for (int i = j + 1; i <= p; i++) if (Math.Abs(a[i, j]) > Math.Abs(a[pivot, j])) pivot = i;
            if (Math.Abs(a[pivot, j]) < 1e-12) throw new InvalidOperationException("Singular regression system.");
            for (int k = j; k <= p + 1; k++) (a[j, k], a[pivot, k]) = (a[pivot, k], a[j, k]);
            double divisor = a[j, j];
            for (int k = j; k <= p + 1; k++) a[j, k] /= divisor;
            for (int i = 0; i <= p; i++)
            {
                if (i == j) continue;
                double factor = a[i, j];
                for (int k = j; k <= p + 1; k++) a[i, k] -= factor * a[j, k];
            }
        }
        return new RidgeRegressor(mean, sd, Enumerable.Range(0, p + 1).Select(j => a[j, p + 1]).ToArray());
    }

    public double Predict(double[] x)
    {
        double y = _weights[0];
        for (int j = 0; j < _mean.Length; j++) y += _weights[j + 1] * Math.Clamp((x[j] - _mean[j]) / _sd[j], -5d, 5d);
        return y;
    }
}
