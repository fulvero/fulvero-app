namespace LShopOzonWebReact.Api.Models;

public class ProductSupplierLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public long OzonProductId { get; set; }
    public string OfferId { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string SupplierUrl { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
