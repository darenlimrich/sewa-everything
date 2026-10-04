using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class BackendRegressionTests(ApiFactory api)
{
    private static SaveAddressRequest Address(string label = "Rumah") => new()
    {
        Label = label,
        RecipientName = "Penyewa Uji",
        Phone = "081234567890",
        FullAddress = "Jalan Mawar nomor 10, Bandung",
        IsDefault = true
    };

    private async Task<(ApiFactory.UserContext Seller, ApiFactory.UserContext Renter,
        BookingResponse Booking)> ConfirmedAsync()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);
        var from = DateTimeOffset.UtcNow.AddDays(30);
        var booking = await api.CreateBookingAsync(renter.Client, item.Id, from, from.AddDays(2));
        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null)).EnsureSuccessStatusCode();
        return (seller, renter, booking);
    }

    [Fact]
    public async Task Memilih_alamat_utama_yang_sama_tetap_menyimpan_default()
    {
        var renter = await api.RenterAsync();
        var created = await renter.Client.PostAsJsonAsync("/addresses", Address());
        created.EnsureSuccessStatusCode();
        var address = (await created.Content.ReadFromJsonAsync<AddressResponse>())!;

        (await renter.Client.PostAsync($"/addresses/{address.Id}/default", null)).EnsureSuccessStatusCode();
        var first = await renter.Client.GetFromJsonAsync<List<AddressResponse>>("/addresses");
        Assert.True(Assert.Single(first!).IsDefault);
        (await renter.Client.PostAsync($"/addresses/{address.Id}/default", null)).EnsureSuccessStatusCode();

        var saved = await renter.Client.GetFromJsonAsync<List<AddressResponse>>("/addresses");
        Assert.True(Assert.Single(saved!).IsDefault);
    }

    [Fact]
    public async Task Alamat_utama_paralel_disimpan_tanpa_gagal_constraint()
    {
        var renter = await api.RenterAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(i => renter.Client.PostAsJsonAsync("/addresses", Address($"Alamat {i}"))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var saved = await renter.Client.GetFromJsonAsync<List<AddressResponse>>("/addresses");
        Assert.Equal(5, saved!.Count);
        Assert.Single(saved, a => a.IsDefault);
    }

    [Fact]
    public async Task Rekening_duplikat_tidak_menghapus_default_yang_sudah_ada()
    {
        var renter = await api.RenterAsync();
        var request = new CreatePayoutAccountRequest
        {
            Kind = PayoutAccountKinds.Bank,
            ProviderCode = "BCA",
            AccountNumber = "1234567890",
            AccountHolder = "Penyewa Uji",
            IsDefault = true
        };
        (await renter.Client.PostAsJsonAsync("/payout-accounts", request)).EnsureSuccessStatusCode();

        var duplicate = await renter.Client.PostAsJsonAsync("/payout-accounts", request);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var saved = await renter.Client.GetFromJsonAsync<List<PayoutAccountResponse>>("/payout-accounts");
        Assert.True(Assert.Single(saved!).IsDefault);
    }

    [Fact]
    public async Task Keranjang_yang_tanggalnya_sudah_lewat_tidak_tersedia()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client);
        var from = DateTime.UtcNow.AddDays(2);
        (await renter.Client.PostAsJsonAsync("/cart", new AddCartItemRequest
        {
            ItemId = item.Id, StartAt = from, EndAt = from.AddDays(1)
        })).EnsureSuccessStatusCode();

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();
            await db.CartItems.Where(c => c.RenterId == renter.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.StartAt, from.AddDays(-4))
                    .SetProperty(c => c.EndAt, from.AddDays(-3)));
        }

        var cart = await renter.Client.GetFromJsonAsync<CartResponse>("/cart");
        Assert.False(Assert.Single(cart!.Items).Available);
    }

    [Fact]
    public async Task Menambahkan_slot_terpakai_ke_keranjang_langsung_mengembalikan_tidak_tersedia()
    {
        var scenario = await ConfirmedAsync();
        var renter = await api.RenterAsync();
        var response = await renter.Client.PostAsJsonAsync("/cart", new AddCartItemRequest
        {
            ItemId = scenario.Booking.ItemId,
            StartAt = scenario.Booking.StartsAt,
            EndAt = scenario.Booking.EndsAt
        });

        response.EnsureSuccessStatusCode();
        Assert.False((await response.Content.ReadFromJsonAsync<CartItemResponse>())!.Available);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("123")]
    [InlineData("\"payload\"")]
    public async Task Webhook_json_bukan_object_ditolak_400(string payload)
    {
        var response = await api.SendWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Nominal_webhook_sah_yang_berbeda_dari_tagihan_tidak_melunasi()
    {
        var s = await ConfirmedAsync();
        var instruction = await api.PaySuccessfullyAsync(s.Renter.Client, s.Booking.Id);
        var rejected = await api.SendWebhookAsync(ApiFactory.MidtransNotification(
            instruction.OrderId, instruction.Amount - 1m, "settlement"));

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var ledger = await s.Renter.Client.GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");
        Assert.Equal(0m, ledger!.AmountSettled);

        (await api.SendWebhookAsync(ApiFactory.MidtransNotification(
            instruction.OrderId, instruction.Amount, "settlement"))).EnsureSuccessStatusCode();
        ledger = await s.Renter.Client.GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");
        Assert.True(ledger!.IsSettled);
    }

    [Fact]
    public async Task Settlement_untuk_tagihan_gagal_tidak_melepas_hold_booking_belum_lunas()
    {
        var s = await ConfirmedAsync();
        var instruction = await api.PaySuccessfullyAsync(s.Renter.Client, s.Booking.Id);
        (await api.SendWebhookAsync(ApiFactory.MidtransNotification(
            instruction.OrderId, instruction.Amount, "expire"))).EnsureSuccessStatusCode();
        (await api.SendWebhookAsync(ApiFactory.MidtransNotification(
            instruction.OrderId, instruction.Amount, "settlement"))).EnsureSuccessStatusCode();

        var ledger = await s.Renter.Client.GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{s.Booking.Id}/ledger");
        var booking = await s.Renter.Client.GetFromJsonAsync<BookingResponse>($"/bookings/{s.Booking.Id}");
        Assert.False(ledger!.IsSettled);
        Assert.NotNull(booking!.HoldExpiresAt);
    }

    [Fact]
    public async Task Pay_setelah_tenggat_tidak_membuat_tagihan_baru()
    {
        var s = await ConfirmedAsync();
        await api.ExpireHoldAsync(s.Booking.Id);

        var response = await api.PayAsync(s.Renter.Client, s.Booking.Id, PaymentChannels.Gopay);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id));
        Assert.DoesNotContain(api.Gateway.Charges, c => c.OrderId.Contains(s.Booking.Id.ToString("N")));
    }

    [Fact]
    public async Task Pay_paralel_memakai_satu_tagihan_dan_satu_panggilan_gateway()
    {
        var s = await ConfirmedAsync();
        HttpResponseMessage[] responses;
        api.Gateway.ChargeDelay = TimeSpan.FromMilliseconds(150);
        try
        {
            responses = await Task.WhenAll(Enumerable.Range(0, 5)
                .Select(_ => api.PayAsync(s.Renter.Client, s.Booking.Id, PaymentChannels.Gopay)));
        }
        finally
        {
            api.Gateway.ChargeDelay = TimeSpan.Zero;
        }

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var instructions = await Task.WhenAll(responses
            .Select(r => r.Content.ReadFromJsonAsync<PaymentInstructionResponse>()));
        Assert.Single(instructions.Select(i => i!.OrderId).Distinct());
        Assert.Single(api.Gateway.Charges, c => c.OrderId.Contains(s.Booking.Id.ToString("N")));
        Assert.Equal(2, (await api.LedgerEntriesAsync(s.Renter.Client, s.Booking.Id)).Count);
    }

    [Theory]
    [InlineData("/checkout")]
    [InlineData("/checkout/quote")]
    public async Task Checkout_dengan_baris_null_ditolak_400(string route)
    {
        var renter = await api.RenterAsync();
        var response = await renter.Client.PostAsync(route,
            new StringContent("{\"lines\":[null]}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ringkasan_refund_booking_batal_mencakup_ongkir()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item = await api.CreateItemAsync(seller.Client, price: 100_000m, deposit: 50_000m, deliveryFee: 25_000m);
        (await renter.Client.PostAsJsonAsync("/addresses", Address())).EnsureSuccessStatusCode();
        var from = DateTime.UtcNow.AddDays(30);
        var added = await renter.Client.PostAsJsonAsync("/cart", new AddCartItemRequest
        {
            ItemId = item.Id, StartAt = from, EndAt = from.AddDays(2)
        });
        added.EnsureSuccessStatusCode();
        var row = (await added.Content.ReadFromJsonAsync<CartItemResponse>())!;
        var checkout = await renter.Client.PostAsJsonAsync("/checkout", new CheckoutRequest
        {
            Lines = [new CheckoutLineRequest { CartItemId = row.Id, DeliveryMethod = DeliveryMethodValues.Delivery }]
        });
        checkout.EnsureSuccessStatusCode();
        var booking = (await checkout.Content.ReadFromJsonAsync<CheckoutResultResponse>())!.Bookings.Single();
        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null)).EnsureSuccessStatusCode();
        await api.SettleAsync(renter.Client, booking.Id);
        (await renter.Client.PostAsJsonAsync($"/bookings/{booking.Id}/cancel", new CancelBookingRequest()))
            .EnsureSuccessStatusCode();

        var ledger = await renter.Client.GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{booking.Id}/ledger");
        Assert.Equal(275_000m, ledger!.AmountRefundPending);
        Assert.Equal(ledger.AmountSettled, ledger.AmountRefundPending);
    }
}
