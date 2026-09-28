using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Bizkit_backend.DTOs.Payments;

public class EsewaInitiateRequest
{
    [Required]
    public int OrderId { get; set; }
}

public class EsewaVerifyRequest
{
    // The base64 "data" query parameter eSewa appends to the success URL
    [Required]
    public string Data { get; set; } = string.Empty;
}

// Returned to the frontend: it POSTs Fields to PaymentUrl as a form
public record EsewaPaymentResponse(
    string PaymentUrl,
    IReadOnlyDictionary<string, string> Fields);

// Response of eSewa's transaction status API (v2)
public class EsewaStatusResponse
{
    [JsonPropertyName("product_code")]
    public string ProductCode { get; set; } = string.Empty;

    [JsonPropertyName("transaction_uuid")]
    public string TransactionUuid { get; set; } = string.Empty;

    [JsonPropertyName("total_amount")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("ref_id")]
    public string? RefId { get; set; }
}
