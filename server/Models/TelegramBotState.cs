namespace Fulvero.Api.Models;

public class TelegramBotState
{
    public string Id { get; set; } = "default";
    public long LastProcessedUpdateId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
