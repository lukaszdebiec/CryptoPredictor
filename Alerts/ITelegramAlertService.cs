using CryptoPredictor.Prediction;

namespace CryptoPredictor.Alerts;

public interface ITelegramAlertService
{
    /// <summary>
    /// Indicates whether Telegram BotToken and ChatId are properly configured.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends a formatted HTML market prediction alert to the configured Telegram chat.
    /// </summary>
    Task<bool> SendPredictionAlertAsync(MarketPrediction prediction, CancellationToken ct = default);

    /// <summary>
    /// Sends a custom plain text / HTML message to the configured Telegram chat.
    /// </summary>
    Task<bool> SendMessageAsync(string message, CancellationToken ct = default);
}
