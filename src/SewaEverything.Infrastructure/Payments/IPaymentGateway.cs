using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Payments;

public readonly record struct ChargeRequest(
    string OrderId,
    long GrossAmount,
    PaymentChannel Channel,
    string CustomerName,
    string CustomerEmail,
    string ItemTitle);

public readonly record struct ChargeResult(
    string GatewayTransactionId,
    string? VirtualAccountNumber,
    string? PaymentCode,
    string? QrString,
    string? RedirectUrl,
    DateTime? ExpiresAt,
    string? QrImageUrl = null);

public enum GatewayKeyVerdict
{
    BelongsToEnvironment,
    WrongEnvironment,
    Unverifiable
}

public interface IPaymentGateway
{
    string Provider { get; }

    Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct = default);

    Task<GatewayKeyVerdict> VerifyServerKeyAsync(
        string serverKey, bool isProduction, CancellationToken ct = default);
}
