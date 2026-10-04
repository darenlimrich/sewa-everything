using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Persistence.Configurations;

namespace SewaEverything.Infrastructure.Persistence;

public static class ItemQueries
{
    public const string SearchConfig = "indonesian";

    public static IQueryable<Item> WhereMatchesText(this IQueryable<Item> items, string text) =>
        items.Where(i => EF.Property<NpgsqlTsVector>(i, ItemConfiguration.SearchVectorProperty)
            .Matches(EF.Functions.WebSearchToTsQuery(SearchConfig, text)));

    public static IOrderedQueryable<Item> OrderByRelevance(this IQueryable<Item> items, string text) =>
        items.OrderByDescending(i =>
                EF.Property<NpgsqlTsVector>(i, ItemConfiguration.SearchVectorProperty)
                    .Rank(EF.Functions.WebSearchToTsQuery(SearchConfig, text)))
            .ThenByDescending(i => i.CreatedAt);

    public static IQueryable<Item> WherePubliclyVisible(this IQueryable<Item> items) =>
        items.Where(i => i.Status == ItemStatus.Active
                         && i.SuspendedAt == null
                         && i.ReviewStatus == ItemReviewStatus.Approved
                         && i.Seller!.IsVerified);

    public static IQueryable<Item> WhereFreeBetween(
        this IQueryable<Item> items, IQueryable<ItemBlockedRange> blocked, DateTime from, DateTime to) =>
        items.Where(i => !blocked.Any(r => r.ItemId == i.Id && r.StartsAt < to && r.EndsAt > from));
}
