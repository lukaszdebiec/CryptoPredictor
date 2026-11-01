using CryptoPredictor.Data.Ingestion;
using CryptoPredictor.Data.Storage;
using CryptoPredictor.Data;
using CryptoPredictor.Prediction.Trajectory;
using Microsoft.AspNetCore.Mvc;

namespace CryptoPredictor.Controllers;

[ApiController]
[Route("api/prediction/trajectory")]
public sealed class TrajectoryController(ITrajectoryProvider provider, IPriceHistoryService downloader, IPriceHistoryCache cache) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TrajectoryForecast>> Latest(CancellationToken ct) => Ok(await provider.GetAsync(ct: ct));

    [HttpGet("historical")]
    public async Task<ActionResult<TrajectoryForecast>> Historical([FromQuery] DateOnly date, CancellationToken ct)
    {
        if (date >= DateOnly.FromDateTime(DateTime.UtcNow)) return BadRequest("Choose a completed UTC day.");
        return Ok(await provider.GetAsync(date, ct));
    }

    [HttpGet("chart")]
    public async Task<ActionResult<MarketChartResponse>> Chart([FromQuery] int days = 180, CancellationToken ct = default)
    {
        if (days < 0 || days > 3650) return BadRequest("days must be between 0 and 3650.");
        var history = await cache.LoadAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var all = history?.Candles.Where(c => c.Date < today).OrderBy(c => c.Date).ToList() ?? [];
        if (all.Count == 0) return StatusCode(503, "No dedicated price history. Use POST /api/prediction/trajectory/sync-history first.");
        var selected = days > 0 ? all.TakeLast(days).ToList() : all;
        var last = all[^1]; var prior = all.Count > 1 ? all[^2] : last;
        return Ok(new MarketChartResponse
        {
            Candles = selected.Select(c => new CandleDataPoint { Time = c.Date.ToString("yyyy-MM-dd"), Open = c.BtcOpen, High = c.BtcHigh, Low = c.BtcLow, Close = c.BtcClose, Volume = c.BtcVolume }).ToList(),
            Markers = [], Indicators = [],
            Summary = new ChartSummary { LatestDate = last.Date.ToString("yyyy-MM-dd"), LatestClose = last.BtcClose,
                PriceChange24hPercent = (last.BtcClose / prior.BtcClose - 1m) * 100m, TotalDays = selected.Count,
                HighPeriod = selected.Max(c => c.BtcHigh), LowPeriod = selected.Min(c => c.BtcLow) },
            Trajectory = await provider.GetAsync(ct: ct)
        });
    }

    [HttpGet("history-status")]
    public async Task<IActionResult> HistoryStatus(CancellationToken ct)
    {
        var history = await cache.LoadAsync(ct);
        return Ok(new { Source = cache.Source.ToString(), Symbol = "BTCUSDT", Interval = "1d", Available = history != null,
            Count = history?.Candles.Count ?? 0, history?.IsComplete, history?.RequestedFrom, history?.RequestedToExclusive,
            MissingDates = history?.MissingDates, FirstDate = history?.Candles.FirstOrDefault()?.Date, LastDate = history?.Candles.LastOrDefault()?.Date });
    }

    [HttpPost("sync-history")]
    public async Task<IActionResult> SyncHistory([FromQuery] int days = 1825, CancellationToken ct = default)
    {
        if (days < 1 || days > 3650) return BadRequest("days must be between 1 and 3650.");
        var end = DateOnly.FromDateTime(DateTime.UtcNow);
        try
        {
            var downloaded = await downloader.DownloadAsync(cache.Source, end.AddDays(-days), end, ct);
            await cache.SaveAsync(downloaded, ct);
            var saved = (await cache.LoadAsync(ct))!;
            return Ok(new { saved.Source, saved.Symbol, saved.RequestedFrom, saved.RequestedToExclusive,
                Count = saved.Candles.Count, saved.IsComplete, saved.MissingDates,
                Message = "Dedicated price history saved. Existing directional forecasts and enriched market data were preserved." });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { Error = "PRICE_HISTORY_DOWNLOAD_FAILED", UpstreamStatus = ex.StatusCode, Message = "The selected public price source could not be reached. Existing cached history was preserved." });
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(502, new { Error = "PRICE_HISTORY_TIMEOUT", Message = "The price source timed out. Existing cached history was preserved." });
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or FormatException)
        { return StatusCode(502, new { Error = "PRICE_HISTORY_INVALID", ex.Message }); }
    }
}
