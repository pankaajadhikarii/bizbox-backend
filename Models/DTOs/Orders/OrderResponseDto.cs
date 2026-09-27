using Bizkit_backend.Models.Enums;
using Bizkit_backend.DTOs.Payments;

namespace Bizkit_backend.DTOs.Orders;

public class OrderResponseDto
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string ShippingAddress { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public OrderPaymentStatus PaymentStatus { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<OrderItemResponseDto> Items { get; set; } = [];
    public ICollection<PaymentResponseDto> Payments { get; set; } = [];
}