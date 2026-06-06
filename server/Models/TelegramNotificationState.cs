namespace Fulvero.Api.Models;

public class TelegramNotificationState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string NotificationKey { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
}
