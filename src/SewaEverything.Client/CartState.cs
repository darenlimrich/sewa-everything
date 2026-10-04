using SewaEverything.Contracts;

namespace SewaEverything.Client;

public sealed class CartState(SewaApi api)
{
    private IReadOnlyList<CartItemResponse> items = [];

    public IReadOnlyList<CartItemResponse> Items => items;

    public int Count => items.Count;

    public bool Loaded { get; private set; }

    public event Action? Changed;

    public async Task RefreshAsync(string? role, CancellationToken ct = default)
    {
        if (role != "renter")
        {
            items = [];
            Loaded = true;
            Changed?.Invoke();
            return;
        }

        var cart = await api.GetCartAsync(ct);
        items = cart.Items;
        Loaded = true;
        Changed?.Invoke();
    }

    public void Clear()
    {
        items = [];
        Loaded = false;
        Changed?.Invoke();
    }
}
