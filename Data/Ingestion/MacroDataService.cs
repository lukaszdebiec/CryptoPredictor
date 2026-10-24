using System.Globalization;
using System.Text.Json;

namespace CryptoPredictor.Data.Ingestion;

public class MacroDataService : IMacroDataService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MacroDataService> _logger;

    public MacroDataService(HttpClient httpClient, ILogger<MacroDataService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailySeriesAsync(string symbol, string range = "1y", CancellationToken ct = default)
    {
        var result = new Dictionary<DateOnly, decimal>();

        try
        {
            var encodedSymbol = Uri.EscapeDataString(symbol);
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{encodedSymbol}?range={range}&interval=1d";

            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch macro data for {Symbol}: HTTP {StatusCode}", symbol, response.StatusCode);
                return result;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var jsonDoc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!jsonDoc.RootElement.TryGetProperty("chart", out var chartObj) ||
                !chartObj.TryGetProperty("result", out var resultArray) ||
                resultArray.GetArrayLength() == 0)
            {
                _logger.LogWarning("Unexpected Yahoo Finance JSON format for symbol {Symbol}", symbol);
                return result;
            }

            var firstResult = resultArray[0];
            if (!firstResult.TryGetProperty("timestamp", out var timestampArray) ||
                !firstResult.TryGetProperty("indicators", out var indicatorsObj) ||
                !indicatorsObj.TryGetProperty("quote", out var quoteArray) ||
                quoteArray.GetArrayLength() == 0)
            {
                _logger.LogWarning("No timestamp or quote elements found for symbol {Symbol}", symbol);
                return result;
            }

            var quote = quoteArray[0];
            if (!quote.TryGetProperty("close", out var closeArray))
            {
                _logger.LogWarning("No close array found in quotes for symbol {Symbol}", symbol);
                return result;
            }

            var timestamps = timestampArray.EnumerateArray().ToList();
            var closes = closeArray.EnumerateArray().ToList();
            var count = Math.Min(timestamps.Count, closes.Count);

            for (int i = 0; i < count; i++)
            {
                var tsElem = timestamps[i];
                var closeElem = closes[i];

                if (tsElem.ValueKind == JsonValueKind.Number &&
                    tsElem.TryGetInt64(out var tsSec) &&
                    closeElem.ValueKind == JsonValueKind.Number &&
                    closeElem.TryGetDecimal(out var closeVal))
                {
                    // Convert UTC timestamp to trading date
                    var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(tsSec).UtcDateTime.Date);
                    result[date] = Math.Round(closeVal, 4);
                }
            }

            _logger.LogInformation("Fetched {Count} macro daily records for {Symbol} (Range: {Range}).", result.Count, symbol, range);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while fetching macro series for {Symbol}", symbol);
        }

        return result;
    }

    public async Task<MacroHistoryResult> GetMacroHistoryAsync(string range = "1y", CancellationToken ct = default)
    {
        _logger.LogInformation("Initiating concurrent macro data fetch for DXY, US10Y, S&P 500, and NASDAQ...");

        var dxyTask = GetDailySeriesAsync("DX-Y.NYB", range, ct);
        var us10yTask = GetDailySeriesAsync("^TNX", range, ct);
        var sp500Task = GetDailySeriesAsync("^GSPC", range, ct);
        var nasdaqTask = GetDailySeriesAsync("^IXIC", range, ct);

        await Task.WhenAll(dxyTask, us10yTask, sp500Task, nasdaqTask);

        return new MacroHistoryResult(
            await dxyTask,
            await us10yTask,
            await sp500Task,
            await nasdaqTask
        );
    }
}
