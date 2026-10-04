using System.Net;
using System.Net.Http.Json;
using SewaEverything.Contracts;
using SewaEverything.Domain;

namespace SewaEverything.Tests;

[Collection(ApiCollection.Name)]
public sealed class DeliveryTests(ApiFactory api)
{
    private static readonly DateTimeOffset Besok = DateTimeOffset.UtcNow.Date.AddDays(1);

    private static SaveAddressRequest Alamat(string label = "Rumah", bool utama = false) => new()
    {
        Label         = label,
        RecipientName = "Rizky Pratama",
        Phone         = "081200011122",
        FullAddress   = "Jalan Mawar No. 10, Bandung, Jawa Barat 40123",
        IsDefault     = utama
    };

    private async Task<Guid> TambahAlamatAsync(HttpClient client, string label = "Rumah", bool utama = false)
    {
        var response = await client.PostAsJsonAsync("/addresses", Alamat(label, utama));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AddressResponse>())!.Id;
    }

    private async Task<Guid> KeKeranjangAsync(HttpClient renter, Guid itemId, int mulaiHari = 1)
    {
        var response = await renter.PostAsJsonAsync("/cart", new AddCartItemRequest
        {
            ItemId  = itemId,
            StartAt = Besok.AddDays(mulaiHari).UtcDateTime,
            EndAt   = Besok.AddDays(mulaiHari + 2).UtcDateTime
        });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CartItemResponse>())!.Id;
    }

    private static CheckoutRequest Ajukan(Guid cartItemId, string metode, Guid? addressId = null) => new()
    {
        Lines     = [new CheckoutLineRequest { CartItemId = cartItemId, DeliveryMethod = metode }],
        AddressId = addressId
    };

    [Fact]
    public async Task Alamat_pertama_otomatis_jadi_utama()
    {
        var renter = await api.RenterAsync();
        await TambahAlamatAsync(renter.Client);

        var daftar = await renter.Client.GetFromJsonAsync<List<AddressResponse>>("/addresses");

        Assert.Single(daftar!);
        Assert.True(daftar![0].IsDefault);
    }

    [Fact]
    public async Task Alamat_utama_hanya_satu_walau_ditambah_berkali_kali()
    {
        var renter = await api.RenterAsync();
        await TambahAlamatAsync(renter.Client, "Rumah", utama: true);
        await TambahAlamatAsync(renter.Client, "Kantor", utama: true);
        await TambahAlamatAsync(renter.Client, "Kos", utama: true);

        var daftar = await renter.Client.GetFromJsonAsync<List<AddressResponse>>("/addresses");

        Assert.Equal(3, daftar!.Count);
        Assert.Single(daftar!, a => a.IsDefault);
        Assert.Equal("Kos", daftar!.Single(a => a.IsDefault).Label);
    }

    [Fact]
    public async Task Alamat_orang_lain_tidak_terlihat_dan_tidak_dapat_diubah()
    {
        var a = await api.RenterAsync();
        var b = await api.RenterAsync();

        var punyaA = await TambahAlamatAsync(a.Client);

        var daftarB = await b.Client.GetFromJsonAsync<List<AddressResponse>>("/addresses");
        Assert.Empty(daftarB!);

        var ubah = await b.Client.PutAsJsonAsync($"/addresses/{punyaA}", Alamat("Dibajak"));
        Assert.Equal(HttpStatusCode.NotFound, ubah.StatusCode);

        var hapus = await b.Client.DeleteAsync($"/addresses/{punyaA}");
        Assert.Equal(HttpStatusCode.NotFound, hapus.StatusCode);
    }

    [Fact]
    public async Task Menghapus_alamat_utama_mengangkat_penggantinya()
    {
        var renter = await api.RenterAsync();
        var utama  = await TambahAlamatAsync(renter.Client, "Rumah", utama: true);
        await TambahAlamatAsync(renter.Client, "Kantor");

        (await renter.Client.DeleteAsync($"/addresses/{utama}")).EnsureSuccessStatusCode();

        var daftar = await renter.Client.GetFromJsonAsync<List<AddressResponse>>("/addresses");

        Assert.Single(daftar!);
        Assert.True(daftar![0].IsDefault);
        Assert.Equal("Kantor", daftar![0].Label);
    }

    [Fact]
    public async Task Ambil_sendiri_tidak_menambah_biaya_dan_tidak_menuntut_alamat()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client, price: 100_000m, deposit: 500_000m, deliveryFee: 25_000m);
        var baris  = await KeKeranjangAsync(renter.Client, item.Id);

        var response = await renter.Client.PostAsJsonAsync("/checkout/quote",
            Ajukan(baris, DeliveryMethodValues.Pickup));
        response.EnsureSuccessStatusCode();

        var quote = (await response.Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!;

        Assert.Equal(0m, quote.TotalDelivery);
        Assert.False(quote.AddressRequired);
        Assert.True(quote.CanSubmit);
        Assert.Equal(200_000m + 500_000m, quote.GrandTotal);
    }

    [Fact]
    public async Task Diantar_menambahkan_ongkir_ke_total_penyewa()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client, price: 100_000m, deposit: 500_000m, deliveryFee: 25_000m);
        var baris  = await KeKeranjangAsync(renter.Client, item.Id);
        var alamat = await TambahAlamatAsync(renter.Client, utama: true);

        var response = await renter.Client.PostAsJsonAsync("/checkout/quote",
            Ajukan(baris, DeliveryMethodValues.Delivery, alamat));
        response.EnsureSuccessStatusCode();

        var quote = (await response.Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!;

        Assert.Equal(25_000m, quote.TotalDelivery);
        Assert.True(quote.AddressRequired);
        Assert.Equal(200_000m + 500_000m + 25_000m, quote.GrandTotal);
    }

    [Fact]
    public async Task Ongkir_TIDAK_kena_komisi_platform()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client, price: 100_000m, deposit: 0m, deliveryFee: 25_000m);
        var baris  = await KeKeranjangAsync(renter.Client, item.Id);
        var alamat = await TambahAlamatAsync(renter.Client, utama: true);

        var response = await renter.Client.PostAsJsonAsync("/checkout",
            Ajukan(baris, DeliveryMethodValues.Delivery, alamat));
        response.EnsureSuccessStatusCode();

        var hasil   = (await response.Content.ReadFromJsonAsync<CheckoutResultResponse>())!;
        var booking = hasil.Bookings.Single();

        var komisi = booking.PlatformFeeAmount;

        Assert.Equal(200_000m, booking.TotalRent);
        Assert.Equal(25_000m, booking.DeliveryFee);
        Assert.Equal(200_000m - komisi + 25_000m, booking.SellerGross);
        Assert.Equal(200_000m + 25_000m, booking.RenterTotal);
    }

    [Fact]
    public async Task Barang_yang_tidak_melayani_antar_menolak_pengantaran()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client, deliveryFee: null);
        var baris  = await KeKeranjangAsync(renter.Client, item.Id);
        var alamat = await TambahAlamatAsync(renter.Client, utama: true);

        var quoteResponse = await renter.Client.PostAsJsonAsync("/checkout/quote",
            Ajukan(baris, DeliveryMethodValues.Delivery, alamat));
        quoteResponse.EnsureSuccessStatusCode();

        var quote = (await quoteResponse.Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!;

        Assert.False(quote.CanSubmit);
        Assert.False(quote.Lines[0].DeliveryAvailable);

        var submit = await renter.Client.PostAsJsonAsync("/checkout",
            Ajukan(baris, DeliveryMethodValues.Delivery, alamat));

        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
    }

    [Fact]
    public async Task Antar_gratis_berbeda_dari_tidak_melayani_antar()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client, deliveryFee: 0m);
        var baris  = await KeKeranjangAsync(renter.Client, item.Id);
        var alamat = await TambahAlamatAsync(renter.Client, utama: true);

        var response = await renter.Client.PostAsJsonAsync("/checkout/quote",
            Ajukan(baris, DeliveryMethodValues.Delivery, alamat));
        response.EnsureSuccessStatusCode();

        var quote = (await response.Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!;

        Assert.True(quote.Lines[0].DeliveryAvailable);
        Assert.True(quote.CanSubmit);
        Assert.Equal(0m, quote.TotalDelivery);
    }

    [Fact]
    public async Task Diantar_tanpa_alamat_tersimpan_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client, deliveryFee: 25_000m);
        var baris  = await KeKeranjangAsync(renter.Client, item.Id);

        var response = await renter.Client.PostAsJsonAsync("/checkout",
            Ajukan(baris, DeliveryMethodValues.Delivery));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Alamat_ikut_tersalin_ke_sewa_supaya_pemilik_tahu_tujuannya()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client, deliveryFee: 25_000m);
        var baris  = await KeKeranjangAsync(renter.Client, item.Id);
        var alamat = await TambahAlamatAsync(renter.Client, utama: true);

        var response = await renter.Client.PostAsJsonAsync("/checkout",
            Ajukan(baris, DeliveryMethodValues.Delivery, alamat));
        response.EnsureSuccessStatusCode();

        var booking = (await response.Content.ReadFromJsonAsync<CheckoutResultResponse>())!.Bookings.Single();

        Assert.Equal(DeliveryMethodValues.Delivery, booking.DeliveryMethod);
        Assert.Equal("Rizky Pratama", booking.DeliveryRecipient);
        Assert.Equal("081200011122", booking.DeliveryPhone);
        Assert.Contains("Jalan Mawar", booking.DeliveryAddress);

        (await renter.Client.DeleteAsync($"/addresses/{alamat}")).EnsureSuccessStatusCode();

        var lagi = await renter.Client.GetFromJsonAsync<BookingResponse>($"/bookings/{booking.Id}");

        Assert.Contains("Jalan Mawar", lagi!.DeliveryAddress);
    }

    [Fact]
    public async Task Checkout_beberapa_barang_melahirkan_satu_sewa_per_barang()
    {
        var seller  = await api.VerifiedSellerAsync();
        var seller2 = await api.VerifiedSellerAsync();
        var renter  = await api.RenterAsync();

        var itemA = await api.CreateItemAsync(seller.Client,  title: "Kamera A", deliveryFee: 25_000m);
        var itemB = await api.CreateItemAsync(seller2.Client, title: "Tripod B", deliveryFee: null);

        var barisA = await KeKeranjangAsync(renter.Client, itemA.Id, mulaiHari: 1);
        var barisB = await KeKeranjangAsync(renter.Client, itemB.Id, mulaiHari: 8);
        var alamat = await TambahAlamatAsync(renter.Client, utama: true);

        var response = await renter.Client.PostAsJsonAsync("/checkout", new CheckoutRequest
        {
            Lines =
            [
                new CheckoutLineRequest { CartItemId = barisA, DeliveryMethod = DeliveryMethodValues.Delivery },
                new CheckoutLineRequest { CartItemId = barisB, DeliveryMethod = DeliveryMethodValues.Pickup }
            ],
            AddressId = alamat
        });
        response.EnsureSuccessStatusCode();

        var hasil = (await response.Content.ReadFromJsonAsync<CheckoutResultResponse>())!;

        Assert.Equal(2, hasil.Count);
        Assert.Single(hasil.Bookings, b => b.DeliveryMethod == DeliveryMethodValues.Delivery && b.DeliveryFee == 25_000m);
        Assert.Single(hasil.Bookings, b => b.DeliveryMethod == DeliveryMethodValues.Pickup && b.DeliveryFee == 0m);

        var keranjang = await renter.Client.GetFromJsonAsync<CartResponse>("/cart");
        Assert.Equal(0, keranjang!.Count);
    }

    [Fact]
    public async Task Satu_slot_bentrok_membatalkan_SELURUH_checkout()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var lain   = await api.RenterAsync();

        var itemA = await api.CreateItemAsync(seller.Client, title: "Aman");
        var itemB = await api.CreateItemAsync(seller.Client, title: "Bentrok");

        var barisA = await KeKeranjangAsync(renter.Client, itemA.Id, mulaiHari: 1);
        var barisB = await KeKeranjangAsync(renter.Client, itemB.Id, mulaiHari: 1);

        await api.CreateBookingAsync(lain.Client, itemB.Id,
            Besok.AddDays(1), Besok.AddDays(3));

        var response = await renter.Client.PostAsJsonAsync("/checkout", new CheckoutRequest
        {
            Lines =
            [
                new CheckoutLineRequest { CartItemId = barisA, DeliveryMethod = DeliveryMethodValues.Pickup },
                new CheckoutLineRequest { CartItemId = barisB, DeliveryMethod = DeliveryMethodValues.Pickup }
            ]
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var sewa = await renter.Client.GetFromJsonAsync<PagedResponse<BookingResponse>>("/bookings");
        Assert.Equal(0, sewa!.Total);

        var keranjang = await renter.Client.GetFromJsonAsync<CartResponse>("/cart");
        Assert.Equal(2, keranjang!.Count);
    }

    [Fact]
    public async Task Baris_keranjang_orang_lain_tidak_dapat_di_checkout()
    {
        var seller = await api.VerifiedSellerAsync();
        var a      = await api.RenterAsync();
        var b      = await api.RenterAsync();

        var item  = await api.CreateItemAsync(seller.Client);
        var baris = await KeKeranjangAsync(a.Client, item.Id);

        var response = await b.Client.PostAsJsonAsync("/checkout",
            Ajukan(baris, DeliveryMethodValues.Pickup));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ongkir_lahir_sebagai_baris_buku_besar_tersendiri()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client, price: 100_000m, deposit: 0m, deliveryFee: 25_000m);
        var baris  = await KeKeranjangAsync(renter.Client, item.Id);
        var alamat = await TambahAlamatAsync(renter.Client, utama: true);

        var checkout = await renter.Client.PostAsJsonAsync("/checkout",
            Ajukan(baris, DeliveryMethodValues.Delivery, alamat));
        checkout.EnsureSuccessStatusCode();

        var booking = (await checkout.Content.ReadFromJsonAsync<CheckoutResultResponse>())!.Bookings.Single();

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null)).EnsureSuccessStatusCode();

        var bayar = await renter.Client.PostAsJsonAsync($"/bookings/{booking.Id}/pay",
            new PayBookingRequest { Channel = PaymentChannels.Gopay });
        bayar.EnsureSuccessStatusCode();

        var ledger = await renter.Client.GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{booking.Id}/ledger");

        Assert.Contains(ledger!.Entries, e => e.Kind == "delivery_charge" && e.Amount == 25_000m);
        Assert.Equal(225_000m, ledger.AmountDue);
    }

    [Fact]
    public async Task Ongkir_ikut_cair_ke_pemilik_saat_sewa_selesai()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client, price: 100_000m, deposit: 0m, deliveryFee: 25_000m);
        var baris  = await KeKeranjangAsync(renter.Client, item.Id);
        var alamat = await TambahAlamatAsync(renter.Client, utama: true);

        var checkout = await renter.Client.PostAsJsonAsync("/checkout",
            Ajukan(baris, DeliveryMethodValues.Delivery, alamat));
        checkout.EnsureSuccessStatusCode();

        var booking = (await checkout.Content.ReadFromJsonAsync<CheckoutResultResponse>())!.Bookings.Single();

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null)).EnsureSuccessStatusCode();
        await api.SettleAsync(renter.Client, booking.Id);

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/handover", null)).EnsureSuccessStatusCode();
        (await seller.Client.PostAsync($"/bookings/{booking.Id}/return", null)).EnsureSuccessStatusCode();

        var entries = await api.LedgerEntriesAsync(seller.Client, booking.Id);

        var payout = entries.Single(e => e.Kind == "seller_payout");
        var komisi = entries.Single(e => e.Kind == "platform_fee").Amount;

        Assert.Equal(200_000m - komisi + 25_000m, payout.Amount);

        var masuk  = entries.Where(e => e.Direction == "in").Sum(e => e.Amount);
        var keluar = entries.Where(e => e.Direction == "out").Sum(e => e.Amount);

        Assert.Equal(komisi, masuk - keluar);
    }

    [Fact]
    public async Task Cara_pengiriman_karangan_ditolak()
    {
        var seller = await api.VerifiedSellerAsync();
        var renter = await api.RenterAsync();
        var item   = await api.CreateItemAsync(seller.Client);
        var baris  = await KeKeranjangAsync(renter.Client, item.Id);

        var response = await renter.Client.PostAsJsonAsync("/checkout",
            Ajukan(baris, "drone-udara"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bukan_penyewa_tidak_dapat_menyentuh_checkout()
    {
        var seller = await api.VerifiedSellerAsync();

        var response = await seller.Client.PostAsJsonAsync("/checkout",
            Ajukan(Guid.NewGuid(), DeliveryMethodValues.Pickup));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
