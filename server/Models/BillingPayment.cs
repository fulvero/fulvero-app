namespace Fulvero.Api.Models;

public class BillingPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public string Provider { get; set; } = "YooKassa";
    public string PaymentId { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "RUB";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }
}
