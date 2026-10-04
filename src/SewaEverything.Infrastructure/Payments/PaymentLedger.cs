using Microsoft.EntityFrameworkCore;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Infrastructure.Payments;

public sealed class PaymentLedger(SewaDbContext db, TimeProvider clock)
{

    public static string OrderId(Guid bookingId, int attempt) => $"SEWA-{bookingId:N}-{attempt}";

    public static string ChargeKey(PaymentKind kind, Guid bookingId, int attempt) =>
        $"{kind.ToDbValue()}:{bookingId:N}:{attempt}";

    public static string RefundKey(PaymentKind kind, Guid bookingId, Guid parentPaymentId) =>
        $"{kind.ToDbValue()}:{bookingId:N}:{parentPaymentId:N}";

    public static string CompletionKey(PaymentKind kind, Guid bookingId) =>
        $"{kind.ToDbValue()}:{bookingId:N}";

    public static bool TryParseOrderId(string? orderId, out Guid bookingId, out int attempt)
    {
        bookingId = Guid.Empty;
        attempt = 0;

        var parts = orderId?.Split('-');

        return parts is { Length: 3 }
            && parts[0] == "SEWA"
            && Guid.TryParseExact(parts[1], "N", out bookingId)
            && int.TryParse(parts[2], out attempt);
    }

    public static decimal WholeRupiah(decimal amount) => Math.Ceiling(amount);

    public Task<int> ChargeAttemptsAsync(Guid bookingId, CancellationToken ct) =>
        db.Payments.CountAsync(p => p.BookingId == bookingId && p.Kind == PaymentKind.RentCharge, ct);

    public Task<List<Payment>> PendingChargesAsync(Guid bookingId, CancellationToken ct) =>
        db.Payments
            .Where(p => p.BookingId == bookingId
                        && p.Direction == PaymentDirection.In
                        && p.Status == PaymentStatus.Pending)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task<List<Payment>> LatestChargesAsync(Guid bookingId, CancellationToken ct)
    {
        var terakhir = await db.Payments
            .Where(p => p.BookingId == bookingId && p.Direction == PaymentDirection.In)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => p.GatewayRef)
            .FirstOrDefaultAsync(ct);

        if (terakhir is null)
        {
            return [];
        }

        return await db.Payments
            .Where(p => p.BookingId == bookingId
                        && p.Direction == PaymentDirection.In
                        && p.GatewayRef == terakhir)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    public List<Payment> CreateCharges(Booking booking, PaymentChannel channel, int attempt)
    {
        var orderId = OrderId(booking.Id, attempt);

        var rentPortion = booking.PlatformFeeMode == CommissionMode.OnTop
            ? booking.TotalRent + booking.PlatformFeeAmount
            : booking.TotalRent;

        var rows = new List<Payment>
        {
            NewCharge(booking, PaymentKind.RentCharge, WholeRupiah(rentPortion), channel, orderId, attempt)
        };

        if (booking.DepositAmount > 0)
        {
            rows.Add(NewCharge(
                booking, PaymentKind.DepositCharge, WholeRupiah(booking.DepositAmount),
                channel, orderId, attempt));
        }

        if (booking.DeliveryFee > 0)
        {
            rows.Add(NewCharge(
                booking, PaymentKind.DeliveryCharge, WholeRupiah(booking.DeliveryFee),
                channel, orderId, attempt));
        }

        db.Payments.AddRange(rows);

        return rows;
    }

    private Payment NewCharge(
        Booking booking, PaymentKind kind, decimal amount,
        PaymentChannel channel, string orderId, int attempt) => new()
    {
        BookingId      = booking.Id,
        Kind           = kind,
        Direction      = kind.DirectionOf(),
        Amount         = amount,
        Currency       = "IDR",
        Status         = PaymentStatus.Pending,
        Method         = PaymentMethod.GatewayCharge,
        Channel        = channel.ToDbValue(),
        CounterpartyId = booking.RenterId,
        GatewayRef     = orderId,
        IdempotencyKey = ChargeKey(kind, booking.Id, attempt)
    };

    public void MarkPaid(IEnumerable<Payment> charges)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        foreach (var charge in charges.Where(c => c.Status == PaymentStatus.Pending))
        {
            charge.Status    = PaymentStatus.Paid;
            charge.SettledAt = now;
        }
    }

    public void MarkUnsuccessful(IEnumerable<Payment> charges, PaymentStatus status, string reason)
    {
        foreach (var charge in charges.Where(c => c.Status == PaymentStatus.Pending))
        {
            charge.Status        = status;
            charge.FailureReason = reason;
        }
    }

    public async Task<decimal> SettledInAsync(Guid bookingId, CancellationToken ct) =>
        await db.Payments
            .Where(p => p.BookingId == bookingId
                        && p.Direction == PaymentDirection.In
                        && p.Status == PaymentStatus.Paid)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

    public async Task<bool> IsSettledAsync(Booking booking, CancellationToken ct) =>
        await SettledInAsync(booking.Id, ct) >= WholeRupiah(booking.RenterTotal);

    public async Task<List<Payment>> CreateFullRefundAsync(
        Booking booking, Guid? payoutAccountId, CancellationToken ct)
    {
        var settled = await db.Payments
            .Where(p => p.BookingId == booking.Id
                        && p.Direction == PaymentDirection.In
                        && p.Status == PaymentStatus.Paid)
            .ToListAsync(ct);

        var existing = await db.Payments
            .Where(p => p.BookingId == booking.Id && p.Direction == PaymentDirection.Out)
            .Select(p => p.IdempotencyKey)
            .ToListAsync(ct);

        var refunds = new List<Payment>();

        foreach (var charge in settled)
        {
            var kind = charge.Kind switch
            {
                PaymentKind.DepositCharge  => PaymentKind.DepositRefund,
                PaymentKind.DeliveryCharge => PaymentKind.DeliveryRefund,
                _                          => PaymentKind.RentRefund
            };

            var key = RefundKey(kind, booking.Id, charge.Id);

            if (existing.Contains(key))
            {
                continue;
            }

            var channel = PaymentChannels.FromDbValue(charge.Channel!);
            var method  = channel.RefundMethod();

            refunds.Add(new Payment
            {
                BookingId       = booking.Id,
                Kind            = kind,
                Direction       = kind.DirectionOf(),
                Amount          = charge.Amount,
                Currency        = charge.Currency,
                Status          = PaymentStatus.Pending,
                Method          = method,
                Channel         = charge.Channel,
                CounterpartyId  = booking.RenterId,
                PayoutAccountId = method == PaymentMethod.Disbursement ? payoutAccountId : null,
                ParentId        = charge.Id,
                IdempotencyKey  = key
            });
        }

        db.Payments.AddRange(refunds);

        return refunds;
    }

    public Task<PayoutAccount?> DefaultPayoutAccountAsync(Guid userId, CancellationToken ct) =>
        db.PayoutAccounts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);
}
