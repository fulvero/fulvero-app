namespace LShopOzonWebReact.Api.Billing;

public class YooKassaOptions
{
    public string ShopId { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public decimal MonthlyAmount { get; set; } = 4900;
    public string Currency { get; set; } = "RUB";
}
