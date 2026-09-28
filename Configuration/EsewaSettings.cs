namespace Bizkit_backend.Configuration;

public class EsewaSettings
{
    public string ProductCode { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    // Form endpoint the browser is sent to
    public string PaymentUrl { get; set; } = string.Empty;

    // Server-to-server status check endpoint
    public string StatusUrl { get; set; } = string.Empty;

    // Frontend pages eSewa redirects back to
    public string SuccessUrl { get; set; } = string.Empty;
    public string FailureUrl { get; set; } = string.Empty;
}
