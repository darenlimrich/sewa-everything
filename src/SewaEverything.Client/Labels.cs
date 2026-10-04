namespace SewaEverything.Client;

public static class Labels
{
    public static string Role(string role) => role switch
    {
        "renter" => "Penyewa",
        "seller" => "Pemilik barang",
        "admin"  => "Admin",
        "owner"  => "Superadmin",
        _        => role
    };

    public static string BookingStatus(string status) => status switch
    {
        "pending"   => "Menunggu",
        "confirmed" => "Dikonfirmasi",
        "active"    => "Berlangsung",
        "completed" => "Selesai",
        "cancelled" => "Dibatalkan",
        "disputed"  => "Sengketa",
        _           => status
    };

    public static string BookingStatusHint(string status) => status switch
    {
        "pending"   => "Menunggu persetujuan pemilik.",
        "confirmed" => "Disetujui. Selesaikan pembayaran.",
        "active"    => "Barang sedang disewa.",
        "completed" => "Sewa selesai. Rincian pengembalian deposit ada di bawah.",
        "cancelled" => "Sewa dibatalkan. Pembayaran yang sudah masuk dikembalikan penuh.",
        "disputed"  => "Sedang ditinjau admin.",
        _           => string.Empty
    };

    public static string SellerBookingHint(string status) => status switch
    {
        "pending"   => "Menunggu persetujuan Anda.",
        "confirmed" => "Disetujui. Menunggu pembayaran penyewa.",
        "active"    => "Barang sedang disewa. Konfirmasi setelah barang kembali.",
        "completed" => "Sewa selesai. Dana masuk antrean pencairan.",
        "cancelled" => "Sewa dibatalkan. Pembayaran penyewa yang sudah masuk dikembalikan penuh.",
        "disputed"  => "Sengketa sedang ditinjau admin.",
        _           => string.Empty
    };

    public static bool ShowsAmount(string status) => status != "cancelled";

    public static string AmountParts(decimal rent, decimal deposit, decimal fee, string feeMode)
    {
        var parts = new List<string> { $"sewa {Format.Rupiah(rent)}" };

        if (deposit > 0)
        {
            parts.Add($"deposit {Format.Rupiah(deposit)}");
        }

        if (feeMode == "on_top" && fee > 0)
        {
            parts.Add($"biaya layanan {Format.Rupiah(fee)}");
        }

        return parts.Count > 1 ? string.Join(" + ", parts) : string.Empty;
    }

    public static bool AwaitingPayment(string status, DateTime? holdExpiresAt) =>
        status == "confirmed" && holdExpiresAt is not null;

    public static bool AwaitingHandover(string status, DateTime? holdExpiresAt) =>
        status == "confirmed" && holdExpiresAt is null;

    public static string BookingStatusHint(string status, DateTime? holdExpiresAt) =>
        AwaitingHandover(status, holdExpiresAt)
            ? "Sudah dibayar. Menunggu pemilik menyerahkan barang."
            : BookingStatusHint(status);

    public static string SellerBookingHint(string status, DateTime? holdExpiresAt) =>
        AwaitingHandover(status, holdExpiresAt)
            ? "Sudah dibayar penyewa. Serahkan barangnya."
            : SellerBookingHint(status);

    public static string ListingReviewHint(string status) => status switch
    {
        "approved" => "Listing disetujui admin dan sudah tampil di katalog.",
        "rejected" => "Listing ditolak admin. Buka barangnya untuk membaca alasannya.",
        _          => string.Empty
    };

    public static string NotificationText(string kind, string status, DateTime? holdExpiresAt) =>
        kind == "listing" ? ListingReviewHint(status)
        : kind == "seller"
            ? SellerBookingHint(status, holdExpiresAt)
            : BookingStatusHint(status, holdExpiresAt);

    public static bool NeedsAction(string kind, string status, DateTime? holdExpiresAt) =>
        kind == "listing" ? status == "rejected"
        : kind == "seller"
            ? status is "pending" or "active" || AwaitingHandover(status, holdExpiresAt)
            : AwaitingPayment(status, holdExpiresAt);

    public static string ActionLabel(string kind, string status, DateTime? holdExpiresAt) =>
        kind == "listing" ? (status == "rejected" ? "Perbaiki listing" : string.Empty)
        : kind == "seller"
            ? status switch
            {
                "pending"   => "Setujui atau tolak",
                "active"    => "Terima barang kembali",
                "confirmed" => "Serahkan barang",
                _           => string.Empty
            }
            : AwaitingPayment(status, holdExpiresAt) ? "Bayar sekarang" : string.Empty;

    public static string ItemStatus(string status) => status switch
    {
        "active"         => "Aktif",
        "inactive"       => "Nonaktif",
        "pending_review" => "Menunggu verifikasi",
        "rejected"       => "Ditolak",
        "suspended"      => "Diturunkan admin",
        _                => status
    };

    public static string ItemListingState(string? status, DateTime? suspendedAt, string? reviewStatus) =>
        suspendedAt is not null ? "suspended"
        : reviewStatus == "rejected" ? "rejected"
        : reviewStatus == "pending" ? "pending_review"
        : status ?? "active";

    public static string ItemStateTone(string state) => state switch
    {
        "active"         => "on",
        "pending_review" => "wait",
        "rejected"       => "danger",
        _                => "off"
    };

    public static string ItemReviewHint(string? reviewStatus, string? rejectionReason) => reviewStatus switch
    {
        "pending"  => "Menunggu verifikasi admin. Barang belum tampil di katalog.",
        "rejected" => string.IsNullOrWhiteSpace(rejectionReason)
            ? "Ditolak admin. Perbaiki lalu simpan — barang otomatis diajukan ulang."
            : $"Ditolak admin: {rejectionReason.Trim()} Perbaiki lalu simpan — barang otomatis diajukan ulang.",
        _ => string.Empty
    };

    public static string PayoutKind(string kind) => kind switch
    {
        "bank"    => "Bank",
        "ewallet" => "E-wallet",
        _         => kind
    };

    public static string PaymentKind(string kind) => kind switch
    {
        "rent_charge"     => "Tagihan sewa",
        "deposit_charge"  => "Tagihan deposit",
        "platform_fee"    => "Komisi platform",
        "deposit_forfeit" => "Potongan deposit",
        "seller_payout"   => "Pencairan ke pemilik",
        "deposit_refund"  => "Pengembalian deposit",
        "rent_refund"     => "Pengembalian sewa",
        _                 => kind
    };

    public static string PaymentStatus(string status) => status switch
    {
        "pending" => "Tertunda",
        "paid"    => "Lunas",
        "failed"  => "Gagal",
        "expired" => "Kedaluwarsa",
        _         => status
    };

    public static string PaymentDirection(string direction) => direction switch
    {
        "in"       => "Masuk",
        "out"      => "Keluar",
        "internal" => "Internal (pembukuan)",
        _          => direction
    };

    public static readonly (string Value, string Label)[] PaymentKindOptions =
    [
        ("rent_charge", "Tagihan sewa"), ("deposit_charge", "Tagihan deposit"),
        ("platform_fee", "Komisi platform"), ("deposit_forfeit", "Potongan deposit"),
        ("seller_payout", "Pencairan ke pemilik"), ("deposit_refund", "Pengembalian deposit"),
        ("rent_refund", "Pengembalian sewa")
    ];

    public static readonly (string Value, string Label)[] PaymentStatusOptions =
    [
        ("pending", "Tertunda"), ("paid", "Lunas"), ("failed", "Gagal"), ("expired", "Kedaluwarsa")
    ];

    public static string PayoutMethod(string method) => method switch
    {
        "disbursement"     => "Transfer ke rekening",
        "gateway_reversal" => "Balik ke sumber bayar",
        _                  => method
    };

    public static string CommissionMode(string mode) => mode switch
    {
        "deduct" => "Potong dari pemilik",
        "on_top" => "Tagihkan ke penyewa",
        _        => mode
    };

    public static readonly (string Value, string Label)[] PriceUnitOptions =
    [
        ("day", "Per hari"), ("hour", "Per jam"), ("week", "Per minggu"), ("month", "Per bulan")
    ];

    public static readonly (string? Value, string Label)[] BookingStatusTabs =
    [
        (null, "Semua"), ("pending", "Menunggu"), ("confirmed", "Dikonfirmasi"),
        ("active", "Berlangsung"), ("completed", "Selesai"), ("disputed", "Sengketa"),
        ("cancelled", "Dibatalkan")
    ];

    public static string PaymentChannel(string channel) => channel switch
    {
        "va_bca"            => "Transfer VA BCA",
        "va_bni"            => "Transfer VA BNI",
        "va_bri"            => "Transfer VA BRI",
        "va_permata"        => "Transfer VA Permata",
        "qris"              => "QRIS",
        "gopay"             => "GoPay",
        "cstore_alfamart"   => "Tunai di Alfamart",
        "cstore_indomaret"  => "Tunai di Indomaret",
        "credit_card"       => "Kartu kredit/debit",
        _                   => channel
    };

    public static readonly PaymentChannelOption[] PaymentChannelOptions =
    [
        new("qris",              "QRIS",                  NeedsRefundAccount: true),
        new("va_bca",            "Transfer VA BCA",        NeedsRefundAccount: true),
        new("va_bni",            "Transfer VA BNI",        NeedsRefundAccount: true),
        new("va_bri",            "Transfer VA BRI",        NeedsRefundAccount: true),
        new("va_permata",        "Transfer VA Permata",    NeedsRefundAccount: true),
        new("cstore_alfamart",   "Tunai di Alfamart",      NeedsRefundAccount: true),
        new("cstore_indomaret",  "Tunai di Indomaret",     NeedsRefundAccount: true)
    ];

    public static DepositState? Deposit(
        string bookingStatus,
        decimal depositCharged, bool depositSettled,
        decimal refunded, bool refundPaid,
        decimal forfeited)
    {
        if (depositCharged <= 0m)
        {
            return null;
        }

        if (!depositSettled)
        {
            return new DepositState(
                "Deposit belum dibayar",
                "Deposit ditagih bersama sewa dan dikembalikan setelah barang kembali.",
                DepositTone.Menunggu, depositCharged);
        }

        if (refunded <= 0m && forfeited <= 0m)
        {
            return new DepositState(
                "Deposit ditahan",
                bookingStatus == "disputed"
                    ? "Sengketa sedang diperiksa admin. Deposit ditahan sampai keputusannya keluar."
                    : "Uang Anda dititipkan platform, bukan ke pemilik barang. " +
                      "Dikembalikan penuh setelah barang dinyatakan kembali.",
                DepositTone.Ditahan, depositCharged);
        }

        if (refundPaid)
        {
            return forfeited > 0m
                ? new DepositState(
                    "Deposit sudah dikembalikan sebagian",
                    "Sisanya sudah ditransfer ke rekening pengembalian dana Anda.",
                    DepositTone.Selesai, refunded, forfeited)
                : new DepositState(
                    "Deposit sudah dikembalikan",
                    "Seluruh deposit sudah ditransfer ke rekening pengembalian dana Anda.",
                    DepositTone.Selesai, refunded);
        }

        return new DepositState(
            forfeited > 0m ? "Pengembalian deposit sedang diproses" : "Deposit sedang dikembalikan",
            "Sudah disetujui dan menunggu ditransfer ke rekening pengembalian dana Anda. " +
            "Transfer dikerjakan admin, biasanya dalam beberapa hari kerja.",
            DepositTone.Diproses, refunded, forfeited);
    }
}

public enum DepositTone
{
    Menunggu,
    Ditahan,
    Diproses,
    Selesai
}

public sealed record DepositState(
    string Judul, string Penjelasan, DepositTone Nada, decimal Nominal, decimal Dipotong = 0m);

public sealed record PaymentChannelOption(string Value, string Label, bool NeedsRefundAccount);
