namespace Fulvero.Api.Models;

public class TelegramIntegration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string LinkCode { get; set; } = string.Empty;
    public long? ChatId { get; set; }
    public string ChatTitle { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LinkedAt { get; set; }
}
