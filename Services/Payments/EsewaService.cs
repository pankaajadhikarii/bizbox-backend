using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bizkit_backend.Configuration;
using Bizkit_backend.Data;
using Bizkit_backend.DTOs.Payments;
using Bizkit_backend.Models.Entities;
using Bizkit_backend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bizkit_backend.Services.Payments;

public sealed class EsewaService(
    ApplicationDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IOptions<EsewaSettings> options,
    ILogger<EsewaService> logger) : IEsewaService
{
    private const string SignedFields =
        "total_amount,transaction_uuid,product_code";

    private readonly EsewaSettings settings = options.Value;

    public async Task<EsewaInitiateResult> InitiateAsync(
        int orderId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var order = await dbContext.Orders
            .Include(item => item.Payments)
            .FirstOrDefaultAsync(
                item => item.Id == orderId
                    && item.CustomerId == userId,
                cancellationToken);

        if (order is null)
        {
            return EsewaInitiateResult.Failure("Order not found.");
        }

        if (order.PaymentMethod != PaymentMethod.Esewa)
        {
            return EsewaInitiateResult.Failure(
                "This order was not placed with eSewa payment.");
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return EsewaInitiateResult.Failure(
                "Cannot pay for a cancelled order.");
        }

        if (order.PaymentStatus == OrderPaymentStatus.Paid)
        {
            return EsewaInitiateResult.Failure(
                "This order has already been paid.");
        }

        var now = DateTime.UtcNow;

        // Reuse the pending payment row on retry, but always issue a
        // fresh transaction uuid (eSewa rejects a reused one).
        var payment = order.Payments.FirstOrDefault(item =>
            item.PaymentMethod == PaymentMethod.Esewa
            && item.Status == PaymentStatus.Pending);

        if (payment is null)
        {
            payment = new Payment
            {
                OrderId = order.Id,
                PaymentMethod = PaymentMethod.Esewa,
                Status = PaymentStatus.Pending
            };

            dbContext.Payments.Add(payment);
        }

        payment.Amount = order.TotalAmount;
        payment.TransactionReference = Guid.NewGuid().ToString();
        payment.UpdatedAt = now;

        order.PaymentStatus = OrderPaymentStatus.Pending;
        order.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        var total = FormatAmount(payment.Amount);
        var uuid = payment.TransactionReference;

        var message =
            $"total_amount={total}," +
            $"transaction_uuid={uuid}," +
            $"product_code={settings.ProductCode}";

        var fields = new Dictionary<string, string>
        {
            ["amount"] = total,
            ["tax_amount"] = "0",
            ["total_amount"] = total,
            ["transaction_uuid"] = uuid,
            ["product_code"] = settings.ProductCode,
            ["product_service_charge"] = "0",
            ["product_delivery_charge"] = "0",
            ["success_url"] = settings.SuccessUrl,
            ["failure_url"] = settings.FailureUrl,
            ["signed_field_names"] = SignedFields,
            ["signature"] = Sign(message)
        };

        return EsewaInitiateResult.Success(
            new EsewaPaymentResponse(settings.PaymentUrl, fields));
    }

    public async Task<PaymentServiceResult> VerifyAsync(
        string data,
        string userId,
        CancellationToken cancellationToken = default)
    {
        // Query strings turn "+" into spaces; base64 needs "+" back.
        var cleaned = data.Trim().Replace(' ', '+');

        Dictionary<string, string> callback;

        try
        {
            var json = Encoding.UTF8.GetString(
                Convert.FromBase64String(cleaned));

            using var document = JsonDocument.Parse(json);

            callback = document.RootElement
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => property.Value.ValueKind
                        == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.ToString());
        }
        catch (Exception ex) when (
            ex is FormatException or JsonException)
        {
            return PaymentServiceResult.Failure(
                "Invalid payment data.");
        }

        if (!callback.TryGetValue("signed_field_names", out var names)
            || !callback.TryGetValue("signature", out var signature))
        {
            return PaymentServiceResult.Failure(
                "Invalid payment data.");
        }

        var message = string.Join(",", names.Split(',')
            .Select(name =>
                $"{name}={(callback.TryGetValue(name, out var value) ? value : string.Empty)}"));

        var expected = Sign(message);

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signature)))
        {
            logger.LogWarning(
                "eSewa callback signature mismatch.");

            return PaymentServiceResult.Failure(
                "Payment signature could not be verified.");
        }

        if (!callback.TryGetValue("transaction_uuid", out var uuid))
        {
            return PaymentServiceResult.Failure(
                "Invalid payment data.");
        }

        var payment = await dbContext.Payments
            .Include(item => item.Order)
            .FirstOrDefaultAsync(
                item => item.TransactionReference == uuid
                    && item.PaymentMethod == PaymentMethod.Esewa
                    && item.Order.CustomerId == userId,
                cancellationToken);

        if (payment is null)
        {
            return PaymentServiceResult.Failure(
                "Payment not found.");
        }

        if (payment.Status == PaymentStatus.Success)
        {
            return PaymentServiceResult.Success(ToResponse(payment));
        }

        var status = await GetStatusAsync(
            payment,
            cancellationToken);

        if (status is null)
        {
            return PaymentServiceResult.Failure(
                "Could not confirm the payment with eSewa. Please try again.");
        }

        var now = DateTime.UtcNow;

        if (status.Status == "COMPLETE"
            && status.TotalAmount == payment.Amount
            && status.ProductCode == settings.ProductCode)
        {
            payment.Status = PaymentStatus.Success;
            payment.PaidAt = now;
            payment.UpdatedAt = now;

            payment.Order.PaymentStatus = OrderPaymentStatus.Paid;
            payment.Order.UpdatedAt = now;

            await dbContext.SaveChangesAsync(cancellationToken);

            return PaymentServiceResult.Success(ToResponse(payment));
        }

        if (status.Status is "CANCELED" or "NOT_FOUND")
        {
            payment.Status = PaymentStatus.Failed;
            payment.UpdatedAt = now;

            payment.Order.PaymentStatus = OrderPaymentStatus.Failed;
            payment.Order.UpdatedAt = now;

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return PaymentServiceResult.Failure(
            $"Payment was not completed (status: {status.Status}).");
    }

    private async Task<EsewaStatusResponse?> GetStatusAsync(
        Payment payment,
        CancellationToken cancellationToken)
    {
        var url =
            $"{settings.StatusUrl}" +
            $"?product_code={Uri.EscapeDataString(settings.ProductCode)}" +
            $"&total_amount={FormatAmount(payment.Amount)}" +
            $"&transaction_uuid={Uri.EscapeDataString(payment.TransactionReference ?? string.Empty)}";

        try
        {
            var client = httpClientFactory.CreateClient();

            var response = await client.GetAsync(
                url,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "eSewa status check returned {StatusCode}.",
                    response.StatusCode);

                return null;
            }

            return await response.Content
                .ReadFromJsonAsync<EsewaStatusResponse>(
                    cancellationToken);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException
                or TaskCanceledException)
        {
            logger.LogError(
                ex,
                "eSewa status check failed.");

            return null;
        }
    }

    private string Sign(string message)
    {
        using var hmac = new HMACSHA256(
            Encoding.UTF8.GetBytes(settings.SecretKey));

        return Convert.ToBase64String(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
    }

    private static string FormatAmount(decimal amount) =>
        decimal.Round(amount, 2)
            .ToString("0.##", CultureInfo.InvariantCulture);

    private static PaymentResponseDto ToResponse(Payment payment) =>
        new()
        {
            Id = payment.Id,
            OrderId = payment.OrderId,
            PaymentMethod = payment.PaymentMethod,
            Amount = payment.Amount,
            TransactionReference = payment.TransactionReference,
            Status = payment.Status,
            PaidAt = payment.PaidAt,
            CreatedAt = payment.CreatedAt,
            UpdatedAt = payment.UpdatedAt
        };
}
