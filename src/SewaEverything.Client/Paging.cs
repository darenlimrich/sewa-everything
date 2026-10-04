using SewaEverything.Contracts;

namespace SewaEverything.Client;

public static class Paging
{
    public static int? OutOfRange<T>(PagedResponse<T>? page) =>
        page is { Total: > 0 } p && p.Page > p.TotalPages ? p.TotalPages : null;
}
