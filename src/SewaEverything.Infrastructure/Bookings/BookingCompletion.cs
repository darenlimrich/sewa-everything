using Microsoft.EntityFrameworkCore;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Payments;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Infrastructure.Bookings;

public sealed class BookingCompletion(SewaDbContext db, PaymentLedger ledger, TimeProvider clock)
{
    public Task<decimal> CollectedDepositAsync(Guid bookingId, CancellationToken ct) =>
        CollectedAsync(bookingId, PaymentKind.DepositCharge, ct);

    private async Task<decimal> CollectedAsync(Guid bookingId, PaymentKind kind, CancellationToken ct) =>
        await db.Payments
            .Where(p => p.BookingId == bookingId && p.Kind == kind && p.Status == PaymentStatus.Paid)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

    public async Task<IReadOnlyList<Payment>> CreateSettlementAsync(
        Booking booking, decimal deduction, CancellationToken ct)
    {
        var collectedRent     = await CollectedAsync(booking.Id, PaymentKind.RentCharge, ct);
        var collectedDeposit  = await CollectedAsync(booking.Id, PaymentKind.DepositCharge, ct);
        var collectedDelivery = await CollectedAsync(booking.Id, PaymentKind.DeliveryCharge, ct);

        if (deduction < 0m || deduction > collectedDeposit)
        {
            throw new ArgumentOutOfRangeException(nameof(deduction), deduction,
                $"Potongan deposit harus antara 0 dan {collectedDeposit:0.##} (deposit yang tertagih).");
        }

        var fee            = PaymentLedger.WholeRupiah(booking.PlatformFeeAmount);
        var depositRefund  = collectedDeposit - deduction;
        var sellerPayout   = collectedRent - fee + deduction + collectedDelivery;
        var depositForfeit = deduction;

        var existing = await db.Payments
            .Where(p => p.BookingId == booking.Id
                        && (p.Direction == PaymentDirection.Out
                            || p.Direction == PaymentDirection.Internal))
            .Select(p => p.IdempotencyKey)
            .ToListAsync(ct);

        var now  = clock.GetUtcNow().UtcDateTime;
        var rows = new List<Payment>();

        if (fee > 0m)
        {
            Stage(rows, existing, new Payment
            {
                BookingId      = booking.Id,
                Kind           = PaymentKind.PlatformFee,
                Direction      = PaymentDirection.Internal,
                Amount         = fee,
                Status         = PaymentStatus.Paid,
                Method         = PaymentMethod.Internal,
                SettledAt      = now,
                IdempotencyKey = PaymentLedger.CompletionKey(PaymentKind.PlatformFee, booking.Id)
            });
        }

        if (depositForfeit > 0m)
        {
            Stage(rows, existing, new Payment
            {
                BookingId      = booking.Id,
                Kind           = PaymentKind.DepositForfeit,
                Direction      = PaymentDirection.Internal,
                Amount         = depositForfeit,
                Status         = PaymentStatus.Paid,
                Method         = PaymentMethod.Internal,
                SettledAt      = now,
                IdempotencyKey = PaymentLedger.CompletionKey(PaymentKind.DepositForfeit, booking.Id)
            });
        }

        if (sellerPayout > 0m)
        {
            var sellerId      = await SellerIdAsync(booking, ct);
            var sellerAccount = await ledger.DefaultPayoutAccountAsync(sellerId, ct)
                ?? throw new InvalidOperationException(
                    $"Seller booking {booking.Id} tidak punya rekening tujuan pencairan; " +
                    "seharusnya dijamin gerbang di POST /bookings/{id}/approve.");

            Stage(rows, existing, new Payment
            {
                BookingId       = booking.Id,
                Kind            = PaymentKind.SellerPayout,
                Direction       = PaymentDirection.Out,
                Amount          = sellerPayout,
                Status          = PaymentStatus.Pending,
                Method          = PaymentMethod.Disbursement,
                CounterpartyId  = sellerId,
                PayoutAccountId = sellerAccount.Id,
                IdempotencyKey  = PaymentLedger.CompletionKey(PaymentKind.SellerPayout, booking.Id)
            });
        }

        if (depositRefund > 0m)
        {
            var depositCharge = await db.Payments
                .Where(p => p.BookingId == booking.Id
                            && p.Kind == PaymentKind.DepositCharge
                            && p.Status == PaymentStatus.Paid)
                .OrderBy(p => p.CreatedAt)
                .FirstAsync(ct);

            var channel = PaymentChannels.FromDbValue(depositCharge.Channel!);
            var method  = channel.RefundMethod();

            Guid? renterAccountId = null;
            if (method == PaymentMethod.Disbursement)
            {
                renterAccountId =
                    (await ledger.DefaultPayoutAccountAsync(booking.RenterId, ct)
                     ?? throw new InvalidOperationException(
                         $"Renter booking {booking.Id} tidak punya rekening tujuan refund; " +
                         "seharusnya dijamin gerbang di POST /bookings/{id}/pay."))
                    .Id;
            }

            Stage(rows, existing, new Payment
            {
                BookingId       = booking.Id,
                Kind            = PaymentKind.DepositRefund,
                Direction       = PaymentDirection.Out,
                Amount          = depositRefund,
                Status          = PaymentStatus.Pending,
                Method          = method,
                Channel         = depositCharge.Channel,
                CounterpartyId  = booking.RenterId,
                PayoutAccountId = renterAccountId,
                ParentId        = depositCharge.Id,
                IdempotencyKey  = PaymentLedger.RefundKey(
                    PaymentKind.DepositRefund, booking.Id, depositCharge.Id)
            });
        }

        db.Payments.AddRange(rows);

        return rows;
    }

    private static void Stage(List<Payment> rows, List<string> existing, Payment row)
    {
        if (!existing.Contains(row.IdempotencyKey))
        {
            rows.Add(row);
        }
    }

    private Task<Guid> SellerIdAsync(Booking booking, CancellationToken ct) =>
        booking.Item is not null
            ? Task.FromResult(booking.Item.SellerId)
            : db.Items.Where(i => i.Id == booking.ItemId).Select(i => i.SellerId).FirstAsync(ct);
}
