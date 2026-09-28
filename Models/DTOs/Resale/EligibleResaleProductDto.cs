namespace Bizkit_backend.DTOs.Resale;

public class EligibleResaleProductDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductImageUrl { get; set; }
    public decimal OriginalPrice { get; set; }
    public bool AlreadyListed { get; set; }
}
