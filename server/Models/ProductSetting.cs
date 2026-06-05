namespace Fulvero.Api.Models;

public class ProductSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long OzonProductId { get; set; }
    public string OfferId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductType { get; set; } = ProductTypes.Unset;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class ProductTypes
{
    public const string Unset = "";
    public const string Production = "Production";
    public const string Purchase = "Purchase";
}
