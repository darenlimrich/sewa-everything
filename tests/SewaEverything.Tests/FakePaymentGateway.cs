using SewaEverything.Domain;
using SewaEverything.Infrastructure.Payments;

namespace SewaEverything.Tests;

public sealed class FakePaymentGateway : IPaymentGateway
{
    public string Provider => MidtransPaymentGateway.ProviderName;

    public List<ChargeRequest> Charges { get; } = [];

    public string? FailWith { get; set; }

    public TimeSpan ChargeDelay { get; set; }

    public List<(string ServerKey, bool IsProduction)> KeyChecks { get; } = [];

    public GatewayKeyVerdict? VerdictOverride { get; set; }

    public Task<GatewayKeyVerdict> VerifyServerKeyAsync(
        string serverKey, bool isProduction, CancellationToken ct = default)
    {
        lock (KeyChecks)
        {
            KeyChecks.Add((serverKey, isProduction));
        }

        if (VerdictOverride is { } forced)
        {
            return Task.FromResult(forced);
        }

        var looksSandbox = serverKey.StartsWith("SB-", StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(looksSandbox == isProduction
            ? GatewayKeyVerdict.WrongEnvironment
            : GatewayKeyVerdict.BelongsToEnvironment);
    }

    public async Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct = default)
    {
        lock (Charges)
        {
            Charges.Add(request);
        }

        if (FailWith is { } message)
        {
            throw new PaymentGatewayException(message);
        }

        if (ChargeDelay > TimeSpan.Zero)
        {
            await Task.Delay(ChargeDelay, ct);
        }

        var isVa = request.Channel is PaymentChannel.VaBca or PaymentChannel.VaBni
                                   or PaymentChannel.VaBri or PaymentChannel.VaPermata;

        var isCstore = request.Channel is PaymentChannel.CstoreAlfamart
                                       or PaymentChannel.CstoreIndomaret;

        return new ChargeResult(
            GatewayTransactionId: $"TRX-{request.OrderId}",
            VirtualAccountNumber: isVa ? $"8808{Math.Abs(request.OrderId.GetHashCode()):D10}" : null,
            PaymentCode:          isCstore ? $"{Math.Abs(request.OrderId.GetHashCode()):D12}" : null,
            QrString:             request.Channel == PaymentChannel.Qris ? $"QR|{request.OrderId}" : null,
            RedirectUrl:          request.Channel is PaymentChannel.Gopay or PaymentChannel.CreditCard
                                      ? $"https://sandbox.example/pay/{request.OrderId}"
                                      : null,
            ExpiresAt:            DateTime.UtcNow.AddHours(24),
            QrImageUrl:           request.Channel == PaymentChannel.Qris
                                      ? $"https://sandbox.example/qr/{request.OrderId}.png"
                                      : null);
    }
}
