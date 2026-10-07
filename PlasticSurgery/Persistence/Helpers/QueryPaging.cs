using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Responses.Billing;

namespace PlasticSurgery.Persistence.Helpers;

/// <summary>Paging and search helpers for the platform-admin list queries.</summary>
public static class QueryPaging
{
    public const int DefaultPageSize = 50;

    public static async Task<PagedResponse<T>> ToPagedAsync<T>(this IQueryable<T> query, int page, int pageSize = DefaultPageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResponse<T>(items, total, page, pageSize);
    }

    /// <summary>Case-insensitive "contains" pattern for EF.Functions.ILike, with % and _ escaped.</summary>
    public static string Like(string term) =>
        "%" + term.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
