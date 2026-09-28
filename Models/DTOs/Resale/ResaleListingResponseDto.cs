using Bizkit_backend.Models.Enums;

namespace Bizkit_backend.DTOs.Resale;

public class ResaleListingResponseDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SellerId { get; set; } = string.Empty;
    public string SellerName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public ResaleCondition Condition { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public ResaleListingStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerEmail { get; set; }
    public string? ShippingAddress { get; set; }
}