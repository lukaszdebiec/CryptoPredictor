using System.Text;
using System.Text.Json;
using CryptoPredictor.Prediction;

namespace CryptoPredictor.Alerts;

public class TelegramAlertService : ITelegramAlertService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelegramAlertService> _logger;

    public TelegramAlertService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<TelegramAlertService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsConfigured
    {
        get
        {
            bool enabled = _configuration.GetValue("Telegram:Enabled", true);
            if (!enabled)
                return false;

            var token = GetBotToken();
            var chatId = GetChatId();

            return !string.IsNullOrWhiteSpace(token) &&
                   !token.Contains("YOUR_BOT_TOKEN", StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(chatId) &&
                   !chatId.Contains("YOUR_CHAT_ID", StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task<bool> SendPredictionAlertAsync(MarketPrediction prediction, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogInformation("[TelegramAlertService] Telegram is not configured. Skipping alert delivery.");
            return false;
        }

        string message = FormatPredictionHtml(prediction);
        return await SendMessageAsync(message, ct);
    }

    public async Task<bool> SendMessageAsync(string message, CancellationToken ct = default)
    {
        var token = GetBotToken();
        var chatId = GetChatId();

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId))
        {
            _logger.LogWarning("[TelegramAlertService] Cannot send message. Missing BotToken or ChatId.");
            return false;
        }

        try
        {
            string url = $"https://api.telegram.org/bot{token}/sendMessage";

            var payload = new
            {
                chat_id = chatId,
                text = message,
                parse_mode = "HTML",
                disable_web_page_preview = true
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[TelegramAlertService] Telegram message delivered successfully to chat {ChatId}.", chatId);
                return true;
            }

            var errBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("[TelegramAlertService] Telegram API error ({StatusCode}): {Body}", response.StatusCode, errBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TelegramAlertService] Exception occurred while sending Telegram alert.");
            return false;
        }
    }

    private string FormatPredictionHtml(MarketPrediction prediction)
    {
        string signalEmoji = prediction.Signal switch
        {
            "BULLISH_OPPORTUNITY" => "🟢 <b>BULLISH OPPORTUNITY DETECTED</b>",
            "BEARISH_RISK" => "🔴 <b>BEARISH RISK WARNING</b>",
            _ => "⚪ <b>NEUTRAL CONSOLIDATION</b>"
        };

        var sb = new StringBuilder();
        sb.AppendLine(signalEmoji);
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"📅 <b>Date:</b> {prediction.Date:yyyy-MM-dd}");
        sb.AppendLine($"💵 <b>BTC Close:</b> ${prediction.BtcClose:N2}");
        sb.AppendLine();
        sb.AppendLine($"🎯 <b>Signal:</b> <code>{prediction.Signal}</code>");
        sb.AppendLine($"📊 <b>Bullish Probability:</b> <b>{prediction.BullishProbability:P1}</b>");
        sb.AppendLine($"⚖️ <b>Regime:</b> {prediction.MarketRegime}");
        sb.AppendLine($"🛡️ <b>Confidence:</b> {prediction.Confidence}");
        sb.AppendLine();

        if (prediction.TargetPrice > 0m)
        {
            sb.AppendLine($"🎯 <b>Target Price (TP):</b> ${prediction.TargetPrice:N2} ({prediction.TargetChangePercent:+0.0%;-0.0%;0.0%})");
            sb.AppendLine($"🛑 <b>Invalidation (SL):</b> ${prediction.InvalidationPrice:N2} ({prediction.InvalidationChangePercent:+0.0%;-0.0%;0.0%})");
            sb.AppendLine($"📐 <b>Risk/Reward:</b> 1:{prediction.RiskRewardRatio:F1} (Horizon: {prediction.HorizonDays}d)");
            sb.AppendLine();
        }

        if (prediction.Ensemble != null)
        {
            sb.AppendLine("🤖 <b>Multi-Model Ensemble Consensus:</b>");
            sb.AppendLine($"• <b>Verdict:</b> {prediction.Ensemble.ConsensusVerdict} ({prediction.Ensemble.ConsensusAgreementPercent:F0}% Agreement)");
            foreach (var vote in prediction.Ensemble.Votes)
            {
                string icon = vote.Signal == "BULLISH" ? "🟢" : vote.Signal == "BEARISH" ? "🔴" : "⚪";
                sb.AppendLine($"  {icon} <code>{vote.ModelName}</code>: <b>{vote.Probability:P1}</b> (Weight: {vote.Weight:P0})");
            }
            sb.AppendLine();
        }

        if (prediction.KeyDrivers != null && prediction.KeyDrivers.Count > 0)
        {
            sb.AppendLine("🔍 <b>Key Market Drivers:</b>");
            foreach (var driver in prediction.KeyDrivers)
            {
                sb.AppendLine($"• {EscapeHtml(driver)}");
            }
        }

        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("🤖 <i>CryptoPredictor Autonomous Quant Engine</i>");

        return sb.ToString();
    }

    private string GetBotToken()
    {
        return Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
               ?? _configuration["Telegram:BotToken"]
               ?? string.Empty;
    }

    private string GetChatId()
    {
        return Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID")
               ?? _configuration["Telegram:ChatId"]
               ?? string.Empty;
    }

    private static string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
