namespace Subscrio.Core.Infrastructure.Repositories;

/// <summary>
/// Shared Skip/Take application for EF list queries.
/// Always apply after OrderBy.
/// </summary>
public static class QueryablePaging
{
    public static IQueryable<T> ApplyPaging<T>(this IQueryable<T> query, int offset, int limit)
    {
        if (offset > 0)
        {
            query = query.Skip(offset);
        }

        if (limit > 0)
        {
            query = query.Take(limit);
        }

        return query;
    }

    public static IQueryable<T> ApplyPaging<T>(this IQueryable<T> query, int? offset, int? limit) =>
        ApplyPaging(query, offset ?? 0, limit ?? 0);
}
