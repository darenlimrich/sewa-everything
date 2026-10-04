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
public sealed class OwnerPaymentGatewayTests(ApiFactory api)
{
    private const string KunciServerBaru = "SB-Mid-server-KUNCI-PANEL-9911";
    private const string KunciKlienBaru  = "SB-Mid-client-PANEL-2026";

    private async Task BersihkanAsync()
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        await db.Database.ExecuteSqlRawAsync("""
            UPDATE platform_settings
               SET midtrans_server_key = NULL,
                   midtrans_client_key = NULL,
                   midtrans_is_production = false
            """);
    }

    private async Task<PaymentGatewayResponse> LihatAsync()
    {
        var owner = await api.ClientAsOwnerAsync();
        return (await owner.GetFromJsonAsync<PaymentGatewayResponse>("/owner/payment-gateway"))!;
    }

    [Fact]
    public async Task Selama_belum_diisi_sumbernya_konfigurasi()
    {
        await BersihkanAsync();

        var gateway = await LihatAsync();

        Assert.True(gateway.IsConfigured);
        Assert.Equal(GatewayCredentialSources.Configuration, gateway.Source);
        Assert.False(gateway.IsProduction);

        Assert.Equal($"••••{ApiFactory.MidtransServerKey[^4..]}", gateway.ServerKeyHint);
    }

    [Fact]
    public async Task Owner_mengisi_kunci_lalu_sumbernya_pindah_ke_database()
    {
        try
        {
            var owner = await api.ClientAsOwnerAsync();

            var response = await owner.PutAsJsonAsync("/owner/payment-gateway",
                new UpdatePaymentGatewayRequest
                {
                    ServerKey    = KunciServerBaru,
                    ClientKey    = KunciKlienBaru,
                    IsProduction = false
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var disimpan = await response.Content.ReadFromJsonAsync<PaymentGatewayResponse>();
            Assert.Equal(GatewayCredentialSources.Database, disimpan!.Source);
            Assert.Equal("••••9911", disimpan.ServerKeyHint);

            Assert.Equal(KunciKlienBaru, disimpan.ClientKey);

            var dibaca = await LihatAsync();
            Assert.Equal(GatewayCredentialSources.Database, dibaca.Source);
            Assert.Equal("••••9911", dibaca.ServerKeyHint);
        }
        finally
        {
            await BersihkanAsync();
        }
    }

    [Fact]
    public async Task Server_key_tidak_pernah_ikut_di_respons()
    {
        try
        {
            var owner = await api.ClientAsOwnerAsync();

            var simpan = await owner.PutAsJsonAsync("/owner/payment-gateway",
                new UpdatePaymentGatewayRequest
                {
                    ServerKey    = KunciServerBaru,
                    ClientKey    = KunciKlienBaru,
                    IsProduction = false
                });

            var jsonSimpan = await simpan.Content.ReadAsStringAsync();
            var jsonBaca = await (await owner.GetAsync("/owner/payment-gateway"))
                .Content.ReadAsStringAsync();

            Assert.DoesNotContain(KunciServerBaru, jsonSimpan);
            Assert.DoesNotContain(KunciServerBaru, jsonBaca);

            var setelan = await (await owner.GetAsync("/owner/settings")).Content.ReadAsStringAsync();
            Assert.DoesNotContain(KunciServerBaru, setelan);
        }
        finally
        {
            await BersihkanAsync();
        }
    }

    [Fact]
    public async Task Mode_sandbox_dengan_kunci_produksi_ditolak()
    {
        var owner = await api.ClientAsOwnerAsync();

        var response = await owner.PutAsJsonAsync("/owner/payment-gateway",
            new UpdatePaymentGatewayRequest
            {
                ServerKey    = "Mid-server-KUNCI-PRODUKSI-1234",
                ClientKey    = "Mid-client-PRODUKSI-1234",
                IsProduction = false
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(GatewayCredentialSources.Configuration, (await LihatAsync()).Source);
    }

    [Fact]
    public async Task Mode_produksi_dengan_kunci_sandbox_ditolak()
    {
        var owner = await api.ClientAsOwnerAsync();

        var response = await owner.PutAsJsonAsync("/owner/payment-gateway",
            new UpdatePaymentGatewayRequest
            {
                ServerKey    = KunciServerBaru,
                ClientKey    = KunciKlienBaru,
                IsProduction = true
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Kunci_sandbox_tanpa_awalan_SB_diterima()
    {
        const string kunci = "Mid-server-TANPA-AWALAN-SB-2026";

        api.Gateway.VerdictOverride = GatewayKeyVerdict.BelongsToEnvironment;

        try
        {
            var owner = await api.ClientAsOwnerAsync();

            var response = await owner.PutAsJsonAsync("/owner/payment-gateway",
                new UpdatePaymentGatewayRequest
                {
                    ServerKey    = kunci,
                    ClientKey    = "Mid-client-TANPA-AWALAN-SB",
                    IsProduction = false
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var disimpan = await response.Content.ReadFromJsonAsync<PaymentGatewayResponse>();
            Assert.Equal(GatewayCredentialSources.Database, disimpan!.Source);
            Assert.False(disimpan.IsProduction);

            Assert.Contains((kunci, false), api.Gateway.KeyChecks);
        }
        finally
        {
            api.Gateway.VerdictOverride = null;
            await BersihkanAsync();
        }
    }

    [Fact]
    public async Task Midtrans_tak_terjangkau_maka_kunci_tidak_disimpan()
    {
        await BersihkanAsync();

        api.Gateway.VerdictOverride = GatewayKeyVerdict.Unverifiable;

        try
        {
            var owner = await api.ClientAsOwnerAsync();

            var response = await owner.PutAsJsonAsync("/owner/payment-gateway",
                new UpdatePaymentGatewayRequest
                {
                    ServerKey    = KunciServerBaru,
                    ClientKey    = KunciKlienBaru,
                    IsProduction = false
                });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(GatewayCredentialSources.Configuration, (await LihatAsync()).Source);
        }
        finally
        {
            api.Gateway.VerdictOverride = null;
            await BersihkanAsync();
        }
    }

    [Fact]
    public async Task Server_key_kosong_mempertahankan_kunci_yang_tersimpan()
    {
        try
        {
            var owner = await api.ClientAsOwnerAsync();

            await owner.PutAsJsonAsync("/owner/payment-gateway", new UpdatePaymentGatewayRequest
            {
                ServerKey    = KunciServerBaru,
                ClientKey    = KunciKlienBaru,
                IsProduction = false
            });

            var response = await owner.PutAsJsonAsync("/owner/payment-gateway",
                new UpdatePaymentGatewayRequest
                {
                    ServerKey    = null,
                    ClientKey    = "SB-Mid-client-GANTI-0001",
                    IsProduction = false
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var hasil = await response.Content.ReadFromJsonAsync<PaymentGatewayResponse>();
            Assert.Equal("••••9911", hasil!.ServerKeyHint);
            Assert.Equal("SB-Mid-client-GANTI-0001", hasil.ClientKey);
        }
        finally
        {
            await BersihkanAsync();
        }
    }

    [Fact]
    public async Task Menyimpan_tanpa_pernah_ada_server_key_ditolak()
    {
        await BersihkanAsync();

        var owner = await api.ClientAsOwnerAsync();

        var response = await owner.PutAsJsonAsync("/owner/payment-gateway",
            new UpdatePaymentGatewayRequest
            {
                ServerKey    = null,
                ClientKey    = KunciKlienBaru,
                IsProduction = false
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_tidak_boleh_melihat_maupun_mengubah_kredensial()
    {
        var admin = await api.ClientAsAdminAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/owner/payment-gateway")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PutAsJsonAsync("/owner/payment-gateway", new UpdatePaymentGatewayRequest
            {
                ServerKey = KunciServerBaru,
                ClientKey = KunciKlienBaru
            })).StatusCode);
    }

    [Fact]
    public async Task Kunci_dari_database_yang_dipakai_memverifikasi_notifikasi()
    {
        var seller  = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();
        var item    = await api.CreateItemAsync(seller.Client, price: 100_000m, deposit: 500_000m);

        var mulai   = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(1501).AddHours(9);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, mulai, mulai.AddDays(2));

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        var instruksi = await api.PaySuccessfullyAsync(renter.Client, booking.Id);

        try
        {
            var owner = await api.ClientAsOwnerAsync();

            (await owner.PutAsJsonAsync("/owner/payment-gateway", new UpdatePaymentGatewayRequest
            {
                ServerKey    = KunciServerBaru,
                ClientKey    = KunciKlienBaru,
                IsProduction = false
            })).EnsureSuccessStatusCode();

            var kunciLama = await api.SendWebhookAsync(ApiFactory.MidtransNotification(
                instruksi.OrderId, instruksi.Amount, "settlement"));

            Assert.Equal(HttpStatusCode.Unauthorized, kunciLama.StatusCode);

            var kunciBaru = await api.SendWebhookAsync(ApiFactory.MidtransNotification(
                instruksi.OrderId, instruksi.Amount, "settlement", serverKey: KunciServerBaru));

            Assert.Equal(HttpStatusCode.OK, kunciBaru.StatusCode);

            var ledger = await renter.Client.GetFromJsonAsync<BookingLedgerResponse>(
                $"/bookings/{booking.Id}/ledger");

            Assert.True(ledger!.IsSettled);
        }
        finally
        {
            await BersihkanAsync();
        }
    }
}
