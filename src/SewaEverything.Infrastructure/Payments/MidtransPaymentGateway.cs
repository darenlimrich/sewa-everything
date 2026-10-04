using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SewaEverything.Domain;

namespace SewaEverything.Infrastructure.Payments;

public sealed class MidtransPaymentGateway(
    HttpClient http,
    IMidtransCredentials credentials,
    ILogger<MidtransPaymentGateway> logger) : IPaymentGateway
{
    public const string ProviderName = "midtrans";

    public string Provider => ProviderName;

    public async Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct = default)
    {
        var options = await credentials.CurrentAsync(ct);

        if (string.IsNullOrWhiteSpace(options.ServerKey))
        {
            throw new PaymentGatewayException(
                "Gerbang pembayaran belum siap menerima tagihan — kunci Midtrans belum diisi " +
                "superadmin. Tidak ada uang yang tertagih; coba lagi setelah kuncinya terpasang.");
        }

        using var message = new HttpRequestMessage(
            HttpMethod.Post, $"{options.BaseUrl}/v2/charge")
        {
            Content = JsonContent.Create(BuildPayload(request, options))
        };

        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ServerKey}:")));

        if (!options.IsProduction && !string.IsNullOrWhiteSpace(options.NotificationUrl))
        {
            message.Headers.TryAddWithoutValidation("X-Override-Notification", options.NotificationUrl);
        }

        HttpResponseMessage response;

        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException ||
                                   (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogError(ex, "Midtrans tidak bisa dihubungi untuk charge {OrderId}", request.OrderId);

            throw new PaymentGatewayException(
                "Gerbang pembayaran tidak bisa dihubungi. Tidak ada tagihan yang tersimpan — coba lagi.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Midtrans menolak charge {OrderId}: HTTP {Status}",
                    request.OrderId, (int)response.StatusCode);

                throw new PaymentGatewayException(
                    $"Gateway menolak permintaan pembayaran (HTTP {(int)response.StatusCode}).");
            }

            return ReadResult(body, request.Channel);
        }
    }

    public async Task<GatewayKeyVerdict> VerifyServerKeyAsync(
        string serverKey, bool isProduction, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serverKey))
        {
            return GatewayKeyVerdict.WrongEnvironment;
        }

        var url = $"{MidtransOptions.UrlFor(isProduction)}/v2/{Guid.NewGuid():N}/status";

        using var message = new HttpRequestMessage(HttpMethod.Get, url);

        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{serverKey.Trim()}:")));

        try
        {
            using var response = await http.SendAsync(message, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return GatewayKeyVerdict.WrongEnvironment;
            }

            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound)
            {
                return GatewayKeyVerdict.BelongsToEnvironment;
            }

            logger.LogError(
                "Midtrans menjawab HTTP {Status} saat memeriksa server key; belum terbukti",
                (int)response.StatusCode);

            return GatewayKeyVerdict.Unverifiable;
        }
        catch (Exception ex) when (ex is HttpRequestException ||
                                   (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogError(ex, "Midtrans tidak bisa dihubungi untuk memeriksa server key");

            return GatewayKeyVerdict.Unverifiable;
        }
    }

    private static object BuildPayload(ChargeRequest request, MidtransOptions options)
    {
        var transaction = new
        {
            order_id     = request.OrderId,
            gross_amount = request.GrossAmount
        };

        var customer = new
        {
            first_name = request.CustomerName,
            email      = request.CustomerEmail
        };

        return request.Channel switch
        {
            PaymentChannel.VaBca or PaymentChannel.VaBni or PaymentChannel.VaBri => new
            {
                payment_type        = "bank_transfer",
                transaction_details = transaction,
                customer_details    = customer,
                bank_transfer       = new { bank = BankCode(request.Channel) }
            },

            PaymentChannel.VaPermata => new
            {
                payment_type        = "permata",
                transaction_details = transaction,
                customer_details    = customer
            },

            PaymentChannel.Qris => new
            {
                payment_type        = "qris",
                transaction_details = transaction,
                customer_details    = customer,
                qris                = new
                {
                    acquirer = string.IsNullOrWhiteSpace(options.QrisAcquirer)
                        ? "gopay"
                        : options.QrisAcquirer
                }
            },

            PaymentChannel.Gopay => new
            {
                payment_type        = "gopay",
                transaction_details = transaction,
                customer_details    = customer
            },

            PaymentChannel.CstoreAlfamart or PaymentChannel.CstoreIndomaret => new
            {
                payment_type        = "cstore",
                transaction_details = transaction,
                customer_details    = customer,
                cstore              = new
                {
                    store   = request.Channel == PaymentChannel.CstoreAlfamart ? "alfamart" : "indomaret",
                    message = request.ItemTitle
                }
            },

            PaymentChannel.CreditCard => new
            {
                payment_type        = "credit_card",
                transaction_details = transaction,
                customer_details    = customer,
                credit_card         = new { secure = true }
            },

            _ => throw new ArgumentOutOfRangeException(
                nameof(request), request.Channel, "channel tidak didukung gateway")
        };
    }

    private static string BankCode(PaymentChannel channel) => channel switch
    {
        PaymentChannel.VaBca => "bca",
        PaymentChannel.VaBni => "bni",
        PaymentChannel.VaBri => "bri",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "bukan channel VA")
    };

    private static ChargeResult ReadResult(string body, PaymentChannel channel)
    {
        try
        {
            return Parse(body, channel);
        }
        catch (Exception ex) when (ex is not PaymentGatewayException)
        {
            throw new PaymentGatewayException(
                "Respons gerbang pembayaran tidak bisa dibaca. Tagihan belum tersimpan.");
        }
    }

    private static ChargeResult Parse(string body, PaymentChannel channel)
    {
        var json = JsonNode.Parse(body)?.AsObject()
            ?? throw new PaymentGatewayException("Respons gateway bukan JSON yang bisa dibaca.");

        string? Text(string key) => json[key]?.GetValue<string>();

        var va = channel == PaymentChannel.VaPermata
            ? Text("permata_va_number")
            : json["va_numbers"]?.AsArray().FirstOrDefault()?["va_number"]?.GetValue<string>();

        string? Action(string name) => json["actions"]?.AsArray()
            .FirstOrDefault(a => a?["name"]?.GetValue<string>() == name)
            ?["url"]?.GetValue<string>();

        var qrImage = Action("generate-qr-code");
        var redirect = Action("deeplink-redirect") ?? qrImage;

        DateTime? expiry = null;
        if (Text("expiry_time") is { } raw
            && DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            expiry = parsed;
        }

        return new ChargeResult(
            GatewayTransactionId: Text("transaction_id")
                ?? throw new PaymentGatewayException("Respons gateway tanpa transaction_id."),
            VirtualAccountNumber: va,
            PaymentCode: Text("payment_code"),
            QrString: Text("qr_string"),
            RedirectUrl: redirect ?? Text("redirect_url"),
            ExpiresAt: expiry,
            QrImageUrl: qrImage);
    }
}

public sealed class PaymentGatewayException(string message) : Exception(message);
