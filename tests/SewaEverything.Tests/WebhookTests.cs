using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Payments;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class WebhookTests(ApiFactory api)
{
    private static DateTimeOffset Slot(int hariKe) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(800 + hariKe).AddHours(9);

    private sealed record Skenario(
        ApiFactory.UserContext Seller,
        ApiFactory.UserContext Renter,
        BookingResponse Booking,
        PaymentInstructionResponse Instruction);

    private async Task<Skenario> MenungguPembayaranAsync(int hariKe)
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client, price: 100_000m, deposit: 500_000m);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(hariKe), Slot(hariKe + 2));

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        var instruction = await api.PaySuccessfullyAsync(renter.Client, booking.Id);

        return new Skenario(seller, renter, booking, instruction);
    }

    [Fact]
    public async Task Webhook_yang_sama_dikirim_berkali_kali_hanya_berefek_sekali()
    {
        var s = await MenungguPembayaranAsync(1);

        var payload = ApiFactory.MidtransNotification(
            s.Instruction.OrderId, s.Instruction.Amount, "settlement");

        var pertama = await api.SendWebhookAsync(payload);
        var kedua = await api.SendWebhookAsync(payload);
        var ketiga = await api.SendWebhookAsync(payload);

        Assert.Equal(HttpStatusCode.OK, pertama.StatusCode);
        Assert.Equal(HttpStatusCode.OK, kedua.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ketiga.StatusCode);

        Assert.Contains("processed", await pertama.Content.ReadAsStringAsync());
        Assert.Contains("duplicate", await kedua.Content.ReadAsStringAsync());
        Assert.Contains("duplicate", await ketiga.Content.ReadAsStringAsync());

        var ledger = await s.Renter.Client
            .GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");

        Assert.Equal(700_000m, ledger!.AmountSettled);
        Assert.Equal(2, ledger.Entries.Count);
        Assert.True(ledger.IsSettled);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        var tercatat = await db.WebhookEvents.AsNoTracking()
            .CountAsync(e => e.EventId.StartsWith($"TRX-{s.Instruction.OrderId}"));

        Assert.Equal(1, tercatat);
    }

    [Fact]
    public async Task Webhook_ganda_yang_tiba_bersamaan_tetap_hanya_berefek_sekali()
    {
        var s = await MenungguPembayaranAsync(5);

        var payload = ApiFactory.MidtransNotification(
            s.Instruction.OrderId, s.Instruction.Amount, "settlement");

        var gerbang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tugas = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await gerbang.Task;
            return await api.SendWebhookAsync(payload);
        })).ToArray();

        gerbang.SetResult();
        var hasil = await Task.WhenAll(tugas);

        Assert.All(hasil, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var ledger = await s.Renter.Client
            .GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");

        Assert.Equal(700_000m, ledger!.AmountSettled);
        Assert.Equal(2, ledger.Entries.Count);
    }

    [Fact]
    public async Task Tanda_tangan_palsu_ditolak_dan_tidak_ada_uang_yang_tercatat()
    {
        var s = await MenungguPembayaranAsync(10);

        var payload = ApiFactory.MidtransNotification(
            s.Instruction.OrderId, s.Instruction.Amount, "settlement",
            overrideSignature: new string('a', 128));

        var response = await api.SendWebhookAsync(payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var ledger = await s.Renter.Client
            .GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");

        Assert.Equal(0m, ledger!.AmountSettled);
        Assert.False(ledger.IsSettled);
    }

    [Fact]
    public async Task Tanda_tangan_dari_kunci_lain_ditolak()
    {
        var s = await MenungguPembayaranAsync(15);

        var payload = ApiFactory.MidtransNotification(
            s.Instruction.OrderId, s.Instruction.Amount, "settlement",
            serverKey: "SB-Mid-server-KUNCI-PENYUSUP");

        var response = await api.SendWebhookAsync(payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Nominal_yang_diutak_atik_membatalkan_tanda_tangan()
    {
        var s = await MenungguPembayaranAsync(20);

        var asli = ApiFactory.MidtransNotification(
            s.Instruction.OrderId, s.Instruction.Amount, "settlement");

        var diubah = asli.Replace("700000.00", "1.00");

        var response = await api.SendWebhookAsync(diubah);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Payload_bukan_json_ditolak()
    {
        var response = await api.SendWebhookAsync("{ ini bukan json ]");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Payload_tanpa_field_wajib_ditolak()
    {
        var response = await api.SendWebhookAsync("""{"order_id":"SEWA-abc-1"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Settlement_melunasi_dan_melepas_tenggat_hold()
    {
        var s = await MenungguPembayaranAsync(25);

        var sebelum = await s.Renter.Client
            .GetFromJsonAsync<BookingResponse>($"/bookings/{s.Booking.Id}");
        Assert.NotNull(sebelum!.HoldExpiresAt);

        await api.SendWebhookAsync(ApiFactory.MidtransNotification(
            s.Instruction.OrderId, s.Instruction.Amount, "settlement"));

        var sesudah = await s.Renter.Client
            .GetFromJsonAsync<BookingResponse>($"/bookings/{s.Booking.Id}");

        Assert.Null(sesudah!.HoldExpiresAt);
        Assert.Equal(BookingStatuses.Confirmed, sesudah.Status);
    }

    [Fact]
    public async Task Booking_lunas_tidak_ikut_tersapu_job_pelepas_hold()
    {
        var s = await MenungguPembayaranAsync(30);

        await api.SendWebhookAsync(ApiFactory.MidtransNotification(
            s.Instruction.OrderId, s.Instruction.Amount, "settlement"));

        await api.SweepHoldsAsync();

        Assert.Equal(BookingStatuses.Confirmed, await api.StatusOfAsync(s.Booking.Id));
    }

    [Theory]
    [InlineData("expire", PaymentStatuses.Expired)]
    [InlineData("deny", PaymentStatuses.Failed)]
    [InlineData("cancel", PaymentStatuses.Failed)]
    public async Task Transaksi_gagal_menandai_tagihannya_gagal(string status, string diharapkan)
    {
        var s = await MenungguPembayaranAsync(35 + status.Length);

        await api.SendWebhookAsync(ApiFactory.MidtransNotification(
            s.Instruction.OrderId, s.Instruction.Amount, status));

        var entries = await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id);

        Assert.All(entries, e => Assert.Equal(diharapkan, e.Status));

        var ledger = await s.Renter.Client
            .GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");
        Assert.Equal(0m, ledger!.AmountSettled);
    }

    [Fact]
    public async Task Setelah_tagihan_kedaluwarsa_renter_bisa_menagih_ulang()
    {
        var s = await MenungguPembayaranAsync(50);

        await api.SendWebhookAsync(ApiFactory.MidtransNotification(
            s.Instruction.OrderId, s.Instruction.Amount, "expire"));

        var lagi = await api.PaySuccessfullyAsync(s.Renter.Client, s.Booking.Id);

        Assert.NotEqual(s.Instruction.OrderId, lagi.OrderId);
        Assert.Equal(PaymentStatuses.Pending, lagi.Status);
    }

    [Fact]
    public async Task Notifikasi_status_pending_dicatat_tanpa_melunasi()
    {
        var s = await MenungguPembayaranAsync(55);

        var response = await api.SendWebhookAsync(ApiFactory.MidtransNotification(
            s.Instruction.OrderId, s.Instruction.Amount, "pending"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ledger = await s.Renter.Client
            .GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");

        Assert.Equal(0m, ledger!.AmountSettled);
        Assert.False(ledger.IsSettled);
    }

    [Fact]
    public async Task Notifikasi_berbeda_untuk_transaksi_sama_tetap_diproses()
    {
        var s = await MenungguPembayaranAsync(60);

        var pending = await api.SendWebhookAsync(ApiFactory.MidtransNotification(
            s.Instruction.OrderId, s.Instruction.Amount, "pending"));

        var settlement = await api.SendWebhookAsync(ApiFactory.MidtransNotification(
            s.Instruction.OrderId, s.Instruction.Amount, "settlement"));

        Assert.Contains("processed", await pending.Content.ReadAsStringAsync());
        Assert.Contains("processed", await settlement.Content.ReadAsStringAsync());

        var ledger = await s.Renter.Client
            .GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");
        Assert.True(ledger!.IsSettled);
    }

    [Fact]
    public async Task Transaksi_tak_dikenal_dijawab_200_supaya_tidak_diulang_selamanya()
    {
        var payload = ApiFactory.MidtransNotification(
            $"SEWA-{Guid.NewGuid():N}-1", 123_000m, "settlement");

        var response = await api.SendWebhookAsync(payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ignored", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Uang_yang_datang_untuk_booking_batal_dicatat_lalu_langsung_jadi_kewajiban_refund()
    {
        var s = await MenungguPembayaranAsync(70);

        await api.ExpireHoldAsync(s.Booking.Id);
        await api.SweepHoldsAsync();
        Assert.Equal(BookingStatuses.Cancelled, await api.StatusOfAsync(s.Booking.Id));

        var response = await api.SendWebhookAsync(ApiFactory.MidtransNotification(
            s.Instruction.OrderId, s.Instruction.Amount, "settlement"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ledger = await s.Renter.Client
            .GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");

        Assert.Equal(700_000m, ledger!.AmountSettled);

        Assert.Equal(700_000m, ledger.AmountRefundPending);

        Assert.Contains(ledger.Entries, e => e.Kind == PaymentKinds.RentRefund);
        Assert.Contains(ledger.Entries, e => e.Kind == PaymentKinds.DepositRefund);
    }
}
