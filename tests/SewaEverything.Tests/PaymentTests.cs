using System.Net;
using System.Net.Http.Json;
using SewaEverything.Client;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class PaymentTests(ApiFactory api)
{
    private static DateTimeOffset Slot(int hariKe) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(700 + hariKe).AddHours(9);

    private sealed record Skenario(
        ApiFactory.UserContext Seller, ApiFactory.UserContext Renter, BookingResponse Booking);

    private async Task<Skenario> ConfirmedAsync(
        int hariKe, decimal price = 100_000m, decimal deposit = 500_000m)
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client, price: price, deposit: deposit);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(hariKe), Slot(hariKe + 2));

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        return new Skenario(seller, renter, booking);
    }

    [Fact]
    public async Task Renter_bisa_membayar_booking_yang_sudah_disetujui()
    {
        var s = await ConfirmedAsync(1);

        var instruksi = await api.PaySuccessfullyAsync(s.Renter.Client, s.Booking.Id, PaymentChannels.Gopay);

        Assert.Equal(700_000m, instruksi.Amount);
        Assert.Equal(PaymentStatuses.Pending, instruksi.Status);
        Assert.StartsWith("SEWA-", instruksi.OrderId);
        Assert.NotNull(instruksi.RedirectUrl);
    }

    [Fact]
    public async Task Tagihan_tercatat_sebagai_dua_baris_sewa_dan_deposit()
    {
        var s = await ConfirmedAsync(5);
        await api.PaySuccessfullyAsync(s.Renter.Client, s.Booking.Id);

        var entries = await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id);

        Assert.Equal(2, entries.Count);

        var sewa = entries.Single(e => e.Kind == PaymentKinds.RentCharge);
        var deposit = entries.Single(e => e.Kind == PaymentKinds.DepositCharge);

        Assert.Equal(200_000m, sewa.Amount);
        Assert.Equal(500_000m, deposit.Amount);
        Assert.All(entries, e => Assert.Equal(PaymentDirections.In, e.Direction));
        Assert.All(entries, e => Assert.Equal(PaymentStatuses.Pending, e.Status));
    }

    [Fact]
    public async Task Deposit_nol_tidak_menghasilkan_baris_deposit()
    {
        var s = await ConfirmedAsync(10, deposit: 0m);
        await api.PaySuccessfullyAsync(s.Renter.Client, s.Booking.Id);

        var entries = await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id);

        Assert.Single(entries);
        Assert.Equal(PaymentKinds.RentCharge, entries[0].Kind);
    }

    [Fact]
    public async Task Pay_dipanggil_berkali_kali_hanya_menagih_sekali()
    {
        var s = await ConfirmedAsync(15);
        await api.AddPayoutAccountAsync(s.Renter.Client);

        var pertama = await api.PaySuccessfullyAsync(s.Renter.Client, s.Booking.Id, PaymentChannels.VaBca);
        var kedua = await api.PaySuccessfullyAsync(s.Renter.Client, s.Booking.Id, PaymentChannels.VaBca);
        var ketiga = await api.PaySuccessfullyAsync(s.Renter.Client, s.Booking.Id, PaymentChannels.VaBca);

        Assert.Equal(pertama.OrderId, kedua.OrderId);
        Assert.Equal(pertama.OrderId, ketiga.OrderId);
        Assert.Equal(pertama.VirtualAccountNumber, kedua.VirtualAccountNumber);
        Assert.NotNull(pertama.VirtualAccountNumber);

        var entries = await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task Pay_berulang_tidak_memanggil_gateway_lagi()
    {
        var s = await ConfirmedAsync(20);

        var sebelum = api.Gateway.Charges.Count;

        await api.PaySuccessfullyAsync(s.Renter.Client, s.Booking.Id);
        await api.PaySuccessfullyAsync(s.Renter.Client, s.Booking.Id);
        await api.PaySuccessfullyAsync(s.Renter.Client, s.Booking.Id);

        Assert.Equal(sebelum + 1, api.Gateway.Charges.Count);
    }

    [Fact]
    public async Task Booking_yang_belum_disetujui_belum_bisa_dibayar()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);

        var booking = await api.CreateBookingAsync(renter.Client, item.Id, Slot(25), Slot(27));

        var response = await api.PayAsync(renter.Client, booking.Id, PaymentChannels.Gopay);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Channel_tidak_dikenal_ditolak()
    {
        var s = await ConfirmedAsync(30);

        var response = await api.PayAsync(s.Renter.Client, s.Booking.Id, "transfer_pulsa");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Orang_lain_tidak_bisa_membayar_booking_yang_bukan_miliknya()
    {
        var s = await ConfirmedAsync(35);
        var orangLain = await api.RenterAsync();

        var response = await api.PayAsync(orangLain.Client, s.Booking.Id, PaymentChannels.Gopay);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Seller_tidak_bisa_membayar_booking_barangnya_sendiri()
    {
        var s = await ConfirmedAsync(40);

        var response = await api.PayAsync(s.Seller.Client, s.Booking.Id, PaymentChannels.Gopay);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Booking_yang_sudah_lunas_tidak_bisa_ditagih_lagi()
    {
        var s = await ConfirmedAsync(45);
        await api.SettleAsync(s.Renter.Client, s.Booking.Id);

        var response = await api.PayAsync(s.Renter.Client, s.Booking.Id, PaymentChannels.Gopay);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(PaymentChannels.VaBca)]
    [InlineData(PaymentChannels.Qris)]
    [InlineData(PaymentChannels.CstoreAlfamart)]
    public async Task Channel_tak_terbalikkan_menuntut_rekening_refund_lebih_dulu(string channel)
    {
        var s = await ConfirmedAsync(50 + channel.Length);

        var ditolak = await api.PayAsync(s.Renter.Client, s.Booking.Id, channel);
        Assert.Equal(HttpStatusCode.Conflict, ditolak.StatusCode);

        await api.AddPayoutAccountAsync(s.Renter.Client);

        var diterima = await api.PayAsync(s.Renter.Client, s.Booking.Id, channel);
        Assert.Equal(HttpStatusCode.OK, diterima.StatusCode);
    }

    public static TheoryData<string> ChannelTerbalikkanYangDapatDitagih()
    {
        var data = new TheoryData<string>();

        foreach (var nilai in PaymentChannels.ReversibleDbValues)
        {
            if (!PaymentChannels.FromDbValue(nilai).NeedsClientToken())
            {
                data.Add(nilai);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ChannelTerbalikkanYangDapatDitagih))]
    public async Task Channel_terbalikkan_tidak_menuntut_rekening(string channel)
    {
        var s = await ConfirmedAsync(60 + channel.Length);

        var response = await api.PayAsync(s.Renter.Client, s.Booking.Id, channel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Gateway_yang_menolak_tidak_meninggalkan_tagihan_menggantung()
    {
        var s = await ConfirmedAsync(70);

        api.Gateway.FailWith = "Sandbox sedang tidak bisa dihubungi.";

        try
        {
            var response = await api.PayAsync(s.Renter.Client, s.Booking.Id, PaymentChannels.Gopay);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }
        finally
        {
            api.Gateway.FailWith = null;
        }

        var entries = await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id);
        Assert.Empty(entries);

        var lagi = await api.PayAsync(s.Renter.Client, s.Booking.Id, PaymentChannels.Gopay);
        Assert.Equal(HttpStatusCode.OK, lagi.StatusCode);
    }

    [Fact]
    public async Task Mode_on_top_menagihkan_komisi_ke_renter()
    {
        var owner = await api.ClientAsOwnerAsync();

        await owner.PutAsJsonAsync("/owner/settings", new UpdatePlatformSettingsRequest
        {
            CommissionRate   = 0.10m,
            CommissionMode   = CommissionModes.OnTop,
            ApprovalMinutes  = 1440,
            PaymentMinutes   = 60,
            ReturnWindowDays = 3
        });

        try
        {
            var s = await ConfirmedAsync(80, price: 100_000m, deposit: 50_000m);
            await api.PaySuccessfullyAsync(s.Renter.Client, s.Booking.Id);

            var entries = await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id);

            Assert.Equal(220_000m, entries.Single(e => e.Kind == PaymentKinds.RentCharge).Amount);
            Assert.Equal(50_000m, entries.Single(e => e.Kind == PaymentKinds.DepositCharge).Amount);
            Assert.Equal(270_000m, entries.Sum(e => e.Amount));
        }
        finally
        {
            await owner.PutAsJsonAsync("/owner/settings", new UpdatePlatformSettingsRequest
            {
                CommissionRate   = 0.05m,
                CommissionMode   = CommissionModes.Deduct,
                ApprovalMinutes  = 1440,
                PaymentMinutes   = 60,
                ReturnWindowDays = 3
            });
        }
    }

    [Fact]
    public async Task Komisi_belum_dialokasikan_saat_uang_baru_masuk()
    {
        var s = await ConfirmedAsync(90);
        await api.SettleAsync(s.Renter.Client, s.Booking.Id);

        var entries = await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id);

        Assert.DoesNotContain(entries, e => e.Kind == PaymentKinds.PlatformFee);
        Assert.DoesNotContain(entries, e => e.Kind == PaymentKinds.SellerPayout);
    }

    [Fact]
    public async Task Ledger_merangkum_posisi_keuangan_booking()
    {
        var s = await ConfirmedAsync(95);
        await api.SettleAsync(s.Renter.Client, s.Booking.Id);

        var ledger = await s.Renter.Client
            .GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");

        Assert.Equal(700_000m, ledger!.AmountDue);
        Assert.Equal(700_000m, ledger.AmountSettled);
        Assert.Equal(0m, ledger.AmountRefundPending);
        Assert.True(ledger.IsSettled);
    }

    [Fact]
    public async Task Ledger_tidak_bisa_dilihat_orang_luar()
    {
        var s = await ConfirmedAsync(100);
        var orangLain = await api.RenterAsync();

        var response = await orangLain.Client.GetAsync($"/bookings/{s.Booking.Id}/ledger");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Channel_yang_butuh_token_klien_ditolak_400_bukan_502()
    {
        var s = await ConfirmedAsync(120);

        var response = await api.PayAsync(s.Renter.Client, s.Booking.Id, PaymentChannels.CreditCard);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var isi = await response.Content.ReadAsStringAsync();
        Assert.Contains("Kartu kredit", isi);

        Assert.Empty(await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id));
    }

    [Fact]
    public async Task Setiap_channel_yang_ditawarkan_klien_diterima_server()
    {
        foreach (var pilihan in Labels.PaymentChannelOptions)
        {
            Assert.True(PaymentChannels.IsKnown(pilihan.Value),
                $"'{pilihan.Value}' ditawarkan klien tapi tidak dikenal server.");

            Assert.False(PaymentChannels.FromDbValue(pilihan.Value).NeedsClientToken(),
                $"'{pilihan.Value}' ditawarkan klien padahal server menolaknya.");
        }

        Assert.DoesNotContain(Labels.PaymentChannelOptions,
            o => o.Value == PaymentChannels.CreditCard);

        Assert.DoesNotContain(Labels.PaymentChannelOptions,
            o => o.Value == PaymentChannels.Gopay);
    }
}
