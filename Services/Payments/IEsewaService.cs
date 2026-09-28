using Bizkit_backend.DTOs.Payments;

namespace Bizkit_backend.Services.Payments;

public interface IEsewaService
{
    Task<EsewaInitiateResult> InitiateAsync(
        int orderId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<PaymentServiceResult> VerifyAsync(
        string data,
        string userId,
        CancellationToken cancellationToken = default);
}

public sealed class EsewaInitiateResult
{
    private EsewaInitiateResult(
        bool succeeded,
        EsewaPaymentResponse? response,
        IReadOnlyCollection<string> errors)
    {
        Succeeded = succeeded;
        Response = response;
        Errors = errors;
    }

    public bool Succeeded { get; }

    public EsewaPaymentResponse? Response { get; }

    public IReadOnlyCollection<string> Errors { get; }

    public static EsewaInitiateResult Success(
        EsewaPaymentResponse response) =>
        new(true, response, []);

    public static EsewaInitiateResult Failure(
        params string[] errors) =>
        new(false, null, errors);
}
