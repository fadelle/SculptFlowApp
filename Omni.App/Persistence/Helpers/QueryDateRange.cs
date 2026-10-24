using System.Linq.Expressions;

namespace Omni.App.Persistence.Helpers;

/// <summary>The [from, to) date filter the reporting repositories share (Dashboard, Reports).</summary>
public static class QueryDateRange
{
    /// <summary>Filters to rows whose date falls in [from, to); either bound may be open (null). Bounds are compared in UTC
    /// (Npgsql timestamptz parameters must be UTC).</summary>
    public static IQueryable<T> InRange<T>(this IQueryable<T> query, Expression<Func<T, DateTimeOffset>> date, DateTimeOffset? from,
        DateTimeOffset? to)
    {
        var row = date.Parameters[0];
        if (from is not null)
        {
            query = query.Where(Expression.Lambda<Func<T, bool>>(
                Expression.GreaterThanOrEqual(date.Body, Expression.Constant(from.Value.ToUniversalTime())), row));
        }
        if (to is not null)
        {
            query = query.Where(Expression.Lambda<Func<T, bool>>(
                Expression.LessThan(date.Body, Expression.Constant(to.Value.ToUniversalTime())), row));
        }
        return query;
    }
}
