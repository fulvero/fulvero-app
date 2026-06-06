namespace Fulvero.Api.Telegram;

public class TelegramOptions
{
    public bool Enabled { get; set; }
    public string BotToken { get; set; } = string.Empty;
    public string BotUsername { get; set; } = string.Empty;
    public int PollingIntervalSeconds { get; set; } = 5;
    public int NotificationIntervalMinutes { get; set; } = 720;
}
