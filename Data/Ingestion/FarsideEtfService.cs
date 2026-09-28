using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CryptoPredictor.Data.Ingestion;

public class FarsideEtfService : IEtfDataService
{
    private const string AllDataUrl = "https://farside.co.uk/bitcoin-etf-flow-all-data/";
    private const string RecentUrl = "https://farside.co.uk/btc/";
    private readonly string _cacheFilePath;
    private readonly HttpClient _httpClient;
    private readonly ILogger<FarsideEtfService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public FarsideEtfService(
        HttpClient httpClient,
        IHostEnvironment environment,
        ILogger<FarsideEtfService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var storageDir = Path.Combine(environment.ContentRootPath, "Data", "storage");
        if (!Directory.Exists(storageDir))
        {
            Directory.CreateDirectory(storageDir);
        }

        _cacheFilePath = Path.Combine(storageDir, "etf_flows.json");
    }

    public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyEtfNetFlowsAsync(CancellationToken ct = default)
    {
        var records = await LoadCacheAsync(ct);

        try
        {
            _logger.LogInformation("Attempting to fetch latest Spot BTC ETF flows from Farside Investors...");
            string html = await FetchHtmlAsync(AllDataUrl, ct);

            if (string.IsNullOrWhiteSpace(html) || !html.Contains("class=\"etf\""))
            {
                _logger.LogWarning("All-data page HTML did not contain expected table. Trying recent flows page...");
                html = await FetchHtmlAsync(RecentUrl, ct);
            }

            if (!string.IsNullOrWhiteSpace(html) && html.Contains("class=\"etf\""))
            {
                var scraped = ParseFarsideTable(html);
                if (scraped.Count > 0)
                {
                    _logger.LogInformation("Successfully parsed {Count} ETF daily flow records from Farside.", scraped.Count);

                    // Merge scraped into records
                    foreach (var kvp in scraped)
                    {
                        records[kvp.Key] = kvp.Value;
                    }

                    await SaveCacheAsync(records, ct);
                }
            }
            else
            {
                _logger.LogWarning("Could not retrieve valid HTML from Farside. Relying on cached data ({Count} records).", records.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scrape Farside ETF data. Using cached data ({Count} records).", records.Count);
        }

        return records;
    }

    private async Task<string> FetchHtmlAsync(string url, CancellationToken ct)
    {
        // 1. Try curl.exe to bypass Cloudflare TLS fingerprint challenge
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "curl.exe",
                Arguments = $"-s -A \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36\" -H \"Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8\" \"{url}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

                var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                await process.WaitForExitAsync(timeoutCts.Token);
                var stdout = await stdoutTask;

                if (process.ExitCode == 0 && stdout.Contains("class=\"etf\""))
                {
                    return stdout;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "curl.exe execution failed or not available. Falling back to HttpClient.");
        }

        // 2. Fallback to HttpClient
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
            req.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            var res = await _httpClient.SendAsync(req, ct);
            if (res.IsSuccessStatusCode)
            {
                return await res.Content.ReadAsStringAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HttpClient fallback request failed for {Url}", url);
        }

        return string.Empty;
    }

    public static Dictionary<DateOnly, decimal> ParseFarsideTable(string html)
    {
        var results = new Dictionary<DateOnly, decimal>();
        var tableMatch = Regex.Match(html, @"<table class=""etf"".*?>(.*?)</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!tableMatch.Success) return results;

        var rowMatches = Regex.Matches(tableMatch.Groups[1].Value, @"<tr.*?>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match rowMatch in rowMatches)
        {
            var cellMatches = Regex.Matches(rowMatch.Groups[1].Value, @"<t[dh].*?>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (cellMatches.Count < 10) continue;

            var rawDate = StripHtml(cellMatches[0].Groups[1].Value);
            var rawTotal = StripHtml(cellMatches[^1].Groups[1].Value);

            if (TryParseFarsideDate(rawDate, out var date))
            {
                var flowUsd = ParseFlowToUsd(rawTotal);
                results[date] = flowUsd;
            }
        }

        return results;
    }

    private static bool TryParseFarsideDate(string dateStr, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(dateStr)) return false;

        var formats = new[] { "d MMM yyyy", "dd MMM yyyy", "yyyy-MM-dd" };
        if (DateTime.TryParseExact(dateStr.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            date = DateOnly.FromDateTime(dt);
            return true;
        }

        return false;
    }

    private static decimal ParseFlowToUsd(string flowStr)
    {
        if (string.IsNullOrWhiteSpace(flowStr))
            return 0m;

        var clean = flowStr.Replace(",", "").Replace("&nbsp;", "").Trim();
        if (clean == "-" || clean == "0" || clean == "0.0")
            return 0m;

        // Financial parenthesis format: (120.2) means -120.2
        if (clean.StartsWith('(') && clean.EndsWith(')'))
        {
            var inner = clean[1..^1].Trim();
            if (decimal.TryParse(inner, NumberStyles.Any, CultureInfo.InvariantCulture, out var negativeVal))
            {
                return -negativeVal * 1_000_000m;
            }
            return 0m;
        }

        if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var positiveVal))
        {
            return positiveVal * 1_000_000m;
        }

        return 0m;
    }

    private static string StripHtml(string input)
    {
        var noHtml = Regex.Replace(input, @"<[^>]+>", " ");
        var normalized = Regex.Replace(noHtml, @"\s+", " ");
        return normalized.Trim();
    }

    private async Task<Dictionary<DateOnly, decimal>> LoadCacheAsync(CancellationToken ct)
    {
        var result = new Dictionary<DateOnly, decimal>();
        if (!File.Exists(_cacheFilePath))
            return result;

        try
        {
            using var stream = File.OpenRead(_cacheFilePath);
            var dict = await JsonSerializer.DeserializeAsync<Dictionary<string, decimal>>(stream, JsonOptions, ct);
            if (dict != null)
            {
                foreach (var (k, v) in dict)
                {
                    if (DateOnly.TryParse(k, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    {
                        result[date] = v;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load ETF cache file at {Path}.", _cacheFilePath);
        }

        return result;
    }

    private async Task SaveCacheAsync(Dictionary<DateOnly, decimal> records, CancellationToken ct)
    {
        try
        {
            var dictToSave = records
                .OrderBy(kvp => kvp.Key)
                .ToDictionary(kvp => kvp.Key.ToString("yyyy-MM-dd"), kvp => kvp.Value);

            using var stream = File.Create(_cacheFilePath);
            await JsonSerializer.SerializeAsync(stream, dictToSave, JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save ETF cache to {Path}.", _cacheFilePath);
        }
    }
}
