using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class PayoutAccountTests(ApiFactory api)
{
    private static CreatePayoutAccountRequest Bca(string nomor) => new()
    {
        Kind          = PayoutAccountKinds.Bank,
        ProviderCode  = "BCA",
        AccountNumber = nomor,
        AccountHolder = "Budi Santoso"
    };

    [Fact]
    public async Task Rekening_pertama_otomatis_jadi_utama()
    {
        var renter = await api.RenterAsync();

        var response = await renter.Client.PostAsJsonAsync("/payout-accounts", Bca("1234567890"));

        response.EnsureSuccessStatusCode();
        var account = (await response.Content.ReadFromJsonAsync<PayoutAccountResponse>())!;

        Assert.True(account.IsDefault,
            "tanpa ini, renter bisa punya rekening terdaftar tapi tetap tanpa tujuan refund yang jelas");
    }

    [Fact]
    public async Task Nomor_rekening_tidak_pernah_dikembalikan_utuh()
    {
        var renter = await api.RenterAsync();

        var response = await renter.Client.PostAsJsonAsync("/payout-accounts", Bca("9876543210"));
        var mentah = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("9876543210", mentah);

        var account = (await response.Content.ReadFromJsonAsync<PayoutAccountResponse>())!;
        Assert.EndsWith("3210", account.AccountNumberMasked);
        Assert.StartsWith("••", account.AccountNumberMasked);
    }

    [Fact]
    public async Task Menandai_rekening_lain_sebagai_utama_melepas_yang_lama()
    {
        var renter = await api.RenterAsync();

        var pertama = await renter.Client.PostAsJsonAsync("/payout-accounts", Bca("1111111111"));
        var kedua = await renter.Client.PostAsJsonAsync("/payout-accounts", Bca("2222222222"));

        var a = (await pertama.Content.ReadFromJsonAsync<PayoutAccountResponse>())!;
        var b = (await kedua.Content.ReadFromJsonAsync<PayoutAccountResponse>())!;

        Assert.True(a.IsDefault);
        Assert.False(b.IsDefault);

        (await renter.Client.PostAsync($"/payout-accounts/{b.Id}/default", null))
            .EnsureSuccessStatusCode();

        var daftar = await renter.Client
            .GetFromJsonAsync<List<PayoutAccountResponse>>("/payout-accounts");

        Assert.Single(daftar!, x => x.IsDefault);
        Assert.True(daftar!.Single(x => x.Id == b.Id).IsDefault);
    }

    [Fact]
    public async Task Rekening_yang_sama_tidak_bisa_didaftarkan_dua_kali()
    {
        var renter = await api.RenterAsync();

        (await renter.Client.PostAsJsonAsync("/payout-accounts", Bca("5555555555")))
            .EnsureSuccessStatusCode();

        var kedua = await renter.Client.PostAsJsonAsync("/payout-accounts", Bca("5555555555"));

        Assert.Equal(HttpStatusCode.Conflict, kedua.StatusCode);
    }

    [Fact]
    public async Task Daftar_hanya_berisi_rekening_sendiri()
    {
        var satu = await api.RenterAsync();
        var dua = await api.RenterAsync();

        await satu.Client.PostAsJsonAsync("/payout-accounts", Bca("7777777777"));
        await dua.Client.PostAsJsonAsync("/payout-accounts", Bca("8888888888"));

        var daftar = await satu.Client
            .GetFromJsonAsync<List<PayoutAccountResponse>>("/payout-accounts");

        Assert.Single(daftar!);
        Assert.EndsWith("7777", daftar![0].AccountNumberMasked);
    }

    [Fact]
    public async Task Rekening_orang_lain_tidak_bisa_dijadikan_utama_atau_dihapus()
    {
        var pemilik = await api.RenterAsync();
        var penyusup = await api.RenterAsync();

        var dibuat = await pemilik.Client.PostAsJsonAsync("/payout-accounts", Bca("3333333333"));
        var account = (await dibuat.Content.ReadFromJsonAsync<PayoutAccountResponse>())!;

        Assert.Equal(HttpStatusCode.NotFound,
            (await penyusup.Client.PostAsync($"/payout-accounts/{account.Id}/default", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await penyusup.Client.DeleteAsync($"/payout-accounts/{account.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("tabungan")]
    [InlineData("")]
    public async Task Jenis_rekening_ngawur_ditolak(string kind)
    {
        var renter = await api.RenterAsync();

        var response = await renter.Client.PostAsJsonAsync("/payout-accounts",
            new CreatePayoutAccountRequest
            {
                Kind          = kind,
                ProviderCode  = "BCA",
                AccountNumber = "1234567890",
                AccountHolder = "Budi"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Nomor_rekening_bukan_angka_ditolak()
    {
        var renter = await api.RenterAsync();

        var response = await renter.Client.PostAsJsonAsync("/payout-accounts",
            Bca("12AB-56/78") with { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rekening_bisa_dihapus_kalau_tidak_ada_kewajiban_menempel()
    {
        var renter = await api.RenterAsync();

        var dibuat = await renter.Client.PostAsJsonAsync("/payout-accounts", Bca("4444444444"));
        var account = (await dibuat.Content.ReadFromJsonAsync<PayoutAccountResponse>())!;

        var hapus = await renter.Client.DeleteAsync($"/payout-accounts/{account.Id}");

        Assert.Equal(HttpStatusCode.NoContent, hapus.StatusCode);
        Assert.Empty((await renter.Client
            .GetFromJsonAsync<List<PayoutAccountResponse>>("/payout-accounts"))!);
    }

    [Fact]
    public async Task Rekening_yang_masih_jadi_tujuan_refund_tidak_bisa_dihapus()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client, price: 100_000m, deposit: 50_000m);

        var mulai = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(1100).AddHours(9);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, mulai, mulai.AddDays(2));

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        var account = await api.AddPayoutAccountAsync(renter.Client);

        await api.SettleAsync(renter.Client, booking.Id, PaymentChannels.VaBca);

        await renter.Client.PostAsJsonAsync($"/bookings/{booking.Id}/cancel",
            new CancelBookingRequest());

        var hapus = await renter.Client.DeleteAsync($"/payout-accounts/{account.Id}");

        Assert.Equal(HttpStatusCode.Conflict, hapus.StatusCode);
    }

    private static DateTimeOffset Mulai(int hariKeDepan) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(hariKeDepan).AddHours(9);

    private static async Task<PayoutAccountResponse> SatuSatunyaRekeningAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<PayoutAccountResponse>>("/payout-accounts"))!.Single();

    [Fact]
    public async Task Pemilik_barang_tidak_bisa_menghapus_rekening_terakhir_selagi_sewa_berjalan()
    {
        var mulai    = Mulai(1200);
        var skenario = await api.ActiveBookingAsync(mulai, mulai.AddDays(2));

        var rekening = await SatuSatunyaRekeningAsync(skenario.Seller.Client);
        var hapus    = await skenario.Seller.Client.DeleteAsync($"/payout-accounts/{rekening.Id}");

        Assert.Equal(HttpStatusCode.Conflict, hapus.StatusCode);
    }

    [Fact]
    public async Task Rekening_pengganti_membuat_yang_lama_bisa_dihapus_dan_sewa_tetap_selesai()
    {
        var mulai    = Mulai(1210);
        var skenario = await api.ActiveBookingAsync(mulai, mulai.AddDays(2));

        var lama = await SatuSatunyaRekeningAsync(skenario.Seller.Client);
        await api.AddPayoutAccountAsync(skenario.Seller.Client);

        var hapus = await skenario.Seller.Client.DeleteAsync($"/payout-accounts/{lama.Id}");
        Assert.Equal(HttpStatusCode.NoContent, hapus.StatusCode);

        (await skenario.Seller.Client.PostAsync($"/bookings/{skenario.Booking.Id}/return", null))
            .EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData(PaymentChannels.VaBca, HttpStatusCode.Conflict)]
    [InlineData(PaymentChannels.Gopay, HttpStatusCode.NoContent)]
    public async Task Rekening_penyewa_ditahan_hanya_kalau_bayarnya_tidak_dapat_dibalik(
        string channel, HttpStatusCode diharapkan)
    {
        var mulai    = Mulai(1220);
        var skenario = await api.ActiveBookingAsync(mulai, mulai.AddDays(2), channel: channel);

        var daftar = (await skenario.Renter.Client
            .GetFromJsonAsync<List<PayoutAccountResponse>>("/payout-accounts"))!;

        var rekening = daftar.SingleOrDefault()
                       ?? await api.AddPayoutAccountAsync(skenario.Renter.Client);

        var hapus = await skenario.Renter.Client.DeleteAsync($"/payout-accounts/{rekening.Id}");

        Assert.Equal(diharapkan, hapus.StatusCode);
    }

    [Fact]
    public async Task Sewa_yang_sudah_batal_tidak_lagi_menahan_rekening_pemilik_barang()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client, price: 100_000m, deposit: 50_000m);

        var mulai   = Mulai(1240);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, mulai, mulai.AddDays(2));

        (await renter.Client.PostAsJsonAsync(
            $"/bookings/{booking.Id}/cancel", new CancelBookingRequest())).EnsureSuccessStatusCode();

        var rekening = await SatuSatunyaRekeningAsync(seller.Client);
        var hapus    = await seller.Client.DeleteAsync($"/payout-accounts/{rekening.Id}");

        Assert.Equal(HttpStatusCode.NoContent, hapus.StatusCode);
    }

    [Fact]
    public async Task Dua_penghapusan_paralel_tidak_bisa_menghabiskan_rekening_terakhir()
    {
        var mulai    = Mulai(1250);
        var skenario = await api.ActiveBookingAsync(mulai, mulai.AddDays(2));

        var pertama = await SatuSatunyaRekeningAsync(skenario.Seller.Client);
        var kedua   = await api.AddPayoutAccountAsync(skenario.Seller.Client);

        var gerbang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tugas = new[] { pertama.Id, kedua.Id }.Select(id => Task.Run(async () =>
        {
            await gerbang.Task;
            return await skenario.Seller.Client.DeleteAsync($"/payout-accounts/{id}");
        })).ToArray();

        gerbang.SetResult();

        var hasil = await Task.WhenAll(tugas);

        Assert.Equal(1, hasil.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(1, hasil.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        Assert.Single((await skenario.Seller.Client
            .GetFromJsonAsync<List<PayoutAccountResponse>>("/payout-accounts"))!);
    }

    [Fact]
    public void Daftar_channel_terbalikkan_sama_dengan_IsReversible()
    {
        foreach (var channel in Enum.GetValues<PaymentChannel>())
        {
            Assert.Equal(
                channel.IsReversible(),
                PaymentChannels.ReversibleDbValues.Contains(channel.ToDbValue()));
        }
    }

    [Fact]
    public async Task Tanpa_token_endpoint_rekening_401()
    {
        var response = await api.CreateClient().GetAsync("/payout-accounts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
