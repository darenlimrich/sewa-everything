using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SewaEverything.Contracts;

namespace SewaEverything.Client;

public sealed class SewaApi(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<(AuthResponse? Auth, bool ButuhKode, string? Error)> LoginAsync(
        LoginRequest request, CancellationToken ct = default) =>
        await ReadSignInAsync(await http.PostAsJsonAsync("auth/login", request, ct), ct);

    public async Task<(AuthResponse? Auth, bool ButuhKode, string? Error)> StaffLoginAsync(
        LoginRequest request, CancellationToken ct = default) =>
        await ReadSignInAsync(await http.PostAsJsonAsync("auth/staff/login", request, ct), ct);

    public async Task<(AuthResponse? Auth, string? Error)> RegisterAsync(
        RegisterRequest request, CancellationToken ct = default) =>
        await ReadAuthAsync(await http.PostAsJsonAsync("auth/register", request, ct), ct);

    public async Task<string?> ForgotPasswordAsync(
        ForgotPasswordRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("auth/forgot-password", request, ct);

        return response.IsSuccessStatusCode ? null : await ReadErrorMessageAsync(response, ct);
    }

    public async Task<(bool Berhasil, bool TautanMati, string? Error)> ResetPasswordAsync(
        ResetPasswordRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("auth/reset-password", request, ct);

        if (response.IsSuccessStatusCode)
        {
            return (true, false, null);
        }

        return (false,
            response.StatusCode == HttpStatusCode.Gone,
            await ReadErrorMessageAsync(response, ct));
    }

    public async Task<UserResponse?> GetMeAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("auth/me", ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserResponse>(ct);
    }

    public async Task<(UserResponse? User, string? Error)> UpdateMeAsync(
        UpdateProfileRequest request, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync("auth/me", request, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<UserResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<PulseResponse?> GetPulseAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await http.GetAsync("pulse", ct);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<PulseResponse>(ct)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<PaymentInstructionResponse?> GetPaymentAsync(
        Guid bookingId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"bookings/{bookingId}/payment", ct);

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<PaymentInstructionResponse>(ct)
            : null;
    }

    public async Task<string?> ChangePasswordAsync(
        ChangePasswordRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("auth/change-password", request, ct);

        return response.IsSuccessStatusCode ? null : await ReadErrorMessageAsync(response, ct);
    }

    public async Task<NotificationListResponse> GetNotificationsAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<NotificationListResponse>("notifications", ct)
                   ?? Empty();
        }
        catch
        {
            return Empty();
        }

        static NotificationListResponse Empty() =>
            new() { Items = [], UnreadCount = 0 };
    }

    public async Task MarkNotificationsSeenAsync(CancellationToken ct = default)
    {
        try { await http.PostAsync("notifications/seen", null, ct); }
        catch { }
    }

    public async Task<CartResponse> GetCartAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<CartResponse>("cart", ct)
                   ?? new CartResponse { Items = [], Count = 0 };
        }
        catch
        {
            return new CartResponse { Items = [], Count = 0 };
        }
    }

    public async Task<(CartItemResponse? Line, string? Error)> AddToCartAsync(
        AddCartItemRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("cart", request, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<CartItemResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<string?> RemoveFromCartAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"cart/{id}", ct);

        return response.IsSuccessStatusCode ? null : await ReadErrorMessageAsync(response, ct);
    }

    public async Task<IReadOnlyList<AddressResponse>> GetAddressesAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<IReadOnlyList<AddressResponse>>("addresses", ct) ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public async Task<(AddressResponse? Address, string? Error)> SaveAddressAsync(
        SaveAddressRequest request, Guid? id = null, CancellationToken ct = default)
    {
        var response = id is { } existing
            ? await http.PutAsJsonAsync($"addresses/{existing}", request, ct)
            : await http.PostAsJsonAsync("addresses", request, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<AddressResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<string?> MakeAddressDefaultAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"addresses/{id}/default", null, ct);

        return response.IsSuccessStatusCode ? null : await ReadErrorMessageAsync(response, ct);
    }

    public async Task<string?> RemoveAddressAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"addresses/{id}", ct);

        return response.IsSuccessStatusCode ? null : await ReadErrorMessageAsync(response, ct);
    }

    public async Task<(CheckoutQuoteResponse? Quote, string? Error)> QuoteCheckoutAsync(
        CheckoutRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("checkout/quote", request, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<CheckoutQuoteResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<(CheckoutResultResponse? Result, string? Error)> SubmitCheckoutAsync(
        CheckoutRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("checkout", request, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<CheckoutResultResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    private static async Task<(AuthResponse?, bool, string?)> ReadSignInAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadFromJsonAsync<AuthResponse>(ct), false, null);
        }

        string body;
        try { body = await response.Content.ReadAsStringAsync(ct); }
        catch { return (null, false, DefaultMessage(response.StatusCode)); }

        var butuhKode = false;

        try
        {
            butuhKode = JsonDocument.Parse(body).RootElement
                .TryGetProperty("totpRequired", out var tanda)
                && tanda.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
        }

        return (null, butuhKode, ReadErrorMessage(body, response.StatusCode));
    }

    private static async Task<(AuthResponse?, string?)> ReadAuthAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadFromJsonAsync<AuthResponse>(ct), null);
        }

        return (null, await ReadErrorMessageAsync(response, ct));
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string body;
        try { body = await response.Content.ReadAsStringAsync(ct); }
        catch { return DefaultMessage(response.StatusCode); }

        return ReadErrorMessage(body, response.StatusCode);
    }

    private static string ReadErrorMessage(string body, HttpStatusCode status)
    {
        try
        {
            var root = JsonDocument.Parse(body).RootElement;

            var hasFieldErrors = root.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Object
                && errors.EnumerateObject().Any();

            var detail = root.TryGetProperty("detail", out var d) ? d.GetString() : null;
            var title  = root.TryGetProperty("title", out var t) ? t.GetString() : null;

            if (!string.IsNullOrWhiteSpace(detail)) return detail!;
            if (hasFieldErrors) return "Periksa kembali isian yang ditandai.";
            if (!string.IsNullOrWhiteSpace(title)) return title!;

            return DefaultMessage(status);
        }
        catch (JsonException)
        {
            return DefaultMessage(status);
        }
    }

    private static string DefaultMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Email atau kata sandi salah.",
        HttpStatusCode.Forbidden    => "Anda tidak punya akses ke tindakan ini.",
        _                           => "Terjadi kesalahan. Coba lagi."
    };

    public async Task<PagedResponse<ItemSummaryResponse>?> SearchItemsAsync(
        string? q = null, string? category = null, string? sort = null,
        int page = 1, int pageSize = 20, decimal? minPrice = null, decimal? maxPrice = null,
        string? priceUnit = null, DateOnly? availableFrom = null, DateOnly? availableTo = null,
        Guid? sellerId = null, CancellationToken ct = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };

        if (!string.IsNullOrWhiteSpace(q)) query.Add($"q={Uri.EscapeDataString(q)}");
        if (!string.IsNullOrWhiteSpace(category)) query.Add($"category={Uri.EscapeDataString(category)}");
        if (sellerId is { } pemilik) query.Add($"sellerId={pemilik}");
        if (!string.IsNullOrWhiteSpace(sort)) query.Add($"sort={Uri.EscapeDataString(sort)}");
        if (minPrice is { } lo) query.Add($"minPrice={lo.ToString(CultureInfo.InvariantCulture)}");
        if (maxPrice is { } hi) query.Add($"maxPrice={hi.ToString(CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(priceUnit)) query.Add($"priceUnit={Uri.EscapeDataString(priceUnit)}");
        if (availableFrom is { } dari) query.Add($"availableFrom={Uri.EscapeDataString(Wib(dari))}");
        if (availableTo is { } sampai) query.Add($"availableTo={Uri.EscapeDataString(Wib(sampai))}");

        return await http.GetFromJsonAsync<PagedResponse<ItemSummaryResponse>>(
            "items?" + string.Join('&', query), ct);
    }

    private static string Wib(DateOnly date) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.FromHours(7))
            .ToString("o", CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<ItemCategoryResponse>> GetItemCategoriesAsync(
        CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<ItemCategoryResponse>>("items/categories", ct) ?? [];

    public async Task<ItemDetailResponse?> GetItemAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"items/{id}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ItemDetailResponse>(ct);
    }

    public async Task<(BookingResponse? Booking, string? Error)> CreateBookingAsync(
        CreateBookingRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("bookings", request, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<BookingResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public Task<PagedResponse<BookingResponse>?> GetBookingsAsync(
        string? status = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");

        return http.GetFromJsonAsync<PagedResponse<BookingResponse>>(
            "bookings?" + string.Join('&', query), ct);
    }

    public async Task<BookingResponse?> GetBookingAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"bookings/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BookingResponse>(ct);
    }

    public async Task<(BookingResponse? Booking, string? Error)> CancelBookingAsync(
        Guid id, string? reason = null, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"bookings/{id}/cancel",
            new CancelBookingRequest { Reason = reason }, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<BookingResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<(PaymentInstructionResponse? Instruction, string? Error)> PayBookingAsync(
        Guid bookingId, string channel, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"bookings/{bookingId}/pay",
            new PayBookingRequest { Channel = channel }, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<PaymentInstructionResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<BookingLedgerResponse?> GetLedgerAsync(Guid bookingId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"bookings/{bookingId}/ledger", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BookingLedgerResponse>(ct);
    }

    public async Task<(ReviewResponse? Review, string? Error)> CreateReviewAsync(
        Guid bookingId, CreateReviewRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"bookings/{bookingId}/reviews", request, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<ReviewResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<ReviewResponse?> GetBookingReviewAsync(Guid bookingId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"bookings/{bookingId}/review", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ReviewResponse>(ct);
    }

    public async Task<ItemReviewsResponse?> GetItemReviewsAsync(
        Guid itemId, int page = 1, int pageSize = 10, int? rating = null, CancellationToken ct = default)
    {
        var saring = rating is { } bintang ? $"&rating={bintang}" : string.Empty;
        var response = await http.GetAsync($"items/{itemId}/reviews?page={page}&pageSize={pageSize}{saring}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ItemReviewsResponse>(ct);
    }

    public Task<(BookingResponse? Booking, string? Error)> ApproveBookingAsync(
        Guid id, CancellationToken ct = default) =>
        PostTransitionAsync($"bookings/{id}/approve", ct);

    public Task<(BookingResponse? Booking, string? Error)> HandoverBookingAsync(
        Guid id, CancellationToken ct = default) =>
        PostTransitionAsync($"bookings/{id}/handover", ct);

    public Task<(BookingResponse? Booking, string? Error)> ReturnBookingAsync(
        Guid id, CancellationToken ct = default) =>
        PostTransitionAsync($"bookings/{id}/return", ct);

    public async Task<(BookingResponse? Booking, string? Error)> DisputeBookingAsync(
        Guid id, string reason, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"bookings/{id}/dispute",
            new RaiseDisputeRequest { Reason = reason }, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<BookingResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    private async Task<(BookingResponse? Booking, string? Error)> PostTransitionAsync(
        string path, CancellationToken ct)
    {
        var response = await http.PostAsync(path, null, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<BookingResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<IReadOnlyList<ItemSummaryResponse>> GetMyItemsAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<ItemSummaryResponse>>("items/mine", ct) ?? [];

    public async Task<(ItemDetailResponse? Item, string? Error)> CreateItemAsync(
        CreateItemRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("items", request, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<ItemDetailResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<(ItemDetailResponse? Item, string? Error)> UpdateItemAsync(
        Guid id, UpdateItemRequest request, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"items/{id}", request, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<ItemDetailResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<(UserResponse? User, string? Error)> UploadAvatarAsync(
        byte[] bytes, string fileName, string? contentType, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        form.Add(file, "file", fileName);

        var response = await http.PostAsync("auth/me/photo", form, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<UserResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<(UserResponse? User, string? Error)> DeleteAvatarAsync(
        CancellationToken ct = default)
    {
        var response = await http.DeleteAsync("auth/me/photo", ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<UserResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<(ItemPhotoResponse? Photo, string? Error)> UploadPhotoAsync(
        Guid itemId, byte[] bytes, string fileName, string? contentType, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        form.Add(file, "file", fileName);

        var response = await http.PostAsync($"items/{itemId}/photos", form, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<ItemPhotoResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<string?> DeletePhotoAsync(Guid itemId, Guid photoId, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"items/{itemId}/photos/{photoId}", ct);
        return response.IsSuccessStatusCode ? null : await ReadErrorMessageAsync(response, ct);
    }

    public async Task<IReadOnlyList<ItemBlackoutResponse>> GetBlackoutsAsync(
        Guid itemId, CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<ItemBlackoutResponse>>($"items/{itemId}/blackouts", ct) ?? [];

    public async Task<(ItemBlackoutResponse? Blackout, string? Error)> CreateBlackoutAsync(
        Guid itemId, CreateBlackoutRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"items/{itemId}/blackouts", request, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<ItemBlackoutResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<string?> DeleteBlackoutAsync(Guid itemId, Guid blackoutId, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"items/{itemId}/blackouts/{blackoutId}", ct);
        return response.IsSuccessStatusCode ? null : await ReadErrorMessageAsync(response, ct);
    }

    public async Task<IReadOnlyList<PayoutAccountResponse>> GetPayoutAccountsAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<PayoutAccountResponse>>("payout-accounts", ct) ?? [];

    public async Task<(PayoutAccountResponse? Account, string? Error)> CreatePayoutAccountAsync(
        CreatePayoutAccountRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("payout-accounts", request, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<PayoutAccountResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<(PayoutAccountResponse? Account, string? Error)> MakeDefaultPayoutAccountAsync(
        Guid id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"payout-accounts/{id}/default", null, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<PayoutAccountResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<string?> DeletePayoutAccountAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"payout-accounts/{id}", ct);
        return response.IsSuccessStatusCode ? null : await ReadErrorMessageAsync(response, ct);
    }

    public async Task<IReadOnlyList<PendingSellerResponse>> GetPendingSellersAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<PendingSellerResponse>>("admin/sellers/pending", ct) ?? [];

    public async Task<IReadOnlyList<AdminSellerResponse>> GetSellersAsync(
        bool? verified = null, CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<AdminSellerResponse>>(
            verified is { } v ? $"admin/sellers?verified={(v ? "true" : "false")}" : "admin/sellers", ct) ?? [];

    public async Task<(UserResponse? Seller, string? Error)> VerifySellerAsync(
        Guid id, bool approve, string? note = null, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"admin/sellers/{id}/verify",
            new VerifySellerRequest { Approve = approve, Note = note }, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<UserResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public Task<PagedResponse<ModeratedItemResponse>?> GetModeratedItemsAsync(
        string? q = null, bool? suspended = null, string? review = null, int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };

        if (!string.IsNullOrWhiteSpace(q)) query.Add($"q={Uri.EscapeDataString(q)}");
        if (suspended is { } s) query.Add($"suspended={(s ? "true" : "false")}");
        if (review is not null) query.Add($"review={review}");

        return http.GetFromJsonAsync<PagedResponse<ModeratedItemResponse>>(
            "admin/items?" + string.Join('&', query), ct);
    }

    public async Task<(ModeratedItemResponse? Item, string? Error)> SuspendItemAsync(
        Guid id, string reason, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"admin/items/{id}/suspend",
            new SuspendItemRequest { Reason = reason }, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<ModeratedItemResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<(ModeratedItemResponse? Item, string? Error)> UnsuspendItemAsync(
        Guid id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"admin/items/{id}/unsuspend", null, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<ModeratedItemResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<(ModeratedItemResponse? Item, string? Error)> ApproveItemAsync(
        Guid id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"admin/items/{id}/approve", null, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<ModeratedItemResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<(ModeratedItemResponse? Item, string? Error)> RejectItemAsync(
        Guid id, string reason, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"admin/items/{id}/reject",
            new RejectItemRequest { Reason = reason }, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<ModeratedItemResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<IReadOnlyList<DisputeResponse>> GetDisputesAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<DisputeResponse>>("admin/disputes", ct) ?? [];

    public async Task<(DisputeResponse? Dispute, string? Error)> ResolveDisputeAsync(
        Guid id, decimal depositDeduction, string resolution, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"admin/disputes/{id}/resolve",
            new ResolveDisputeRequest { DepositDeduction = depositDeduction, Resolution = resolution }, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<DisputeResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<IReadOnlyList<PendingPayoutResponse>> GetPendingPayoutsAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<PendingPayoutResponse>>("admin/payouts/pending", ct) ?? [];

    public async Task<(PendingPayoutResponse? Payout, string? Error)> SettlePayoutAsync(
        Guid id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"admin/payouts/{id}/settle", null, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<PendingPayoutResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public Task<PlatformSettingsResponse?> GetPlatformSettingsAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<PlatformSettingsResponse>("owner/settings", ct);

    public async Task<(PlatformSettingsResponse? Settings, string? Error)> UpdatePlatformSettingsAsync(
        UpdatePlatformSettingsRequest request, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync("owner/settings", request, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<PlatformSettingsResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public Task<RevenueSummaryResponse?> GetRevenueAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<RevenueSummaryResponse>("owner/revenue", ct);

    public async Task<PagedResponse<OwnerTransactionResponse>?> GetTransactionsAsync(
        string? kind = null, string? status = null, int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };

        if (!string.IsNullOrWhiteSpace(kind)) query.Add($"kind={Uri.EscapeDataString(kind)}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");

        return await http.GetFromJsonAsync<PagedResponse<OwnerTransactionResponse>>(
            $"owner/transactions?{string.Join('&', query)}", ct);
    }

    public async Task<IReadOnlyList<AdminAccountResponse>> GetAdminsAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<AdminAccountResponse>>("owner/admins", ct) ?? [];

    public async Task<(AdminAccountResponse? Admin, string? Error)> CreateAdminAsync(
        CreateAdminRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("owner/admins", request, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<AdminAccountResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<(AdminAccountResponse? Admin, string? Error)> SetAdminAccessAsync(
        Guid id, bool active, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"owner/admins/{id}/access",
            new SetAdminAccessRequest { Active = active }, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<AdminAccountResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<(AdminAccountResponse? Admin, string? Error)> ResetAdminTotpAsync(
        Guid id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"owner/admins/{id}/2fa/reset", null, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<AdminAccountResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public Task<TotpStatusResponse?> GetTotpStatusAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<TotpStatusResponse>("auth/2fa", ct);

    public async Task<(TotpSetupResponse? Setup, string? Error)> StartTotpSetupAsync(
        TotpSetupRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("auth/2fa/setup", request, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<TotpSetupResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public async Task<(IReadOnlyList<string>? RecoveryCodes, string? Error)> EnableTotpAsync(
        TotpCodeRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("auth/2fa/enable", request, ct);

        if (!response.IsSuccessStatusCode)
        {
            return (null, await ReadErrorMessageAsync(response, ct));
        }

        var hasil = await response.Content.ReadFromJsonAsync<TotpEnableResponse>(ct);

        return (hasil?.RecoveryCodes ?? [], null);
    }

    public async Task<string?> DisableTotpAsync(
        TotpDisableRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("auth/2fa/disable", request, ct);

        return response.IsSuccessStatusCode ? null : await ReadErrorMessageAsync(response, ct);
    }

    public Task<PaymentGatewayResponse?> GetPaymentGatewayAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<PaymentGatewayResponse>("owner/payment-gateway", ct);

    public async Task<(PaymentGatewayResponse? Gateway, string? Error)> UpdatePaymentGatewayAsync(
        UpdatePaymentGatewayRequest request, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync("owner/payment-gateway", request, ct);

        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<PaymentGatewayResponse>(ct), null)
            : (null, await ReadErrorMessageAsync(response, ct));
    }

    public string? PhotoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
            ? absolute.ToString()
            : new Uri(http.BaseAddress!, url).ToString();
    }
}
