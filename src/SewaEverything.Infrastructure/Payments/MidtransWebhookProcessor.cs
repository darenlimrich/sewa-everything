using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Infrastructure.Payments;

public enum WebhookOutcome
{
    Processed,

    Duplicate,

    InvalidSignature,

    Malformed,

    Unknown
}

public sealed record WebhookResult(WebhookOutcome Outcome, string? Detail = null);

public sealed class MidtransWebhookProcessor(
    SewaDbContext db,
    PaymentLedger ledger,
    IMidtransCredentials credentials,
    TimeProvider clock,
    ILogger<MidtransWebhookProcessor> logger)
{
    private static bool IsSuccess(string transactionStatus, string? fraudStatus) =>
        transactionStatus == "settlement"
        || (transactionStatus == "capture" && fraudStatus is null or "accept");

    private static bool IsFailure(string transactionStatus) =>
        transactionStatus is "deny" or "cancel" or "failure";

    private static bool IsExpired(string transactionStatus) => transactionStatus == "expire";

    public async Task<WebhookResult> ProcessAsync(string rawBody, CancellationToken ct = default)
    {
        JsonElement payload;

        try
        {
            payload = JsonDocument.Parse(rawBody).RootElement;
        }
        catch (JsonException)
        {
            return new WebhookResult(WebhookOutcome.Malformed, "Payload bukan JSON yang sah.");
        }

        string? Text(string name) =>
            payload.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        var orderId           = Text("order_id");
        var statusCode        = Text("status_code");
        var grossAmount       = Text("gross_amount");
        var signature         = Text("signature_key");
        var transactionId     = Text("transaction_id");
        var transactionStatus = Text("transaction_status");
        var fraudStatus       = Text("fraud_status");

        if (orderId is null || statusCode is null || grossAmount is null
            || transactionId is null || transactionStatus is null)
        {
            return new WebhookResult(WebhookOutcome.Malformed,
                "Payload tidak memuat field wajib Midtrans.");
        }

        var serverKey = (await credentials.CurrentAsync(ct)).ServerKey;

        if (string.IsNullOrWhiteSpace(serverKey))
        {
            logger.LogError(
                "Notifikasi {OrderId} ditolak: kredensial Midtrans belum diisi, jadi tanda " +
                "tangannya tidak dapat dibuktikan sama sekali.", orderId);

            return new WebhookResult(WebhookOutcome.InvalidSignature);
        }

        if (!MidtransSignature.Verify(signature, orderId, statusCode, grossAmount, serverKey))
        {
            logger.LogWarning("Notifikasi dengan tanda tangan tidak sah ditolak: {OrderId}", orderId);
            return new WebhookResult(WebhookOutcome.InvalidSignature);
        }

        var eventId = $"{transactionId}:{transactionStatus}";

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.WebhookEvents.Add(new WebhookEvent
        {
            Provider    = MidtransPaymentGateway.ProviderName,
            EventId     = eventId,
            Signature   = signature,
            Payload     = rawBody,
            ProcessedAt = clock.GetUtcNow().UtcDateTime
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();

            return new WebhookResult(WebhookOutcome.Duplicate);
        }

        var charges = await db.Payments
            .Where(p => p.GatewayRef == orderId && p.Direction == PaymentDirection.In)
            .ToListAsync(ct);

        if (charges.Count == 0)
        {
            await tx.CommitAsync(ct);
            return new WebhookResult(WebhookOutcome.Unknown, $"Tidak ada tagihan untuk {orderId}.");
        }

        if (IsSuccess(transactionStatus, fraudStatus))
        {
            await SettleAsync(charges, ct);
        }
        else if (IsFailure(transactionStatus))
        {
            ledger.MarkUnsuccessful(charges, PaymentStatus.Failed, $"Gateway: {transactionStatus}");
        }
        else if (IsExpired(transactionStatus))
        {
            ledger.MarkUnsuccessful(charges, PaymentStatus.Expired, "Tagihan kedaluwarsa di gateway.");
        }
        else
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new WebhookResult(WebhookOutcome.Processed, $"Status '{transactionStatus}' dicatat.");
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new WebhookResult(WebhookOutcome.Processed, transactionStatus);
    }

    private async Task SettleAsync(List<Payment> charges, CancellationToken ct)
    {
        ledger.MarkPaid(charges);

        await db.SaveChangesAsync(ct);

        var bookingId = charges[0].BookingId;

        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null)
        {
            return;
        }

        if (booking.Status is BookingStatus.Cancelled)
        {
            var account = await ledger.DefaultPayoutAccountAsync(booking.RenterId, ct);
            await ledger.CreateFullRefundAsync(booking, account?.Id, ct);

            logger.LogWarning(
                "Pembayaran diterima untuk booking {BookingId} yang sudah batal; refund dicatat.",
                booking.Id);

            return;
        }

        booking.HoldExpiresAt = null;
    }
}
