using CryptoPredictor.Alerts;
using CryptoPredictor.Data.Storage;
using CryptoPredictor.Prediction;
using Microsoft.AspNetCore.Mvc;

namespace CryptoPredictor.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlertsController : ControllerBase
{
    private readonly ITelegramAlertService _telegramAlertService;
    private readonly IMarketDataStorage _storage;
    private readonly IPredictorService _predictor;
    private readonly ILogger<AlertsController> _logger;

    public AlertsController(
        ITelegramAlertService telegramAlertService,
        IMarketDataStorage storage,
        IPredictorService predictor,
        ILogger<AlertsController> logger)
    {
        _telegramAlertService = telegramAlertService;
        _storage = storage;
        _predictor = predictor;
        _logger = logger;
    }

    /// <summary>
    /// Checks the current configuration and operational status of the Telegram alert service.
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            IsConfigured = _telegramAlertService.IsConfigured,
            Message = _telegramAlertService.IsConfigured
                ? "Telegram bot is configured and ready to dispatch alerts."
                : "Telegram bot is not configured. Provide BotToken and ChatId in appsettings.json or via environment variables."
        });
    }

    /// <summary>
    /// Sends an instant test ping message to the configured Telegram chat to verify connectivity.
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTestMessage(CancellationToken ct = default)
    {
        if (!_telegramAlertService.IsConfigured)
        {
            return BadRequest(new
            {
                Success = false,
                Message = "Telegram is not configured. Set 'Telegram:BotToken' and 'Telegram:ChatId' in appsettings.json or environment variables."
            });
        }

        string testMessage = "🚀 <b>CryptoPredictor Connection Test</b>\n\n" +
                             "✅ Your Telegram Bot integration is working perfectly!\n" +
                             "Automated daily market briefings will be delivered here every day at 00:05 UTC.";

        bool success = await _telegramAlertService.SendMessageAsync(testMessage, ct);
        if (success)
        {
            return Ok(new { Success = true, Message = "Test alert sent to Telegram successfully!" });
        }

        return StatusCode(500, new { Success = false, Message = "Failed to send message to Telegram. Check console logs for API details." });
    }

    /// <summary>
    /// Dispatches today's latest market prediction alert to Telegram immediately on demand.
    /// </summary>
    [HttpPost("send-today")]
    public async Task<IActionResult> SendTodayAlert(CancellationToken ct = default)
    {
        if (!_telegramAlertService.IsConfigured)
        {
            return BadRequest(new
            {
                Success = false,
                Message = "Telegram is not configured. Set 'Telegram:BotToken' and 'Telegram:ChatId' in appsettings.json."
            });
        }

        var prediction = await _storage.GetLatestPredictionAsync(ct);
        if (prediction == null)
        {
            return NotFound("No prediction found in storage. Run /api/prediction/today first to generate a prediction.");
        }

        bool success = await _telegramAlertService.SendPredictionAlertAsync(prediction, ct);
        if (success)
        {
            return Ok(new
            {
                Success = true,
                Message = $"Alert for {prediction.Date} ({prediction.Signal}) dispatched to Telegram successfully!"
            });
        }

        return StatusCode(500, new { Success = false, Message = "Failed to dispatch prediction alert to Telegram." });
    }
}
