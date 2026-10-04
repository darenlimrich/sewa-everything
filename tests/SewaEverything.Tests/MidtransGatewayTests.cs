using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Payments;

namespace SewaEverything.Tests;

public class MidtransGatewayTests
{
    private static ChargeRequest Permintaan() => new(
        OrderId: "SEWA-uji-1",
        GrossAmount: 250_000,
        Channel: PaymentChannel.Gopay,
        CustomerName: "Penyewa Uji",
        CustomerEmail: "uji@sewaeverything.local",
        ItemTitle: "Kamera");

    private static MidtransPaymentGateway Gateway(string serverKey, HttpMessageHandler? handler = null) =>
        new(new HttpClient(handler ?? new TidakPernahDipanggil()),
            new Kredensial(new MidtransOptions { ServerKey = serverKey }),
            NullLogger<MidtransPaymentGateway>.Instance);

    [Fact]
    public async Task Tanpa_server_key_gagal_sebagai_kegagalan_gateway()
    {
        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(
            () => Gateway("").ChargeAsync(Permintaan()));

        Assert.Contains("belum", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Server_key_berisi_spasi_juga_ditolak()
    {
        await Assert.ThrowsAsync<PaymentGatewayException>(
            () => Gateway("   ").ChargeAsync(Permintaan()));
    }

    [Fact]
    public async Task Gagal_menyambung_gagal_sebagai_kegagalan_gateway()
    {
        var gateway = Gateway("SB-Mid-server-uji",
            new Melempar(new HttpRequestException("connection refused")));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.ChargeAsync(Permintaan()));
    }

    [Fact]
    public async Task Waktu_habis_gagal_sebagai_kegagalan_gateway()
    {
        var gateway = Gateway("SB-Mid-server-uji", new Melempar(new TaskCanceledException("timeout")));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.ChargeAsync(Permintaan()));
    }

    [Fact]
    public async Task Pembatalan_pemanggil_tidak_disamarkan_jadi_kegagalan_gateway()
    {
        var gateway = Gateway("SB-Mid-server-uji", new Melempar(new TaskCanceledException("dibatalkan")));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => gateway.ChargeAsync(Permintaan(), cts.Token));
    }

    [Fact]
    public async Task Gateway_yang_menolak_gagal_sebagai_kegagalan_gateway()
    {
        var gateway = Gateway("SB-Mid-server-uji",
            new Menjawab(HttpStatusCode.Unauthorized, """{"status_code":"401"}"""));

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(
            () => gateway.ChargeAsync(Permintaan()));

        Assert.Contains("401", ex.Message);
    }

    [Theory]
    [InlineData("bukan json sama sekali")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"transaction_id":404}""")]
    [InlineData("""{"tanpa_transaction_id":"ya"}""")]
    public async Task Respons_yang_tidak_terduga_gagal_sebagai_kegagalan_gateway(string body)
    {
        var gateway = Gateway("SB-Mid-server-uji", new Menjawab(HttpStatusCode.OK, body));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.ChargeAsync(Permintaan()));
    }

    [Fact]
    public async Task Respons_gopay_yang_benar_dibaca_jadi_tautan_lanjutan()
    {
        var gateway = Gateway("SB-Mid-server-uji", new Menjawab(HttpStatusCode.OK, """
            {
              "transaction_id": "TRX-1",
              "actions": [ { "name": "deeplink-redirect", "url": "https://sandbox.example/pay/1" } ],
              "expiry_time": "2026-08-20 09:00:00"
            }
            """));

        var hasil = await gateway.ChargeAsync(Permintaan());

        Assert.Equal("TRX-1", hasil.GatewayTransactionId);
        Assert.Equal("https://sandbox.example/pay/1", hasil.RedirectUrl);
        Assert.NotNull(hasil.ExpiresAt);
    }

    [Fact]
    public async Task Kunci_yang_dijawab_401_berarti_salah_lingkungan()
    {
        var gateway = Gateway("", new Menjawab(HttpStatusCode.Unauthorized,
            """{"status_code":"401","status_message":"Unknown Merchant server_key/id"}"""));

        Assert.Equal(GatewayKeyVerdict.WrongEnvironment,
            await gateway.VerifyServerKeyAsync("Mid-server-entah", isProduction: false));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.OK)]
    public async Task Transaksi_tidak_ada_berarti_kuncinya_benar_lingkungannya(HttpStatusCode status)
    {
        var gateway = Gateway("", new Menjawab(status,
            """{"status_code":"404","status_message":"Transaction doesn't exist."}"""));

        Assert.Equal(GatewayKeyVerdict.BelongsToEnvironment,
            await gateway.VerifyServerKeyAsync("Mid-server-entah", isProduction: false));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Jawaban_di_luar_dugaan_tidak_membuktikan_kunci(HttpStatusCode status)
    {
        var gateway = Gateway("", new Menjawab(status, "{}"));

        Assert.Equal(GatewayKeyVerdict.Unverifiable,
            await gateway.VerifyServerKeyAsync("Mid-server-entah", isProduction: false));
    }

    [Fact]
    public async Task Midtrans_tak_terjangkau_membuat_kunci_tidak_dapat_diperiksa()
    {
        var gateway = Gateway("", new Melempar(new HttpRequestException("connection refused")));

        Assert.Equal(GatewayKeyVerdict.Unverifiable,
            await gateway.VerifyServerKeyAsync("Mid-server-entah", isProduction: false));
    }

    [Fact]
    public async Task Kunci_kosong_ditolak_tanpa_menghubungi_midtrans()
    {
        var perekam = new Merekam(HttpStatusCode.NotFound, """{"status_code":"404"}""");

        Assert.Equal(GatewayKeyVerdict.WrongEnvironment,
            await Gateway("", perekam).VerifyServerKeyAsync("   ", isProduction: false));

        Assert.Null(perekam.Terakhir);
    }

    [Theory]
    [InlineData(false, "api.sandbox.midtrans.com")]
    [InlineData(true, "api.midtrans.com")]
    public async Task Pemeriksaan_menembak_lingkungan_yang_dipilih(bool produksi, string host)
    {
        var perekam = new Merekam(HttpStatusCode.NotFound, """{"status_code":"404"}""");

        await Gateway("", perekam).VerifyServerKeyAsync("Mid-server-entah", produksi);

        Assert.Equal(host, perekam.Terakhir!.RequestUri!.Host);
        Assert.Equal(HttpMethod.Get, perekam.Terakhir.Method);

        Assert.Equal("Basic", perekam.Terakhir.Headers.Authorization!.Scheme);
    }

    private sealed class Kredensial(MidtransOptions options) : IMidtransCredentials
    {
        public ValueTask<MidtransOptions> CurrentAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(options);
    }

    private sealed class TidakPernahDipanggil : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("tidak boleh sampai mengirim HTTP");
    }

    private sealed class Melempar(Exception ex) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            throw ex;
        }
    }

    private sealed class Menjawab(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private sealed class Merekam(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Terakhir { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Terakhir = request;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body)
            });
        }
    }
}
