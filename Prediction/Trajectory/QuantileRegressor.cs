namespace CryptoPredictor.Prediction.Trajectory;

/// <summary>
/// Deterministic L2-regularized quantile regression trained via Iteratively Reweighted Least Squares (IRLS)
/// on smoothed pinball loss. All feature standardization moments are fitted on training inputs only.
/// </summary>
internal sealed class QuantileRegressor
{
    private const int MaxIterations = 25;
    private const double ConvergenceTolerance = 1e-7;

    private readonly double[] _mean;
    private readonly double[] _sd;
    private readonly double[] _weights;

    private QuantileRegressor(double[] mean, double[] sd, double[] weights)
        => (_mean, _sd, _weights) = (mean, sd, weights);

    public static QuantileRegressor Fit(
        IReadOnlyList<double[]> inputs,
        IReadOnlyList<double> targets,
        double quantile,
        double slopePenalty,
        double interceptPenalty,
        double smoothing = TrajectoryPolicy.PinballSmoothing)
    {
        if (inputs.Count == 0 || inputs.Count != targets.Count)
            throw new ArgumentException("Regression requires non-empty aligned inputs and targets.");
        if (quantile is <= 0d or >= 1d)
            throw new ArgumentOutOfRangeException(nameof(quantile));

        int n = inputs.Count, p = inputs[0].Length;
        if (inputs.Any(x => x.Length != p || x.Any(v => !double.IsFinite(v))) || targets.Any(v => !double.IsFinite(v)))
            throw new ArgumentException("Regression requires finite, aligned inputs and targets.");

        var mean = Enumerable.Range(0, p).Select(j => inputs.Average(x => x[j])).ToArray();
        var sd = Enumerable.Range(0, p).Select(j => Math.Max(1e-8, Math.Sqrt(inputs.Average(x => Math.Pow(x[j] - mean[j], 2))))).ToArray();

        var z = new double[n][];
        for (int k = 0; k < n; k++)
        {
            var row = new double[p + 1];
            row[0] = 1d;
            for (int j = 0; j < p; j++)
                row[j + 1] = Math.Clamp((inputs[k][j] - mean[j]) / sd[j], -5d, 5d);
            z[k] = row;
        }

        var weights = new double[p + 1];
        double bias = quantile - 0.5d;
        double epsSq = smoothing * smoothing;

        for (int iter = 0; iter < MaxIterations; iter++)
        {
            var a = new double[p + 1, p + 2];
            for (int k = 0; k < n; k++)
            {
                var row = z[k];
                double pred = 0d;
                for (int j = 0; j <= p; j++) pred += row[j] * weights[j];

                double residual = targets[k] - pred;
                double invScale = 0.5d / Math.Sqrt(residual * residual + epsSq);
                double rhsWeight = invScale * targets[k] + bias;

                for (int i = 0; i <= p; i++)
                {
                    double scaledRowI = row[i] * invScale / n;
                    for (int j = i; j <= p; j++)
                        a[i, j] += scaledRowI * row[j];
                    a[i, p + 1] += row[i] * rhsWeight / n;
                }
            }

            for (int i = 0; i <= p; i++)
                for (int j = 0; j < i; j++)
                    a[i, j] = a[j, i];

            a[0, 0] += interceptPenalty;
            for (int j = 1; j <= p; j++)
                a[j, j] += slopePenalty;

            var next = SolveLinearSystem(a, p + 1);
            double maxDelta = 0d;
            for (int j = 0; j <= p; j++)
                maxDelta = Math.Max(maxDelta, Math.Abs(next[j] - weights[j]));

            weights = next;
            if (maxDelta < ConvergenceTolerance)
                break;
        }

        return new QuantileRegressor(mean, sd, weights);
    }

    public double Predict(double[] x)
    {
        double y = _weights[0];
        for (int j = 0; j < _mean.Length; j++)
            y += _weights[j + 1] * Math.Clamp((x[j] - _mean[j]) / _sd[j], -5d, 5d);
        return y;
    }

    private static double[] SolveLinearSystem(double[,] a, int size)
    {
        for (int j = 0; j < size; j++)
        {
            int pivot = j;
            for (int i = j + 1; i < size; i++)
                if (Math.Abs(a[i, j]) > Math.Abs(a[pivot, j]))
                    pivot = i;

            if (Math.Abs(a[pivot, j]) < 1e-12)
                throw new InvalidOperationException("Singular regression system.");

            if (pivot != j)
                for (int k = j; k <= size; k++)
                    (a[j, k], a[pivot, k]) = (a[pivot, k], a[j, k]);

            double divisor = a[j, j];
            for (int k = j; k <= size; k++)
                a[j, k] /= divisor;

            for (int i = 0; i < size; i++)
            {
                if (i == j) continue;
                double factor = a[i, j];
                for (int k = j; k <= size; k++)
                    a[i, k] -= factor * a[j, k];
            }
        }

        var solution = new double[size];
        for (int j = 0; j < size; j++)
            solution[j] = a[j, size];
        return solution;
    }
}

