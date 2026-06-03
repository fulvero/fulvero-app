namespace LShopOzonWebReact.Api.Models;

public class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string LoginName { get; set; } = string.Empty;
    public string OzonClientIdProtected { get; set; } = string.Empty;
    public string OzonApiKeyProtected { get; set; } = string.Empty;
    public string SubscriptionStatus { get; set; } = CompanySubscriptionStatuses.Trial;
    public DateTimeOffset TrialEndsAt { get; set; } = DateTimeOffset.UtcNow.AddDays(3);
    public DateTimeOffset? SubscriptionPaidUntil { get; set; }
    public string YooKassaPaymentMethodIdProtected { get; set; } = string.Empty;
    public string LastYooKassaPaymentId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class CompanySubscriptionStatuses
{
    public const string Trial = "Trial";
    public const string Active = "Active";
    public const string PastDue = "PastDue";
    public const string Blocked = "Blocked";
}
