using System.Globalization;
using CryptoPredictor.Data;

namespace CryptoPredictor.Prediction.Confluence;

public interface IConfluenceIndicatorService
{
    ConfluenceForecast Evaluate(
        IReadOnlyList<DailyMarketData> marketHistory,
        IReadOnlyList<DailyFeatures> features,
        int horizonDays = 7,
        DateOnly? asOfDate = null,
        IReadOnlyList<DailyMarketData>? longPriceHistory = null);
}

public sealed class ConfluenceIndicatorService : IConfluenceIndicatorService
{
    public ConfluenceForecast Evaluate(
        IReadOnlyList<DailyMarketData> marketHistory,
        IReadOnlyList<DailyFeatures> features,
        int horizonDays = 7,
        DateOnly? asOfDate = null,
        IReadOnlyList<DailyMarketData>? longPriceHistory = null)
    {
        if (horizonDays is <= 0 or > 90)
            throw new ArgumentOutOfRangeException(nameof(horizonDays), "Horizon must be between 1 and 90 days.");

        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = asOfDate ?? todayUtc.AddDays(-1);
        if (cutoff >= todayUtc)
            throw new ArgumentException("Confluence origin must be a closed UTC day.", nameof(asOfDate));

        var bars = marketHistory
            .Where(b => b.Date <= cutoff && b.Date < todayUtc && b.BtcClose > 0m)
            .OrderBy(b => b.Date)
            .ToList();

        var latest = bars.LastOrDefault();
        var empty = new ConfluenceForecast
        {
            Status = "INSUFFICIENT_DATA",
            Message = "Za mało ciągłych zamkniętych świec dziennych (wymagane min. 50 dni rozgrzewki wskaźników).",
            AsOfDate = latest?.Date,
            HorizonDays = horizonDays,
            ReferenceClose = latest?.BtcClose,
            Bias = "NEUTRAL",
            BiasLabel = "BRAK DANYCH"
        };

        if (latest == null)
            return empty;

        if (asOfDate.HasValue && latest.Date != cutoff)
            return empty with { Message = $"Brak zamkniętej świecy dziennej dla daty {cutoff:yyyy-MM-dd}." };

        var feat = features.FirstOrDefault(f => f.Date == latest.Date);
        if (feat == null || feat.ConsecutivePriceDays < ConfluencePolicy.WarmupDays)
            return empty;

        bool isLongHorizon = horizonDays >= 14;
        var rawPillars = BuildPillars(latest, feat, isLongHorizon);

        double activeWeightSum = rawPillars.Where(p => p.IsAvailable).Sum(p => p.NominalWeight);
        if (activeWeightSum <= 0d)
            return empty;

        var finalizedPillars = rawPillars.Select(p =>
        {
            double effWeight = p.IsAvailable ? p.NominalWeight / activeWeightSum : 0d;
            double pts = p.IsAvailable ? p.RawScore * effWeight * 100.0 : 0d;
            return p with
            {
                EffectiveWeight = Math.Round(effWeight, 4),
                WeightedPoints = Math.Round(pts, 2)
            };
        }).ToList();

        double totalScore = Math.Clamp(finalizedPillars.Sum(p => p.WeightedPoints), -100.0, 100.0);
        double normalizedScore = totalScore / 100.0;

        var (bias, biasLabel) = ClassifyBias(totalScore);

        // Daily volatility anchor blending realized 30d volatility (70%) and Deribit DVOL implied vol (30% when present)
        double rvolDaily = (double)feat.RealizedVolatility30d / Math.Sqrt(365.0);
        double ivolDaily = latest.BtcDvol > 0m ? ((double)latest.BtcDvol / 100.0) / Math.Sqrt(365.0) : rvolDaily;
        double dailyVol = Math.Clamp(rvolDaily > 0d ? 0.70 * rvolDaily + 0.30 * ivolDaily : ivolDaily, 0.005, 0.12);

        double driftScale = horizonDays >= 20 ? ConfluencePolicy.DriftScale30d : ConfluencePolicy.DriftScale7d;
        double refClose = (double)latest.BtcClose;

        var points = new List<ConfluencePoint>(horizonDays + 1)
        {
            new()
            {
                Time = latest.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                HorizonDays = 0,
                ProjectedPrice = latest.BtcClose,
                LowerBandPrice = latest.BtcClose,
                UpperBandPrice = latest.BtcClose,
                ProjectedReturnPercent = 0d
            }
        };

        for (int d = 1; d <= horizonDays; d++)
        {
            double volStep = dailyVol * Math.Sqrt(d);
            double driftLog = driftScale * normalizedScore * volStep;
            double bandHalfWidth = ConfluencePolicy.BandZ80 * volStep;
            double centerTilt = 0.5 * driftLog;

            double proj = refClose * Math.Exp(driftLog);
            double lower = Math.Min(proj, refClose * Math.Exp(centerTilt - bandHalfWidth));
            double upper = Math.Max(proj, refClose * Math.Exp(centerTilt + bandHalfWidth));

            points.Add(new ConfluencePoint
            {
                Time = latest.Date.AddDays(d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                HorizonDays = d,
                ProjectedPrice = Math.Round((decimal)proj, 2),
                LowerBandPrice = Math.Round((decimal)lower, 2),
                UpperBandPrice = Math.Round((decimal)upper, 2),
                ProjectedReturnPercent = Math.Round((proj / refClose - 1.0) * 100.0, 2)
            });
        }

        var cycleAnalog = BuildCycleAnalog(bars, longPriceHistory, latest.Date, latest.BtcClose, dailyVol, horizonDays);
        var endPoint = points[^1];
        string sign = endPoint.ProjectedReturnPercent >= 0 ? "+" : "";

        return new ConfluenceForecast
        {
            Status = "READY",
            Message = $"Konfluencja {horizonDays}D ({latest.Date:yyyy-MM-dd}): {totalScore:+0.0;-0.0;0.0}/100 ({biasLabel}) · Projekcja: ${endPoint.ProjectedPrice:N0} ({sign}{endPoint.ProjectedReturnPercent:0.1}%).",
            AsOfDate = latest.Date,
            HorizonDays = horizonDays,
            ReferenceClose = latest.BtcClose,
            ConfluenceScore = Math.Round(totalScore, 1),
            Bias = bias,
            BiasLabel = biasLabel,
            DailyVolatility = Math.Round(dailyVol, 6),
            ProjectedChangePercent = endPoint.ProjectedReturnPercent,
            ProjectedEndPrice = endPoint.ProjectedPrice,
            Pillars = finalizedPillars,
            Points = points,
            CycleAnalog = cycleAnalog
        };
    }

    private static List<ConfluencePillarScore> BuildPillars(DailyMarketData latest, DailyFeatures f, bool isLongHorizon)
    {
        double wFlow = isLongHorizon ? 0.35 : 0.40;
        double wPos = isLongHorizon ? 0.25 : 0.30;
        double wStruct = isLongHorizon ? 0.10 : 0.20;
        double wMacro = isLongHorizon ? 0.30 : 0.10;

        // 1. Spot & Institutional Flows (Coinbase Premium + 7d Spot ETF Net Flow)
        bool hasCb = f.CoinbasePremiumZScore30d != 0m;
        bool hasEtf = f.EtfFlowZScore30d != 0m;
        double sCb = hasCb ? Math.Tanh((double)f.CoinbasePremiumZScore30d / 1.75) : 0d;
        double sEtf = hasEtf ? Math.Tanh((double)f.EtfFlowZScore30d / 2.00) : 0d;
        bool flowAvail = hasCb || hasEtf;
        double flowRaw = (hasCb && hasEtf)
            ? (isLongHorizon ? 0.60 * sCb + 0.40 * sEtf : 0.75 * sCb + 0.25 * sEtf)
            : (hasCb ? sCb : sEtf);

        string flowSummary = !flowAvail
            ? "Brak aktywnych danych przepływów (waga rozdzielona na pozostałe filary)"
            : string.Create(CultureInfo.InvariantCulture,
                $"Coinbase Z={(hasCb ? $"{f.CoinbasePremiumZScore30d:+0.00;-0.00;0.00}" : "b/d")} · ETF 7d Z={(hasEtf ? $"{f.EtfFlowZScore30d:+0.00;-0.00;0.00}" : "b/d")}");

        // 2. Derivatives Positioning & Contrarian Sentiment (Funding, Fear & Greed extremes, Volume Climax, Liquidity Sweeps)
        bool hasFund = f.FundingZScore30d != 0m;
        bool hasFng = latest.FearGreedIndex > 0;
        var posTerms = new List<(double Weight, double Value)>(3);
        if (hasFund)
            posTerms.Add((0.45, Math.Tanh(-(double)f.FundingZScore30d / 1.75)));
        if (hasFng)
        {
            double x = -(double)f.FearGreedCentered; // +1 at Extreme Fear (0), -1 at Extreme Greed (100)
            posTerms.Add((0.35, Math.Clamp(0.35 * x + 0.65 * x * x * x, -1.0, 1.0)));
        }

        double volExhaust = -Math.Tanh(((double)f.VolumeVs30dAverage - 1.0) * 1.2) * Math.Tanh((double)f.BtcReturn3d / 0.04);
        posTerms.Add((0.20, volExhaust));

        double sweepBonus = f.IsLongLiquiditySwept && !f.IsShortLiquiditySwept ? 0.30
            : f.IsShortLiquiditySwept && !f.IsLongLiquiditySwept ? -0.30 : 0.0;

        double posWeightSum = posTerms.Sum(t => t.Weight);
        double posRaw = Math.Clamp(posTerms.Sum(t => t.Weight * t.Value) / posWeightSum + sweepBonus, -1.0, 1.0);
        string sweepTag = sweepBonus > 0 ? " · Wycięcie SL Long (Spring)" : sweepBonus < 0 ? " · Wycięcie SL Short (Upthrust)" : "";
        string posSummary = string.Create(CultureInfo.InvariantCulture,
            $"Funding Z={(hasFund ? $"{f.FundingZScore30d:+0.00;-0.00;0.00}" : "b/d")} · F&G={(hasFng ? latest.FearGreedIndex.ToString(CultureInfo.InvariantCulture) : "b/d")}{sweepTag}");

        // 3. Price Structure, Fibonacci Swing & Signed KER Impulse
        double sTrend = Math.Tanh((double)f.WeeklyTrendBias / 0.10);
        double gpBoost = f.IsInGoldenPocket30d && f.WeeklyTrendBias >= -0.05m ? 1.25 : 1.0;
        double sFib = Math.Tanh(((double)f.FibRetracementRatio30d - 0.50) * 2.5) * gpBoost;
        double ker = (double)f.KaufmanEfficiencyRatio10d;
        double signedKer = (double)f.SignedKaufmanEfficiencyRatio10d;
        // Moderate impulse aligns with direction; extreme parabolic stretch (>0.55) dampens chasing tops/bottoms
        double sImpulse = ker <= 0.45
            ? 0.35 * signedKer
            : -signedKer * Math.Clamp((ker - 0.45) / 0.55, 0.0, 1.0);
        double structRaw = Math.Clamp(0.35 * sTrend + 0.35 * sFib + 0.30 * sImpulse, -1.0, 1.0);
        string gpTag = f.IsInGoldenPocket30d ? " (Golden Pocket)" : "";
        string structSummary = string.Create(CultureInfo.InvariantCulture,
            $"Trend 50d {f.WeeklyTrendBias * 100m:+0.0;-0.0;0.0}% · Fibo {f.FibRetracementRatio30d * 100m:0}%{gpTag} · KER kier. {f.SignedKaufmanEfficiencyRatio10d:+0.00;-0.00;0.00}");

        // 4. Macro Liquidity (Inverted DXY Z-Score & US10Y 7d change)
        bool hasDxy = latest.Dxy > 0m && f.DxyZScore30d != 0m;
        bool has10y = latest.Us10YearYield > 0m;
        bool macroAvail = hasDxy || has10y;
        double sDxy = hasDxy ? Math.Tanh(-(double)f.DxyZScore30d / 1.75) : 0d;
        double s10y = has10y ? Math.Tanh(-(double)f.Us10YYieldChange7d / 0.25) : 0d;
        double macroRaw = (hasDxy && has10y) ? 0.80 * sDxy + 0.20 * s10y : (hasDxy ? sDxy : s10y);
        string macroSummary = !macroAvail
            ? "Brak aktywnych danych makro (waga rozdzielona na pozostałe filary)"
            : string.Create(CultureInfo.InvariantCulture,
                $"DXY Z={(hasDxy ? $"{f.DxyZScore30d:+0.00;-0.00;0.00}" : "b/d")} · US10Y 7d {(has10y ? $"{f.Us10YYieldChange7d:+0.00;-0.00;0.00} pp" : "b/d")}");

        return
        [
            new()
            {
                Key = "flows",
                Name = "Przepływy Spot & ETF",
                IsAvailable = flowAvail,
                RawScore = Math.Round(flowRaw, 4),
                NominalWeight = wFlow,
                Summary = flowSummary
            },
            new()
            {
                Key = "positioning",
                Name = "Pozycjonowanie & Sentyment",
                IsAvailable = true,
                RawScore = Math.Round(posRaw, 4),
                NominalWeight = wPos,
                Summary = posSummary
            },
            new()
            {
                Key = "structure",
                Name = "Struktura Ceny (Fibo & KER)",
                IsAvailable = true,
                RawScore = Math.Round(structRaw, 4),
                NominalWeight = wStruct,
                Summary = structSummary
            },
            new()
            {
                Key = "macro",
                Name = "Makro Płynność (DXY & 10Y)",
                IsAvailable = macroAvail,
                RawScore = Math.Round(macroRaw, 4),
                NominalWeight = wMacro,
                Summary = macroSummary
            }
        ];
    }

    private static (string Bias, string Label) ClassifyBias(double score) => score switch
    {
        >= 35.0 => ("BULLISH", "SILNA KONFLUENCJA BYCZA"),
        >= 15.0 => ("BULLISH", "UMIARKOWANA KONFLUENCJA BYCZA"),
        <= -35.0 => ("BEARISH", "SILNA KONFLUENCJA NIEDŹWIEDZIA"),
        <= -15.0 => ("BEARISH", "UMIARKOWANA PRESJA PODAŻOWA"),
        _ => ("NEUTRAL", "NEUTRALNA RÓWNOWAGA")
    };

    private static string DescribeFourYearCyclePhase(int year) => (year % 4) switch
    {
        0 => "Rok Halvingu BTC / Wyborów Prezydenckich USA",
        1 => "Rok Pohalwingowej Ekspansji (+1 po Halvingu)",
        2 => "Rok Wyborów Połówkowych USA (Midterms / +2 po Halvingu)",
        _ => "Rok Przedhalwingowej Akumulacji (+3 po Halvingu)"
    };

    private static ConfluenceCycleAnalog BuildCycleAnalog(
        List<DailyMarketData> marketBars,
        IReadOnlyList<DailyMarketData>? longPriceHistory,
        DateOnly asOfDate,
        decimal referenceClose,
        double currentDailyVol,
        int horizonDays)
    {
        string phaseLabel = DescribeFourYearCyclePhase(asOfDate.Year);
        var closeByDate = new Dictionary<DateOnly, double>(marketBars.Count + (longPriceHistory?.Count ?? 0));

        if (longPriceHistory != null)
        {
            foreach (var b in longPriceHistory)
            {
                if (b.Date <= asOfDate && b.BtcClose > 0m)
                    closeByDate[b.Date] = (double)b.BtcClose;
            }
        }
        foreach (var b in marketBars)
        {
            if (b.Date <= asOfDate && b.BtcClose > 0m)
                closeByDate[b.Date] = (double)b.BtcClose;
        }

        if (closeByDate.Count == 0)
        {
            return new ConfluenceCycleAnalog
            {
                Available = false,
                CyclePhaseLabel = phaseLabel,
                Message = "Brak wieloletniej historii cen do wyznaczenia analogu cyklicznego."
            };
        }

        int minYear = closeByDate.Keys.Min(d => d.Year);
        var analogYears = new List<int>();
        var matchedCycleYears = new List<int>();
        var weightedPaths = new List<(double Weight, double[] NormalizedLogReturns)>();

        for (int yr = minYear; yr < asOfDate.Year; yr++)
        {
            int anchorDay = Math.Min(asOfDate.Day, DateTime.DaysInMonth(yr, asOfDate.Month));
            var anchor = new DateOnly(yr, asOfDate.Month, anchorDay);
            if (anchor.AddDays(horizonDays) >= asOfDate)
                continue; // Strictly prior completed outcomes only (zero lookahead leakage)

            if (!closeByDate.TryGetValue(anchor, out double anchorClose) || anchorClose <= 0d)
                continue;

            bool completeForward = true;
            var rawLogRet = new double[horizonDays + 1];
            for (int d = 1; d <= horizonDays; d++)
            {
                if (!closeByDate.TryGetValue(anchor.AddDays(d), out double fwdClose) || fwdClose <= 0d)
                {
                    completeForward = false;
                    break;
                }
                rawLogRet[d] = Math.Log(fwdClose / anchorClose);
            }
            if (!completeForward)
                continue;

            // Estimate local 30-day realized daily volatility prior to anchor to normalize across historical BTC volatility regimes
            var priorReturns = new List<double>(30);
            for (int k = 1; k <= 30; k++)
            {
                if (closeByDate.TryGetValue(anchor.AddDays(-k), out double pPrev) &&
                    closeByDate.TryGetValue(anchor.AddDays(-(k - 1)), out double pCurr) &&
                    pPrev > 0d)
                {
                    priorReturns.Add(Math.Log(pCurr / pPrev));
                }
            }

            double histVol = currentDailyVol;
            if (priorReturns.Count >= 15)
            {
                double mean = priorReturns.Average();
                double variance = priorReturns.Sum(r => (r - mean) * (r - mean)) / (priorReturns.Count - 1);
                histVol = Math.Clamp(Math.Sqrt(variance), 0.008, 0.15);
            }

            var normPath = new double[horizonDays + 1];
            for (int d = 1; d <= horizonDays; d++)
            {
                normPath[d] = Math.Clamp(rawLogRet[d] / histVol, -12.0, 12.0);
            }

            bool sameCyclePhase = (yr % 4) == (asOfDate.Year % 4);
            analogYears.Add(yr);
            if (sameCyclePhase)
                matchedCycleYears.Add(yr);

            // Give 2x weight to years sharing the exact 4-year Halving/Midterm cycle phase
            weightedPaths.Add((sameCyclePhase ? 2.0 : 1.0, normPath));
        }

        if (weightedPaths.Count < 2)
        {
            return new ConfluenceCycleAnalog
            {
                Available = false,
                CyclePhaseLabel = phaseLabel,
                AnalogYears = analogYears,
                MatchedCycleYears = matchedCycleYears,
                Message = "Kliknij „Historia BTC: 5 lat”, aby pobrać wieloletnie świece i odblokować ścieżkę Analogu Cyklu / Sezonowości."
            };
        }

        double totalWeight = weightedPaths.Sum(p => p.Weight);
        double refClose = (double)referenceClose;
        var analogPoints = new List<CycleAnalogPoint>(horizonDays + 1)
        {
            new()
            {
                Time = asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                HorizonDays = 0,
                AnalogPrice = referenceClose,
                AnalogReturnPercent = 0d
            }
        };

        for (int d = 1; d <= horizonDays; d++)
        {
            double avgNormRet = weightedPaths.Sum(p => p.Weight * p.NormalizedLogReturns[d]) / totalWeight;
            double scaledLogRet = avgNormRet * currentDailyVol;
            double price = refClose * Math.Exp(scaledLogRet);
            analogPoints.Add(new CycleAnalogPoint
            {
                Time = asOfDate.AddDays(d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                HorizonDays = d,
                AnalogPrice = Math.Round((decimal)price, 2),
                AnalogReturnPercent = Math.Round((price / refClose - 1.0) * 100.0, 2)
            });
        }

        double endRet = analogPoints[^1].AnalogReturnPercent;
        string sign = endRet >= 0 ? "+" : "";
        string cycleNote = matchedCycleYears.Count > 0
            ? $" (w tym 2x waga dla analogicznej fazy cyklu: {string.Join(", ", matchedCycleYears)})"
            : "";

        return new ConfluenceCycleAnalog
        {
            Available = true,
            CyclePhaseLabel = phaseLabel,
            AnalogYears = analogYears,
            MatchedCycleYears = matchedCycleYears,
            ExpectedReturnPercent = endRet,
            Message = $"Analog historyczny {horizonDays}D z lat {string.Join(", ", analogYears)}{cycleNote}: {sign}{endRet:0.1}% ({phaseLabel}). Próba cykliczna jest mała — traktuj wyłącznie jako kontekst kalendarzowy.",
            Points = analogPoints
        };
    }
}

